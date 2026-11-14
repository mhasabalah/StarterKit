using System.Collections.Concurrent;
using System.Security.Claims;
using Agentic.Contracts.Agents;
using Agentic.Contracts.Approvals;
using Agentic.Contracts.Runs;
using Agentic.Contracts.Scripting;
using Agentic.Contracts.Tools;
using Agentic.Data;
using Agentic.Domains.AgentRuns.Jobs;
using Agentic.Domains.AgentRuns.Models;
using Agentic.Domains.Approvals.Jobs;
using Agentic.Domains.Approvals.Models;
using Agentic.Options;
using FluentValidation;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Notification.Contracts;
using Notification.Contracts.Email;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;
using Notification.Contracts.Realtime;
using Npgsql;
using Shared.Contracts.Localization;
using Shared.Contracts.Mediation;
using Shared.Core.Authorization;
using Shared.Core.Data;
using Shared.Core.Entitlements;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using Shared.Core.Web;
using Testcontainers.PostgreSql;
using UserDirectory.Contracts.Queries;

namespace Agentic.IntegrationTests;

/// <summary>
/// Agentic as it runs in a MULTI-TENANT production deployment: the REAL migrations (tables, unique indexes, forced RLS)
/// applied as the database owner; the REAL module (<c>AddMultiTenancy</c> + <c>AddAgenticModule</c>) connecting as a
/// NON-superuser role, so RLS is enforced; two tenants entered with <see cref="TenantScope"/>; the REAL
/// <see cref="HttpContextService"/>, so "the current user" inside a tool is whoever <c>ActingUser</c> says.
/// Faked boundaries only: the model (the scripted provider, driven per test through <see cref="Board"/>), Hangfire
/// (recording), memberships and the user directory, the clock, and the owning module (test tools on <see cref="World"/>).
/// </summary>
public sealed class AgenticPostgresFixture : IAsyncLifetime
{
    private const string AppRole = "agentic_app";
    private const string AppPassword = "agentic_app_password";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private ServiceProvider? _provider;

    public static readonly TenantInfo TenantA = new(Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001"), "acme", "Acme");
    public static readonly TenantInfo TenantB = new(Guid.Parse("bbbbbbbb-2222-0000-0000-000000000002"), "globex", "Globex");

    public IServiceProvider Services => _provider ?? throw new InvalidOperationException("Not initialized");
    public string AdminConnectionString => _container.GetConnectionString();

    public MutableTimeProvider Clock { get; } = new();
    public RecordingJobClient Jobs { get; } = new();
    public TestCaller Caller { get; } = new();
    public TestWorld World { get; } = new();
    public ScriptBoard Board { get; } = new();
    public RecordingNotifications Notifications { get; } = new();
    public RecordingUserEvents Events { get; } = new();
    public TestQuota Quota { get; } = new();
    public TestEntitlements Entitlements { get; } = new();

    /// <summary>The module's live options (tests lower limits temporarily; <see cref="Reset"/> restores them).</summary>
    public AgenticOptions Options => Services.GetRequiredService<IOptions<AgenticOptions>>().Value;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // 1. The real migrations, as the owner of the schema.
        var adminOptions = new DbContextOptionsBuilder<AgenticDbContext>()
            .UseNpgsql(AdminConnectionString, b => b.UseModuleMigrationsHistory<AgenticDbContext>())
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var admin = new AgenticDbContext(adminOptions))
        {
            await admin.Database.MigrateAsync();
        }

        // 2. The application role: DML only, not a superuser and not the table owner, so RLS applies.
        await AdminExecAsync(
            $"CREATE ROLE {AppRole} LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS; " +
            $"GRANT USAGE ON SCHEMA agentic TO {AppRole}; " +
            $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA agentic TO {AppRole};");
        var appConnection = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = AppRole,
            Password = AppPassword
        }.ConnectionString;

        // 3. The real module, as the app role.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DatabaseDefaults.ConnectionStringName}"] = appConnection,
                ["MultiTenancy:Enabled"] = "true",
                ["Agentic:Enabled"] = "true",
                ["Agentic:DefaultModel"] = "default",
                ["Agentic:Models:default:Provider"] = "scripted",
                ["Agentic:Models:default:ModelId"] = "scripted",
                ["Agentic:Providers:scripted:Kind"] = "Scripted"
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
        services.AddHttpContextAccessor();
        services.AddScoped<IHttpContextService, HttpContextService>();
        services.AddSingleton(Caller);
        services.AddSingleton<INotificationSender>(Notifications);
        services.AddSingleton(World);
        FakeUserDirectory.Register(services, Caller);

        services.AddAgenticModule(configuration);

        // The owning module: its tools, agents and (scripted) model behaviour.
        services.AddAgentTool<GetItemTool>();
        services.AddAgentTool<ReadDocumentTool>();
        services.AddAgentTool<CreateNoteTool>();
        services.AddAgentTool<AppendLogTool>();
        services.AddAgentTool<SendMailTool>();
        services.AddAgentTool<SlowLookupTool>();
        services.AddScoped<IValidator<NoteInput>, NoteInputValidator>();
        foreach (var agent in TestAgents.All)
        {
            services.AddAgent(agent);
            services.AddSingleton<IAgentScript>(new BoardScript(agent.Key, Board));
        }

        services.RemoveAll<IBackgroundJobClient>();
        services.AddSingleton<IBackgroundJobClient>(Jobs);
        services.RemoveAll<IUserEventPublisher>();
        services.AddSingleton<IUserEventPublisher>(Events);
        services.RemoveAll<IQuotaService>();
        services.AddSingleton<IQuotaService>(Quota);
        services.RemoveAll<IEntitlementService>();
        services.AddSingleton<IEntitlementService>(Entitlements);
        services.RemoveAll<IAuthorizationService>();
        services.AddSingleton<IAuthorizationService>(new CallerAuthorization(Caller));

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>One HTTP request by <paramref name="user"/> (default: <see cref="TestCaller.UserId"/>) in <paramref name="tenant"/>.</summary>
    public async Task<T> SendAsync<T>(TenantInfo tenant, IRequest<T> request, Guid? user = null,
        IReadOnlyCollection<string>? apiKeyScopes = null, Guid? apiKeyId = null)
    {
        using (TenantScope.Begin(tenant))
        {
            await using var scope = Services.CreateAsyncScope();
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = Caller.HttpContext(user ?? Caller.UserId, apiKeyScopes, apiKeyId);
            try
            {
                return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
            }
            finally
            {
                accessor.HttpContext = null;
            }
        }
    }

    /// <summary>A command sent by a consumer or a job: the tenant is set, there is no HTTP request and no caller.</summary>
    public Task<T> SendWithoutRequestAsync<T>(TenantInfo tenant, IRequest<T> request) =>
        InTenantAsync(tenant, sp => sp.GetRequiredService<ISender>().Send(request));

    public async Task<T> InTenantAsync<T>(TenantInfo tenant, Func<IServiceProvider, Task<T>> work)
    {
        using (TenantScope.Begin(tenant))
        {
            await using var scope = Services.CreateAsyncScope();
            return await work(scope.ServiceProvider);
        }
    }

    public Task<AgentRunDto> StartAsync(TenantInfo tenant, string agentKey = TestAgents.Assistant, string prompt = "Do it",
        Guid? user = null, IReadOnlyCollection<string>? apiKeyScopes = null, Guid? apiKeyId = null) =>
        SendAsync(tenant, new StartAgentRunCommand(agentKey, prompt), user, apiKeyScopes, apiKeyId);

    /// <summary>Runs the queued Hangfire job exactly like the agentic server would (the tenant restored, nothing else).</summary>
    public Task RunAsync(TenantInfo tenant, Guid runId, CancellationToken cancellationToken = default) =>
        InTenantAsync(tenant, async sp =>
        {
            await sp.GetRequiredService<ExecuteAgentRunJob>().ExecuteAsync(runId, cancellationToken);
            return true;
        });

    /// <summary>The recurring stall sweep, for one tenant (the job fans out over the tenants itself).</summary>
    public Task<int> SweepAsync(TenantInfo tenant) =>
        InTenantAsync(tenant, sp => sp.GetRequiredService<RequeueStalledAgentRunsJob>().RequeueCurrentTenantAsync(null));

    /// <summary>The recurring approval-expiry job, for one tenant.</summary>
    public Task<int> ExpireApprovalsAsync(TenantInfo tenant) =>
        InTenantAsync(tenant, sp => sp.GetRequiredService<ExpireAgentApprovalsJob>().ExpireCurrentTenantAsync(null));

    /// <summary>The approval request of a run (the one request a suspended run waits for).</summary>
    public Task<AgentApproval> ApprovalOfAsync(TenantInfo tenant, Guid runId) =>
        InTenantAsync(tenant, sp => sp.GetRequiredService<AgenticDbContext>().Approvals.AsNoTracking()
            .SingleAsync(a => a.RunId == runId));

    /// <summary>A person approves exactly the payload on record, then the queued job runs.</summary>
    public async Task<AgentApprovalDto> ApproveAndRunAsync(TenantInfo tenant, Guid runId, Guid? approver = null)
    {
        var approval = await ApprovalOfAsync(tenant, runId);
        var decided = await SendAsync(tenant, new ApproveAgentActionCommand(approval.Id, approval.PayloadDigest), approver);
        await RunAsync(tenant, runId);
        return decided;
    }

    public Task<AgentRun> RunOfAsync(TenantInfo tenant, Guid id) =>
        InTenantAsync(tenant, sp => sp.GetRequiredService<AgenticDbContext>().Runs.AsNoTracking().SingleAsync(r => r.Id == id));

    public Task<List<AgentRunStep>> StepsOfAsync(TenantInfo tenant, Guid runId) =>
        InTenantAsync(tenant, sp => sp.GetRequiredService<AgenticDbContext>().Steps.AsNoTracking()
            .Where(s => s.RunId == runId).OrderBy(s => s.Sequence).ToListAsync());

    /// <summary>Test isolation: the tests share one database and the fakes.</summary>
    public void Reset()
    {
        Caller.Reset();
        World.Reset();
        Board.Reset();
        Notifications.Clear();
        Events.Clear();
        Quota.Reset();
        Entitlements.Limits.Clear();
        // The tests share one database, so runs other tests left waiting must not count against these two guards.
        Options.Limits.MaxConcurrentRunsPerTenant = 0;
        Options.Triggers.MaxAutomaticRunsPerHour = 0;
        Options.ProgressIntervalMilliseconds = 0; // every checkpoint is sent unless a test asks for throttling
        var defaults = new AgentRunLimitOptions();
        Options.Limits.MaxModelTurns = defaults.MaxModelTurns;
        Options.Limits.MaxToolCalls = defaults.MaxToolCalls;
        Options.Limits.MaxToolCallsPerTurn = defaults.MaxToolCallsPerTurn;
        Options.Limits.MaxTokens = defaults.MaxTokens;
        Options.Limits.MaxToolResultBytes = defaults.MaxToolResultBytes;
        Options.Limits.MaxToolArgumentsBytes = defaults.MaxToolArgumentsBytes;
        Options.MaxAttempts = 3;
        Options.StalledAfterMinutes = 10;
        Options.Approvals.ExpireAfterHours = 72;
        Options.Approvals.RequireDifferentApprover = false;
        Options.Enabled = true;
    }

    /// <summary>Integration events of a run, as written to the transactional outbox (message types, oldest first).</summary>
    public async Task<List<string>> OutboxEventsAsync(Guid runId)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select message_type from agentic.outbox_messages where payload::text like @run order by occurred_on, id", connection);
        command.Parameters.AddWithValue("run", $"%{runId}%");
        var types = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            types.Add(reader.GetString(0));

        return types;
    }

    /// <summary>SQL as the database owner (bypasses RLS): verification across tenants.</summary>
    public async Task<T?> AdminScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
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
public sealed class AgenticPostgresCollection : ICollectionFixture<AgenticPostgresFixture>
{
    public const string Name = "agentic-postgres";
}

// ----- the owning module: a tiny in-memory business domain with agent tools ---------------------------------------

/// <summary>In-memory business state per tenant, and what the tools observed about who called them.</summary>
public sealed class TestWorld
{
    public sealed record Observation(string Tool, Guid? TenantId, Guid? UserId, string? CorrelationId, BranchScope Branches,
        string IdempotencyKey);

    private readonly ConcurrentQueue<Observation> _observations = new();

    /// <summary>Notes by (tenant, idempotency key): a replayed call overwrites its own note instead of adding one.</summary>
    public ConcurrentDictionary<(Guid Tenant, string Key), string> Notes { get; } = new();

    public ConcurrentQueue<string> Log { get; } = new();
    public ConcurrentQueue<string> Mail { get; } = new();

    public IReadOnlyList<Observation> Observations => _observations.ToList();

    public int Executions(string tool) => _observations.Count(o => o.Tool == tool);

    /// <summary>Throws this from the named tool, once (an infrastructure failure in the middle of a run).</summary>
    public (string Tool, Exception Exception)? FailOnce { get; set; }

    /// <summary>Runs inside a tool after its work (e.g. the membership is removed, a second worker shows up).</summary>
    public Func<string, Task>? AfterTool { get; set; }

    /// <summary>What <c>test.read_document</c> returns: content the platform does not control.</summary>
    public string Document { get; set; } = "Quarterly report.";

    public void Observe(Observation observation) => _observations.Enqueue(observation);

    public void ThrowIfAsked(string tool)
    {
        if (FailOnce is { } failure && failure.Tool == tool)
        {
            FailOnce = null;
            throw failure.Exception;
        }
    }

    public void Reset()
    {
        _observations.Clear();
        Notes.Clear();
        Log.Clear();
        Mail.Clear();
        FailOnce = null;
        AfterTool = null;
        Document = "Quarterly report.";
    }
}

/// <summary>Shared plumbing of the test tools: record who the tool ran as, exactly as a real handler would see it.</summary>
public abstract class TestTool(TestWorld world, ITenantContext tenant, IHttpContextService http)
{
    protected TestWorld World { get; } = world;

    protected async Task<Guid> EnterAsync(string tool, AgentToolContext context)
    {
        World.Observe(new TestWorld.Observation(tool, tenant.TenantId, http.GetUserIdAsGuid(), http.GetCorrelationId(),
            tenant.Branches, context.IdempotencyKey));
        World.ThrowIfAsked(tool);
        if (World.AfterTool is { } hook)
            await hook(tool);
        return tenant.TenantId!.Value;
    }
}

public sealed record ItemInput(string Code);

public sealed record ItemOutput(string Code, string Name);

[AgentTool(Name, "Looks up an item by its code.", AllowAnyMember = true)]
public sealed class GetItemTool(TestWorld world, ITenantContext tenant, IHttpContextService http)
    : TestTool(world, tenant, http), IAgentTool<ItemInput, ItemOutput>
{
    public const string Name = "test.get_item";

    public async Task<ItemOutput> ExecuteAsync(ItemInput input, AgentToolContext context, CancellationToken cancellationToken)
    {
        await EnterAsync(Name, context);
        return input.Code == "missing"
            ? throw new Shared.Core.Exceptions.NotFoundException("Item", input.Code)
            : new ItemOutput(input.Code, $"Item {input.Code}");
    }
}

public sealed record DocumentOutput(string Content);

[AgentTool(Name, "Reads the attached document.", AllowAnyMember = true, ReturnsUntrustedContent = true)]
public sealed class ReadDocumentTool(TestWorld world, ITenantContext tenant, IHttpContextService http)
    : TestTool(world, tenant, http), IAgentTool<ItemInput, DocumentOutput>
{
    public const string Name = "test.read_document";

    public async Task<DocumentOutput> ExecuteAsync(ItemInput input, AgentToolContext context, CancellationToken cancellationToken)
    {
        await EnterAsync(Name, context);
        return new DocumentOutput(World.Document);
    }
}

public sealed record NoteInput(string Title);

public sealed record NoteOutput(string Key);

public sealed class NoteInputValidator : AbstractValidator<NoteInput>
{
    public NoteInputValidator() => RuleFor(x => x.Title).NotEmpty().WithMessage("A title is required.");
}

[AgentTool(Name, "Creates a draft note.", Risk = AgentToolRisk.LowRiskMutation, Permission = TenantPermissions.ProductsManage)]
public sealed class CreateNoteTool(TestWorld world, ITenantContext tenant, IHttpContextService http)
    : TestTool(world, tenant, http), IAgentTool<NoteInput, NoteOutput>
{
    public const string Name = "test.create_note";

    public async Task<NoteOutput> ExecuteAsync(NoteInput input, AgentToolContext context, CancellationToken cancellationToken)
    {
        var tenantId = await EnterAsync(Name, context);
        World.Notes[(tenantId, context.IdempotencyKey)] = input.Title;
        return new NoteOutput(context.IdempotencyKey);
    }
}

[AgentTool(Name, "Appends a line to the activity log.", Risk = AgentToolRisk.LowRiskMutation,
    Permission = TenantPermissions.ProductsManage, Idempotent = false)]
public sealed class AppendLogTool(TestWorld world, ITenantContext tenant, IHttpContextService http)
    : TestTool(world, tenant, http), IAgentTool<NoteInput, NoteOutput>
{
    public const string Name = "test.append_log";

    public async Task<NoteOutput> ExecuteAsync(NoteInput input, AgentToolContext context, CancellationToken cancellationToken)
    {
        await EnterAsync(Name, context);
        World.Log.Enqueue(input.Title);
        return new NoteOutput(context.IdempotencyKey);
    }
}

[AgentTool(Name, "Sends an email to a customer.", Risk = AgentToolRisk.ExternalOrIrreversible,
    Permission = TenantPermissions.WebhooksManage, Idempotent = false)]
public sealed class SendMailTool(TestWorld world, ITenantContext tenant, IHttpContextService http)
    : TestTool(world, tenant, http), IAgentTool<NoteInput, NoteOutput>
{
    public const string Name = "test.send_mail";

    public async Task<NoteOutput> ExecuteAsync(NoteInput input, AgentToolContext context, CancellationToken cancellationToken)
    {
        await EnterAsync(Name, context);
        World.Mail.Enqueue(input.Title);
        return new NoteOutput(context.IdempotencyKey);
    }
}

[AgentTool(Name, "A lookup that takes too long.", AllowAnyMember = true, TimeoutSeconds = 1)]
public sealed class SlowLookupTool(TestWorld world, ITenantContext tenant, IHttpContextService http)
    : TestTool(world, tenant, http), IAgentTool<ItemInput, ItemOutput>
{
    public const string Name = "test.slow_lookup";

    public async Task<ItemOutput> ExecuteAsync(ItemInput input, AgentToolContext context, CancellationToken cancellationToken)
    {
        await EnterAsync(Name, context);
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        return new ItemOutput(input.Code, "never");
    }
}

public sealed record Summary(string Title, int Items);

public static class TestAgents
{
    public const string Assistant = "test.assistant";
    public const string Typed = "test.typed";
    public const string Restricted = "test.restricted";
    public const string Automatic = "test.automatic";

    public static readonly AgentDefinition[] All =
    [
        new()
        {
            Key = Assistant,
            Name = new LocalizedText("Assistant", "مساعد"),
            Instructions = "Help the user with items and notes.",
            Autonomy = AgentAutonomy.Execute,
            Tools =
            [
                GetItemTool.Name, ReadDocumentTool.Name, CreateNoteTool.Name, AppendLogTool.Name, SendMailTool.Name,
                SlowLookupTool.Name
            ]
        },
        new()
        {
            Key = Typed,
            Name = new LocalizedText("Summarizer", "ملخِّص"),
            Instructions = "Summarize the items.",
            Autonomy = AgentAutonomy.Prepare,
            OutputType = typeof(Summary),
            Tools = [GetItemTool.Name, CreateNoteTool.Name]
        },
        new()
        {
            Key = Restricted,
            Name = new LocalizedText("Restricted", "مقيَّد"),
            Instructions = "Only for webhook managers.",
            RequiredPermission = TenantPermissions.WebhooksManage,
            Tools = [GetItemTool.Name]
        },
        new()
        {
            Key = Automatic,
            Name = new LocalizedText("Automatic", "تلقائي"),
            Instructions = "React to events.",
            Autonomy = AgentAutonomy.Prepare,
            AllowedTriggers = [AgentTriggerKind.Event, AgentTriggerKind.Schedule],
            Tools = [GetItemTool.Name, CreateNoteTool.Name]
        }
    ];
}

/// <summary>The scripted model of every test agent: each test sets what "the model" answers.</summary>
public sealed class ScriptBoard
{
    private int _turns;

    public Func<ScriptedTurn, ScriptedReply> Next { get; set; } = _ => ScriptedReply.Say("Done.");

    /// <summary>Model requests answered since the last reset.</summary>
    public int Turns => _turns;

    public ScriptedReply Answer(ScriptedTurn turn)
    {
        Interlocked.Increment(ref _turns);
        return Next(turn);
    }

    /// <summary>Answers with each reply in order, then "Done.".</summary>
    public void Play(params ScriptedReply[] replies) =>
        Next = turn => turn.TurnNumber <= replies.Length ? replies[turn.TurnNumber - 1] : ScriptedReply.Say("Done.");

    public void Reset()
    {
        _turns = 0;
        Next = _ => ScriptedReply.Say("Done.");
    }
}

public sealed class BoardScript(string agentKey, ScriptBoard board) : IAgentScript
{
    public string AgentKey => agentKey;

    public ScriptedReply Next(ScriptedTurn turn) => board.Answer(turn);
}

// ----- fakes ------------------------------------------------------------------------------------------------------

public sealed class MutableTimeProvider : TimeProvider
{
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

/// <summary>The users of the tests: who is calling, which tenant permissions are missing, who lost access.</summary>
public sealed class TestCaller
{
    public static readonly Guid Alice = Guid.Parse("11111111-aaaa-0000-0000-000000000001");
    public static readonly Guid Bob = Guid.Parse("22222222-bbbb-0000-0000-000000000002");

    public Guid UserId { get; set; } = Alice;

    /// <summary>Tenant permissions NO user holds (every other permission is held by every member).</summary>
    public HashSet<string> Denied { get; } = new(StringComparer.Ordinal);

    /// <summary>Tenant permissions denied to one user only.</summary>
    public HashSet<(Guid User, string Permission)> DeniedTo { get; } = [];

    /// <summary>Users whose membership ended (the resolver returns none).</summary>
    public HashSet<Guid> NotMembers { get; } = [];

    /// <summary>Users deactivated in the user directory.</summary>
    public HashSet<Guid> Inactive { get; } = [];

    /// <summary>Branch restriction of every membership (null = unrestricted).</summary>
    public IReadOnlyList<Guid>? AllowedBranches { get; set; }

    public bool Holds(Guid user, string permission) =>
        !Denied.Contains(permission) && !DeniedTo.Contains((user, permission));

    public void Reset()
    {
        UserId = Alice;
        Denied.Clear();
        DeniedTo.Clear();
        NotMembers.Clear();
        Inactive.Clear();
        AllowedBranches = null;
    }

    public HttpContext HttpContext(Guid user, IReadOnlyCollection<string>? apiKeyScopes = null, Guid? apiKeyId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ToString()) };
        if (apiKeyScopes is not null)
        {
            claims.Add(new Claim(ApiKeyClaimTypes.KeyId, (apiKeyId ?? Guid.NewGuid()).ToString()));
            claims.AddRange(apiKeyScopes.Select(scope => new Claim(ApiKeyClaimTypes.Scope, scope)));
        }

        return new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
    }
}

/// <summary>Memberships as the run re-reads them before every tool call.</summary>
public sealed class CallerMemberships(TestCaller caller) : ITenantMembershipResolver
{
    public Task<TenantMembershipInfo?> GetMembershipAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (caller.NotMembers.Contains(userId))
            return Task.FromResult<TenantMembershipInfo?>(null);
        var permissions = TenantPermissions.All.Where(p => caller.Holds(userId, p)).ToHashSet(StringComparer.Ordinal);
        return Task.FromResult<TenantMembershipInfo?>(new TenantMembershipInfo(tenantId, userId, false, permissions,
            new BranchScope(caller.AllowedBranches), "stamp"));
    }

    public Task<IReadOnlyList<Guid>> GetTenantIdsOfUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task InvalidateAsync(Guid? tenantId = null, Guid? userId = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>The endpoint policies of the request: membership permissions AND, for an API key, its scopes.</summary>
public sealed class CallerAuthorization(TestCaller caller) : IAuthorizationService
{
    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource,
        IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(AuthorizationResult.Success());

    public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
    {
        var permission = policyName[TenantPermissions.PolicyPrefix.Length..];
        var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var allowed = caller.Holds(userId, permission) && ApiKeyPrincipal.Allows(user, permission);
        return Task.FromResult(allowed ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }
}

/// <summary>The user directory (Identity) stand-in, through the same contracts the module uses.</summary>
public static class FakeUserDirectory
{
    public static void Register(IServiceCollection services, TestCaller caller)
    {
        services.AddTransient<IRequestHandler<GetUserProfileQuery, UserProfileDto?>>(_ => new Profile(caller));
        services.AddTransient<IRequestHandler<GetUserRolesQuery, IReadOnlyList<string>>>(_ => new Roles());
    }

    private sealed class Profile(TestCaller caller) : IRequestHandler<GetUserProfileQuery, UserProfileDto?>
    {
        public Task<UserProfileDto?> Handle(GetUserProfileQuery request, CancellationToken cancellationToken) =>
            Task.FromResult<UserProfileDto?>(new UserProfileDto(request.UserId, "Test user", null, null,
                !caller.Inactive.Contains(request.UserId)));
    }

    private sealed class Roles : IRequestHandler<GetUserRolesQuery, IReadOnlyList<string>>
    {
        public Task<IReadOnlyList<string>> Handle(GetUserRolesQuery request, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}

public sealed class RecordingJobClient : IBackgroundJobClient
{
    public sealed record Recorded(Job Job, IState State)
    {
        public bool IsScheduled => State is ScheduledState;
    }

    private readonly ConcurrentQueue<Recorded> _jobs = new();

    public IReadOnlyList<Recorded> For(Guid runId) =>
        _jobs.Where(j => j.Job.Args.Count > 0 && j.Job.Args[0] is Guid id && id == runId).ToList();

    public string Create(Job job, IState state)
    {
        _jobs.Enqueue(new Recorded(job, state));
        return Guid.NewGuid().ToString("N");
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}

/// <summary>The notification module stand-in: every in-app notification sent, in order.</summary>
public sealed class RecordingNotifications : INotificationSender
{
    private readonly ConcurrentQueue<(Guid User, InAppContent Content, Guid? CorrelationId)> _sent = new();

    public IReadOnlyList<(Guid User, InAppContent Content, Guid? CorrelationId)> InApp => _sent.ToList();

    public void Clear() => _sent.Clear();

    public INotificationBuilder For(Guid userId, NotificationCulture culture = NotificationCulture.En) => new Builder(this, userId);

    public INotificationBuilder ToEmail(string email, NotificationCulture culture = NotificationCulture.En, Guid? userId = null) =>
        new Builder(this, userId ?? Guid.Empty);

    private sealed class Builder(RecordingNotifications owner, Guid user) : INotificationBuilder
    {
        private InAppContent? _content;
        private Guid? _correlationId;

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

        public INotificationBuilder WithCorrelationId(Guid correlationId)
        {
            _correlationId = correlationId;
            return this;
        }

        public Task<Guid> SendAsync(NotificationChannel? forcedChannel = null, CancellationToken ct = default)
        {
            if (_content is not null)
                owner._sent.Enqueue((user, _content, _correlationId));
            return Task.FromResult(_correlationId ?? Guid.NewGuid());
        }
    }
}

/// <summary>The hub stand-in: every live event sent, in order.</summary>
public sealed class RecordingUserEvents : IUserEventPublisher
{
    private readonly ConcurrentQueue<(Guid User, UserEvent Event)> _events = new();

    public IReadOnlyList<(Guid User, UserEvent Event)> For(Guid runId) =>
        _events.Where(e => e.Event.Data.TryGetValue("runId", out var id) && Equals(id, runId)).ToList();

    public Task PublishAsync(Guid userId, UserEvent userEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue((userId, userEvent));
        return Task.CompletedTask;
    }

    public void Clear() => _events.Clear();
}

/// <summary>Plan quotas of the current subscriber: unlimited unless a limit is set for the key.</summary>
public sealed class TestQuota : IQuotaService
{
    private readonly Dictionary<string, long> _used = new(StringComparer.Ordinal);

    public Dictionary<string, long> Limits { get; } = new(StringComparer.Ordinal);

    public long Used(string key)
    {
        lock (_used)
            return _used.GetValueOrDefault(key);
    }

    public Task<QuotaResult> TryConsumeAsync(string key, long quantity = 1, CancellationToken cancellationToken = default)
    {
        lock (_used)
        {
            var used = _used.GetValueOrDefault(key);
            if (Limits.TryGetValue(key, out var limit) && used + quantity > limit)
                return Task.FromResult(new QuotaResult(false, used, limit));
            _used[key] = used + quantity;
            return Task.FromResult(new QuotaResult(true, used + quantity, Limits.TryGetValue(key, out var l) ? l : null));
        }
    }

    public void Reset()
    {
        lock (_used)
        {
            _used.Clear();
            Limits.Clear();
        }
    }
}

/// <summary>The plan of the current subscriber: every feature, and the numeric limits a test sets.</summary>
public sealed class TestEntitlements : IEntitlementService
{
    public Dictionary<string, int> Limits { get; } = new(StringComparer.Ordinal);

    public Task<bool> HasFeatureAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<int?> GetLimitAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Limits.TryGetValue(key, out var limit) ? limit : (int?)null);
}
