namespace GroundUp.Tests.Unit.Architecture;

/// <summary>
/// Known violations of the layer rules, as (assembly, used assembly) pairs. Every entry names the
/// story that removes it. <see cref="LayerRulesTests"/> fails on a violation that is not listed here,
/// and also on an entry that no longer occurs, so this list can only shrink.
/// Empty since GU-82: every layer rule holds with no exceptions. Add an entry only with a story to remove it.
/// </summary>
internal static class LayerRulesAllowlist
{
    public static readonly IReadOnlyList<(string Assembly, string UsedAssembly, string Reason)> Entries =
    [
    ];
}
