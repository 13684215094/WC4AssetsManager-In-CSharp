using System.Diagnostics;
using System.Text;

namespace WC4MapEditor.Core.Config;

/// <summary>
/// 字符串表解析器 - 负责解析和管理 stringtable_*.ini 文件
/// 类似于 SettingTxtParser，专门处理键值对格式的ini文件
/// </summary>
public sealed class StringTableParser
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _filePath;
    private bool _isDirty;
    private readonly HashSet<string> _changedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _deletedKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 文件路径
    /// </summary>
    public string FilePath => _filePath;

    /// <summary>
    /// 是否已修改但未保存
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// 条目数量
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// 所有条目的只读视图
    /// </summary>
    public IReadOnlyDictionary<string, string> Entries => _entries;

    public StringTableParser(string filePath)
    {
        _filePath = filePath;
        if (File.Exists(filePath))
        {
            var lines = File.ReadAllLines(filePath);
            Parse(lines);
        }
    }

    /// <summary>
    /// 从文本行解析
    /// </summary>
    public void Parse(string[] lines)
    {
        _entries.Clear();
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[')) continue;

            int eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;

            string key = line.Substring(0, eqIdx).Trim();
            string value = line.Substring(eqIdx + 1).Trim();
            _entries[key] = value;
        }
        _isDirty = false;
        _changedKeys.Clear();
        _deletedKeys.Clear();
        Debug.WriteLine($"[StringTableParser] 已解析 {_entries.Count} 个条目");
    }

    /// <summary>
    /// 获取值
    /// </summary>
    public string GetValue(string key, string defaultValue = "")
    {
        return _entries.TryGetValue(key, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// 设置值
    /// </summary>
    public void SetValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || key.IndexOfAny(['=', '\r', '\n']) >= 0 || value.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("String table keys and values must fit on one line.");
        if (_entries.TryGetValue(key, out var existing) && existing == value)
            return;

        _entries[key] = value;
        _changedKeys.Add(key);
        _deletedKeys.Remove(key);
        _isDirty = true;
    }

    /// <summary>
    /// 删除条目
    /// </summary>
    public bool Remove(string key)
    {
        if (_entries.Remove(key))
        {
            _changedKeys.Remove(key);
            _deletedKeys.Add(key);
            _isDirty = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 检查是否包含键
    /// </summary>
    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    /// <summary>
    /// 查找以指定前缀开头的所有条目
    /// </summary>
    public Dictionary<string, string> FindByPrefix(string prefix)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _entries)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                result[kvp.Key] = kvp.Value;
        }
        return result;
    }

    /// <summary>
    /// 查找城市名称条目 (battle_cityname_xxx)
    /// </summary>
    public Dictionary<int, string> FindCityNames()
    {
        var result = new Dictionary<int, string>();
        foreach (var kvp in _entries)
        {
            if (kvp.Key.StartsWith("battle_cityname_", StringComparison.OrdinalIgnoreCase))
            {
                var numPart = kvp.Key["battle_cityname_".Length..];
                if (int.TryParse(numPart, out int cityId))
                    result[cityId] = kvp.Value;
            }
        }
        return result;
    }

    /// <summary>
    /// 根据城市名称查找ID
    /// </summary>
    public int? FindCityIdByName(string cityName)
    {
        foreach (var kvp in _entries)
        {
            if (kvp.Key.StartsWith("battle_cityname_", StringComparison.OrdinalIgnoreCase)
                && string.Equals(kvp.Value, cityName, StringComparison.Ordinal))
            {
                var numPart = kvp.Key["battle_cityname_".Length..];
                if (int.TryParse(numPart, out int cityId))
                    return cityId;
            }
        }
        return null;
    }

    /// <summary>
    /// 获取最大的城市名称ID
    /// </summary>
    public int GetMaxCityId()
    {
        int maxId = 0;
        foreach (var kvp in _entries)
        {
            if (kvp.Key.StartsWith("battle_cityname_", StringComparison.OrdinalIgnoreCase))
            {
                var numPart = kvp.Key["battle_cityname_".Length..];
                if (int.TryParse(numPart, out int cityId) && cityId > maxId)
                    maxId = cityId;
            }
        }
        return maxId;
    }

    /// <summary>
    /// 添加或更新城市名称
    /// </summary>
    /// <returns>城市ID</returns>
    public int AddOrUpdateCityName(string cityName)
    {
        // 先查找是否已存在
        var existingId = FindCityIdByName(cityName);
        if (existingId.HasValue)
            return existingId.Value;

        // 创建新ID
        int newId = GetMaxCityId() + 1;
        string key = $"battle_cityname_{newId:D3}";
        SetValue(key, cityName);

        Debug.WriteLine($"[StringTableParser] 添加新城市名称: {key}={cityName}");
        return newId;
    }

    /// <summary>
    /// 保存到文件
    /// </summary>
    public byte[]? SerializePending()
    {
        if (!_isDirty) return null;
        var sb = new StringBuilder();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(_filePath))
        {
            foreach (string raw in File.ReadAllLines(_filePath))
            {
                string line = raw.Trim();
                int equals = line.IndexOf('=');
                if (equals < 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('['))
                {
                    sb.AppendLine(raw);
                    continue;
                }
                string key = line[..equals].Trim();
                if (_deletedKeys.Contains(key)) continue;
                if (_changedKeys.Contains(key))
                {
                    sb.AppendLine($"{key}={_entries[key]}");
                    processed.Add(key);
                }
                else sb.AppendLine(raw);
            }
        }
        foreach (string key in _changedKeys)
            if (!processed.Contains(key)) sb.AppendLine($"{key}={_entries[key]}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public void AcceptSaved(byte[] bytes)
        => Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n'));

    public void Save()
    {
        byte[]? bytes = SerializePending();
        if (bytes == null) return;
        WC4MapEditor.Core.Parsers.AtomicFile.Write(_filePath, bytes);
        AcceptSaved(bytes);
        WC4MapEditor.Core.Assets.AssetManager.Default.InvalidateData();
    }

    /// <summary>
    /// 重新加载文件
    /// </summary>
    public void Reload()
    {
        if (File.Exists(_filePath))
        {
            var lines = File.ReadAllLines(_filePath);
            Parse(lines);
        }
    }
}
