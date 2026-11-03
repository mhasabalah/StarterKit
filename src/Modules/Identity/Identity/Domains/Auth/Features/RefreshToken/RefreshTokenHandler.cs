using Identity.Contracts.Auth.Commands;
using Identity.Services.Sessions;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.RefreshToken;

public class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty()
            .WithMessage("Refresh token is required");
    }
}

/// <summary>Rotation, reuse detection and user-state checks live in <see cref="IAuthSessionService.RefreshAsync"/>.</summary>
public class RefreshTokenHandler(
    IAuthSessionService sessionService,
    IHttpContextService httpContextService)
    : ICommandHandler<RefreshTokenCommand, RefreshTokenResult>
{
    public async Task<RefreshTokenResult> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var session = await sessionService.RefreshAsync(
            command.RefreshToken, httpContextService.GetIpAddress(), cancellationToken);

        return new RefreshTokenResult(session.AccessToken, session.RefreshToken, session.AccessTokenExpiresAt);
    }
}
