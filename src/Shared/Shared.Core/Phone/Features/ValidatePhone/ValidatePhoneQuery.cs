using Shared.Contracts.CQRS;

namespace Shared.Core.Phone.Features.ValidatePhone;

public record ValidatePhoneQuery(string PhoneNumber, string CountryCode)
    : IQuery<PhoneValidationResult>;

public class ValidatePhoneQueryValidator : AbstractValidator<ValidatePhoneQuery>
{
    public ValidatePhoneQueryValidator()
    {
        RuleFor(x => x.PhoneNumber).NotEmpty().WithMessage("Phone number is required.");
        RuleFor(x => x.CountryCode).NotEmpty().WithMessage("Country code is required.");
    }
}
