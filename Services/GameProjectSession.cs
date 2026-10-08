using WC4MapEditor.Core.Assets;
using WC4MapEditor.Rendering.Skia;
using WC4MapEditor.Views.Assist;

namespace WC4MapEditor.Services;

public sealed class GameProjectSession
{
    public sealed record GeneralViewState(string Search, int TypeFilter, bool ChangedOnly, int? SelectedId, double ScrollOffset, int TabIndex);
    public GeneralViewState? GeneralView { get; set; }
    private readonly GameProjectDataSession _data = new();
    public GameProjectWorkspace? Current => _data.Current;

    public void Activate(GameProjectWorkspace? project)
    {
        _data.Activate(project);
        GeneralView = null;
        ClearViewCaches();
        RenderSceneManager.Instance.ClearAllScenes();
    }

    public async Task ActivateAsync(GameProjectWorkspace project, IProgress<ProjectCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        await Task.Run(() => _data.Activate(project, progress, cancellationToken));
        GeneralView = null;
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
