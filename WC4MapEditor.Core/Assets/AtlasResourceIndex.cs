using WC4MapEditor.Core.Parsers.HdAtlas;

namespace WC4MapEditor.Core.Assets;

public sealed class AtlasResourceIndex
{
    public sealed record Tile(string Atlas, HdAtlasImageDef Definition);
    private readonly Dictionary<string, Tile> _tiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<Tile>> _candidates = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _atlases = [];
    public IReadOnlyList<string> Atlases => _atlases;
    public int Count => _tiles.Count;

    public AtlasResourceIndex(AssetManager manager)
    {
        if (!manager.IsLoaded || !AssetManager.Default.IsLoaded ||
            !string.Equals(manager.AssetsRoot, AssetManager.Default.AssetsRoot, GameProjectWorkspace.PathComparison))
            throw new InvalidOperationException("请在当前活动项目中创建图集索引。");
        // Layouts use HD artwork when both resolutions define the same tile.
        foreach (var entry in manager.ListByExtension("xml")
            .OrderBy(entry => entry.FileNameWithoutExtension.EndsWith("_hd", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            string text;
            try { text = manager.ReadText(entry); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (!text.Contains("<Texture", StringComparison.OrdinalIgnoreCase) ||
                !text.Contains("<Image", StringComparison.OrdinalIgnoreCase) && !text.Contains("<SubTexture", StringComparison.OrdinalIgnoreCase)) continue;
            string atlasName = entry.RelativePath.Replace('\\', '/')[..^4];
            var parser = HdAtlasParser.Get(atlasName);
            if (!parser.IsLoaded) continue;
            _atlases.Add(atlasName);
            foreach (var definition in parser.ImageDefinitions)
            {
                var tile = new Tile(atlasName, definition);
                if (!_candidates.TryGetValue(definition.Name, out var candidates))
                    _candidates[definition.Name] = candidates = [];
                candidates.Add(tile);
            }
        }
        foreach (var (name, candidates) in _candidates)
            _tiles[name] = Order(candidates).First();
    }

    public Tile? Resolve(string imageReference)
    {
        return ResolveCandidates(imageReference).FirstOrDefault();
    }

    public IReadOnlyList<Tile> ResolveCandidates(string imageReference)
    {
        string normalized = imageReference.Replace('\\', '/');
        string bare = normalized.Split('/')[^1];
        if (!_candidates.TryGetValue(normalized, out var candidates) && !_candidates.TryGetValue(bare, out candidates)) return [];
        int slash = normalized.LastIndexOf('/');
        if (slash >= 0)
        {
            string scope = normalized[..slash];
            var scoped = candidates.Where(tile => tile.Atlas.Equals(scope, StringComparison.OrdinalIgnoreCase) ||
                tile.Atlas.Equals("image/" + scope, StringComparison.OrdinalIgnoreCase) ||
                Path.GetDirectoryName(tile.Atlas)?.Replace('\\', '/').Equals(scope, StringComparison.OrdinalIgnoreCase) == true).ToArray();
            if (scoped.Length != 0) return Order(scoped).ToArray();
            if (!scope.Equals("image", StringComparison.OrdinalIgnoreCase)) return [];
        }
        return Order(candidates).ToArray();
    }

    private static IOrderedEnumerable<Tile> Order(IEnumerable<Tile> candidates) => candidates
        .OrderByDescending(tile => tile.Atlas.EndsWith("_hd", StringComparison.OrdinalIgnoreCase) ||
            tile.Atlas.Contains("_hd", StringComparison.OrdinalIgnoreCase))
        .ThenByDescending(tile => Path.GetFileName(tile.Atlas).StartsWith("image_ui", StringComparison.OrdinalIgnoreCase))
        .ThenByDescending(tile => tile.Atlas, StringComparer.OrdinalIgnoreCase);

    public IEnumerable<(string Name, Tile Tile)> Tiles => _tiles.Select(pair => (pair.Key, pair.Value));
}
