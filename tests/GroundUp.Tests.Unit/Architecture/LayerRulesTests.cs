using System.Reflection;

namespace GroundUp.Tests.Unit.Architecture;

/// <summary>
/// Enforces the layer rules in CLAUDE.md on the compiled assemblies. The compiler records an assembly
/// reference only when code uses one of its types, method bodies included, so
/// <see cref="Assembly.GetReferencedAssemblies"/> shows what each layer really depends on.
/// A failure names the assembly and the assembly it must not use; search the first for types of the second.
/// </summary>
public sealed class LayerRulesTests
{
    private const string EfCore = "Microsoft.EntityFrameworkCore";

    /// <summary>Every production project under <c>src</c> and its layer. Test helpers are not production code.</summary>
    private static readonly IReadOnlyDictionary<string, Layer> Projects = new Dictionary<string, Layer>
    {
        ["GroundUp.Core"] = Layer.FrameworkCore,
        ["GroundUp.Events"] = Layer.Events,
        ["GroundUp.Data.Abstractions"] = Layer.DataAbstractions,
        ["GroundUp.Repositories"] = Layer.Repositories,
        ["GroundUp.Data.Postgres"] = Layer.DataProvider,
        ["GroundUp.Services"] = Layer.Services,
        ["GroundUp.Api"] = Layer.Api,
        ["GroundUp.Auth.Core"] = Layer.ModuleCore,
        ["GroundUp.Auth.Data.Abstractions"] = Layer.DataAbstractions,
        ["GroundUp.Auth.Repositories"] = Layer.Repositories,
        ["GroundUp.Auth.Data.Postgres"] = Layer.DataProvider,
        ["GroundUp.Auth.Services"] = Layer.Services,
        ["GroundUp.Auth.Api"] = Layer.Api,
        ["GroundUp.Auth.Keycloak"] = Layer.IdentityProvider,
    };

    private static readonly string[] NotProduction = ["GroundUp.Tests.Common"];

    private enum Layer
    {
        FrameworkCore,
        ModuleCore,
        Events,
        DataAbstractions,
        Repositories,
        DataProvider,
        Services,
        Api,
        IdentityProvider
    }

    /// <summary>The rule that makes <paramref name="used"/> forbidden for an assembly in <paramref name="layer"/>, or null.</summary>
    private static string? BrokenRule(Layer layer, string used)
    {
        var isGroundUp = used.StartsWith("GroundUp.", StringComparison.Ordinal);
        var isEf = used == EfCore || used.StartsWith(EfCore + ".", StringComparison.Ordinal);
        var isNpgsql = used == "Npgsql" || used.StartsWith("Npgsql.", StringComparison.Ordinal);
        var isDataAbstractions = isGroundUp && used.EndsWith(".Data.Abstractions", StringComparison.Ordinal);
        var isDataProvider = isGroundUp && used.EndsWith(".Data.Postgres", StringComparison.Ordinal);

        return layer switch
        {
            Layer.FrameworkCore when isGroundUp =>
                "GroundUp.Core has no dependencies on other GroundUp projects",
            Layer.Events when isGroundUp && used != "GroundUp.Core" =>
                "GroundUp.Events depends on GroundUp.Core only",
            Layer.Api when isDataAbstractions || isDataProvider || isEf || isNpgsql =>
                "API projects call services only: no repository interfaces, DbContext, EF Core or Npgsql",
            Layer.Services when isDataProvider || isEf || isNpgsql =>
                "Services use repository interfaces only: no Data.Postgres, EF Core or Npgsql",
            Layer.Repositories when isDataProvider || isNpgsql =>
                "Repositories are database-agnostic: provider-specific code lives in Data.Postgres",
            _ => null
        };
    }

    private static IEnumerable<(string Assembly, string Used, string Rule)> FindViolations()
    {
        foreach (var (name, layer) in Projects)
        {
            var assembly = Assembly.Load(new AssemblyName(name));
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                var rule = BrokenRule(layer, reference.Name!);
                if (rule is not null)
                {
                    yield return (name, reference.Name!, rule);
                }
            }
        }
    }

    private static bool IsAllowlisted(string assembly, string used)
        => LayerRulesAllowlist.Entries.Any(e => e.Assembly == assembly && e.UsedAssembly == used);

    [Fact]
    public void NoAssemblyBreaksALayerRule()
    {
        var violations = FindViolations()
            .Where(v => !IsAllowlisted(v.Assembly, v.Used))
            .Select(v => $"{v.Assembly} uses {v.Used}. Rule: {v.Rule}.")
            .ToList();

        Assert.True(violations.Count == 0,
            "Layer rule violations (see CLAUDE.md, Project Dependency Rules):" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void EveryAllowlistEntryStillOccurs()
    {
        var actual = FindViolations().Select(v => (v.Assembly, v.Used)).ToHashSet();

        var stale = LayerRulesAllowlist.Entries
            .Where(e => !actual.Contains((e.Assembly, e.UsedAssembly)))
            .Select(e => $"{e.Assembly} -> {e.UsedAssembly} ({e.Reason})")
            .ToList();

        Assert.True(stale.Count == 0,
            "These allowlist entries no longer occur; remove them from LayerRulesAllowlist:" + Environment.NewLine
            + string.Join(Environment.NewLine, stale));
    }

    [Fact]
    public void EveryProjectUnderSrcIsChecked()
    {
        var srcProjects = Directory.GetDirectories(Path.Combine(FindRepositoryRoot(), "src"))
            .Where(dir => Directory.GetFiles(dir, "*.csproj").Length > 0)
            .Select(Path.GetFileName)
            .Where(name => !NotProduction.Contains(name))
            .ToList();

        var missing = srcProjects.Where(name => !Projects.ContainsKey(name!)).ToList();

        Assert.True(missing.Count == 0,
            "Add these projects and their layer to LayerRulesTests.Projects: " + string.Join(", ", missing));
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "groundup.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("groundup.sln not found above the test output folder.");
    }
}
