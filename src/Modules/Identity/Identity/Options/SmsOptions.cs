namespace Identity.Options;

/// <summary>
/// SMS service provider configuration
/// </summary>
public class SmsOptions
{
    public const string SectionName = "SmsSettings";

    public SmsProvider Provider { get; init; } = SmsProvider.Mock;
    public TwilioOptions Twilio { get; init; } = new();
    public AwsSnsOptions AwsSns { get; init; } = new();
}

public enum SmsProvider
{
    Mock,
    Twilio,
    AwsSns
}

public class TwilioOptions
{
    public string AccountSid { get; init; } = string.Empty;
    public string AuthToken { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
}

public class AwsSnsOptions
{
    public string AccessKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public string Region { get; init; } = "us-east-1";
}
