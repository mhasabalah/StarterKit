using Agentic.Contracts.Agents;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Exceptions;
using Shared.Core.MultiTenancy;
using Shipping.Contracts.Declarations;
using Shipping.Contracts.Shipments;
using Shipping.Contracts.Shipments.IntegrationEvents;
using Shipping.Domains.Agents;
using Shipping.Domains.Declarations.Services;
using Shipping.Domains.Shipments.Consumers;
using Shipping.Domains.Shipments.Exceptions;
using static Shipping.IntegrationTests.ShippingPostgresFixture;

namespace Shipping.IntegrationTests;

/// <summary>The sample's commands on real PostgreSQL (RLS app role, two tenants): the rules the agent's tools rely on.</summary>
[Collection(ShippingPostgresCollection.Name)]
public class ShippingFlowTests(ShippingPostgresFixture fx)
{
    private static readonly ShipmentDocumentInput[] AllDocuments =
    [
        new(ShipmentDocumentKind.CommercialInvoice, "INV-1", "10 laptops at 100 USD."),
        new(ShipmentDocumentKind.PackingList, "PL-1", null),
        new(ShipmentDocumentKind.CertificateOfOrigin, "CO-1", "Made in China.")
    ];

    private static string NewReference() => "SHP-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<Guid> CreateAsync(TenantInfo? tenant = null, IReadOnlyList<ShipmentDocumentInput>? documents = null,
        string? reference = null) =>
        (await fx.SendAsync(tenant ?? TenantA, new CreateShipmentCommand(reference ?? NewReference(), "CN", "AE", "8471.30",
            1000m, "USD", 120m, documents ?? AllDocuments))).Id;

    private async Task<Guid> ReadyAsync(IReadOnlyList<ShipmentDocumentInput>? documents = null)
    {
        var id = await CreateAsync(documents: documents);
        await fx.SendAsync(TenantA, new MarkShipmentReadyForClearanceCommand(id));
        return id;
    }

    [Fact]
    public async Task ACreatedShipment_IsReadBack_WithItsDocuments()
    {
        var id = await CreateAsync();

        var shipment = await fx.SendAsync(TenantA, new GetShipmentByIdQuery(id));

        shipment.Status.Should().Be(ShipmentStatus.Draft);
        shipment.Documents.Select(d => d.Kind).Should().BeEquivalentTo(AllDocuments.Select(d => d.Kind));
    }

    [Fact]
    public async Task AReferenceIsUniquePerTenant()
    {
        var reference = NewReference();
        await CreateAsync(reference: reference);

        var again = () => CreateAsync(reference: reference.ToLowerInvariant());
        var otherTenant = () => CreateAsync(TenantB, reference: reference);

        (await again.Should().ThrowAsync<ShipmentAlreadyExistsException>()).Which.ErrorCode.Should().Be("shipment_already_exists");
        await otherTenant.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AnotherTenantsShipment_DoesNotExist()
    {
        var id = await CreateAsync();

        var read = () => fx.SendAsync(TenantB, new GetShipmentByIdQuery(id));
        var ready = () => fx.SendAsync(TenantB, new MarkShipmentReadyForClearanceCommand(id));

        await read.Should().ThrowAsync<ShipmentNotFoundException>();
        await ready.Should().ThrowAsync<ShipmentNotFoundException>();
    }

    [Fact]
    public async Task ReadyForClearance_WritesTheTriggerEventToTheOutbox_Once_AndChecksTheVersion()
    {
        var id = await CreateAsync();
        var stale = () => fx.SendAsync(TenantA, new MarkShipmentReadyForClearanceCommand(id, Version: 99));
        await stale.Should().ThrowAsync<ConcurrencyConflictException>();

        await fx.SendAsync(TenantA, new MarkShipmentReadyForClearanceCommand(id));
        var twice = () => fx.SendAsync(TenantA, new MarkShipmentReadyForClearanceCommand(id));

        (await twice.Should().ThrowAsync<InvalidShipmentStateException>()).Which.ErrorCode.Should().Be("shipment_not_draft");
        (await fx.AdminScalarAsync<long>(
                $"select count(*) from shipping.outbox_messages where payload::text like '%{id}%' and (message_type like '%ready-for-clearance%' or message_type like '%ReadyForClearance%')"))
            .Should().Be(1);
    }

    [Fact]
    public async Task ADeclaration_NeedsAShipmentReadyForClearance_AndPreparingAgainRecomputesTheSameDraft()
    {
        var draftShipment = await CreateAsync();
        var tooEarly = () => fx.SendAsync(TenantA, new PrepareDeclarationDraftCommand(draftShipment));
        (await tooEarly.Should().ThrowAsync<InvalidShipmentStateException>()).Which.ErrorCode.Should().Be("shipment_not_ready");

        var id = await ReadyAsync();
        var first = await fx.SendAsync(TenantA, new PrepareDeclarationDraftCommand(id, "First"));
        var second = await fx.SendAsync(TenantA, new PrepareDeclarationDraftCommand(id, "Second"));

        second.Id.Should().Be(first.Id);
        second.Notes.Should().Be("Second");
        second.Vat.Should().Be(50m, "1000 USD of computers into the UAE: no duty, 5% VAT");
        (await fx.SendAsync(TenantA, new GetDeclarationByShipmentQuery(id))).Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task Submit_RefusesMissingDocuments_ThenSubmitsOnce_EvenWhenRepeated()
    {
        var incomplete = await ReadyAsync([AllDocuments[0]]);
        var incompleteDraft = await fx.SendAsync(TenantA, new PrepareDeclarationDraftCommand(incomplete));
        var refused = () => fx.SendAsync(TenantA, new SubmitDeclarationCommand(incompleteDraft.Id));
        (await refused.Should().ThrowAsync<InvalidShipmentStateException>()).Which.ErrorCode.Should().Be("documents_missing");

        var id = await ReadyAsync();
        var draft = await fx.SendAsync(TenantA, new PrepareDeclarationDraftCommand(id));
        var gateway = fx.Services.GetRequiredService<MockCustomsGateway>();
        var before = gateway.Received;

        var submitted = await fx.SendAsync(TenantA, new SubmitDeclarationCommand(draft.Id));
        var again = await fx.SendAsync(TenantA, new SubmitDeclarationCommand(draft.Id));

        submitted.Status.Should().Be(DeclarationStatus.Submitted);
        submitted.SubmissionReference.Should().StartWith("MOCK-");
        again.SubmissionReference.Should().Be(submitted.SubmissionReference);
        gateway.Received.Should().Be(before + 1);
        (await fx.SendAsync(TenantA, new GetShipmentByIdQuery(id))).Status.Should().Be(ShipmentStatus.DeclarationSubmitted);
        var recompute = () => fx.SendAsync(TenantA, new PrepareDeclarationDraftCommand(id));
        await recompute.Should().ThrowAsync<InvalidShipmentStateException>();
    }

    [Fact]
    public async Task ADeclaration_OfAnotherTenant_DoesNotExist()
    {
        var id = await ReadyAsync();
        var draft = await fx.SendAsync(TenantA, new PrepareDeclarationDraftCommand(id));

        var read = () => fx.SendAsync(TenantB, new GetDeclarationByShipmentQuery(id));
        var prepare = () => fx.SendAsync(TenantB, new PrepareDeclarationDraftCommand(id));
        var submit = () => fx.SendAsync(TenantB, new SubmitDeclarationCommand(draft.Id));

        await read.Should().ThrowAsync<NotFoundException>();
        await prepare.Should().ThrowAsync<ShipmentNotFoundException>();
        await submit.Should().ThrowAsync<DeclarationNotFoundException>();
        (await fx.SendAsync(TenantA, new GetDeclarationByShipmentQuery(id))).Status.Should().Be(DeclarationStatus.Draft);
    }

    [Fact]
    public async Task Submit_WithStaleVersion_IsRejected_AndNothingIsSubmitted()
    {
        var id = await ReadyAsync();
        var draft = await fx.SendAsync(TenantA, new PrepareDeclarationDraftCommand(id));
        var gateway = fx.Services.GetRequiredService<MockCustomsGateway>();
        var before = gateway.Received;

        var stale = () => fx.SendAsync(TenantA, new SubmitDeclarationCommand(draft.Id, draft.Version + 5));

        await stale.Should().ThrowAsync<ConcurrencyConflictException>();
        gateway.Received.Should().Be(before);
        (await fx.SendAsync(TenantA, new GetDeclarationByShipmentQuery(id))).Status.Should().Be(DeclarationStatus.Draft);
    }

    // ----- the event trigger ------------------------------------------------------------------------------------

    private async Task ConsumeAsync(Guid shipmentId, Guid requestedBy)
    {
        var context = new Mock<ConsumeContext<ShipmentReadyForClearanceIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(new ShipmentReadyForClearanceIntegrationEvent
        {
            ShipmentId = shipmentId,
            RequestedBy = requestedBy
        });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        using (TenantScope.Begin(TenantA))
        {
            await using var scope = fx.Services.CreateAsyncScope();
            await ActivatorUtilities.CreateInstance<ShipmentReadyForClearanceConsumer>(scope.ServiceProvider).Consume(context.Object);
        }
    }

    [Fact]
    public async Task TheTrigger_RequestsTheAgentRun_ForTheUserWhoHandedItOver_WithAStableKey()
    {
        var shipment = Guid.NewGuid();
        var user = Guid.NewGuid();
        fx.AgentRuns.Fail = null;

        await ConsumeAsync(shipment, user);

        var (tenant, command) = fx.AgentRuns.Requested.Last();
        tenant.Should().Be(TenantA.Id);
        command.AgentKey.Should().Be(ShipmentPreparationAgent.Key);
        command.Trigger.Should().Be(AgentTriggerKind.Event);
        command.TriggerKey.Should().Be($"shipment:{shipment:N}:ready-for-clearance");
        command.OnBehalfOfUserId.Should().Be(user);
        command.InputJson.Should().Contain(shipment.ToString());
    }

    [Fact]
    public async Task TheTrigger_AcknowledgesNothingToDo_ButLetsTheBusRetryAnythingElse()
    {
        try
        {
            fx.AgentRuns.Fail = new NotFoundException("Agents are not available.");
            await FluentActions.Awaiting(() => ConsumeAsync(Guid.NewGuid(), Guid.NewGuid())).Should().NotThrowAsync();

            fx.AgentRuns.Fail = new ForbiddenException("The user may not start this agent.");
            await FluentActions.Awaiting(() => ConsumeAsync(Guid.NewGuid(), Guid.NewGuid())).Should().NotThrowAsync();

            fx.AgentRuns.Fail = new InvalidOperationException("Database down.");
            await FluentActions.Awaiting(() => ConsumeAsync(Guid.NewGuid(), Guid.NewGuid()))
                .Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            fx.AgentRuns.Fail = null;
        }
    }
}
