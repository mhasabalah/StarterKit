using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Validation.ClamAv;
using Attachment.Tests.TestSupport;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Logging.Abstractions;

namespace Attachment.Tests.Integration;

/// <summary>Opt-in: needs Docker and pulls the ~400 MB ClamAV image; clamd takes about a minute to load signatures.</summary>
public sealed class ClamAvFactAttribute : FactAttribute
{
    public const string Variable = "STARTERKIT_CLAMAV_TESTS";

    public ClamAvFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
            Skip = $"Real ClamAV test: set {Variable}=1 (Docker required, slow).";
    }
}

/// <summary>
/// <see cref="ClamAvMalwareScanner"/> against a real clamd (the image used by <c>docker-compose.clamav.yml</c>):
/// the EICAR test file is detected, a clean PDF passes, content over clamd's limits is never clean, PING answers. Run with
/// <c>STARTERKIT_CLAMAV_TESTS=1 dotnet test src/Modules/Attachment/Attachment.Tests --filter ClamAvContainerTests</c>.
/// </summary>
public sealed class ClamAvContainerTests
{
    public const string Image = "clamav/clamav:1.4";

    /// <summary>
    /// The EICAR anti-virus test string, assembled at run time so this source file itself is not quarantined by the
    /// developer's antivirus.
    /// </summary>
    private static byte[] Eicar() => System.Text.Encoding.ASCII.GetBytes(
        string.Concat(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$", "EICAR-STANDARD-", "ANTIVIRUS-TEST-", "FILE!$H+H*"));

    [ClamAvFact]
    public async Task Real_clamd_detects_eicar_passes_a_clean_file_and_answers_ping()
    {
        await using var clamd = new ContainerBuilder(Image)
            // As in docker-compose.clamav.yml, plus a tiny MaxFileSize so the "skipped content" path is testable.
            .WithEnvironment("CLAMD_CONF_AlertExceedsMax", "yes")
            .WithEnvironment("CLAMD_CONF_MaxFileSize", "1M")
            .WithEnvironment("CLAMD_CONF_StreamMaxLength", "10M")
            .WithPortBinding(3310, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(3310))
            .Build();
        using var startup = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await clamd.StartAsync(startup.Token);

        var scanner = new ClamAvMalwareScanner(
            TestOptions.Wrap(TestOptions.Create(o =>
            {
                o.Validation.Scanner.Provider = MalwareScannerProvider.ClamAv;
                o.Validation.Scanner.Host = clamd.Hostname;
                o.Validation.Scanner.Port = clamd.GetMappedPublicPort(3310);
                o.Validation.Scanner.TimeoutSeconds = 60;
            })),
            TimeProvider.System,
            NullLogger<ClamAvMalwareScanner>.Instance);

        (await scanner.PingAsync()).Should().BeTrue();

        var infected = await scanner.ScanAsync(new MemoryStream(Eicar()), "eicar.txt");
        infected.IsClean.Should().BeFalse();
        infected.ThreatName.Should().ContainEquivalentOf("eicar"); // e.g. Win.Test.EICAR_HDB-1

        var clean = await scanner.ScanAsync(new MemoryStream(TestFiles.Pdf()), "invoice.pdf");
        clean.IsClean.Should().BeTrue();

        // Content clamd did not fully scan (over MaxFileSize) must NOT come back clean (AlertExceedsMax).
        var oversized = new byte[2 * 1024 * 1024];
        Random.Shared.NextBytes(oversized);
        var skipped = await scanner.ScanAsync(new MemoryStream(oversized), "big.bin");
        skipped.IsClean.Should().BeFalse("without AlertExceedsMax clamd answers OK for content it skipped");
        skipped.ThreatName.Should().Contain("Limits");
    }
}
