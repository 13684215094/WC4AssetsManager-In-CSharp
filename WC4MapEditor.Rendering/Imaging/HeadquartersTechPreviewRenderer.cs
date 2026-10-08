using SkiaSharp;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Imaging;

public static class HeadquartersTechPreviewRenderer
{
    public sealed record Sprite(SKBitmap Page, SKRect Source);
    public sealed record CardRegion(HeadquartersTechNode Node, SKRect Bounds);

    public static IReadOnlyList<CardRegion> GetCardRegions(HeadquartersTechTree tree, HeadquartersTechGrid grid, int category)
    {
        return tree.Nodes.Where(node => node.Category == category && node.GridColumn > 0 && node.GridColumn <= grid.Columns &&
                node.GridRow > 0 && node.GridRow <= grid.Rows &&
                (long)(node.GridRow - 1) * grid.Columns + node.GridColumn <= grid.ItemCount)
            .GroupBy(node => (node.GridRow, node.GridColumn)).SelectMany(group =>
            {
                var members = group.ToArray(); float width = (float)grid.CardWidth / members.Length;
                return members.Select((node, index) => new CardRegion(node, SKRect.Create(
                    (float)(node.X + (grid.CellWidth - grid.CardWidth) / 2) + index * width,
                    (float)node.Y + 2, width, (float)grid.CellHeight - 4)));
            }).ToArray();
    }

    public static SKBitmap Render(HeadquartersTechTree tree, HeadquartersTechGrid grid, int category,
        int? selectedId, Func<string, Sprite?> images, Func<string, string?> strings, SKTypeface? typeface = null,
        bool showUpgrades = true, bool showDependencies = true)
    {
        int width = checked((int)Math.Ceiling(grid.Width)), height = checked((int)Math.Ceiling(grid.Height));
        if ((long)width * height > 16_000_000) throw new InvalidDataException("科技预览区域过大，请缩小布局尺寸。");
        var bitmap = new SKBitmap(width, height);
        try
        {
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(new SKColor(23, 29, 32));
            using var line = new SKPaint { Color = new SKColor(60, 73, 77), StrokeWidth = 1, IsAntialias = true };
            for (int column = 0; column <= grid.Columns; column++)
                canvas.DrawLine((float)(column * grid.CellWidth), 0, (float)(column * grid.CellWidth), height, line);
            for (int row = 0; row <= grid.Rows; row++)
                canvas.DrawLine(0, (float)(row * grid.CellHeight), width, (float)(row * grid.CellHeight), line);
            var nodes = tree.Nodes.Where(node => node.Category == category).ToArray();
            var lookup = nodes.GroupBy(node => node.Id).ToDictionary(group => group.Key, group => group.First());
            foreach (var edge in tree.Edges)
            {
                if (edge.IsUpgrade ? !showUpgrades : !showDependencies) continue;
                if (!lookup.TryGetValue(edge.FromId, out var from) || !lookup.TryGetValue(edge.ToId, out var to) ||
                    !Visible(from) || !Visible(to)) continue;
                line.Color = edge.IsUpgrade ? new SKColor(225, 175, 85) : new SKColor(84, 163, 157);
                line.StrokeWidth = selectedId == edge.FromId || selectedId == edge.ToId ? 5 : 2;
                float x1 = (float)(from.X + grid.CellWidth / 2), y1 = (float)(from.Y + grid.CellHeight / 2);
                float x2 = (float)(to.X + grid.CellWidth / 2), y2 = (float)(to.Y + grid.CellHeight / 2);
                if (!float.IsFinite(x1 + x2 + y1 + y2)) continue;
                canvas.DrawLine(x1, y1, x2, y2, line);
                double angle = Math.Atan2(y2 - y1, x2 - x1);
                float ax = x1 + (x2 - x1) * 0.67f, ay = y1 + (y2 - y1) * 0.67f;
                canvas.DrawLine(ax, ay, ax - 10 * (float)Math.Cos(angle - 0.4), ay - 10 * (float)Math.Sin(angle - 0.4), line);
                canvas.DrawLine(ax, ay, ax - 10 * (float)Math.Cos(angle + 0.4), ay - 10 * (float)Math.Sin(angle + 0.4), line);
            }
            using var font = new SKFont(typeface ?? SKTypeface.Default, 16);
            using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
            foreach (var cell in GetCardRegions(tree, grid, category).GroupBy(region => (region.Node.GridRow, region.Node.GridColumn)))
            {
                var first = cell.First();
                var card = new SKRect(first.Bounds.Left, first.Bounds.Top, cell.Last().Bounds.Right, first.Bounds.Bottom);
                var background = images("bg_tech_item.png");
                if (background != null) canvas.DrawBitmap(background.Page, background.Source, card);
                else { paint.Color = new SKColor(65, 71, 72); canvas.DrawRoundRect(card, 5, 5, paint); }
                foreach (var region in cell) DrawNode(region.Node, region.Bounds);
            }
            return bitmap;

            void DrawNode(HeadquartersTechNode node, SKRect card)
            {
                float left = card.Left, top = (float)node.Y, segmentWidth = card.Width;
                string title = strings($"tech_name_{node.Type}") ?? $"Type {node.Type}";
                paint.Color = SKColors.White;
                canvas.Save(); canvas.ClipRect(card);
                DrawFitted(title, top + 20, 16);
                var icon = images($"tech_{node.Type}.png") ?? images($"tech_{node.Type}_1.png") ?? images($"tech_{node.Type}_2.png");
                if (icon != null)
                {
                    float size = Math.Min(Math.Min(48, (float)grid.CellHeight - 53), segmentWidth - 4);
                    float x = left + (segmentWidth - size) / 2;
                    canvas.DrawBitmap(icon.Page, icon.Source, SKRect.Create(x, top + 25, size, size));
                }
                DrawFitted($"Lv{node.Level}", top + (float)grid.CellHeight - 19, 13);
                DrawFitted($"#{node.Id}", top + (float)grid.CellHeight - 5, 11);
                canvas.Restore();
                if (selectedId == node.Id)
                {
                    paint.Color = new SKColor(255, 208, 112); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 3;
                    canvas.DrawRect(card, paint); paint.Style = SKPaintStyle.Fill;
                }

                void DrawFitted(string text, float baseline, float size)
                {
                    font.Size = size;
                    float measured = font.MeasureText(text), available = Math.Max(1, segmentWidth - 14);
                    if (measured > available) font.Size = Math.Max(9, size * available / measured);
                    float x = left + (segmentWidth - font.MeasureText(text)) / 2;
                    canvas.DrawText(text, x, baseline, font, paint);
                }
            }
        }
        catch { bitmap.Dispose(); throw; }

        bool Visible(HeadquartersTechNode node) => node.GridColumn > 0 && node.GridColumn <= grid.Columns &&
            node.GridRow > 0 && node.GridRow <= grid.Rows &&
            (long)(node.GridRow - 1) * grid.Columns + node.GridColumn <= grid.ItemCount;
    }
}
