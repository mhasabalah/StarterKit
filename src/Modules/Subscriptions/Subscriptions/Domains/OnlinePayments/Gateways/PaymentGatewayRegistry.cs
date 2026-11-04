using Subscriptions.Domains.OnlinePayments.Exceptions;
using Subscriptions.Options;

namespace Subscriptions.Domains.OnlinePayments.Gateways;

/// <summary>Resolves an ENABLED gateway by name (<c>Subscriptions:PaymentGateways:&lt;Name&gt;:Enabled</c>).</summary>
internal interface IPaymentGatewayRegistry
{
    /// <exception cref="PaymentGatewayNotFoundException">Unknown or disabled provider (404).</exception>
    IPaymentGateway Get(string name);
}

internal sealed class PaymentGatewayRegistry(
    IEnumerable<IPaymentGateway> gateways,
    IOptions<SubscriptionOptions> options) : IPaymentGatewayRegistry
{
    public IPaymentGateway Get(string name)
    {
        var enabled = options.Value.PaymentGateways.TryGetValue(name, out var settings) && settings.Enabled;
        return enabled
            ? gateways.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
              ?? throw new PaymentGatewayNotFoundException(name)
            : throw new PaymentGatewayNotFoundException(name);
    }
}
