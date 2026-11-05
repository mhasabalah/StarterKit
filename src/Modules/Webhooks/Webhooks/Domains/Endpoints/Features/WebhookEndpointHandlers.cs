using Hangfire;
using Microsoft.Extensions.Options;
using Webhooks.Domains.Deliveries.Jobs;
using Webhooks.Domains.Deliveries.Models;
using Webhooks.Domains.Endpoints.Exceptions;
using Webhooks.Domains.Endpoints.Models;
using Webhooks.Security;
using Webhooks.Services;

namespace Webhooks.Domains.Endpoints.Features;

/// <summary>Validation shared by create and update.</summary>
internal static class WebhookEndpointRules
{
    public static void AddUrlRules<T>(this AbstractValidator<T> validator, System.Linq.Expressions.Expression<Func<T, string>> url,
        IStringLocalizer<WebhooksResource> localizer, IWebhookUrlPolicy policy)
    {
        validator.RuleFor(url)
            .NotEmpty().WithMessage(_ => localizer["Validation.Url.Required"])
            .MaximumLength(WebhookEndpoint.UrlMaxLength)
            .WithMessage(_ => localizer["Validation.Url.MaxLength"].Value.Replace("{MaxLength}", WebhookEndpoint.UrlMaxLength.ToString()))
            .Custom((value, context) =>
            {
                if (string.IsNullOrWhiteSpace(value) || value.Length > WebhookEndpoint.UrlMaxLength)
                    return;
                var key = policy.Check(value) switch
                {
                    WebhookUrlCheck.Ok => null,
                    WebhookUrlCheck.HttpsRequired => "Validation.Url.HttpsRequired",
                    WebhookUrlCheck.HasCredentials => "Validation.Url.Credentials",
                    WebhookUrlCheck.BlockedHost => "Validation.Url.BlockedHost",
                    _ => "Validation.Url.Invalid"
                };
                if (key is not null)
                    context.AddFailure(localizer[key]);
            });
    }

    public static void AddEventTypeRules<T>(this AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, IReadOnlyList<string>>> eventTypes,
        IStringLocalizer<WebhooksResource> localizer, IWebhookEventCatalog catalog)
    {
        validator.RuleFor(eventTypes)
            .NotEmpty().WithMessage(_ => localizer["Validation.EventTypes.Required"])
            .Must(list => list is null || list.Count <= WebhookEndpoint.MaxEventTypes)
            .WithMessage(_ => localizer["Validation.EventTypes.TooMany"].Value
                .Replace("{MaxCount}", WebhookEndpoint.MaxEventTypes.ToString()))
            .Must(list => list is null || list.All(t => !string.IsNullOrWhiteSpace(t) && catalog.IsKnown(t.Trim())))
            .WithMessage(_ => localizer["Validation.EventTypes.Unknown"]);
    }

    public static void AddDescriptionRule<T>(this AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, string?>> description, IStringLocalizer<WebhooksResource> localizer) =>
        validator.RuleFor(description)
            .MaximumLength(WebhookEndpoint.DescriptionMaxLength)
            .WithMessage(_ => localizer["Validation.Description.MaxLength"].Value
                .Replace("{MaxLength}", WebhookEndpoint.DescriptionMaxLength.ToString()));

    public static WebhookEndpointDto ToDto(WebhookEndpoint e) => new(
        e.Id, e.Url, e.Description, e.EventTypes, e.Enabled, e.ConsecutiveFailures, e.DisabledAt, e.DisabledReason,
        e.LastSuccessAt, e.LastFailureAt, e.CreatedAt ?? DateTime.MinValue, e.Version);
}

// ----- create ---------------------------------------------------------------------------------------------------

public class CreateWebhookEndpointCommandValidator : AbstractValidator<CreateWebhookEndpointCommand>
{
    public CreateWebhookEndpointCommandValidator(IStringLocalizer<WebhooksResource> localizer, IWebhookUrlPolicy urlPolicy,
        IWebhookEventCatalog catalog)
    {
        this.AddUrlRules(x => x.Url, localizer, urlPolicy);
        this.AddDescriptionRule(x => x.Description, localizer);
        this.AddEventTypeRules(x => x.EventTypes, localizer, catalog);
    }
}

public class CreateWebhookEndpointHandler(
    IWebhookEndpointRepository repository,
    IWebhookSecretProtector secrets,
    IOptions<WebhooksOptions> options)
    : ICommandHandler<CreateWebhookEndpointCommand, CreateWebhookEndpointResult>
{
    public async Task<CreateWebhookEndpointResult> Handle(CreateWebhookEndpointCommand command,
        CancellationToken cancellationToken)
    {
        if (await repository.CountAsync(cancellationToken) >= options.Value.MaxEndpointsPerTenant)
            throw new InvalidWebhookEndpointException(
                $"A tenant can have at most {options.Value.MaxEndpointsPerTenant} webhook endpoints.");

        var secret = secrets.GenerateSecret();
        var endpoint = WebhookEndpoint.Create(command.Url, command.Description, command.EventTypes, secrets.Protect(secret));

        await repository.AddAsync(endpoint, cancellationToken);
        await repository.SaveChangesAsync(endpoint.Url, cancellationToken);

        // The ONLY time the raw secret leaves the server (besides rotation).
        return new CreateWebhookEndpointResult(endpoint.Id, secret);
    }
}

// ----- update ---------------------------------------------------------------------------------------------------

public class UpdateWebhookEndpointCommandValidator : AbstractValidator<UpdateWebhookEndpointCommand>
{
    public UpdateWebhookEndpointCommandValidator(IStringLocalizer<WebhooksResource> localizer, IWebhookUrlPolicy urlPolicy,
        IWebhookEventCatalog catalog)
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
        this.AddUrlRules(x => x.Url, localizer, urlPolicy);
        this.AddDescriptionRule(x => x.Description, localizer);
        this.AddEventTypeRules(x => x.EventTypes, localizer, catalog);
        RuleFor(x => x.Version).GreaterThanOrEqualTo(0).WithMessage(_ => localizer["Validation.Version.Min"])
            .When(x => x.Version.HasValue);
    }
}

public class UpdateWebhookEndpointHandler(
    IWebhookEndpointRepository repository,
    WebhooksDbContext dbContext,
    TimeProvider timeProvider)
    : ICommandHandler<UpdateWebhookEndpointCommand, UpdateWebhookEndpointResult>
{
    public async Task<UpdateWebhookEndpointResult> Handle(UpdateWebhookEndpointCommand command,
        CancellationToken cancellationToken)
    {
        var endpoint = await repository.GetByIdAsync(command.Id, cancellationToken)
                       ?? throw new WebhookEndpointNotFoundException(command.Id);

        dbContext.ExpectVersion(endpoint, command.Version);
        endpoint.Update(command.Url, command.Description, command.EventTypes, command.Enabled,
            timeProvider.GetUtcNow().UtcDateTime);

        await repository.SaveChangesAsync(endpoint.Url, cancellationToken);
        return new UpdateWebhookEndpointResult(true, endpoint.Version);
    }
}

// ----- delete ---------------------------------------------------------------------------------------------------

public class DeleteWebhookEndpointCommandValidator : AbstractValidator<DeleteWebhookEndpointCommand>
{
    public DeleteWebhookEndpointCommandValidator(IStringLocalizer<WebhooksResource> localizer) =>
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

/// <summary>Hard delete; its deliveries go with it (cascade). Deletes never take a version.</summary>
public class DeleteWebhookEndpointHandler(IWebhookEndpointRepository repository)
    : ICommandHandler<DeleteWebhookEndpointCommand, DeleteWebhookEndpointResult>
{
    public async Task<DeleteWebhookEndpointResult> Handle(DeleteWebhookEndpointCommand command,
        CancellationToken cancellationToken)
    {
        var endpoint = await repository.GetByIdAsync(command.Id, cancellationToken)
                       ?? throw new WebhookEndpointNotFoundException(command.Id);

        repository.Remove(endpoint);
        await repository.SaveChangesAsync(endpoint.Url, cancellationToken);
        return new DeleteWebhookEndpointResult(true);
    }
}

// ----- rotate secret --------------------------------------------------------------------------------------------

public class RotateWebhookEndpointSecretCommandValidator : AbstractValidator<RotateWebhookEndpointSecretCommand>
{
    public RotateWebhookEndpointSecretCommandValidator(IStringLocalizer<WebhooksResource> localizer) =>
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

public class RotateWebhookEndpointSecretHandler(IWebhookEndpointRepository repository, IWebhookSecretProtector secrets)
    : ICommandHandler<RotateWebhookEndpointSecretCommand, RotateWebhookEndpointSecretResult>
{
    public async Task<RotateWebhookEndpointSecretResult> Handle(RotateWebhookEndpointSecretCommand command,
        CancellationToken cancellationToken)
    {
        var endpoint = await repository.GetByIdAsync(command.Id, cancellationToken)
                       ?? throw new WebhookEndpointNotFoundException(command.Id);

        var secret = secrets.GenerateSecret();
        endpoint.RotateSecret(secrets.Protect(secret));
        await repository.SaveChangesAsync(endpoint.Url, cancellationToken);
        return new RotateWebhookEndpointSecretResult(endpoint.Id, secret);
    }
}

// ----- test ping ------------------------------------------------------------------------------------------------

public class SendWebhookTestPingCommandValidator : AbstractValidator<SendWebhookTestPingCommand>
{
    public SendWebhookTestPingCommandValidator(IStringLocalizer<WebhooksResource> localizer) =>
        RuleFor(x => x.Id).NotEmpty().WithMessage(_ => localizer["Validation.Id.Required"]);
}

public class SendWebhookTestPingHandler(
    IWebhookEndpointRepository repository,
    IWebhookDeliveryRepository deliveries,
    IBackgroundJobClient jobs,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : ICommandHandler<SendWebhookTestPingCommand, SendWebhookTestPingResult>
{
    public async Task<SendWebhookTestPingResult> Handle(SendWebhookTestPingCommand command,
        CancellationToken cancellationToken)
    {
        var endpoint = await repository.GetByIdAsync(command.Id, cancellationToken)
                       ?? throw new WebhookEndpointNotFoundException(command.Id);
        if (!endpoint.Enabled)
            throw new InvalidWebhookEndpointException("The endpoint is disabled; enable it before sending a test ping.");

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var eventId = Guid.NewGuid();
        var payload = WebhookPayloadFactory.CreatePing(eventId, now, tenantContext.TenantId ?? Guid.Empty);
        var delivery = WebhookDelivery.Create(endpoint.Id, eventId, WebhookPayloadFactory.PingEventType, payload, now);

        await deliveries.AddAsync(delivery, cancellationToken);
        await deliveries.SaveChangesAsync(cancellationToken);

        var deliveryId = delivery.Id;
        jobs.Enqueue<DeliverWebhookJob>(job => job.ExecuteAsync(deliveryId, CancellationToken.None));
        return new SendWebhookTestPingResult(deliveryId);
    }
}

// ----- queries ---------------------------------------------------------------------------------------------------

public class GetWebhookEndpointsQueryValidator : AbstractValidator<GetWebhookEndpointsQuery>
{
    public GetWebhookEndpointsQueryValidator(IStringLocalizer<WebhooksResource> localizer)
    {
        RuleFor(x => x.PageNumber).GreaterThan(0).WithMessage(_ => localizer["Validation.PageNumber.Min"])
            .When(x => x.PageNumber.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage(_ => localizer["Validation.PageSize.Range"])
            .When(x => x.PageSize.HasValue);
    }
}

public class GetWebhookEndpointsHandler(WebhooksDbContext dbContext)
    : IQueryHandler<GetWebhookEndpointsQuery, PaginatedResult<WebhookEndpointDto>>
{
    public Task<PaginatedResult<WebhookEndpointDto>> Handle(GetWebhookEndpointsQuery query,
        CancellationToken cancellationToken) =>
        dbContext.Endpoints.AsNoTracking()
            .OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new WebhookEndpointDto(e.Id, e.Url, e.Description, e.EventTypes, e.Enabled, e.ConsecutiveFailures,
                e.DisabledAt, e.DisabledReason, e.LastSuccessAt, e.LastFailureAt, e.CreatedAt ?? DateTime.MinValue, e.Version))
            .PagedResult(query.PageNumber, query.PageSize, cancellationToken);
}

public class GetWebhookEndpointByIdHandler(WebhooksDbContext dbContext)
    : IQueryHandler<GetWebhookEndpointByIdQuery, WebhookEndpointDto>
{
    public async Task<WebhookEndpointDto> Handle(GetWebhookEndpointByIdQuery query, CancellationToken cancellationToken)
    {
        var endpoint = await dbContext.Endpoints.AsNoTracking().FirstOrDefaultAsync(e => e.Id == query.Id, cancellationToken)
                       ?? throw new WebhookEndpointNotFoundException(query.Id);
        return WebhookEndpointRules.ToDto(endpoint);
    }
}

public class GetWebhookEventTypesHandler(IWebhookEventCatalog catalog)
    : IQueryHandler<GetWebhookEventTypesQuery, IReadOnlyList<WebhookEventTypeDto>>
{
    public Task<IReadOnlyList<WebhookEventTypeDto>> Handle(GetWebhookEventTypesQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<WebhookEventTypeDto>>(
            catalog.Events.Select(e => new WebhookEventTypeDto(e.Type, e.Description)).ToList());
}
