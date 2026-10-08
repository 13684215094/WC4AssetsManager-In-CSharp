using System.IO;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Parsers.ArmyGroupSetting;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.Core.Parsers.BuildingSetting;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Core.Parsers.General;
using WC4MapEditor.Core.Parsers.HdAtlas;
using WC4MapEditor.Core.Parsers.Skill;
using WC4MapEditor.Rendering.Skia;
using WC4MapEditor.Views.Assist;

namespace WC4MapEditor.Services;

public sealed class GameProjectSession
{
    public GameProjectWorkspace? Current { get; private set; }

    public void Activate(GameProjectWorkspace? project, string? assetsDirectory = null)
    {
        var manager = AssetManager.Default;
        string previousRoot = manager.AssetsRoot;
        manager.Scan(project?.AssetsRoot ?? assetsDirectory ?? AssetManager.GetDefaultAssetsPath(), forceReload: true);
        try { ReloadEditors(); }
        catch
        {
            if (Directory.Exists(previousRoot)) manager.Scan(previousRoot, forceReload: true);
            else manager.Clear();
            ReloadEditors();
            throw;
        }
        RenderSceneManager.Instance.ClearAllScenes();
        Current = project;
    }

    public static void ReloadEditors()
    {
        ConfigManager.Instance.Reload();
        GeneralSettingParser.Instance.Reload();
        CountrySettingParser.Instance.Reload();
        // Editors keep paths in their singleton parsers; resolve them again for the new project.
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
        HdAtlasImageLoader.ClearCache();
        FlagImageLoader.ClearCache();
        LayoutImageProvider.ClearCache(clearExtra: true);
        LayoutFontProvider.ClearCache();
        TacticalMapImageCache.Instance.ClearCache();
        BuildingRender.ClearCityNameCache();
    }
}
