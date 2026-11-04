using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Web;
using Xunit;

namespace Api.E2ETests.Infrastructure;

/// <summary>
/// Long operations opt out of the 30 s default timeout in the right way: uploads and the tenant export use the
/// long-running policy; streaming DOWNLOADS disable the timeout entirely (a slow client must not get a truncated body).
/// </summary>
[Collection(E2ECollection.Name)]
public class RequestTimeoutPolicyTests(E2ETestFixture fixture)
{
    private Endpoint Named(string name) =>
        fixture.Factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(d => d.Endpoints)
            .Single(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == name);

    [Theory]
    [InlineData("ExportTenantData")]
    [InlineData("UploadAttachment")]
    [InlineData("UploadPendingAttachment")]
    public void Uploads_and_tenant_export_use_the_long_running_policy(string endpoint)
    {
        Named(endpoint).Metadata.GetMetadata<RequestTimeoutAttribute>()!
            .PolicyName.Should().Be(RequestTimeoutPolicies.LongRunning);
    }

    [Theory]
    [InlineData("DownloadAttachment")]
    [InlineData("GetLocalAttachmentFile")]
    public void Streaming_downloads_disable_the_request_timeout(string endpoint)
    {
        var metadata = Named(endpoint).Metadata;

        metadata.GetMetadata<DisableRequestTimeoutAttribute>().Should().NotBeNull();
        metadata.GetMetadata<RequestTimeoutAttribute>().Should().BeNull();
    }
}
