using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
using System.Diagnostics;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.HdAtlas;

namespace WC4MapEditor.Rendering.Imaging;

public class FlagEditorService : IFlagEditorService
{
    private static readonly SKSamplingOptions HighQuality = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly string? _overlayImagePath;

    public FlagEditorService(string? overlayImagePath = null) => _overlayImagePath = overlayImagePath;

    private SKBitmap GetFlagOverlayImage(int countryId)
    {
        if (_overlayImagePath != null)
            return ProjectImageDecoder.Decode(ProjectFile.ReadAllBytes(_overlayImagePath));
        foreach (string atlas in new[] { "tacticalmap", "image/image_flags_hd" })
        {
            var parser = HdAtlasParser.Get(atlas);
            if (parser.SurfaceData == null) continue;
            var template = parser.ImageDefinitions
                .Where(def => def.Name.StartsWith("flag_", StringComparison.OrdinalIgnoreCase) && def.Width == 52 && def.Height == 54)
                .OrderBy(def => def.Name == $"flag_{countryId}.png" ? 0 : def.Name == "flag_1.png" ? 1 : 2)
                .ThenBy(def => def.Name, StringComparer.Ordinal).FirstOrDefault();
            if (template == null) continue;
            using var page = ProjectImageDecoder.Decode(parser.SurfaceData);
            var overlay = BitmapPixels.Crop(page, template.X, template.Y, template.Width, template.Height);
            using var canvas = new SKCanvas(overlay);
            using var paint = new SKPaint { BlendMode = SKBlendMode.Clear, IsAntialias = true };
            // Remove the country's design while keeping the selected project's outer ring and shadow.
            canvas.DrawCircle(26.5f, 25.5f, 23.5f, paint);
            return overlay;
        }
        throw new InvalidDataException("当前项目没有 52×54 国旗模板，无法提取边框。请先导入兼容的国旗图块。");
    }

    public SKBitmap? LoadSourceImage(string path)
    {
        try
        {
            return ProjectImageDecoder.Decode(ProjectFile.ReadAllBytes(path));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 加载图片失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? CreateCanvasWithImage(SKBitmap sourceImage, int canvasWidth, int canvasHeight, SKColor bgColor, int srcX, int srcY)
    {
        try
        {
            var info = new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            var canvas = new SKBitmap(info);
            using var surface = new SKCanvas(canvas);

            surface.Clear(bgColor);

            int drawX = srcX + (canvasWidth - sourceImage.Width) / 2;
            int drawY = srcY + (canvasHeight - sourceImage.Height) / 2;
            surface.DrawBitmap(sourceImage, drawX, drawY);

            return canvas;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 创建画布失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? CropCircularRegion(SKBitmap image, int x0, int y0, int x1, int y1)
    {
        try
        {
            int cx = (x0 + x1) / 2;
            int cy = (y0 + y1) / 2;
            int diameter = Math.Min(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            int radius = diameter / 2;

            if (diameter <= 0) return null;

            var info = new SKImageInfo(diameter, diameter, SKColorType.Rgba8888, SKAlphaType.Premul);
            var result = new SKBitmap(info);
            using var canvas = new SKCanvas(result);
            canvas.Clear(SKColors.Transparent);

            using var path = new SKPath();
            path.AddCircle(radius, radius, radius);
            canvas.ClipPath(path);

            // 从源图像的指定区域绘制到目标圆形区域
            // 源区域: 以(cx,cy)为中心，直径为diameter的矩形
            int srcLeft = cx - radius;
            int srcTop = cy - radius;
            canvas.DrawBitmap(image, new SKRectI(srcLeft, srcTop, srcLeft + diameter, srcTop + diameter),
                new SKRect(0, 0, diameter, diameter));
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 裁剪圆形区域失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? ResizeImage(SKBitmap image, int width, int height)
    {
        try
        {
            var info = new SKImageInfo(width, height, image.ColorType, image.AlphaType);
            return image.Resize(info, HighQuality);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 缩放图片失败: {ex.Message}");
            return null;
        }
    }

    public bool SaveImage(SKBitmap image, string path, SKEncodedImageFormat format, int quality)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !ProjectFile.DirectoryExists(dir))
                Directory.CreateDirectory(dir);

            using var data = image.Encode(format, quality);
            if (data == null) return false;
            WC4MapEditor.Core.Parsers.AtomicFile.Write(path, data.ToArray());
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 保存图片失败: {ex.Message}");
            return false;
        }
    }

    public FlagEditorSaveResult SaveFlagImages(SKBitmap canvasImage, SKBitmap? circleCropped, string baseName, string outputDir, int resize1W, int resize1H, int resize2W, int resize2H)
    {
        var result = new FlagEditorSaveResult();
        try
        {
            AtlasCompositeRenderer.ValidateBaseName(baseName);
            var files = new List<(string Path, byte[] Bytes)>();
            var flagPath = Path.Combine(outputDir, $"flag_{baseName}.png");
            files.Add((flagPath, AtlasCompositeRenderer.Encode(canvasImage, flagPath)));
            string? circlePath = null, smallPath = null;

            if (circleCropped != null)
            {
                circlePath = Path.Combine(outputDir, $"flag_circle_{baseName}.png");
                files.Add((circlePath, AtlasCompositeRenderer.Encode(circleCropped, circlePath)));

                using var resizedSmall = ResizeImage(circleCropped, resize2W, resize2H);
                if (resizedSmall == null) throw new IOException("小国旗缩放失败。");
                smallPath = Path.Combine(outputDir, $"f_{baseName}.png");
                files.Add((smallPath, AtlasCompositeRenderer.Encode(resizedSmall, smallPath)));
            }
            WC4MapEditor.Core.Parsers.AtomicFile.WriteAll(files.ToArray());
            result.FlagPath = flagPath;
            result.CirclePath = circlePath;
            result.SmallPath = smallPath;
            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            return result;
        }
    }

    // ========== 新增国旗制作功能 ==========

    public SKBitmap? CreateTiledCanvas(SKBitmap? sourceImage, int canvasWidth, int canvasHeight)
    {
        try
        {
            var info = new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            var canvas = new SKBitmap(info);
            using var surface = new SKCanvas(canvas);

            // 透明背景
            surface.Clear(SKColors.Transparent);

            if (sourceImage != null)
            {
                // 平铺绘制图片
                DrawTiledPattern(surface, sourceImage, canvasWidth, canvasHeight, 0, 0, 1.0f);
            }

            return canvas;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 创建平铺画布失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? DrawTiledImage(SKBitmap canvas, SKBitmap sourceImage, float offsetX, float offsetY, float scale)
    {
        try
        {
            using var surface = new SKCanvas(canvas);

            // 计算缩放后的图片尺寸
            int scaledWidth = (int)(sourceImage.Width * scale);
            int scaledHeight = (int)(sourceImage.Height * scale);

            if (scaledWidth <= 0 || scaledHeight <= 0) return canvas;

            // 缩放图片
            using var scaledImage = sourceImage.Resize(
                new SKImageInfo(scaledWidth, scaledHeight, SKColorType.Rgba8888, SKAlphaType.Premul),
                HighQuality);

            if (scaledImage == null) return canvas;

            // 平铺绘制，考虑偏移
            DrawTiledPattern(surface, scaledImage, canvas.Width, canvas.Height, offsetX, offsetY, 1.0f);

            return canvas;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 绘制平铺图片失败: {ex.Message}");
            return null;
        }
    }

    private void DrawTiledPattern(SKCanvas surface, SKBitmap pattern, int canvasWidth, int canvasHeight, float offsetX, float offsetY, float scale)
    {
        int patternWidth = (int)(pattern.Width * scale);
        int patternHeight = (int)(pattern.Height * scale);

        if (patternWidth <= 0 || patternHeight <= 0) return;

        // 计算起始位置（考虑偏移）
        float startX = offsetX % patternWidth;
        if (startX > 0) startX -= patternWidth;

        float startY = offsetY % patternHeight;
        if (startY > 0) startY -= patternHeight;

        // 平铺绘制
        for (float y = startY; y < canvasHeight; y += patternHeight)
        {
            for (float x = startX; x < canvasWidth; x += patternWidth)
            {
                surface.DrawBitmap(pattern, x, y);
            }
        }
    }

    public SKBitmap? ExtendEdgesToCanvas(SKBitmap sourceImage, int canvasWidth, int canvasHeight, float offsetX = 0, float offsetY = 0, float scale = 1.0f)
    {
        try
        {
            var info = new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            var result = new SKBitmap(info);
            using var surface = new SKCanvas(result);

            // 清空画布
            surface.Clear(SKColors.Transparent);

            // 计算缩放后的图片尺寸和位置
            int scaledWidth = (int)(sourceImage.Width * scale);
            int scaledHeight = (int)(sourceImage.Height * scale);

            if (scaledWidth <= 0 || scaledHeight <= 0) return result;

            // 缩放图片
            using var scaledImage = sourceImage.Resize(
                new SKImageInfo(scaledWidth, scaledHeight, SKColorType.Rgba8888, SKAlphaType.Premul),
                HighQuality);

            if (scaledImage == null) return result;

            // 计算图片在画布中的位置（居中+偏移）
            float drawX = (canvasWidth - scaledWidth) / 2 + offsetX;
            float drawY = (canvasHeight - scaledHeight) / 2 + offsetY;

            // 先绘制原始图片
            surface.DrawBitmap(scaledImage, drawX, drawY);

            // 获取像素数据指针
            if (result.GetPixels() == IntPtr.Zero) return result;

            int rowBytes = result.RowBytes;
            unsafe
            {
                byte* pixels = (byte*)result.GetPixels();

                // 计算图片在画布中的整数边界（只处理图片实际覆盖的区域）
                int imgLeft = (int)Math.Round(drawX);
                int imgTop = (int)Math.Round(drawY);
                int imgRight = imgLeft + scaledWidth - 1;
                int imgBottom = imgTop + scaledHeight - 1;

                // 确保边界在画布范围内
                imgLeft = Math.Clamp(imgLeft, 0, canvasWidth - 1);
                imgTop = Math.Clamp(imgTop, 0, canvasHeight - 1);
                imgRight = Math.Clamp(imgRight, 0, canvasWidth - 1);
                imgBottom = Math.Clamp(imgBottom, 0, canvasHeight - 1);

                // 1. 先处理上下边缘的延伸（水平方向：只延伸到图片左右边界）
                // 上边缘延伸：将图片最上面1行像素向上延伸到画布顶部
                if (imgTop > 0)
                {
                    for (int y = 0; y < imgTop; y++)
                    {
                        for (int x = imgLeft; x <= imgRight; x++)
                        {
                            CopyPixel(pixels, x, y, x, imgTop, rowBytes);
                        }
                    }
                }

                // 下边缘延伸：将图片最下面1行像素向下延伸到画布底部
                if (imgBottom < canvasHeight - 1)
                {
                    for (int y = imgBottom + 1; y < canvasHeight; y++)
                    {
                        for (int x = imgLeft; x <= imgRight; x++)
                        {
                            CopyPixel(pixels, x, y, x, imgBottom, rowBytes);
                        }
                    }
                }

                // 2. 再处理左右边缘的延伸（垂直方向：覆盖整个画布高度）
                // 左边缘延伸：将图片最左边1列像素向左延伸到画布左边缘
                if (imgLeft > 0)
                {
                    for (int x = 0; x < imgLeft; x++)
                    {
                        for (int y = 0; y < canvasHeight; y++)
                        {
                            int srcX = imgLeft;
                            int srcY = Math.Clamp(y, 0, canvasHeight - 1);
                            CopyPixel(pixels, x, y, srcX, srcY, rowBytes);
                        }
                    }
                }

                // 右边缘延伸：将图片最右边1列像素向右延伸到画布右边缘
                if (imgRight < canvasWidth - 1)
                {
                    for (int x = imgRight + 1; x < canvasWidth; x++)
                    {
                        for (int y = 0; y < canvasHeight; y++)
                        {
                            int srcX = imgRight;
                            int srcY = Math.Clamp(y, 0, canvasHeight - 1);
                            CopyPixel(pixels, x, y, srcX, srcY, rowBytes);
                        }
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 拉伸边缘失败: {ex.Message}");
            return null;
        }
    }

    private static unsafe void CopyPixel(byte* pixels, int dstX, int dstY, int srcX, int srcY, int rowBytes)
    {
        byte* dst = pixels + dstY * rowBytes + dstX * 4;
        byte* src = pixels + srcY * rowBytes + srcX * 4;
        dst[0] = src[0];
        dst[1] = src[1];
        dst[2] = src[2];
        dst[3] = src[3];
    }

    public SKBitmap? CropAndResizeCircle(SKBitmap image, int x, int y, int size, int targetSize)
    {
        try
        {
            // 裁剪圆形区域
            var circleCropped = CropCircularRegion(image, x, y, x + size, y + size);
            if (circleCropped == null) return null;

            // 缩放到目标大小
            using (circleCropped)
            {
                return ResizeImage(circleCropped, targetSize, targetSize);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 裁剪并缩放圆形失败: {ex.Message}");
            return null;
        }
    }

    public SKBitmap? CreateMaskedFlag(SKBitmap circleFlag, SKBitmap overlay)
    {
        try
        {
            const int canvasWidth = 52;
            const int canvasHeight = 54;

            var info = new SKImageInfo(canvasWidth, canvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            var result = new SKBitmap(info);
            using var surface = new SKCanvas(result);

            surface.Clear(SKColors.Transparent);

            // 将圆形国旗贴到 (3, 2)
            surface.DrawBitmap(circleFlag, 3, 2);

            // 将 overlay 贴到 (0, 0)
            surface.DrawBitmap(overlay, 0, 0);

            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 创建遮罩国旗失败: {ex.Message}");
            return null;
        }
    }

    public FlagMakerResult MakeFlagWorkflow(SKBitmap workingCanvas, int countryId, string outputDir)
    {
        var result = new FlagMakerResult();
        try
        {
            using var circle = CropAndResizeCircle(workingCanvas, 0, 0, 150, 47)
                ?? throw new InvalidOperationException("圆形国旗裁剪失败。");
            using var overlay = GetFlagOverlayImage(countryId);
            using var flag = CreateMaskedFlag(circle, overlay) ?? throw new InvalidOperationException("国旗遮罩生成失败。");
            using var small = ResizeImage(flag, 37, 38) ?? throw new InvalidOperationException("小国旗缩放失败。");
            var files = new List<(string Path, byte[] Bytes)>();
            var (tacticalImage, tacticalXml) = FindTacticalMapFiles();
            if (tacticalImage != null && tacticalXml != null)
            {
                var editor = new TacticalMapEditor();
                if (!editor.LoadFromFiles(tacticalImage, tacticalXml)) throw new InvalidDataException("战术国旗图集加载失败。");
                files.AddRange(editor.PrepareImagesAndArrange(
                    [($"flag_{countryId}.png", flag), ($"f_{countryId:D2}.png", small)], tacticalImage, tacticalXml));
                result.FlagPath = $"tacticalmap:flag_{countryId}.png";
                result.SmallFlagPath = $"tacticalmap:f_{countryId:D2}.png";
            }
            else
            {
                if (tacticalImage != null || tacticalXml != null) throw new InvalidDataException("战术国旗图集缺少图片或 XML。");
                result.FlagPath = Path.Combine(outputDir, $"flag_{countryId}.png");
                result.SmallFlagPath = Path.Combine(outputDir, $"f_{countryId:D2}.png");
                files.Add((result.FlagPath, AtlasCompositeRenderer.Encode(flag, result.FlagPath)));
                files.Add((result.SmallFlagPath, AtlasCompositeRenderer.Encode(small, result.SmallFlagPath)));
            }
            var (hdImage, hdXml) = FindHdAtlasFiles();
            if (hdImage != null && hdXml != null)
            {
                var editor = new TacticalMapEditor();
                if (!editor.LoadFromFiles(hdImage, hdXml)) throw new InvalidDataException("HD 国旗图集加载失败。");
                files.AddRange(editor.PrepareImagesAndArrange([($"flag_{countryId}.png", flag)], hdImage, hdXml));
                result.HdFlagPath = $"hdatlas:flag_{countryId}.png";
            }
            else
            {
                if (hdImage != null || hdXml != null) throw new InvalidDataException("HD 国旗图集缺少图片或 XML。");
                result.HdFlagPath = Path.Combine(outputDir, $"flag_hd_{countryId}.png");
                files.Add((result.HdFlagPath, AtlasCompositeRenderer.Encode(flag, result.HdFlagPath)));
            }
            WC4MapEditor.Core.Parsers.AtomicFile.WriteAll(files.ToArray());
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.FlagPath = result.SmallFlagPath = result.HdFlagPath = null;
            result.ErrorMessage = ex.Message;
        }
        return result;
    }

    private static (string? imagePath, string? xmlPath) FindTacticalMapFiles()
        => FindAtlasFiles("tacticalmap");

    private static (string? imagePath, string? xmlPath) FindHdAtlasFiles()
        => FindAtlasFiles("image/image_flags_hd");

    private static (string? imagePath, string? xmlPath) FindAtlasFiles(string stem)
    {
        string root = AssetManager.Default.GetActiveAssetsRoot();
        string xml = Path.Combine(root, stem + ".xml");
        string? image = ProjectTextureLoader.FindPath(stem);
        if (ProjectFile.Exists(xml)) image = HdAtlasParser.Get(stem).ImagePath ?? image;
        return (ProjectFile.Exists(image) ? image : null, ProjectFile.Exists(xml) ? xml : null);
    }

    // ========== 涂鸦引擎 ==========

    public void PaintBrush(SKBitmap canvas, int x, int y, int radius, SKColor color)
    {
        try
        {
            using var surface = new SKCanvas(canvas);
            using var paint = new SKPaint
            {
                Color = color,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            surface.DrawCircle(x, y, radius, paint);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FlagEditorService] 绘制笔触失败: {ex.Message}");
        }
    }

    public SKColor GetPixelColor(SKBitmap canvas, int x, int y)
    {
        try
        {
            if (x < 0 || x >= canvas.Width || y < 0 || y >= canvas.Height)
                return SKColors.Transparent;

            return canvas.GetPixel(x, y);
        }
        catch
        {
            return SKColors.Transparent;
        }
    }
}
