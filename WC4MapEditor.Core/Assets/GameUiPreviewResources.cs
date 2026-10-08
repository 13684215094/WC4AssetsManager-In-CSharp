using System.Text.Json;
using System.Text.Json.Serialization;
using WC4MapEditor.Core.Parsers.Layout;

namespace WC4MapEditor.Core.Assets;

public sealed record GameUiLayoutCatalog(IReadOnlyDictionary<string, LayoutDocument> Layouts, IReadOnlyList<string> Issues);

public static class GameUiPreviewResources
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static GameUiLayoutCatalog LoadLayouts(AssetManager manager, params string[] formIds)
    {
        var layouts = new Dictionary<string, LayoutDocument>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();
        foreach (var entry in manager.ListByExtension("xml").Where(entry => !entry.RelativePath.Contains('/') &&
            entry.FileNameWithoutExtension.StartsWith("layout", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var document = new LayoutDocument();
                document.Load(manager.ReadText(entry), entry.RelativePath);
                if (document.Forms.Any(form => formIds.Contains(form.Get("id")))) layouts[entry.RelativePath] = document;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            { issues.Add($"布局 {entry.RelativePath} 无法读取：{ex.Message}"); }
        }
        return new(layouts, issues);
    }

    public static List<T> ReadRows<T>(AssetManager manager, string relativePath, ICollection<string> issues)
    {
        var entry = manager.Find(relativePath);
        if (entry == null) { issues.Add($"项目缺少 {relativePath}。"); return []; }
        try
        {
            var rows = JsonSerializer.Deserialize<List<T>>(manager.ReadText(entry), JsonOptions)
                ?? throw new JsonException("需要 JSON 数组，不能为 null。");
            if (rows.Any(row => row is null)) throw new JsonException("配置数组包含 null 记录。");
            return rows;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { issues.Add($"{relativePath} 无法读取：{ex.Message}"); return []; }
    }
}
