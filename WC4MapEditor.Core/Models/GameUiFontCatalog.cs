using System.Globalization;
using System.Xml.Linq;

namespace WC4MapEditor.Core.Models;

public sealed record GameUiFontDefinition(string Name, double Size, double Outline, double OffsetY, string? RelativePath);

public sealed class GameUiFontCatalog
{
    private readonly Dictionary<string, GameUiFontDefinition> _fonts = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, GameUiFontDefinition> Fonts => _fonts;
    public string Profile { get; private set; } = "PHONE/HD";
    public string Locale { get; private set; } = "cn";
    public GameUiFontDefinition Get(string? name) => name != null && _fonts.TryGetValue(name, out var font)
        ? font : new(name ?? "sysfont", 18, 1, 0, $"font/NotoSans_{Locale}.otf");

    public static GameUiFontCatalog Parse(string xml, string locale = "cn", string profile = "PHONE/HD")
    {
        if (locale is not ("cn" or "tw" or "en" or "ja" or "ko")) throw new ArgumentException("不支持的字体语言。", nameof(locale));
        var parts = profile.Split('/');
        if (parts.Length != 2) throw new InvalidDataException("字体配置需要形如 PHONE/HD。");
        var document = XDocument.Parse(xml);
        var section = document.Root?.Element(parts[0])?.Element(parts[1])
            ?? throw new InvalidDataException($"font_resource.xml 缺少 {profile} 配置。");
        var result = new GameUiFontCatalog { Profile = profile, Locale = locale };
        foreach (var element in section.Elements("file"))
        {
            string? name = (string?)element.Attribute("name");
            if (name == null || element.Attribute("size") == null) continue;
            double Read(string key, double fallback)
            {
                string? raw = (string?)element.Attribute(key);
                if (raw == null) return fallback;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
                    throw new InvalidDataException($"字体 {name} 的 {key} 不是有效数字。");
                return number;
            }
            double size = Read("size", 18), outline = Read("outline", 0);
            if (size <= 0 || size > 256 || outline < 0 || outline > 16) throw new InvalidDataException($"字体 {name} 的 size/outline 超出预览范围。");
            string file = (string?)element.Attribute("file") ?? "NotoSans";
            string ext = (string?)element.Attribute("ext") ?? "otf";
            if (file.IndexOfAny(['/', '\\']) >= 0 || file.Contains("..") || ext.IndexOfAny(['/', '\\', '.']) >= 0)
                throw new InvalidDataException($"字体 {name} 路径无效。");
            bool localized = (string?)element.Attribute("lang") == "true";
            result._fonts[name] = new(name, size, outline, Read("offsety", 0), $"font/{file}{(localized ? "_" + locale : "")}.{ext}");
        }
        return result;
    }
}
