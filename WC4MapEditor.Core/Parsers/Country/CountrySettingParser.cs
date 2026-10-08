using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.Country;

public class CountrySettingParser
{
    private static readonly object _lock = new();
    private static CountrySettingParser? _instance;
    public static CountrySettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new CountrySettingParser();
                }
            }
            return _instance;
        }
    }

    private readonly JsonTableFile<CountrySettingData> _countriesFile = new();
    private readonly JsonTableFile<ConquerCountrySettingData> _conquerCountriesFile = new();
    private readonly JsonTableFile<ConquerSettingData> _conquersFile = new();
    private readonly AssetManager _manager = AssetManager.Default;

    private List<CountrySettingData> _countries = new();
    private List<ConquerCountrySettingData> _conquerCountries = new();
    private List<ConquerSettingData> _conquers = new();

    public IReadOnlyList<CountrySettingData> Countries => _countries;
    public IReadOnlyList<ConquerCountrySettingData> ConquerCountries => _conquerCountries;
    public IReadOnlyList<ConquerSettingData> Conquers => _conquers;
    public string? LastError { get; private set; }
    public bool HasChanges => _countriesFile.HasChanges(_countries) || _conquerCountriesFile.HasChanges(_conquerCountries) ||
        _stringTableParser?.IsDirty == true;

    public string CountrySettingsPath { get; private set; } = "";
    public string ConquerCountrySettingsPath { get; private set; } = "";
    public string ConquerSettingsPath { get; private set; } = "";
    public string TacticalMapDir { get; private set; } = "";
    public string FlagsHdImagePath { get; private set; } = "";
    public string FlagsHdXmlPath { get; private set; } = "";
    public string StringTablePath { get; private set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private CountrySettingParser()
    {
        ResolvePaths();
        LoadAll();
    }

    private void ResolvePaths()
    {
        string root = _manager.GetActiveAssetsRoot();
        CountrySettingsPath = Path.Combine(root, "json", "CountrySettings.json");
        ConquerCountrySettingsPath = Path.Combine(root, "json", "ConquerCountrySettings.json");
        ConquerSettingsPath = Path.Combine(root, "json", "ConquerSettings.json");
        TacticalMapDir = Path.Combine(root, "image");
        FlagsHdImagePath = Path.Combine(root, "image", "image_flags_hd.webp");
        FlagsHdXmlPath = Path.Combine(root, "image", "image_flags_hd.xml");
        StringTablePath = Path.Combine(root, "stringtable_tw.ini");
    }

    private void LoadAll()
    {
        _countriesFile.Invalidate();
        _conquerCountriesFile.Invalidate();
        _conquersFile.Invalidate();
        LoadCountries();
        LoadConquerCountries();
        LoadConquers();
    }

    private void LoadCountries()
    {
        try
        {
            if (!ProjectFile.Exists(CountrySettingsPath))
            {
                Debug.WriteLine($"[CountrySettingParser] CountrySettings.json 不存在: {CountrySettingsPath}");
                return;
            }
            var data = _countriesFile.Read(CountrySettingsPath, JsonOpts);
            if (data != null)
            {
                _countries = data;
                Debug.WriteLine($"[CountrySettingParser] 加载了 {_countries.Count} 个国家设置");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CountrySettingParser] 加载 CountrySettings.json 失败: {ex.Message}");
        }
    }

    private void LoadConquerCountries()
    {
        try
        {
            if (!ProjectFile.Exists(ConquerCountrySettingsPath))
            {
                Debug.WriteLine($"[CountrySettingParser] ConquerCountrySettings.json 不存在: {ConquerCountrySettingsPath}");
                return;
            }
            var data = _conquerCountriesFile.Read(ConquerCountrySettingsPath, JsonOpts);
            if (data != null)
            {
                _conquerCountries = data;
                Debug.WriteLine($"[CountrySettingParser] 加载了 {_conquerCountries.Count} 个征服国家设置");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CountrySettingParser] 加载 ConquerCountrySettings.json 失败: {ex.Message}");
        }
    }

    private void LoadConquers()
    {
        try
        {
            if (!ProjectFile.Exists(ConquerSettingsPath))
            {
                Debug.WriteLine($"[CountrySettingParser] ConquerSettings.json 不存在: {ConquerSettingsPath}");
                return;
            }
            var data = _conquersFile.Read(ConquerSettingsPath, JsonOpts);
            if (data != null)
            {
                _conquers = data;
                Debug.WriteLine($"[CountrySettingParser] 加载了 {_conquers.Count} 个征服设置");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CountrySettingParser] 加载 ConquerSettings.json 失败: {ex.Message}");
        }
    }

    public CountrySettingData? GetCountryById(int id)
    {
        foreach (var c in _countries)
            if (c.Id == id) return c;
        return null;
    }

    public List<ConquerCountrySettingData> GetConquerCountriesByConquerId(int conquerId)
    {
        var result = new List<ConquerCountrySettingData>();
        foreach (var cc in _conquerCountries)
            if (cc.ConquerId == conquerId) result.Add(cc);
        return result;
    }

    public List<ConquerCountrySettingData> GetConquerCountriesByCountryId(int countryId)
    {
        var result = new List<ConquerCountrySettingData>();
        foreach (var cc in _conquerCountries)
            if (cc.CountryId == countryId) result.Add(cc);
        return result;
    }

    public ConquerSettingData? GetConquerById(int id)
    {
        foreach (var c in _conquers)
            if (c.Id == id) return c;
        return null;
    }

    public string GetCountryName(int countryId)
    {
        var country = GetCountryById(countryId);
        return country?.Name ?? $"未知国家({countryId})";
    }

    public string GetConquerName(int conquerId)
    {
        var conquer = GetConquerById(conquerId);
        return conquer?.Name ?? $"未知征服({conquerId})";
    }

    public bool SaveCountries()
    {
        try
        {
            AtomicFile.WriteAllWithStringTable(_stringTableParser, (CountrySettingsPath, _countriesFile.Serialize(CountrySettingsPath, _countries)));
            LastError = null;
            Debug.WriteLine($"[CountrySettingParser] 保存 CountrySettings.json 成功");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CountrySettingParser] 保存 CountrySettings.json 失败: {ex.Message}");
            LastError = ex.Message;
            return false;
        }
    }

    public bool SaveConquerCountries()
    {
        try
        {
            _conquerCountriesFile.Save(ConquerCountrySettingsPath, _conquerCountries);
            LastError = null;
            Debug.WriteLine($"[CountrySettingParser] 保存 ConquerCountrySettings.json 成功");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CountrySettingParser] 保存 ConquerCountrySettings.json 失败: {ex.Message}");
            LastError = ex.Message;
            return false;
        }
    }

    public void Reload()
    {
        ResolvePaths();
        _stringTableParser = null;
        _countries.Clear();
        _conquerCountries.Clear();
        _conquers.Clear();
        LoadAll();
    }

    public bool SaveAll()
    {
        try
        {
            AtomicFile.WriteAllWithStringTable(_stringTableParser,
                (CountrySettingsPath, _countriesFile.Serialize(CountrySettingsPath, _countries)),
                (ConquerCountrySettingsPath, _conquerCountriesFile.Serialize(ConquerCountrySettingsPath, _conquerCountries)));
            LastError = null;
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public bool AddCountry(CountrySettingData country)
    {
        if (GetCountryById(country.Id) != null)
        {
            LastError = $"国家 ID {country.Id} 已存在。";
            Debug.WriteLine($"[CountrySettingParser] 添加国家失败: ID {country.Id} 已存在");
            return false;
        }
        var candidate = _countries.Append(country).ToList();
        try
        {
            AtomicFile.WriteAllWithStringTable(_stringTableParser,
                (CountrySettingsPath, _countriesFile.Serialize(CountrySettingsPath, candidate)));
            _countries = candidate;
            LastError = null;
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public bool RemoveCountry(int id)
    {
        var country = GetCountryById(id);
        if (country == null) return false;
        var countries = _countries.Where(c => !ReferenceEquals(c, country)).ToList();
        var seats = _conquerCountries.Where(cc => cc.CountryId != id).ToList();
        try
        {
            AtomicFile.WriteAllWithStringTable(_stringTableParser,
                (CountrySettingsPath, _countriesFile.Serialize(CountrySettingsPath, countries)),
                (ConquerCountrySettingsPath, _conquerCountriesFile.Serialize(ConquerCountrySettingsPath, seats)));
            _countries = countries;
            _conquerCountries = seats;
            LastError = null;
            return true;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public int GetNextCountryId()
    {
        int maxId = 0;
        foreach (var c in _countries)
            if (c.Id > maxId) maxId = c.Id;
        return maxId + 1;
    }

    // ========== StringTable 真实名称相关 ==========

    private StringTableParser? _stringTableParser;

    private StringTableParser GetStringTableParser()
    {
        if (_stringTableParser == null)
        {
            _stringTableParser = new StringTableParser(StringTablePath);
        }
        return _stringTableParser;
    }

    /// <summary>
    /// 获取国家在 stringtable 中的真实名称 (country_{id})
    /// </summary>
    public string GetCountryRealName(int countryId)
    {
        var parser = GetStringTableParser();
        return parser.GetValue($"country_{countryId}", "");
    }

    /// <summary>
    /// 设置国家在 stringtable 中的真实名称
    /// </summary>
    public bool SetCountryRealName(int countryId, string realName)
    {
        try
        {
            var parser = GetStringTableParser();
            parser.SetValue($"country_{countryId}", realName);
            parser.Save();
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CountrySettingParser] 设置真实名称失败: {ex.Message}");
            LastError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 同步名称到真实名称 (将 CountrySettings.json 中的 Name 写入 stringtable)
    /// </summary>
    public bool SyncNameToRealName(int countryId)
    {
        var country = GetCountryById(countryId);
        if (country == null || string.IsNullOrEmpty(country.Name))
            return false;
        return SetCountryRealName(countryId, country.Name);
    }

    /// <summary>
    /// 同步真实名称到名称 (将 stringtable 中的值写入 CountrySettings.json)
    /// </summary>
    public bool SyncRealNameToName(int countryId)
    {
        var realName = GetCountryRealName(countryId);
        if (string.IsNullOrEmpty(realName))
            return false;
        var country = GetCountryById(countryId);
        if (country == null) return false;
        country.Name = realName;
        return SaveCountries();
    }

    public static void ClearInstance(bool preserveChanges = false)
    {
        lock (_lock)
        {
            if (!preserveChanges || _instance?.HasChanges != true) _instance = null;
        }
    }
}
