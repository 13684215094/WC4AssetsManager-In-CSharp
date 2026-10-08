using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Rendering.Imaging;
using WC4MapEditor.Views.Assist;

namespace WC4MapEditor.Views;

public class CountryEditScene : UserControl, IUnsavedScene, IDisposable
{
    public bool HasUnsavedChanges => _parser.HasChanges;
    public bool SaveChanges()
    {
        if (_parser.SaveAll()) return true;
        MessageBox.Show(_parser.LastError ?? "保存失败，修改仍保留。", "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        return false;
    }
    public void DiscardChanges() => _parser.Reload();

    private readonly MainWindow _window;
    private readonly CountrySettingParser _parser = CountrySettingParser.Instance;
    private readonly IFlagImageService _flagService = new FlagImageService();

    private Grid _root = null!;
    private ListBox _countryList = null!;
    private ListBox _conquerList = null!;
    private TabControl _listTabs = null!;
    private StackPanel _detailPanel = null!;
    private Image _flagPreview = null!;
    private TextBlock _statusText = null!;
    private ProjectDetailPreviewControl _countryPreview = null!;
    private readonly TextBox _countrySearch = new() { Margin = new Thickness(5), ToolTip = "搜索国家 Id 或名称" };
    private readonly TextBox _conquerSearch = new() { Margin = new Thickness(5), ToolTip = "搜索征服 Id 或名称" };

    private CountrySettingData? _currentCountry;
    private bool _refreshingConquers;

    public CountryEditScene(MainWindow window)
    {
        _window = window;
        Unloaded += (_, _) => (_flagService as IDisposable)?.Dispose();
        InitializeUI();
        LoadCountries();
        LoadConquers();
        _countrySearch.TextChanged += (_, _) => LoadCountries();
        _conquerSearch.TextChanged += (_, _) => { if (_currentCountry != null) LoadConquerCountries(_currentCountry.Id); };
        _listTabs.SelectionChanged += (_, args) => { if (ReferenceEquals(args.OriginalSource, _listTabs)) ShowSelectedDetail(); };
    }

    private void InitializeUI()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        _root = new Grid();
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Top bar
        var backBtn = new Button
        {
            Content = "返回",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(15, 5, 15, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        backBtn.Click += (_, _) => _window.SetCurrentScene(new BeginScene(_window));

        var addBtn = new Button
        {
            Content = "+ 新增国家",
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(15, 5, 15, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        addBtn.Click += OnAddCountry;

        var topBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Height = 50,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    backBtn,
                    addBtn,
                    new TextBlock
                    {
                        Text = "国家数据编辑",
                        Foreground = Brushes.White,
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(20, 0, 0, 0)
                    }
                }
            }
        };
        Grid.SetRow(topBar, 0);
        _root.Children.Add(topBar);

        // Main content
        var mainGrid = new Grid();
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250), MinWidth = 190, MaxWidth = 350 });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });
        Grid.SetRow(mainGrid, 1);
        _listTabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
        mainGrid.Children.Add(_listTabs);
        var splitter = new GridSplitter { Width = 4, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.DimGray,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(splitter, 1); mainGrid.Children.Add(splitter);

        // Country list
        _countryList = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(5)
        };
        _countryList.SelectionChanged += OnCountrySelected;
        _listTabs.Items.Add(new TabItem { Header = "基础国家", Content = CreatePanel("搜索国家 Id / 名称", _countrySearch, _countryList) });

        // Conquer list for selected country
        _conquerList = new ListBox
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(5)
        };
        _conquerList.SelectionChanged += OnConquerCountrySelected;
        _listTabs.Items.Add(new TabItem { Header = "征服实例", Content = CreatePanel("当前国家的征服实例", _conquerSearch, _conquerList) });

        // Detail panel
        _detailPanel = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Margin = new Thickness(5)
        };
        _countryPreview = new ProjectDetailPreviewControl(army: false);
        var detailBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = new EditorPreviewPane(new ScrollViewer { Content = _detailPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, _countryPreview, editorMinWidth: 320)
        };
        Grid.SetColumn(detailBorder, 2);
        mainGrid.Children.Add(detailBorder);

        _root.Children.Add(mainGrid);

        // Status bar
        _statusText = new TextBlock
        {
            Foreground = Brushes.Gray,
            Margin = new Thickness(10, 5, 10, 5)
        };
        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Child = _statusText
        };
        Grid.SetRow(statusBar, 2);
        _root.Children.Add(statusBar);

        Content = _root;
    }

    private static Border CreatePanel(string title, TextBox search, ListBox list)
    {
        var panel = new DockPanel();
        var heading = new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(5)
        };
        DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        DockPanel.SetDock(search, Dock.Top); panel.Children.Add(search);
        panel.Children.Add(list);

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(5),
            Child = panel
        };
    }

    private void LoadCountries()
    {
        int? selected = (_countryList.SelectedItem as ListBoxItem)?.Tag is CountrySettingData data ? data.Id : null;
        _countryList.Items.Clear();
        string search = _countrySearch.Text.Trim();
        foreach (var country in _parser.Countries)
        {
            if (search.Length > 0 && !country.Id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !(country.Name?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)) continue;
            var item = new ListBoxItem
            {
                Content = $"[{country.Id}] {country.Name}",
                Tag = country,
                Foreground = Brushes.White,
                Background = Brushes.Transparent
            };
            _countryList.Items.Add(item);
            if (country.Id == selected) _countryList.SelectedItem = item;
        }
        if (_countryList.SelectedItem == null)
        {
            _currentCountry = null; _conquerList.Items.Clear(); _detailPanel.Children.Clear(); _countryPreview.Clear();
        }
        SetStatus($"显示 {_countryList.Items.Count} / {_parser.Countries.Count} 个国家 · 字段确认后立即保存到增量输出");
    }

    private void LoadConquers()
    {
        // Conquers are loaded in the list when a country is selected
    }

    private void OnCountrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (_countryList.SelectedItem is not ListBoxItem item) return;
        _currentCountry = item.Tag as CountrySettingData;
        if (_currentCountry == null) return;

        LoadConquerCountries(_currentCountry.Id);
    }

    private void LoadConquerCountries(int countryId)
    {
        var selected = (_conquerList.SelectedItem as ListBoxItem)?.Tag as ConquerCountrySettingData;
        _refreshingConquers = true;
        try
        {
            _conquerList.Items.Clear();
            string search = _conquerSearch.Text.Trim();
            foreach (var cc in _parser.GetConquerCountriesByCountryId(countryId))
            {
                var conquerName = _parser.GetConquerName(cc.ConquerId);
                if (search.Length > 0 && !cc.ConquerId.ToString().Contains(search, StringComparison.OrdinalIgnoreCase) &&
                    !conquerName.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
                var item = new ListBoxItem
                {
                    Content = $"[{cc.ConquerId}] {conquerName} - 星级:{cc.Star}",
                    Tag = cc,
                    Foreground = Brushes.White,
                    Background = Brushes.Transparent
                };
                _conquerList.Items.Add(item);
                if (ReferenceEquals(cc, selected)) _conquerList.SelectedItem = item;
            }
        }
        finally { _refreshingConquers = false; }
        ShowSelectedDetail();
    }

    private void OnConquerCountrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingConquers) ShowSelectedDetail();
    }

    private void ShowSelectedDetail()
    {
        if (_listTabs.SelectedIndex == 1 && (_conquerList.SelectedItem as ListBoxItem)?.Tag is ConquerCountrySettingData cc)
            ShowConquerCountryDetail(cc);
        else if (_currentCountry != null) ShowCountryDetail(_currentCountry);
    }

    private void ShowCountryDetail(CountrySettingData country)
    {
        _countryPreview.Clear();
        _detailPanel.Children.Clear();

        // Flag preview
        _flagPreview = new Image
        {
            Width = 100,
            Height = 100,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(5)
        };
        LoadFlagPreview(country.Id);
        _detailPanel.Children.Add(_flagPreview);

        // Edit flag button
        var editFlagBtn = new Button
        {
            Content = "编辑国旗",
            Margin = new Thickness(5),
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        editFlagBtn.Click += (_, _) =>
        {
            var scene = new FlagEditScene(_window, country.Id, country.Name);
            _window.SetCurrentScene(scene);
        };

        // 按钮行：编辑国旗 + 使用现有国旗
        var flagBtnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        flagBtnPanel.Children.Add(editFlagBtn);

        var useExistingFlagBtn = new Button
        {
            Content = "使用现有国旗",
            Margin = new Thickness(5, 0, 0, 0),
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        useExistingFlagBtn.Click += (_, _) => OnUseExistingFlag(country.Id);
        flagBtnPanel.Children.Add(useExistingFlagBtn);

        _detailPanel.Children.Add(flagBtnPanel);

        // Country info with edit buttons
        var realName = _parser.GetCountryRealName(country.Id);
        _detailPanel.Children.Add(CreateEditableInfoBlockWithSync("名称", country.Name,
            v => { country.Name = v; SaveAndReload(country); },
            () =>
            {
                if (_parser.SyncRealNameToName(country.Id)) { LoadCountries(); SetStatus($"已将真实名称同步为名称: {country.Name}"); }
                else ShowSaveError();
            }));
        _detailPanel.Children.Add(CreateEditableInfoBlockWithSync("真实名称", realName,
            v => { if (_parser.SetCountryRealName(country.Id, v)) { ShowCountryDetail(country); SetStatus($"已更新真实名称: {v}"); } else ShowSaveError(); },
            () => { if (_parser.SyncNameToRealName(country.Id)) { ShowCountryDetail(country); SetStatus($"已将名称同步为真实名称: {country.Name}"); } else ShowSaveError(); }));
        _detailPanel.Children.Add(CreateEditableInfoBlock("阵营", country.Camp.ToString(), v => { if (int.TryParse(v, out var n)) { country.Camp = n; SaveAndReload(country); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock("步兵模组", country.InfantryMod.ToString(), v => { if (int.TryParse(v, out var n)) { country.InfantryMod = n; SaveAndReload(country); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock("装甲模组", country.ArmoredMod.ToString(), v => { if (int.TryParse(v, out var n)) { country.ArmoredMod = n; SaveAndReload(country); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock("炮兵模组", country.ArtilleryMod.ToString(), v => { if (int.TryParse(v, out var n)) { country.ArtilleryMod = n; SaveAndReload(country); } }));
        _detailPanel.Children.Add(CreateEditableInfoBlock("Logo", country.Logo.ToString(), v => { if (int.TryParse(v, out var n)) { country.Logo = n; SaveAndReload(country); } }));

        // Delete country button
        var deleteBtn = new Button
        {
            Content = "删除国家",
            Margin = new Thickness(5, 15, 5, 5),
            Padding = new Thickness(10, 5, 10, 5),
            Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x30, 0x30)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        deleteBtn.Click += async (_, _) =>
        {
            var result = MessageBox.Show($"确定要删除国家 [{country.Id}] {country.Name} 吗？\n这将同时删除该国家在所有征服中的数据。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            if (_parser.RemoveCountry(country.Id))
            {
                _currentCountry = null;
                LoadCountries();
                _detailPanel.Children.Clear();
                SetStatus($"已删除国家: [{country.Id}] {country.Name}");
            }
            else MessageBox.Show(_parser.LastError ?? "删除失败。", "删除失败", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        _detailPanel.Children.Add(deleteBtn);
    }

    private void SaveAndReload(CountrySettingData country)
    {
        if (!_parser.SaveCountries())
        {
            ShowSaveError();
            return;
        }
        LoadCountries();
        SetStatus($"已更新: [{country.Id}] {country.Name}");
        foreach (ListBoxItem item in _countryList.Items)
        {
            if (item.Tag is CountrySettingData c && c.Id == country.Id)
            {
                _countryList.SelectedItem = item;
                break;
            }
        }
    }

    private void ShowSaveError()
    {
        string message = _parser.LastError ?? "无法保存或同步，请检查名称与当前资源。";
        SetStatus($"保存失败，修改仍保留：{message}");
        MessageBox.Show(message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private StackPanel CreateEditableInfoBlock(string label, string value, Action<string> onSave)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        panel.Children.Add(new TextBlock
        {
            Text = label + ": ",
            Foreground = Brushes.Gray,
            Width = 80
        });
        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = Brushes.White,
            Width = 120
        };
        panel.Children.Add(valueBlock);

        var editBtn = new Button
        {
            Content = "编辑",
            FontSize = 11,
            Padding = new Thickness(8, 2, 8, 2),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        editBtn.Click += async (_, _) =>
        {
            using var dialog = new Views.Dialogs.SingleInputDialog(_window)
            {
                Title = $"修改 {label}",
                Description = $"请输入新的{label}：",
                DefaultValue = value,
                ConfirmText = "保存",
                CancelText = "取消"
            };
            var newValue = await dialog.ShowAsync();
            if (newValue != null)
            {
                onSave(newValue.Trim());
            }
        };
        panel.Children.Add(editBtn);
        return panel;
    }

    private StackPanel CreateEditableInfoBlockWithSync(string label, string value, Action<string> onSave, Action onSync)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        panel.Children.Add(new TextBlock
        {
            Text = label + ": ",
            Foreground = Brushes.Gray,
            Width = 80
        });
        var valueBlock = new TextBlock
        {
            Text = value,
            Foreground = Brushes.White,
            Width = 120
        };
        panel.Children.Add(valueBlock);

        var editBtn = new Button
        {
            Content = "编辑",
            FontSize = 11,
            Padding = new Thickness(8, 2, 8, 2),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0)
        };
        editBtn.Click += async (_, _) =>
        {
            using var dialog = new Views.Dialogs.SingleInputDialog(_window)
            {
                Title = $"修改 {label}",
                Description = $"请输入新的{label}：",
                DefaultValue = value,
                ConfirmText = "保存",
                CancelText = "取消"
            };
            var newValue = await dialog.ShowAsync();
            if (newValue != null)
            {
                onSave(newValue.Trim());
            }
        };
        panel.Children.Add(editBtn);

        var syncBtn = new Button
        {
            Content = label == "名称" ? "同步为真实名称" : "同步为名称",
            FontSize = 11,
            Padding = new Thickness(8, 2, 8, 2),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        syncBtn.Click += (_, _) => onSync();
        panel.Children.Add(syncBtn);

        return panel;
    }

    private void ShowConquerCountryDetail(ConquerCountrySettingData cc)
    {
        if (_parser.GetCountryById(cc.CountryId) is { } country) _countryPreview.UpdateCountry(country, cc);
        else _countryPreview.Clear();
        _detailPanel.Children.Clear();

        var countryName = _parser.GetCountryName(cc.CountryId);
        var conquerName = _parser.GetConquerName(cc.ConquerId);

        _detailPanel.Children.Add(CreateInfoBlock("ID", cc.Id.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("征服", $"[{cc.ConquerId}] {conquerName}"));
        _detailPanel.Children.Add(CreateInfoBlock("国家", $"[{cc.CountryId}] {countryName}"));
        _detailPanel.Children.Add(CreateInfoBlock("座位", cc.Seat.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("星级", cc.Star.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("阵营", cc.Camp.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("战争回合", cc.WarTurn.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("经验奖励", cc.PrizeExp.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("金币奖励", cc.PrizeGold.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("工业奖励", cc.PrizeIndustry.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("能源奖励", cc.PrizeEnergy.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("科技奖励", cc.PrizeTech.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("照片", cc.Photo));
        _detailPanel.Children.Add(CreateInfoBlock("费用-金钱", cc.CostMoney.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("费用-齿轮", cc.CostGear.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("费用-原子", cc.CostAtomic.ToString()));
        _detailPanel.Children.Add(CreateInfoBlock("系数", cc.Coefficient.ToString("F2")));
        _detailPanel.Children.Add(CreateInfoBlock("投降", cc.Surrender ? "是" : "否"));
    }

    private static StackPanel CreateInfoBlock(string label, string value)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
        panel.Children.Add(new TextBlock
        {
            Text = label + ": ",
            Foreground = Brushes.Gray,
            Width = 100
        });
        panel.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = Brushes.White
        });
        return panel;
    }

    public void Dispose() { _countryPreview.Dispose(); (_flagService as IDisposable)?.Dispose(); }

    private void LoadFlagPreview(int countryId)
    {
        _flagPreview.Source = null;
        try
        {
            using var flag = _flagService.LoadFlagFromHdAtlas(countryId) ?? _flagService.LoadBigFlag(countryId);

            if (flag != null)
            {
                _flagPreview.Source = SkBitmapToBitmapSource(flag);
                _flagPreview.ToolTip = $"flag_{countryId}.png（来自当前项目图集）";
            }
            else
            {
                _flagPreview.ToolTip = _flagService.LastError ?? "当前项目未提供该国家旗帜。";
                SetStatus($"国旗加载失败：{_flagPreview.ToolTip}");
            }
        }
        catch (Exception ex) { _flagPreview.ToolTip = ex.Message; SetStatus($"国旗加载失败：{ex.Message}"); }
    }

    private static BitmapSource SkBitmapToBitmapSource(SKBitmap bitmap)
        => WC4MapEditor.Views.Assist.SkiaBitmapSource.Create(bitmap);

    private async void OnAddCountry(object sender, RoutedEventArgs e)
    {
        var nextId = _parser.GetNextCountryId();
        using var dialog = new Views.Dialogs.SingleInputDialog(_window)
        {
            Title = "新增国家",
            Description = $"将要新增一个国家，建议ID为 {nextId}。\n请输入国家名称：",
            DefaultValue = "",
            ConfirmText = "确定",
            CancelText = "取消"
        };
        var name = await dialog.ShowAsync();
        if (name == null) return;

        name = name.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("国家名称不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var country = new CountrySettingData
        {
            Id = nextId,
            Name = name,
            Camp = 0,
            InfantryMod = 0,
            ArmoredMod = 0,
            ArtilleryMod = 0,
            Logo = 0
        };

        if (_parser.AddCountry(country))
        {
            LoadCountries();
            SetStatus($"新增国家成功: [{nextId}] {name}");
            foreach (ListBoxItem item in _countryList.Items)
            {
                if (item.Tag is CountrySettingData c && c.Id == nextId)
                {
                    _countryList.SelectedItem = item;
                    break;
                }
            }
        }
        else
        {
            ShowSaveError();
        }
    }

    private void SetStatus(string text)
    {
        _statusText.Text = text;
    }

    private void OnUseExistingFlag(int targetCountryId)
    {
        var inputDialog = new Window
        {
            Title = "使用现有国旗",
            Width = 320,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = _window,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30))
        };

        var panel = new StackPanel { Margin = new Thickness(15) };
        panel.Children.Add(new TextBlock
        {
            Text = $"输入要复制的源国家ID（将复制该国家的国旗到当前国家ID={targetCountryId}）",
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var idBox = new TextBox
        {
            Text = "",
            Margin = new Thickness(0, 0, 0, 10),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White
        };
        panel.Children.Add(idBox);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var okBtn = new Button
        {
            Content = "确定",
            Padding = new Thickness(20, 5, 20, 5),
            Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x63, 0x9C)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            IsDefault = true
        };
        var cancelBtn = new Button
        {
            Content = "取消",
            Padding = new Thickness(20, 5, 20, 5),
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            IsCancel = true
        };
        btnPanel.Children.Add(okBtn);
        btnPanel.Children.Add(cancelBtn);
        panel.Children.Add(btnPanel);

        inputDialog.Content = panel;

        int? sourceId = null;
        okBtn.Click += (_, _) => { if (int.TryParse(idBox.Text, out int id)) sourceId = id; inputDialog.Close(); };
        cancelBtn.Click += (_, _) => inputDialog.Close();

        inputDialog.ShowDialog();

        if (sourceId == null || sourceId.Value <= 0)
        {
            SetStatus("已取消或ID无效");
            return;
        }

        if (sourceId.Value == targetCountryId)
        {
            MessageBox.Show("源国家ID与当前国家ID相同，无需复制", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetStatus($"正在复制国家{sourceId.Value}的国旗到国家{targetCountryId}...");

        try
        {
            using var bigFlag = _flagService.LoadBigFlag(sourceId.Value);
            using var smallFlag = _flagService.LoadSmallFlag(sourceId.Value);
            using var hdFlag = _flagService.LoadFlagFromHdAtlas(sourceId.Value);

            if (bigFlag == null && smallFlag == null && hdFlag == null)
            {
                MessageBox.Show($"国家{sourceId.Value}没有任何国旗数据，无法复制", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var files = new List<(string Path, byte[] Bytes)>();
            if (bigFlag != null || smallFlag != null)
            {
                var (imagePath, xmlPath) = FindTacticalMapFiles();
                if (imagePath == null || xmlPath == null) throw new InvalidDataException("战术国旗图集缺少图片或 XML。");
                var editor = new TacticalMapEditor();
                if (!editor.LoadFromFiles(imagePath, xmlPath)) throw new InvalidDataException("战术国旗图集加载失败。");
                var images = new List<(string name, SKBitmap image)>();
                if (bigFlag != null) images.Add(($"flag_{targetCountryId}.png", bigFlag));
                if (smallFlag != null) images.Add(($"f_{targetCountryId:D2}.png", smallFlag));
                files.AddRange(editor.PrepareImagesAndArrange(images, imagePath, xmlPath));
            }
            if (hdFlag != null)
            {
                var (imagePath, xmlPath) = FindHdAtlasFiles();
                if (imagePath == null || xmlPath == null) throw new InvalidDataException("HD 国旗图集缺少图片或 XML。");
                var editor = new TacticalMapEditor();
                if (!editor.LoadFromFiles(imagePath, xmlPath)) throw new InvalidDataException("HD 国旗图集加载失败。");
                files.AddRange(editor.PrepareImagesAndArrange([($"flag_{targetCountryId}.png", hdFlag)], imagePath, xmlPath));
            }
            Core.Parsers.AtomicFile.WriteAll(files.ToArray());

            _flagService.RefreshCache();

            if (_currentCountry != null)
                LoadFlagPreview(_currentCountry.Id);

            MessageBox.Show($"已将国家{sourceId.Value}的国旗复制到国家{targetCountryId}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            SetStatus($"国旗复制完成: 源ID={sourceId.Value} → 目标ID={targetCountryId}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"复制国旗失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus($"复制失败: {ex.Message}");
        }
    }

    private (string? imagePath, string? xmlPath) FindTacticalMapFiles()
    {
        var am = Core.Assets.AssetManager.Default;
        if (am.IsLoaded)
        {
            var imgEntry = am.Find("tacticalmap.webp") ?? am.Find("tacticalmap.png");
            var xmlEntry = am.Find("tacticalmap.xml");
            if (imgEntry != null && xmlEntry != null)
                return (imgEntry.FullPath, xmlEntry.FullPath);
        }

        var dir = _parser.TacticalMapDir;
        if (string.IsNullOrEmpty(dir)) return (null, null);

        var dirs = new[] { dir, System.IO.Path.GetDirectoryName(dir) ?? "" };
        foreach (var d in dirs)
        {
            if (string.IsNullOrEmpty(d) || !ProjectFile.DirectoryExists(d)) continue;
            foreach (var ext in new[] { ".png", ".webp" })
            {
                var imgPath = System.IO.Path.Combine(d, $"tacticalmap{ext}");
                var xmlPath = System.IO.Path.Combine(d, "tacticalmap.xml");
                if (ProjectFile.Exists(imgPath) && ProjectFile.Exists(xmlPath))
                    return (imgPath, xmlPath);
            }
        }
        return (null, null);
    }

    private (string? imagePath, string? xmlPath) FindHdAtlasFiles()
    {
        var hdImg = _parser.FlagsHdImagePath;
        var hdXml = _parser.FlagsHdXmlPath;

        if (!string.IsNullOrEmpty(hdImg) && !string.IsNullOrEmpty(hdXml) && ProjectFile.Exists(hdImg) && ProjectFile.Exists(hdXml))
            return (hdImg, hdXml);

        if (!string.IsNullOrEmpty(hdXml) && ProjectFile.Exists(hdXml))
        {
            var dir = System.IO.Path.GetDirectoryName(hdImg) ?? "";
            var pngPath = System.IO.Path.Combine(dir, "image_flags_hd.png");
            if (ProjectFile.Exists(pngPath)) return (pngPath, hdXml);
        }
        return (null, null);
    }
}
