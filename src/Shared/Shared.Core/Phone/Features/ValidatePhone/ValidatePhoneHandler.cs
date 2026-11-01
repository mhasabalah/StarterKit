using Shared.Contracts.CQRS;

namespace Shared.Core.Phone.Features.ValidatePhone;

public class ValidatePhoneHandler(IPhoneValidationService phoneService)
    : IQueryHandler<ValidatePhoneQuery, PhoneValidationResult>
{
    public Task<PhoneValidationResult> Handle(ValidatePhoneQuery query, CancellationToken cancellationToken)
        => Task.FromResult(phoneService.Validate(query.PhoneNumber, query.CountryCode));
}
