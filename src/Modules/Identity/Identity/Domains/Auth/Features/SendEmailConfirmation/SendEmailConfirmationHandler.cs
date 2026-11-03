using Identity.Contracts.Auth.Commands;
using Identity.Services.EmailConfirmation;

namespace Identity.Domains.Auth.Features.SendEmailConfirmation;

public class SendEmailConfirmationCommandValidator : AbstractValidator<SendEmailConfirmationCommand>
{
    public SendEmailConfirmationCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public class SendEmailConfirmationHandler(
    UserManager<ApplicationUser> userManager,
    IEmailConfirmationSender confirmationSender)
    : ICommandHandler<SendEmailConfirmationCommand, SendEmailConfirmationResult>
{
    public const string SentMessage = "A confirmation link has been sent to your email.";
    public const string AlreadyConfirmedMessage = "Your email is already confirmed.";
    public const string NoEmailMessage = "Your account has no email address to confirm.";

    public async Task<SendEmailConfirmationResult> Handle(
        SendEmailConfirmationCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new NotFoundException("User not found.");

        if (user.EmailConfirmed)
        {
            return new SendEmailConfirmationResult(AlreadyConfirmedMessage);
        }

        if (!EmailConfirmationSender.CanConfirm(user))
        {
            throw new BadRequestException(NoEmailMessage);
        }

        await confirmationSender.SendAsync(user, cancellationToken);
        return new SendEmailConfirmationResult(SentMessage);
    }
}
