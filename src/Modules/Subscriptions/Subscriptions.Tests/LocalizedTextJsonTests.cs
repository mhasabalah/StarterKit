using System.Text.Json;
using FluentAssertions;
using Shared.Contracts.Localization;
using Subscriptions.Contracts.Subscriptions.Commands.Plans;
using Xunit;

namespace Subscriptions.Tests;

public class LocalizedTextJsonTests
{
    [Fact]
    public void CreatePlanCommand_deserializes_localized_text_body()
    {
        const string json = """
        {
          "name": { "en": "Starter", "ar": "ستارتر" },
          "description": { "en": "Basic plan", "ar": "خطة أساسية" },
          "price": 100,
          "currency": "EGP",
          "billingCycleInMonths": 1,
          "maxItems": 3,
          "maxStorageGb": 10,
          "maxUsers": 2,
          "isPublic": true
        }
        """;

        var command = JsonSerializer.Deserialize<CreatePlanCommand>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        command.Should().NotBeNull();
        command!.Name.Should().Be(LocalizedText.Create("Starter", "ستارتر"));
        command.Description.Should().Be(LocalizedText.Create("Basic plan", "خطة أساسية"));
    }
}
