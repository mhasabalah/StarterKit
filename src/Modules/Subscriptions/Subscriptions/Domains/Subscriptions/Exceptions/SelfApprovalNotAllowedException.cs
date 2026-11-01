namespace Subscriptions.Domains.Subscriptions.Exceptions;

public class SelfApprovalNotAllowedException()
    : ConflictException("An admin cannot approve their own subscription.");
