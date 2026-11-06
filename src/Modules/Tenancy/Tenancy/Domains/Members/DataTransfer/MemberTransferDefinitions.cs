using Shared.Contracts.Localization;
using System.Globalization;
using System.Runtime.CompilerServices;
using DataTransfer.Contracts.Definitions;
using Tenancy.Domains.Members.Features;

namespace Tenancy.Domains.Members.DataTransfer;

/// <summary>Columns shared by the member import and export (stable names: they are the file contract).</summary>
public static class MemberTransferColumns
{
    public const string Key = "tenancy.members";

    /// <summary>Separates role names and branch codes inside one cell.</summary>
    public const char ListSeparator = ';';

    /// <summary>Appended to a branch code to assign ONLY that branch (default: the branch and its descendants).</summary>
    public const string SelfOnlySuffix = ":self";

    public const string Email = "email";
    public const string Roles = "roles";
    public const string Branches = "branches";
    public const string DisplayName = "display_name";
    public const string Status = "status";
    public const string JoinedAt = "joined_at";

    public static readonly LocalizedText Title = new("Members", "الأعضاء");

    public static readonly IReadOnlyList<TransferColumn> Import =
    [
        new(Email, new LocalizedText("Email", "البريد الإلكتروني"), Required: true, Sample: "new.member@example.com",
            Description: new LocalizedText(
                "Invites this address. An existing member or a pending invitation is left unchanged.",
                "يدعو هذا العنوان. العضو الحالي أو الدعوة المعلّقة تُترك دون تغيير.")),
        new(Roles, new LocalizedText("Roles", "الأدوار"), Sample: "Member",
            Description: new LocalizedText("Tenant role names separated by ';'. Empty = the default Member role.",
                "أسماء أدوار المستأجر مفصولة بـ ';'. الفارغ = دور العضو الافتراضي.")),
        new(Branches, new LocalizedText("Branches", "الفروع"),
            Description: new LocalizedText(
                "Branch codes separated by ';'. A code covers its sub-branches; add ':self' to assign only that branch.",
                "أكواد الفروع مفصولة بـ ';'. الكود يشمل فروعه الفرعية؛ أضف ':self' لتعيين الفرع وحده."))
    ];

    public static readonly IReadOnlyList<TransferColumn> Export =
    [
        .. Import,
        new(DisplayName, new LocalizedText("Name", "الاسم"),
            Description: new LocalizedText("Read only: ignored on import.", "للقراءة فقط: يُتجاهل عند الاستيراد.")),
        new(Status, new LocalizedText("Status", "الحالة"),
            Description: new LocalizedText("Read only: ignored on import.", "للقراءة فقط: يُتجاهل عند الاستيراد.")),
        new(JoinedAt, new LocalizedText("Joined at", "تاريخ الانضمام"),
            Description: new LocalizedText("Read only (UTC): ignored on import.", "للقراءة فقط (UTC): يُتجاهل عند الاستيراد."))
    ];

    public static List<string> SplitList(string? cell) =>
        cell is null
            ? []
            : cell.Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

/// <summary>
/// Member import: INVITES each e-mail through <see cref="InviteMemberCommand"/> (the same handler as the API: plan seat
/// limit, anti-escalation rules, audit, invitation e-mail), acting as the requester. Role names become ids through
/// <see cref="GetTenantRolesQuery"/> and branch codes through the tenant's branches (both once per batch).
/// <list type="bullet">
/// <item>Idempotent: an e-mail that is already a member or has a pending invitation is <c>Unchanged</c> (checked up
/// front per batch; a concurrent duplicate that still reaches the handler's conflict check is also <c>Unchanged</c>).</item>
/// <item>Seat limit reached: that row fails with <c>tenant_limit_exceeded</c>; the others are unaffected.</item>
/// <item>Dry run: every check including role/branch grant rules runs, but nothing is invited and no e-mail is sent. The
/// plan seat limit is only enforced by a real run.</item>
/// <item>Each invitation commits with its own save (the invite handler owns its unit of work); the pre-check makes a
/// replayed batch harmless.</item>
/// </list>
/// The requester's membership is the actor: a non-owner can only grant permissions they hold, in their own branches.
/// </summary>
public sealed class MemberImportDefinition(
    TenancyDbContext dbContext,
    ISender sender,
    MembershipGrantValidator grants,
    TimeProvider timeProvider) : IImportDefinition
{
    public string Key => MemberTransferColumns.Key;
    public LocalizedText DisplayName => MemberTransferColumns.Title;
    public string RequiredPermission => TenantPermissions.MembersManage;
    public IReadOnlyList<TransferColumn> Columns => MemberTransferColumns.Import;

    public async Task<IReadOnlyList<ImportRowResult>> ApplyBatchAsync(IReadOnlyList<ImportRecord> records,
        ImportContext context, CancellationToken cancellationToken)
    {
        var rows = records.Select(r => (Record: r, Row: MemberRow.Parse(r, context.Culture))).ToList();
        var valid = rows.Where(r => r.Row.Errors.Count == 0).Select(r => r.Row).ToList();

        var results = new List<ImportRowResult>(records.Count);
        if (valid.Count == 0)
        {
            results.AddRange(rows.Select(r => ImportRowResult.Failed(r.Record.RowNumber, r.Row.Errors)));
            return results;
        }

        var lookups = await LoadLookupsAsync(valid, cancellationToken);
        foreach (var (record, row) in rows)
            results.Add(await ApplyRowAsync(record, row, lookups, context, cancellationToken));
        return results;
    }

    private async Task<ImportRowResult> ApplyRowAsync(ImportRecord record, MemberRow row, Lookups lookups,
        ImportContext context, CancellationToken cancellationToken)
    {
        if (row.Errors.Count > 0)
            return ImportRowResult.Failed(record.RowNumber, row.Errors);

        var culture = context.Culture;
        if (lookups.KnownEmails.Contains(row.Email))
            return ImportRowResult.Unchanged(record.RowNumber);

        var roleIds = new List<Guid>();
        foreach (var name in row.RoleNames)
        {
            if (!lookups.RolesByName.TryGetValue(name, out var roleId))
            {
                return Fail(record, MemberTransferColumns.Roles, "role_not_found",
                    Text(culture, $"No tenant role is named '{name}'.", $"لا يوجد دور في المستأجر باسم '{name}'."));
            }

            roleIds.Add(roleId);
        }

        var branches = new List<BranchAssignment>();
        foreach (var (code, includeDescendants) in row.Branches)
        {
            if (!lookups.BranchesByCode.TryGetValue(code, out var branchId))
            {
                return Fail(record, MemberTransferColumns.Branches, "branch_not_found",
                    Text(culture, $"No active branch has the code '{code}'.", $"لا يوجد فرع نشط بالكود '{code}'."));
            }

            branches.Add(new BranchAssignment(branchId, includeDescendants));
        }

        var command = new InviteMemberCommand(row.Email, roleIds.Distinct().ToList(), branches);
        var validation = await new InviteMemberCommandValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return ImportRowResult.Failed(record.RowNumber, validation.Errors
                .Select(e => new ImportRowError(
                    e.PropertyName == nameof(InviteMemberCommand.Email) ? MemberTransferColumns.Email : null,
                    "invalid", e.ErrorMessage))
                .ToList());
        }

        try
        {
            if (context.DryRun)
                await grants.EnsureValidAsync(command.RoleIds, command.Branches, cancellationToken);
            else
                await sender.Send(command, cancellationToken);
        }
        catch (ConflictException)
        {
            // A pending invitation appeared since the pre-check (another import or a click): same as "already invited".
            lookups.KnownEmails.Add(row.Email);
            return ImportRowResult.Unchanged(record.RowNumber);
        }
        catch (TenantLimitExceededException ex)
        {
            return Fail(record, null, "tenant_limit_exceeded", ex.Message);
        }
        catch (Exception ex) when (ex is InvalidMembershipException or ForbiddenException)
        {
            return Fail(record, null, ex is InvalidMembershipException ? "invalid" : "forbidden", ex.Message);
        }

        lookups.KnownEmails.Add(row.Email); // the same address again in this file is a no-op
        return ImportRowResult.Created(record.RowNumber);
    }

    private async Task<Lookups> LoadLookupsAsync(List<MemberRow> valid, CancellationToken cancellationToken)
    {
        var roles = await sender.Send(new GetTenantRolesQuery(), cancellationToken);
        var rolesByName = roles.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var codes = valid.SelectMany(r => r.Branches.Select(b => b.Code)).Distinct(StringComparer.Ordinal).ToList();
        var branchesByCode = codes.Count == 0
            ? new Dictionary<string, Guid>()
            : await dbContext.Branches.AsNoTracking().Where(b => b.IsActive && codes.Contains(b.Code))
                .ToDictionaryAsync(b => b.Code, b => b.Id, StringComparer.Ordinal, cancellationToken);

        var emails = valid.Select(r => r.Email).Distinct(StringComparer.Ordinal).ToList();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var known = new HashSet<string>(StringComparer.Ordinal);
        known.UnionWith(await dbContext.Invitations.AsNoTracking()
            .Where(i => emails.Contains(i.Email) && i.Status == TenantInvitationStatus.Pending && i.ExpiresAt > now)
            .Select(i => i.Email).ToListAsync(cancellationToken));
        await foreach (var member in MemberPaging.AllAsync(sender, null, cancellationToken))
        {
            if (member.Email is { } email)
                known.Add(TenantInvitation.NormalizeEmail(email));
        }

        return new Lookups(rolesByName, branchesByCode, known);
    }

    private static ImportRowResult Fail(ImportRecord record, string? column, string code, string message) =>
        ImportRowResult.Failed(record.RowNumber, column, code, message);

    private static string Text(string culture, string en, string ar) => TransferCulture.IsArabic(culture) ? ar : en;

    private sealed record Lookups(
        Dictionary<string, Guid> RolesByName,
        Dictionary<string, Guid> BranchesByCode,
        HashSet<string> KnownEmails);
}

/// <summary>
/// Member export: the tenant's members (owner first, then by join date) through <see cref="GetMembersQuery"/>, in the
/// import column order plus name, status and join date. Filter: <c>status</c> (a <see cref="TenantMembershipStatus"/>).
/// </summary>
public sealed class MemberExportDefinition(TenancyDbContext dbContext, ISender sender) : IExportDefinition
{
    public string Key => MemberTransferColumns.Key;
    public LocalizedText DisplayName => MemberTransferColumns.Title;
    public string RequiredPermission => TenantPermissions.MembersManage;
    public IReadOnlyList<TransferColumn> Columns => MemberTransferColumns.Export;

    public async IAsyncEnumerable<IReadOnlyList<string?>> StreamAsync(ExportContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TenantMembershipStatus? status = null;
        if (context.Filter.TryGetValue("status", out var statusText) &&
            Enum.TryParse<TenantMembershipStatus>(statusText, ignoreCase: true, out var parsed))
        {
            status = parsed;
        }

        var codes = await dbContext.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code, cancellationToken);

        await foreach (var member in MemberPaging.AllAsync(sender, status, cancellationToken))
        {
            var branches = member.Branches.Select(b =>
                (codes.GetValueOrDefault(b.BranchId) ?? b.BranchId.ToString()) +
                (b.IncludeDescendants ? string.Empty : MemberTransferColumns.SelfOnlySuffix));
            yield return
            [
                member.Email,
                string.Join(MemberTransferColumns.ListSeparator, member.Roles),
                string.Join(MemberTransferColumns.ListSeparator, branches),
                member.DisplayName,
                member.Status.ToString(),
                member.JoinedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            ];
        }
    }
}

/// <summary>Pages through <see cref="GetMembersQuery"/> (the handler resolves user summaries in chunks per page).</summary>
internal static class MemberPaging
{
    private const int PageSize = 100;

    public static async IAsyncEnumerable<MemberDto> AllAsync(ISender sender, TenantMembershipStatus? status,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var page = 1; ; page++)
        {
            var result = await sender.Send(
                new GetMembersQuery { Status = status, PageNumber = page, PageSize = PageSize }, cancellationToken);
            foreach (var member in result.Result ?? [])
                yield return member;

            var info = result.PageInfoResult;
            if (info is null || (long)info.CurrentPage * Math.Max(1, info.PageSize) >= info.TotalItems)
                yield break;
        }
    }
}

/// <summary>One parsed import row: typed values, or the parse errors of its cells.</summary>
internal sealed class MemberRow
{
    public string Email { get; private init; } = string.Empty;
    public List<string> RoleNames { get; private init; } = [];
    public List<(string Code, bool IncludeDescendants)> Branches { get; private init; } = [];
    public List<ImportRowError> Errors { get; } = [];

    public static MemberRow Parse(ImportRecord record, string culture)
    {
        var errors = new List<ImportRowError>();
        var email = record.Get(MemberTransferColumns.Email);
        if (email is null)
        {
            errors.Add(new ImportRowError(MemberTransferColumns.Email, "required",
                TransferCulture.IsArabic(culture) ? "القيمة مطلوبة." : "A value is required."));
        }

        var branches = MemberTransferColumns.SplitList(record.Get(MemberTransferColumns.Branches))
            .Select(entry => entry.EndsWith(MemberTransferColumns.SelfOnlySuffix, StringComparison.OrdinalIgnoreCase)
                ? (entry[..^MemberTransferColumns.SelfOnlySuffix.Length].Trim().ToUpperInvariant(), false)
                : (entry.ToUpperInvariant(), true))
            .DistinctBy(b => b.Item1)
            .ToList();

        var row = new MemberRow
        {
            Email = email is null ? string.Empty : TenantInvitation.NormalizeEmail(email),
            RoleNames = MemberTransferColumns.SplitList(record.Get(MemberTransferColumns.Roles))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Branches = branches
        };
        row.Errors.AddRange(errors);
        return row;
    }
}
