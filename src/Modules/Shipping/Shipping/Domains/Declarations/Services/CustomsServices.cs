using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Shipping.Contracts.Declarations;

namespace Shipping.Domains.Declarations.Services;

/// <summary>
/// The tariff rules of the sample: deterministic, in code, and the only source of declaration amounts. Duty by the HS
/// chapter (first two digits), VAT by the destination country, customs value = declared value. Illustrative rates,
/// not a real tariff.
/// </summary>
public static class DutyCalculator
{
    private const decimal DefaultDutyRate = 0.05m;
    private const decimal DefaultVatRate = 0.10m;

    private static readonly Dictionary<string, decimal> DutyByChapter = new(StringComparer.Ordinal)
    {
        ["84"] = 0.00m, // machinery, computers
        ["85"] = 0.05m, // electrical equipment
        ["61"] = 0.12m, // knitted apparel
        ["62"] = 0.12m, // apparel
        ["64"] = 0.15m, // footwear
        ["22"] = 0.50m  // beverages
    };

    private static readonly Dictionary<string, decimal> VatByCountry = new(StringComparer.Ordinal)
    {
        ["AE"] = 0.05m,
        ["SA"] = 0.15m,
        ["EG"] = 0.14m,
        ["GB"] = 0.20m,
        ["DE"] = 0.19m
    };

    public static DutyEstimateDto Estimate(Shipment shipment) =>
        Estimate(shipment.HsCode, shipment.DestinationCountry, shipment.DeclaredValue, shipment.Currency);

    public static DutyEstimateDto Estimate(string hsCode, string destinationCountry, decimal declaredValue, string currency)
    {
        var dutyRate = DutyByChapter.GetValueOrDefault(hsCode[..2], DefaultDutyRate);
        var vatRate = VatByCountry.GetValueOrDefault(destinationCountry, DefaultVatRate);
        var duty = Math.Round(declaredValue * dutyRate, 2, MidpointRounding.AwayFromZero);
        var vat = Math.Round((declaredValue + duty) * vatRate, 2, MidpointRounding.AwayFromZero);
        return new DutyEstimateDto(hsCode, destinationCountry, declaredValue, dutyRate, duty, vatRate, vat, duty + vat,
            currency);
    }
}

public sealed record CustomsSubmission(Guid DeclarationId, string ShipmentReference, string HsCode, decimal CustomsValue,
    decimal Duty, decimal Vat, string Currency);

/// <summary>The customs authority's API. Must be idempotent for <c>idempotencyKey</c>.</summary>
public interface ICustomsGateway
{
    /// <returns>The customs reference of the accepted declaration.</returns>
    Task<string> SubmitAsync(CustomsSubmission submission, string idempotencyKey, CancellationToken cancellationToken);
}

/// <summary>
/// A MOCK of a customs authority, for the sample and its tests: it accepts every declaration and answers with a
/// reference derived from the idempotency key, so a repeated submission gets the same reference. Replace it with a real
/// adapter before using the sample for anything else.
/// </summary>
public sealed class MockCustomsGateway : ICustomsGateway
{
    private readonly ConcurrentDictionary<string, CustomsSubmission> _received = new(StringComparer.Ordinal);

    /// <summary>Distinct submissions received (by idempotency key).</summary>
    public int Received => _received.Count;

    public Task<string> SubmitAsync(CustomsSubmission submission, string idempotencyKey, CancellationToken cancellationToken)
    {
        _received.TryAdd(idempotencyKey, submission);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
        return Task.FromResult($"MOCK-{hash[..12]}");
    }
}
