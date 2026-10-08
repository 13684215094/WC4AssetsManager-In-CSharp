using System.Text.Json;
using System.Text.Json.Nodes;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Core.Parsers;

// Keep each original row and replace only modeled values the editor changed.
public sealed class JsonTableFile<T> where T : class
{
    private readonly Dictionary<T, (JsonObject Original, JsonObject Modeled)> _rows = new(ReferenceEqualityComparer.Instance);
    private JsonSerializerOptions? _options;
    private string _path = "";
    private string _assetsRoot = "";
    private bool _loaded;

    public void Invalidate() => _loaded = false;

    public List<T> Read(string path, JsonSerializerOptions options)
    {
        _loaded = false;
        _path = Path.GetFullPath(path);
        _assetsRoot = AssetManager.Default.AssetsRoot;
        if (!string.IsNullOrEmpty(_assetsRoot))
        {
            string relative = Path.GetRelativePath(_assetsRoot, _path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Reopen this editor for the active assets directory.");
        }
        string text = File.ReadAllText(_path).TrimStart('\uFEFF');
        var documentOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = options.AllowTrailingCommas,
            CommentHandling = options.ReadCommentHandling
        };
        var array = JsonNode.Parse(text, documentOptions: documentOptions) as JsonArray
            ?? throw new InvalidDataException($"{path} must contain a JSON array.");
        var items = new List<T>();
        var rows = new Dictionary<T, (JsonObject, JsonObject)>(ReferenceEqualityComparer.Instance);
        foreach (var node in array)
        {
            if (node is not JsonObject original) throw new InvalidDataException($"{path} contains a non-object row.");
            if (original.Select(property => property.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != original.Count)
                throw new InvalidDataException($"{path} contains ambiguous property names.");
            var item = original.Deserialize<T>(options) ?? throw new InvalidDataException($"Invalid row in {path}.");
            var modeled = JsonSerializer.SerializeToNode(item, options) as JsonObject
                ?? throw new InvalidDataException($"Invalid model for {path}.");
            rows.Add(item, ((JsonObject)original.DeepClone(), modeled));
            items.Add(item);
        }
        _rows.Clear();
        foreach (var row in rows) _rows.Add(row.Key, row.Value);
        _options = options;
        _loaded = true;
        return items;
    }

    public byte[] Serialize(string path, IEnumerable<T> items)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!_loaded || !File.Exists(_path) || !string.Equals(_path, Path.GetFullPath(path), comparison))
            throw new InvalidOperationException($"Load {path} successfully before saving it.");
        if (!string.Equals(_assetsRoot, AssetManager.Default.AssetsRoot, comparison))
            throw new InvalidOperationException("The active assets directory changed. Reopen this editor before saving.");
        var array = new JsonArray();
        foreach (var item in items)
        {
            if (item == null) throw new InvalidDataException("A JSON table cannot contain null rows.");
            var current = (JsonObject)JsonSerializer.SerializeToNode(item, _options)!;
            if (!_rows.TryGetValue(item, out var row)) { array.Add(current); continue; }
            var result = (JsonObject)row.Original.DeepClone();
            foreach (var property in current)
            {
                if (JsonNode.DeepEquals(property.Value, row.Modeled[property.Key])) continue;
                string? originalKey = result.Select(p => p.Key).FirstOrDefault(key => key.Equals(property.Key, StringComparison.OrdinalIgnoreCase));
                result[originalKey ?? property.Key] = property.Value?.DeepClone();
            }
            array.Add(result);
        }
        return JsonSerializer.SerializeToUtf8Bytes(array, _options);
    }

    public void Save(string path, IEnumerable<T> items)
    {
        AtomicFile.Write(path, Serialize(path, items));
        AssetManager.Default.InvalidateData();
    }
}
