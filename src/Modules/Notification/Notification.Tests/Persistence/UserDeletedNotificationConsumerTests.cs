using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Core.Data;
using Notification.Persistence.Entities;
using Testcontainers.PostgreSql;
using UserDirectory.Contracts.Users.IntegrationEvents;

namespace Notification.Tests.Persistence;

/// <summary>Account erasure: the user's delivery log and preferences go, other users' stay, redelivery is harmless.</summary>
public sealed class UserDeletedNotificationConsumerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private DbContextOptions<NotificationDbContext> _options = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        _options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(_container.GetConnectionString(), b => b.UseModuleMigrationsHistory<NotificationDbContext>())
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new NotificationDbContext(_options);
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    private async Task SeedAsync(Guid userId, int logs)
    {
        await using var db = new NotificationDbContext(_options);
        for (var i = 0; i < logs; i++)
            db.NotificationLogs.Add(new NotificationLog
            {
                Id = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), UserId = userId, TemplateName = "Welcome",
                MessageJson = "{\"to\":\"person@example.com\"}"
            });
        db.UserNotificationPreferences.Add(new UserNotificationPreference
        {
            Id = Guid.NewGuid(), UserId = userId, ChannelOrder = [NotificationChannel.Email]
        });
        await db.SaveChangesAsync();
    }

    private async Task ConsumeAsync(Guid userId)
    {
        var context = new Mock<ConsumeContext<UserDeletedIntegrationEvent>>();
        context.SetupGet(c => c.Message).Returns(new UserDeletedIntegrationEvent { UserId = userId });
        context.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);

        await using var db = new NotificationDbContext(_options);
        await new UserDeletedNotificationConsumer(db, NullLogger<UserDeletedNotificationConsumer>.Instance)
            .Consume(context.Object);
    }

    private async Task<(int Logs, int Preferences)> CountsAsync(Guid userId)
    {
        await using var db = new NotificationDbContext(_options);
        return (await db.NotificationLogs.CountAsync(x => x.UserId == userId),
            await db.UserNotificationPreferences.CountAsync(x => x.UserId == userId));
    }

    [Fact]
    public async Task Consume_DeletesTheUsersLogAndPreferences_AndLeavesOtherUsersAlone()
    {
        var gone = Guid.NewGuid();
        var kept = Guid.NewGuid();
        await SeedAsync(gone, 3);
        await SeedAsync(kept, 2);

        await ConsumeAsync(gone);

        (await CountsAsync(gone)).Should().Be((0, 0));
        (await CountsAsync(kept)).Should().Be((2, 1));
    }

    [Fact]
    public async Task Consume_Twice_IsIdempotent()
    {
        var gone = Guid.NewGuid();
        await SeedAsync(gone, 1);

        await ConsumeAsync(gone);
        var again = () => ConsumeAsync(gone);

        await again.Should().NotThrowAsync();
        (await CountsAsync(gone)).Should().Be((0, 0));
    }

    [Fact]
    public async Task Consume_ForAUserWithNoNotifications_DoesNothing()
    {
        var again = () => ConsumeAsync(Guid.NewGuid());

        await again.Should().NotThrowAsync();
    }
}
