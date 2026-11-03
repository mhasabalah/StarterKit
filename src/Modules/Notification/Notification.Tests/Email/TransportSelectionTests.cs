using MailKit.Security;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Notification.Tests.Email;

public sealed class TransportSelectionTests
{
    [Theory]
    [InlineData(465, true, SecureSocketOptions.SslOnConnect)]
    [InlineData(587, true, SecureSocketOptions.StartTls)]
    [InlineData(25, true, SecureSocketOptions.StartTls)]
    [InlineData(1025, false, SecureSocketOptions.None)]
    public void Socket_options_follow_port(int port, bool useTls, SecureSocketOptions expected)
        => MailKitEmailService.ResolveSocketOptions(new EmailOptions { SmtpPort = port, UseTls = useTls })
            .Should().Be(expected);

    [Theory]
    [InlineData(EmailProvider.Auto, "Development", "", EmailProvider.NoOp)]
    [InlineData(EmailProvider.Auto, "Development", "smtp.example.com", EmailProvider.NoOp)]
    [InlineData(EmailProvider.Auto, "Development", "mailhog", EmailProvider.Smtp)]
    [InlineData(EmailProvider.Auto, "Production", "", EmailProvider.Smtp)]
    [InlineData(EmailProvider.NoOp, "Production", "smtp.real.test", EmailProvider.NoOp)]
    [InlineData(EmailProvider.Smtp, "Development", "", EmailProvider.Smtp)]
    public void Provider_switch(EmailProvider configured, string environment, string host, EmailProvider expected)
        => NotificationModule.ResolveEmailProvider(
                new EmailOptions { Provider = configured, SmtpHost = host },
                new FakeEnvironment(environment))
            .Should().Be(expected);

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
