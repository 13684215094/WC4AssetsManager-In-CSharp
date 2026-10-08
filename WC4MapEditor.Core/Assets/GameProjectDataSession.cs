using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers.ArmyGroupSetting;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.Core.Parsers.BuildingSetting;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Parsers.HdAtlas;
using WC4MapEditor.Core.Parsers.Skill;

namespace WC4MapEditor.Core.Assets;

public sealed class GameProjectDataSession
{
    public GameProjectWorkspace? Current { get; private set; }

    public void Activate(GameProjectWorkspace? project, IProgress<ProjectCopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var manager = AssetManager.Default;
        string previousRoot = manager.AssetsRoot;
        bool wasLoaded = manager.IsLoaded;
        try
        {
            if (project?.IsIncremental == true && !Directory.Exists(project.SourceAssetsRoot))
                throw new DirectoryNotFoundException($"来源资源目录不存在：{project.SourceAssetsRoot}");
            if (project == null) manager.Clear();
            else manager.Scan(project.AssetsRoot, forceReload: true, progress, cancellationToken);
            ReloadEditors();
        }
        catch
        {
            if (wasLoaded)
            {
                if (!string.Equals(manager.AssetsRoot, previousRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    manager.Scan(previousRoot, forceReload: true);
                    ReloadEditors();
                }
            }
            else if (manager.IsLoaded)
            {
                manager.Clear();
                ReloadEditors();
            }
            throw;
        }
        Current = project;
    }

    public static void ReloadEditors(bool preserveStringDraft = false)
    {
        // Lazy parsers are recreated from the selected project, including missing tables.
        GeneralSettingParser.ClearInstance();
        CountrySettingParser.ClearInstance();
        ArmySettingParser.ClearInstance();
        ArmyBuffSettingParser.ClearInstance();
        ArmyGroupSettingParser.ClearInstance();
        BuildingFacilitySettingParser.ClearInstance();
        SkillSettingParser.ClearInstance();
        ArmyGroupEventSettingParser.ClearInstance();
        EventBuffSettingParser.ClearInstance();
        ConquerEventSettingParser.ClearInstance();
        CountryTechSettingParser.ClearInstance();
        HdAtlasParser.ClearCache();
        ConfigManager.Instance.Reload(preserveStringDraft);
    }

    public static void RefreshEditorFor(string savedPath)
    {
        // Invalidate only the saved table's clean parser. A draft retains its version guard.
        switch (Path.GetFileName(savedPath).ToLowerInvariant())
        {
            case "generalsettings.json": GeneralSettingParser.ClearInstance(preserveChanges: true); break;
            case "countrysettings.json":
            case "conquercountrysettings.json":
            case "conquersettings.json": CountrySettingParser.ClearInstance(preserveChanges: true); break;
            case "armysettings.json": ArmySettingParser.ClearInstance(preserveChanges: true); break;
            case "armybuffsettings.json": ArmyBuffSettingParser.ClearInstance(preserveChanges: true); break;
            case "armygroupsettings.json":
            case "armygroupreinforcementsettings.json":
            case "eventsettings.json":
            case "eventstagesettings.json":
            case "eventcalendarsettings.json": ArmyGroupSettingParser.ClearInstance(preserveChanges: true); break;
            case "buildingsettings.json":
            case "facilitysettings.json": BuildingFacilitySettingParser.ClearInstance(preserveChanges: true); break;
            case "skillsettings.json": SkillSettingParser.ClearInstance(preserveChanges: true); break;
            case "armygroupeventsettings.json": ArmyGroupEventSettingParser.ClearInstance(preserveChanges: true); break;
            case "eventbuffsettings.json": EventBuffSettingParser.ClearInstance(preserveChanges: true); break;
            case "conquereventsettings.json": ConquerEventSettingParser.ClearInstance(preserveChanges: true); break;
            case "countrytechsettings.json": CountryTechSettingParser.ClearInstance(preserveChanges: true); break;
        }
        ConfigManager.Instance.Reload(preserveStringDraft: true);
    }

    public void RefreshAssets()
    {
        if (Current == null) throw new InvalidOperationException("请先加载游戏项目。");
        AssetManager.Default.Scan(Current.AssetsRoot, forceReload: true);
    }
}
