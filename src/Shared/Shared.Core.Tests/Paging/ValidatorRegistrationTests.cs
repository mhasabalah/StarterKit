using FluentValidation;
using Shared.Contracts.Mediation;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.CQRS;
using Shared.Core.Pagination;
using Shared.Core.Phone;

namespace Shared.Core.Tests.Paging;

/// <summary>
/// Regression: BaseQueryValidator&lt;TQuery&gt; is an open generic base class inside Shared.Core. Assembly scanning
/// (AddPhoneValidationFeature / AddMediator) once registered it as a service. After that, EVERY
/// IValidator&lt;T&gt; resolution in every module threw "Cannot instantiate implementation type".
/// </summary>
public sealed class ValidatorRegistrationTests
{
    public sealed record SampleQuery : BaseQuery, IQuery<string>;

    [Fact]
    public void Scanning_Shared_Core_does_not_register_open_generic_validators()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPhoneValidationFeature();
        Extensions.MediatorExtensions.AddMediator(services, typeof(BaseQueryValidator<>).Assembly);

        services.Should().NotContain(d =>
            d.ImplementationType != null && d.ImplementationType.IsGenericTypeDefinition &&
            typeof(IValidator).IsAssignableFrom(d.ServiceType));

        using var provider = services.BuildServiceProvider();
        var resolve = () => provider.GetServices<IValidator<SampleQuery>>().ToList();
        resolve.Should().NotThrow();
    }
}
