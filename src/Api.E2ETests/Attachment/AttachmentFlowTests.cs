using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.E2ETests.Infrastructure;
using FluentAssertions;
using Shared.Core.Authorization;
using Xunit;

namespace Api.E2ETests.Attachment;

/// <summary>
/// Attachment smoke flow over real HTTP against the booted Api and an auto-migrated Postgres. It covers upload
/// (two-phase, Ready), metadata, streamed download, the access check for strangers, and delete (hidden at once).
/// It also catches a missing or outdated Attachment migration: every attachment read fails without the
/// <c>status</c> column.
/// </summary>
[Collection(E2ECollection.Name)]
public class AttachmentFlowTests(E2ETestFixture fixture)
{
    private const string Base = "/api/v1/attachments";

    // Smallest valid PNG (1x1): passes magic-byte detection and the megapixel limit.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record Envelope<T>(bool IsSuccess, T? Data, List<string>? Errors);

    private sealed record Uploaded(Guid Id, string FileName, string FileUrl, long FileSize, string ContentType);

    private sealed record AttachmentInfo(Guid Id, string FileName, long FileSize, string? ContentHash);

    [Fact]
    public async Task Owner_uploads_reads_downloads_and_deletes_own_avatar_file()
    {
        var ownerId = Guid.NewGuid();
        var owner = fixture.CreateClientAs(ownerId, RoleConstants.User);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "avatar.png");
        form.Add(new StringContent("Image"), "type");
        form.Add(new StringContent("User"), "entityType");
        form.Add(new StringContent(ownerId.ToString()), "entityId");
        form.Add(new StringContent("false"), "isPublic");

        var upload = await owner.PostAsync($"{Base}/upload", form);
        upload.StatusCode.Should().Be(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync());
        var uploaded = (await upload.Content.ReadFromJsonAsync<Envelope<Uploaded>>(Json))!.Data!;
        uploaded.FileSize.Should().Be(Png.Length);
        uploaded.ContentType.Should().Be("image/png");

        // Metadata: Ready row with server-computed SHA-256.
        var info = await owner.GetAsync($"{Base}/{uploaded.Id}");
        info.StatusCode.Should().Be(HttpStatusCode.OK, await info.Content.ReadAsStringAsync());
        var meta = (await info.Content.ReadFromJsonAsync<Envelope<AttachmentInfo>>(Json))!.Data!;
        meta.ContentHash.Should().HaveLength(64);

        // Download returns the exact bytes (Local provider streams; cloud providers redirect to a signed URL).
        var download = await owner.GetAsync($"{Base}/{uploaded.Id}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(Png);
        download.Headers.TryGetValues("X-Content-Type-Options", out var nosniff).Should().BeTrue();
        nosniff.Should().Contain("nosniff");

        // A stranger can't read someone else's private file.
        var stranger = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);
        (await stranger.GetAsync($"{Base}/{uploaded.Id}/download")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        (await fixture.CreateAnonymousClient().GetAsync($"{Base}/{uploaded.Id}/download")).StatusCode
            .Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound);

        // Delete hides the row immediately; blob removal happens in the background job.
        (await owner.DeleteAsync($"{Base}/{uploaded.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.GetAsync($"{Base}/{uploaded.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Uploading_to_another_users_entity_is_forbidden()
    {
        var attacker = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "evil.png");
        form.Add(new StringContent("Image"), "type");
        form.Add(new StringContent("User"), "entityType");
        form.Add(new StringContent(Guid.NewGuid().ToString()), "entityId"); // victim

        (await attacker.PostAsync($"{Base}/upload", form)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Html_disguised_as_text_is_rejected()
    {
        var user = fixture.CreateClientAs(Guid.NewGuid(), RoleConstants.User);

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("<html><script>alert(1)</script></html>"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "notes.html");
        form.Add(new StringContent("Document"), "type");

        (await user.PostAsync($"{Base}/upload", form)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
