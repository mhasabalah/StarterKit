namespace Identity.Domains.Sessions.Exceptions;

public sealed class SessionNotFoundException(Guid sessionId)
    : NotFoundException($"Session '{sessionId}' was not found.");
