using Attachment.Tests.TestSupport;

namespace Attachment.Tests.Unit;

public sealed class PendingSessionServiceTests
{
    [Fact]
    public void Session_issued_to_a_user_is_owned_by_that_user_only()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var session = TestServices.Sessions(Caller.User(alice)).IssueForCurrentCaller();

        TestServices.Sessions(Caller.User(alice)).IsOwnedByCurrentCaller(session).Should().BeTrue();
        TestServices.Sessions(Caller.User(bob)).IsOwnedByCurrentCaller(session).Should().BeFalse();
        TestServices.Sessions(Caller.Anonymous()).IsOwnedByCurrentCaller(session).Should().BeFalse();
        TestServices.Sessions(Caller.User(bob)).IsOwnedBy(session, alice).Should().BeTrue();
        TestServices.Sessions(Caller.User(bob)).IsOwnedBy(session, bob).Should().BeFalse();
    }

    [Fact]
    public void Client_chosen_guids_are_rejected()
    {
        var sessions = TestServices.Sessions(Caller.User(Guid.NewGuid()));

        sessions.IsOwnedByCurrentCaller(Guid.NewGuid()).Should().BeFalse();
        sessions.IsOwnedByCurrentCaller(Guid.Empty).Should().BeFalse();
    }

    [Fact]
    public void Anonymous_session_is_bound_to_the_client_fingerprint()
    {
        var session = TestServices.Sessions(Caller.Anonymous("1.2.3.4", "browser-a")).IssueForCurrentCaller();

        TestServices.Sessions(Caller.Anonymous("1.2.3.4", "browser-a")).IsOwnedByCurrentCaller(session).Should().BeTrue();
        TestServices.Sessions(Caller.Anonymous("9.9.9.9", "browser-a")).IsOwnedByCurrentCaller(session).Should().BeFalse();
        TestServices.Sessions(Caller.Anonymous("1.2.3.4", "browser-b")).IsOwnedByCurrentCaller(session).Should().BeFalse();
    }

    [Fact]
    public void Anonymous_uploader_who_logs_in_can_still_associate_from_the_same_client()
    {
        var session = TestServices.Sessions(Caller.Anonymous("1.2.3.4", "browser-a")).IssueForCurrentCaller();
        var userId = Guid.NewGuid();
        var loggedIn = new Caller(userId, ["User"], "1.2.3.4", "browser-a");

        TestServices.Sessions(loggedIn).IsOwnedBy(session, userId).Should().BeTrue();
        TestServices.Sessions(new Caller(userId, ["User"], "5.5.5.5", "other")).IsOwnedBy(session, userId).Should().BeFalse();
    }

    [Fact]
    public void Tampering_any_bit_invalidates_the_session()
    {
        var caller = Caller.User(Guid.NewGuid());
        var session = TestServices.Sessions(caller).IssueForCurrentCaller();
        var bytes = session.ToByteArray();
        bytes[3] ^= 0x01;

        TestServices.Sessions(caller).IsOwnedByCurrentCaller(new Guid(bytes)).Should().BeFalse();
    }
}
