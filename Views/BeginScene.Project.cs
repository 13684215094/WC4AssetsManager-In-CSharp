using System.IO;
using Microsoft.Win32;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Views.Dialogs;

namespace WC4MapEditor.Views;

public partial class BeginScene
{
    private TextBox _sourcePathBox = null!;
    private TextBox _outputPathBox = null!;
    private TextBlock _loadStatus = null!;
    private TextBlock _defaultOutputHint = null!;
    private string _lastAutomaticOutput = "";
    private bool _isLoading;

    private FrameworkElement CreateProjectLoadPanel()
    {
        var panel = new StackPanel { MaxWidth = 880, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(Heading("加载游戏项目", 32));
        panel.Children.Add(Paragraph("选择游戏目录或 APK 安装包。加载完成后，在项目内编辑地图、将领、国家及其他资源。", 16));

        var form = new StackPanel { Margin = new Thickness(24) };
        form.Children.Add(Heading("游戏项目来源（目录 / APK，必填）", 16));
        _sourcePathBox = PathBox();
        System.Windows.Automation.AutomationProperties.SetName(_sourcePathBox, "游戏项目来源（目录或 APK，必填）");
        form.Children.Add(PathRow(_sourcePathBox, "选择目录", (_, _) => PickFolder(_sourcePathBox, "选择解包游戏项目目录（含 assets）")));
        form.Children.Add(ActionButton("选择 APK 安装包", (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "选择 World Conqueror 4 APK", Filter = "APK 安装包|*.apk" };
            if (picker.ShowDialog(_window) == true) _sourcePathBox.Text = picker.FileName;
        }));
        form.Children.Add(Paragraph("目录需包含 assets，也可直接选择 assets。APK 自动提取到独立缓存，输出目录仍只保存新增或修改文件。"));
        form.Children.Add(Heading("修改输出目录（可选）", 16));
        _outputPathBox = PathBox();
        System.Windows.Automation.AutomationProperties.SetName(_outputPathBox, "修改输出目录（可选，默认来源同级）");
        form.Children.Add(PathRow(_outputPathBox, "选择输出", (_, _) => PickFolder(_outputPathBox, "选择输出目录；也可在输入框填写尚未创建的目录")));
        _defaultOutputHint = Paragraph("留空时，在来源目录的同级创建 <项目名>-edited。", 13);
        form.Children.Add(_defaultOutputHint);
        form.Children.Add(Paragraph("原目录用于读取；输出只保存新增或修改的文件，并保持原来的目录结构。", 14));

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var load = ActionButton("加载项目", LoadProject_Click);
        actions.Children.Add(load);
        actions.Children.Add(ActionButton("选择已有输出", ChooseExistingOutput_Click));
        if (_window.Projects.Current != null)
            actions.Children.Add(ActionButton("返回当前项目", (_, _) => _window.ReturnToMainScene()));
        form.Children.Add(actions);
        _loadStatus = Paragraph("请填写来源目录，输出目录可使用默认值。");
        form.Children.Add(_loadStatus);
        panel.Children.Add(Card(form));

        if (_window.Projects.Current is { } project)
        {
            _sourcePathBox.Text = project.SourcePath;
            _outputPathBox.Text = project.OutputRoot;
            _lastAutomaticOutput = GameProjectWorkspace.GetDefaultOutputDirectory(project.SourcePath);
        }
        _sourcePathBox.TextChanged += (_, _) => UpdateDefaultOutput();
        _outputPathBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { LoadProject_Click(load, e); e.Handled = true; } };
        UpdateDefaultOutput();
        _sourcePathBox.Loaded += (_, _) => _sourcePathBox.Focus();
        return panel;
    }

    private void UpdateDefaultOutput()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_sourcePathBox.Text))
            {
                _defaultOutputHint.Text = "留空时，在来源目录的同级创建 <项目名>-edited。";
                return;
            }
            string suggested = GameProjectWorkspace.GetDefaultOutputDirectory(_sourcePathBox.Text);
            if (string.IsNullOrWhiteSpace(_outputPathBox.Text) || _outputPathBox.Text == _lastAutomaticOutput)
                _outputPathBox.Text = suggested;
            _lastAutomaticOutput = suggested;
            _defaultOutputHint.Text = $"默认输出：{suggested}\n可直接输入新的目录；已存在的输出项目将继续编辑。";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            _defaultOutputHint.Text = "请输入有效的来源路径，以生成默认输出目录。";
        }
    }

    private void PickFolder(TextBox box, string title)
    {
        var picker = new OpenFolderDialog { Title = title };
        string candidate = box.Text.Trim().Trim('"');
        if (Directory.Exists(candidate)) picker.InitialDirectory = candidate;
        if (picker.ShowDialog(_window) == true) box.Text = picker.FolderName;
    }

    private void ChooseExistingOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = "选择已有项目输出目录（含 .wc4-project.json）" };
        if (picker.ShowDialog(_window) != true) return;
        try
        {
            string source = GameProjectWorkspace.ReadSourceDirectory(picker.FolderName);
            _outputPathBox.Text = picker.FolderName;
            _sourcePathBox.Text = source;
            _loadStatus.Text = "已填入此输出的来源和保存目录；点击“加载项目”继续编辑。";
        }
        catch (Exception ex) { _loadStatus.Text = $"读取已有项目失败：{ex.Message}"; }
    }

    private async void LoadProject_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (string.IsNullOrWhiteSpace(_sourcePathBox.Text))
        {
            _loadStatus.Text = "来源必填，请选择游戏目录或 APK 安装包。";
            _sourcePathBox.Focus();
            return;
        }
        if (!_window.ConfirmAllUnsavedChanges("加载项目")) return;
        string source = _sourcePathBox.Text;
        string output = _outputPathBox.Text;
        using var cancellation = new CancellationTokenSource();
        var progressWindow = new ProgressWindow("加载游戏项目") { Owner = _window };
        progressWindow.CancellationRequested += cancellation.Cancel;
        var progress = new Progress<ProjectCopyProgress>(value => progressWindow.UpdateStatus(value.RelativePath,
            value.TotalFiles == 0 ? 0 : 100.0 * value.CompletedFiles / value.TotalFiles));
        _isLoading = true;
        _mainGrid.IsEnabled = false;
        progressWindow.Show();
        try
        {
            var project = await Task.Run(() => GameProjectWorkspace.LoadOrCreate(source, output, progress, cancellation.Token));
            cancellation.Token.ThrowIfCancellationRequested();
            await _window.Projects.ActivateAsync(project, progress, cancellation.Token);
            _window.ReturnToMainScene();
        }
        catch (OperationCanceledException) { _loadStatus.Text = "已取消加载。"; }
        catch (Exception ex) { _loadStatus.Text = $"加载失败：{ex.Message}"; }
        finally
        {
            progressWindow.PlayFadeOutAndClose();
            _mainGrid.IsEnabled = true;
            _isLoading = false;
        }
    }

    private FrameworkElement CreateProjectOverviewPanel(GameProjectWorkspace project)
    {
        var manager = AssetManager.Default;
        string? scanError = null;
        try { manager.Scan(project.AssetsRoot); }
        catch (Exception ex) { scanError = ex.Message; }
        var catalog = new ProjectEditorCatalog(manager);
        var panel = new StackPanel();
        panel.Children.Add(Heading(IOPath.GetFileName(project.SourcePath), 30));
        panel.Children.Add(Paragraph($"来源：{project.SourcePath}\n输出：{project.OutputRoot}" +
            (project.IsApkSource ? $"\nAPK 资源缓存：{project.SourceRoot}" : ""), 14));
        panel.Children.Add(Paragraph($"已加载 {manager.Count:N0} 个资源文件 · " +
            (project.IsIncremental ? "仅保存新增或修改文件" : "旧版完整副本项目"), 14));
        if (scanError != null) panel.Children.Add(Paragraph($"刷新资源索引失败：{scanError}。请检查来源目录或切换项目。"));
        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 16) };
        actions.Children.Add(ActionButton("切换项目 / 输出目录", (_, _) => _window.ShowProjectLoader()));
        actions.Children.Add(ActionButton("数据关系检查", (_, _) => new AssetAuditWindow(_window, project.AssetsRoot).ShowDialog()));
        actions.Children.Add(ActionButton("关闭项目", (_, _) => _window.CloseProject()));
        panel.Children.Add(actions);
        if (WC4MapEditor.Core.Services.ExternalToolRunner.UnavailableReason is { } toolReason)
            panel.Children.Add(Paragraph(toolReason, 12));

        AddGroup("地图与关卡", ProjectEditorKind.Campaign, ProjectEditorKind.Conquest, ProjectEditorKind.OtherBattle, ProjectEditorKind.World);
        var newMaps = new WrapPanel { Margin = new Thickness(0, 2, 0, 16) };
        newMaps.Children.Add(ActionButton("新增战役", NewCampaignButton_Click));
        var newConquest = ActionButton("新增征服", NewConquestButton_Click);
        newConquest.IsEnabled = manager.Find("world.bin")?.Kind == AssetKind.World;
        newConquest.ToolTip = newConquest.IsEnabled ? "使用当前项目的 world.bin 地形，地图编号固定为 1"
            : "当前项目缺少有效的 world.bin，无法创建引用它的征服关卡";
        ToolTipService.SetShowOnDisabled(newConquest, true);
        newMaps.Children.Add(newConquest);
        newMaps.Children.Add(ActionButton("新增世界地形", NewMapButton_Click));
        panel.Children.Add(newMaps);
        AddGroup("角色、国家与战斗数据", ProjectEditorKind.General, ProjectEditorKind.Country, ProjectEditorKind.Skill,
            ProjectEditorKind.Army, ProjectEditorKind.ArmyProgress, ProjectEditorKind.ArmyBuff, ProjectEditorKind.BuildingFacility, ProjectEditorKind.CountryTech);
        AddGroup("集团军与事件", ProjectEditorKind.ArmyGroup, ProjectEditorKind.ArmyGroupEvent,
            ProjectEditorKind.EventBuff, ProjectEditorKind.ConquerEvent);
        AddGroup("扩展配置与界面资源", ProjectEditorKind.GeneralProgress, ProjectEditorKind.HeadquartersTech,
            ProjectEditorKind.JsonTables, ProjectEditorKind.Layout, ProjectEditorKind.Resources);
        panel.Children.Add(Paragraph("灰色入口表示项目缺少所需资源或配置格式有误，悬停可查看原因。其他 JSON、本地化和图集可从“全部资源”打开。"));
        return panel;

        void AddGroup(string title, params ProjectEditorKind[] kinds)
        {
            panel.Children.Add(Heading(title, 19));
            var grid = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            foreach (var kind in kinds)
            {
                var option = catalog[kind];
                var label = new StackPanel();
                label.Children.Add(Heading(option.Title, 15));
                label.Children.Add(Paragraph(option.Summary, 12));
                var button = new Button
                {
                    Content = label, Width = 250, MinHeight = 78, Padding = new Thickness(14, 6, 14, 6),
                    Margin = new Thickness(0, 0, 12, 10), HorizontalContentAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromRgb(39, 46, 60)), Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(65, 78, 98)), BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand, IsEnabled = option.IsAvailable, Opacity = option.IsAvailable ? 1 : 0.55,
                    ToolTip = option.Details
                };
                ToolTipService.SetShowOnDisabled(button, true);
                System.Windows.Automation.AutomationProperties.SetName(button, option.Title);
                button.Click += (_, _) => OpenProjectEditor(kind);
                grid.Children.Add(button);
            }
            panel.Children.Add(grid);
        }
    }

    private void OpenProjectEditor(ProjectEditorKind kind)
    {
        if (_window.Projects.Current == null) return;
        try
        {
            var option = new ProjectEditorCatalog(AssetManager.Default)[kind];
            if (!option.IsAvailable)
            {
                MessageBox.Show(_window, option.Details, "项目资源不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (kind is ProjectEditorKind.Campaign or ProjectEditorKind.Conquest or ProjectEditorKind.OtherBattle or ProjectEditorKind.World)
            {
                Func<AssetEntry, bool> filter = kind switch
                {
                    ProjectEditorKind.Campaign => entry => entry.Kind == AssetKind.Stage,
                    ProjectEditorKind.Conquest => entry => entry.Kind == AssetKind.Conquest,
                    ProjectEditorKind.World => entry => entry.Kind == AssetKind.World,
                    _ => entry => entry.Extension == "btl" && entry.Kind is not (AssetKind.Stage or AssetKind.Conquest)
                };
                _window.SetCurrentScene(new AssetBrowserScene(_window, filter));
                return;
            }
            UserControl scene = kind switch
            {
                ProjectEditorKind.General => new GeneralEditScene(_window),
                ProjectEditorKind.Country => new CountryEditScene(_window),
                ProjectEditorKind.Skill => new SkillEditScene(_window),
                ProjectEditorKind.Army => new ArmyEditScene(_window),
                ProjectEditorKind.ArmyBuff => new ArmyBuffEditScene(_window),
                ProjectEditorKind.BuildingFacility => new BuildingFacilityEditScene(_window),
                ProjectEditorKind.CountryTech => new CountryTechEditScene(_window),
                ProjectEditorKind.ArmyGroup => new ArmyGroupEditScene(_window),
                ProjectEditorKind.ArmyGroupEvent => new ArmyGroupEventEditScene(_window),
                ProjectEditorKind.EventBuff => new EventBuffEditScene(_window),
                ProjectEditorKind.ConquerEvent => new ConquerEventEditScene(_window),
                ProjectEditorKind.Layout => new LayoutEditScene(_window),
                ProjectEditorKind.JsonTables => new AssetBrowserScene(_window, entry => entry.Extension == "json" &&
                    entry.RelativePath.Replace('\\', '/').StartsWith("json/", StringComparison.OrdinalIgnoreCase)),
                ProjectEditorKind.GeneralProgress => new AssetBrowserScene(_window, entry => entry.RelativePath.Replace('\\', '/') is
                    "json/GeneralPromotionSettings.json" or "json/GeneralTitleSettings.json" or "json/GeneralStageSettings.json"),
                ProjectEditorKind.ArmyProgress => new AssetBrowserScene(_window, entry => entry.RelativePath.Replace('\\', '/') is
                    "json/EliteArmySettings.json" or "json/ArmyFeatureSettings.json" or "json/ArmyLevelSettings.json" or
                    "json/ArmySubTypeSettings.json" or "json/ArmyNumberSettings.json"),
                ProjectEditorKind.HeadquartersTech => new JsonTableEditScene(_window,
                    AssetManager.Default.Find("json/TechnologySettings.json")!.FullPath),
                _ => new AssetBrowserScene(_window)
            };
            _window.SetCurrentScene(scene);
        }
        catch (Exception ex) { MessageBox.Show(_window, ex.Message, "打开项目编辑器失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static TextBlock Heading(string text, double size) => new()
    {
        Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8)
    };

    private static TextBlock Paragraph(string text, double size = 13) => new()
    {
        Text = text, FontSize = size, Foreground = new SolidColorBrush(Color.FromRgb(183, 195, 209)),
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
    };

    private static Border Card(UIElement content) => new()
    {
        Child = content, Background = new SolidColorBrush(Color.FromArgb(240, 32, 39, 51)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(68, 82, 103)), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 12, 0, 12)
    };

    private static TextBox PathBox() => new()
    {
        FontSize = 14, Padding = new Thickness(10, 8, 10, 8), MinWidth = 80,
        Foreground = Brushes.White, CaretBrush = Brushes.White,
        Background = new SolidColorBrush(Color.FromRgb(20, 26, 35)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(86, 102, 126)),
        VerticalContentAlignment = VerticalAlignment.Center
    };

    private static Grid PathRow(TextBox box, string label, RoutedEventHandler handler)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(box);
        var button = ActionButton(label, handler);
        button.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(button, 1);
        row.Children.Add(button);
        return row;
    }

    private static Button ActionButton(string text, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = text, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 10, 8),
            FontSize = 13, Cursor = Cursors.Hand, Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(57, 74, 98)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(96, 117, 145))
        };
        button.Click += handler;
        return button;
    }
}
