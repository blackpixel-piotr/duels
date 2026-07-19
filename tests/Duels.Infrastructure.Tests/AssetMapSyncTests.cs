using System.Text.Json;
using Xunit;

namespace Duels.Infrastructure.Tests;

// asset-map.md (repo root) must mirror asset-manifest.json — the file
// toon.js actually loads (see loadAssetManifest in wwwroot/js/toon.js). The
// implementation brief requires the human-readable manifest to stay current
// with every content addition; this fails loudly if one file is updated and
// the other is forgotten.
public class AssetMapSyncTests
{
    private sealed record AssetRow(string ItemId, string DisplayName, string ModelRef);
    private sealed record Manifest(List<AssetRow> Weapons, List<AssetRow> Armor);

    private const string EquipDir = "assets/models/equip/";

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Duels.sln")))
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        return dir ?? throw new InvalidOperationException("Could not locate repo root (Duels.sln not found).");
    }

    private static Manifest LoadManifest(string root)
    {
        var manifestPath = Path.Combine(root, "src", "Duels.Web", "wwwroot", "data", "asset-manifest.json");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), options)!;
    }

    [Fact]
    public void AssetMapMd_ContainsEveryManifestItem_WithMatchingModelRef()
    {
        var root = RepoRoot();
        var manifest = LoadManifest(root);
        var mapContent = File.ReadAllText(Path.Combine(root, "asset-map.md"));

        foreach (var row in manifest.Weapons.Concat(manifest.Armor))
        {
            var expectedRow = $"| {row.ItemId} | {row.DisplayName} | {EquipDir}{row.ModelRef} |";
            Assert.Contains(expectedRow, mapContent);
        }
    }

    [Fact]
    public void AssetManifest_HasNoDuplicateItemIds()
    {
        var manifest = LoadManifest(RepoRoot());

        var dupes = manifest.Weapons.Concat(manifest.Armor)
            .GroupBy(r => r.ItemId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(dupes);
    }
}
