using Catalog.Contracts.Products.IntegrationEvents;
using Catalog.Domains.Products.Consumers;
using FluentAssertions;
using Catalog.Contracts.Products.Queries;
using Catalog.Domains.Products.Exceptions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Notification.Contracts;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;
using Shared.Contracts.Mediation;
using Xunit;

namespace Catalog.Tests;

public class ProductPublishedInAppTests
{
    [Fact]
    public void The_owner_gets_a_catalog_inbox_entry_in_both_languages()
    {
        var content = ProductPublishedConsumer.Content(new ProductPublishedIntegrationEvent
        {
            ProductId = Guid.NewGuid(), OwnerId = Guid.NewGuid(), Sku = "SKU-1", PublishedAt = DateTime.UtcNow
        });

        content.Category.Should().Be("catalog.product-published");
        content.Body.En.Should().Contain("SKU-1");
        content.Body.Ar.Should().Contain("SKU-1").And.NotBe(content.Body.En);
        content.Title.Ar.Should().NotBe(content.Title.En);
    }

    private static ProductPublishedIntegrationEvent Event() => new()
    {
        ProductId = Guid.NewGuid(), OwnerId = Guid.NewGuid(), Sku = "SKU-9", PublishedAt = DateTime.UtcNow
    };

    private static (ProductPublishedConsumer Consumer, Mock<INotificationSender> Sender, Mock<INotificationBuilder> Builder, Mock<ISender> Mediator)
        Create()
    {
        var builder = new Mock<INotificationBuilder>();
        builder.Setup(b => b.WithInApp(It.IsAny<InAppContent>())).Returns(builder.Object);
        builder.Setup(b => b.WithCorrelationId(It.IsAny<Guid>())).Returns(builder.Object);
        builder.Setup(b => b.SendAsync(It.IsAny<NotificationChannel?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Guid.NewGuid());
        var sender = new Mock<INotificationSender>();
        sender.Setup(s => s.For(It.IsAny<Guid>(), It.IsAny<NotificationCulture>())).Returns(builder.Object);
        var mediator = new Mock<ISender>();
        return (new ProductPublishedConsumer(mediator.Object, sender.Object, NullLogger<ProductPublishedConsumer>.Instance),
            sender, builder, mediator);
    }

    private static Mock<ConsumeContext<ProductPublishedIntegrationEvent>> Context(ProductPublishedIntegrationEvent message, Guid? messageId)
    {
        var context = new Mock<ConsumeContext<ProductPublishedIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(message);
        context.SetupGet(c => c.MessageId).Returns(messageId);
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
        return context;
    }

    [Fact]
    public async Task The_owner_is_notified_with_a_correlation_id_pinned_to_the_message_so_redelivery_deduplicates()
    {
        var (consumer, sender, builder, _) = Create();
        var message = Event();
        var messageId = Guid.NewGuid();

        await consumer.Consume(Context(message, messageId).Object);

        sender.Verify(s => s.For(message.OwnerId, It.IsAny<NotificationCulture>()), Times.Once);
        builder.Verify(b => b.WithCorrelationId(messageId), Times.Once);
        builder.Verify(b => b.SendAsync(It.IsAny<NotificationChannel?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Without_a_transport_message_id_the_event_id_pins_the_correlation()
    {
        var (consumer, _, builder, _) = Create();
        var message = Event();

        await consumer.Consume(Context(message, messageId: null).Object);

        builder.Verify(b => b.WithCorrelationId(message.EventId), Times.Once);
    }

    [Fact]
    public async Task A_product_that_is_no_longer_published_is_not_announced()
    {
        var (consumer, sender, _, mediator) = Create();
        var message = Event();
        mediator.Setup(m => m.Send(It.IsAny<GetProductByIdQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProductNotFoundException(message.ProductId));

        await consumer.Consume(Context(message, Guid.NewGuid()).Object);

        sender.Verify(s => s.For(It.IsAny<Guid>(), It.IsAny<NotificationCulture>()), Times.Never);
    }
}
