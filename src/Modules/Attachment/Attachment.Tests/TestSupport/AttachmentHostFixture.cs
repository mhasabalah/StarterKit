using System.Collections.Concurrent;
using Attachment.Data;
using Attachment.Domains.Attachments.Events;
using Attachment.Infrastructure.Storage;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Shared.Contracts.Mediation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shared.Core.Web;
using Testcontainers.PostgreSql;

namespace Attachment.Tests.TestSupport;

/// <summary>
/// The real Attachment module composition (<c>AddAttachmentModule</c>: mediator pipeline, repositories, jobs,
/// reaper, sweeper, unit-of-work runner, domain-event dispatch) over a Testcontainers PostgreSQL database, with:
/// <list type="bullet">
/// <item>Local storage in a temp folder, wrapped by <see cref="FlakyStorage"/> (inject upload/delete failures,
/// observe uploads);</item>
/// <item>a <see cref="TestClock"/> (<see cref="TimeProvider"/>) shared by handlers, the audit interceptor and the
/// sweeper;</item>
/// <item>a recording Hangfire client (<see cref="Jobs"/>) and a recording event handler (<see cref="Events"/>);</item>
/// <item>a switchable caller (<see cref="SendAsync{T}"/> takes the caller per request).</item>
/// </list>
/// Hosted services are registered but never started (no Hangfire server).
/// </summary>
public sealed class AttachmentHostFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly TempDirectory _storageRoot = new();
    private ServiceProvider? _provider;

    public TestClock Clock { get; } = new();
    public RecordingJobClient Jobs { get; } = new();
    public EventLog Events { get; } = new();
    public CallerAccessor Callers { get; } = new();
    public FlakyStorage Storage { get; private set; } = default!;
    public string StorageRoot => _storageRoot.Path;

    public IServiceProvider Services => _provider ?? throw new InvalidOperationException("Not initialized");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:starterkit"] = _container.GetConnectionString(),
                ["Attachment:Storage:Provider"] = "Local",
                ["Attachment:Storage:Local:UploadPath"] = _storageRoot.Path,
                ["Attachment:Download:SigningKey"] = "unit-test-signing-key-0123456789abcdef",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAttachmentModule(configuration);

        // Test doubles.
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton(Callers);
        services.AddScoped<IHttpContextService>(_ => Callers.Current.ToHttpContext());
        services.RemoveAll<IBackgroundJobClient>();
        services.AddSingleton<IBackgroundJobClient>(Jobs);
        services.AddSingleton(Events);
        services.AddTransient<INotificationHandler<AttachmentUploadedEvent>, EventRecorder<AttachmentUploadedEvent>>();
        services.AddTransient<INotificationHandler<AttachmentDeletedEvent>, EventRecorder<AttachmentDeletedEvent>>();
        services.AddTransient<INotificationHandler<AttachmentThumbnailGeneratedEvent>, EventRecorder<AttachmentThumbnailGeneratedEvent>>();
        services.AddTransient<INotificationHandler<PendingAttachmentThumbnailGeneratedEvent>, EventRecorder<PendingAttachmentThumbnailGeneratedEvent>>();

        Storage = new FlakyStorage(TestServices.LocalStorage(_storageRoot.Path));
        services.RemoveAll<IStorageService>();
        services.AddSingleton<IStorageService>(Storage);

        _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        await using var scope = _provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AttachmentDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null) await _provider.DisposeAsync();
        await _container.DisposeAsync();
        _storageRoot.Dispose();
    }

    /// <summary>Resets clock, fault injection and recordings between tests.</summary>
    public void Reset()
    {
        Clock.Reset();
        Storage.Reset();
        Jobs.Clear();
        Events.Clear();
        Callers.Current = Caller.Anonymous();
    }

    /// <summary>Sends a mediator request as <paramref name="caller"/> in its own scope (= one HTTP request).</summary>
    public async Task<T> SendAsync<T>(Caller caller, IRequest<T> request)
    {
        Callers.Current = caller;
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    /// <summary>Runs <paramref name="work"/> in a fresh scope (e.g. to resolve a job or query the DB).</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public Task InScopeAsync(Func<IServiceProvider, Task> work) =>
        InScopeAsync(async sp => { await work(sp); return true; });

    /// <summary>Raw DB access ignoring the read filter.</summary>
    public Task<T> DbAsync<T>(Func<AttachmentDbContext, Task<T>> query) =>
        InScopeAsync(sp => query(sp.GetRequiredService<AttachmentDbContext>()));

    public bool FileExists(string storagePath) =>
        File.Exists(Path.Combine([StorageRoot, .. storagePath.Split('/')]));
}

[CollectionDefinition(Name)]
public sealed class AttachmentHostCollection : ICollectionFixture<AttachmentHostFixture>
{
    public const string Name = "attachment-host";
}

/// <summary>Mutable caller identity resolved per scope.</summary>
public sealed class CallerAccessor
{
    public Caller Current { get; set; } = Caller.Anonymous();
}

/// <summary>A <see cref="TimeProvider"/> that follows the real clock plus an adjustable offset.</summary>
public sealed class TestClock : TimeProvider
{
    private TimeSpan _offset;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;

    public void Advance(TimeSpan by) => _offset += by;

    public void Reset() => _offset = TimeSpan.Zero;
}

/// <summary>Records every Hangfire job the code under test enqueues (nothing runs).</summary>
public sealed class RecordingJobClient : IBackgroundJobClient
{
    private readonly ConcurrentQueue<Job> _jobs = new();

    public IReadOnlyList<Job> All => _jobs.ToList();

    public IReadOnlyList<Job> Of<TJob>(string method) =>
        _jobs.Where(j => j.Type == typeof(TJob) && j.Method.Name == method).ToList();

    public void Clear() => _jobs.Clear();

    public string Create(Job job, IState state)
    {
        _jobs.Enqueue(job);
        return Guid.NewGuid().ToString("N");
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}

public sealed class EventLog
{
    private readonly ConcurrentQueue<object> _events = new();

    public void Add(object e) => _events.Enqueue(e);

    public IReadOnlyList<T> Of<T>() => _events.OfType<T>().ToList();

    public void Clear() => _events.Clear();
}

public sealed class EventRecorder<T>(EventLog log) : INotificationHandler<T> where T : INotification
{
    public Task Handle(T notification, CancellationToken cancellationToken)
    {
        log.Add(notification);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Local storage decorator with fault injection. Implements <see cref="IStorageListing"/> by forwarding, like the
/// real providers.
/// </summary>
public sealed class FlakyStorage(LocalFileStorageService inner) : IStorageService, IStorageListing
{
    /// <summary>Uploads write the blob completely and THEN throw (worst case: bytes stored, call failed).</summary>
    public bool FailUploadsAfterWrite { get; set; }

    /// <summary>Paths (or every path when "*" is present) whose deletion fails.</summary>
    public HashSet<string> FailDeletes { get; } = [];

    /// <summary>Called after a successful blob write, before UploadToPathAsync returns.</summary>
    public Func<string, Task>? AfterUpload { get; set; }

    public LocalFileStorageService Inner => inner;

    public void Reset()
    {
        FailUploadsAfterWrite = false;
        FailDeletes.Clear();
        AfterUpload = null;
    }

    public bool ServesThroughApplication => inner.ServesThroughApplication;

    public async Task<StorageResult> UploadToPathAsync(Stream fileStream, string storagePath, string contentType,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.UploadToPathAsync(fileStream, storagePath, contentType, cancellationToken);
        if (FailUploadsAfterWrite)
            throw new Attachment.Domains.Attachments.Exceptions.FileStorageException("injected upload failure");
        if (AfterUpload is not null)
            await AfterUpload(storagePath);
        return result;
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default) =>
        inner.OpenReadAsync(storagePath, cancellationToken);

    public Task DeleteAsync(string storagePath, CancellationToken cancellationToken = default) =>
        ShouldFail(storagePath)
            ? throw new Attachment.Domains.Attachments.Exceptions.FileStorageException("injected delete failure")
            : inner.DeleteAsync(storagePath, cancellationToken);

    public async Task<BatchDeleteResult> DeleteBatchAsync(IEnumerable<string> storagePaths,
        CancellationToken cancellationToken = default)
    {
        var paths = storagePaths.Distinct().ToList();
        var failed = paths.Where(ShouldFail).ToList();
        var result = await inner.DeleteBatchAsync(paths.Except(failed), cancellationToken);
        failed.AddRange(result.Failed);
        return failed.Count == 0 ? BatchDeleteResult.Success : new BatchDeleteResult(failed);
    }

    public Task<bool> ExistsAsync(string storagePath, CancellationToken cancellationToken = default) =>
        inner.ExistsAsync(storagePath, cancellationToken);

    public Task<StorageResult> CopyAsync(string sourceStoragePath, string destinationStoragePath,
        CancellationToken cancellationToken = default) =>
        inner.CopyAsync(sourceStoragePath, destinationStoragePath, cancellationToken);

    public Task<StorageResult> MoveAsync(string sourceStoragePath, string destinationStoragePath,
        CancellationToken cancellationToken = default) =>
        inner.MoveAsync(sourceStoragePath, destinationStoragePath, cancellationToken);

    public Task<string> GetReadUrlAsync(string storagePath, TimeSpan ttl, ReadUrlOptions? options = null,
        CancellationToken cancellationToken = default) =>
        inner.GetReadUrlAsync(storagePath, ttl, options, cancellationToken);

    public IAsyncEnumerable<StoredObjectInfo> ListAsync(string? prefix = null,
        CancellationToken cancellationToken = default) =>
        inner.ListAsync(prefix, cancellationToken);

    private bool ShouldFail(string path) => FailDeletes.Contains("*") || FailDeletes.Contains(path);
}
