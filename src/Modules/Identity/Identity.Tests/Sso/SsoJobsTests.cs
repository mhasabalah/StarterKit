using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Identity.Contracts.Sso;
using Identity.Data.Repository.Sso;
using Identity.Domains.Sso.Events;
using Identity.Domains.Sso.Jobs;
using Identity.Domains.Sso.Models;
using Identity.Options;
using Identity.Services.Sso;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Contracts.Mediation;
using Shared.Core.Data;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Identity.Tests.Sso;

/// <summary>The branches of the two recurring SSO jobs: isolation per connection, race handling, exactly-once alerts.</summary>
public sealed class SsoJobsTests : IDisposable
{
    private const string EntityId = "https://idp.acme.example/saml";
    private const string SsoUrl = "https://idp.acme.example/sso";

    private readonly X509Certificate2 _current = Certificate("CN=current", 200);
    private readonly X509Certificate2 _next = Certificate("CN=next", 400);
    private readonly X509Certificate2 _expiring = Certificate("CN=expiring", 20);

    public void Dispose()
    {
        _current.Dispose();
        _next.Dispose();
        _expiring.Dispose();
    }

    // ----- RefreshSamlMetadataJob -----

    [Fact]
    public async Task One_failing_connection_does_not_stop_the_others()
    {
        var broken = Guid.NewGuid();
        var healthy = SamlConnection(_current);
        var repository = Repository();
        repository.Setup(r => r.GetRefreshableSamlIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([broken, healthy.Id]);
        repository.Setup(r => r.GetByIdAsync(broken, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        repository.Setup(r => r.GetByIdAsync(healthy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(healthy);
        var logger = new ListLogger<RefreshSamlMetadataJob>();

        await RefreshJob(repository, Metadata(_next), logger).ExecuteAsync();

        healthy.SamlIdpCertificates.Should().Equal([Base64(_next)], "the healthy connection was still refreshed");
        repository.Verify(r => r.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task A_concurrency_conflict_is_logged_and_skipped_and_the_next_connection_runs()
    {
        var raced = SamlConnection(_current);
        var healthy = SamlConnection(_current);
        var repository = Repository();
        repository.Setup(r => r.GetRefreshableSamlIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([raced.Id, healthy.Id]);
        repository.Setup(r => r.GetByIdAsync(raced.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raced);
        repository.Setup(r => r.GetByIdAsync(healthy.Id, It.IsAny<CancellationToken>())).ReturnsAsync(healthy);
        var saves = 0;
        repository.Setup(r => r.SaveAsync(It.IsAny<CancellationToken>())).Returns(() =>
            ++saves == 1 ? throw new DbUpdateConcurrencyException("stale") : Task.CompletedTask);
        var logger = new ListLogger<RefreshSamlMetadataJob>();

        await RefreshJob(repository, Metadata(_next), logger).ExecuteAsync();

        saves.Should().Be(2, "the second connection was still saved");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Information && e.Message.Contains("concurrency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_refresh_job_does_nothing_when_sso_is_disabled()
    {
        var repository = Repository();

        await RefreshJob(repository, Metadata(_next), new ListLogger<RefreshSamlMetadataJob>(), enabled: false).ExecuteAsync();

        repository.Verify(r => r.GetRefreshableSamlIdsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- CheckSamlCertificateExpiryJob -----

    [Fact]
    public async Task The_alert_is_published_exactly_once_across_two_runs()
    {
        var connection = SamlConnection(_expiring);
        var state = new ClaimState();
        var repository = ExpiryRepository(state, connection);
        var publisher = new RecordingPublisher();
        var job = ExpiryJob(repository, publisher);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        var published = publisher.Published.OfType<SsoCertificateExpiringEvent>().Should().ContainSingle().Subject;
        published.ConnectionId.Should().Be(connection.Id);
        published.TenantId.Should().Be(connection.TenantId);
        published.DaysLeft.Should().BeInRange(18, 20);
    }

    [Fact]
    public async Task A_failed_delivery_releases_the_claim_so_the_next_run_retries()
    {
        var connection = SamlConnection(_expiring);
        var state = new ClaimState();
        var repository = ExpiryRepository(state, connection);
        var publisher = new RecordingPublisher { Fail = true };
        var job = ExpiryJob(repository, publisher, out var logger);

        await job.ExecuteAsync(); // does not throw: the failure is logged and the claim released

        state.NotAfter.Should().BeNull("the claim was released");
        logger.Entries.Should().Contain(e => e.Level == LogLevel.Error);

        publisher.Fail = false;
        await job.ExecuteAsync();

        publisher.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task One_failing_connection_does_not_stop_the_other_alerts()
    {
        var broken = Guid.NewGuid();
        var healthy = SamlConnection(_expiring);
        var state = new ClaimState();
        var repository = ExpiryRepository(state, healthy);
        repository.Setup(r => r.GetActiveSamlIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([broken, healthy.Id]);
        repository.Setup(r => r.GetByIdAsync(broken, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        var publisher = new RecordingPublisher();

        await ExpiryJob(repository, publisher).ExecuteAsync();

        publisher.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task A_concurrency_conflict_during_the_check_is_logged_and_skipped()
    {
        var raced = SamlConnection(_expiring);
        var repository = Repository();
        repository.Setup(r => r.GetActiveSamlIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([raced.Id]);
        repository.Setup(r => r.GetByIdAsync(raced.Id, It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateConcurrencyException("stale"));
        var publisher = new RecordingPublisher();

        await ExpiryJob(repository, publisher, out var logger).ExecuteAsync();

        publisher.Published.Should().BeEmpty();
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Certificates_that_are_not_close_to_expiry_raise_nothing()
    {
        var connection = SamlConnection(_next);
        var repository = ExpiryRepository(new ClaimState(), connection);
        var publisher = new RecordingPublisher();

        await ExpiryJob(repository, publisher).ExecuteAsync();

        publisher.Published.Should().BeEmpty();
        repository.Verify(r => r.TryClaimExpiryAlertAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_expiry_job_respects_its_switch()
    {
        var repository = Repository();

        await ExpiryJob(repository, new RecordingPublisher(), alertsEnabled: false).ExecuteAsync();

        repository.Verify(r => r.GetActiveSamlIdsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- helpers -----

    private sealed class ClaimState
    {
        public DateTime? NotAfter { get; set; }

        public int? Days { get; set; }
    }

    private sealed class RecordingPublisher : IPublisher
    {
        public List<object> Published { get; } = [];

        public bool Fail { get; set; }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new InvalidOperationException("delivery failed");
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification =>
            Publish((object)notification, cancellationToken);
    }

    private sealed class FakeRunner(IServiceProvider services) : IUnitOfWorkRunner
    {
        public Task RunAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken = default) =>
            work(services, cancellationToken);

        public Task<TResult> RunAsync<TResult>(
            Func<IServiceProvider, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default) =>
            work(services, cancellationToken);
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }

    private static Mock<ISsoConnectionRepository> Repository() => new();

    /// <summary>A repository whose claim behaves like the conditional UPDATE: first caller wins, release undoes it.</summary>
    private static Mock<ISsoConnectionRepository> ExpiryRepository(ClaimState state, SsoConnection connection)
    {
        var repository = Repository();
        repository.Setup(r => r.GetActiveSamlIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([connection.Id]);
        repository.Setup(r => r.GetByIdAsync(connection.Id, It.IsAny<CancellationToken>())).ReturnsAsync(connection);
        repository.Setup(r => r.TryClaimExpiryAlertAsync(connection.Id, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, DateTime notAfter, int days, CancellationToken _) =>
            {
                if (state.NotAfter == notAfter && state.Days <= days)
                    return false;
                state.NotAfter = notAfter;
                state.Days = days;
                return true;
            });
        repository.Setup(r => r.ReleaseExpiryAlertAsync(connection.Id, It.IsAny<DateTime>(), It.IsAny<int>(),
                It.IsAny<DateTime?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, DateTime _, int _, DateTime? previousNotAfter, int? previousDays, CancellationToken _) =>
            {
                state.NotAfter = previousNotAfter;
                state.Days = previousDays;
                return Task.CompletedTask;
            });
        return repository;
    }

    private RefreshSamlMetadataJob RefreshJob(
        Mock<ISsoConnectionRepository> repository, string metadataXml, ListLogger<RefreshSamlMetadataJob> logger, bool enabled = true)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(SsoHttp.ClientName))
            .Returns(() => new HttpClient(new StubHandler(metadataXml), disposeHandler: false));
        var options = MsOptions.Create(new SsoOptions { Enabled = enabled });
        var fetcher = new SamlMetadataFetcher(factory.Object, options, NullLogger<SamlMetadataFetcher>.Instance);
        var refresher = new SamlMetadataRefresher(fetcher, repository.Object, options, TimeProvider.System,
            NullLogger<SamlMetadataRefresher>.Instance);

        var services = new ServiceCollection()
            .AddSingleton(repository.Object)
            .AddSingleton(refresher)
            .BuildServiceProvider();
        return new RefreshSamlMetadataJob(new FakeRunner(services), options, logger);
    }

    private CheckSamlCertificateExpiryJob ExpiryJob(
        Mock<ISsoConnectionRepository> repository, RecordingPublisher publisher, bool alertsEnabled = true) =>
        ExpiryJob(repository, publisher, out _, alertsEnabled);

    private CheckSamlCertificateExpiryJob ExpiryJob(
        Mock<ISsoConnectionRepository> repository, RecordingPublisher publisher,
        out ListLogger<CheckSamlCertificateExpiryJob> logger, bool alertsEnabled = true)
    {
        var services = new ServiceCollection()
            .AddSingleton(repository.Object)
            .AddSingleton<IPublisher>(publisher)
            .BuildServiceProvider();
        var options = MsOptions.Create(new SsoOptions
        {
            CertificateExpiryAlertsEnabled = alertsEnabled,
            CertificateExpiryAlertDays = [.. SsoOptions.DefaultCertificateExpiryAlertDays]
        });
        logger = new ListLogger<CheckSamlCertificateExpiryJob>();
        return new CheckSamlCertificateExpiryJob(new FakeRunner(services), options, TimeProvider.System, logger);
    }

    private static SsoConnection SamlConnection(X509Certificate2 certificate) =>
        SsoConnection.Create(Guid.NewGuid(), new SsoConnectionInput
        {
            Name = "Acme SAML",
            Protocol = SsoProtocol.Saml,
            SamlIdpEntityId = EntityId,
            SamlIdpSsoUrl = SsoUrl,
            SamlIdpMetadataUrl = "https://idp.acme.example/metadata",
            SamlIdpCertificates = [Base64(certificate)]
        }, null);

    private static string Metadata(X509Certificate2 certificate) =>
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata" entityID="{EntityId}">
           <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
             <md:KeyDescriptor use="signing"><ds:KeyInfo xmlns:ds="http://www.w3.org/2000/09/xmldsig#"><ds:X509Data><ds:X509Certificate>{Base64(certificate)}</ds:X509Certificate></ds:X509Data></ds:KeyInfo></md:KeyDescriptor>
             <md:SingleSignOnService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect" Location="{SsoUrl}"/>
           </md:IDPSSODescriptor>
         </md:EntityDescriptor>
         """;

    private static string Base64(X509Certificate2 certificate) => Convert.ToBase64String(certificate.RawData);

    private static X509Certificate2 Certificate(string subject, int validDays)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(validDays));
    }

    private sealed class StubHandler(string xml) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
    }
}
