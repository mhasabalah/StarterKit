namespace Shared.Contracts.Lookups;

public record LookupDto(
    Guid Id,
    string ArabicName,
    string EnglishName,
    string? Description = null);
