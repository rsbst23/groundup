namespace GroundUp.Tests.Unit.Architecture;

/// <summary>
/// Known violations of the layer rules, as (assembly, used assembly) pairs. Every entry names the
/// story that removes it. <see cref="LayerRulesTests"/> fails on a violation that is not listed here,
/// and also on an entry that no longer occurs, so this list can only shrink.
/// </summary>
internal static class LayerRulesAllowlist
{
    public static readonly IReadOnlyList<(string Assembly, string UsedAssembly, string Reason)> Entries =
    [
        // GU-82: settings, bootstrap and setup services use GroundUpDbContext directly
        ("GroundUp.Services", "GroundUp.Data.Postgres", "GU-82"),
        ("GroundUp.Services", "Microsoft.EntityFrameworkCore", "GU-82"),
    ];
}
