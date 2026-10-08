using System.Text.Json.Nodes;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.Views.Assist;

internal sealed class HeadquartersTechPreview : UserControl
{
    private sealed record LayoutChoice(string Name, string Text) { public override string ToString() => Name; }
    private readonly ComboBox _layouts = new() { MinWidth = 110, Margin = new Thickness(6) };
    private readonly ComboBox _categories = new() { MinWidth = 100, Margin = new Thickness(6) };
    private readonly Image _image = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _details = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly TextBlock _issues = new() { Foreground = Brushes.LightSalmon, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly Slider _zoom = new() { Minimum = 0.25, Maximum = 1.5, Value = 0.5, Width = 100, Margin = new Thickness(6) };
    private readonly ScrollViewer _scroll;
    private JsonArray _rows = [];
    private int? _selected;
    private HeadquartersTechTree? _tree;
    private HeadquartersTechGrid? _grid;
    private bool _refreshing;
    private bool _draftValid = true;
    private SkiaSharp.SKTypeface? _typeface;
    private long _fontRevision = -1;
    public event Action<int>? RowSelected;
    public event Action? RefreshRequested;

    public HeadquartersTechPreview()
    {
        var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(23, 29, 32)) };
        var toolbar = new WrapPanel();
        toolbar.Children.Add(new TextBlock { Text = "总部科技 UI 预览", Foreground = Brushes.White, Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(_layouts); toolbar.Children.Add(_categories); toolbar.Children.Add(_zoom);
        var export = new Button { Content = "导出预览 PNG", Margin = new Thickness(6), Padding = new Thickness(6) };
        export.Click += (_, _) => Export(); toolbar.Children.Add(export);
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var note = new TextBlock { Text = "基于当前未保存数据、项目图标与布局网格生成。金色为升级、青色为依赖；点击节点编辑。动画、解锁状态和原生连线不在预览范围。",
            Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        DockPanel.SetDock(_details, Dock.Top); root.Children.Add(_details);
        var diagnostics = new ScrollViewer { Content = _issues, MaxHeight = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(diagnostics, Dock.Bottom); root.Children.Add(diagnostics);
        _scroll = new ScrollViewer { Content = _image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(_scroll); Content = root;
        foreach (var entry in AssetManager.Default.ListByExtension("xml").Where(entry =>
            !entry.RelativePath.Contains('/') && entry.FileNameWithoutExtension.StartsWith("layout", StringComparison.OrdinalIgnoreCase)))
        {
            string text = AssetManager.Default.ReadText(entry);
            try { HeadquartersTechGrid.FromLayout(text, entry.RelativePath); _layouts.Items.Add(new LayoutChoice(entry.RelativePath, text)); }
            catch (InvalidDataException) { }
        }
        _layouts.SelectedItem = _layouts.Items.Cast<LayoutChoice>().FirstOrDefault(choice => choice.Name == "layout_x.xml")
            ?? _layouts.Items.Cast<LayoutChoice>().FirstOrDefault();
        _layouts.SelectionChanged += (_, _) => RefreshRequested?.Invoke();
        _categories.SelectionChanged += (_, _) => { if (!_refreshing) Render(); };
        _zoom.ValueChanged += (_, _) => _image.LayoutTransform = new ScaleTransform(_zoom.Value, _zoom.Value);
        _image.LayoutTransform = new ScaleTransform(_zoom.Value, _zoom.Value);
        _image.MouseLeftButtonDown += (_, e) =>
        {
            if (_tree == null || _grid == null || _categories.SelectedItem is not int category) return;
            var point = e.GetPosition(_image);
            int column = (int)(point.X / _grid.CellWidth) + 1, row = (int)(point.Y / _grid.CellHeight) + 1;
            var members = _tree.Nodes.Where(node => node.Category == category && node.GridRow == row && node.GridColumn == column).ToArray();
            double offset = point.X - (column - 1) * _grid.CellWidth - (_grid.CellWidth - _grid.CardWidth) / 2;
            int slot = members.Length == 0 ? 0 : Math.Clamp((int)(offset / _grid.CardWidth * members.Length), 0, members.Length - 1);
            var node = members.ElementAtOrDefault(slot);
            if (node != null) RowSelected?.Invoke(node.RowIndex);
        };
        Unloaded += (_, _) => { _typeface?.Dispose(); _typeface = null; _fontRevision = -1; };
    }

    public void Update(JsonArray rows, int? selected)
    {
        _draftValid = true;
        _rows = rows; _selected = selected;
        if (_layouts.SelectedItem is not LayoutChoice choice)
        {
            _issues.Text = "项目未提供有效总部科技布局，无法确定游戏网格。"; _image.Source = null; return;
        }
        try
        {
            _grid = HeadquartersTechGrid.FromLayout(choice.Text, choice.Name);
            _tree = HeadquartersTechTree.Build(rows, _grid);
            int? category = _categories.SelectedItem as int?;
            var selectedNode = _tree.Nodes.FirstOrDefault(node => node.RowIndex == selected);
            if (selectedNode != null) category = selectedNode.Category;
            _refreshing = true;
            _categories.Items.Clear();
            foreach (int value in _tree.Nodes.Select(node => node.Category).Distinct().Order()) _categories.Items.Add(value);
            _categories.SelectedItem = category;
            if (_categories.SelectedIndex < 0 && _categories.Items.Count > 0) _categories.SelectedIndex = 0;
            _refreshing = false;
            _issues.Text = string.Join("\n", _tree.Issues.Take(40));
            var missingIcons = _tree.Nodes.Select(node => node.Type).Distinct().Where(type =>
                !new[] { $"tech_{type}.png", $"tech_{type}_1.png", $"tech_{type}_2.png" }
                    .Any(reference => LayoutImageProvider.Resolve(reference)?.Ready == true)).ToArray();
            if (missingIcons.Length > 0) _issues.Text += $"\n缺少科技图标 Type：{string.Join(", ", missingIcons)}";
            _details.Text = $"{_grid.Columns} 列 × {_grid.Rows} 行 · Position=[行,列] · 布局 {_grid.LayoutName}";
            if (selectedNode != null)
            {
                var record = rows[selectedNode.RowIndex]!.AsObject();
                _details.Text += $"\n选中 #{selectedNode.Id} Lv{selectedNode.Level}：金币 {record["CostGold"]} / 工业 {record["CostIndustry"]} / 能源 {record["CostEnergy"]} / 科技 {record["CostTech"]}";
                if (record["TechDesc"] is JsonArray descriptions && record["TechValue"] is JsonArray values)
                {
                    var strings = AssetManager.Default.GetStringTable("cn");
                    var effects = descriptions.Select((node, index) =>
                    {
                        string key = $"tech_desc_{node}";
                        string text = strings.TryGetValue(key, out string? value) ? value : key;
                        return index < values.Count ? text.Replace("%d", values[index]?.ToJsonString() ?? "null").Replace("%%", "%") : text;
                    });
                    _details.Text += "\n" + string.Join("；", effects);
                }
            }
            Render();
        }
        catch (Exception ex) { _issues.Text = ex.Message; _image.Source = null; }
        finally { _refreshing = false; }
    }

    public void ShowInvalidInput(string message)
    {
        _draftValid = false;
        _issues.Text = $"当前输入无效，保留上一次有效预览：{message}";
    }

    private SkiaSharp.SKBitmap? Draw()
    {
        if (_tree == null || _grid == null || _categories.SelectedItem is not int category) return null;
        var table = AssetManager.Default.GetStringTable("cn");
        return HeadquartersTechPreviewRenderer.Render(_tree, _grid, category,
            _tree.Nodes.FirstOrDefault(node => node.RowIndex == _selected)?.Id,
            reference =>
            {
                var image = LayoutImageProvider.Resolve(reference);
                return image?.Ready == true ? new(image.Page!, image.SrcRect) : null;
            }, key => table.TryGetValue(key, out string? text) ? text : null, GetTypeface());
    }

    private SkiaSharp.SKTypeface GetTypeface()
    {
        if (_fontRevision != AssetManager.Default.Revision)
        {
            _typeface?.Dispose(); _typeface = null;
            _fontRevision = AssetManager.Default.Revision;
            var font = AssetManager.Default.Find("font/NotoSans_cn.otf");
            if (font != null) _typeface = SkiaSharp.SKTypeface.FromFile(ProjectFiles.ResolveRead(font.FullPath));
        }
        return _typeface ?? LayoutFontProvider.Typeface;
    }

    private void Render()
    {
        try { using var bitmap = Draw(); _image.Source = bitmap == null ? null : SkiaBitmapSource.Create(bitmap); }
        catch (Exception ex) { _image.Source = null; _issues.Text = ex.Message; }
    }

    private void Export()
    {
        RefreshRequested?.Invoke();
        if (!_draftValid) { _issues.Text = "请先修正无效输入，再导出当前预览。"; return; }
        var picker = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = "总部科技预览.png" };
        if (picker.ShowDialog(System.Windows.Window.GetWindow(this)) != true) return;
        try
        {
            var project = ProjectFiles.FindProject(AssetManager.Default.AssetsRoot);
            if (project?.IsSourcePath(picker.FileName) == true ||
                project != null && (IOPath.GetFullPath(picker.FileName).StartsWith(
                    project.OutputRoot + IOPath.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                throw new InvalidOperationException("预览图片请导出到游戏来源和修改输出以外的目录，避免替换游戏资源。");
            if (!IOPath.GetExtension(picker.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("预览图需要使用 .png 扩展名。");
            using var bitmap = Draw();
            if (bitmap == null) return;
            using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            System.IO.File.WriteAllBytes(picker.FileName, encoded.ToArray());
        }
        catch (Exception ex) { _issues.Text = $"导出失败：{ex.Message}"; }
    }
}
