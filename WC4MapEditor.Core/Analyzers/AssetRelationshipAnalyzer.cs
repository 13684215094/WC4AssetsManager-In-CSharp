using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.BTL;

namespace WC4MapEditor.Core.Analyzers;

public sealed record AssetDiagnostic(string Severity, string Code, string Source, int? Row,
    int? Id, string Field, string Message);

public sealed record AssetReference(string Source, int Row, int? Id, string Field,
    string Target, string TargetField, int Value, string Status);

public sealed class AssetAuditReport
{
    public string AssetsRoot { get; init; } = "";
    public string? BaseAssetsRoot { get; init; }
    public bool UsesUnsavedGenerals { get; init; }
    public bool MapsRequested { get; init; }
    public int MapsChecked { get; internal set; }
    public int UnverifiedReinforcements { get; internal set; }
    public Dictionary<string, string> Tables { get; } = new(StringComparer.Ordinal);
    public List<string> Coverage { get; } = [];
    public List<AssetDiagnostic> Diagnostics { get; } = [];
    public List<AssetReference> References { get; } = [];
    public int Errors => Diagnostics.Count(d => d.Severity == "error");
    public int Warnings => Diagnostics.Count(d => d.Severity == "warning");

    public IEnumerable<AssetReference> GeneralReferences(int id) => References.Where(r => r.Value == id &&
        (r.Target == "GeneralSettings.json" && r.TargetField == "Id" ||
         r.Target == "GeneralTitleSettings.json" && r.TargetField == "GeneralId"));

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    public string ToText(int? generalId = null)
    {
        var text = new StringBuilder();
        text.AppendLine($"Assets: {AssetsRoot}");
        if (BaseAssetsRoot != null) text.AppendLine($"Base: {BaseAssetsRoot}");
        text.AppendLine($"Tables: {Tables.Count}; maps checked: {MapsChecked}; errors: {Errors}; warnings: {Warnings}");
        if (UsesUnsavedGenerals) text.AppendLine("GeneralSettings: unsaved editor snapshot");
        foreach (string scope in Coverage) text.AppendLine($"Coverage: {scope}");
        if (generalId.HasValue)
        {
            var references = GeneralReferences(generalId.Value).ToList();
            text.AppendLine($"General {generalId}: {references.Count} incoming references (within reported coverage)");
            foreach (var r in references)
                text.AppendLine($"  {r.Source} row {r.Row} Id={r.Id} {r.Field} -> {r.Target}.{r.TargetField}={r.Value} [{r.Status}]");
        }
        else
            foreach (var d in Diagnostics)
                text.AppendLine($"{d.Severity} {d.Code}: {d.Source} row {d.Row} Id={d.Id} {d.Field}: {d.Message}");
        return text.ToString();
    }

    public void Save(string output)
    {
        string full = Path.GetFullPath(output);
        foreach (string root in new[] { AssetsRoot, BaseAssetsRoot }.OfType<string>())
        {
            string relative = Path.GetRelativePath(PhysicalDirectory(root), full);
            if (relative == "." || !Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException("Audit reports must be written outside input assets directories.");
        }
        // Existing destinations could be assets reached through symlinks/hardlinks.
        if (File.Exists(full) || Directory.Exists(full))
            throw new IOException("Choose a new report filename; existing files are never overwritten.");
        var parent = new DirectoryInfo(Path.GetDirectoryName(full)!);
        for (var dir = parent; dir != null; dir = dir.Parent)
            if (dir.LinkTarget != null) throw new IOException("Report output cannot traverse directory links.");
        Directory.CreateDirectory(parent.FullName);
        using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(Encoding.UTF8.GetBytes(ToJson()));
    }

    private static string PhysicalDirectory(string path)
    {
        var dir = new DirectoryInfo(path);
        string physical = dir.Parent == null ? dir.FullName : Path.Combine(PhysicalDirectory(dir.Parent.FullName), dir.Name);
        return new DirectoryInfo(physical).ResolveLinkTarget(true)?.FullName ?? physical;
    }
}

/// <summary>Read-only checks for explicitly known JSON links and BTL army references.</summary>
public sealed class AssetRelationshipAnalyzer
{
    private sealed record Rule(string Source, string Field, string Target, string Key = "Id",
        bool Array = false, bool ZeroIsEmpty = true);
    private static readonly Rule[] Rules =
    [
        new("GeneralSettings", "Skills", "SkillSettings", Array: true),
        new("GeneralPromotionSettings", "BaseID", "GeneralSettings", ZeroIsEmpty: false),
        new("GeneralPromotionSettings", "Skills", "SkillSettings", Array: true),
        new("GeneralPromotionSettings", "AdvanceID", "GeneralPromotionSettings"),
        new("GeneralTitleSettings", "GeneralId", "GeneralSettings", ZeroIsEmpty: false),
        new("GeneralStageSettings", "GeneralId", "GeneralTitleSettings", "GeneralId", ZeroIsEmpty: false),
        new("GeneralStageSettings", "UnlockStageId", "GeneralStageSettings"),
        new("SkillSettings", "UpgradeId", "SkillSettings"),
        new("EliteArmySettings", "ArmyId", "ArmySettings", ZeroIsEmpty: false),
        new("ArmySettings", "Feature", "ArmyFeatureSettings", "Type", Array: true, ZeroIsEmpty: false),
        new("ArmyFeatureSettings", "RelatedArmyId", "ArmySettings"),
        new("ArmyFeatureSettings", "AntiairId", "AirDefenceSettings"),
        new("ArmyFeatureSettings", "ArmyBuff", "ArmyBuffSettings"),
        new("BuildingSettings", "ArmyId", "ArmySettings"),
        new("FacilitySettings", "UnlockArmy", "ArmySettings", "Army", Array: true),
        new("ItemSettings", "Army", "ArmySettings", "Army"),
        new("EliteSkinSettings", "Army", "ArmySettings", "Army", ZeroIsEmpty: false),
        new("LegionSettings", "Army", "ArmySettings", "Army", ZeroIsEmpty: false),
        new("CitySettings", "GeneralId", "GeneralSettings"),
        new("StageSettings", "GeneralId", "GeneralSettings"),
        new("StageSettings", "PrizeGeneralId", "GeneralSettings"),
        new("StageSettings", "UnlockedGeneralId", "GeneralSettings"),
        new("ScenarioSettings", "GeneralId", "GeneralSettings", Array: true),
        new("ArmyGroupSettings", "GeneralId", "GeneralSettings", Array: true),
        new("EventSettings", "GeneralId", "GeneralSettings"),
        new("EventStageSettings", "GeneralID", "GeneralSettings"),
        new("FrontierStageSetting", "General", "GeneralSettings"),
        new("PaySettings", "PrizeGeneralId", "GeneralSettings"),
        new("PaySettings", "PrizeGeneralId2", "GeneralSettings"),
        new("LoginRewardSettings", "PrizeGeneralId", "GeneralSettings"),
        new("ConquerPassSettings", "GeneralId", "GeneralSettings"),
        new("StarterPassSettings", "GeneralId", "GeneralSettings")
    ];

    private readonly Dictionary<string, JsonArray> _tables = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string), Dictionary<int, List<(int Row, JsonObject Data)>>> _indices = [];
    private readonly HashSet<string> _unavailable = new(StringComparer.Ordinal);
    private AssetAuditReport _report = null!;
    private CancellationToken _cancellation;

    // Use a fresh instance per run; parsing does not use or change editor caches.
    public AssetAuditReport Analyze(string assetsRoot, string? baseAssetsRoot = null,
        bool includeMaps = false, JsonArray? generalSnapshot = null, CancellationToken cancellationToken = default)
    {
        _cancellation = cancellationToken;
        _cancellation.ThrowIfCancellationRequested();
        _tables.Clear(); _indices.Clear(); _unavailable.Clear();
        _report = new AssetAuditReport
        {
            AssetsRoot = ResolveRoot(assetsRoot),
            BaseAssetsRoot = baseAssetsRoot == null ? null : ResolveRoot(baseAssetsRoot),
            MapsRequested = includeMaps, UsesUnsavedGenerals = generalSnapshot != null
        };
        foreach (var file in EffectiveFiles("json", "*.json"))
        {
            _cancellation.ThrowIfCancellationRequested();
            string name = Path.GetFileName(file.Key);
            _report.Tables[name] = file.Value;
            try
            {
                if (new FileInfo(file.Value).Length > 32 * 1024 * 1024)
                    throw new InvalidDataException("Table exceeds the 32 MiB audit budget.");
                var rows = name == "GeneralSettings.json" && generalSnapshot != null ? generalSnapshot.DeepClone().AsArray() :
                    JsonNode.Parse(File.ReadAllText(file.Value), documentOptions: new JsonDocumentOptions
                    { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonArray;
                if (rows == null || rows.Any(row => row is not JsonObject))
                    throw new InvalidDataException("Expected an array of objects.");
                foreach (var row in rows) _ = row!.AsObject().Count;
                _tables.Add(name, rows);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException or UnauthorizedAccessException)
            { Add("error", "table-unreadable", name, null, null, "", ex.Message); }
        }
        if (generalSnapshot != null && !_report.Tables.ContainsKey("GeneralSettings.json"))
            throw new InvalidDataException("Unsaved general snapshot requires a loaded GeneralSettings table.");
        foreach (var (name, rows) in _tables)
        {
            _cancellation.ThrowIfCancellationRequested();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i]!.AsObject();
                if (row.ContainsKey("Id") && Int(row["Id"]) == null)
                    Add("error", "id-type", name, i + 1, null, "Id", "Expected an Int32 ID.");
            }
            foreach (var (id, matches) in Index(name, "Id").Where(pair => pair.Value.Count > 1))
                Add("warning", "duplicate-id", name, matches[0].Row, id, "Id", $"{matches.Count} records share this ID; no record was merged.");
        }
        foreach (var rule in Rules) { _cancellation.ThrowIfCancellationRequested(); CheckRule(rule); }
        CheckChain("SkillSettings.json", "UpgradeId", "Type");
        CheckChain("GeneralPromotionSettings.json", "AdvanceID", "BaseID");
        CheckChain("GeneralStageSettings.json", "UnlockStageId", "GeneralId");
        CheckFeatures();
        CheckGeneralSlots();
        if (includeMaps) CheckMaps();
        _report.Coverage.Add($"{Rules.Length} explicit JSON relationships; absent fields are not inferred. Row numbers are 1-based.");
        _report.Coverage.Add("Overlay replaces a whole file, never merges rows; malformed overlay files do not fall back to base.");
        _report.Coverage.Add(includeMaps ? $"BTL coverage: deployed armies only; {_report.UnverifiedReinforcements} reinforcement records skipped (field meanings unverified). Event/opaque/native/save references are not exhaustive." :
            "BTL files not scanned. No conclusions about map references.");
        _report.Coverage.Add("No image/XML/localization completeness or Android runtime verification; no automatic fixes.");
        if (generalSnapshot != null) _report.Coverage.Add("GeneralSettings uses the unsaved editor snapshot; other inputs come from disk.");
        return _report;
    }

    public static string ResolveRoot(string input)
    {
        string root = Path.GetFullPath(input);
        if (Directory.Exists(Path.Combine(root, "json"))) return root;
        if (Directory.Exists(Path.Combine(root, "assets", "json"))) return Path.Combine(root, "assets");
        throw new DirectoryNotFoundException($"Expected an assets directory containing json/: {root}");
    }

    private SortedDictionary<string, string> EffectiveFiles(string directory, string pattern)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string root in new[] { _report.BaseAssetsRoot, _report.AssetsRoot }.OfType<string>())
        {
            string path = Path.Combine(root, directory);
            if (!Directory.Exists(path)) continue;
            foreach (string file in Directory.EnumerateFiles(path, pattern, directory == "json" ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories))
                files[Path.GetRelativePath(root, file).Replace('\\', '/')] = file;
        }
        return files;
    }

    private static int? Int(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<int>(out int number) ? number : null;

    private Dictionary<int, List<(int Row, JsonObject Data)>> Index(string table, string field)
    {
        if (_indices.TryGetValue((table, field), out var cached)) return cached;
        var result = new Dictionary<int, List<(int, JsonObject)>>();
        if (_tables.TryGetValue(table, out var rows))
            for (int i = 0; i < rows.Count; i++)
                if (Int(rows[i]![field]) is int id)
                {
                    if (!result.TryGetValue(id, out var list)) result[id] = list = [];
                    list.Add((i + 1, rows[i]!.AsObject()));
                }
        _indices[(table, field)] = result;
        return result;
    }

    private void CheckRule(Rule rule)
    {
        string source = rule.Source + ".json", target = rule.Target + ".json";
        if (!_tables.TryGetValue(source, out var rows)) return;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i]!.AsObject();
            if (row[rule.Field] == null) continue;
            if (!rule.Array) CheckValue(source, i + 1, Int(row["Id"]), rule.Field, row[rule.Field], target, rule.Key, rule.ZeroIsEmpty);
            else if (row[rule.Field] is JsonArray values)
                for (int j = 0; j < values.Count; j++)
                    CheckValue(source, i + 1, Int(row["Id"]), $"{rule.Field}[{j}]", values[j], target, rule.Key, rule.ZeroIsEmpty);
            else Add("error", "reference-shape", source, i + 1, Int(row["Id"]), rule.Field, "Expected an ID array or null.");
        }
    }

    private void CheckValue(string source, int row, int? id, string field, JsonNode? node,
        string target, string key, bool zero)
    {
        if (Int(node) is not int value || value < 0)
        { Add("error", "reference-type", source, row, id, field, "Expected a non-negative Int32 ID."); return; }
        if (zero && value == 0) return;
        string status = "resolved";
        if (!_tables.ContainsKey(target))
        {
            status = "unchecked";
            if (_unavailable.Add(target)) Add("warning", "target-unavailable", target, null, null, key,
                "Target table absent or unreadable; supply matching base assets for an overlay. Its references are unchecked.");
        }
        else if (!Index(target, key).TryGetValue(value, out var matches))
        {
            status = "missing";
            Add("error", "missing-reference", source, row, id, field, $"No {target}.{key}={value}.");
        }
        else if (key == "Id" && matches.Count > 1)
        {
            status = "ambiguous";
            Add("warning", "ambiguous-reference", source, row, id, field, $"{target}.{key}={value} matches {matches.Count} rows.");
        }
        _report.References.Add(new AssetReference(source, row, id, field, target, key, value, status));
    }

    private void CheckChain(string table, string field, string owner)
    {
        if (!_tables.ContainsKey(table)) return;
        var nodes = Index(table, "Id").Where(p => p.Value.Count == 1).ToDictionary(p => p.Key, p => p.Value[0]);
        var graph = new Dictionary<int, int>();
        foreach (var (id, source) in nodes)
        {
            if (Int(source.Data[field]) is not int next || next == 0 || !nodes.TryGetValue(next, out var target)) continue;
            graph[id] = next;
            if (Int(source.Data[owner]) is int originalOwner && Int(target.Data[owner]) is int nextOwner && originalOwner != nextOwner)
                Add("error", "chain-owner", table, source.Row, id, field, $"Next record {next} has a different {owner}.");
        }
        var visited = new HashSet<int>();
        foreach (int start in graph.Keys)
        {
            var positions = new Dictionary<int, int>();
            var path = new List<int>();
            int current = start;
            while (!visited.Contains(current) && graph.TryGetValue(current, out int next))
            {
                if (positions.TryGetValue(current, out int cycleStart))
                {
                    string cycle = string.Join(" -> ", path.Skip(cycleStart).Append(current));
                    Add("error", "chain-cycle", table, nodes[current].Row, current, field, cycle);
                    break;
                }
                positions[current] = path.Count; path.Add(current); current = next;
            }
            visited.UnionWith(path);
        }
    }

    private void CheckFeatures()
    {
        if (!_tables.TryGetValue("ArmySettings.json", out var rows)) return;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i]!;
            if (row["FeatureLevel"] == null) continue;
            if (row["FeatureLevel"] is not JsonArray levels || row["Feature"] is not JsonArray features)
            { Add("error", "feature-shape", "ArmySettings.json", i + 1, Int(row["Id"]), "FeatureLevel", "Feature and FeatureLevel must be arrays."); continue; }
            if (levels.Count == 0) continue; // Ordinary units use implicit levels.
            if (levels.Count != features.Count)
            { Add("error", "feature-length", "ArmySettings.json", i + 1, Int(row["Id"]), "FeatureLevel", "Feature/FeatureLevel lengths differ."); continue; }
            for (int j = 0; j < levels.Count; j++)
            {
                if (Int(levels[j]) is not int level || level < 1)
                    Add("error", "feature-level", "ArmySettings.json", i + 1, Int(row["Id"]), $"FeatureLevel[{j}]", "Expected a positive level.");
                else if (_tables.ContainsKey("ArmyFeatureSettings.json") && Int(features[j]) is int type &&
                    (!Index("ArmyFeatureSettings.json", "Type").TryGetValue(type, out var matches) || !matches.Any(m => Int(m.Data["Level"]) == level)))
                    Add("error", "missing-feature-level", "ArmySettings.json", i + 1, Int(row["Id"]), $"FeatureLevel[{j}]", $"No feature Type={type}, Level={level}.");
            }
        }
    }

    private void CheckGeneralSlots()
    {
        foreach (string table in new[] { "GeneralSettings.json", "GeneralPromotionSettings.json" })
        {
            if (!_tables.TryGetValue(table, out var rows)) continue;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i]!;
                if (row["Skills"] is not JsonArray skills) continue;
                if (Int(row["SkillsMax"]) is int max && skills.Count > max)
                    Add("error", "skill-max", table, i + 1, Int(row["Id"]), "Skills", $"{skills.Count} entries exceed SkillsMax={max}.");
                if (skills.Count > 5)
                    Add("warning", "btl-skill-slots", table, i + 1, Int(row["Id"]), "Skills", "More than five slots; editor BTL auto-assignment skips this record.");
            }
        }
    }

    private void CheckMaps()
    {
        foreach (var (relative, full) in EffectiveFiles("stage", "*.btl"))
        {
            _cancellation.ThrowIfCancellationRequested();
            try
            {
                var map = BTLParser.LoadFromFile(full);
                _report.MapsChecked++;
                void Unit(string kind, int index, int general, int army)
                {
                    CheckValue(relative, index + 1, null, kind + ".General", JsonValue.Create(general), "GeneralSettings.json", "Id", true);
                    CheckValue(relative, index + 1, null, kind + ".UnitType", JsonValue.Create(army), "ArmySettings.json", "Army", true);
                }
                for (int i = 0; i < map.Armies.Count; i++) Unit("Armies", i, map.Armies[i].General, map.Armies[i].UnitType);
                for (int i = 0; i < map.ArmiesV3.Count; i++) Unit("ArmiesV3", i, map.ArmiesV3[i].General, map.ArmiesV3[i].UnitType);
                // Known record strides do not establish the meaning of each word.
                _report.UnverifiedReinforcements += map.Reinforcements.Count + map.ReinforcementsV3.Count;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or OverflowException or UnauthorizedAccessException)
            { Add("error", "map-unreadable", relative, null, null, "", ex.Message); }
        }
    }

    private void Add(string severity, string code, string source, int? row, int? id, string field, string message)
        => _report.Diagnostics.Add(new AssetDiagnostic(severity, code, source, row, id, field, message));
}
