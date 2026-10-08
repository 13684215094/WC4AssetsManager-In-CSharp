using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Parsers.Country;

/// <summary>
/// 事件 Buff 解析器（EventBuffSettings.json + stringtable_tw.ini 的 event_buff_desc_{Id}）。
/// 与 CountryTechSettingParser 采用相同的单例与路径解析约定。
/// </summary>
public class EventBuffSettingParser
{
    private static readonly object _lock = new();
    private static EventBuffSettingParser? _instance;
    public static EventBuffSettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new EventBuffSettingParser();
                }
            }
            return _instance;
        }
    }

    private readonly JsonTableFile<EventBuffData> _buffsFile = new();
    private readonly AssetManager _manager = AssetManager.Default;
    private List<EventBuffData> _buffs = new();
    private StringTableParser? _stringTable;

    public IReadOnlyList<EventBuffData> All => _buffs;
    public IReadOnlyList<EventBuffData> Items => _buffs;
    public string ConfigPath { get; private set; } = "";
    public string StringTablePath { get; private set; } = "";

    public bool HasChanges => _buffsFile.HasChanges(_buffs) || _stringTable?.IsDirty == true;
    public string? LastError { get; private set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private EventBuffSettingParser()
    {
        ResolvePaths();
        LoadAll();
    }

    private void ResolvePaths()
    {
        string root = _manager.GetActiveAssetsRoot();
        ConfigPath = Path.Combine(root, "json", "EventBuffSettings.json");
        StringTablePath = Path.Combine(root, "stringtable_tw.ini");
    }

    public void LoadAll()
    {
        _buffsFile.Invalidate();
        LoadBuffs();
        _stringTable = new StringTableParser(StringTablePath);
    }

    private void LoadBuffs()
    {
        try
        {
            if (!ProjectFile.Exists(ConfigPath))
            {
                Debug.WriteLine($"[EventBuffSettingParser] 文件不存在: {ConfigPath}");
                return;
            }
            var data = _buffsFile.Read(ConfigPath, JsonOpts);
            if (data != null) _buffs = data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EventBuffSettingParser] 加载失败: {ex.Message}");
        }
    }

    public EventBuffData? GetById(int id)
    {
        foreach (var b in _buffs) if (b.Id == id) return b;
        return null;
    }

    /// <summary>获取 Buff 描述：优先 event_buff_desc_{Id}，其次 event_buff_desc_{Type}。</summary>
    public string GetBuffDesc(int id, int type = -1)
    {
        var v = _stringTable?.GetValue($"event_buff_desc_{id}", "");
        if (!string.IsNullOrEmpty(v)) return v!;
        if (type >= 0)
        {
            var v2 = _stringTable?.GetValue($"event_buff_desc_{type}", "");
            if (!string.IsNullOrEmpty(v2)) return v2!;
        }
        return "";
    }

    /// <summary>设置 Buff 描述（写入 event_buff_desc_{Id}）。</summary>
    public void SetBuffDesc(int id, string desc) => _stringTable?.SetValue($"event_buff_desc_{id}", desc);

    public int GetNextId()
    {
        int max = 0;
        foreach (var b in _buffs) if (b.Id > max) max = b.Id;
        return max + 1;
    }

    public bool AddBuff(EventBuffData buff)
    {
        if (GetById(buff.Id) != null) return false;
        _buffs.Add(buff);
        return true;
    }

    public bool RemoveBuff(int id)
    {
        var b = GetById(id);
        if (b == null) return false;
        _buffs.Remove(b);
        _stringTable?.Remove($"event_buff_desc_{id}");
        return true;
    }

    public bool SaveAll()
    {
        try
        {
            AtomicFile.WriteAllWithStringTable(_stringTable,
                (ConfigPath, _buffsFile.Serialize(ConfigPath, _buffs)));
            Debug.WriteLine($"[EventBuffSettingParser] 保存成功: {ConfigPath}");
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.WriteLine($"[EventBuffSettingParser] 保存失败: {ex.Message}");
            return false;
        }
    }

    public void Reload() => LoadAll();

    public static void ClearInstance(bool preserveChanges = false)
    {
        lock (_lock)
        {
            if (!preserveChanges || _instance?.HasChanges != true) _instance = null;
        }
    }
}

/// <summary>
/// 事件 Buff（EventBuffSettings.json）。
/// 描述不保存在 JSON 中，而是写入 stringtable_tw.ini 的 event_buff_desc_{Id}（Type 回退）。
/// </summary>
public class EventBuffData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("Value")] public int Value { get; set; }
    [JsonPropertyName("Round")] public int Round { get; set; }
    [JsonPropertyName("ArmyBuffs")] public List<int> ArmyBuffs { get; set; } = new();
}
