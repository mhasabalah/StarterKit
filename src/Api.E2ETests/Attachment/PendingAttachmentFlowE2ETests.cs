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
/// The pre-entity upload flow over real HTTP: an anonymous visitor gets a server-issued session, uploads into it, lists
/// it, signs in and attaches the files to their own user, then downloads them. Sessions are bound to the caller (the
/// user, or the anonymous IP + User-Agent fingerprint), so another visitor can neither read nor fill them.
/// </summary>
[Collection(E2ECollection.Name)]
public class PendingAttachmentFlowE2ETests(E2ETestFixture fixture)
{
    private const string Base = "/api/v1/attachments";

    // JSON bodies carry enums as numbers (no global string-enum converter); multipart forms accept names.
    private const int UserEntity = 1; // EntityType.User

    // Smallest valid PNG (1x1).
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record Envelope<T>(bool IsSuccess, T? Data, List<string>? Errors);

    private sealed record SessionData(Guid SessionId);

    private sealed record PendingUploaded(Guid Id, Guid SessionId, string FileName, int DisplayOrder);

    private sealed record PendingItem(Guid Id, Guid SessionId, string OriginalFileName, long FileSize);

    private sealed record PendingSession(List<PendingItem> Attachments, int TotalCount, int RemainingSlots);

    private sealed record Associated(int TotalAssociated, List<Guid> AttachmentIds);

    private sealed record EntityAttachment(Guid Id, string FileName);


    /// <summary>A visitor = one browser: anonymous until <paramref name="userId"/> is given; same User-Agent throughout.</summary>
    private HttpClient Visitor(string browser, Guid? userId = null)
    {
        var client = userId is { } id ? fixture.CreateClientAs(id, RoleConstants.User) : fixture.CreateAnonymousClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"E2E-{browser}/1.0");
        return client;
    }

    private static MultipartFormDataContent PngForm(Guid? sessionId, int displayOrder = 0, string name = "photo.png")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", name);
        form.Add(new StringContent("Image"), "type");
        form.Add(new StringContent(displayOrder.ToString(System.Globalization.CultureInfo.InvariantCulture)), "displayOrder");
        if (sessionId is { } id)
            form.Add(new StringContent(id.ToString()), "sessionId");
        return form;
    }

    private static readonly byte[] Notes = "meeting notes"u8.ToArray();

    private static MultipartFormDataContent NotesForm(Guid sessionId, int displayOrder)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Notes);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "file", "notes.txt");
        form.Add(new StringContent("Document"), "type");
        form.Add(new StringContent(displayOrder.ToString(System.Globalization.CultureInfo.InvariantCulture)), "displayOrder");
        form.Add(new StringContent(sessionId.ToString()), "sessionId");
        return form;
    }

    private static async Task<T> OkAsync<T>(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonSerializer.Deserialize<Envelope<T>>(text, Json)!.Data!;
    }

    private static async Task<Guid> NewSessionAsync(HttpClient visitor) =>
        (await OkAsync<SessionData>(await visitor.PostAsync($"{Base}/pending/sessions", null))).SessionId;

    [Fact]
    public async Task Anonymous_upload_then_sign_in_associate_and_download()
    {
        var browser = $"browser-{Guid.NewGuid():N}";
        var anonymous = Visitor(browser);

        var sessionId = await NewSessionAsync(anonymous);
        var first = await OkAsync<PendingUploaded>(await anonymous.PostAsync($"{Base}/pending/upload", PngForm(sessionId, 0, "a.png")));
        var second = await OkAsync<PendingUploaded>(await anonymous.PostAsync($"{Base}/pending/upload", NotesForm(sessionId, 1)));
        first.SessionId.Should().Be(sessionId);

        var listed = await OkAsync<PendingSession>(await anonymous.GetAsync($"{Base}/pending/session/{sessionId}"));
        listed.Attachments.Select(a => a.Id).Should().BeEquivalentTo([first.Id, second.Id]);
        listed.Attachments.Select(a => a.FileSize).Should().BeEquivalentTo([(long)Png.Length, (long)Notes.Length]);

        // The same browser signs in (registration finished) and attaches the files to the new account.
        var userId = Guid.NewGuid();
        var signedIn = Visitor(browser, userId);
        var associated = await OkAsync<Associated>(await signedIn.PostAsJsonAsync($"{Base}/pending/associate",
            new { sessionId, entityType = UserEntity, entityId = userId, isPublic = false }));
        associated.TotalAssociated.Should().Be(2);

        var onEntity = await OkAsync<List<EntityAttachment>>(await signedIn.GetAsync($"{Base}/entity/User/{userId}"));
        onEntity.Select(a => a.Id).Should().BeEquivalentTo(associated.AttachmentIds);

        // The image became the user's avatar and the document stayed beside it; both download byte for byte.
        var downloaded = new List<byte[]>();
        foreach (var id in associated.AttachmentIds)
        {
            var download = await signedIn.GetAsync($"{Base}/{id}/download");
            download.StatusCode.Should().Be(HttpStatusCode.OK);
            downloaded.Add(await download.Content.ReadAsByteArrayAsync());
        }

        downloaded.Should().ContainEquivalentOf(Png).And.ContainEquivalentOf(Notes);

        // The session is consumed: nothing left pending.
        (await OkAsync<PendingSession>(await anonymous.GetAsync($"{Base}/pending/session/{sessionId}"))).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Omitting_the_session_id_starts_a_new_server_issued_session()
    {
        var visitor = Visitor($"browser-{Guid.NewGuid():N}");

        var uploaded = await OkAsync<PendingUploaded>(await visitor.PostAsync($"{Base}/pending/upload", PngForm(sessionId: null)));

        uploaded.SessionId.Should().NotBeEmpty();
        (await OkAsync<PendingSession>(await visitor.GetAsync($"{Base}/pending/session/{uploaded.SessionId}")))
            .Attachments.Should().ContainSingle(a => a.Id == uploaded.Id);
    }

    [Fact]
    public async Task Another_visitor_can_neither_read_nor_fill_nor_delete_from_a_session()
    {
        var owner = Visitor($"owner-{Guid.NewGuid():N}");
        var sessionId = await NewSessionAsync(owner);
        var uploaded = await OkAsync<PendingUploaded>(await owner.PostAsync($"{Base}/pending/upload", PngForm(sessionId)));

        var stranger = Visitor($"stranger-{Guid.NewGuid():N}"); // different fingerprint
        (await stranger.GetAsync($"{Base}/pending/session/{sessionId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.PostAsync($"{Base}/pending/upload", PngForm(sessionId, 1))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.DeleteAsync($"{Base}/pending/{uploaded.Id}?sessionId={sessionId}")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);

        // A signed-in stranger cannot claim it either.
        var strangerUser = Guid.NewGuid();
        (await Visitor($"stranger-{Guid.NewGuid():N}", strangerUser).PostAsJsonAsync($"{Base}/pending/associate",
                new { sessionId, entityType = UserEntity, entityId = strangerUser }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await OkAsync<PendingSession>(await owner.GetAsync($"{Base}/pending/session/{sessionId}"))).TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task A_client_invented_session_id_is_rejected()
    {
        var visitor = Visitor($"browser-{Guid.NewGuid():N}");

        (await visitor.PostAsync($"{Base}/pending/upload", PngForm(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Files_cannot_be_associated_to_another_users_entity()
    {
        var browser = $"browser-{Guid.NewGuid():N}";
        var userId = Guid.NewGuid();
        var signedIn = Visitor(browser, userId);
        var sessionId = await NewSessionAsync(signedIn);
        await OkAsync<PendingUploaded>(await signedIn.PostAsync($"{Base}/pending/upload", PngForm(sessionId)));

        (await signedIn.PostAsJsonAsync($"{Base}/pending/associate",
                new { sessionId, entityType = UserEntity, entityId = Guid.NewGuid() })) // the victim
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owner_can_delete_a_pending_upload()
    {
        var visitor = Visitor($"browser-{Guid.NewGuid():N}");
        var sessionId = await NewSessionAsync(visitor);
        // A document, not an image: an image starts the thumbnail job, whose row update can race the delete (a known
        // 409 the client retries; see Template-Backlog). This test is about ownership, not that race.
        var uploaded = await OkAsync<PendingUploaded>(await visitor.PostAsync($"{Base}/pending/upload", NotesForm(sessionId, 0)));

        (await visitor.DeleteAsync($"{Base}/pending/{uploaded.Id}?sessionId={sessionId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await OkAsync<PendingSession>(await visitor.GetAsync($"{Base}/pending/session/{sessionId}"))).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Associate_requires_sign_in()
    {
        (await fixture.CreateAnonymousClient().PostAsJsonAsync($"{Base}/pending/associate",
                new { sessionId = Guid.NewGuid(), entityType = UserEntity, entityId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
