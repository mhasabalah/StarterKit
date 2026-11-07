using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Identity.Contracts.Sso;
using Identity.Data.Repository.Sso;
using Identity.Domains.Sso.Events;
using Identity.Domains.Sso.Models;
using Identity.Options;
using Identity.Services.Sso;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Identity.Tests.Sso;

public sealed class SamlMetadataRefreshTests : IDisposable
{
    private const string EntityId = "https://idp.acme.example/saml";
    private const string SsoUrl = "https://idp.acme.example/sso";
    private const string MetadataUrl = "https://idp.acme.example/metadata";

    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly X509Certificate2 _current = Certificate("CN=current", TimeSpan.FromDays(200));
    private readonly X509Certificate2 _next = Certificate("CN=next", TimeSpan.FromDays(400));

    public void Dispose()
    {
        _current.Dispose();
        _next.Dispose();
    }

    // ----- aggregate -----

    [Fact]
    public void Metadata_of_another_entity_is_rejected_and_nothing_changes()
    {
        var connection = SamlConnection();
        var before = connection.SamlIdpCertificates.ToList();

        var result = connection.ApplyIdpMetadata("https://evil.example/saml", SsoUrl, [Base64(_next)], Now);

        result.Outcome.Should().Be(SamlMetadataOutcome.Failed);
        result.Error.Should().Be(SamlMetadataErrors.EntityIdMismatch);
        connection.SamlIdpCertificates.Should().Equal(before);
        connection.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Identical_metadata_changes_nothing_and_raises_no_event()
    {
        var connection = SamlConnection();

        var result = connection.ApplyIdpMetadata(EntityId, SsoUrl, [Base64(_current)], Now);

        result.Outcome.Should().Be(SamlMetadataOutcome.Unchanged);
        connection.SamlMetadataRefreshedAt.Should().BeNull("the caller records unchanged refreshes with a set-based update");
        connection.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void A_new_certificate_and_sso_url_replace_the_old_ones_and_raise_the_change_event()
    {
        var connection = SamlConnection();

        var result = connection.ApplyIdpMetadata(EntityId, "https://idp.acme.example/sso2", [Base64(_next)], Now);

        result.Outcome.Should().Be(SamlMetadataOutcome.Updated);
        connection.SamlIdpCertificates.Should().Equal(Base64(_next));
        connection.SamlIdpSsoUrl.Should().Be("https://idp.acme.example/sso2");
        connection.SamlMetadataRefreshedAt.Should().Be(Now);
        connection.SamlMetadataLastError.Should().BeNull();
        connection.SamlCertificatesNotAfter.Should().BeCloseTo(_next.NotAfter.ToUniversalTime(), TimeSpan.FromSeconds(1));
        connection.DomainEvents.OfType<SsoConnectionChangedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Metadata_without_certificates_is_rejected()
    {
        var connection = SamlConnection();

        var result = connection.ApplyIdpMetadata(EntityId, SsoUrl, [], Now);

        result.Error.Should().Be(SamlMetadataErrors.NoCertificates);
        connection.SamlIdpCertificates.Should().HaveCount(1);
    }

    [Fact]
    public void Replacing_certificates_by_hand_raises_the_change_event()
    {
        var connection = SamlConnection();

        connection.ReplaceCertificates([Base64(_next)]);

        connection.DomainEvents.OfType<SsoConnectionChangedEvent>().Should().ContainSingle();
        connection.SamlCertificatesNotAfter.Should().BeCloseTo(_next.NotAfter.ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void The_expiry_alert_uses_the_latest_certificate()
    {
        int[] thresholds = [30, 14, 7, 1];
        var rollover = SamlConnection(Base64(_current), Base64(_next)); // latest expires in ~400 days

        rollover.ExpiryAlertDue(DateTime.UtcNow, thresholds).Should().BeNull("a rollover certificate is still valid for long");
        rollover.LatestNotAfter().Should().BeCloseTo(_next.NotAfter.ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void The_expiry_alert_band_is_the_smallest_threshold_crossed()
    {
        int[] thresholds = [30, 14, 7, 1];
        using var shortLived = Certificate("CN=short", TimeSpan.FromDays(20));
        var connection = SamlConnection(Base64(shortLived));
        var now = DateTime.UtcNow;

        connection.ExpiryAlertDue(now, thresholds)!.Value.ThresholdDays.Should().Be(30);
        connection.ExpiryAlertDue(now.AddDays(7), thresholds)!.Value.ThresholdDays.Should().Be(14);
        connection.ExpiryAlertDue(now.AddDays(15), thresholds)!.Value.ThresholdDays.Should().Be(7);
        connection.ExpiryAlertDue(now.AddDays(-30), thresholds).Should().BeNull("not within 30 days yet");
    }

    [Fact]
    public void An_alert_that_was_already_sent_for_this_expiry_is_not_due_again()
    {
        int[] thresholds = [30, 14, 7, 1];
        using var shortLived = Certificate("CN=short", TimeSpan.FromDays(20));
        var connection = SamlConnection(Base64(shortLived));
        var now = DateTime.UtcNow;
        var first = connection.ExpiryAlertDue(now, thresholds)!.Value;
        typeof(SsoConnection).GetProperty(nameof(SsoConnection.SamlExpiryAlertedNotAfter))!.SetValue(connection, first.NotAfter);
        typeof(SsoConnection).GetProperty(nameof(SsoConnection.SamlExpiryAlertedDays))!.SetValue(connection, first.ThresholdDays);

        connection.ExpiryAlertDue(now, thresholds).Should().BeNull("the 30 day alert was already sent");
        connection.ExpiryAlertDue(now.AddDays(7), thresholds)!.Value.ThresholdDays.Should().Be(14, "a smaller threshold is a new alert");
    }

    [Fact]
    public void The_same_certificates_in_another_order_are_unchanged()
    {
        var connection = SamlConnection(Base64(_current), Base64(_next));

        var result = connection.ApplyIdpMetadata(EntityId, SsoUrl, [Base64(_next), Base64(_current)], Now);

        result.Outcome.Should().Be(SamlMetadataOutcome.Unchanged);
    }

    [Fact]
    public void More_certificates_than_allowed_are_rejected_as_invalid_certificates()
    {
        var connection = SamlConnection();
        var many = Enumerable.Range(0, SsoConnection.MaxCertificates + 1)
            .Select(i => { using var c = Certificate($"CN=c{i}", TimeSpan.FromDays(30)); return Base64(c); }).ToList();

        var result = connection.ApplyIdpMetadata(EntityId, SsoUrl, many, Now);

        result.Error.Should().Be(SamlMetadataErrors.InvalidCertificates);
    }

    [Fact]
    public void An_unparseable_certificate_is_rejected_as_invalid_certificates()
    {
        var connection = SamlConnection();

        var result = connection.ApplyIdpMetadata(EntityId, SsoUrl, ["bm90IGEgY2VydA=="], Now);

        result.Error.Should().Be(SamlMetadataErrors.InvalidCertificates);
        connection.SamlIdpCertificates.Should().Equal(Base64(_current));
    }

    // ----- signature -----

    [Fact]
    public void Unsigned_metadata_is_reported_as_unsigned()
    {
        SamlMetadataSignature.Verify(Metadata(EntityId, _next), [Base64(_current)]).Should().Be(MetadataSignature.NotSigned);
    }

    [Fact]
    public void Metadata_signed_by_a_pinned_certificate_verifies_even_when_it_announces_a_new_key()
    {
        var signed = Sign(Metadata(EntityId, _next), _current); // rollover: the old key signs the new metadata

        SamlMetadataSignature.Verify(signed, [Base64(_current)]).Should().Be(MetadataSignature.Valid);
    }

    [Fact]
    public void Metadata_signed_by_another_key_is_invalid()
    {
        using var attacker = Certificate("CN=attacker", TimeSpan.FromDays(30));
        var signed = Sign(Metadata(EntityId, attacker), attacker);

        SamlMetadataSignature.Verify(signed, [Base64(_current)]).Should().Be(MetadataSignature.Invalid);
    }

    [Fact]
    public void Tampered_signed_metadata_is_invalid()
    {
        var signed = Sign(Metadata(EntityId, _next), _current);
        var tampered = signed.Replace(SsoUrl, "https://evil.example/sso", StringComparison.Ordinal);

        tampered.Should().NotBe(signed);
        SamlMetadataSignature.Verify(tampered, [Base64(_current)]).Should().Be(MetadataSignature.Invalid);
    }

    [Fact]
    public async Task Refresh_rejects_signed_metadata_that_does_not_verify_and_keeps_the_keys()
    {
        using var attacker = Certificate("CN=attacker", TimeSpan.FromDays(30));
        var connection = SamlConnection();
        var (refresher, repository) = Refresher(Sign(Metadata(EntityId, attacker), attacker));

        var result = await refresher.RefreshAsync(connection, CancellationToken.None);

        result.Error.Should().Be(SamlMetadataErrors.SignatureInvalid);
        connection.SamlIdpCertificates.Should().Equal(Base64(_current));
        repository.Verify(r => r.RecordMetadataRefreshAsync(connection.Id, null, SamlMetadataErrors.SignatureInvalid, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Refresh_accepts_a_rollover_signed_by_the_pinned_key()
    {
        var connection = SamlConnection();
        var (refresher, _) = Refresher(Sign(Metadata(EntityId, _next), _current));

        var result = await refresher.RefreshAsync(connection, CancellationToken.None);

        result.Outcome.Should().Be(SamlMetadataOutcome.Updated);
        connection.SamlIdpCertificates.Should().Equal(Base64(_next));
    }

    // ----- fetcher -----

    [Theory]
    [InlineData("<md:EntityDescriptor xmlns:md=\"urn:oasis:names:tc:SAML:2.0:metadata\" entityID=\"x\"/>", true)]
    [InlineData("<!DOCTYPE a [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><a>&x;</a>", false)]
    [InlineData("not xml at all", false)]
    public void Metadata_xml_with_a_dtd_or_garbage_is_not_safe(string xml, bool safe) =>
        SamlMetadataFetcher.IsSafeXml(xml).Should().Be(safe);

    [Fact]
    public async Task Plain_http_metadata_urls_are_refused_without_a_request()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var (xml, error) = await Fetcher(handler).FetchAsync("http://idp.acme.example/metadata", CancellationToken.None);

        xml.Should().BeNull();
        error.Should().Be(SamlMetadataErrors.UrlNotAllowed);
        handler.Requests.Should().Be(0);
    }

    [Fact]
    public async Task An_oversized_metadata_response_is_refused()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('a', SamlMetadataFetcher.MaxBytes + 10))
        });

        var (_, error) = await Fetcher(handler).FetchAsync(MetadataUrl, CancellationToken.None);

        error.Should().Be(SamlMetadataErrors.TooLarge);
    }

    [Fact]
    public async Task A_failing_metadata_server_is_a_fetch_error()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var (_, error) = await Fetcher(handler).FetchAsync(MetadataUrl, CancellationToken.None);

        error.Should().Be(SamlMetadataErrors.FetchFailed);
    }

    [Fact]
    public async Task A_response_without_a_content_length_is_capped_while_streaming()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new EndlessContent()
        });

        var (xml, error) = await Fetcher(handler).FetchAsync(MetadataUrl, CancellationToken.None);

        xml.Should().BeNull();
        error.Should().Be(SamlMetadataErrors.TooLarge);
    }

    [Fact]
    public async Task A_redirect_is_not_followed_and_is_a_fetch_error()
    {
        var handler = new StubHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://evil.example/metadata");
            return redirect;
        });

        var (_, error) = await Fetcher(handler).FetchAsync(MetadataUrl, CancellationToken.None);

        error.Should().Be(SamlMetadataErrors.FetchFailed);
        handler.Requests.Should().Be(1, "no second request to the redirect target");
    }

    [Fact]
    public async Task A_dtd_payload_is_refused_by_the_fetcher()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<!DOCTYPE a [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><a>&x;</a>")
        });

        var (xml, error) = await Fetcher(handler).FetchAsync(MetadataUrl, CancellationToken.None);

        xml.Should().BeNull();
        error.Should().Be(SamlMetadataErrors.InvalidMetadata);
    }

    [Fact]
    public async Task Refresh_with_too_many_certificates_records_invalid_certificates()
    {
        var connection = SamlConnection();
        var certificates = Enumerable.Range(0, SsoConnection.MaxCertificates + 1)
            .Select(i => Certificate($"CN=c{i}", TimeSpan.FromDays(30))).ToList();
        var xml = Metadata(EntityId, certificates.ToArray());
        certificates.ForEach(c => c.Dispose());
        var (refresher, repository) = Refresher(xml);

        var result = await refresher.RefreshAsync(connection, CancellationToken.None);

        result.Error.Should().Be(SamlMetadataErrors.InvalidCertificates);
        repository.Verify(r => r.RecordMetadataRefreshAsync(connection.Id, null, SamlMetadataErrors.InvalidCertificates, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ----- refresher (job body) -----

    [Fact]
    public async Task Refresh_with_a_new_certificate_saves_the_aggregate()
    {
        var connection = SamlConnection();
        var (refresher, repository) = Refresher(Metadata(EntityId, _next));

        var result = await refresher.RefreshAsync(connection, CancellationToken.None);

        result.Outcome.Should().Be(SamlMetadataOutcome.Updated);
        connection.SamlIdpCertificates.Should().Equal(Base64(_next));
        repository.Verify(r => r.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Refresh_with_unchanged_metadata_only_touches_the_refresh_time()
    {
        var connection = SamlConnection();
        var (refresher, repository) = Refresher(Metadata(EntityId, _current));

        var result = await refresher.RefreshAsync(connection, CancellationToken.None);

        result.Outcome.Should().Be(SamlMetadataOutcome.Unchanged);
        repository.Verify(r => r.RecordMetadataRefreshAsync(connection.Id, It.IsNotNull<DateTime?>(), null, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refresh_with_a_different_entity_id_records_the_error_and_keeps_the_keys()
    {
        var connection = SamlConnection();
        var (refresher, repository) = Refresher(Metadata("https://evil.example/saml", _next));

        var result = await refresher.RefreshAsync(connection, CancellationToken.None);

        result.Outcome.Should().Be(SamlMetadataOutcome.Failed);
        result.Error.Should().Be(SamlMetadataErrors.EntityIdMismatch);
        connection.SamlIdpCertificates.Should().Equal(Base64(_current));
        repository.Verify(r => r.RecordMetadataRefreshAsync(connection.Id, null, SamlMetadataErrors.EntityIdMismatch, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(r => r.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Refresh_of_unparseable_metadata_records_an_invalid_metadata_error()
    {
        var connection = SamlConnection();
        var (refresher, repository) = Refresher("<html><body>login page</body></html>");

        var result = await refresher.RefreshAsync(connection, CancellationToken.None);

        result.Error.Should().Be(SamlMetadataErrors.InvalidMetadata);
        repository.Verify(r => r.RecordMetadataRefreshAsync(connection.Id, null, SamlMetadataErrors.InvalidMetadata, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ----- helpers -----

    private SsoConnection SamlConnection(params string[] certificates) => SsoConnection.Create(Guid.NewGuid(), new SsoConnectionInput
    {
        Name = "Acme SAML",
        Protocol = SsoProtocol.Saml,
        SamlIdpEntityId = EntityId,
        SamlIdpSsoUrl = SsoUrl,
        SamlIdpMetadataUrl = MetadataUrl,
        SamlIdpCertificates = certificates.Length == 0 ? [Base64(_current)] : certificates
    }, null);

    private (SamlMetadataRefresher Refresher, Mock<ISsoConnectionRepository> Repository) Refresher(string metadataXml)
    {
        var repository = new Mock<ISsoConnectionRepository>();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(metadataXml) });
        var refresher = new SamlMetadataRefresher(
            Fetcher(handler), repository.Object, MsOptions.Create(new SsoOptions()), TimeProvider.System,
            NullLogger<SamlMetadataRefresher>.Instance);
        return (refresher, repository);
    }

    private static SamlMetadataFetcher Fetcher(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(SsoHttp.ClientName)).Returns(() => new HttpClient(handler, disposeHandler: false));
        return new SamlMetadataFetcher(factory.Object, MsOptions.Create(new SsoOptions()), NullLogger<SamlMetadataFetcher>.Instance);
    }

    private static string Metadata(string entityId, params X509Certificate2[] certificates) =>
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata" ID="_meta1" entityID="{entityId}">
           <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
             {string.Concat(certificates.Select(c => $"<md:KeyDescriptor use=\"signing\"><ds:KeyInfo xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\"><ds:X509Data><ds:X509Certificate>{Base64(c)}</ds:X509Certificate></ds:X509Data></ds:KeyInfo></md:KeyDescriptor>"))}
             <md:SingleSignOnService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect" Location="{SsoUrl}"/>
           </md:IDPSSODescriptor>
         </md:EntityDescriptor>
         """;

    /// <summary>Enveloped signature over the whole document, as SAML metadata publishers produce.</summary>
    private static string Sign(string xml, X509Certificate2 signer)
    {
        var document = new System.Xml.XmlDocument { PreserveWhitespace = true };
        document.LoadXml(xml);
        var signed = new System.Security.Cryptography.Xml.SignedXml(document) { SigningKey = signer.GetRSAPrivateKey() };
        var reference = new System.Security.Cryptography.Xml.Reference("#_meta1");
        reference.AddTransform(new System.Security.Cryptography.Xml.XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new System.Security.Cryptography.Xml.XmlDsigExcC14NTransform());
        signed.AddReference(reference);
        signed.ComputeSignature();
        document.DocumentElement!.PrependChild(document.ImportNode(signed.GetXml(), true));
        return document.OuterXml;
    }

    /// <summary>A body that never ends and has no Content-Length (chunked): the cap must stop reading it.</summary>
    private sealed class EndlessContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new EndlessStream());

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        private sealed class EndlessStream : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                Array.Fill(buffer, (byte)'a', offset, count);
                return count;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                Task.FromResult(Read(buffer, offset, count));

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                buffer.Span.Fill((byte)'a');
                return ValueTask.FromResult(buffer.Length);
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    private static string Base64(X509Certificate2 certificate) => Convert.ToBase64String(certificate.RawData);

    private static X509Certificate2 Certificate(string subject, TimeSpan validFor)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.Add(validFor));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(respond(request));
        }
    }
}
