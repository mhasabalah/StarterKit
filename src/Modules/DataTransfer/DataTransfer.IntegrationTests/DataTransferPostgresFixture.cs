using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Queries;
using DataTransfer.Data;
using DataTransfer.Domains.TransferJobs.Jobs;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notification.Contracts;
using Notification.Contracts.Email;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;
using Npgsql;
using Shared.Core.Data;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.Web;
using Testcontainers.PostgreSql;

namespace DataTransfer.IntegrationTests;

/// <summary>
/// DataTransfer as it runs in a MULTI-TENANT production deployment: the REAL migration (tables, partial unique index,
/// forced RLS) applied as the admin; the REAL module (<c>AddMultiTenancy</c> + <c>AddDataTransferModule</c>) connecting
/// as a NON-superuser role, so RLS is enforced; two tenants entered with <see cref="TenantScope"/>.
/// Faked boundaries only: Attachment storage, notifications, Hangfire (recording), authorization decisions, the clock,
/// and the owning module (a test definition, <see cref="TestItems"/>).
/// </summary>
public sealed class DataTransferPostgresFixture : IAsyncLifetime
{
    private const string AppRole = "datatransfer_app";
    private const string AppPassword = "datatransfer_app_password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider? _provider;

    public static readonly TenantInfo TenantA = new(Guid.Parse("aaaaaaaa-3333-0000-0000-000000000001"), "acme", "Acme");
    public static readonly TenantInfo TenantB = new(Guid.Parse("bbbbbbbb-4444-0000-0000-000000000002"), "globex", "Globex");

    public IServiceProvider Services => _provider ?? throw new InvalidOperationException("Not initialized");
    public string AdminConnectionString => _container.GetConnectionString();

    public MutableTimeProvider Clock { get; } = new();
    public RecordingJobClient Jobs { get; } = new();
    public FakeAttachments Attachments { get; } = new();
    public RecordingNotifications Notifications { get; } = new();
    public TestCaller Caller { get; } = new();
    public TestItems Items { get; } = new();
    public RecordingUserEvents Events { get; } = new();
    public TestQuota Quota { get; } = new();

    /// <summary>The module's live options (tests lower limits temporarily; <see cref="ResetAsync"/> restores them).</summary>
    public DataTransfer.Options.DataTransferOptions Options =>
        Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DataTransfer.Options.DataTransferOptions>>().Value;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<DataTransferDbContext>()
            .UseNpgsql(AdminConnectionString, b => b.UseModuleMigrationsHistory<DataTransferDbContext>())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var admin = new DataTransferDbContext(adminOptions))
        {
            await admin.Database.MigrateAsync();
        }

        await AdminExecAsync($"CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS");
        await AdminExecAsync($"GRANT USAGE ON SCHEMA datatransfer TO {AppRole}");
        await AdminExecAsync($"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA datatransfer TO {AppRole}");

        var appConnection = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = AppRole,
            Password = AppPassword
        }.ConnectionString;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:starterkit"] = appConnection,
                ["MultiTenancy:Enabled"] = "true",
                ["DataTransfer:BatchSize"] = "2",
                ["DataTransfer:MaxAttempts"] = "3",
                ["DataTransfer:ExportProgressEvery"] = "2",
                ["DataTransfer:RetentionDays"] = "7",
                ["DataTransfer:StalledAfterMinutes"] = "10"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddMultiTenancy(configuration);
        services.AddSingleton<ITenantStore>(new FixedTenantStore(TenantA, TenantB));
        services.AddSingleton<ITenantMembershipResolver>(new CallerMemberships(Caller));
        services.AddSingleton<IHttpContextService>(new CallerHttpContextService(Caller));
        services.AddSingleton<INotificationSender>(Notifications);
        services.AddSingleton(Attachments);
        services.AddSingleton(Items);
        Attachments.Register(services);

        services.AddDataTransferModule(configuration);

        services.AddImportDefinition<TestItemImportDefinition>();
        services.AddExportDefinition<TestItemExportDefinition>();
        services.RemoveAll<IBackgroundJobClient>();
        services.AddSingleton<IBackgroundJobClient>(Jobs);
        services.RemoveAll<Notification.Contracts.Realtime.IUserEventPublisher>();
        services.AddSingleton<Notification.Contracts.Realtime.IUserEventPublisher>(Events);
        services.RemoveAll<Shared.Core.Entitlements.IQuotaService>();
        services.AddSingleton<Shared.Core.Entitlements.IQuotaService>(Quota);
        services.RemoveAll<IAuthorizationService>();
        services.AddSingleton<IAuthorizationService>(new CallerAuthorization(Caller));

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>One HTTP request by <paramref name="user"/> (default: <see cref="TestCaller.UserId"/>) in <paramref name="tenant"/>.</summary>
    public async Task<T> SendAsync<T>(TenantInfo tenant, IRequest<T> request, Guid? user = null, string culture = "en")
    {
        var previous = Caller.UserId;
        var previousCulture = System.Globalization.CultureInfo.CurrentUICulture;
        Caller.UserId = user ?? previous;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(culture);
        try
        {
            using (TenantScope.Begin(tenant))
            {
                await using var scope = Services.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = Caller.HttpContext();
                return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
            }
        }
        finally
        {
            Caller.UserId = previous;
            System.Globalization.CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    public async Task<T> InTenantAsync<T>(TenantInfo tenant, Func<IServiceProvider, Task<T>> work)
    {
        using (TenantScope.Begin(tenant))
        {
            await using var scope = Services.CreateAsyncScope();
            return await work(scope.ServiceProvider);
        }
    }

    public Task<T> InTenantDbAsync<T>(TenantInfo tenant, Func<DataTransferDbContext, Task<T>> work) =>
        InTenantAsync(tenant, sp => work(sp.GetRequiredService<DataTransferDbContext>()));

    public Task<TransferJob> JobAsync(TenantInfo tenant, Guid id) =>
        InTenantDbAsync(tenant, db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == id));

    /// <summary>Runs the queued Hangfire job exactly like the data-transfer server would (tenant restored).</summary>
    public Task RunImportAsync(TenantInfo tenant, Guid jobId) =>
        InTenantAsync(tenant, async sp =>
        {
            await sp.GetRequiredService<ProcessImportJob>().ExecuteAsync(jobId, CancellationToken.None);
            return true;
        });

    public Task RunExportAsync(TenantInfo tenant, Guid jobId) =>
        InTenantAsync(tenant, async sp =>
        {
            await sp.GetRequiredService<ProcessExportJob>().ExecuteAsync(jobId, CancellationToken.None);
            return true;
        });

    public async Task<TransferJobDto> StartImportAsync(TenantInfo tenant, string csv, ImportMode mode = ImportMode.Commit,
        ImportPolicy policy = ImportPolicy.BestEffort, string definition = TestItemImportDefinition.DefinitionKey,
        string culture = "en") =>
        await StartImportAsync(tenant, System.Text.Encoding.UTF8.GetBytes(csv), "items.csv", mode, policy, definition, culture);

    public async Task<TransferJobDto> StartImportAsync(TenantInfo tenant, byte[] bytes, string fileName,
        ImportMode mode = ImportMode.Commit, ImportPolicy policy = ImportPolicy.BestEffort,
        string definition = TestItemImportDefinition.DefinitionKey, string culture = "en")
    {
        await using var stream = new MemoryStream(bytes);
        return await SendAsync(tenant, new StartImportCommand(definition, fileName, "application/octet-stream", bytes.Length,
            stream, mode, policy), culture: culture);
    }

    /// <summary>Runs the import with a token that is cancelled from inside the run (server shutdown).</summary>
    public Task RunImportAsync(TenantInfo tenant, Guid jobId, CancellationToken cancellationToken) =>
        InTenantAsync(tenant, async sp =>
        {
            await sp.GetRequiredService<ProcessImportJob>().ExecuteAsync(jobId, cancellationToken);
            return true;
        });

    /// <summary>
    /// Test isolation: the tests share one database, and an unfinished import would block the next one (one active
    /// import per definition). Finishes leftovers and resets the fakes.
    /// </summary>
    public async Task ResetAsync()
    {
        Items.Reset();
        Caller.Reset();
        Events.Clear();
        Quota.Reset();
        Options.ProgressIntervalMilliseconds = 1_000;
        Options.ExportProgressEvery = 2;
        Options.BatchSize = 2;
        Attachments.RejectUploads = null;
        Options.MaxStoredErrors = 10_000;
        Options.Limits.MaxFileBytes = 50L * 1024 * 1024;
        Options.Limits.MaxExportRows = 1_000_000;
        await AdminExecAsync("UPDATE datatransfer.transfer_jobs SET status = 'Cancelled' WHERE status IN ('Pending', 'Running')");
    }

    public async Task<T?> AdminScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
    }

    public async Task AdminExecAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class DataTransferPostgresCollection : ICollectionFixture<DataTransferPostgresFixture>
{
    public const string Name = "datatransfer-postgres";
}

// ----- the owning module: a tiny "items" store with an import and an export definition ---------------------------

/// <summary>In-memory business data per tenant, standing in for the owning module's tables.</summary>
public sealed class TestItems
{
    public ConcurrentDictionary<(Guid Tenant, string Code), (string Name, int Qty)> Rows { get; } = new();

    /// <summary>How often each code was written (replay detection).</summary>
    public ConcurrentDictionary<(Guid Tenant, string Code), int> Writes { get; } = new();

    private int _batches;

    public int Batches => _batches;

    public void CountBatch() => Interlocked.Increment(ref _batches);

    /// <summary>Throws while applying a batch that contains this code (crash simulation); cleared after the first throw unless <see cref="AlwaysThrow"/>.</summary>
    public string? ThrowOnCode { get; set; }

    public bool AlwaysThrow { get; set; }

    /// <summary>Runs after each applied batch (e.g. a user cancels meanwhile).</summary>
    public Func<Task>? AfterBatch { get; set; }

    /// <summary>The branch scope the definition ran in (last call).</summary>
    public BranchScope? ObservedBranches { get; set; }

    public int Count(Guid tenant) => Rows.Keys.Count(k => k.Tenant == tenant);

    public void Reset()
    {
        Rows.Clear();
        Writes.Clear();
        _batches = 0;
        ThrowOnCode = null;
        AlwaysThrow = false;
        AfterBatch = null;
        ObservedBranches = null;
    }
}

public sealed class TestItemImportDefinition(TestItems items, ITenantContext tenant) : IImportDefinition
{
    public const string DefinitionKey = "test.items";

    public string Key => DefinitionKey;
    public LocalizedText DisplayName => new("Items", "عناصر");
    public string RequiredPermission => TenantPermissions.ProductsManage;

    public IReadOnlyList<TransferColumn> Columns { get; } =
    [
        new("code", new LocalizedText("Code", "الرمز"), Required: true, Sample: "A-1"),
        new("name", new LocalizedText("Name", "الاسم"), Required: true, Sample: "Apple"),
        new("qty", new LocalizedText("Quantity", "الكمية"), Sample: "3", Kind: TransferColumnKind.Number)
    ];

    public async Task<IReadOnlyList<ImportRowResult>> ApplyBatchAsync(IReadOnlyList<ImportRecord> records,
        ImportContext context, CancellationToken cancellationToken)
    {
        items.CountBatch();
        if (items.ThrowOnCode is { } code && records.Any(r => r.Get("code") == code))
        {
            if (!items.AlwaysThrow)
                items.ThrowOnCode = null;
            throw new InvalidOperationException("Simulated crash while saving the batch.");
        }

        var tenantId = tenant.TenantId!.Value;
        items.ObservedBranches = tenant.Branches;
        var results = records.Select(r => Apply(r, tenantId, context.DryRun)).ToList();
        if (items.AfterBatch is { } hook)
            await hook();
        return results;
    }

    private ImportRowResult Apply(ImportRecord record, Guid tenantId, bool dryRun)
    {
        var code = record.Get("code");
        var name = record.Get("name");
        if (code is null || name is null)
            return ImportRowResult.Failed(record.RowNumber, code is null ? "code" : "name", "required", "A value is required.");
        var qtyText = record.Get("qty") ?? "0";
        if (!int.TryParse(qtyText, out var qty))
            return ImportRowResult.Failed(record.RowNumber, "qty", "invalid_number", $"'{qtyText}' is not a number.");

        var key = (tenantId, code);
        var exists = items.Rows.TryGetValue(key, out var current);
        if (exists && current == (name, qty))
            return ImportRowResult.Unchanged(record.RowNumber);
        if (dryRun)
            return exists ? ImportRowResult.Updated(record.RowNumber) : ImportRowResult.Created(record.RowNumber);

        items.Rows[key] = (name, qty);
        items.Writes.AddOrUpdate(key, 1, (_, n) => n + 1);
        return exists ? ImportRowResult.Updated(record.RowNumber) : ImportRowResult.Created(record.RowNumber);
    }
}

public sealed class TestItemExportDefinition(TestItems items, ITenantContext tenant) : IExportDefinition
{
    public string Key => TestItemImportDefinition.DefinitionKey;
    public LocalizedText DisplayName => new("Items", "عناصر");
    public string RequiredPermission => TenantPermissions.ProductsManage;

    public IReadOnlyList<TransferColumn> Columns { get; } =
    [
        new("code", new LocalizedText("Code", "الرمز")),
        new("name", new LocalizedText("Name", "الاسم")),
        new("qty", new LocalizedText("Quantity", "الكمية"), Kind: TransferColumnKind.Number)
    ];

    public async IAsyncEnumerable<IReadOnlyList<string?>> StreamAsync(ExportContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tenantId = tenant.TenantId!.Value;
        items.ObservedBranches = tenant.Branches;
        foreach (var (key, value) in items.Rows.Where(r => r.Key.Tenant == tenantId).OrderBy(r => r.Key.Code, StringComparer.Ordinal))
        {
            await Task.Yield();
            if (context.Filter.TryGetValue("minQty", out var min) && value.Qty < int.Parse(min))
                continue;
            yield return [key.Code, value.Name, value.Qty.ToString()];
            if (items.AfterBatch is { } hook)
            {
                items.AfterBatch = null;
                await hook();
            }
        }
    }
}

// ----- fakes ------------------------------------------------------------------------------------------------------

public sealed class MutableTimeProvider : TimeProvider
{
    // Starts at the real time and only moves forward through Advance: several tests compare the job clock with
    // Postgres now() / DateTime.UtcNow (expiry and purge), so a pinned date would turn them into a date bomb.
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public sealed class FixedTenantStore(params TenantInfo[] tenants) : ITenantStore
{
    public Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(tenants.FirstOrDefault(t => t.Id == tenantId));

    public Task<TenantInfo?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default) =>
        Task.FromResult(tenants.FirstOrDefault(t => t.Identifier == identifier));

    public Task<TenantInfo?> FindByHostAsync(string host, CancellationToken cancellationToken = default) =>
        Task.FromResult<TenantInfo?>(null);

    public Task<IReadOnlyList<TenantInfo>> GetServiceableTenantsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantInfo>>(tenants);
}

/// <summary>The current request's user and which tenant permissions they lack.</summary>
public sealed class TestCaller
{
    public Guid UserId { get; set; } = Guid.Parse("11111111-aaaa-0000-0000-000000000001");

    public HashSet<string> Denied { get; } = new(StringComparer.Ordinal);

    /// <summary>Users whose membership ended (the resolver returns none).</summary>
    public HashSet<Guid> NotMembers { get; } = [];

    /// <summary>Branch restriction of every membership (null = unrestricted).</summary>
    public IReadOnlyList<Guid>? AllowedBranches { get; set; }

    public void Reset()
    {
        Denied.Clear();
        NotMembers.Clear();
        AllowedBranches = null;
    }

    public HttpContext HttpContext() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "test"))
    };
}

/// <summary>Memberships as the job re-reads them at every attempt: every user holds every non-denied permission.</summary>
public sealed class CallerMemberships(TestCaller caller) : ITenantMembershipResolver
{
    public Task<TenantMembershipInfo?> GetMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (caller.NotMembers.Contains(userId))
            return Task.FromResult<TenantMembershipInfo?>(null);
        var permissions = TenantPermissions.All.Except(caller.Denied).ToHashSet(StringComparer.Ordinal);
        return Task.FromResult<TenantMembershipInfo?>(new TenantMembershipInfo(tenantId, userId, false, permissions,
            new BranchScope(caller.AllowedBranches), "stamp"));
    }

    public Task<IReadOnlyList<Guid>> GetTenantIdsOfUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task InvalidateAsync(Guid? tenantId = null, Guid? userId = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed class CallerAuthorization(TestCaller caller) : IAuthorizationService
{
    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource,
        IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(AuthorizationResult.Success());

    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
        Task.FromResult(caller.Denied.Contains(policyName[TenantPermissions.PolicyPrefix.Length..])
            ? AuthorizationResult.Failed()
            : AuthorizationResult.Success());
}

public sealed class CallerHttpContextService(TestCaller caller) : IHttpContextService
{
    public string? GetUserId() => caller.UserId.ToString();
    public long? GetUserIdAsLong() => null;
    public Guid? GetUserIdAsGuid() => caller.UserId;
    public string? GetUserName() => caller.UserId.ToString();
    public string? GetEmail() => null;
    public IEnumerable<string> GetRoles() => [];
    public bool IsAuthenticated() => true;
    public string? GetClaimValue(string claimType) => null;
    public IEnumerable<string> GetClaimValues(string claimType) => [];
    public IEnumerable<Claim> GetAllClaims() => [];
    public bool HasClaim(string claimType, string? claimValue = null) => false;
    public string? GetIpAddress() => null;
    public string? GetUserAgent() => null;
    public string? GetRequestPath() => null;
    public string? GetRequestMethod() => null;
    public string? GetCorrelationId() => null;
    public string? GetHeaderValue(string headerKey) => null;
    public string? GetHost() => null;
    public string? GetScheme() => null;
    public string? GetRequestUrl() => null;
    public string? GetTenantId() => null;
}

/// <summary>Attachment stand-in: files in memory, through the same trusted contracts the module uses.</summary>
public sealed class FakeAttachments
{
    public sealed record StoredFile(Guid Id, Guid? EntityId, string FileName, string ContentType, byte[] Content);

    public ConcurrentDictionary<Guid, StoredFile> Files { get; } = new();

    /// <summary>Makes uploads fail: a transient (503, retried) or a permanent (400, fails the job) Attachment error.</summary>
    public Exception? RejectUploads { get; set; }

    public IEnumerable<StoredFile> Of(Guid jobId) => Files.Values.Where(f => f.EntityId == jobId);

    public void Register(IServiceCollection services)
    {
        services.AddTransient<IRequestHandler<UploadAttachmentCommand, UploadAttachmentResult>>(_ => new Upload(this));
        services.AddTransient<IRequestHandler<OpenAttachmentContentQuery, OpenAttachmentContentResult>>(_ => new Open(this));
        services.AddTransient<IRequestHandler<GetAttachmentReadUrlQuery, GetAttachmentReadUrlResult>>(_ => new ReadUrl(this));
        services.AddTransient<IRequestHandler<DeleteAttachmentsByEntityCommand, DeleteAttachmentsByEntityResult>>(_ => new Delete(this));
    }

    private sealed class Upload(FakeAttachments store) : IRequestHandler<UploadAttachmentCommand, UploadAttachmentResult>
    {
        public async Task<UploadAttachmentResult> Handle(UploadAttachmentCommand request, CancellationToken cancellationToken)
        {
            if (store.RejectUploads is { } rejection)
                throw rejection;
            using var buffer = new MemoryStream();
            await request.FileStream.CopyToAsync(buffer, cancellationToken);
            var file = new StoredFile(Guid.NewGuid(), request.EntityId, request.FileName, request.ContentType, buffer.ToArray());
            store.Files[file.Id] = file;
            return new UploadAttachmentResult(file.Id, file.FileName, $"/files/{file.Id}", null, file.Content.Length, file.ContentType);
        }
    }

    private sealed class Open(FakeAttachments store) : IRequestHandler<OpenAttachmentContentQuery, OpenAttachmentContentResult>
    {
        public Task<OpenAttachmentContentResult> Handle(OpenAttachmentContentQuery request, CancellationToken cancellationToken)
        {
            var file = store.Files.TryGetValue(request.AttachmentId, out var f) ? f : throw new NotFoundException("Attachment", request.AttachmentId);
            return Task.FromResult(new OpenAttachmentContentResult(new MemoryStream(file.Content), file.FileName, file.ContentType,
                file.Content.Length));
        }
    }

    private sealed class ReadUrl(FakeAttachments store) : IRequestHandler<GetAttachmentReadUrlQuery, GetAttachmentReadUrlResult>
    {
        public Task<GetAttachmentReadUrlResult> Handle(GetAttachmentReadUrlQuery request, CancellationToken cancellationToken)
        {
            var file = store.Files[request.AttachmentId];
            return Task.FromResult(new GetAttachmentReadUrlResult($"https://files.example.com/{file.Id}", DateTime.UtcNow.AddMinutes(5),
                file.FileName, file.ContentType));
        }
    }

    private sealed class Delete(FakeAttachments store) : IRequestHandler<DeleteAttachmentsByEntityCommand, DeleteAttachmentsByEntityResult>
    {
        public Task<DeleteAttachmentsByEntityResult> Handle(DeleteAttachmentsByEntityCommand request, CancellationToken cancellationToken)
        {
            var ids = store.Files.Values.Where(f => f.EntityId == request.EntityId).Select(f => f.Id).ToList();
            foreach (var id in ids)
                store.Files.TryRemove(id, out _);
            return Task.FromResult(new DeleteAttachmentsByEntityResult(ids.Count));
        }
    }
}

public sealed class RecordingNotifications : INotificationSender, INotificationBuilder
{
    private readonly ConcurrentQueue<(Guid User, InAppContent Content)> _sent = new();
    private Guid _user;
    private InAppContent? _content;

    public IReadOnlyList<(Guid User, InAppContent Content)> InApp => _sent.ToList();

    public INotificationBuilder For(Guid userId, NotificationCulture culture = NotificationCulture.En)
    {
        _user = userId;
        return this;
    }

    public INotificationBuilder ToEmail(string email, NotificationCulture culture = NotificationCulture.En, Guid? userId = null) => this;

    public INotificationBuilder WithEmail(EmailTemplate template, Dictionary<string, object>? model = null,
        EmailAttachment[]? attachments = null) => this;

    public INotificationBuilder WithWhatsApp(WhatsAppTemplate template, Dictionary<string, object>? model = null) => this;

    public INotificationBuilder WithInApp(InAppContent content)
    {
        _content = content;
        return this;
    }

    public INotificationBuilder WithPush(Notification.Contracts.Push.PushContent content) => this;

    public INotificationBuilder WithPush(bool sensitive = false) => this;

    public Task<Guid> SendAsync(NotificationChannel? forcedChannel = null, CancellationToken ct = default)
    {
        if (_content is not null)
            _sent.Enqueue((_user, _content));
        _content = null;
        return Task.FromResult(Guid.NewGuid());
    }
}

/// <summary>The hub stand-in: every live event sent, in order.</summary>
public sealed class RecordingUserEvents : Notification.Contracts.Realtime.IUserEventPublisher
{
    private readonly ConcurrentQueue<(Guid User, Notification.Contracts.Realtime.UserEvent Event)> _events = new();

    public IReadOnlyList<(Guid User, Notification.Contracts.Realtime.UserEvent Event)> All => _events.ToList();

    public IReadOnlyList<Notification.Contracts.Realtime.UserEvent> For(Guid jobId) =>
        _events.Select(e => e.Event).Where(e => e.Data.TryGetValue("jobId", out var id) && Equals(id, jobId)).ToList();

    public Task PublishAsync(Guid userId, Notification.Contracts.Realtime.UserEvent userEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue((userId, userEvent));
        return Task.CompletedTask;
    }

    public void Clear() => _events.Clear();
}

/// <summary>A plan quota for the current subscriber: unlimited unless <see cref="Limit"/> is set.</summary>
public sealed class TestQuota : Shared.Core.Entitlements.IQuotaService
{
    private long _used;

    public long? Limit { get; set; }

    public long Used => Interlocked.Read(ref _used);

    public List<string> Keys { get; } = [];

    public Task<Shared.Core.Entitlements.QuotaResult> TryConsumeAsync(string key, long quantity = 1,
        CancellationToken cancellationToken = default)
    {
        lock (Keys)
        {
            Keys.Add(key);
            if (Limit is { } limit && _used + quantity > limit)
                return Task.FromResult(new Shared.Core.Entitlements.QuotaResult(false, _used, limit));
            _used += quantity;
            return Task.FromResult(new Shared.Core.Entitlements.QuotaResult(true, _used, Limit));
        }
    }

    public void Reset()
    {
        lock (Keys)
        {
            _used = 0;
            Limit = null;
            Keys.Clear();
        }
    }
}

public sealed class RecordingJobClient : IBackgroundJobClient
{
    public sealed record Recorded(Job Job, IState State)
    {
        public Guid JobId => (Guid)Job.Args[0];
        public bool IsScheduled => State is ScheduledState;
    }

    private readonly ConcurrentQueue<Recorded> _jobs = new();

    public IReadOnlyList<Recorded> All => _jobs.ToList();

    public IReadOnlyList<Recorded> For(Guid jobId) => _jobs.Where(j => j.Job.Args.Count > 0 && j.Job.Args[0] is Guid id && id == jobId).ToList();

    public string Create(Job job, IState state)
    {
        _jobs.Enqueue(new Recorded(job, state));
        return Guid.NewGuid().ToString("N");
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}
