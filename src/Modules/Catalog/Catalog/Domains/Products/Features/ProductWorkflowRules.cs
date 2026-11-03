namespace Catalog.Domains.Products.Features;

/// <summary>Shared by the workflow validators/endpoints.</summary>
public static class ProductWorkflowRules
{
    public const int ReasonMinLength = 10;
}

/// <summary>Optional body of the workflow endpoints that carry nothing but the concurrency token.</summary>
public record ProductWorkflowRequest(long? Version = null);
