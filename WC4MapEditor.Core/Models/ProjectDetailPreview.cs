using System.Globalization;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.Core.Parsers.Layout;

namespace WC4MapEditor.Core.Models;

public static class ProjectDetailPreview
{
    public static GameUiPreviewFrame Army(LayoutDocument layout, ArmySettingData army, string name,
        IReadOnlyList<ArmyFeatureData> features, Func<string, bool> imageExists)
    {
        var root = GameUiPreview.CloneForm(layout, "form_unitinfo");
        var issues = new List<string>();
        Text(root, "lbl_item_name", name);
        Text(root, "lbl_value_atk", $"{army.MinAttack}–{army.MaxAttack}");
        Text(root, "lbl_value_def", army.Defence.ToString(CultureInfo.InvariantCulture));
        Text(root, "lbl_value_move", army.Mobility.ToString(CultureInfo.InvariantCulture));
        Text(root, "lbl_value_range", $"{army.MinRange}–{army.MaxRange}");
        Text(root, "lbl_value_hp", army.HP.ToString(CultureInfo.InvariantCulture));
        Show(root, "motion_unit", false); Show(root, "btn_disband", false); Show(root, "btn_legenddetail", false);
        Show(root, "btn_general", false); Show(root, "img_general_grade", false);
        var model = GameUiPreview.Find(root, "gbox_model");
        string icon = $"image/image_legion_icon_hd/legion_icon_{army.Army}.png";
        if (model != null && imageExists(icon))
        {
            var picture = new LayoutNode { Tag = "Image", Parent = model, IsVirtual = true };
            picture.Set("image", icon); picture.Set("width", "100"); picture.Set("height", "100");
            picture.Set("halign", "center"); picture.Set("valign", "center"); picture.Set("drawmode", "stretch");
            model.Children.Add(picture);
        }
        else issues.Add($"当前项目缺少兵种 Army={army.Army} 的静态图标；不使用其他兵种替代。");
        for (int index = 0; index < 6; index++)
        {
            var button = GameUiPreview.Find(root, $"btn_feat_{index + 1}");
            if (button == null) continue;
            bool configured = index < army.Feature.Count; button.Set("visible", configured ? "true" : "false");
            if (!configured) continue;
            int type = army.Feature[index];
            int? level = index < army.FeatureLevel.Count ? army.FeatureLevel[index] : null;
            var row = features.Where(row => row.Type == type && (!level.HasValue || row.Level == level)).OrderBy(row => row.Level).FirstOrDefault();
            string reference = $"image/image_feats_hd/feat_{type}_{row?.Level ?? level ?? 1}.png";
            button.Set("normalimage", reference); button.Set("width", "45"); button.Set("height", "53"); button.Set("drawmode", "stretch");
            if (row != null) button.Set("previewFeatureId", row.Id.ToString(CultureInfo.InvariantCulture));
            else issues.Add($"特性 Type={type} / Level={level?.ToString() ?? "未指定"} 在项目中没有配置。");
        }
        if (army.Feature.Count > 6) issues.Add($"配置了 {army.Feature.Count} 个特性，form_unitinfo 只有 6 个按钮。");
        if (army.FeatureLevel.Count == 0) issues.Add("FeatureLevel 为空，特性图标采用项目基础等级供参考，未补写等级。");
        else if (army.FeatureLevel.Count != army.Feature.Count) issues.Add($"Feature={army.Feature.Count} / FeatureLevel={army.FeatureLevel.Count} 数量不匹配，原值保留。");
        return new(root, "兵种详情 · 基础配置", $"#{army.Id} · {name} · Army={army.Army}\n基础数值与静态兵种图标；不含将领、科技、编队和战斗状态。动画、方向与 def_armypos 位移未模拟。", issues);
    }

    public static GameUiPreviewFrame Country(LayoutDocument layout, CountrySettingData country, ConquerCountrySettingData context, string name)
    {
        if (context.CountryId != country.Id) throw new InvalidDataException("征服实例与所选国家不匹配。");
        var root = GameUiPreview.CloneForm(layout, "form_conquestcountry");
        var issues = new List<string>();
        Text(root, "lbl_countryname", name);
        Image(root, "img_flag", $"image/image_flags_hd/flag_{country.Id}.png");
        Image(root, "img_camp", $"icon_camp_{context.Camp}.png");
        // The colored camp backdrop is runtime logic; keep the configured camp emblem only.
        GameUiPreview.Find(root, "img_camp_color")?.Set("image", null);
        for (int star = 1; star <= 3; star++) Show(root, $"gbox_star{star}", context.Star == star);
        Show(root, "gbox_gengeral", false); Show(root, "img_crown", false);
        if (context.Star is < 1 or > 3) issues.Add($"星级 {context.Star} 超出此布局的 1–3 星区域，未猜测显示方式。");
        return new(root, "征服国家", $"国家 #{country.Id} · 征服 {context.ConquerId} / 记录 #{context.Id} · 阵营 {context.Camp} · 星级 {context.Star}\n将领数量、外交状态和解锁状态来自存档/游戏运行，本预览未提供。", issues);
    }

    private static void Text(LayoutNode root, string id, string value) => GameUiPreview.Find(root, id)?.Set("string", value.Length == 0 ? " " : value);
    private static void Show(LayoutNode root, string id, bool visible) => GameUiPreview.Find(root, id)?.Set("visible", visible ? "true" : "false");
    private static void Image(LayoutNode root, string id, string value)
    {
        var node = GameUiPreview.Find(root, id); node?.Set("texture", null); node?.Set("image", value);
    }
}
