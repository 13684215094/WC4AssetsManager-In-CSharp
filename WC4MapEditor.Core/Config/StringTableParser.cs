using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
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
    private readonly Dictionary<string, string> _baseline = new(StringComparer.OrdinalIgnoreCase);
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
        if (ProjectFile.Exists(filePath))
        {
            var lines = ProjectFile.ReadAllLines(filePath);
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
            string line = rawLine.TrimStart('\uFEFF').Trim();
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
        _baseline.Clear();
        foreach (var item in _entries) _baseline[item.Key] = item.Value;
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
        if (_baseline.TryGetValue(key, out string? original) && original == value) _changedKeys.Remove(key);
        else _changedKeys.Add(key);
        _deletedKeys.Remove(key);
        _isDirty = _changedKeys.Count > 0 || _deletedKeys.Count > 0;
    }

    /// <summary>
    /// 删除条目
    /// </summary>
    public bool Remove(string key)
    {
        if (_entries.Remove(key))
        {
            _changedKeys.Remove(key);
            if (_baseline.ContainsKey(key)) _deletedKeys.Add(key);
            _isDirty = _changedKeys.Count > 0 || _deletedKeys.Count > 0;
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
        VerifyPendingKeys();
        var preparedVersion = new WC4MapEditor.Core.Parsers.FileVersionGuard();
        preparedVersion.Capture(_filePath);
        var sb = new StringBuilder();
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        byte[] original = ProjectFile.Exists(_filePath) ? ProjectFile.ReadAllBytes(_filePath) : [];
        bool bom = original.AsSpan().StartsWith(Encoding.UTF8.Preamble);
        string text = Encoding.UTF8.GetString(original.AsSpan(bom ? Encoding.UTF8.Preamble.Length : 0));
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        // Retain each line's spacing, terminator and BOM so reverting a value restores the source bytes.
        foreach (System.Text.RegularExpressions.Match match in
            System.Text.RegularExpressions.Regex.Matches(text, @"[^\r\n]*(?:\r\n|\n|\r|$)"))
        {
            if (match.Length == 0) continue;
            string raw = match.Value.TrimEnd('\r', '\n');
            string terminator = match.Value[raw.Length..];
            string line = raw.Trim();
            int equals = raw.IndexOf('=');
            if (equals < 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('['))
            {
                sb.Append(match.Value);
                continue;
            }
            string key = raw[..equals].Trim();
            if (_deletedKeys.Contains(key)) continue;
            if (_changedKeys.Contains(key))
            {
                string previous = raw[(equals + 1)..];
                int leading = previous.Length - previous.TrimStart().Length;
                int trailing = previous.Length - previous.TrimEnd().Length;
                if (leading + trailing > previous.Length) trailing = 0;
                sb.Append(raw[..(equals + 1)]).Append(previous[..leading]).Append(_entries[key])
                    .Append(trailing == 0 ? "" : previous[^trailing..]).Append(terminator);
                processed.Add(key);
            }
            else sb.Append(match.Value);
        }
        foreach (string key in _changedKeys)
            if (!processed.Contains(key))
            {
                if (sb.Length > 0 && sb[^1] is not ('\r' or '\n')) sb.Append(newline);
                sb.Append(key).Append('=').Append(_entries[key]).Append(newline);
            }
        byte[] content = Encoding.UTF8.GetBytes(sb.ToString());
        byte[] bytes = bom ? Encoding.UTF8.Preamble.ToArray().Concat(content).ToArray() : content;
        WC4MapEditor.Core.Parsers.ResourceSaveTracker.Register(bytes, () =>
        {
            VerifyPendingKeys();
            preparedVersion.Verify();
        }, AcceptSaved);
        return bytes;
    }

    private void VerifyPendingKeys()
    {
        var latest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ProjectFile.Exists(_filePath))
            foreach (string raw in ProjectFile.ReadAllLines(_filePath))
            {
                string line = raw.TrimStart('\uFEFF').Trim();
                int equals = line.IndexOf('=');
                if (equals < 0 || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('[')) continue;
                latest[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
        foreach (string key in _changedKeys.Concat(_deletedKeys))
        {
            bool wasPresent = _baseline.TryGetValue(key, out string? original);
            bool isPresent = latest.TryGetValue(key, out string? current);
            bool samePending = _deletedKeys.Contains(key) ? !isPresent : isPresent && current == _entries[key];
            if (!samePending && (wasPresent != isPresent || current != original))
                throw new IOException($"字符串键 {key} 已被其他编辑器修改，请重新加载后再保存：{_filePath}");
        }
    }

    public void AcceptSaved(byte[] bytes)
        => Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n'));

    public void Save()
    {
        byte[]? bytes = SerializePending();
        if (bytes == null) return;
        WC4MapEditor.Core.Parsers.AtomicFile.Write(_filePath, bytes);
        WC4MapEditor.Core.Assets.AssetManager.Default.InvalidateData();
    }

    /// <summary>
    /// 重新加载文件
    /// </summary>
    public void Reload()
    {
        Parse(ProjectFile.Exists(_filePath) ? ProjectFile.ReadAllLines(_filePath) : []);
    }
}
