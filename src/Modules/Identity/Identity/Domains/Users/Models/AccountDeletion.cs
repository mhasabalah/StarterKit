using UserDirectory.Contracts.Users.IntegrationEvents;

namespace Identity.Domains.Users.Models;

/// <summary>
/// Tombstone of a self-service account erasure. <see cref="Entity{T}.Id"/> IS the erased user's id (the user row is kept,
/// anonymized, for referential integrity). Holds no personal data. It exists to carry
/// <see cref="UserDeletedIntegrationEvent"/> through the transactional outbox: the event is written in the SAME save
/// that anonymizes the user, so other modules erase their data if and only if the erasure was committed.
/// </summary>
public class AccountDeletion : Aggregate<Guid>
{
    public DateTime DeletedAt { get; private set; }

    private AccountDeletion()
    {
    }

    public static AccountDeletion Create(Guid userId, DateTime utcNow)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id is required.", nameof(userId));

        var deletion = new AccountDeletion { Id = userId, DeletedAt = utcNow };
        deletion.AddIntegrationEvent(new UserDeletedIntegrationEvent { UserId = userId });
        return deletion;
    }
}
