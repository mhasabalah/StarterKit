using System.Text.Json;
using FluentAssertions;
using Subscriptions.Contracts.Subscriptions.DTOs;
using Xunit;

namespace Subscriptions.Tests;

public class PaymentMethodSnapshotDtoJsonTests
{
    [Fact]
    public void Payment_history_serializes_payment_method_as_object()
    {
        var payment = new PaymentHistoryDto(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            100m,
            "EGP",
            "Submitted",
            new PaymentMethodSnapshotDto(
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                "BankTransfer",
                "CIB Bank Transfer",
                "Account: 1234567890",
                "Transfer to CIB account and attach the receipt"),
            null,
            DateTimeOffset.Parse("2026-05-14T00:00:00Z"));

        var json = JsonSerializer.Serialize(payment, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"paymentMethod\":{");
        json.Should().Contain("\"id\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"");
        json.Should().Contain("\"displayName\":\"CIB Bank Transfer\"");
    }
}
