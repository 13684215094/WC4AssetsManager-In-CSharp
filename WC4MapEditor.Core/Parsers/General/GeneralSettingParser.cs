using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using System.Xml;
using System.Xml.Linq;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.General;

public class GeneralSettingParser
{
    private static readonly object _lock = new();
    private static GeneralSettingParser? _instance;
    public static GeneralSettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new GeneralSettingParser();
                }
            }
            return _instance;
        }
    }

    private readonly AssetManager _manager;
    private readonly string? _explicitRoot;
    private bool _generalsLoaded;
    private bool _portraitsLoaded;
    private readonly Dictionary<GeneralSettingData, (JsonObject Original, JsonObject Baseline, int Id)> _original = new();
    private XDocument _portraitDocument = new(new XElement("Portraits"));
    public string? LastError { get; private set; }

    private List<GeneralSettingData> _data = new();
    public IReadOnlyList<GeneralSettingData> All => _data;

    public string ConfigPath { get; private set; } = "";
    public string PortraitPosPath { get; private set; } = "";
    public string GeneralPhotoDir { get; private set; } = "";
    public string HeadsDir { get; private set; } = "";

    private readonly Dictionary<string, PortraitPosEntry> _portraits = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, PortraitPosEntry> Portraits => _portraits;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private GeneralSettingParser() : this(null, AssetManager.Default) { }

    public GeneralSettingParser(string? assetsRoot, AssetManager? manager = null)
    {
        _manager = manager ?? AssetManager.Default;
        _explicitRoot = assetsRoot == null ? null : Path.GetFullPath(assetsRoot);
        LoadAll();
    }

    private void ResolvePaths()
    {
        // 如果 AssetManager 未扫描，先尝试默认路径扫描
        if (_explicitRoot == null && !_manager.IsLoaded)
        {
            try { _manager.ScanDefault(); } catch { /* 静默处理 */ }
        }

        var root = _explicitRoot ?? _manager.AssetsRoot;
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            ConfigPath = Path.Combine(root, "json", "GeneralSettings.json");
            PortraitPosPath = Path.Combine(root, "config", "def_portraitpos.xml");
            GeneralPhotoDir = Path.Combine(root, "image", "generalphoto");
            HeadsDir = Path.Combine(root, "image", "heads");
        }
        else
        {
            // 兜底：从执行目录向上搜索 Resource/WC4DATA/assets
            string candidate = _explicitRoot ?? AssetManager.GetDefaultAssetsPath();

            ConfigPath = Path.Combine(candidate, "json", "GeneralSettings.json");
            PortraitPosPath = Path.Combine(candidate, "config", "def_portraitpos.xml");
            GeneralPhotoDir = Path.Combine(candidate, "image", "generalphoto");
            HeadsDir = Path.Combine(candidate, "image", "heads");
        }

        Debug.WriteLine($"[GeneralSettingParser] 解析路径:");
        Debug.WriteLine($"  ConfigPath      = {ConfigPath}");
        Debug.WriteLine($"  PortraitPosPath = {PortraitPosPath}");
        Debug.WriteLine($"  GeneralPhotoDir = {GeneralPhotoDir}");
        Debug.WriteLine($"  HeadsDir        = {HeadsDir}");
    }

    public void LoadAll()
    {
        LastError = null;
        ResolvePaths();
        LoadGeneralSettings();
        LoadPortraitPos();
    }

    public void Reload() => LoadAll();

    // ============================== GeneralSettings.json ==============================
    public void LoadGeneralSettings()
    {
        _generalsLoaded = false;
        try
        {
            var json = File.ReadAllText(ConfigPath);
            var rows = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonArray
                ?? throw new InvalidDataException("GeneralSettings.json must contain an array.");
            var loaded = JsonSerializer.Deserialize<List<GeneralSettingData>>(json, JsonOpts)
                ?? throw new InvalidDataException("GeneralSettings.json is null.");
            if (loaded.Any(g => g == null)) throw new InvalidDataException("Null general record.");
            _original.Clear();
            for (int i = 0; i < loaded.Count; i++)
                _original.Add(loaded[i], ((JsonObject)rows[i]!, JsonSerializer.SerializeToNode(loaded[i], JsonOpts)!.AsObject(), loaded[i].Id));
            _data = loaded;
            _generalsLoaded = true;
            _manager.InvalidateData();
            Debug.WriteLine($"[GeneralSettingParser] 已加载 {_data.Count} 个将领配置");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GeneralSettingParser] 加载 GeneralSettings.json 失败: {ex.Message}");
            LastError = $"GeneralSettings load failed: {ex.Message}";
        }
    }

    public GeneralSettingData? GetById(int id) => _data.FirstOrDefault(g => g.Id == id);
    public GeneralSettingData? GetByEName(string ename) => _data.FirstOrDefault(g => string.Equals(g.EName, ename, StringComparison.OrdinalIgnoreCase));

    public int GetNextId() => _data.Count == 0 ? 1001 : checked(_data.Max(g => g.Id) + 1);

    public JsonArray GetSnapshot() => JsonNode.Parse(SerializeGenerals())!.AsArray();

    private byte[] SerializeGenerals()
    {
        VerifyAssetRoot();
        if (!_generalsLoaded) throw new InvalidOperationException("GeneralSettings was not loaded successfully; saving is disabled.");
        var output = new JsonArray();
        foreach (var general in _data)
        {
            var current = JsonSerializer.SerializeToNode(general, JsonOpts)!.AsObject();
            if (_original.TryGetValue(general, out var source))
            {
                if (general.Id != source.Id)
                    throw new InvalidOperationException("Existing general IDs cannot be changed without migrating their references.");
                var merged = source.Original.DeepClone().AsObject();
                foreach (var field in current)
                    if (!JsonNode.DeepEquals(field.Value, source.Baseline[field.Key]))
                    {
                        string key = merged.Select(f => f.Key).FirstOrDefault(k =>
                            string.Equals(k, field.Key, StringComparison.OrdinalIgnoreCase)) ?? field.Key;
                        merged[key] = field.Value?.DeepClone();
                    }
                output.Add(merged);
            }
            else output.Add(current);
        }
        return System.Text.Encoding.UTF8.GetBytes(output.ToJsonString(JsonOpts));
    }

    private void VerifyAssetRoot()
    {
        if (_explicitRoot == null && _manager.IsLoaded &&
            !string.Equals(Path.GetFullPath(Path.Combine(_manager.AssetsRoot, "json", "GeneralSettings.json")),
                Path.GetFullPath(ConfigPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("The active assets directory changed. Reload this editor before saving.");
    }

    public bool SaveGeneralSettings(string? outputPath = null)
    {
        try
        {
            var path = outputPath ?? ConfigPath;
            var bytes = SerializeGenerals();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            AtomicFile.Write(path, bytes);
            _manager.InvalidateData();
            LastError = null;
            Debug.WriteLine($"[GeneralSettingParser] 已保存 {_data.Count} 个将领配置 -> {path}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GeneralSettingParser] 保存失败: {ex.Message}");
            LastError = ex.Message;
            return false;
        }
    }

    // ============================== PortraitPos ==============================
    public void LoadPortraitPos()
    {
        _portraitsLoaded = false;
        try
        {
            if (!File.Exists(PortraitPosPath))
            {
                _portraits.Clear();
                _portraitDocument = new XDocument(new XElement("Portraits"));
                _portraitsLoaded = true;
                return;
            }
            var doc = XDocument.Load(PortraitPosPath);
            var root = doc.Element("Portraits");
            if (root == null) throw new InvalidDataException("Expected a Portraits root.");
            var portraits = new Dictionary<string, PortraitPosEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in root.Elements("general"))
            {
                var name = (string?)el.Attribute("name") ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;
                var entry = new PortraitPosEntry
                {
                    Name = name,
                    PosX = (int?)el.Attribute("posx") ?? -30,
                    PosY = (int?)el.Attribute("posy") ?? 40,
                    Scale = (double?)el.Attribute("scale") ?? 1.0
                };
                if (!double.IsFinite(entry.Scale) || entry.Scale <= 0)
                    throw new InvalidDataException($"Invalid portrait scale: {name}");
                portraits[name] = entry;
            }
            _portraitDocument = doc;
            _portraits.Clear();
            foreach (var item in portraits) _portraits[item.Key] = item.Value;
            _portraitsLoaded = true;
            Debug.WriteLine($"[GeneralSettingParser] 已加载 {_portraits.Count} 个 PortraitPos");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GeneralSettingParser] 加载 PortraitPos 失败: {ex.Message}");
            LastError = $"PortraitPos load failed: {ex.Message}";
        }
    }

    public PortraitPosEntry? GetPortrait(string ename)
        => string.IsNullOrEmpty(ename) ? null : _portraits.GetValueOrDefault(ename);

    public PortraitPosEntry EnsurePortraitDefault(string ename)
    {
        if (string.IsNullOrEmpty(ename)) throw new ArgumentNullException(nameof(ename));
        if (!_portraits.TryGetValue(ename, out var p))
        {
            p = new PortraitPosEntry { Name = ename, PosX = -30, PosY = 40, Scale = 1.0 };
            _portraits[ename] = p;
        }
        return p;
    }

    public bool RemovePortrait(string ename)
        => !string.IsNullOrEmpty(ename) && _portraits.Remove(ename);

    public bool SavePortraitPos(string? outputPath = null)
    {
        try
        {
            var path = outputPath ?? PortraitPosPath;
            var bytes = SerializePortraits();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            AtomicFile.Write(path, bytes);
            LastError = null;
            Debug.WriteLine($"[GeneralSettingParser] 已保存 PortraitPos {_portraits.Count} 项 -> {path}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GeneralSettingParser] 保存 PortraitPos 失败: {ex.Message}");
            LastError = ex.Message;
            return false;
        }
    }

    private byte[] SerializePortraits()
    {
        VerifyAssetRoot();
        if (!_portraitsLoaded) throw new InvalidOperationException("PortraitPos was not loaded successfully; saving is disabled.");
        var doc = new XDocument(_portraitDocument);
        var root = doc.Root!;
        foreach (var element in root.Elements("general").ToList())
        {
            string name = (string?)element.Attribute("name") ?? "";
            if (name.Length != 0 && !_portraits.ContainsKey(name)) element.Remove();
        }
        foreach (var entry in _portraits.Values)
        {
            if (!double.IsFinite(entry.Scale) || entry.Scale <= 0)
                throw new InvalidDataException($"Invalid portrait scale: {entry.Name}");
            var element = root.Elements("general").LastOrDefault(e =>
                string.Equals((string?)e.Attribute("name"), entry.Name, StringComparison.OrdinalIgnoreCase));
            if (element == null)
            {
                element = new XElement("general", new XAttribute("name", entry.Name),
                    new XAttribute("posx", entry.PosX), new XAttribute("posy", entry.PosY), new XAttribute("scale", entry.Scale));
                root.Add(element);
            }
            else
            {
                if (((int?)element.Attribute("posx") ?? -30) != entry.PosX) element.SetAttributeValue("posx", entry.PosX);
                if (((int?)element.Attribute("posy") ?? 40) != entry.PosY) element.SetAttributeValue("posy", entry.PosY);
                if (((double?)element.Attribute("scale") ?? 1) != entry.Scale) element.SetAttributeValue("scale", entry.Scale);
            }
        }
        using var stream = new MemoryStream();
        doc.Save(stream);
        return stream.ToArray();
    }

    // ============================== 复合操作（新增/删除 同步两边） ==============================
    public (GeneralSettingData g, PortraitPosEntry p) AddNewGeneral(string name, string ename, int? id = null)
    {
        if (string.IsNullOrWhiteSpace(ename)) throw new ArgumentException("EName 不能为空");
        if (!_generalsLoaded) throw new InvalidOperationException("Load GeneralSettings successfully before adding a general.");

        int finalId = id ?? GetNextId();
        if (finalId <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (GetById(finalId) != null) throw new InvalidOperationException($"ID={finalId} 已被占用");

        var g = new GeneralSettingData
        {
            Id = finalId,
            Name = string.IsNullOrWhiteSpace(name) ? ename : name,
            EName = ename,
            Photo = ename,
            InfantryMax = 6, ArmorMax = 6, ArtilleryMax = 6, NavyMax = 6, AirForceMax = 6, MarchMax = 6,
            SkillsMax = 5, ResetSkills = 5,
            Skills = new List<int>(),
            Medals = new List<int>()
        };
        _data.Add(g);
        var initial = JsonSerializer.SerializeToNode(g, JsonOpts)!.AsObject();
        _original.Add(g, (initial, initial.DeepClone().AsObject(), g.Id));

        // 新增时 portraitpos = posx=-30, posy=40, scale=1.0
        var p = EnsurePortraitDefault(ename);
        return (g, p);
    }

    public bool DeleteGeneral(int id)
    {
        var g = GetById(id);
        return g != null && DeleteGeneral(g);
    }

    // Portraits may also be used by other generals or promotion records.
    // Deleting a row does not authorize deleting those shared resources.
    public bool DeleteGeneral(GeneralSettingData general) => _data.Remove(general);

    public static string GetPhotoKey(GeneralSettingData general)
        => string.IsNullOrWhiteSpace(general.Photo) ? general.EName ?? "" : general.Photo;

    public bool SaveAll()
    {
        try
        {
            var generals = SerializeGenerals();
            var portraits = SerializePortraits();
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(PortraitPosPath)!);
            AtomicFile.WriteAll((ConfigPath, generals), (PortraitPosPath, portraits));
            _manager.InvalidateData();
            LastError = null;
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    // ============================== 辅助：VB 版本的专长/军衔/技能等级算法 ==============================
    public string GetSpecialty(int generalId)
    {
        var g = GetById(generalId);
        if (g == null) return "Infantry";
        int[] vals = { g.Infantry, g.Armor, g.Artillery, g.Navy, g.AirForce };
        string[] names = { "Infantry", "Armor", "Artillery", "Navy", "AirForce" };
        int max = vals.Max();
        var candidates = new List<string>();
        for (int i = 0; i < 5; i++)
            if (vals[i] == max && vals[i] > 0) candidates.Add(names[i]);
        if (candidates.Count == 0) return names[Random.Shared.Next(5)];
        if (candidates.Count == 1) return candidates[0];
        return candidates[Random.Shared.Next(candidates.Count)];
    }

    public int GetMilitaryRank(int id) => GetById(id)?.MilitaryRank ?? 0;
    public int GetHp(int id) => GetById(id)?.Hp ?? 0;
    public List<int> GetSkills(int id) => GetById(id)?.Skills?.ToList() ?? new List<int>();
    public string? GetEname(int id) => GetById(id)?.EName;
    public static int CalculateSkillLevel(int skillId) => GeneralSkillRules.GetLevel(AssetManager.Default, skillId);

    // ============================== 图片路径解析 ==============================
    public string? GetGeneralPhotoPath(string ename)
    {
        if (string.IsNullOrEmpty(ename)) return null;
        var f1 = Path.Combine(GeneralPhotoDir, $"general_{ename}.webp");
        if (File.Exists(f1)) return f1;
        var f2 = Path.Combine(GeneralPhotoDir, $"general_{ename}.png");
        return File.Exists(f2) ? f2 : null;
    }

    public string? GetHeadPath(string ename)
    {
        if (string.IsNullOrEmpty(ename)) return null;
        var f1 = Path.Combine(HeadsDir, $"general_circle_{ename}.webp");
        if (File.Exists(f1)) return f1;
        var f2 = Path.Combine(HeadsDir, $"general_circle_{ename}.png");
        return File.Exists(f2) ? f2 : null;
    }
}
