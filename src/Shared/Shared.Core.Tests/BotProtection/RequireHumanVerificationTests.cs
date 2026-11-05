using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.BotProtection;
using Shared.Core.Exceptions;
using Shared.Core.Exceptions.Handler;

namespace Shared.Core.Tests.BotProtection;

public class RequireHumanVerificationTests
{
    private static async Task<object?> RunAsync(
        Mock<IHumanVerifier> verifier, string action, string? header, Action<BotProtectionOptions>? configure = null)
    {
        var options = new BotProtectionOptions { Enabled = true, SecretKey = "secret" };
        configure?.Invoke(options);

        var services = new ServiceCollection()
            .AddSingleton(verifier.Object)
            .AddSingleton(Microsoft.Extensions.Options.Options.Create(options))
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        if (header is not null)
            http.Request.Headers[BotProtectionExtensions.TokenHeader] = header;

        var conventions = new List<Action<EndpointBuilder>>();
        new ConventionCollector(conventions).RequireHumanVerification(action);
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/"), 0);
        conventions.ForEach(c => c(endpoint));

        var factoryContext = new EndpointFilterFactoryContext
        {
            MethodInfo = typeof(RequireHumanVerificationTests).GetMethod(nameof(Handler))!,
            ApplicationServices = services
        };
        EndpointFilterDelegate pipeline = _ => ValueTask.FromResult<object?>("ran");
        foreach (var factory in endpoint.FilterFactories.AsEnumerable().Reverse())
            pipeline = factory(factoryContext, pipeline);

        return await pipeline(new DefaultEndpointFilterInvocationContext(http));
    }

    public static string Handler() => "ran";

    private static Mock<IHumanVerifier> VerifierReturning(HumanVerificationResult result)
    {
        var mock = new Mock<IHumanVerifier>();
        mock.Setup(v => v.VerifyAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }

    [Fact]
    public async Task MissingHeader_IsBadRequest_WithCaptchaRequiredCode()
    {
        var verifier = VerifierReturning(HumanVerificationResult.Failed(HumanVerificationResult.MissingTokenCode));

        var act = () => RunAsync(verifier, "login", header: null);

        var ex = (await act.Should().ThrowAsync<BadRequestException>()).Which;
        var mapping = ExceptionStatusMapper.Map(ex);
        mapping.StatusCode.Should().Be(400);
        mapping.ErrorCode.Should().Be("captcha_required");
        verifier.Verify(v => v.VerifyAsync(null, It.IsAny<string?>(), "login", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidToken_IsBadRequest_WithCaptchaInvalidCode()
    {
        var verifier = VerifierReturning(HumanVerificationResult.Failed("invalid-input-response"));

        var act = () => RunAsync(verifier, "login", header: "bad");

        var ex = (await act.Should().ThrowAsync<BadRequestException>()).Which;
        ExceptionStatusMapper.Map(ex).ErrorCode.Should().Be("captcha_invalid");
    }

    [Fact]
    public async Task ProviderDownWhileFailClosed_Is503_WithCaptchaUnavailableCode()
    {
        var verifier = VerifierReturning(new HumanVerificationResult(false, ["provider-unavailable"], ProviderUnavailable: true));

        var act = () => RunAsync(verifier, "login", header: "tok");

        var ex = (await act.Should().ThrowAsync<CustomException>()).Which;
        var mapping = ExceptionStatusMapper.Map(ex);
        mapping.StatusCode.Should().Be(503);
        mapping.ErrorCode.Should().Be("captcha_unavailable");
    }

    [Fact]
    public async Task ValidToken_RunsEndpoint_AndForwardsTheTrimmedToken()
    {
        var verifier = VerifierReturning(HumanVerificationResult.Passed);

        (await RunAsync(verifier, "login", header: " good ")).Should().Be("ran");

        verifier.Verify(v => v.VerifyAsync("good", It.IsAny<string?>(), "login", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ActionNotInConfiguredList_RunsEndpoint_WithoutVerifying()
    {
        var verifier = VerifierReturning(HumanVerificationResult.Failed("x"));

        (await RunAsync(verifier, "login", header: null, o => o.Actions = ["register"])).Should().Be("ran");

        verifier.Verify(v => v.VerifyAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActionInConfiguredList_IsVerified()
    {
        var verifier = VerifierReturning(HumanVerificationResult.Failed("x"));

        var act = () => RunAsync(verifier, "Register", header: "t", o => o.Actions = ["register"]);

        await act.Should().ThrowAsync<BadRequestException>();
    }

    [Fact]
    public async Task Disabled_UsesNoOpVerifier_AndPassesWithoutHeader()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["BotProtection:Enabled"] = "false" }).Build();
        var provider = new ServiceCollection().AddLogging().AddBotProtection(configuration).BuildServiceProvider();

        provider.GetRequiredService<IHumanVerifier>().Should().BeOfType<NoOpHumanVerifier>();

        var result = await provider.GetRequiredService<IHumanVerifier>().VerifyAsync(null, null, "login", CancellationToken.None);
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Enabled_WithTurnstile_RegistersTurnstileAdapter_AndValidatesSecret()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BotProtection:Enabled"] = "true",
            ["BotProtection:Provider"] = "Turnstile",
            ["BotProtection:SecretKey"] = "s"
        }).Build();
        var provider = new ServiceCollection().AddLogging().AddBotProtection(configuration).BuildServiceProvider();

        provider.GetRequiredService<IHumanVerifier>().Should().BeOfType<TurnstileHumanVerifier>();
    }

    [Fact]
    public void Enabled_WithoutSecret_FailsOptionsValidation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BotProtection:Enabled"] = "true"
        }).Build();
        var provider = new ServiceCollection().AddLogging().AddBotProtection(configuration).BuildServiceProvider();

        var act = () => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<BotProtectionOptions>>().Value;

        act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>().WithMessage("*SecretKey*");
    }

    // Minimal IEndpointConventionBuilder that records conventions (same trick as RequireFeatureTests).
    private sealed class ConventionCollector(List<Action<EndpointBuilder>> conventions) : IEndpointConventionBuilder
    {
        public void Add(Action<EndpointBuilder> convention) => conventions.Add(convention);

        public void Finally(Action<EndpointBuilder> finallyConvention) => conventions.Add(finallyConvention);
    }
}
