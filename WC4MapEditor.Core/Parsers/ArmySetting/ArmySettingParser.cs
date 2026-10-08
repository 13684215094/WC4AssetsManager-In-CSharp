using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Xml;
using System.Xml.Linq;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers.General;

namespace WC4MapEditor.Core.Parsers.ArmySetting;

/// <summary>
/// 兵种解析器（三文件联动：ArmySettings.json + config/def_armypos.xml +
/// stringtable_tw.ini 的 unit_name_{Id}）。
/// 采用与 GeneralSettingParser 相同的单例、路径解析与 XML 读写约定。
/// </summary>
public class ArmySettingParser
{
    private static readonly object _lock = new();
    private static ArmySettingParser? _instance;
    public static ArmySettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ArmySettingParser();
                }
            }
            return _instance;
        }
    }

    private readonly JsonTableFile<ArmySettingData> _unitsFile = new();
    private readonly AssetManager _manager = AssetManager.Default;
    private List<ArmySettingData> _units = new();
    private StringTableParser? _stringTable;
    private readonly Dictionary<int, ArmyPosEntry> _pos = new();
    private XDocument? _positionDocument;
    private readonly Dictionary<int, (int X, int Y, double Scale)> _positionBaseline = new();
    private bool _positionsLoaded;

    public IReadOnlyList<ArmySettingData> All => _units;
    public List<ArmySettingData> Items => _units;
    public IReadOnlyDictionary<int, ArmyPosEntry> Positions => _pos;

    public string ConfigPath { get; private set; } = "";
    public string XmlPosPath { get; private set; } = "";
    public string StringTablePath { get; private set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new SafeInt32Converter(), new SafeIntListConverter() }
    };

    private ArmySettingParser()
    {
        ResolvePaths();
        LoadAll();
    }

    private void ResolvePaths()
    {
        if (!_manager.IsLoaded)
        {
            try { _manager.ScanDefault(); } catch { }
        }

        var root = _manager.AssetsRoot;
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            ConfigPath = Path.Combine(root, "json", "ArmySettings.json");
            XmlPosPath = Path.Combine(root, "config", "def_armypos.xml");
            StringTablePath = Path.Combine(root, "stringtable_tw.ini");
        }
        else
        {
            var baseDir = AppContext.BaseDirectory;
            string? candidate = null;
            string? current = baseDir;
            for (int i = 0; i < 5 && !string.IsNullOrEmpty(current); i++)
            {
                var test = Path.Combine(current, "Resource", "WC4DATA", "assets");
                if (Directory.Exists(test)) { candidate = test; break; }
                current = Path.GetDirectoryName(current);
            }
            candidate ??= Path.GetFullPath(Path.Combine(baseDir ?? "", "..", "..", "..", "..", "Resource", "WC4DATA", "assets"));

            ConfigPath = Path.Combine(candidate, "json", "ArmySettings.json");
            XmlPosPath = Path.Combine(candidate, "config", "def_armypos.xml");
            StringTablePath = Path.Combine(candidate, "stringtable_tw.ini");
        }

        Debug.WriteLine($"[ArmySettingParser] ConfigPath={ConfigPath} XmlPosPath={XmlPosPath} StringTablePath={StringTablePath}");
    }

    public void LoadAll()
    {
        _unitsFile.Invalidate();
        LoadUnits();
        LoadXmlPos();
        _stringTable = new StringTableParser(StringTablePath);
    }

    private void LoadUnits()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Debug.WriteLine($"[ArmySettingParser] 文件不存在: {ConfigPath}");
                return;
            }
            var data = _unitsFile.Read(ConfigPath, JsonOpts);
            if (data != null) _units = data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ArmySettingParser] 加载失败: {ex.Message}");
        }
    }

    public ArmySettingData? GetById(int id)
    {
        foreach (var u in _units) if (u.Id == id) return u;
        return null;
    }

    public int GetNextId()
    {
        int max = 1000;
        foreach (var u in _units) if (u.Id > max) max = u.Id;
        return max + 1;
    }

    public bool RemoveUnit(int id)
    {
        var u = GetById(id);
        if (u == null) return false;
        _units.Remove(u);
        _pos.Remove(id);
        _stringTable?.Remove($"unit_name_{id}");
        return true;
    }

    /// <summary>获取兵种名称（stringtable 的 unit_name_{Id}）。</summary>
    public string GetUnitName(int id) => _stringTable?.GetValue($"unit_name_{id}", "") ?? "";

    /// <summary>设置兵种名称（写入 stringtable 的 unit_name_{Id}）。</summary>
    public void SetUnitName(int id, string name) => _stringTable?.SetValue($"unit_name_{id}", name);

    // ============================== def_armypos.xml ==============================

    public void LoadXmlPos()
    {
        _positionsLoaded = false;
        try
        {
            var document = File.Exists(XmlPosPath) ? XDocument.Load(XmlPosPath, LoadOptions.PreserveWhitespace)
                : new XDocument(new XElement("units"));
            var root = document.Element("units") ?? throw new InvalidDataException("Expected a units root.");
            var positions = new Dictionary<int, ArmyPosEntry>();
            foreach (var element in root.Elements("unit"))
            {
                int id = (int?)element.Attribute("id") ?? throw new InvalidDataException("Missing unit id.");
                var entry = new ArmyPosEntry
                {
                    Id = id, PosX = (int?)element.Attribute("x") ?? 0,
                    PosY = (int?)element.Attribute("y") ?? 0, Scale = (double?)element.Attribute("scale") ?? 1.0
                };
                if (!double.IsFinite(entry.Scale) || entry.Scale <= 0 || !positions.TryAdd(id, entry))
                    throw new InvalidDataException("Invalid or duplicate unit position.");
            }
            _pos.Clear();
            _positionBaseline.Clear();
            foreach (var entry in positions.Values)
            {
                _pos.Add(entry.Id, entry);
                _positionBaseline.Add(entry.Id, (entry.PosX, entry.PosY, entry.Scale));
            }
            _positionDocument = document;
            _positionsLoaded = true;
        }
        catch (Exception ex) { Debug.WriteLine($"[ArmySettingParser] Position load failed: {ex.Message}"); }
    }

    public ArmyPosEntry? GetPos(int id) => _pos.GetValueOrDefault(id);

    public ArmyPosEntry EnsurePosDefault(int id)
    {
        if (!_pos.TryGetValue(id, out var p))
        {
            p = new ArmyPosEntry { Id = id, PosX = 0, PosY = 0, Scale = 1.0 };
            _pos[id] = p;
        }
        return p;
    }

    public bool RemovePos(int id) => _pos.Remove(id);

    private byte[] SerializePositions()
    {
        if (!_positionsLoaded || _positionDocument == null) throw new InvalidOperationException("Load unit positions successfully before saving.");
        var document = new XDocument(_positionDocument);
        var root = document.Root!;
        foreach (var element in root.Elements("unit").ToList())
            if (!_pos.ContainsKey((int)element.Attribute("id")!)) element.Remove();
        foreach (var entry in _pos.Values)
        {
            if (!double.IsFinite(entry.Scale) || entry.Scale <= 0) throw new InvalidDataException("Invalid unit scale.");
            var element = root.Elements("unit").FirstOrDefault(node => (int)node.Attribute("id")! == entry.Id);
            bool created = element == null;
            if (created) { element = new XElement("unit", new XAttribute("id", entry.Id)); root.Add(element); }
            var baseline = _positionBaseline.GetValueOrDefault(entry.Id);
            if (created || baseline.X != entry.PosX) element!.SetAttributeValue("x", entry.PosX);
            if (created || baseline.Y != entry.PosY) element!.SetAttributeValue("y", entry.PosY);
            if (created || baseline.Scale != entry.Scale) element!.SetAttributeValue("scale", entry.Scale.ToString("R", CultureInfo.InvariantCulture));
        }
        using var stream = new MemoryStream();
        document.Save(stream, SaveOptions.DisableFormatting);
        return stream.ToArray();
    }

    public bool SaveXmlPos()
    {
        try
        {
            _unitsFile.Serialize(ConfigPath, _units);
            AtomicFile.Write(XmlPosPath, SerializePositions());
            return true;
        }
        catch (Exception ex) { Debug.WriteLine($"[ArmySettingParser] Position save failed: {ex.Message}"); return false; }
    }

    public bool SaveAll()
    {
        try
        {
            AtomicFile.WriteAllWithStringTable(_stringTable,
                (ConfigPath, _unitsFile.Serialize(ConfigPath, _units)), (XmlPosPath, SerializePositions()));
            return true;
        }
        catch (Exception ex) { Debug.WriteLine($"[ArmySettingParser] Save failed: {ex.Message}"); return false; }
    }

    public void Reload() => LoadAll();

    public static void ClearInstance()
    {
        lock (_lock) { _instance = null; }
    }
}

/// <summary>
/// 兵种节点（ArmySettings.json）。
/// 名称不保存在 JSON 中，而是写入 stringtable_tw.ini 的 unit_name_{Id} 键。
/// </summary>
public class ArmySettingData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("Anim")] public string Anim { get; set; } = "";
    [JsonPropertyName("Army")] public int Army { get; set; }
    [JsonPropertyName("SubType")] public int SubType { get; set; }
    [JsonPropertyName("Elite")] public int Elite { get; set; }
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("Ranking")] public int Ranking { get; set; }
    [JsonPropertyName("Feature")] public List<int> Feature { get; set; } = new();
    [JsonPropertyName("FeatureLevel")] public List<int> FeatureLevel { get; set; } = new();
    [JsonPropertyName("MinAttack")] public int MinAttack { get; set; }
    [JsonPropertyName("MaxAttack")] public int MaxAttack { get; set; }
    [JsonPropertyName("MinRange")] public int MinRange { get; set; }
    [JsonPropertyName("MaxRange")] public int MaxRange { get; set; }
    [JsonPropertyName("HP")] public int HP { get; set; }
    [JsonPropertyName("Defence")] public int Defence { get; set; }
    [JsonPropertyName("Mobility")] public int Mobility { get; set; }
    [JsonPropertyName("CostMoney")] public int CostMoney { get; set; }
    [JsonPropertyName("CostGear")] public int CostGear { get; set; }
    [JsonPropertyName("CostAtomic")] public int CostAtomic { get; set; }
    [JsonPropertyName("CostPoints")] public int CostPoints { get; set; }
    [JsonPropertyName("MaxElite")] public int MaxElite { get; set; }
    [JsonPropertyName("MaxFormation")] public int MaxFormation { get; set; }
    [JsonPropertyName("Carrier")] public int Carrier { get; set; }
    [JsonPropertyName("BuildTime")] public int BuildTime { get; set; }
    [JsonPropertyName("BuildCD")] public int BuildCD { get; set; }
    [JsonPropertyName("AOE1")] public int AOE1 { get; set; }
    [JsonPropertyName("AOE2")] public int AOE2 { get; set; }
    [JsonPropertyName("Country")] public List<int> Country { get; set; } = new();
    [JsonPropertyName("FormationChance")] public List<int> FormationChance { get; set; } = new();
    [JsonPropertyName("MeritExp")] public int MeritExp { get; set; }
    [JsonPropertyName("Formation")] public bool Formation { get; set; }
}

/// <summary>def_armypos.xml 中单个兵种的展示坐标与缩放。</summary>
public class ArmyPosEntry
{
    public int Id { get; set; }
    public int PosX { get; set; }
    public int PosY { get; set; }
    public double Scale { get; set; } = 1.0;
}
