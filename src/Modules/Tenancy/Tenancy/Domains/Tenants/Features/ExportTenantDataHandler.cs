using System.IO.Compression;
using System.Text.Json;
using UserDirectory.Contracts.Queries;
using Tenancy.Services;

namespace Tenancy.Domains.Tenants.Features;

/// <summary>
/// Builds the tenant's data export into a temporary file (deleted on dispose) and returns it for streaming.
/// <list type="bullet">
/// <item><c>manifest.json</c> - tenant, time, exporting user, format version, tables.</item>
/// <item><c>tenant/*.json</c> - profile, branches, roles, members (+ e-mail/name), invitations (no token hashes),
/// audit trail (streamed).</item>
/// <item><c>data/{schema}.{table}.csv</c> - every table of every tenant-data context (shared OR dedicated database),
/// via <see cref="TenantDataExporter"/> in the caller's tenant scope (EF filters + RLS + explicit tenant predicate).</item>
/// </list>
/// Owner only and audited. Files/blobs are not included (they are listed in the attachment tables; download them
/// through the attachment API).
/// </summary>
public class ExportTenantDataHandler(
    TenancyDbContext dbContext,
    TenancyRequest request,
    ITenantAuditWriter audit,
    TenantDataContextRegistry registry,
    IServiceProvider services,
    ISender sender,
    TimeProvider timeProvider,
    ILogger<ExportTenantDataHandler> logger) : ICommandHandler<ExportTenantDataCommand, TenantDataExportResult>
{
    public const int FormatVersion = 1;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<TenantDataExportResult> Handle(ExportTenantDataCommand command, CancellationToken cancellationToken)
    {
        request.EnsureOwner();
        var tenantId = request.TenantId;
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var tenant = await dbContext.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
                     ?? throw new NotFoundException(nameof(Tenant), tenantId);

        // Audit first: an export that was started is recorded even if building it fails.
        audit.Record(TenantAuditActions.TenantDataExported, nameof(Tenant), tenantId.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);

        var path = Path.Combine(Path.GetTempPath(), $"tenant-export-{Guid.NewGuid():N}.zip");
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            var tables = new List<string>();
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                await WriteControlPlaneAsync(zip, tenant, cancellationToken);

                foreach (var contextType in registry.ContextTypes)
                {
                    var context = (DbContext)services.GetRequiredService(contextType);
                    await TenantDataExporter.WriteCsvAsync(context, tenantId, zip, "data", cancellationToken);
                    tables.AddRange(TenantDataPurger.TenantTablesInDeleteOrder(context.Model)
                        .Select(t => t.Name.Replace("\"", string.Empty)));
                }

                await WriteJsonAsync(zip, "manifest.json", new
                {
                    FormatVersion,
                    TenantId = tenantId,
                    tenant.Identifier,
                    tenant.Name,
                    Isolation = tenant.Isolation.ToString(),
                    ExportedAt = now,
                    ExportedBy = request.UserId,
                    Tables = tables.Order(StringComparer.Ordinal).ToList()
                }, cancellationToken);
            }

            file.Position = 0;
            logger.LogInformation("Tenant {TenantId} data exported ({Bytes} bytes)", tenantId, file.Length);
            return new TenantDataExportResult(file, $"{tenant.Identifier}-export-{now:yyyyMMddHHmmss}.zip");
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private async Task WriteControlPlaneAsync(ZipArchive zip, Tenant tenant, CancellationToken ct)
    {
        await WriteJsonAsync(zip, "tenant/profile.json", new
        {
            tenant.Id, tenant.Identifier, tenant.Name, tenant.LegalName, Status = tenant.Status.ToString(),
            Isolation = tenant.Isolation.ToString(), tenant.Region, tenant.Culture, tenant.TimeZone, tenant.Currency,
            tenant.LogoAttachmentId, tenant.PrimaryColor, tenant.OwnerUserId, tenant.CreatedAt,
            Domains = tenant.Domains.Select(d => new { d.Host, d.VerifiedAt })
        }, ct);

        var branches = await dbContext.Branches.AsNoTracking().IgnoreBranchScope().OrderBy(b => b.Path)
            .Select(b => new { b.Id, b.ParentId, b.Code, b.Name, b.Depth, b.IsHeadOffice, b.IsActive, b.Address, b.Phone, b.TimeZone })
            .ToListAsync(ct);
        await WriteJsonAsync(zip, "tenant/branches.json", branches, ct);

        var roles = await dbContext.Roles.AsNoTracking().OrderBy(r => r.Name)
            .Select(r => new { r.Id, r.Name, r.Description, r.SystemKey, r.Permissions })
            .ToListAsync(ct);
        await WriteJsonAsync(zip, "tenant/roles.json", roles, ct);

        var memberships = await dbContext.Memberships.AsNoTracking().OrderBy(m => m.JoinedAt).ToListAsync(ct);
        var users = new Dictionary<Guid, UserSummaryDto>();
        foreach (var chunk in memberships.Select(m => m.UserId).Chunk(GetUserSummariesQuery.MaxIds))
        {
            foreach (var user in await sender.Send(new GetUserSummariesQuery(chunk), ct)) users[user.UserId] = user;
        }

        await WriteJsonAsync(zip, "tenant/members.json", memberships.Select(m => new
        {
            m.UserId,
            Email = users.GetValueOrDefault(m.UserId)?.Email,
            DisplayName = users.GetValueOrDefault(m.UserId)?.DisplayName,
            Status = m.Status.ToString(), m.IsOwner, m.RoleIds, m.Branches, m.JoinedAt, m.InvitedBy
        }), ct);

        var invitations = await dbContext.Invitations.AsNoTracking().OrderBy(i => i.CreatedAt)
            .Select(i => new { i.Id, i.Email, i.RoleIds, i.Branches, i.Status, i.ExpiresAt, i.InvitedBy, i.AcceptedBy, i.AcceptedAt, i.CreatedAt })
            .ToListAsync(ct);
        await WriteJsonAsync(zip, "tenant/invitations.json", invitations, ct);

        // Potentially large: streamed row by row.
        var entry = zip.CreateEntry("tenant/audit.json", CompressionLevel.Optimal);
        await using var output = entry.Open();
        await JsonSerializer.SerializeAsync(output,
            dbContext.AuditLogs.AsNoTracking().OrderBy(a => a.OccurredAt)
                .Select(a => new { a.Id, a.OccurredAt, a.ActorUserId, a.ImpersonatorUserId, a.Action, a.TargetType, a.TargetId, a.DataJson, a.IpAddress })
                .AsAsyncEnumerable(), Json, ct);
    }

    private static async Task WriteJsonAsync<T>(ZipArchive zip, string name, T value, CancellationToken ct)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var output = entry.Open();
        await JsonSerializer.SerializeAsync(output, value, Json, ct);
    }
}
