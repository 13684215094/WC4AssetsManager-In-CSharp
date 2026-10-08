using System.Globalization;
using System.Text.RegularExpressions;
using WC4MapEditor.Core.Parsers.Layout;
using WC4MapEditor.Core.Parsers.Skill;

namespace WC4MapEditor.Core.Models;

public enum GameUiPreviewMode { General, Promotion, SkillUpgrade }

public sealed class GeneralPromotionData
{
    public int Id { get; set; }
    public int BaseID { get; set; }
    public int AdvanceID { get; set; }
    public int Evaluate { get; set; }
    public string Photo { get; set; } = "";
    public List<int> Skills { get; set; } = [];
    public int SkillsMax { get; set; }
    public int InfantryMax { get; set; }
    public int ArtilleryMax { get; set; }
    public int ArmorMax { get; set; }
    public int NavyMax { get; set; }
    public int AirForceMax { get; set; }
    public int MarchMax { get; set; }
    public int CostSword { get; set; }
    public int CostSceptre { get; set; }
    public int CostMedal { get; set; }
    public int CostMerit { get; set; }
}

public sealed class GeneralMedalPreviewData
{
    public int Id { get; set; }
    public int Type { get; set; }
    public int Pic { get; set; }
}

public sealed class GeneralTitlePreviewData
{
    public int GeneralId { get; set; }
}

public sealed class GameUiPreviewInput
{
    public required LayoutDocument Layout { get; init; }
    public GameUiPreviewMode Mode { get; init; }
    public GeneralSettingData? General { get; init; }
    public int? PromotionId { get; init; }
    public int? SkillId { get; init; }
    public IReadOnlyList<SkillSettingData> Skills { get; init; } = [];
    public IReadOnlyList<GeneralPromotionData> Promotions { get; init; } = [];
    public IReadOnlyList<GeneralMedalPreviewData> Medals { get; init; } = [];
    public ISet<int> TitledGenerals { get; init; } = new HashSet<int>();
    public Func<string, PortraitPosEntry?> PortraitLookup { get; init; } = _ => null;
    public Func<string, string?> StringLookup { get; init; } = _ => null;
    public Func<string, bool> ImageExists { get; init; } = _ => true;
}

public sealed record GameUiPreviewFrame(LayoutNode Root, string Title, string Details, IReadOnlyList<string> Issues);

// Bind only a disposable expansion. Runtime values must never enter the editable layout XML.
public static class GameUiPreview
{
    public static GameUiPreviewFrame Build(GameUiPreviewInput input)
    {
        string formId = input.Mode switch
        {
            GameUiPreviewMode.General => "form_generalinfo",
            GameUiPreviewMode.Promotion => "form_generalpromotion",
            _ => "form_upgradeskill"
        };
        var root = CloneForm(input.Layout, formId);
        var issues = new List<string>();
        var skills = input.Skills.GroupBy(skill => skill.Id).ToDictionary(group => group.Key, group => group.First());
        foreach (var group in input.Skills.GroupBy(skill => skill.Id).Where(group => group.Count() > 1))
            issues.Add($"技能 Id {group.Key} 重复，预览使用第一条记录。");
        string title = Lookup(root.Get("text") ?? formId);
        string details;
        if (input.Mode == GameUiPreviewMode.SkillUpgrade)
        {
            if (input.SkillId is not int id || !skills.TryGetValue(id, out var skill))
                throw new InvalidDataException("请先选择存在的技能。");
            ValidateSkillChain(skill);
            BindSkill(Find(root, "gb_left"), skill);
            var next = skill.UpgradeId == 0 ? null : skills.GetValueOrDefault(skill.UpgradeId);
            BindSkill(Find(root, "gb_right"), next);
            SetText(root, "lbl_medal", next?.CostMedal.ToString(CultureInfo.InvariantCulture) ?? "--");
            details = $"#{skill.Id} Lv{skill.Level} → " + (next == null ? "没有下一等级" : $"#{next.Id} Lv{next.Level}；升级费用 {next.CostMedal} 勋章") +
                $"；效果 {skill.SkillEffect}，触发率 {skill.ActivatesChance}%，IfPercent={skill.IfPercent}";
            if (next != null && (next.NeedStageId != 0 || next.NeedScenarioId != 0))
                details += $"；解锁条件 Stage={next.NeedStageId} / Scenario={next.NeedScenarioId}";
        }
        else
        {
            var general = input.General ?? throw new InvalidDataException("请先选择将领。");
            GeneralPromotionData? promotion = null;
            if (input.Mode == GameUiPreviewMode.Promotion)
            {
                promotion = input.Promotions.FirstOrDefault(item => item.Id == input.PromotionId && item.BaseID == general.Id)
                    ?? throw new InvalidDataException("当前将领没有所选进修阶段。进修记录可以在项目配置表中编辑。");
                ValidatePromotionChain(promotion);
            }
            string photo = !string.IsNullOrWhiteSpace(promotion?.Photo) ? promotion.Photo :
                !string.IsNullOrWhiteSpace(general.Photo) ? general.Photo : general.EName;
            int evaluate = promotion?.Evaluate ?? general.Evaluate;
            SetImage(root, "img_bg_general", $"image/largebg/general_evaluat_{evaluate}.png");
            SetImage(root, "img_quality", $"bg1_general_name_{evaluate}.png");
            SetText(root, "lbl_name", general.Name);
            var photoNode = Find(root, "img_general_photo");
            string photoRef = $"image/generalphoto/general_{photo}.png";
            bool hasPhoto = !string.IsNullOrWhiteSpace(photo) && input.ImageExists(photoRef);
            Show(root, "img_general_photo", hasPhoto);
            Show(root, "img_general_head", !hasPhoto);
            if (hasPhoto && photoNode != null)
            {
                var pos = input.PortraitLookup(photo) ?? new PortraitPosEntry();
                if (!double.IsFinite(pos.Scale) || pos.Scale <= 0) throw new InvalidDataException("半身像缩放必须是有限正数。");
                SetImage(photoNode, "img_general_photo", photoRef);
                photoNode.Set("margin", $"{pos.PosX},{pos.PosY}");
                photoNode.Set("scale", pos.Scale.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                string circle = $"image/heads/general_circle_{photo}.png";
                if (!input.ImageExists(circle) && photo != general.EName) circle = $"image/heads/general_circle_{general.EName}.png";
                if (!input.ImageExists(circle)) circle = $"tacticalmap/head_{general.EName}.png";
                SetImage(root, "img_head", circle);
            }
            // The game places portrait art behind its name and equipment widgets at runtime.
            var photoGroup = Find(root, "gb_photo");
            if (photoGroup != null && photoNode != null && photoGroup.Children.Remove(photoNode))
                photoGroup.Children.Insert(Math.Min(1, photoGroup.Children.Count), photoNode);
            Show(root, "gb_rank", true);
            SetImage(root, "icon_rank", $"general_rank_{general.MilitaryRank}.png");
            SetText(root, "lbl_rank", Lookup($"general_rank_{general.MilitaryRank}"));
            SetText(root, "lbl_hp", $"HP Lv{general.Hp}");
            foreach (string id in new[] { "lbl_rank", "lbl_hp" })
            {
                var label = Find(root, id);
                label?.Set("margin", "50,0"); label?.Set("width", "200"); label?.Set("height", "38");
            }
            Find(root, "lbl_name")?.Set("halign", "center");
            Show(root, "btn_title", input.TitledGenerals.Contains(general.Id));
            Show(root, "rpt_ribbons", false);

            int[] values = [general.Infantry, general.Artillery, general.Armor, general.Navy, general.AirForce, general.March];
            int[] maxima = promotion == null
                ? [general.InfantryMax, general.ArtilleryMax, general.ArmorMax, general.NavyMax, general.AirForceMax, general.MarchMax]
                : [promotion.InfantryMax, promotion.ArtilleryMax, promotion.ArmorMax, promotion.NavyMax, promotion.AirForceMax, promotion.MarchMax];
            var abilityCells = Find(root, "rpt_ability")?.Items ?? [];
            string[] abilityNames = ["步兵", "火炮", "装甲", "海军", "空军", "行军"];
            int[] abilityIcons = [1, 3, 2, 4, 5, 0];
            for (int i = 0; i < Math.Min(6, abilityCells.Count); i++)
            {
                var cell = abilityCells[i];
                SetImage(cell, "img_ability_logo", $"general_ability_{abilityIcons[i]}.png");
                int value = promotion == null ? values[i] : maxima[i];
                int displayMax = promotion == null ? Math.Max(value, maxima[i]) : maxima[i];
                var stars = Find(cell, "gb_ability_star");
                if (stars != null)
                {
                    stars.Children.Clear();
                    int style = value <= 2 ? 1 : value <= 4 ? 2 : value == 5 ? 3 : 4;
                    for (int j = 0; j < Math.Clamp(Math.Max(value, displayMax), 0, 6); j++)
                        Add(stars, "Image", ("image", $"general_star_{(j < value ? style : 0)}.png"),
                            ("width", "32"), ("height", "32"), ("drawmode", "stretch"), ("margin", $"{j * 34},10"));
                    Add(stars, "Label", ("string", $"{value} / {displayMax}"), ("font", "font_text_0"),
                        ("width", "210"), ("height", "24"), ("margin", "0,48"));
                }
                if (value > 6 || maxima[i] > 6) issues.Add($"{abilityNames[i]}能力 {value}/{maxima[i]} 超出原版六星范围，预览最多画六颗星；运行时支持需实测。");
                if (value < 0 || maxima[i] < 0)
                    issues.Add($"{abilityNames[i]}能力 {value}/{maxima[i]} 的当前值和上限不一致。");
            }
            if (abilityCells.Count != 6) issues.Add($"布局能力格数量为 {abilityCells.Count}，数据需要六种能力。");
            var ids = promotion?.Skills ?? general.Skills ?? [];
            int max = promotion?.SkillsMax ?? general.SkillsMax;
            var skillCells = Find(root, "rpt_skill")?.Items ?? [];
            if (ids.Count > skillCells.Count || max > skillCells.Count)
                issues.Add($"技能配置 {ids.Count} 项、SkillsMax={max}，布局只有 {skillCells.Count} 个槽位；超出部分不会出现在该页面。");
            if (ids.Count > max && !(promotion == null && general.Type == 3 && max == 0))
                issues.Add($"Skills 有 {ids.Count} 项，超过 SkillsMax={max}。");
            for (int i = 0; i < skillCells.Count; i++)
            {
                var cell = skillCells[i];
                bool occupied = i < ids.Count && ids[i] != 0;
                Show(cell, "img_back", occupied); Show(cell, "img_bg_level", occupied);
                Show(cell, "btn_empty_skill", !occupied && i < max);
                if (!occupied) continue;
                cell.Set("previewSkillId", ids[i].ToString(CultureInfo.InvariantCulture));
                if (!skills.TryGetValue(ids[i], out var skill)) { issues.Add($"找不到技能 Id={ids[i]}。"); continue; }
                SetImage(cell, "img_skill_icon", skill.Type is >= 0 and <= 5 ? $"general_ability_{skill.Type}.png" :
                    $"image/general_skill/general_skill_{skill.Type}.png");
                SetText(cell, "lbl_level", $"LV {skill.Level}");
                Lookup($"skill_name_{skill.Type}");
                FormatSkillDescription(Lookup($"skill_info_{skill.Type}"), skill, issues);
                ValidateSkillChain(skill);
            }
            var medalCells = Find(root, "rpt_medals")?.Items ?? [];
            var medalIds = general.Medals ?? [];
            Show(root, "rpt_medals", medalIds.Count != 0);
            if (medalIds.Count > medalCells.Count) issues.Add($"勋章 {medalIds.Count} 项超过布局 {medalCells.Count} 个槽位。");
            for (int i = 0; i < medalCells.Count; i++)
            {
                var cell = medalCells[i];
                cell.Set("visible", (i < medalIds.Count && medalIds[i] != 0).ToString().ToLowerInvariant());
                if (i >= medalIds.Count || medalIds[i] == 0) continue;
                var medal = input.Medals.FirstOrDefault(item => item.Id == medalIds[i]);
                if (medal == null) { issues.Add($"找不到勋章 Id={medalIds[i]}。"); continue; }
                if (medal.Pic != 0)
                {
                    issues.Add($"装备 #{medal.Id} 使用 Pic={medal.Pic} 的特殊勋带图片，当前预览未绑定其运行时图片规则。");
                    cell.Set("visible", "false"); continue;
                }
                SetImage(cell, "img_equip", $"general_medal_{medal.Type}.png");
                var icon = Find(cell, "img_equip");
                icon?.Set("scale", "0.45");
            }
            details = $"将领 #{general.Id} · 技能 {ids.Count}/{max} · 布局槽位 {skillCells.Count} · {(hasPhoto ? "半身像" : "已有头像")}";
            if (promotion != null)
                details += $"\n进修 #{promotion.Id} → {promotion.AdvanceID}；剑 {promotion.CostSword} / 权杖 {promotion.CostSceptre} / 勋章 {promotion.CostMedal} / 功绩 {promotion.CostMerit}。能力显示该阶段上限，未合并存档技能。";
        }
        return new(root, title, details, issues.Distinct().ToArray());

        string Lookup(string key)
        {
            string? value = input.StringLookup(key);
            if (value != null) return value;
            issues.Add($"本地化缺少 {key}。"); return key;
        }

        void BindSkill(LayoutNode? group, SkillSettingData? skill)
        {
            if (group == null) return;
            bool ability = skill?.Type is >= 0 and <= 5;
            Show(group, "img_skill_icon", skill != null && !ability);
            Show(group, "img_ability_icon", skill != null && ability);
            SetText(group, "lbl_skill_level", skill == null ? "--" : $"LV {skill.Level}");
            SetText(group, "lbl_skill_name", skill == null ? "没有下一等级" : Lookup($"skill_name_{skill.Type}"));
            SetText(group, "lbl_skill_info", skill == null ? "UpgradeId=0 表示链尾；可在技能编辑页追加等级。" :
                FormatSkillDescription(Lookup($"skill_info_{skill.Type}"), skill, issues));
            if (skill != null)
            {
                if (ability) SetImage(group, "img_ability_icon", $"general_ability_{skill.Type}.png");
                else SetImage(group, "img_skill_icon", $"image/general_skill/general_skill_{skill.Type}.png");
                group.Set("previewSkillId", skill.Id.ToString(CultureInfo.InvariantCulture));
            }
            var label = Find(group, "lbl_skill_info");
            label?.Set("width", "330"); label?.Set("height", "135");
            label?.Set("valign", "top"); label?.Set("vtextalign", "top");
        }

        void ValidateSkillChain(SkillSettingData first)
        {
            var visited = new HashSet<int>();
            var current = first;
            while (true)
            {
                if (!visited.Add(current.Id)) { issues.Add($"技能 #{first.Id} 的 UpgradeId 链有环，节点 #{current.Id}。"); break; }
                if (current.UpgradeId == 0) break;
                if (!skills.TryGetValue(current.UpgradeId, out var next)) { issues.Add($"技能 #{current.Id} 的 UpgradeId={current.UpgradeId} 不存在。"); break; }
                if (next.Type != current.Type || (long)next.Level != (long)current.Level + 1)
                    issues.Add($"技能 #{current.Id} → #{next.Id} 的 Type/Level 不连续。");
                current = next;
            }
        }

        void ValidatePromotionChain(GeneralPromotionData first)
        {
            var visited = new HashSet<int>();
            var current = first;
            while (true)
            {
                if (!visited.Add(current.Id)) { issues.Add($"进修 #{first.Id} 的 AdvanceID 链有环。"); break; }
                if (current.AdvanceID == 0) break;
                var next = input.Promotions.FirstOrDefault(item => item.Id == current.AdvanceID);
                if (next == null || next.BaseID != first.BaseID) { issues.Add($"进修 #{current.Id} 的 AdvanceID={current.AdvanceID} 缺失或属于其他将领。"); break; }
                current = next;
            }
        }
    }

    public static string FormatSkillDescription(string template, SkillSettingData skill, ICollection<string>? issues = null)
    {
        // Probability based skills use ActivatesChance even when SkillEffect stores a separate bonus.
        int value = UsesActivationChance(skill) ? skill.ActivatesChance : skill.SkillEffect;
        int arguments = 0;
        string text = Regex.Replace(template, @"%%|%d", match => match.Value == "%%" ? "%" :
            ++arguments == 1 ? value.ToString(CultureInfo.InvariantCulture) : match.Value);
        if (arguments > 1 || Regex.IsMatch(text, @"%[dfs]"))
            issues?.Add($"技能 #{skill.Id} 描述有未确定的格式参数，已保留占位符；原生多参数逻辑需实测。");
        return text.Replace(@"\n", "\n");
    }

    private static bool UsesActivationChance(SkillSettingData skill)
    {
        if (skill.SkillEffect == 0 && skill.IfPercent == 1) return true;
        // Type, rather than translated wording, defines the parameter for these families.
        return skill.Type is >= 18 and <= 22 or 33 or 34 or 42 or 54 or >= 61 and <= 65 or 119 or 123 or
            138 or 140 or 163 or 169 or 174 or 179 or 190;
    }

    public static IEnumerable<LayoutNode> Walk(LayoutNode node)
    {
        yield return node;
        foreach (var child in LayoutDocument.ExpandedKids(node) ?? LayoutDocument.RealKids(node))
            foreach (var item in Walk(child)) yield return item;
    }

    public static LayoutNode? Find(LayoutNode node, string id) => Walk(node).FirstOrDefault(item => item.Get("id") == id);
    public static LayoutNode CloneForm(LayoutDocument layout, string formId)
    {
        var source = layout.Forms.FirstOrDefault(form => form.Get("id") == formId)
            ?? throw new InvalidDataException($"项目布局缺少 {formId}。");
        ValidateExpansion(source, layout, new HashSet<string>(), 0);
        var root = source.DeepClone(); layout.BuildExpansion(root);
        foreach (var node in Walk(root))
            foreach (var child in LayoutDocument.ExpandedKids(node) ?? LayoutDocument.RealKids(node)) child.Parent = node;
        return root;
    }
    private static void Show(LayoutNode node, string id, bool visible) => Find(node, id)?.Set("visible", visible ? "true" : "false");
    private static void SetText(LayoutNode node, string id, string? text) => Find(node, id)?.Set("string", string.IsNullOrEmpty(text) ? " " : text);
    private static void SetImage(LayoutNode node, string id, string reference)
    {
        var target = Find(node, id);
        if (target == null) return;
        target.Set("texture", null); target.Set("image", reference);
    }

    private static void Add(LayoutNode parent, string tag, params (string Key, string Value)[] attributes)
    {
        var node = new LayoutNode { Tag = tag, Parent = parent, IsVirtual = true };
        foreach (var (key, value) in attributes) node.Set(key, value);
        parent.Children.Add(node);
    }

    private static int ValidateExpansion(LayoutNode node, LayoutDocument layout, HashSet<string> templates, int depth)
    {
        if (depth > 64) throw new InvalidDataException("布局展开层级过深或模板循环引用。");
        int repeats = 1;
        if (node.Tag == "Repeater")
        {
            int count = node.Children.Count;
            if (node.Get("itemcount") is { } raw && !int.TryParse(raw, out count) || count < 0 || count > 128)
                throw new InvalidDataException("预览 Repeater 的 itemcount 必须在 0–128 范围内。");
            repeats = count;
        }
        long descendants = 0;
        if (node.Tag == "PlaceHolder" && node.Get("templateid") is { } id)
        {
            if (!layout.Templates.TryGetValue(id, out var template)) throw new InvalidDataException($"找不到布局模板 {id}。");
            if (!templates.Add(id)) throw new InvalidDataException($"布局模板 {id} 循环引用。");
            descendants += ValidateExpansion(template, layout, templates, depth + 1); templates.Remove(id);
        }
        foreach (var child in node.Children) descendants += ValidateExpansion(child, layout, templates, depth + 1);
        long expanded = 1 + repeats * (descendants + (node.Tag == "Repeater" ? 1 : 0));
        if (expanded > 12_000) throw new InvalidDataException("布局展开超过 12,000 个节点，请减少嵌套 Repeater。");
        return (int)expanded;
    }
}
