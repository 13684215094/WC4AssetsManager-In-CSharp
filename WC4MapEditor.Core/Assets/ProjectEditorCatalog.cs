using System.Text.Json;

namespace WC4MapEditor.Core.Assets;

public enum ProjectEditorKind
{
    Campaign, Conquest, OtherBattle, World, General, Country, Skill, Army, ArmyBuff,
    BuildingFacility, CountryTech, ArmyGroup, ArmyGroupEvent, EventBuff, ConquerEvent, Layout,
    GeneralProgress, HeadquartersTech, ArmyProgress, JsonTables, Resources
}

public sealed record ProjectEditorOption(ProjectEditorKind Kind, string Title, string Summary,
    bool IsAvailable, string Details);

public sealed class ProjectEditorCatalog
{
    private readonly List<ProjectEditorOption> _options = [];
    public IReadOnlyList<ProjectEditorOption> Options => _options;
    public ProjectEditorOption this[ProjectEditorKind kind] => _options.Single(option => option.Kind == kind);

    public ProjectEditorCatalog(AssetManager manager)
    {
        if (!manager.IsLoaded) throw new InvalidOperationException("请先加载游戏项目。");
        var entries = manager.ListAll();
        AddFiles(ProjectEditorKind.Campaign, "战役关卡", entries.Count(e => e.Kind == AssetKind.Stage), "stage/stage*.btl");
        AddFiles(ProjectEditorKind.Conquest, "征服关卡", entries.Count(e => e.Kind == AssetKind.Conquest), "stage/conquest*.btl");
        AddFiles(ProjectEditorKind.OtherBattle, "活动、前线与其他关卡", entries.Count(e => e.Extension == "btl" &&
            e.Kind is not (AssetKind.Stage or AssetKind.Conquest)), "stage 中的活动、前线、传记、传奇、战区与入侵 BTL");
        AddFiles(ProjectEditorKind.World, "世界地形地图", entries.Count(e => e.Kind == AssetKind.World), "world.bin；征服关卡引用的外部地形");
        AddTables(ProjectEditorKind.General, "将领编辑", "GeneralSettings");
        AddTables(ProjectEditorKind.GeneralProgress, "将领进修、称号与传记", "GeneralPromotionSettings", "GeneralTitleSettings", "GeneralStageSettings");
        AddTables(ProjectEditorKind.HeadquartersTech, "总部科技与 UI 预览", "TechnologySettings");
        AddTables(ProjectEditorKind.Country, "国家与征服国家", "CountrySettings", "ConquerCountrySettings", "ConquerSettings");
        AddTables(ProjectEditorKind.Skill, "技能与等级链", "SkillSettings");
        AddTables(ProjectEditorKind.Army, "兵种编辑", "ArmySettings");
        AddTables(ProjectEditorKind.ArmyProgress, "精英兵种、特性与等级", "EliteArmySettings", "ArmyFeatureSettings", "ArmyLevelSettings");
        var army = this[ProjectEditorKind.Army];
        if (manager.Find("config/def_armypos.xml") == null)
            _options[_options.IndexOf(army)] = army with { IsAvailable = false, Summary = "资源缺失或格式错误", Details = army.Details + "\n缺少 config/def_armypos.xml" };
        AddTables(ProjectEditorKind.ArmyBuff, "军队状态效果", "ArmyBuffSettings");
        AddTables(ProjectEditorKind.BuildingFacility, "建筑与设施", "BuildingSettings", "FacilitySettings");
        AddTables(ProjectEditorKind.CountryTech, "国家科技", "CountryTechSettings");
        AddTables(ProjectEditorKind.ArmyGroup, "集团军与援军", "ArmyGroupSettings", "ArmyGroupReinforcementSettings",
            "EventSettings", "EventStageSettings", "EventCalendarSettings");
        AddTables(ProjectEditorKind.ArmyGroupEvent, "集团军事件", "ArmyGroupEventSettings");
        AddTables(ProjectEditorKind.EventBuff, "事件效果", "EventBuffSettings");
        AddTables(ProjectEditorKind.ConquerEvent, "征服事件", "ConquerEventSettings");
        AddFiles(ProjectEditorKind.Layout, "布局编辑", entries.Count(e => e.Extension == "xml" &&
            !e.RelativePath.Replace('\\', '/').Contains('/') && e.FileNameWithoutExtension.StartsWith("layout", StringComparison.OrdinalIgnoreCase)), "assets/layout*.xml");
        AddFiles(ProjectEditorKind.Resources, "全部资源", entries.Count, "浏览地图、图片、配置、本地化与其他项目资源");
        AddFiles(ProjectEditorKind.JsonTables, "全部配置表", entries.Count(e => e.Extension == "json" &&
            e.RelativePath.Replace('\\', '/').StartsWith("json/", StringComparison.OrdinalIgnoreCase)),
            "逐记录编辑项目 JSON 表，支持新增、克隆、删除与字段类型校验；包含进修、称号、传记、总部科技与精英兵种资源");

        void AddTables(ProjectEditorKind kind, string title, params string[] tables)
        {
            var details = new List<string>();
            int count = 0;
            bool available = true;
            foreach (string table in tables)
            {
                string path = $"json/{table}.json";
                var entry = manager.Find(path);
                if (entry == null)
                {
                    available = false;
                    details.Add($"缺少 {path}");
                    continue;
                }
                try
                {
                    using var doc = JsonDocument.Parse(manager.ReadText(entry), new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
                    });
                    if (doc.RootElement.ValueKind != JsonValueKind.Array ||
                        doc.RootElement.EnumerateArray().Any(row => row.ValueKind != JsonValueKind.Object))
                        throw new InvalidDataException("需要对象数组");
                    int rows = doc.RootElement.GetArrayLength();
                    count += rows;
                    details.Add($"{path}：{rows:N0} 条记录");
                }
                catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    available = false;
                    details.Add($"无法读取 {path}：{ex.Message}");
                }
            }
            _options.Add(new(kind, title, available ? $"{count:N0} 条记录" : "资源缺失或格式错误", available, string.Join("\n", details)));
        }
    }

    private void AddFiles(ProjectEditorKind kind, string title, int count, string details)
        => _options.Add(new(kind, title, count > 0 ? $"{count:N0} 个文件" : "项目中未找到资源", count > 0, details));
}
