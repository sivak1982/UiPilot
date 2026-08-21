using System.Xml.Linq;
using Xunit;

namespace UiPilot.Tests;

public sealed class ClientPackageContractTests
{
    [Fact]
    public void Client_build_emits_documentation_for_test_authoring_api()
    {
        var documentationPath = Path.Combine(AppContext.BaseDirectory, "UiPilot.Client.xml");

        Assert.True(
            File.Exists(documentationPath),
            $"Expected client XML documentation at '{documentationPath}'.");

        var members = XDocument.Load(documentationPath)
            .Descendants("member")
            .ToDictionary(
                member => (string)member.Attribute("name")!,
                member => member,
                StringComparer.Ordinal);

        var waitForElement = Assert.Single(
            members,
            pair => pair.Key.StartsWith(
                "M:UiPilot.Client.UiPilotClient.WaitForElementAsync",
                StringComparison.Ordinal));

        Assert.False(string.IsNullOrWhiteSpace(waitForElement.Value.Element("summary")?.Value));
        Assert.False(string.IsNullOrWhiteSpace(waitForElement.Value.Element("remarks")?.Value));

        var coreDocumentationPath = Path.Combine(AppContext.BaseDirectory, "UiPilot.Core.xml");
        Assert.True(
            File.Exists(coreDocumentationPath),
            $"Expected core XML documentation at '{coreDocumentationPath}'.");

        var elementInfo = XDocument.Load(coreDocumentationPath)
            .Descendants("member")
            .Single(member => string.Equals(
                (string?)member.Attribute("name"),
                "P:UiPilot.Inspection.ElementInfo.Id",
                StringComparison.Ordinal));
        Assert.Contains("Session-scoped", elementInfo.Element("summary")?.Value ?? "");
    }

    [Fact]
    public void Client_package_sources_include_readme_and_opt_in_cursor_skill_target()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageReadme = File.ReadAllText(
            Path.Combine(repositoryRoot, "src", "UiPilot.Client", "PACKAGE.md"));
        var targets = XDocument.Load(
            Path.Combine(
                repositoryRoot,
                "src",
                "UiPilot.Client",
                "build",
                "UiPilot.Client.targets"));
        var skill = Path.Combine(
            repositoryRoot,
            ".cursor",
            "skills",
            "uipilot-csharp-tests",
            "SKILL.md");
        Assert.True(File.Exists(skill), $"Expected packaged Cursor skill at '{skill}'.");
        var skillText = File.ReadAllText(skill);

        Assert.Contains("MCP to C#", packageReadme, StringComparison.Ordinal);
        Assert.Contains("UiPilotInstallCursorSkill", packageReadme, StringComparison.Ordinal);
        Assert.Contains("configured UiPilot MCP server", packageReadme, StringComparison.Ordinal);
        Assert.Contains(
            targets.Descendants("Target"),
            target => string.Equals(
                (string?)target.Attribute("Name"),
                "UiPilotInstallCursorSkill",
                StringComparison.Ordinal));
        Assert.Contains(
            "already references `UiPilot.Client`, keep its current version",
            skillText,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "UiPilot.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the UiPilot repository root.");
    }
}
