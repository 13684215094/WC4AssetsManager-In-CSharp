using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Parsers.Skill;

/// <summary>
/// 技能解析器（SkillSettings.json + stringtable_tw.ini 中的
/// skill_name_{Type} / skill_info_{Type}）。
/// 采用与 CountryTechSettingParser 相同的单例与路径解析约定。
/// 技能名/描述不保存在 JSON 中，而是写入 stringtable，索引取 Type。
/// </summary>
public class SkillSettingParser
{
    private static readonly object _lock = new();
    private static SkillSettingParser? _instance;
    public static SkillSettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new SkillSettingParser();
                }
            }
            return _instance;
        }
    }

    private readonly JsonTableFile<SkillSettingData> _skillsFile = new();
    private readonly AssetManager _manager = AssetManager.Default;
    private List<SkillSettingData> _skills = new();
    private StringTableParser? _stringTable;

    public IReadOnlyList<SkillSettingData> All => _skills;
    public List<SkillSettingData> Items => _skills;
    public string ConfigPath { get; private set; } = "";
    public string StringTablePath { get; private set; } = "";

    public bool HasChanges => _skillsFile.HasChanges(_skills) || _stringTable?.IsDirty == true;
    public string? LastError { get; private set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private SkillSettingParser()
    {
        ResolvePaths();
        LoadAll();
    }

    private void ResolvePaths()
    {
        string root = _manager.GetActiveAssetsRoot();
        ConfigPath = Path.Combine(root, "json", "SkillSettings.json");
        StringTablePath = Path.Combine(root, "stringtable_tw.ini");
    }

    public void LoadAll()
    {
        _skillsFile.Invalidate();
        LoadSkills();
        _stringTable = new StringTableParser(StringTablePath);
    }

    private void LoadSkills()
    {
        try
        {
            if (!ProjectFile.Exists(ConfigPath))
            {
                Debug.WriteLine($"[SkillSettingParser] 文件不存在: {ConfigPath}");
                return;
            }
            var data = _skillsFile.Read(ConfigPath, JsonOpts);
            if (data != null) _skills = data;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkillSettingParser] 加载失败: {ex.Message}");
        }
    }

    public SkillSettingData? GetById(int id)
    {
        foreach (var s in _skills) if (s.Id == id) return s;
        return null;
    }

    /// <summary>获取技能名（stringtable 的 skill_name_{Type}）。</summary>
    public string GetSkillName(int type) => _stringTable?.GetValue($"skill_name_{type}", "") ?? "";

    /// <summary>设置技能名（写入 stringtable 的 skill_name_{Type}）。</summary>
    public void SetSkillName(int type, string name) => _stringTable?.SetValue($"skill_name_{type}", name);

    /// <summary>获取技能描述（stringtable 的 skill_info_{Type}）。</summary>
    public string GetSkillDesc(int type) => _stringTable?.GetValue($"skill_info_{type}", "") ?? "";

    /// <summary>设置技能描述（写入 stringtable 的 skill_info_{Type}）。</summary>
    public void SetSkillDesc(int type, string desc) => _stringTable?.SetValue($"skill_info_{type}", desc);

    public int GetNextId()
    {
        int max = 1000;
        foreach (var s in _skills) if (s.Id > max) max = s.Id;
        return checked(max + 1);
    }

    public bool AddSkill(SkillSettingData skill)
    {
        if (GetById(skill.Id) != null) return false;
        _skills.Add(skill);
        return true;
    }

    public SkillSettingData AppendNextLevel(SkillSettingData selected)
    {
        if (!_skills.Contains(selected)) throw new InvalidOperationException("请先选择当前表中的技能。");
        var family = _skills.Where(skill => skill.Type == selected.Type).ToArray();
        if (family.GroupBy(skill => skill.Level).Any(group => group.Count() > 1))
            throw new InvalidDataException("同一 Type 存在重复等级，请先整理后再追加。");
        if (selected.Level != family.Max(skill => skill.Level) || selected.UpgradeId != 0)
            throw new InvalidDataException("请选择该技能最高等级且 UpgradeId 为 0 的链尾。");
        if (_skills.Count(skill => skill.Id == selected.Id) != 1)
            throw new InvalidDataException("所选技能 Id 重复，不能确定升级来源。");
        int nextLevel = checked(selected.Level + 1);
        int nextId = checked(selected.Id + 1);
        if (GetById(nextId) != null) nextId = GetNextId();
        var clone = _skillsFile.CloneRecord(selected);
        clone.Id = nextId;
        clone.Level = nextLevel;
        clone.UpgradeId = 0;
        _skills.Add(clone);
        selected.UpgradeId = clone.Id;
        return clone;
    }

    public bool RemoveSkill(int id)
    {
        var s = GetById(id);
        if (s == null) return false;
        _skills.Remove(s);
        return true;
    }

    public bool SaveAll()
    {
        try
        {
            AtomicFile.WriteAllWithStringTable(_stringTable,
                (ConfigPath, _skillsFile.Serialize(ConfigPath, _skills)));
            Debug.WriteLine($"[SkillSettingParser] 保存成功: {ConfigPath}");
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Debug.WriteLine($"[SkillSettingParser] 保存失败: {ex.Message}");
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
/// 技能节点（SkillSettings.json）。
/// 名称/描述不保存在 JSON 中，而是写入 stringtable_tw.ini 的
/// skill_name_{Type} 与 skill_info_{Type} 键。
/// </summary>
public class SkillSettingData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("Level")] public int Level { get; set; }
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("Series")] public int Series { get; set; }
    [JsonPropertyName("ActivatesChance")] public int ActivatesChance { get; set; }
    [JsonPropertyName("IfPercent")] public int IfPercent { get; set; }
    [JsonPropertyName("SkillEffect")] public int SkillEffect { get; set; }
    [JsonPropertyName("UpgradeId")] public int UpgradeId { get; set; }
    [JsonPropertyName("CostMedal")] public int CostMedal { get; set; }
    [JsonPropertyName("OpenDefault")] public int OpenDefault { get; set; }
    [JsonPropertyName("NeedStageId")] public int NeedStageId { get; set; }
    [JsonPropertyName("NeedScenarioId")] public int NeedScenarioId { get; set; }
    [JsonPropertyName("Score")] public int Score { get; set; }
    [JsonPropertyName("ArmyBuff")] public int ArmyBuff { get; set; }
    [JsonPropertyName("AurasRange")] public int AurasRange { get; set; }
}
