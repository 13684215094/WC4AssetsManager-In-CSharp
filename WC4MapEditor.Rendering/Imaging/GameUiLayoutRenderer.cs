using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.Layout;

namespace WC4MapEditor.Rendering.Imaging;

public sealed record GameUiHitRegion(int SkillId, SKRect Bounds);
public sealed record GameUiRenderResult(SKBitmap Bitmap, IReadOnlyList<string> Issues, IReadOnlyList<GameUiHitRegion> HitRegions) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
}

public static class GameUiLayoutRenderer
{
    private sealed record Glyph(string Text, SKColor Color, float Width);
    private static readonly Regex RichText = new(@"\[#([0-9A-Fa-f]{6})#([^\]]*)\]", RegexOptions.Compiled);
    public static string PlainText(string text) => RichText.Replace(text, "$2").Replace(@"\n", "\n");

    public static GameUiRenderResult Render(GameUiPreviewFrame frame, GameUiFontCatalog fonts,
        Func<string, GameUiSprite?> images, Func<string, string?> strings, Func<GameUiFontDefinition, SKTypeface> typefaces)
    {
        var issues = frame.Issues.ToHashSet();
        var hitRegions = new List<GameUiHitRegion>();
        var root = frame.Root;
        double w = root.Num("width"), h = root.Num("height");
        if (!double.IsFinite(w) || !double.IsFinite(h) || w < 1 || h < 1 || w > 4096 || h > 4096)
            throw new InvalidDataException("预览表单需要有效的 width/height，单边不能超过 4096。");
        var activeImages = Visible(root).Where(node => node.Tag != "Animation").Select(LayoutDocument.ImageRefOf)
            .Where(reference => reference != null).Select(reference => reference!.Value.Ref).ToHashSet();
        var context = new LayoutEngine.Context
        {
            ImageSize = reference => activeImages.Contains(reference) && images(reference) is { } sprite ? (sprite.Source.Width, sprite.Source.Height) : null,
            DefaultFontSize = fonts.Get("font_text_1").Size,
            FontSizeLookup = name => fonts.Get(name).Size,
            StringLookup = strings,
            MeasureText = (text, size) =>
            {
                using var font = new SKFont(typefaces(fonts.Get("font_text_1")), (float)size);
                return PlainText(text).Split('\n').Max(line => (double)font.MeasureText(line));
            }
        };
        LayoutEngine.Layout(root, 12, 12, w, h, ctx: context);
        var visible = Visible(root).ToArray();
        foreach (var node in visible)
            if (!double.IsFinite(node.X + node.Y + node.W + node.H) || node.W > 8192 || node.H > 8192 ||
                Math.Abs(node.X) > 8192 || Math.Abs(node.Y) > 8192)
                throw new InvalidDataException($"布局 {node} 的坐标或尺寸超出预览范围。");
        var bounds = visible.Select(EffectiveBounds).Where(rect => !rect.IsEmpty).ToArray();
        int width = (int)Math.Ceiling(Math.Max(w + 24, bounds.Max(rect => rect.Right) + 12));
        int height = (int)Math.Ceiling(Math.Max(h + 24, bounds.Max(rect => rect.Bottom) + 12));
        if (width <= 0 || height <= 0 || (long)width * height > 16_000_000)
            throw new InvalidDataException("预览画布过大，请检查布局和头像缩放。");
        var bitmap = new SKBitmap(width, height);
        try
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(20, 24, 27));
            using var paint = new SKPaint { Color = new SKColor(59, 65, 66), IsAntialias = true };
            canvas.DrawRoundRect(SKRect.Create(8, 8, (float)w + 8, (float)h + 8), 6, 6, paint);
            var header = images("image/image_ui_hd5/window_title_bg.png");
            if (header != null) DrawImage(canvas, header, SKRect.Create(12, 12, (float)w, 40), "hextend", 255);
            DrawText(canvas, frame.Title, SKRect.Create(12, 12, (float)w, 40), fonts.Get("font_text_2"),
                "center", "center", false, SKColors.White, typefaces, issues, "窗口标题");
            DrawNode(root);
            return new(bitmap, issues.ToArray(), hitRegions);

            void DrawNode(LayoutNode node)
            {
                if (!LayoutDocument.VisibleKid(node)) return;
                var rect = SKRect.Create((float)node.X, (float)node.Y, (float)node.W, (float)node.H);
                canvas.Save();
                if (node.Get("clipped") is { } clipped && !LayoutDocument.IsFalseValue(clipped)) canvas.ClipRect(rect);
                if (node.Tag != "Animation" && LayoutDocument.ImageRefOf(node) is { } reference)
                {
                    var sprite = images(reference.Ref);
                    if (sprite != null) DrawImage(canvas, sprite, rect, node.Get("drawmode") ?? "", Alpha(node));
                    else
                    {
                        issues.Add($"缺少图片：{reference.Ref}");
                        if (node.Tag == "Image")
                        {
                            paint.Color = new SKColor(148, 66, 52, 100); canvas.DrawRect(rect, paint);
                            paint.Style = SKPaintStyle.Stroke; paint.Color = new SKColor(210, 117, 94); canvas.DrawRect(rect, paint); paint.Style = SKPaintStyle.Fill;
                        }
                    }
                }
                if (node.Tag is "Label" or "TextBox" or "EditBox")
                {
                    if (node.Get("string") == null && node.Get("text") is { } key && strings(key) == null)
                        issues.Add($"本地化缺少 {key}。");
                    string text = LayoutEngine.TextOf(node, context);
                    var definition = fonts.Get(node.Get("font"));
                    if (node.Get("font") is { } fontName && !fonts.Fonts.ContainsKey(fontName))
                        issues.Add($"字体 {fontName} 未定义，使用默认字号。");
                    DrawText(canvas, text, rect, definition, node.Get("htextalign") ?? "center", node.Get("vtextalign") ?? "center",
                        node.Get("breakwords") is { } wrap && !LayoutDocument.IsFalseValue(wrap), Color(node.Get("color")), typefaces,
                        issues, node.Get("id") ?? "Label");
                }
                if (int.TryParse(node.Get("previewSkillId"), out int skillId) && rect.Width > 0 && rect.Height > 0)
                {
                    var hitBounds = EffectiveBounds(node);
                    if (!hitBounds.IsEmpty) hitRegions.Add(new(skillId, hitBounds));
                }
                foreach (var child in LayoutDocument.ExpandedKids(node) ?? LayoutDocument.RealKids(node)) DrawNode(child);
                canvas.Restore();
            }
        }
        catch { bitmap.Dispose(); throw; }
    }

    private static SKRect EffectiveBounds(LayoutNode node)
    {
        var bounds = SKRect.Create((float)node.X, (float)node.Y, (float)node.W, (float)node.H);
        for (var parent = node.Parent; parent != null; parent = parent.Parent)
            if (parent.Get("clipped") is { } clip && !LayoutDocument.IsFalseValue(clip))
            {
                bounds = new SKRect(Math.Max(bounds.Left, (float)parent.X), Math.Max(bounds.Top, (float)parent.Y),
                    Math.Min(bounds.Right, (float)(parent.X + parent.W)), Math.Min(bounds.Bottom, (float)(parent.Y + parent.H)));
                if (bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return SKRect.Empty;
            }
        return bounds;
    }

    private static IEnumerable<LayoutNode> Visible(LayoutNode node)
    {
        if (!LayoutDocument.VisibleKid(node)) yield break;
        yield return node;
        foreach (var child in LayoutDocument.ExpandedKids(node) ?? LayoutDocument.RealKids(node))
            foreach (var descendant in Visible(child)) yield return descendant;
    }

    private static byte Alpha(LayoutNode node) => double.TryParse(node.Get("alpha"), NumberStyles.Float,
        CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? (byte)Math.Clamp(value <= 1 ? value * 255 : value, 0, 255) : (byte)255;

    private static SKColor Color(string? raw)
    {
        if (raw == null) return SKColors.White;
        var values = raw.Split(',');
        if (values.Length < 3) return SKColors.White;
        byte Part(int i, byte fallback) => i < values.Length && byte.TryParse(values[i], out byte number) ? number : fallback;
        return new(Part(0, 255), Part(1, 255), Part(2, 255), Part(3, 255));
    }

    private static void DrawImage(SKCanvas canvas, GameUiSprite sprite, SKRect target, string mode, byte alpha)
    {
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(alpha), IsAntialias = true };
        var src = sprite.Source;
        if (mode == "tile")
        {
            canvas.Save(); canvas.ClipRect(target);
            for (float y = target.Top; y < target.Bottom; y += src.Height)
                for (float x = target.Left; x < target.Right; x += src.Width)
                    canvas.DrawBitmap(sprite.Page, src, SKRect.Create(x, y, src.Width, src.Height), paint);
            canvas.Restore(); return;
        }
        // WC4 extend textures retain their end caps; stretch scales the whole image.
        if (mode is "extend" or "hextend" or "vextend")
        {
            float capX = mode == "vextend" ? 0 : Math.Min(src.Width / 3, target.Width / 3);
            float capY = mode == "hextend" ? 0 : Math.Min(src.Height / 3, target.Height / 3);
            float[] sx = [src.Left, src.Left + capX, src.Right - capX, src.Right];
            float[] sy = [src.Top, src.Top + capY, src.Bottom - capY, src.Bottom];
            float[] dx = [target.Left, target.Left + capX, target.Right - capX, target.Right];
            float[] dy = [target.Top, target.Top + capY, target.Bottom - capY, target.Bottom];
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
                if (sx[x + 1] > sx[x] && sy[y + 1] > sy[y])
                    canvas.DrawBitmap(sprite.Page, new SKRect(sx[x], sy[y], sx[x + 1], sy[y + 1]), new SKRect(dx[x], dy[y], dx[x + 1], dy[y + 1]), paint);
            return;
        }
        canvas.DrawBitmap(sprite.Page, src, target, paint);
    }

    private static void DrawText(SKCanvas canvas, string text, SKRect rect, GameUiFontDefinition definition,
        string horizontal, string vertical, bool wrap, SKColor color, Func<GameUiFontDefinition, SKTypeface> typefaces,
        ISet<string> issues, string id)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        using var font = new SKFont(typefaces(definition), (float)definition.Size);
        font.Edging = SKFontEdging.Antialias;
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        var glyphs = new List<Glyph>();
        text = text.Replace(@"\n", "\n");
        int offset = 0;
        foreach (Match match in RichText.Matches(text))
        {
            Append(text[offset..match.Index], color);
            Append(match.Groups[2].Value, SKColor.Parse("#" + match.Groups[1].Value).WithAlpha(color.Alpha));
            offset = match.Index + match.Length;
        }
        Append(text[offset..], color);
        var lines = new List<List<Glyph>> { new() };
        float width = 0;
        foreach (var glyph in glyphs)
        {
            if (glyph.Text == "\n") { lines.Add([]); width = 0; continue; }
            if (wrap && width > 0 && width + glyph.Width > rect.Width - 4) { lines.Add([]); width = 0; }
            lines[^1].Add(glyph); width += glyph.Width;
        }
        float lineHeight = (float)definition.Size * 1.2f;
        var metrics = font.Metrics;
        float totalHeight = lines.Count * lineHeight;
        if (totalHeight > rect.Height + 2 || lines.Any(line => line.Sum(glyph => glyph.Width) > rect.Width + 2))
            issues.Add($"文字 {id} 超出区域 ({rect.Width:0}×{rect.Height:0})，已裁切；可调整字号、换行或布局尺寸。");
        float y = rect.Top + (vertical == "bottom" ? rect.Height - totalHeight : vertical == "center" ? (rect.Height - totalHeight) / 2 : 0)
            + (lineHeight - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent + (float)definition.OffsetY;
        canvas.Save(); canvas.ClipRect(rect);
        foreach (var line in lines)
        {
            float lineWidth = line.Sum(glyph => glyph.Width);
            float x = rect.Left + (horizontal == "right" ? rect.Width - lineWidth : horizontal == "center" ? (rect.Width - lineWidth) / 2 : 2);
            foreach (var glyph in line)
            {
                if (definition.Outline > 0)
                {
                    paint.Color = SKColors.Black.WithAlpha(color.Alpha); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = (float)definition.Outline * 2;
                    canvas.DrawText(glyph.Text, x, y, font, paint);
                }
                paint.Style = SKPaintStyle.Fill; paint.Color = glyph.Color;
                canvas.DrawText(glyph.Text, x, y, font, paint); x += glyph.Width;
            }
            y += lineHeight;
        }
        canvas.Restore();

        void Append(string segment, SKColor tint)
        {
            foreach (var rune in segment.EnumerateRunes())
            {
                if (rune.Value == '\r') continue;
                string character = rune.ToString();
                glyphs.Add(new(character, tint, rune.Value == '\n' ? 0 : font.MeasureText(character)));
            }
        }
    }
}
