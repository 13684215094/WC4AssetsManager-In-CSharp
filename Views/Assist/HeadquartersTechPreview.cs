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
    private sealed record LayoutChoice(string Name, HeadquartersTechGrid Grid) { public override string ToString() => Name; }
    private readonly ComboBox _layouts = new() { MinWidth = 110, Margin = new Thickness(6) };
    private readonly ComboBox _categories = new() { MinWidth = 100, Margin = new Thickness(6) };
    private readonly Image _image = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _details = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly TextBlock _issues = new() { Foreground = Brushes.LightSalmon, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly Slider _zoom = new() { Minimum = 0.25, Maximum = 1.5, Value = 0.5, Width = 100, Margin = new Thickness(6) };
    private readonly ScrollViewer _scroll;
    private int? _selected;
    private HeadquartersTechTree? _tree;
    private HeadquartersTechGrid? _grid;
    private bool _refreshing;
    private bool _draftValid;
    private long _layoutRevision = -1;
    private readonly List<string> _resourceIssues = [];
    private SkiaSharp.SKTypeface? _typeface;
    private long _fontRevision = -1;
    private bool _autoFit = true;
    private readonly CheckBox _upgrades = new() { Content = "升级线", IsChecked = true, Foreground = Brushes.White, Margin = new Thickness(6) };
    private readonly CheckBox _dependencies = new() { Content = "依赖线", IsChecked = true, Foreground = Brushes.White, Margin = new Thickness(6) };
    public event Action<int>? RowSelected;
    public event Action? RefreshRequested;

    public HeadquartersTechPreview()
    {
        var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(23, 29, 32)) };
        var toolbar = new WrapPanel();
        toolbar.Children.Add(new TextBlock { Text = "总部科技 UI 预览", Foreground = Brushes.White, Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(_layouts); toolbar.Children.Add(_categories); toolbar.Children.Add(_zoom);
        void Action(string label, System.Action action)
        {
            var button = new Button { Content = label, Margin = new Thickness(4), Padding = new Thickness(6) };
            button.Click += (_, _) => action(); toolbar.Children.Add(button);
        }
        Action("适应区域", () => { _autoFit = true; ApplyZoom(); });
        Action("100%", () => { _autoFit = false; _zoom.Value = 1; ApplyZoom(); });
        Action("定位选中", CenterSelected);
        Action("更新预览", () => { AssetManager.Default.InvalidateData(); RefreshRequested?.Invoke(); });
        toolbar.Children.Add(_upgrades); toolbar.Children.Add(_dependencies);
        _upgrades.Click += (_, _) => Render(); _dependencies.Click += (_, _) => Render();
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
        _layouts.SelectionChanged += (_, _) => { if (!_refreshing) RefreshRequested?.Invoke(); };
        _categories.SelectionChanged += (_, _) => { if (!_refreshing) Render(); };
        _zoom.Minimum = 0.05; _zoom.Maximum = 2;
        _zoom.ValueChanged += (_, _) => { _autoFit = false; ApplyZoom(); };
        _scroll.SizeChanged += (_, _) => { if (_autoFit) ApplyZoom(); };
        _image.MouseLeftButtonDown += (_, e) =>
        {
            if (!_draftValid || _tree == null || _grid == null || _categories.SelectedItem is not int category) return;
            var point = e.GetPosition(_image);
            var node = HeadquartersTechPreviewRenderer.GetCardRegions(_tree, _grid, category)
                .LastOrDefault(region => region.Bounds.Contains((float)point.X, (float)point.Y))?.Node;
            if (node != null) RowSelected?.Invoke(node.RowIndex);
        };
        Loaded += (_, _) => RefreshRequested?.Invoke();
        Unloaded += (_, _) => { _typeface?.Dispose(); _typeface = null; _fontRevision = -1; };
    }

    private void LoadLayouts()
    {
        var manager = AssetManager.Default;
        if (_layoutRevision == manager.Revision) return;
        _refreshing = true;
        try
        {
            string? selected = (_layouts.SelectedItem as LayoutChoice)?.Name;
            _layouts.Items.Clear(); _resourceIssues.Clear();
            var catalog = GameUiPreviewResources.LoadLayouts(manager, "form_technique");
            _resourceIssues.AddRange(catalog.Issues);
            foreach (var (name, document) in catalog.Layouts)
            {
                try { _layouts.Items.Add(new LayoutChoice(name, HeadquartersTechGrid.FromLayout(document))); }
                catch (InvalidDataException ex) { _resourceIssues.Add($"布局 {name} 不可用于科技预览：{ex.Message}"); }
            }
            _layouts.SelectedItem = _layouts.Items.Cast<LayoutChoice>().FirstOrDefault(choice => choice.Name.Equals(selected, StringComparison.OrdinalIgnoreCase))
                ?? _layouts.Items.Cast<LayoutChoice>().FirstOrDefault(choice => choice.Name.Equals("layout_x.xml", StringComparison.OrdinalIgnoreCase))
                ?? _layouts.Items.Cast<LayoutChoice>().FirstOrDefault();
            _layoutRevision = manager.Revision;
        }
        finally { _refreshing = false; }
    }

    public void Update(JsonArray rows, int? selected)
    {
        bool selectionChanged = _selected != selected;
        int? previousCategory = _tree?.Nodes.FirstOrDefault(node => node.RowIndex == _selected)?.Category;
        _draftValid = false; _selected = selected;
        _tree = null; _grid = null; _details.Text = "";
        try
        {
            LoadLayouts();
            if (_layouts.SelectedItem is not LayoutChoice choice)
                throw new InvalidDataException("项目未提供有效总部科技布局，无法确定游戏网格。\n" + string.Join("\n", _resourceIssues));
            _grid = choice.Grid;
            _tree = HeadquartersTechTree.Build(rows, _grid);
            int? category = _categories.SelectedItem as int?;
            var selectedNode = _tree.Nodes.FirstOrDefault(node => node.RowIndex == selected);
            if (selectedNode != null && (selectionChanged || selectedNode.Category != previousCategory)) category = selectedNode.Category;
            _refreshing = true;
            _categories.Items.Clear();
            foreach (int value in _tree.Nodes.Select(node => node.Category).Distinct().Order()) _categories.Items.Add(value);
            _categories.SelectedItem = category;
            if (_categories.SelectedIndex < 0 && _categories.Items.Count > 0) _categories.SelectedIndex = 0;
            _refreshing = false;
            _issues.Text = string.Join("\n", _resourceIssues.Concat(_tree.Issues).Take(40));
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
            _draftValid = true;
            Render();
        }
        catch (Exception ex) { ClearFailedPreview(ex.Message); }
        finally { _refreshing = false; }
    }

    public void ShowInvalidInput(string message)
    {
        _draftValid = false;
        _issues.Text = $"当前输入无效，保留上一次有效预览：{message}";
    }

    private SkiaSharp.SKBitmap? Draw()
    {
        if (!_draftValid || _tree == null || _grid == null || _categories.SelectedItem is not int category) return null;
        var table = AssetManager.Default.GetStringTable("cn");
        return HeadquartersTechPreviewRenderer.Render(_tree, _grid, category,
            _tree.Nodes.FirstOrDefault(node => node.RowIndex == _selected)?.Id,
            reference =>
            {
                var image = LayoutImageProvider.Resolve(reference);
                return image?.Ready == true ? new(image.Page!, image.SrcRect) : null;
            }, key => table.TryGetValue(key, out string? text) ? text : null, GetTypeface(),
            _upgrades.IsChecked == true, _dependencies.IsChecked == true);
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
        if (!_draftValid) return;
        if (_layoutRevision != AssetManager.Default.Revision) { RefreshRequested?.Invoke(); return; }
        try { using var bitmap = Draw(); _image.Source = bitmap == null ? null : SkiaBitmapSource.Create(bitmap); ApplyZoom(); }
        catch (Exception ex) { ClearFailedPreview(ex.Message); }
    }

    private void ClearFailedPreview(string message)
    {
        _draftValid = false; _tree = null; _grid = null; _image.Source = null; _details.Text = "";
        _issues.Text = $"预览失败：{message}";
    }

    private void ApplyZoom()
    {
        double zoom = _autoFit && _grid != null && _scroll.ViewportWidth > 0 && _scroll.ViewportHeight > 0
            ? Math.Clamp(Math.Min((_scroll.ViewportWidth - 12) / _grid.Width, (_scroll.ViewportHeight - 12) / _grid.Height), 0.05, 2)
            : _zoom.Value;
        _image.LayoutTransform = new ScaleTransform(zoom, zoom);
    }

    private void CenterSelected()
    {
        if (!_draftValid || _grid == null || _tree == null || _categories.SelectedItem is not int category) return;
        var region = HeadquartersTechPreviewRenderer.GetCardRegions(_tree, _grid, category).FirstOrDefault(item => item.Node.RowIndex == _selected);
        if (region == null) return;
        double zoom = (_image.LayoutTransform as ScaleTransform)?.ScaleX ?? 1;
        _scroll.ScrollToHorizontalOffset(Math.Max(0, region.Bounds.MidX * zoom - _scroll.ViewportWidth / 2));
        _scroll.ScrollToVerticalOffset(Math.Max(0, region.Bounds.MidY * zoom - _scroll.ViewportHeight / 2));
    }

    private void Export()
    {
        RefreshRequested?.Invoke();
        if (!_draftValid || _tree == null || _grid == null) { _issues.Text += "\n请先修正输入或预览资源，再导出当前预览。"; return; }
        var picker = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = "总部科技预览.png" };
        if (picker.ShowDialog(System.Windows.Window.GetWindow(this)) != true) return;
        try
        {
            string path = GameUiPreviewExport.ValidateDestination(picker.FileName, AssetManager.Default.AssetsRoot);
            using var bitmap = Draw();
            if (bitmap == null) return;
            using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            WC4MapEditor.Core.Parsers.AtomicFile.Write(path, encoded.ToArray());
            _details.Text += $"\n已导出 {path}";
        }
        catch (Exception ex) { _issues.Text = $"导出失败：{ex.Message}"; }
    }
}
