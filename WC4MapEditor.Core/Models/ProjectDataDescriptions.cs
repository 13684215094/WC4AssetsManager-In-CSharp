using System.Globalization;
using System.Text.RegularExpressions;
using WC4MapEditor.Core.Parsers.Skill;

namespace WC4MapEditor.Core.Models;

public sealed class ArmyFeatureData
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public int Level { get; set; }
    public int ActivatesChance { get; set; }
    public int FeatureEffect { get; set; }
    public int SecondEffect { get; set; }
    public int CD { get; set; }
    public int Range { get; set; }
    public int Round { get; set; }
}

public sealed record ProjectDataDescription(int? RecordId, string Title, string Description, string Parameters, string? Warning);

public static class ProjectDataDescriptions
{
    public static ProjectDataDescription Skill(int id, IReadOnlyList<SkillSettingData> skills, Func<string, string?> lookup)
    {
        if (id == 0) return new(null, "空技能槽（Id=0）", "此位置未配置技能。", "", null);
        var matches = skills.Where(item => item.Id == id).ToArray();
        if (matches.Length == 0) return new(null, $"未知技能 #{id}", "当前项目没有此技能记录。", "", "请检查 Skills 中的 ID。");
        var skill = matches[0];
        var warnings = new List<string>();
        if (matches.Length > 1) warnings.Add("技能 ID 重复，使用第一条记录。");
        string? name = lookup($"skill_name_{skill.Type}"), template = lookup($"skill_info_{skill.Type}");
        if (name == null || template == null) warnings.Add("当前语言缺少名称或介绍，保留内部名称/缺失提示。");
        string description = template == null ? "此语言没有技能介绍。" : GameUiPreview.FormatSkillDescription(template, skill, warnings);
        return new(id, $"{name ?? skill.Name} · Lv{skill.Level} · #{id}", PlainText(description),
            $"Type={skill.Type} · 效果={skill.SkillEffect} · 触发率={skill.ActivatesChance}% · 下一等级={skill.UpgradeId}",
            warnings.Count == 0 ? null : string.Join("\n", warnings));
    }

    public static ProjectDataDescription Feature(int type, int? level, IReadOnlyList<ArmyFeatureData> features, Func<string, string?> lookup)
    {
        var family = features.Where(item => item.Type == type).OrderBy(item => item.Level).ToArray();
        var matches = level.HasValue ? family.Where(item => item.Level == level).ToArray() : family.Take(1).ToArray();
        string? name = lookup($"feat_name_{type}");
        if (matches.Length == 0)
            return new(null, $"{name ?? "未知特性"} · Type {type}" + (level.HasValue ? $" · Lv{level}" : ""),
                PlainText(lookup($"feat_desc_{type}") ?? "当前语言没有特性介绍。"), "",
                "当前项目没有对应 Type/Level 的特性记录；参数无法绑定。");
        var feature = matches[0];
        string? template = lookup($"feat_desc_{type}_Lv{feature.Level}") ?? lookup($"feat_desc_{type}");
        string description = template ?? "当前语言没有特性介绍。";
        int[] values = [feature.ActivatesChance, feature.FeatureEffect, feature.SecondEffect];
        description = Regex.Replace(description, @"\{([012])\}", match => values[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)].ToString(CultureInfo.InvariantCulture));
        var warnings = new List<string>();
        if (!level.HasValue) warnings.Add($"FeatureLevel 未指定：展示项目基础 Lv{feature.Level} 供参考，实际等级可能受科技或游戏运行状态影响。");
        if (matches.Length > 1) warnings.Add("此 Type/Level 有多条记录，使用第一条。");
        if (name == null || template == null) warnings.Add("当前语言缺少名称或介绍。");
        if (Regex.IsMatch(description, @"\{[^}]+\}|%[dfs]")) warnings.Add("介绍含未绑定参数，已保留占位符。");
        return new(feature.Id, $"{name ?? feature.Name} · {(level.HasValue ? "" : "参考 ")}Lv{feature.Level} · Type {type} · #{feature.Id}",
            PlainText(description), $"效果={feature.FeatureEffect} · 第二效果={feature.SecondEffect} · 触发率={feature.ActivatesChance}% · CD={feature.CD} · 范围={feature.Range} · 回合={feature.Round}",
            warnings.Count == 0 ? null : string.Join("\n", warnings));
    }

    public static string PlainText(string value) => Regex.Replace(value, @"\[#([0-9A-Fa-f]{6})#([^\]]*)\]", "$2").Replace(@"\n", "\n");
}
