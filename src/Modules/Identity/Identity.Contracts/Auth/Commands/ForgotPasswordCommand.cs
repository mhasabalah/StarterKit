namespace Identity.Contracts.Auth.Commands;

public record ForgotPasswordCommand(string Identifier) : ICommand<ForgotPasswordResult>;

public record ForgotPasswordResult(string Message);
