using SkiaSharp;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Imaging;

public static class HeadquartersTechPreviewRenderer
{
    public sealed record Sprite(SKBitmap Page, SKRect Source);

    public static SKBitmap Render(HeadquartersTechTree tree, HeadquartersTechGrid grid, int category,
        int? selectedId, Func<string, Sprite?> images, Func<string, string?> strings, SKTypeface? typeface = null)
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
                if (!lookup.TryGetValue(edge.FromId, out var from) || !lookup.TryGetValue(edge.ToId, out var to) ||
                    !Visible(from) || !Visible(to)) continue;
                line.Color = edge.IsUpgrade ? new SKColor(225, 175, 85) : new SKColor(84, 163, 157);
                line.StrokeWidth = 3;
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
            foreach (var cell in nodes.Where(Visible).GroupBy(node => (node.GridRow, node.GridColumn)))
            {
                var first = cell.First();
                float left = (float)(first.X + (grid.CellWidth - grid.CardWidth) / 2), top = (float)first.Y;
                var card = SKRect.Create(left, top + 2, (float)grid.CardWidth, (float)grid.CellHeight - 4);
                var background = images("bg_tech_item.png");
                if (background != null) canvas.DrawBitmap(background.Page, background.Source, card);
                else { paint.Color = new SKColor(65, 71, 72); canvas.DrawRoundRect(card, 5, 5, paint); }
                var members = cell.ToArray();
                for (int member = 0; member < members.Length; member++) DrawNode(members[member], member, members.Length);
            }
            return bitmap;

            void DrawNode(HeadquartersTechNode node, int member, int memberCount)
            {
                float left = (float)(node.X + (grid.CellWidth - grid.CardWidth) / 2), top = (float)node.Y;
                float segmentWidth = (float)grid.CardWidth / memberCount;
                left += member * segmentWidth;
                var card = SKRect.Create(left, top + 2, segmentWidth, (float)grid.CellHeight - 4);
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
