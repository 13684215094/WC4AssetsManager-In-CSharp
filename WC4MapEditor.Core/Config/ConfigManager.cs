using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Config;

public sealed class ConfigManager
{
    private static readonly object _lock = new();
    private static ConfigManager? _instance;

    public static event Action? StringTableChanged;

    public static ConfigManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ConfigManager();
                }
            }
            return _instance;
        }
    }

    private readonly Dictionary<string, string> _textConfig = new();
    private string _resourcePath = "";
    private bool _isInitialized;

    private SettingTxtData? _settingData;
    private long _gameLookupRevision = -1;
    private List<MapTerrainEntry>? _mapTerrainEntries;
    private HashSet<int>? _buildingTypeIds;
    private HashSet<int>? _armyTypeIds;
    private HashSet<int>? _countryIds;
    private HashSet<int>? _armyLevels;
    private HashSet<int>? _rankIds;
    private HashSet<int>? _qualityIds;

    private List<CountryGeneralsConfig>? _generalInCountryData;
    private List<LegionLevelConfig>? _legionLvSettingData;
    private List<MaxFormationConfig>? _maxFormationData;
    private List<GeneralRandomTemplate>? _generalRandomTemplates;
    private List<GeneralSpecialtyTemplate>? _generalSpecialtyTemplates;
    private TacticalMapParser? _tacticalMapParser;
    private StringTableParser? _stringTableParser;

    private ConfigManager() { }

    public void Initialize()
    {
        if (_isInitialized) return;

        _resourcePath = DetectResourcePath();
        Debug.WriteLine($"[ConfigManager] Resource path: {_resourcePath}");

        LoadSettingFile();
        LoadConfigFiles();
        LoadTacticalMap();

        _isInitialized = true;
        Debug.WriteLine("[ConfigManager] Configuration initialized");
    }

    private string DetectResourcePath()
        => FindRuntimePath("EditorResources");

    private static string FindRuntimePath(string name)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // Release assets live beside the executable; development outputs can
        // be deeper (tmp/bin/project/configuration/framework/runtime).
        var directory = new DirectoryInfo(baseDir);
        for (int depth = 0; directory != null && depth < 8; depth++, directory = directory.Parent)
        {
            string candidate = IOPath.Combine(directory.FullName, name);
            if (ProjectFile.DirectoryExists(candidate) || ProjectFile.Exists(candidate)) return candidate;
        }
        return IOPath.Combine(baseDir, name);
    }

    private void LoadSettingFile()
    {
        try
        {
            string configPath = FindRuntimePath("setting.txt");

            if (!ProjectFile.Exists(configPath))
            {
                Debug.WriteLine($"[ConfigManager] setting.txt not found: {configPath}");
                return;
            }

            var lines = ProjectFile.ReadAllLines(configPath);
            _settingData = SettingTxtParser.Parse(lines);

            // 将解析后的文本配置合并到 ConfigManager 的字典中
            foreach (var kv in _settingData.TextConfig)
            {
                if (_textConfig.ContainsKey(kv.Key))
                    _textConfig[kv.Key] = kv.Value;
                else
                    _textConfig.Add(kv.Key, kv.Value);
            }

            Debug.WriteLine($"[ConfigManager] Loaded {_textConfig.Count} config entries from setting.txt");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] Failed to load setting.txt: {ex.Message}");
        }
    }

    private void LoadConfigFiles()
    {
        try
        {
            string configPath = IOPath.Combine(_resourcePath, "Config");
            if (!ProjectFile.DirectoryExists(configPath))
            {
                Debug.WriteLine($"[ConfigManager] Config 目录不存在: {configPath}");
                return;
            }

            LoadGeneralInCountryConfig(configPath);
            LoadLegionLvSettingConfig(configPath);
            LoadMaxFormationConfig(configPath);
            LoadGeneralRandomTemplates(configPath);
            LoadGeneralSpecialtyTemplates(configPath);

            Debug.WriteLine("[ConfigManager] Config 文件加载完成");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 Config 文件失败: {ex.Message}");
        }
    }

    private void LoadGeneralInCountryConfig(string configPath)
    {
        try
        {
            string filePath = IOPath.Combine(configPath, "GeneralInCountry.json");
            if (!ProjectFile.Exists(filePath))
            {
                Debug.WriteLine("[ConfigManager] GeneralInCountry.json 不存在");
                return;
            }

            var json = ProjectFile.ReadAllText(filePath);
            _generalInCountryData = JsonSerializer.Deserialize<List<CountryGeneralsConfig>>(json);

            Debug.WriteLine($"[ConfigManager] 加载了 {_generalInCountryData?.Count ?? 0} 个国家将领配置");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 GeneralInCountry.json 失败: {ex.Message}");
        }
    }

    private void LoadLegionLvSettingConfig(string configPath)
    {
        try
        {
            string filePath = IOPath.Combine(configPath, "LegionLvSetting.json");
            if (!ProjectFile.Exists(filePath))
            {
                Debug.WriteLine("[ConfigManager] LegionLvSetting.json 不存在");
                return;
            }

            var json = ProjectFile.ReadAllText(filePath);
            _legionLvSettingData = JsonSerializer.Deserialize<List<LegionLevelConfig>>(json);

            Debug.WriteLine($"[ConfigManager] 加载了 {_legionLvSettingData?.Count ?? 0} 个军团等级配置");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 LegionLvSetting.json 失败: {ex.Message}");
        }
    }

    private void LoadMaxFormationConfig(string configPath)
    {
        try
        {
            string filePath = IOPath.Combine(configPath, "MaxFormation.json");
            if (!ProjectFile.Exists(filePath))
            {
                Debug.WriteLine("[ConfigManager] MaxFormation.json 不存在");
                return;
            }

            var json = ProjectFile.ReadAllText(filePath);
            _maxFormationData = JsonSerializer.Deserialize<List<MaxFormationConfig>>(json);

            Debug.WriteLine($"[ConfigManager] 加载了 {_maxFormationData?.Count ?? 0} 个最大编队配置");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 MaxFormation.json 失败: {ex.Message}");
        }
    }

    private void LoadGeneralRandomTemplates(string configPath)
    {
        try
        {
            string filePath = IOPath.Combine(configPath, "GeneralRandomTemplates.json");
            if (!ProjectFile.Exists(filePath))
            {
                Debug.WriteLine("[ConfigManager] GeneralRandomTemplates.json 不存在");
                return;
            }

            var json = ProjectFile.ReadAllText(filePath);
            _generalRandomTemplates = JsonSerializer.Deserialize<List<GeneralRandomTemplate>>(json);

            Debug.WriteLine($"[ConfigManager] 加载了 {_generalRandomTemplates?.Count ?? 0} 个将领随机参数模板");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 GeneralRandomTemplates.json 失败: {ex.Message}");
        }
    }

    private void LoadGeneralSpecialtyTemplates(string configPath)
    {
        try
        {
            string filePath = IOPath.Combine(configPath, "GeneralSpecialtyTemplates.json");
            if (!ProjectFile.Exists(filePath))
            {
                Debug.WriteLine("[ConfigManager] GeneralSpecialtyTemplates.json 不存在");
                return;
            }

            var json = ProjectFile.ReadAllText(filePath);
            _generalSpecialtyTemplates = JsonSerializer.Deserialize<List<GeneralSpecialtyTemplate>>(json);

            Debug.WriteLine($"[ConfigManager] 加载了 {_generalSpecialtyTemplates?.Count ?? 0} 个兵种专长模板");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 GeneralSpecialtyTemplates.json 失败: {ex.Message}");
        }
    }

    public string GetTerrainTypeName(int terrainId)
        => GetMapTerrainEntries().LastOrDefault(entry => entry.Terrain == terrainId)?.Name ?? $"未知({terrainId})";

    public Dictionary<int, string> GetTerrainTypes()
        => GetMapTerrainEntries().GroupBy(entry => entry.Terrain)
            .ToDictionary(group => group.Key, group => group.Last().Name);

    private void EnsureCurrentGameLookups()
    {
        long revision = Assets.AssetManager.Default.Revision;
        if (_gameLookupRevision == revision) return;
        _mapTerrainEntries = null;
        _buildingTypeIds = null;
        _armyTypeIds = null;
        _countryIds = null;
        _armyLevels = null;
        _rankIds = null;
        _qualityIds = null;
        _gameLookupRevision = revision;
    }

    /// <summary>
    /// 地形定义列表，来自 assets/config/def_mapterrain.xml —— 游戏对地形的<b>权威定义</b>，
    /// 与 Java 版 ResConfig.Config.TERRAINIDS 同源。
    /// <para>
    /// 职责划分：由 <c>AssetManager</c> 负责定位并读取当前项目内的文件，
    /// 由 <see cref="DefMapTerrainParser"/> 负责解析 XML 语义。
    /// 资源未扫描或文件缺失时返回空列表，让调用方<b>跳过</b>地形检查，
    /// 而不是把「取不到配置」误判成「所有 ID 都非法」。
    /// </para>
    /// </summary>
    public IReadOnlyList<MapTerrainEntry> GetMapTerrainEntries()
    {
        if (!_isInitialized) Initialize();
        EnsureCurrentGameLookups();
        if (_mapTerrainEntries != null) return _mapTerrainEntries;

        _mapTerrainEntries = new List<MapTerrainEntry>();

        try
        {
            var am = Assets.AssetManager.Default;
            var entry = am.Find(DefMapTerrainParser.AssetPath);
            if (entry == null)
            {
                Debug.WriteLine($"[ConfigManager] AssetManager 中未找到 {DefMapTerrainParser.AssetPath}");
                return _mapTerrainEntries;
            }

            _mapTerrainEntries = DefMapTerrainParser.Parse(ReadConfigText(am, entry));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 {DefMapTerrainParser.AssetPath} 失败: {ex.Message}");
        }

        return _mapTerrainEntries;
    }

    /// <summary>
    /// 合法地形组 ID 集合（<see cref="GetMapTerrainEntries"/> 的便捷形式）。
    /// 哨兵值 0 与 63 由调用方另行放行 —— 它们未必出现在定义表中。
    /// </summary>
    public HashSet<int> GetTerrainGroupIds()
        => DefMapTerrainParser.ToGroupIdSet(GetMapTerrainEntries());

    /// <summary>
    /// 合法建筑类型集合（来自 assets/json/BuildingSettings.json 的 Id）。
    /// 对应 Java 版 ResConfig.Config.DEF_WC4BUILD。
    /// 取不到时返回空集合，调用方据此<b>跳过</b>建筑类型校验。
    /// </summary>
    public HashSet<int> GetBuildingTypeIds()
        => LoadIdSet(GameSettingsParser.BuildingAssetPath, GameSettingsParser.ParseBuildingIds, ref _buildingTypeIds);

    /// <summary>
    /// 合法兵种 ID 集合（来自 assets/json/ArmySettings.json 的 Army）。
    /// 对应 Java 版 ResConfig.Config.DEF_WC4ARMY。
    /// 取不到时返回空集合，调用方据此<b>跳过</b>兵种校验。
    /// </summary>
    public HashSet<int> GetArmyTypeIds()
        => LoadIdSet(GameSettingsParser.ArmyAssetPath, GameSettingsParser.ParseArmyIds, ref _armyTypeIds);

    /// <summary>合法国家 ID 集合（CountrySettings.json 的 Id）</summary>
    public HashSet<int> GetCountryIds()
        => LoadIdSet(GameSettingsParser.CountryAssetPath, GameSettingsParser.ParseCountryIds, ref _countryIds);

    /// <summary>合法兵种等级集合（ArmyLevelSettings.json 的 Level）</summary>
    public HashSet<int> GetArmyLevels()
        => LoadIdSet(GameSettingsParser.ArmyLevelAssetPath, GameSettingsParser.ParseArmyLevels, ref _armyLevels);

    /// <summary>合法军衔集合（GeneralLevelSettings.json 的 Id）</summary>
    public HashSet<int> GetRankIds()
        => LoadIdSet(GameSettingsParser.RankAssetPath, GameSettingsParser.ParseRankIds, ref _rankIds);

    /// <summary>合法将领品质（HP 等级）集合（GeneralQualitySettings.json 的 Id）</summary>
    public HashSet<int> GetQualityIds()
        => LoadIdSet(GameSettingsParser.QualityAssetPath, GameSettingsParser.ParseQualityIds, ref _qualityIds);

    /// <summary>
    /// 经 AssetManager 取文件、交给 Parser 解析，并缓存结果。
    /// </summary>
    private HashSet<int> LoadIdSet(string assetPath, Func<string, HashSet<int>> parse, ref HashSet<int>? cache)
    {
        if (!_isInitialized) Initialize();
        EnsureCurrentGameLookups();
        if (cache != null) return cache;

        cache = [];

        try
        {
            var am = Assets.AssetManager.Default;
            var entry = am.Find(assetPath);
            if (entry == null)
            {
                Debug.WriteLine($"[ConfigManager] AssetManager 中未找到 {assetPath}");
                return cache;
            }

            cache = parse(am.ReadText(entry));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载 {assetPath} 失败: {ex.Message}");
        }

        return cache;
    }

    /// <summary>
    /// 读取 UTF-8 或 GBK/GB18030 配置，保留所选项目的中文地形名称。
    /// </summary>
    private static string ReadConfigText(Assets.AssetManager am, Assets.AssetEntry entry)
    {
        string utf8 = am.ReadText(entry, Encoding.UTF8);
        if (!utf8.Contains('\uFFFD')) return utf8;

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return am.ReadText(entry, Encoding.GetEncoding("GB18030"));
        }
        catch
        {
            return utf8;
        }
    }

    public int GetTerrainVariantCount(int terrainId)
        => Math.Max(1, GetMapTerrainEntries().LastOrDefault(entry => entry.Terrain == terrainId)?.TileCount ?? 1);

    public string ResourcePath => _resourcePath;

    public string GetText(string key, string defaultValue = "")
    {
        if (key.Equals("AIApiKey", StringComparison.OrdinalIgnoreCase))
            return Environment.GetEnvironmentVariable("WC4_EDITOR_AI_API_KEY") ?? defaultValue;
        if (!_isInitialized) Initialize();
        return _textConfig.TryGetValue(key, out var v) ? v : defaultValue;
    }

    public int GetThemeColor(int defaultColor = 0x002fa7)
    {
        string hex = GetText("ThemeColor", "");
        if (string.IsNullOrEmpty(hex))
            return defaultColor;
        try
        {
            int val = hex.StartsWith("0x") ? Convert.ToInt32(hex, 16) : int.Parse(hex);
            return val;
        }
        catch { return defaultColor; }
    }

    public enum OpeningAnimationType { Logo, Token, None }

    public OpeningAnimationType GetOpeningAnimationType(OpeningAnimationType defaultType = OpeningAnimationType.Logo)
    {
        string v = GetText("OpeningAnimation", "Logo").Trim().ToLower();
        return v switch { "token" => OpeningAnimationType.Token, "none" => OpeningAnimationType.None, _ => defaultType };
    }

    public string GetMainToken() => GetText("Main_Token", "");
    public WordCloudConfig GetWordCloudConfig() => _settingData?.WordCloudConfig ?? new WordCloudConfig();

    public string GetBackgroundPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_BackGround");
    public string GetLoadingBackgroundPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_Loading");
    public string GetLogoPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_Logo");
    public string GetConsoleBackgroundPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_Console", "Console.jpg");
    public string GetRenderBackgroundPath() => IOPath.Combine(ResourcePath, "Texture", "ZSEditor_MEBackGround", "MEBackGround.jpg");
    public string GetLoadingBackgroundImagePath()
    {
        string folder = GetLoadingBackgroundPath();
        if (!ProjectFile.DirectoryExists(folder)) return "";
        foreach (string ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" })
        {
            var files = ProjectFile.GetFiles(folder, "*" + ext);
            if (files.Length > 0) return files[0];
        }
        return "";
    }

    public Dictionary<int, int> GetCountryColors() => _settingData?.CountryColors ?? new Dictionary<int, int>();

    public int GetCountryColor(int countryId, int defaultValue = -1)
    {
        var colors = GetCountryColors();
        return colors.TryGetValue(countryId, out int color) ? color : defaultValue;
    }

    public ColorToTerrainConfig GetColorToTerrainConfig() => _settingData?.ColorToTerrainConfig ?? new ColorToTerrainConfig();

    public BuildingConfig GetBuildingConfig() => _settingData?.BuildingConfig ?? new BuildingConfig();

    public ArmyEditConfig GetArmyEditConfig() => _settingData?.ArmyEditConfig ?? new ArmyEditConfig();

    public LegionEditConfig GetLegionEditConfig() => _settingData?.LegionEditConfig ?? new LegionEditConfig();

    public EventEditConfig GetEventEditConfig() => _settingData?.EventEditConfig ?? new EventEditConfig();

    public AirForceEditConfig GetAirForceEditConfig() => _settingData?.AirForceEditConfig ?? new AirForceEditConfig();

    public CaseEditConfig GetCaseEditConfig() => _settingData?.CaseEditConfig ?? new CaseEditConfig();

    public WeatherEditConfig GetWeatherEditConfig() => _settingData?.WeatherEditConfig ?? new WeatherEditConfig();

    public List<CountryGeneralsConfig> GetGeneralInCountryData()
    {
        if (!_isInitialized) Initialize();
        return _generalInCountryData ?? [];
    }

    public List<LegionLevelConfig> GetLegionLvSettingData()
    {
        if (!_isInitialized) Initialize();
        return _legionLvSettingData ?? [];
    }

    public List<MaxFormationConfig> GetMaxFormationData()
    {
        if (!_isInitialized) Initialize();
        return _maxFormationData ?? [];
    }

    public List<GeneralRandomTemplate> GetGeneralRandomTemplates()
    {
        if (!_isInitialized) Initialize();
        return _generalRandomTemplates ?? [];
    }

    public List<GeneralSpecialtyTemplate> GetGeneralSpecialtyTemplates()
    {
        if (!_isInitialized) Initialize();
        return _generalSpecialtyTemplates ?? [];
    }

    public LegionLevelConfig? GetLegionLevelConfig(int id)
    {
        if (!_isInitialized) Initialize();
        return _legionLvSettingData?.FirstOrDefault(c => c.Id == id);
    }

    public List<LegionLevelConfig> GetAllLegionLevelConfigs()
    {
        if (!_isInitialized) Initialize();
        return _legionLvSettingData ?? [];
    }

    public int GetMaxFormation(int unitType)
    {
        if (!_isInitialized) Initialize();
        var variants = Assets.AssetManager.Default.GetArmySettings().Where(a => a.Army == unitType).ToList();
        // Without country/era context, do not exceed any matching variant's limit.
        if (variants.Count != 0) return Math.Clamp(variants.Min(a => a.MaxFormation), 0, byte.MaxValue);
        var config = _maxFormationData?.FirstOrDefault(m => m.Id == unitType);
        return config?.MaxFormation ?? 0;
    }

    public List<int> GetGeneralsByCountryId(int countryId)
    {
        if (!_isInitialized) Initialize();
        var config = _generalInCountryData?.FirstOrDefault(c => c.CountryId == countryId);
        return config?.Generals ?? [];
    }

    public GeneralSettings? GetGeneralSettingsById(int generalId)
    {
        if (!_isInitialized) Initialize();
        return GetGeneralSettingsData().FirstOrDefault(g => g.Id == generalId);
    }

    public string? GetGeneralSpecialty(int generalId)
    {
        if (!_isInitialized) Initialize();
        var settings = GetGeneralSettingsById(generalId);
        if (settings == null) return null;

        var maxStat = new List<(string Specialty, int Value)>
        {
            ("Infantry", settings.Infantry),
            ("Armor", settings.Armor),
            ("Artillery", settings.Artillery),
            ("Navy", settings.Navy),
            ("AirForce", settings.AirForce)
        }.OrderByDescending(s => s.Value).First();

        return maxStat.Value > 0 ? maxStat.Specialty : null;
    }

    public string? GetUnitSpecialtyType(int unitType)
    {
        if (!_isInitialized) Initialize();
        var army = Assets.AssetManager.Default.GetArmy(unitType);
        if (army != null)
            return army.Type switch
            {
                1 => "Infantry", 2 => "Armor", 3 => "Artillery", 4 => "Navy",
                5 or 13 => "AirForce", _ => null
            };
        var armyConfig = GetArmyEditConfig();

        if (armyConfig.Infantry.Contains(unitType)) return "Infantry";
        if (armyConfig.Armor.Contains(unitType)) return "Armor";
        if (armyConfig.Artillery.Contains(unitType)) return "Artillery";
        if (armyConfig.Navy.Contains(unitType)) return "Navy";
        if (armyConfig.AirForce.Contains(unitType)) return "AirForce";

        return null;
    }

    private readonly HashSet<int> _assignedGenerals = new();

    public void AddAssignedGeneral(int generalId) => _assignedGenerals.Add(generalId);

    public void RemoveAssignedGeneral(int generalId) => _assignedGenerals.Remove(generalId);

    public bool IsGeneralAssigned(int generalId) => _assignedGenerals.Contains(generalId);

    public void ClearAssignedGenerals() => _assignedGenerals.Clear();

    public void SyncAssignedGenerals(MapData map)
    {
        _assignedGenerals.Clear();
        foreach (int id in map.Armies.Select(a => (int)a.General)
            .Concat(map.ArmiesV3.Select(a => (int)a.General))
            .Concat(map.Reinforcements.Select(a => a.General))
            .Concat(map.ReinforcementsV3.Select(a => a.General)).Where(id => id > 0))
            _assignedGenerals.Add(id);
    }

    public List<int> GetAvailableGenerals(List<int> generalIds)
    {
        return generalIds.Where(id => !IsGeneralAssigned(id)).ToList();
    }

    public List<ConquerCountryConfig> GetConquerCountrySettingsData()
    {
        if (!_isInitialized) Initialize();
        return Assets.AssetManager.Default.GetConquerCountrySettings();
    }

    public List<GeneralSettings> GetGeneralSettingsData()
    {
        if (!_isInitialized) Initialize();
        return Assets.AssetManager.Default.GetGeneralSettings();
    }

    public Dictionary<string, string> GetStringTableData()
    {
        if (!_isInitialized) Initialize();
        return Assets.AssetManager.Default.GetStringTable();
    }

    public string GetStringTableValue(string key, string defaultValue = "")
    {
        if (!_isInitialized) Initialize();
        return Assets.AssetManager.Default.GetStringTableValue(key, defaultValue);
    }

    /// <summary>
    /// 获取字符串表解析器（支持读写）
    /// </summary>
    public StringTableParser GetStringTableParser()
    {
        if (!_isInitialized) Initialize();
        string path = GetStringTableFilePath();
        if (_stringTableParser == null || !string.Equals(_stringTableParser.FilePath, path,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            _stringTableParser = new StringTableParser(path);
        return _stringTableParser;
    }

    /// <summary>
    /// 获取stringtable文件路径（优先返回AssetManager使用的路径）
    /// </summary>
    public string GetStringTableFilePath()
    {
        return IOPath.Combine(Assets.AssetManager.Default.GetActiveAssetsRoot(), "stringtable_tw.ini");
    }

    /// <summary>
    /// 重新加载字符串表
    /// </summary>
    public void ReloadStringTable()
    {
        _stringTableParser?.Reload();
        StringTableChanged?.Invoke();
    }

    public TacticalMapParser? TacticalMapParser
    {
        get
        {
            if (!_isInitialized) Initialize();
            return _tacticalMapParser;
        }
    }

    public void Reload(bool preserveStringDraft = false)
    {
        var stringDraft = preserveStringDraft ? _stringTableParser : null;
        _isInitialized = false;
        _textConfig.Clear();
        _settingData = null;
        _stringTableParser = null;
        _tacticalMapParser = null;
        _mapTerrainEntries = null;
        _buildingTypeIds = null;
        _armyTypeIds = null;
        _countryIds = null;
        _armyLevels = null;
        _rankIds = null;
        _qualityIds = null;
        Core.Parsers.TacticalMapParser.ClearCache();
        Initialize();
        if (stringDraft != null && string.Equals(stringDraft.FilePath, GetStringTableFilePath(),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            _stringTableParser = stringDraft;
    }

    private void LoadTacticalMap()
    {
        try
        {
            _tacticalMapParser = new TacticalMapParser();
            _tacticalMapParser.EnsureLoadedFromAssets();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ConfigManager] 加载战术地图失败: {ex.Message}");
        }
    }

}
