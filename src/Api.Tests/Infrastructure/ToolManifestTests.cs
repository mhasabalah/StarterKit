using System.Text.Json;
using System.Text.RegularExpressions;

namespace Api.Tests.Infrastructure;

/// <summary>
/// <c>dotnet ef</c> comes from the local tool manifest (<c>.config/dotnet-tools.json</c>, <c>dotnet tool restore</c>).
/// Its version must match the EF Core Design package, or migrations get scaffolded by a different EF than the one
/// that runs them.
/// </summary>
public sealed partial class ToolManifestTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StarterKit.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("StarterKit.slnx not found above the test output folder.");
    }

    [GeneratedRegex(@"Include=""Microsoft\.EntityFrameworkCore\.Design"" Version=""(?<v>[^""]+)""")]
    private static partial Regex EfDesignVersion();

    [Fact]
    public void DotnetEf_tool_version_matches_the_EF_Core_Design_package()
    {
        var root = FindRepoRoot();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".config", "dotnet-tools.json")));
        var tool = manifest.RootElement.GetProperty("tools").GetProperty("dotnet-ef").GetProperty("version").GetString();

        var packages = File.ReadAllText(Path.Combine(root, "Directory.Packages.props"));
        var match = EfDesignVersion().Match(packages);
        match.Success.Should().BeTrue("Directory.Packages.props pins Microsoft.EntityFrameworkCore.Design");

        tool.Should().Be(match.Groups["v"].Value,
            "update .config/dotnet-tools.json together with EF Core (Dependabot groups them in 'microsoft')");
    }
}
