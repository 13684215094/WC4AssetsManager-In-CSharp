using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Path = System.IO.Path;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.Layout;
using WC4MapEditor.Core.Parsers.Skill;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.Views.Assist;

internal sealed class GameUiPreviewControl : UserControl, IDisposable
{
    private sealed record Choice<T>(string Label, T Value) { public override string ToString() => Label; }
    private readonly ComboBox _mode = new() { MinWidth = 100, Margin = new Thickness(4) };
    private readonly ComboBox _layout = new() { MinWidth = 105, Margin = new Thickness(4) };
    private readonly ComboBox _locale = new() { MinWidth = 85, Margin = new Thickness(4) };
    private readonly ComboBox _profile = new() { MinWidth = 95, Margin = new Thickness(4) };
    private readonly ComboBox _promotion = new() { MinWidth = 130, Margin = new Thickness(4) };
    private readonly ComboBox _skill = new() { MinWidth = 130, Margin = new Thickness(4) };
    private readonly Slider _zoom = new() { Minimum = 0.25, Maximum = 1.5, Value = 0.6, Width = 85, Margin = new Thickness(4) };
    private readonly Image _image = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _details = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly TextBlock _issues = new() { Foreground = Brushes.LightSalmon, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly TextBlock _selection = new() { Foreground = Brushes.LightGoldenrodYellow, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly Button _editSkill = new() { Content = "编辑所选技能", Margin = new Thickness(4), Padding = new Thickness(6), IsEnabled = false };
    private readonly Button _editPromotion = new() { Content = "编辑进修配置", Margin = new Thickness(4), Padding = new Thickness(6), IsEnabled = false };
    private readonly CheckBox _fit = new() { Content = "适应宽度", IsChecked = true, Foreground = Brushes.White, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _timer;
    private readonly ProjectUiImageSource _images = new();
    private readonly ProjectUiFontSource _fonts = new();
    private readonly ScrollViewer _scroll;
    private readonly bool _skillOnly;
    private GeneralSettingData? _general;
    private int? _skillId;
    private Func<string, PortraitPosEntry?> _portraitLookup = _ => null;
    private IReadOnlyList<GeneralPromotionData> _promotions = [];
    private IReadOnlyList<GeneralMedalPreviewData> _medals = [];
    private HashSet<int> _titles = [];
    private Dictionary<string, LayoutDocument> _layouts = new();
    private string? _fontXml;
    private long _revision = -1;
    private bool _refreshing;
    private bool _draftValid = true;
    private bool _disposed;
    private int _pixelWidth;
    private IReadOnlyList<GameUiHitRegion> _hitRegions = [];
    private readonly List<string> _resourceIssues = [];
    public event Action<int>? EditSkillRequested;
    public event Action<int>? SkillSelected;
    public event Action<int>? EditPromotionRequested;
    public event Action<string, string?>? EditLayoutRequested;
    public event Action? ValidateRequested;

    public GameUiPreviewControl(bool skillOnly = false)
    {
        _skillOnly = skillOnly;
        var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(20, 24, 27)) };
        var toolbar = new WrapPanel { Margin = new Thickness(2) };
        toolbar.Children.Add(_mode); toolbar.Children.Add(_layout); toolbar.Children.Add(_locale); toolbar.Children.Add(_profile);
        toolbar.Children.Add(_promotion); toolbar.Children.Add(_skill); toolbar.Children.Add(_fit); toolbar.Children.Add(_zoom);
        var refresh = new Button { Content = "更新预览", Margin = new Thickness(4), Padding = new Thickness(6) };
        refresh.Click += (_, _) => { ValidateRequested?.Invoke(); AssetManager.Default.InvalidateData(); Render(); };
        toolbar.Children.Add(refresh);
        var export = new Button { Content = "导出 PNG", Margin = new Thickness(4), Padding = new Thickness(6) };
        export.Click += (_, _) => Export(); toolbar.Children.Add(export);
        if (!skillOnly) toolbar.Children.Add(_editSkill);
        _editSkill.Click += (_, _) => { if (_skillId is int id) EditSkillRequested?.Invoke(id); };
        if (!skillOnly) toolbar.Children.Add(_editPromotion);
        _editPromotion.Click += (_, _) => { if ((_promotion.SelectedItem as Choice<int>)?.Value is int id) EditPromotionRequested?.Invoke(id); };
        var editLayout = new Button { Content = "编辑页面布局", Margin = new Thickness(4), Padding = new Thickness(6) };
        editLayout.Click += (_, _) => EditLayoutRequested?.Invoke(Mode switch { GameUiPreviewMode.General => "form_generalinfo", GameUiPreviewMode.Promotion => "form_generalpromotion", _ => "form_upgradeskill" }, _layout.SelectedItem as string);
        toolbar.Children.Add(editLayout);
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var note = new TextBlock { Text = "当前草稿的静态 UI 预览：项目布局、图片和字体。详情中点击技能可选中；进修显示目标上限。存档装备、动画和原生游戏逻辑仍需实机核对。",
            Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6), FontSize = 11 };
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        DockPanel.SetDock(_details, Dock.Top); root.Children.Add(_details);
        DockPanel.SetDock(_selection, Dock.Top); root.Children.Add(_selection);
        var diagnostics = new ScrollViewer { Content = _issues, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(diagnostics, Dock.Bottom); root.Children.Add(diagnostics);
        _scroll = new ScrollViewer { Content = _image, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(_scroll); Content = root;
        _mode.Items.Add(new Choice<GameUiPreviewMode>("将领详情", GameUiPreviewMode.General));
        _mode.Items.Add(new Choice<GameUiPreviewMode>("进修阶段", GameUiPreviewMode.Promotion));
        _mode.Items.Add(new Choice<GameUiPreviewMode>("技能升级", GameUiPreviewMode.SkillUpgrade));
        _mode.SelectedIndex = skillOnly ? 2 : 0; _mode.IsEnabled = !skillOnly;
        _locale.Items.Add(new Choice<string>("简体中文", "cn")); _locale.Items.Add(new Choice<string>("繁体中文", "tw")); _locale.Items.Add(new Choice<string>("English", "en"));
        _locale.SelectedIndex = skillOnly ? 1 : 0;
        foreach (string profile in new[] { "PHONE/HD", "PAD/HD", "PAD/SD" }) _profile.Items.Add(profile);
        _profile.SelectedIndex = 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _timer.Tick += (_, _) => { _timer.Stop(); Render(); };
        foreach (var combo in new[] { _mode, _layout, _locale, _profile }) combo.SelectionChanged += (_, _) => { if (!_refreshing) Schedule(); };
        _promotion.SelectionChanged += (_, _) => { if (!_refreshing) { UpdateSkillChoices(); Schedule(); } };
        _skill.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            _skillId = (_skill.SelectedItem as Choice<int>)?.Value;
            UpdateSelection(); Schedule();
        };
        _image.MouseLeftButtonDown += (_, args) =>
        {
            var point = args.GetPosition(_image);
            var region = _hitRegions.LastOrDefault(item => item.Bounds.Contains((float)point.X, (float)point.Y));
            if (region == null) return;
            if (_skillOnly) { SkillSelected?.Invoke(region.SkillId); return; }
            _skillId = region.SkillId;
            UpdateSkillChoices(); UpdateSelection(); Schedule();
        };
        _zoom.ValueChanged += (_, _) => { if (!_refreshing) { _fit.IsChecked = false; ApplyZoom(); } };
        _fit.Checked += (_, _) => ApplyZoom(); _fit.Unchecked += (_, _) => ApplyZoom();
        _scroll.SizeChanged += (_, _) => ApplyZoom();
        Loaded += (_, _) => Schedule();
        Unloaded += (_, _) => { _timer.Stop(); _images.Dispose(); _fonts.Dispose(); };
    }

    public void UpdateGeneral(GeneralSettingData? general, Func<string, PortraitPosEntry?> portraitLookup)
    {
        bool changed = !ReferenceEquals(_general, general);
        _general = general; _portraitLookup = portraitLookup; _draftValid = true;
        if (changed) _skillId = general?.Skills?.FirstOrDefault();
        Schedule();
    }

    public void UpdateSkill(int? id) { _skillId = id; _draftValid = true; Schedule(); }
    public void ShowInvalidInput(string message)
    {
        _draftValid = false; _timer.Stop(); _hitRegions = []; _editSkill.IsEnabled = false;
        _issues.Text = $"当前输入无效，保留上次有效预览：{message}";
        _editPromotion.IsEnabled = false;
    }

    public void Clear()
    {
        _general = null; _skillId = null; _timer.Stop(); _image.Source = null; _hitRegions = [];
        _details.Text = "请选择记录。"; _selection.Text = ""; _issues.Text = ""; _editSkill.IsEnabled = false; _editPromotion.IsEnabled = false;
    }

    private void Schedule() { if (_disposed) return; _timer.Stop(); if (IsLoaded) _timer.Start(); }
    private void LoadResources()
    {
        var manager = AssetManager.Default;
        if (_revision == manager.Revision) return;
        _resourceIssues.Clear(); _refreshing = true;
        try
        {
            string? selectedLayout = (_layout.SelectedItem as string);
            _layouts.Clear(); _layout.Items.Clear();
            var catalog = GameUiPreviewResources.LoadLayouts(manager, "form_generalinfo", "form_upgradeskill", "form_generalpromotion");
            _resourceIssues.AddRange(catalog.Issues);
            foreach (var (name, document) in catalog.Layouts)
            {
                _layouts[name] = document; _layout.Items.Add(name);
            }
            _layout.SelectedItem = selectedLayout != null && _layouts.ContainsKey(selectedLayout) ? selectedLayout :
                _layouts.ContainsKey("layout_x.xml") ? "layout_x.xml" : _layouts.Keys.FirstOrDefault();
            _promotions = ReadRows<GeneralPromotionData>("GeneralPromotionSettings.json");
            _medals = ReadRows<GeneralMedalPreviewData>("GeneralMedalSettings.json");
            _titles = ReadRows<GeneralTitlePreviewData>("GeneralTitleSettings.json").Select(row => row.GeneralId).ToHashSet();
            var font = manager.Find("font_resource.xml");
            _fontXml = font == null ? null : manager.ReadText(font);
            _revision = manager.Revision;
        }
        finally { _refreshing = false; }
    }

    private List<T> ReadRows<T>(string name)
        => GameUiPreviewResources.ReadRows<T>(AssetManager.Default, "json/" + name, _resourceIssues);

    private GameUiPreviewMode Mode => (_mode.SelectedItem as Choice<GameUiPreviewMode>)?.Value ?? GameUiPreviewMode.General;
    private string Locale => (_locale.SelectedItem as Choice<string>)?.Value ?? "cn";
    private string? Lookup(string key)
    {
        if (Locale == "tw")
        {
            if (key.StartsWith("skill_name_") && int.TryParse(key[11..], out int type)) return SkillSettingParser.Instance.GetSkillName(type) is { Length: > 0 } name ? name : null;
            if (key.StartsWith("skill_info_") && int.TryParse(key[11..], out type)) return SkillSettingParser.Instance.GetSkillDesc(type) is { Length: > 0 } desc ? desc : null;
        }
        return AssetManager.Default.GetStringTable(Locale).GetValueOrDefault(key);
    }

    private void UpdateChoices()
    {
        _refreshing = true;
        try
        {
            int? selected = (_promotion.SelectedItem as Choice<int>)?.Value;
            _promotion.Items.Clear();
            foreach (var item in _promotions.Where(item => item.BaseID == _general?.Id).OrderBy(item => item.Id))
                _promotion.Items.Add(new Choice<int>($"进修 #{item.Id}", item.Id));
            _promotion.SelectedItem = _promotion.Items.Cast<Choice<int>>().FirstOrDefault(item => item.Value == selected);
            if (_promotion.SelectedIndex < 0 && _promotion.Items.Count > 0) _promotion.SelectedIndex = 0;
            _promotion.Visibility = Mode == GameUiPreviewMode.Promotion ? Visibility.Visible : Visibility.Collapsed;
            _editPromotion.Visibility = Mode == GameUiPreviewMode.Promotion ? Visibility.Visible : Visibility.Collapsed;
            _editPromotion.IsEnabled = _promotion.SelectedItem != null && _draftValid;
            _skill.Visibility = _skillOnly ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _refreshing = false; }
        UpdateSkillChoices();
    }

    private void UpdateSkillChoices()
    {
        if (_skillOnly) { UpdateSelection(); return; }
        _refreshing = true;
        try
        {
            _skill.Items.Clear();
            var promotion = Mode == GameUiPreviewMode.Promotion ? _promotions.FirstOrDefault(item => item.Id == (_promotion.SelectedItem as Choice<int>)?.Value) : null;
            var ids = promotion?.Skills ?? _general?.Skills ?? [];
            if (Mode == GameUiPreviewMode.SkillUpgrade && _skillId is int extra && !ids.Contains(extra) &&
                _promotions.Any(item => item.BaseID == _general?.Id && item.Skills.Contains(extra))) ids = ids.Concat([extra]).ToList();
            foreach (int id in ids.Where(id => id != 0).Distinct())
            {
                var skill = SkillSettingParser.Instance.GetById(id);
                string name = skill == null ? "缺失" : Lookup($"skill_name_{skill.Type}") ?? skill.Name;
                _skill.Items.Add(new Choice<int>($"{name} #{id}", id));
            }
            _skill.SelectedItem = _skill.Items.Cast<Choice<int>>().FirstOrDefault(item => item.Value == _skillId);
            if (_skill.SelectedIndex < 0 && _skill.Items.Count > 0) _skill.SelectedIndex = 0;
            _skillId = (_skill.SelectedItem as Choice<int>)?.Value;
        }
        finally { _refreshing = false; }
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var skill = _skillId is int id ? SkillSettingParser.Instance.GetById(id) : null;
        _editSkill.IsEnabled = skill != null && _draftValid;
        _selection.Text = skill == null ? "" : $"选中 {Lookup($"skill_name_{skill.Type}") ?? skill.Name} Lv{skill.Level}：" +
            GameUiLayoutRenderer.PlainText(GameUiPreview.FormatSkillDescription(Lookup($"skill_info_{skill.Type}") ?? "", skill));
    }

    private GameUiRenderResult? Draw()
    {
        if (_general == null && !_skillOnly || _skillOnly && _skillId == null) return null;
        LoadResources(); UpdateChoices();
        if (_layout.SelectedItem is not string key || !_layouts.TryGetValue(key, out var layout)) throw new InvalidDataException("项目没有可用的将领/技能布局。");
        if (_fontXml == null) throw new InvalidDataException("项目缺少 font_resource.xml，无法读取 UI 字号配置。");
        var catalog = GameUiFontCatalog.Parse(_fontXml, Locale, _profile.SelectedItem as string ?? "PHONE/HD");
        _images.BeginFrame();
        var frame = GameUiPreview.Build(new GameUiPreviewInput
        {
            Layout = layout, Mode = Mode, General = _general, SkillId = _skillId,
            PromotionId = (_promotion.SelectedItem as Choice<int>)?.Value,
            Skills = SkillSettingParser.Instance.All, Promotions = _promotions, Medals = _medals, TitledGenerals = _titles,
            PortraitLookup = _portraitLookup, StringLookup = Lookup, ImageExists = reference => _images.Resolve(reference, false) != null
        });
        _details.Text = frame.Details + $"\n{key} · 字体 {catalog.Profile} · {Locale}（技能文本草稿保存在 tw）";
        var result = GameUiLayoutRenderer.Render(frame, catalog, reference => _images.Resolve(reference), Lookup, _fonts.Get);
        _issues.Text = string.Join("\n", _resourceIssues.Concat(result.Issues).Concat(_images.Issues).Concat(_fonts.MissingFonts).Distinct().Take(50));
        if (_issues.Text.Length == 0) _issues.Text = "当前预览未发现缺失图片、断链或溢出。";
        return result;
    }

    private void Render()
    {
        if (_disposed || !_draftValid) return;
        try
        {
            using var result = Draw();
            _image.Source = result == null ? null : SkiaBitmapSource.Create(result.Bitmap);
            _hitRegions = result?.HitRegions ?? []; _pixelWidth = result?.Bitmap.Width ?? 0; ApplyZoom();
        }
        catch (Exception ex) { _image.Source = null; _hitRegions = []; _issues.Text = $"预览失败：{ex.Message}"; }
    }

    private void ApplyZoom()
    {
        double scale = _fit.IsChecked == true && _pixelWidth > 0 && _scroll.ViewportWidth > 0
            ? Math.Clamp((_scroll.ViewportWidth - 12) / _pixelWidth, 0.1, 1.5) : _zoom.Value;
        _image.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void Export()
    {
        ValidateRequested?.Invoke(); _timer.Stop();
        if (!_draftValid) { _issues.Text = "请先修正无效输入，再导出。"; return; }
        var dialog = new SaveFileDialog { Filter = "PNG 图片|*.png", FileName = $"{_general?.EName ?? "Skill"}-{Mode}.png" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            string full = GameUiPreviewExport.ValidateDestination(dialog.FileName, AssetManager.Default.AssetsRoot);
            using var result = Draw();
            if (result == null) return;
            using var encoded = result.Bitmap.Encode(SKEncodedImageFormat.Png, 100);
            WC4MapEditor.Core.Parsers.AtomicFile.Write(full, encoded.ToArray()); _details.Text += $"\n已导出 {full}";
        }
        catch (Exception ex) { _issues.Text = $"导出失败：{ex.Message}"; }
    }

    public void Dispose() { _disposed = true; _timer.Stop(); _image.Source = null; _images.Dispose(); _fonts.Dispose(); }
}
