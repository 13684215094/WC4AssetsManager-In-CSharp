using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.Core.Parsers.Layout;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.Views.Assist;

internal sealed class ProjectDetailPreviewControl : UserControl, IDisposable
{
    private readonly ComboBox _layout = new() { MinWidth = 105, Margin = new Thickness(4) };
    private readonly ComboBox _locale = new() { ItemsSource = new[] { "cn", "tw", "en" }, SelectedIndex = 0, Margin = new Thickness(4) };
    private readonly Image _image = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _details = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly TextBlock _issues = new() { Foreground = Brushes.LightSalmon, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly ProjectUiImageSource _images = new();
    private readonly ProjectUiFontSource _fonts = new();
    private readonly Dictionary<string, LayoutDocument> _layouts = new();
    private readonly List<string> _resourceIssues = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly ScrollViewer _scroll;
    private readonly bool _army;
    private ArmySettingData? _unit;
    private CountrySettingData? _country;
    private ConquerCountrySettingData? _context;
    private string _unitName = "";
    private IReadOnlyList<ArmyFeatureData> _features = [];
    private string? _fontXml;
    private long _revision = -1;
    private bool _valid = true, _refreshing, _disposed, _fit = true;
    private int _width;
    private IReadOnlyList<GameUiHitRegion> _regions = [];
    public event Action<int>? FeatureSelected;
    public event Action? ValidateRequested;

    public ProjectDetailPreviewControl(bool army)
    {
        _army = army;
        var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(20, 24, 27)) };
        var toolbar = new WrapPanel(); toolbar.Children.Add(_layout); toolbar.Children.Add(_locale);
        void Action(string label, System.Action action)
        {
            var button = new Button { Content = label, Padding = new Thickness(6), Margin = new Thickness(4) };
            button.Click += (_, _) => action(); toolbar.Children.Add(button);
        }
        Action("适应宽度", () => { _fit = true; Zoom(); });
        Action("100%", () => { _fit = false; Zoom(); });
        Action("更新预览", () => { ValidateRequested?.Invoke(); AssetManager.Default.InvalidateData(); Render(); });
        Action("导出 PNG", Export);
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(_details, Dock.Top); root.Children.Add(_details);
        var diagnostics = new ScrollViewer { Content = _issues, MaxHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(diagnostics, Dock.Bottom); root.Children.Add(diagnostics);
        _scroll = new ScrollViewer { Content = _image, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _scroll.SizeChanged += (_, _) => Zoom(); root.Children.Add(_scroll); Content = root;
        _layout.SelectionChanged += (_, _) => Schedule(); _locale.SelectionChanged += (_, _) => Schedule();
        _timer.Tick += (_, _) => { _timer.Stop(); Render(); };
        _image.MouseLeftButtonDown += (_, args) =>
        {
            if (!_valid) return;
            var point = args.GetPosition(_image);
            var region = _regions.LastOrDefault(region => region.Kind == GameUiHitKind.ArmyFeature && region.Bounds.Contains((float)point.X, (float)point.Y));
            if (region != null) FeatureSelected?.Invoke(region.SkillId);
        };
        Loaded += (_, _) => Schedule();
        Unloaded += (_, _) => { _timer.Stop(); _images.Dispose(); _fonts.Dispose(); };
        Clear();
    }

    public void UpdateArmy(ArmySettingData unit, string name)
    { _unit = unit; _unitName = name; _valid = true; Schedule(); }

    public void UpdateCountry(CountrySettingData country, ConquerCountrySettingData context)
    { _country = country; _context = context; _valid = true; Schedule(); }

    public void Clear()
    {
        _unit = null; _country = null; _context = null; _image.Source = null; _regions = []; _timer.Stop();
        _details.Text = _army ? "选择兵种以查看基础配置 UI。" : "选择国家，再选择一个征服实例以查看 UI。"; _issues.Text = "";
    }

    public void ShowInvalidInput(string message)
    {
        _valid = false; _timer.Stop(); _regions = [];
        _issues.Text = $"当前输入无效，展示上次有效预览：{message}";
    }

    private void Schedule() { if (!_disposed && !_refreshing && IsLoaded) { _timer.Stop(); _timer.Start(); } }

    private void LoadResources()
    {
        var manager = AssetManager.Default;
        if (_revision == manager.Revision) return;
        _refreshing = true;
        try
        {
            _resourceIssues.Clear();
            string? previous = _layout.SelectedItem as string;
            _layouts.Clear(); _layout.Items.Clear();
            string form = _army ? "form_unitinfo" : "form_conquestcountry";
            var catalog = GameUiPreviewResources.LoadLayouts(manager, form);
            _resourceIssues.AddRange(catalog.Issues);
            foreach (var (name, document) in catalog.Layouts)
            {
                _layouts[name] = document; _layout.Items.Add(name);
            }
            _layout.SelectedItem = previous != null && _layouts.ContainsKey(previous) ? previous :
                _layouts.ContainsKey("layout_x.xml") ? "layout_x.xml" : _layouts.Keys.FirstOrDefault();
            var font = manager.Find("font_resource.xml"); _fontXml = font == null ? null : manager.ReadText(font);
            _features = _army ? GameUiPreviewResources.ReadRows<ArmyFeatureData>(manager, "json/ArmyFeatureSettings.json", _resourceIssues) : [];
            _revision = manager.Revision;
        }
        finally { _refreshing = false; }
    }

    private GameUiRenderResult? Draw()
    {
        if (_army ? _unit == null : _country == null || _context == null) return null;
        LoadResources();
        if (_layout.SelectedItem is not string key || !_layouts.TryGetValue(key, out var layout)) throw new InvalidDataException("当前项目缺少对应的详情布局。");
        if (_fontXml == null) throw new InvalidDataException("项目缺少 font_resource.xml。");
        string locale = _locale.SelectedItem as string ?? "cn";
        string? Lookup(string name) => AssetManager.Default.GetStringTable(locale).GetValueOrDefault(name);
        var fonts = GameUiFontCatalog.Parse(_fontXml, locale);
        _images.BeginFrame();
        var frame = _army ? ProjectDetailPreview.Army(layout, _unit!, _unitName, _features, reference => _images.Resolve(reference, false) != null)
            : ProjectDetailPreview.Country(layout, _country!, _context!, Lookup($"country_{_country!.Id}") ?? _country.Name);
        var result = GameUiLayoutRenderer.Render(frame, fonts, reference => _images.Resolve(reference), Lookup, _fonts.Get);
        _details.Text = frame.Details + $"\n{key} · 字体 PHONE/HD · {locale}";
        _issues.Text = string.Join("\n", _resourceIssues.Concat(result.Issues).Concat(_images.Issues).Concat(_fonts.MissingFonts).Distinct().Take(40));
        return result;
    }

    private void Render()
    {
        if (_disposed || !_valid) return;
        try
        {
            using var result = Draw(); _image.Source = result == null ? null : SkiaBitmapSource.Create(result.Bitmap);
            _regions = result?.HitRegions ?? []; _width = result?.Bitmap.Width ?? 0; Zoom();
        }
        catch (Exception ex) { _image.Source = null; _regions = []; _issues.Text = $"预览失败：{ex.Message}"; }
    }

    private void Zoom()
    {
        double scale = _fit && _width > 0 && _scroll.ViewportWidth > 0 ? Math.Clamp((_scroll.ViewportWidth - 12) / _width, 0.1, 1.5) : 1;
        _image.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void Export()
    {
        ValidateRequested?.Invoke(); _timer.Stop();
        if (!_valid) { _issues.Text = "请先修正无效输入，再导出当前预览。"; return; }
        var picker = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = _army ? $"Army-{_unit?.Id}.png" : $"Country-{_context?.Id}.png" };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            string path = GameUiPreviewExport.ValidateDestination(picker.FileName, AssetManager.Default.AssetsRoot);
            using var result = Draw(); if (result == null) return;
            using var encoded = result.Bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            WC4MapEditor.Core.Parsers.AtomicFile.Write(path, encoded.ToArray()); _details.Text += $"\n已导出 {path}";
        }
        catch (Exception ex) { _issues.Text = $"导出失败：{ex.Message}"; }
    }

    public void Dispose() { _disposed = true; _timer.Stop(); _image.Source = null; _images.Dispose(); _fonts.Dispose(); }
}
