using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using WC4MapEditor.Core.Parsers;

namespace WC4MapEditor.Core.Assets;

public sealed class JsonTableDocument
{
    private static readonly JsonDocumentOptions ParseOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly FileVersionGuard _version = new();
    private readonly string? _activeRoot;
    private JsonArray _baseline = [];
    private bool _bom;
    public string FilePath { get; }
    public JsonArray Rows { get; private set; } = [];
    public bool HasChanges => !JsonNode.DeepEquals(Rows, _baseline);
    public IReadOnlyList<string> Fields => Rows.OfType<JsonObject>().SelectMany(row => row.Select(pair => pair.Key)).Distinct(StringComparer.Ordinal).ToArray();

    public JsonTableDocument(string path)
    {
        FilePath = ProjectFiles.FindProject(path)?.GetEditablePath(path) ?? Path.GetFullPath(path);
        _activeRoot = AssetManager.Default.IsLoaded ? AssetManager.Default.AssetsRoot : null;
        Reload();
    }

    public static bool CanOpen(string path)
    {
        try { _ = Parse(ProjectFiles.ReadAllText(path)); return true; }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    private static JsonArray Parse(string text)
    {
        if (JsonNode.Parse(text.TrimStart('\uFEFF'), documentOptions: ParseOptions) is not JsonArray rows ||
            rows.Any(row => row is not JsonObject))
            throw new InvalidDataException("配置表必须是 JSON 对象数组。");
        // JsonObject materializes lazily; validate duplicate properties before editing.
        foreach (JsonNode? row in rows) ValidateProperties(row);
        return rows;
    }

    private static void ValidateProperties(JsonNode? node)
    {
        if (node is JsonObject obj) foreach (var pair in obj) ValidateProperties(pair.Value);
        else if (node is JsonArray array) foreach (var value in array) ValidateProperties(value);
    }

    public void Reload()
    {
        VerifyProject();
        byte[] bytes = ProjectFiles.ReadAllBytes(FilePath);
        var rows = Parse(new UTF8Encoding(false, true).GetString(bytes));
        _bom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        Rows = rows;
        _baseline = (JsonArray)rows.DeepClone();
        _version.Capture(FilePath);
    }

    public JsonObject GetRow(int index) => Rows[index] as JsonObject ?? throw new InvalidDataException("记录不是对象。");

    public static string GetValueText(JsonNode? value)
        => value is JsonValue v && v.TryGetValue<string>(out var text) ? text : value?.ToJsonString() ?? "null";

    public JsonValueKind? GetFieldType(int index, string field)
    {
        var row = GetRow(index);
        if (row.TryGetPropertyValue(field, out var node) && node != null) return node.GetValueKind();
        var kinds = Rows.OfType<JsonObject>().Where(r => r.TryGetPropertyValue(field, out var n) && n != null)
            .Select(r => r[field]!.GetValueKind()).Select(NormalizeKind).Distinct().ToArray();
        return kinds.Length == 1 ? kinds[0] : null;
    }

    public JsonNode? ParseFieldValue(int index, string field, string text)
    {
        var expected = GetFieldType(index, field);
        if (expected == JsonValueKind.String) return JsonValue.Create(text);
        JsonNode? node;
        try { node = JsonNode.Parse(text, documentOptions: ParseOptions); }
        catch (JsonException ex) { throw new InvalidDataException($"{field} 输入无效：{ex.Message}", ex); }
        if (node != null && expected != null && NormalizeKind(node.GetValueKind()) != NormalizeKind(expected.Value))
            throw new InvalidDataException($"{field} 需要 {expected}，不能写入 {node.GetValueKind()}。");
        if (node?.GetValueKind() == JsonValueKind.Number && RequiresInteger(index, field) && ReadInteger(node) == null)
            throw new InvalidDataException($"{field} 需要整数。");
        if (node is JsonArray array) ValidateArray(index, field, array);
        return node;
    }

    private void ValidateArray(int index, string field, JsonArray array)
    {
        var sample = GetRow(index)[field] as JsonArray;
        if (sample == null || sample.Count == 0)
            sample = Rows.OfType<JsonObject>().Select(row => row[field]).OfType<JsonArray>().FirstOrDefault(value => value.Count > 0);
        if (sample == null) return;
        var kinds = sample.Where(value => value != null).Select(value => NormalizeKind(value!.GetValueKind())).Distinct().ToArray();
        if (kinds.Length != 1) return;
        if (array.Any(value => value != null && NormalizeKind(value.GetValueKind()) != kinds[0]))
            throw new InvalidDataException($"{field} 数组元素需要 {kinds[0]}。");
        if (kinds[0] == JsonValueKind.Number && sample.Where(value => value != null).All(value => ReadInteger(value) != null) &&
            array.Any(value => value != null && ReadInteger(value) == null))
            throw new InvalidDataException($"{field} 数组元素需要整数。");
    }

    private bool RequiresInteger(int index, string field)
    {
        var values = _baseline.OfType<JsonObject>().Select(row => row[field])
            .Where(node => node?.GetValueKind() == JsonValueKind.Number).ToArray();
        if (values.Length == 0) values = Rows.OfType<JsonObject>().Select(row => row[field])
            .Where(node => node?.GetValueKind() == JsonValueKind.Number).ToArray();
        return values.Length > 0 && values.All(node => ReadInteger(node) != null);
    }

    public void ApplyFields(int index, IEnumerable<(string Field, string Text)> fields)
    {
        var replacements = fields.Select(pair => (pair.Field, Value: ParseFieldValue(index, pair.Field, pair.Text))).ToArray();
        foreach (var pair in replacements) GetRow(index)[pair.Field] = pair.Value;
    }

    public void ApplyEdits(int index, IEnumerable<(string Field, string Text, bool IsNull)> fields)
    {
        var replacements = fields.Select(pair => (pair.Field,
            Value: pair.IsNull ? null : ParseFieldValue(index, pair.Field, pair.Text))).ToArray();
        foreach (var pair in replacements) GetRow(index)[pair.Field] = pair.Value;
    }

    public JsonArray CreatePreviewRows(int? index, IEnumerable<(string Field, string Text, bool IsNull)> fields)
    {
        var preview = (JsonArray)Rows.DeepClone();
        if (index == null) return preview;
        var replacements = fields.Select(pair => (pair.Field,
            Value: pair.IsNull ? null : ParseFieldValue(index.Value, pair.Field, pair.Text))).ToArray();
        var row = preview[index.Value]!.AsObject();
        foreach (var pair in replacements) row[pair.Field] = pair.Value;
        return preview;
    }

    public int AddRow(bool clone, int? sourceIndex = null)
    {
        JsonObject row;
        if (clone && sourceIndex != null) row = (JsonObject)GetRow(sourceIndex.Value).DeepClone();
        else
        {
            row = new JsonObject();
            foreach (string field in Fields)
            {
                JsonNode? sample = Rows.OfType<JsonObject>().Select(r => r[field]).FirstOrDefault(n => n != null);
                row[field] = sample?.GetValueKind() switch
                {
                    JsonValueKind.String => JsonValue.Create(""), JsonValueKind.Number => JsonValue.Create(0),
                    JsonValueKind.True or JsonValueKind.False => JsonValue.Create(false),
                    JsonValueKind.Array => new JsonArray(), JsonValueKind.Object => new JsonObject(), _ => null
                };
            }
        }
        var used = Rows.OfType<JsonObject>().Select(row => ReadId(row)).Where(id => id != null).Select(id => id!.Value).ToHashSet();
        if (Fields.Contains("Id") && Rows.OfType<JsonObject>().Any(row => ReadId(row) != null))
        {
            long id = used.Count == 0 ? 1 : checked(used.Max() + 1);
            row["Id"] = id;
        }
        Rows.Add(row);
        return Rows.Count - 1;
    }

    public void RemoveRow(int index) => Rows.RemoveAt(index);

    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        if (Rows.Any(row => row is not JsonObject)) issues.Add("表内存在非对象记录。");
        var before = DuplicateIds(_baseline);
        foreach (var pair in DuplicateIds(Rows))
            if (!before.TryGetValue(pair.Key, out int original) || pair.Value > original)
                issues.Add($"新增了重复 Id：{pair.Key}（{pair.Value} 条）。");
        return issues;
    }

    private static Dictionary<long, int> DuplicateIds(JsonArray rows)
        => rows.OfType<JsonObject>().Select(ReadId).Where(id => id != null).GroupBy(id => id!.Value)
            .Where(group => group.Count() > 1).ToDictionary(group => group.Key, group => group.Count());
    private static long? ReadId(JsonObject row) => ReadInteger(row["Id"]);
    private static long? ReadInteger(JsonNode? node)
    {
        if (node?.GetValueKind() != JsonValueKind.Number) return null;
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return doc.RootElement.TryGetInt64(out long value) ? value : null;
    }
    private static JsonValueKind NormalizeKind(JsonValueKind kind) => kind == JsonValueKind.False ? JsonValueKind.True : kind;

    public void Save()
    {
        VerifyProject();
        _version.Verify();
        if (!HasChanges) return;
        var issues = Validate();
        if (issues.Count > 0) throw new InvalidDataException(string.Join("\n", issues));
        byte[] bytes = Encoding.UTF8.GetBytes(Rows.ToJsonString(WriteOptions) + "\n");
        if (_bom) bytes = [.. Encoding.UTF8.Preamble, .. bytes];
        var savedRows = (JsonArray)Rows.DeepClone();
        ResourceSaveTracker.Register(bytes, _version.Verify, saved =>
        {
            _version.AcceptSaved(saved);
            _baseline = savedRows;
        });
        AtomicFile.Write(FilePath, bytes);
    }

    private void VerifyProject()
    {
        if (_activeRoot != null && (!AssetManager.Default.IsLoaded ||
            !string.Equals(_activeRoot, AssetManager.Default.AssetsRoot, GameProjectWorkspace.PathComparison)))
            throw new InvalidOperationException("项目已经切换或关闭，请从当前项目重新打开配置表。");
    }

    public void Discard() => Rows = (JsonArray)_baseline.DeepClone();
}
