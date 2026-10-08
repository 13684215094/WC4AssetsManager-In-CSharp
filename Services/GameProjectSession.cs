using WC4MapEditor.Core.Assets;
using WC4MapEditor.Rendering.Skia;
using WC4MapEditor.Views.Assist;

namespace WC4MapEditor.Services;

public sealed class GameProjectSession
{
    private readonly GameProjectDataSession _data = new();
    public GameProjectWorkspace? Current => _data.Current;

    public void Activate(GameProjectWorkspace? project)
    {
        _data.Activate(project);
        ClearViewCaches();
        RenderSceneManager.Instance.ClearAllScenes();
    }

    public async Task ActivateAsync(GameProjectWorkspace project, IProgress<ProjectCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        await Task.Run(() => _data.Activate(project, progress, cancellationToken));
        ClearViewCaches();
        RenderSceneManager.Instance.ClearAllScenes();
    }

    public void RefreshAssets()
    {
        _data.RefreshAssets();
        ClearViewCaches();
    }

    private static void ClearViewCaches()
    {
        HdAtlasImageLoader.ClearCache();
        FlagImageLoader.ClearCache();
        LayoutImageProvider.ClearCache(clearExtra: true);
        LayoutFontProvider.ClearCache();
        TacticalMapImageCache.Instance.ClearCache();
        BuildingRender.ClearCityNameCache();
    }
}
