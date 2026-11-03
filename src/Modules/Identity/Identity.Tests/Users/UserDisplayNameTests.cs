using FluentAssertions;
using Identity.Domains.Users.Models;
using Xunit;

namespace Identity.Tests.Users;

public class UserDisplayNameTests
{
    private static readonly Guid Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Theory]
    [InlineData("Jane", "Doe", "j@x.com", "Jane Doe")]
    [InlineData(" Jane ", null, "j@x.com", "Jane")]
    [InlineData(null, "Doe", null, "Doe")]
    [InlineData("  ", "", " j@x.com ", "j@x.com")]
    [InlineData(null, null, null, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    public void Name_then_email_then_id(string? first, string? last, string? email, string expected)
    {
        UserDisplayName.Of(first, last, email, Id).Should().Be(expected);
    }
}
