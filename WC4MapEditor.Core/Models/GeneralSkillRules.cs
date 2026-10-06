using System.Globalization;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Core.Models;

public static class GeneralSkillRules
{
    public static List<int> ParseIds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        return text.Split([',', ' ', ';', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && id >= 0
                ? id : throw new FormatException($"Invalid non-negative ID: {token}")).ToList();
    }

    public static int GetLevel(AssetManager manager, int skillId)
    {
        if (skillId == 0) return 0;
        var skill = manager.GetSkill(skillId) ?? throw new InvalidDataException($"Unknown SkillSettings ID: {skillId}");
        if (skill.Level < 0 || skill.Level > byte.MaxValue)
            throw new InvalidDataException($"Skill level cannot fit in a BTL byte: {skill.Level}");
        return skill.Level;
    }

    public static List<int> RandomizeLevels(AssetManager manager, IEnumerable<int> ids, int min, int max, int count)
    {
        if (min < 0 || max < min || count < 0) throw new ArgumentOutOfRangeException(nameof(min));
        var result = ids.ToList();
        for (int i = 0; i < Math.Min(count, result.Count); i++)
        {
            if (result[i] == 0) continue;
            var source = manager.GetSkill(result[i]) ?? throw new InvalidDataException($"Unknown skill {result[i]}");
            var choices = manager.GetSkillSettings().Where(s => s.Type == source.Type && s.Level >= min && s.Level <= max).ToList();
            if (choices.Count == 0) throw new InvalidDataException($"No configured level {min}..{max} for skill type {source.Type}.");
            result[i] = choices[Random.Shared.Next(choices.Count)].Id;
        }
        return result;
    }

    public static bool CanAssign(GeneralSettings general, AssetManager manager)
    {
        if (general.Id <= 0 || general.Id > short.MaxValue || general.MilitaryRank < 0 || general.MilitaryRank > byte.MaxValue ||
            general.Skills == null || general.Skills.Count > 5) return false;
        return general.Skills.All(id => id == 0 || manager.GetSkill(id) is { Level: >= 0 and <= 255 });
    }

    public static void Apply(ref Army army, GeneralSettings general, AssetManager manager)
    {
        var levels = Levels(general, manager);
        army.General = checked((short)general.Id);
        army.Rank = checked((byte)general.MilitaryRank);
        (army.SkillLevel1, army.SkillLevel2, army.SkillLevel3, army.SkillLevel4, army.SkillLevel5) =
            (levels[0], levels[1], levels[2], levels[3], levels[4]);
    }

    public static void Apply(ref Army_3 army, GeneralSettings general, AssetManager manager)
    {
        var levels = Levels(general, manager);
        army.General = checked((short)general.Id);
        army.Rank = checked((byte)general.MilitaryRank);
        (army.SkillLevel1, army.SkillLevel2, army.SkillLevel3, army.SkillLevel4, army.SkillLevel5) =
            (levels[0], levels[1], levels[2], levels[3], levels[4]);
    }

    private static byte[] Levels(GeneralSettings general, AssetManager manager)
    {
        if (!CanAssign(general, manager))
            throw new InvalidDataException($"General {general.Id} cannot be assigned to the five-slot BTL record.");
        var levels = new byte[5];
        for (int i = 0; i < general.Skills.Count; i++) levels[i] = checked((byte)GetLevel(manager, general.Skills[i]));
        return levels;
    }
}
