using System.Windows.Media.Imaging;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.Views.Assist;

internal static class FlagImageLoader
{
    private static readonly Dictionary<int, BitmapSource?> Cache = new();
    private static readonly FlagImageService Service = new();
    private static long _revision = -1;

    public static BitmapSource? Load(int countryId)
    {
        if (_revision != AssetManager.Default.Revision)
        {
            ClearCache();
            _revision = AssetManager.Default.Revision;
        }
        if (Cache.TryGetValue(countryId, out var cached)) return cached;
        using var flag = Service.LoadBigFlag(countryId) ?? Service.LoadFlagFromHdAtlas(countryId);
        var result = flag == null ? null : SkiaBitmapSource.Create(flag);
        Cache[countryId] = result;
        return result;
    }

    public static void ClearCache()
    {
        Cache.Clear();
        Service.Dispose();
        _revision = -1;
    }

    public static void ClearMisses()
    {
        foreach (int id in Cache.Where(pair => pair.Value == null).Select(pair => pair.Key).ToArray()) Cache.Remove(id);
    }
}
