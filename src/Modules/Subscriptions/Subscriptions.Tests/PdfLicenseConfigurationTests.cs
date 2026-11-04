using FluentAssertions;
using QuestPDF.Infrastructure;
using Subscriptions.Domains.Subscriptions.Services;
using Xunit;

namespace Subscriptions.Tests;

/// <summary>The QuestPDF license is configuration (Subscriptions:Pdf:License), and a typo fails the startup.</summary>
public class PdfLicenseConfigurationTests
{
    [Theory]
    [InlineData(null, LicenseType.Community)]
    [InlineData("", LicenseType.Community)]
    [InlineData("community", LicenseType.Community)]
    [InlineData("Professional", LicenseType.Professional)]
    [InlineData("ENTERPRISE", LicenseType.Enterprise)]
    public void License_IsReadFromConfiguration(string? value, LicenseType expected) =>
        PdfConfiguration.ParseLicense(value).Should().Be(expected);

    [Theory]
    [InlineData("Comunity")]
    [InlineData("42")]
    public void UnknownLicense_FailsFast_EvenWhenAlreadyConfigured(string value)
    {
        PdfConfiguration.Configure();

        var act = () => PdfConfiguration.Configure(value);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Subscriptions:Pdf:License*");
    }
}
