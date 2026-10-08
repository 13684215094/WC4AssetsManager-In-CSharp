using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WC4MapEditor.Core.Parsers.Stage;
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Views.Dialogs;

namespace WC4MapEditor.Views;

public partial class BeginScene : UserControl
{
    private readonly MainWindow _window;
    private Grid _mainGrid = null!;
    private DispatcherTimer? _bgTimer;
    private ImageBrush _bgBrush1 = null!;
    private ImageBrush _bgBrush2 = null!;
    private int _bgIndex;
    private bool _useFirstBrush = true;
    private Border _bgBorder1 = null!;
    private Border _bgBorder2 = null!;

    public BeginScene(MainWindow window, bool showProjectLoader = false)
    {
        _window = window;
        InitializeComponent();
        SetupUI(showProjectLoader);
        InitializeBackgroundSlideshow();
        Unloaded += BeginScene_Unloaded;
    }

    private void SetupUI(bool showProjectLoader)
    {
        ConfigManager.Instance.Initialize();
        _mainGrid = new Grid { Background = new SolidColorBrush(Color.FromRgb(24, 28, 36)) };
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        _mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _bgBorder1 = new Border();
        _bgBorder2 = new Border { Opacity = 0 };
        Grid.SetRowSpan(_bgBorder1, 2);
        Grid.SetRowSpan(_bgBorder2, 2);
        _bgBrush1 = new ImageBrush { Stretch = Stretch.UniformToFill };
        _bgBrush2 = new ImageBrush { Stretch = Stretch.UniformToFill };
        _bgBorder1.Background = _bgBrush1;
        _bgBorder2.Background = _bgBrush2;
        _mainGrid.Children.Add(_bgBorder1);
        _mainGrid.Children.Add(_bgBorder2);
        var shade = new Border { Background = new SolidColorBrush(Color.FromArgb(210, 20, 24, 32)), IsHitTestVisible = false };
        Grid.SetRowSpan(shade, 2);
        _mainGrid.Children.Add(shade);

        var titleBar = new DockPanel { LastChildFill = true, Background = Brushes.Transparent };
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) _window.DragMove();
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 8, 0) };
        DockPanel.SetDock(controls, Dock.Right);
        controls.Children.Add(CreateCircleButton("➖", (_, _) => _window.MinimizeWindow()));
        controls.Children.Add(CreateCircleButton("⬜", (_, _) => _window.ToggleMaximize()));
        controls.Children.Add(CreateCircleButton("✕", (_, _) => _window.CloseApplication(), true));
        titleBar.Children.Add(controls);
        titleBar.Children.Add(new TextBlock
        {
            Text = ConfigManager.Instance.GetText("TitleBarText", "WC4 Map Editor"),
            Foreground = Brushes.White, FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 0, 0)
        });
        _mainGrid.Children.Add(titleBar);

        FrameworkElement panel = !showProjectLoader && _window.Projects.Current is { } project
            ? CreateProjectOverviewPanel(project) : CreateProjectLoadPanel();
        var scroll = new ScrollViewer
        {
            Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(24, 12, 24, 24)
        };
        Grid.SetRow(scroll, 1);
        _mainGrid.Children.Add(scroll);
        Content = _mainGrid;
    }

    private void BeginScene_Unloaded(object? sender, RoutedEventArgs e) => _bgTimer?.Stop();

    private void InitializeBackgroundSlideshow()
    {
        string bgPath = ConfigManager.Instance.GetBackgroundPath();
        if (!ProjectFile.DirectoryExists(bgPath)) return;

        var images = new List<string>();
        foreach (string ext in new[] { ".jpg", ".jpeg", ".png", ".bmp" })
            images.AddRange(ProjectFile.GetFiles(bgPath, "*" + ext));

        if (images.Count == 0) return;

        try { _bgBrush1.ImageSource = new BitmapImage(new Uri(ProjectFile.ResolveRead(images[0]), UriKind.Absolute)); } catch { }
        if (images.Count > 1)
            try { _bgBrush2.ImageSource = new BitmapImage(new Uri(ProjectFile.ResolveRead(images[1]), UriKind.Absolute)); } catch { }

        _bgIndex = 0;
        _useFirstBrush = true;

        _bgTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _bgTimer.Tick += (s, e) =>
        {
            _bgIndex = (_bgIndex + 1) % images.Count;
            var nextUri = new Uri(ProjectFile.ResolveRead(images[_bgIndex]), UriKind.Absolute);
            if (_useFirstBrush)
            {
                try { _bgBrush2.ImageSource = new BitmapImage(nextUri); } catch { }
                CrossfadeBackgrounds(true);
            }
            else
            {
                try { _bgBrush1.ImageSource = new BitmapImage(nextUri); } catch { }
                CrossfadeBackgrounds(false);
            }
            _useFirstBrush = !_useFirstBrush;
        };
        _bgTimer.Start();
    }

    private void CrossfadeBackgrounds(bool toSecond)
    {
        var target = toSecond ? _bgBorder2 : _bgBorder1;
        var source = toSecond ? _bgBorder1 : _bgBorder2;
        target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1.5)));
        source.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.5)));
    }

    private Button CreateCircleButton(string content, RoutedEventHandler handler, bool isClose = false)
    {
        var tb = new TextBlock
        {
            Text = content,
            Foreground = Brushes.White,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }
        };

        var btn = new Button
        {
            Width = 42, Height = 42, Foreground = Brushes.White,
            BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 9),
            Cursor = Cursors.Hand, Content = tb,
            Template = CreateCircleButtonTemplate(isClose)
        };

        btn.MouseEnter += (s, e) =>
        {
            if (btn.Content is TextBlock ttb) { ttb.Foreground = isClose ? Brushes.White : Brushes.Black; ttb.Effect = null; }
        };
        btn.MouseLeave += (s, e) =>
        {
            if (btn.Content is TextBlock ttb) { ttb.Foreground = Brushes.White; ttb.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }; }
        };
        btn.Click += handler;
        return btn;
    }

    private ControlTemplate CreateCircleButtonTemplate(bool isClose)
    {
        var f = new FrameworkElementFactory(typeof(Border));
        f.Name = "border";
        f.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        f.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        f.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        f.AppendChild(cp);
        return new ControlTemplate(typeof(Button)) { VisualTree = f };
    }

    private string ProjectInitialDirectory => _window.Projects.Current!.AssetsRoot;

    private string NewBattleSaveDirectory()
    {
        string stage = IOPath.Combine(ProjectInitialDirectory, "stage");
        _window.Projects.Current!.ValidateOutputPath(IOPath.Combine(stage, "new.btl"));
        Directory.CreateDirectory(stage);
        return stage;
    }

    private bool ValidateProjectDestination(string path)
    {
        try { _window.Projects.Current!.ValidateOutputPath(path); return true; }
        catch (Exception ex) { MessageBox.Show(_window, ex.Message, "保存位置", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }

    private async void NewCampaignButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmResult = MessageBox.Show("确定要创建新的战役文件吗？", "新建战役", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmResult != MessageBoxResult.Yes) return;

        using var widthDialog = new SingleInputDialog(_window)
        {
            Title = "新建战役",
            Description = "请输入地图宽度（格子数）：",
            DefaultValue = "10",
            MinValue = 1
        };
        var widthStr = await widthDialog.ShowAsync();
        if (widthStr == null || !int.TryParse(widthStr, out int mapWidth) || mapWidth < 1)
        {
            if (widthStr != null) MessageBox.Show("请输入有效的地图宽度", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        using var heightDialog = new SingleInputDialog(_window)
        {
            Title = "新建战役",
            Description = "请输入地图高度（格子数）：",
            DefaultValue = "10",
            MinValue = 1
        };
        var heightStr = await heightDialog.ShowAsync();
        if (heightStr == null || !int.TryParse(heightStr, out int mapHeight) || mapHeight < 1)
        {
            if (heightStr != null) MessageBox.Show("请输入有效的地图高度", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        using var legionDialog = new SingleInputDialog(_window)
        {
            Title = "新建战役",
            Description = "请输入军团数量：",
            DefaultValue = "2",
            MinValue = 1
        };
        var legionStr = await legionDialog.ShowAsync();
        if (legionStr == null || !int.TryParse(legionStr, out int numLegions) || numLegions < 1)
        {
            if (legionStr != null) MessageBox.Show("请输入有效的军团数量", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var parser = new StageParser();
        if (!parser.CreateNew(mapWidth, mapHeight, numLegions))
        {
            MessageBox.Show("创建战役文件失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var saveResult = MessageBox.Show(
            $"已创建 {mapWidth}x{mapHeight} 的新战役，包含 {mapWidth * mapHeight} 个格子和 {numLegions} 个军团。\n\n是否保存新创建的战役文件？",
            "保存文件", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (saveResult == MessageBoxResult.Yes)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "战役文件 (*.btl)|*.btl|所有文件 (*.*)|*.*",
                Title = "保存战役文件",
                FileName = $"stage_new_{mapWidth}x{mapHeight}.btl",
                InitialDirectory = NewBattleSaveDirectory()
            };
            if (saveDialog.ShowDialog() == true && ValidateProjectDestination(saveDialog.FileName))
            {
                if (parser.SaveData(saveDialog.FileName))
                {
                    MessageBox.Show($"战役文件已保存到:\n{saveDialog.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    var openResult = MessageBox.Show("是否立即打开编辑？", "打开编辑", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (openResult == MessageBoxResult.Yes)
                    {
                        var scene = new StageRenderScene(_window, saveDialog.FileName);
                        _window.SetCurrentScene(scene);
                    }
                }
                else
                {
                    MessageBox.Show("保存战役文件失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
    private async void NewConquestButton_Click(object sender, RoutedEventArgs e)
    {
        var confirmResult = MessageBox.Show("确定要创建新的征服文件吗？", "新建征服", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmResult != MessageBoxResult.Yes) return;

        string selectedMapFilePath = IOPath.Combine(_window.Projects.Current!.AssetsRoot, "world.bin");
        MapData? mapData;
        try { mapData = WorldParser.LoadFromFile(selectedMapFilePath); }
        catch (Exception ex) { MessageBox.Show(_window, ex.Message, "底图加载失败", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        if (mapData == null)
        {
            MessageBox.Show("无法加载地图文件，请检查文件是否有效！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (mapData.MapHeight <= 4)
        {
            MessageBox.Show(_window, "世界地图高度必须大于 4，才能保留征服窗口的上下边距。", "底图尺寸不足", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        const int mapNumber = 1;

        using var legionDialog = new SingleInputDialog(_window)
        {
            Title = "新建征服",
            Description = "请输入军团数量：",
            DefaultValue = "2",
            MinValue = 1
        };
        var legionStr = await legionDialog.ShowAsync();
        if (legionStr == null || !int.TryParse(legionStr, out int numLegions) || numLegions < 1)
        {
            if (legionStr != null) MessageBox.Show("请输入有效的军团数量", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ProjectMapDocument document;
        try { document = ProjectMapDocument.CreateConquest(_window.Projects.Current!, numLegions); }
        catch (Exception ex) { MessageBox.Show(_window, ex.Message, "创建征服失败", MessageBoxButton.OK, MessageBoxImage.Error); return; }
        int actualMapWidth = document.Map.MapWidth, actualMapHeight = document.Map.MapHeight;

        var mapFileName = System.IO.Path.GetFileNameWithoutExtension(selectedMapFilePath);
        var saveResult = MessageBox.Show(
            $"已创建基于 \"{mapFileName}\" 的新征服（{actualMapWidth}x{actualMapHeight}，地图编号 {mapNumber}，{numLegions} 个军团）。\n\n是否保存新创建的征服文件？",
            "保存文件", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (saveResult == MessageBoxResult.Yes)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "征服文件 (*.btl)|*.btl|所有文件 (*.*)|*.*",
                Title = "保存征服文件",
                FileName = $"conquest_{mapFileName}_{mapNumber}_{actualMapWidth}x{actualMapHeight}.btl",
                InitialDirectory = NewBattleSaveDirectory()
            };
            if (saveDialog.ShowDialog() == true && ValidateProjectDestination(saveDialog.FileName))
            {
                try
                {
                    document.Save(saveDialog.FileName);
                    MessageBox.Show($"征服文件已保存到:\n{saveDialog.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    var openResult = MessageBox.Show("是否立即打开编辑？", "打开编辑", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (openResult == MessageBoxResult.Yes)
                    {
                        var scene = new ConquestRenderScene(_window, saveDialog.FileName);
                        _window.SetCurrentScene(scene);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存征服文件失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
    private async void NewMapButton_Click(object sender, RoutedEventArgs e)
    {
        using var inputDialog = new SingleInputDialog(_window)
        {
            Title = "创建新地图",
            Description = "请输入地图尺寸（格式：宽度x高度，如：100x100）：",
            DefaultValue = "100x100"
        };
        var sizeStr = await inputDialog.ShowAsync();
        if (sizeStr == null) return;

        try
        {
            var parts = sizeStr.Trim().Split(['x', 'X', '*', ' '], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                MessageBox.Show("请输入正确的尺寸格式（如：100x100）", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int width = int.Parse(parts[0].Trim());
            int height = int.Parse(parts[1].Trim());

            MapLimits.Area(width, height);

            var mapData = WorldParser.CreateNew(width, height);
            mapData.IsModified = true;

            var saveResult = MessageBox.Show(
                $"已创建 {width}x{height} 的新地图。\n\n是否保存地图文件？",
                "保存文件", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (saveResult == MessageBoxResult.Yes)
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "地图文件 (*.bin)|*.bin|所有文件 (*.*)|*.*",
                    Title = "保存地图文件",
                    FileName = $"new_map_{width}x{height}.bin",
                    InitialDirectory = ProjectInitialDirectory
                };
                if (saveDialog.ShowDialog() == true && ValidateProjectDestination(saveDialog.FileName))
                {
                    WorldParser.SaveToFile(mapData, saveDialog.FileName);
                    MessageBox.Show($"地图文件已保存到:\n{saveDialog.FileName}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    var openResult = MessageBox.Show("是否立即打开编辑？", "打开编辑", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (openResult == MessageBoxResult.Yes)
                    {
                        var scene = new MapRenderScene(_window, saveDialog.FileName);
                        _window.SetCurrentScene(scene);
                    }
                }
            }
            else
            {
                var scene = new MapRenderScene(_window, initialMap: mapData);
                _window.SetCurrentScene(scene);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建新地图失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}
