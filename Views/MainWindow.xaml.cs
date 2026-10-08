using ProjectFile = WC4MapEditor.Core.Assets.ProjectFiles;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.Services;
// 与 WPF 的 System.Windows.Input.CommandManager 同名，使用别名消除歧义
using CoreCommandManager = WC4MapEditor.Core.Commands.CommandManager;
using WC4MapEditor.Views;

namespace WC4MapEditor.Views;

public partial class MainWindow : Window
{
    private const int WS_BORDER = 0x800000;
    private const int WS_CAPTION = 0xC00000;
    private const int GWL_STYLE = -16;
    private const int WM_NCHITTEST = 0x84;
    private const int WM_GETMINMAXINFO = 0x24;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int BORDER_WIDTH = 8;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    private HwndSource? _hwndSource;
    private Grid _mainGrid = null!;
    private Grid _sceneHost = null!;
    private Border _windowControls = null!;
    private Button _maximizeButton = null!;
    private UserControl? _currentScene;
    private bool _isClosing;
    private bool _isSwitchingScene;
    private readonly HashSet<Key> _forwardedMapKeys = [];
    private readonly DispatcherTimer _contextTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private TextBlock _projectContext = null!;
    private TextBlock _documentState = null!;
    private Button _saveDocument = null!;

    public GameProjectSession Projects { get; } = new();

    /// <summary>
    /// 鼠标输入管理器（由 DI 注入，供各渲染场景共享）
    /// </summary>
    public MouseManager MouseManager { get; }

    /// <summary>
    /// 调试控制台（由 DI 注入，全局唯一）
    /// </summary>
    public DebugConsole DebugConsole { get; }

    /// <summary>
    /// 键盘输入管理器（由 DI 注入，供主窗口与各渲染场景共享）
    /// </summary>
    public KeyboardManager KeyboardManager { get; }

    /// <summary>格子选择器（由 DI 注入，各渲染场景共享同一选择状态）</summary>
    public HexSelector HexSelector { get; }

    /// <summary>控制台命令管理器（由 DI 注入）</summary>
    public CoreCommandManager CommandManager { get; }

    /// <summary>编辑模式管理器（由 DI 注入）</summary>
    public EditModeManager EditModeManager { get; }

    public MainWindow(MouseManager mouseManager, DebugConsole debugConsole, KeyboardManager keyboardManager,
                      HexSelector hexSelector, CoreCommandManager commandManager, EditModeManager editModeManager)
    {
        MouseManager = mouseManager;
        DebugConsole = debugConsole;
        KeyboardManager = keyboardManager;
        HexSelector = hexSelector;
        CommandManager = commandManager;
        EditModeManager = editModeManager;
        InitializeComponent();
        SetupWindow();
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        Deactivated += (_, _) => { KeyboardManager.ResetState(); _forwardedMapKeys.Clear(); };
        PreviewLostKeyboardFocus += (_, args) =>
        {
            if (_currentScene is RenderSceneBase map && map.IsMapKeyboardTarget(args.OldFocus) && !map.IsMapKeyboardTarget(args.NewFocus))
            { KeyboardManager.ResetState(); _forwardedMapKeys.Clear(); }
        };
        _contextTimer.Tick += (_, _) => { _contextTimer.Stop(); UpdateDocumentContext(); };
    }

    private void SetupWindow()
    {
        Title = ConfigManager.Instance.GetText("TitleBarText", "WC4 Map Editor");
        Width = 1250;
        Height = 700;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = true;

        _mainGrid = new Grid();
        _mainGrid.Margin = new Thickness(10);
        _mainGrid.Effect = new DropShadowEffect
        {
            BlurRadius = 15,
            ShadowDepth = 5,
            Opacity = 0.6,
            Color = Colors.Black
        };
        _mainGrid.Background = new SolidColorBrush(ColorFromInt(ConfigManager.Instance.GetThemeColor()));

        Content = _mainGrid;
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _mainGrid.Children.Add(layout);
        CreateWindowControls();
        layout.Children.Add(_windowControls);
        _sceneHost = new Grid();
        _sceneHost.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler((_, _) => QueueDocumentContext()));
        _sceneHost.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, _) => QueueDocumentContext()));
        _sceneHost.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => QueueDocumentContext()));
        _sceneHost.AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler((_, _) => QueueDocumentContext()), true);
        Grid.SetRow(_sceneHost, 1);
        layout.Children.Add(_sceneHost);
        StateChanged += (_, _) => UpdateWindowControlsState();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewKeyUp += MainWindow_PreviewKeyUp;
    }

    private void CreateWindowControls()
    {
        _windowControls = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(25, 25, 30)),
            Padding = new Thickness(8, 3, 8, 3)
        };
        _windowControls.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else if (e.LeftButton == MouseButtonState.Pressed && WindowState == WindowState.Normal) DragMove();
        };
        var titleBar = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(CreateWindowButton("−", "最小化窗口", (_, _) => MinimizeWindow()));
        _maximizeButton = CreateWindowButton("□", "最大化/全屏（F11）", (_, _) => ToggleMaximize());
        buttons.Children.Add(_maximizeButton);
        var closeButton = CreateWindowButton("×", "关闭窗口", (_, _) => CloseApplication());
        closeButton.Foreground = new SolidColorBrush(Color.FromRgb(255, 150, 150));
        buttons.Children.Add(closeButton);
        DockPanel.SetDock(buttons, Dock.Right);
        titleBar.Children.Add(buttons);
        var documentActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _documentState = new TextBlock { Foreground = Brushes.LightGray, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        _saveDocument = new Button { Content = "保存 (Ctrl+S)", Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(4, 0, 10, 0) };
        _saveDocument.Click += (_, _) => SaveCurrentDocument();
        documentActions.Children.Add(_documentState); documentActions.Children.Add(_saveDocument);
        DockPanel.SetDock(documentActions, Dock.Right); titleBar.Children.Add(documentActions);
        _projectContext = new TextBlock
        {
            Text = Title,
            Focusable = true,
            Foreground = Brushes.White,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 12, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        KeyboardNavigation.SetIsTabStop(_projectContext, false);
        titleBar.Children.Add(_projectContext);
        _windowControls.Child = titleBar;
    }

    private void QueueDocumentContext()
    {
        _contextTimer.Stop(); _contextTimer.Start();
    }

    private void UpdateDocumentContext()
    {
        var project = Projects.Current;
        _projectContext.Text = project == null ? Title : $"{IOPath.GetFileName(project.SourcePath.TrimEnd(IOPath.DirectorySeparatorChar))} · {Title}";
        _projectContext.ToolTip = project == null ? Title : $"来源（只读）：{project.SourcePath}\n增量输出：{project.OutputRoot}";
        bool editable = _currentScene is IUnsavedScene;
        bool dirty = (_currentScene as IUnsavedScene)?.HasUnsavedChanges == true;
        _documentState.Text = dirty ? "有未保存修改 / 待修正输入" : editable ? "无未保存修改" : "";
        _documentState.Foreground = dirty ? Brushes.LightGoldenrodYellow : Brushes.LightGray;
        _saveDocument.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveCurrentDocument()
    {
        if (_isClosing || _isSwitchingScene || _currentScene is not IUnsavedScene document || IsDialogOverlayOpen()) return;
        bool saved = CommitInputsAndRestoreFocus(document.SaveChanges);
        UpdateDocumentContext();
        if (saved) _documentState.Text = "已保存到输出目录";
    }

    private T CommitInputsAndRestoreFocus<T>(Func<T> action)
    {
        var previous = Keyboard.FocusedElement;
        try
        {
            // Move logical focus as well, so fields that commit on LostFocus are included.
            _projectContext.Focus();
            return action();
        }
        finally
        {
            if (IsActive && !IsDialogOverlayOpen() && previous is UIElement { IsVisible: true, IsEnabled: true } element &&
                Window.GetWindow(element) == this && (Keyboard.FocusedElement == null ||
                    ReferenceEquals(Keyboard.FocusedElement, _projectContext) || ReferenceEquals(Keyboard.FocusedElement, previous)))
                Keyboard.Focus(element);
        }
    }

    private static Button CreateWindowButton(string content, string tooltip, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = content,
            Width = 34,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(1),
            FontSize = 17,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = tooltip
        };
        button.Click += handler;
        System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        return button;
    }

    private void UpdateWindowControlsState()
    {
        if (_windowControls == null) return;
        bool maximized = WindowState == WindowState.Maximized;
        _maximizeButton.Content = maximized ? "❐" : "□";
        _maximizeButton.ToolTip = maximized ? "还原窗口（F11）" : "最大化/全屏（F11）";
        System.Windows.Automation.AutomationProperties.SetName(_maximizeButton, (string)_maximizeButton.ToolTip);
        _mainGrid.Margin = maximized ? new Thickness(0) : new Thickness(10);
        _mainGrid.Effect = maximized ? null : new DropShadowEffect
        {
            BlurRadius = 15,
            ShadowDepth = 5,
            Opacity = 0.6,
            Color = Colors.Black
        };
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        RenderSceneManager.Instance.SceneSwitchRequested += OnSceneSwitchRequested;

        double targetTop = Top;
        double screenBottom = SystemParameters.WorkArea.Bottom;

        var topAnim = new DoubleAnimation(screenBottom, targetTop, TimeSpan.FromMilliseconds(500))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
        { FillBehavior = FillBehavior.Stop };

        BeginAnimation(TopProperty, topAnim);
        BeginAnimation(OpacityProperty, opacityAnim);

        RemoveWindowBorder();
        ShowOpeningAnimation();
    }

    private void ShowOpeningAnimation()
    {
        var animType = ConfigManager.Instance.GetOpeningAnimationType();
        switch (animType)
        {
            case ConfigManager.OpeningAnimationType.None:
                SwitchToBeginScene();
                break;
            case ConfigManager.OpeningAnimationType.Logo:
                ShowLogoAnimationAsync();
                break;
            case ConfigManager.OpeningAnimationType.Token:
                ShowTokenAnimationAsync();
                break;
        }
    }

    private async void ShowLogoAnimationAsync()
    {
        string logoPath = IOPath.Combine(ConfigManager.Instance.GetLogoPath(), "Logo.png");
        if (!ProjectFile.Exists(logoPath)) { SwitchToBeginScene(); return; }

        var logoWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Width = 256, Height = 256,
            Topmost = true,
            ShowInTaskbar = false
        };

        var image = new Image { Stretch = Stretch.Uniform, RenderTransformOrigin = new System.Windows.Point(0.5, 0.5) };
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
        }
        catch { SwitchToBeginScene(); return; }

        var grid = new Grid();
        grid.Children.Add(image);
        logoWindow.Content = grid;

        var rotateTransform = new RotateTransform(0);
        image.RenderTransform = rotateTransform;

        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;
        double groundY = screenHeight - 40 - logoWindow.Height;
        double centerY = (screenHeight - logoWindow.Height) / 2;

        logoWindow.Left = (screenWidth - logoWindow.Width) / 2;
        logoWindow.Top = -logoWindow.Height;
        logoWindow.Show();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        logoWindow.BeginAnimation(OpacityProperty, fadeIn);

        double gravity = 0.98, bounceDamping = 0.75, springStiffness = 0.06, springDamping = 0.92;
        double positionY = -logoWindow.Height, velocityY = 25;
        bool isBouncing = true, hasBounced = false;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (s, e) =>
        {
            if (isBouncing)
            {
                velocityY += gravity;
                positionY += velocityY;
                if (positionY >= groundY && velocityY > 0)
                {
                    positionY = groundY;
                    if (!hasBounced)
                    {
                        velocityY = -velocityY * bounceDamping;
                        hasBounced = true;
                        var rotAnim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(2400))
                        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                        rotateTransform.BeginAnimation(RotateTransform.AngleProperty, rotAnim);
                    }
                }
                if (hasBounced && positionY < centerY && velocityY < 0) isBouncing = false;
            }
            else
            {
                double disp = centerY - positionY;
                velocityY += disp * springStiffness;
                velocityY *= springDamping;
                positionY += velocityY;
                if (Math.Abs(disp) < 0.5 && Math.Abs(velocityY) < 0.5)
                {
                    positionY = centerY;
                    timer.Stop();
                    var stay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    stay.Tick += (s2, e2) =>
                    {
                        stay.Stop();
                        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                        fadeOut.Completed += (s3, e3) => logoWindow.Close();
                        logoWindow.BeginAnimation(OpacityProperty, fadeOut);
                    };
                    stay.Start();
                }
            }
            logoWindow.Top = positionY;
        };
        timer.Start();
    }

    private async void ShowTokenAnimationAsync()
    {
        string tokenText = ConfigManager.Instance.GetMainToken();
        var wcConfig = ConfigManager.Instance.GetWordCloudConfig();
        if (string.IsNullOrWhiteSpace(tokenText)) { SwitchToBeginScene(); return; }

        var tokenWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            WindowState = WindowState.Maximized,
            Topmost = true,
            ShowInTaskbar = false
        };

        var canvas = new Canvas();
        double sw = SystemParameters.PrimaryScreenWidth;
        double sh = SystemParameters.PrimaryScreenHeight;
        canvas.Width = sw; canvas.Height = sh;

        var mainViewbox = new Viewbox { Stretch = Stretch.Uniform, Width = sw - 200, Height = sh * 0.3 };
        Canvas.SetLeft(mainViewbox, 100);
        Canvas.SetTop(mainViewbox, (sh - mainViewbox.Height) / 2);

        var mainTb = new TextBlock
        {
            Text = tokenText, FontSize = 100, FontWeight = FontWeights.Black,
            Foreground = Brushes.White, TextWrapping = TextWrapping.NoWrap,
            TextAlignment = TextAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 0, Opacity = 0.5 }
        };
        mainViewbox.Child = mainTb;
        canvas.Children.Add(mainViewbox);
        tokenWindow.Content = canvas;
        tokenWindow.Show();

        var mainFadeIn = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(1));
        var mainFadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1))
        { BeginTime = TimeSpan.FromSeconds(1 + wcConfig.AnimationKeep) };
        mainViewbox.BeginAnimation(UIElement.OpacityProperty, mainFadeIn);
        mainViewbox.BeginAnimation(UIElement.OpacityProperty, mainFadeOut);

        var validTokens = wcConfig.Tokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
        var random = new Random();
        double interval = wcConfig.AnimationKeep / Math.Max(validTokens.Length, 1);
        if (interval < 0.1) interval = 0.1;

        int idx = 0;
        var tokenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
        tokenTimer.Tick += (s, e) =>
        {
            if (idx >= validTokens.Length) { tokenTimer.Stop(); return; }
            var tb = new TextBlock
            {
                Text = validTokens[idx], FontSize = 18, Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Opacity = 0,
                Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.8, Color = Colors.Black }
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            tb.Arrange(new Rect(tb.DesiredSize));
            double margin = 50;
            Canvas.SetLeft(tb, random.NextDouble() * (sw - tb.ActualWidth - margin) + margin);
            Canvas.SetTop(tb, random.NextDouble() * (sh - tb.ActualHeight - margin) + margin);
            Panel.SetZIndex(tb, -1);
            canvas.Children.Add(tb);
            var fi = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.5))
            { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            var fo = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.5))
            { BeginTime = TimeSpan.FromSeconds(1.5), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
            fo.Completed += (s2, e2) => canvas.Children.Remove(tb);
            tb.BeginAnimation(UIElement.OpacityProperty, fi);
            tb.BeginAnimation(UIElement.OpacityProperty, fo);
            idx++;
        };
        tokenTimer.Start();

        var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1 + wcConfig.AnimationKeep + 1) };
        closeTimer.Tick += (s, e) => { closeTimer.Stop(); tokenWindow.Close(); };
        closeTimer.Start();
    }

    private void SwitchToBeginScene()
    {
        var beginScene = new BeginScene(this);
        SetCurrentScene(beginScene);
    }

    public void ShowProjectLoader()
    {
        if (!ConfirmAllUnsavedChanges("切换项目")) return;
        SetCurrentScene(new BeginScene(this, showProjectLoader: true));
    }

    public void CloseProject()
    {
        if (!ConfirmAllUnsavedChanges("关闭项目")) return;
        Projects.Activate(null);
        SwitchToBeginScene();
    }

    private void RemoveWindowBorder()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(hwnd, GWL_STYLE);
        style &= ~WS_BORDER;
        style &= ~WS_CAPTION;
        SetWindowLong(hwnd, GWL_STYLE, style);
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var monitor = MonitorFromWindow(hwnd, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                // Borderless windows still maximize within the current monitor's taskbar work area.
                var limits = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                limits.MaxPosition.X = info.Work.Left - info.Monitor.Left;
                limits.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
                limits.MaxSize.X = info.Work.Right - info.Work.Left;
                limits.MaxSize.Y = info.Work.Bottom - info.Work.Top;
                Marshal.StructureToPtr(limits, lParam, false);
                handled = true;
            }
        }
        else if (msg == WM_NCHITTEST && WindowState == WindowState.Normal)
        {
            long position = lParam.ToInt64();
            var mp = new Point((short)(position & 0xFFFF), (short)((position >> 16) & 0xFFFF));
            var wp = PointFromScreen(mp);
            bool l = wp.X < BORDER_WIDTH, r = wp.X > ActualWidth - BORDER_WIDTH;
            bool t = wp.Y < BORDER_WIDTH, b = wp.Y > ActualHeight - BORDER_WIDTH;
            if (t && l) { handled = true; return new IntPtr(HTTOPLEFT); }
            if (t && r) { handled = true; return new IntPtr(HTTOPRIGHT); }
            if (b && l) { handled = true; return new IntPtr(HTBOTTOMLEFT); }
            if (b && r) { handled = true; return new IntPtr(HTBOTTOMRIGHT); }
            if (l) { handled = true; return new IntPtr(HTLEFT); }
            if (r) { handled = true; return new IntPtr(HTRIGHT); }
            if (t) { handled = true; return new IntPtr(HTTOP); }
            if (b) { handled = true; return new IntPtr(HTBOTTOM); }
        }
        return IntPtr.Zero;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing) { base.OnClosing(e); return; }
        e.Cancel = true;
        if (_isSwitchingScene) return;
        if (!ConfirmAllUnsavedChanges("关闭窗口")) return;
        _isClosing = true;
        _sceneHost.IsEnabled = false;
        _saveDocument.IsEnabled = false;
        RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        var st = new ScaleTransform(1, 1);
        RenderTransform = st;
        var ea = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var sx = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ea };
        var sy = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ea };
        var oa = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ea };
        oa.Completed += (s, ev) => { RenderTransform = null; Application.Current.Shutdown(); };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        BeginAnimation(OpacityProperty, oa);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _contextTimer.Stop();
        if (_currentScene is IDisposable d) d.Dispose();
        _currentScene = null;
    }

    private void MainWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (!e.IsRepeat && !IsDialogOverlayOpen()) ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (_isClosing || _isSwitchingScene) { e.Handled = true; return; }
        if (IsDialogOverlayOpen()) return;
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && _currentScene is IUnsavedScene)
        {
            if (!e.IsRepeat) SaveCurrentDocument();
            e.Handled = true; return;
        }
        if (_currentScene is not RenderSceneBase { ReceivesMapKeyboardInput: true } || Keyboard.Modifiers != ModifierKeys.None) return;
        KeyboardManager.SetModifierState(KeyModifiers.None);

        if (e.Key == Key.F3)
        {
            KeyboardManager.ProcessKeyDown((int)Key.F3);
            _forwardedMapKeys.Add(Key.F3);
            e.Handled = true;
        }
        else if (e.Key == Key.Tab)
        {
            KeyboardManager.ProcessKeyDown((int)Key.Tab);
            _forwardedMapKeys.Add(Key.Tab);
            e.Handled = true;
        }
    }

    private void MainWindow_PreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (_currentScene is IUnsavedScene) QueueDocumentContext();
        if (e.Key == Key.F11)
        {
            e.Handled = true;
            return;
        }
        if (_forwardedMapKeys.Remove(e.Key))
        {
            KeyboardManager.ProcessKeyUp((int)e.Key);
            e.Handled = true;
        }
    }

    /// <summary>
    /// 当前是否有叠加在主窗口内的对话框覆盖层。
    /// <para>
    /// DoubleInputDialog / SingleInputDialog / ObjectPropertiesDialog / NotificationDialog /
    /// ConfirmDialog 都是把 Border 挂进主窗口根面板的“伪对话框”，不是独立 Window。
    /// 因此它们内部控件的 Tab 焦点切换会被窗口级 PreviewKeyDown 抢先拦截，
    /// 需要在这里临时放行。各对话框统一以 ZIndex = 9999 挂载，以此作为判定依据。
    /// </para>
    /// </summary>
    private bool IsDialogOverlayOpen()
    {
        Panel? rootPanel = Content as Panel;
        if (rootPanel == null && Content is FrameworkElement contentElement)
            rootPanel = contentElement.Parent as Panel;

        if (rootPanel == null) return false;

        foreach (UIElement child in rootPanel.Children)
        {
            if (Panel.GetZIndex(child) >= 9999) return true;
        }
        return false;
    }

    private void OnSceneSwitchRequested(object? sender, SceneSwitchRequestEventArgs e)
    {
        if (Projects.Current == null) return;
        Dispatcher.Invoke(() =>
        {
            if (_isClosing || _isSwitchingScene) return;
            var newScene = CreateRenderScene(e.TargetSceneType, e.MapFilePath);
            if (newScene != null)
            {
                newScene.AssignSceneId(e.TargetSceneId);
                RenderSceneManager.Instance.ActivateScene(e.TargetSceneId);
                SetCurrentScene(newScene);
            }
        });
    }

    private RenderSceneBase? CreateRenderScene(RenderSceneType sceneType, string mapFilePath)
    {
        return sceneType switch
        {
            RenderSceneType.Test => new MapRenderScene(this, mapFilePath),
            RenderSceneType.Stage => new StageRenderScene(this, mapFilePath),
            RenderSceneType.Conquest => new ConquestRenderScene(this, mapFilePath),
            RenderSceneType.Campaign => new StageRenderScene(this, mapFilePath),
            _ => null
        };
    }

    public void SetCurrentScene(UserControl scene)
    {
        if (ReferenceEquals(scene, _currentScene)) return;
        if (_isClosing || _isSwitchingScene)
        {
            if (scene is IDisposable pending) pending.Dispose();
            return;
        }
        if (Projects.Current == null && scene is not BeginScene)
        {
            if (scene is IDisposable rejected) rejected.Dispose();
            return;
        }
        if (_currentScene is IUnsavedScene document &&
            !(_currentScene is RenderSceneBase && scene is RenderSceneBase) &&
            !ConfirmUnsavedDocument(document, "离开编辑器"))
        {
            if (scene is IDisposable cancelled) cancelled.Dispose();
            return;
        }
        KeyboardManager.ResetState(); _forwardedMapKeys.Clear();
        QueueDocumentContext();
        if (_currentScene != null)
        {
            // The leave decision is final; the fading editor must no longer accept edits or another switch.
            _isSwitchingScene = true;
            _sceneHost.IsEnabled = false;
            _saveDocument.IsEnabled = false;
            // 如果是渲染场景（战役/征服等），显示加载画面
            if (scene is RenderSceneBase)
            {
                ShowLoadingAndSwitchScene(scene);
            }
            else
            {
                FadeOutScene(() =>
                {
                    _sceneHost.Children.Remove(_currentScene);
                    if (_currentScene is IDisposable d) d.Dispose();
                    _currentScene = scene;
                    CompleteSceneSwitch();
                    _sceneHost.Children.Add(scene);
                    FadeInScene(scene);
                });
            }
        }
        else
        {
            _currentScene = scene;
            _sceneHost.Children.Add(scene);
            FadeInScene(scene);
        }
    }

    public bool ConfirmUnsavedDocument(IUnsavedScene document, string action)
        => CommitInputsAndRestoreFocus(() => ConfirmUnsavedDocumentCore(document, action));

    private bool ConfirmUnsavedDocumentCore(IUnsavedScene document, string action)
    {
        if (!document.HasUnsavedChanges) return true;
        var choice = MessageBox.Show(this, $"存在未保存的修改。是否保存后{action}？\n是：保存；否：放弃修改；取消：继续编辑。",
            "未保存的修改", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.Yes) return document.SaveChanges();
        document.DiscardChanges();
        return true;
    }

    public bool ConfirmAllUnsavedChanges(string action)
        => CommitInputsAndRestoreFocus(() => ConfirmAllUnsavedChangesCore(action));

    private bool ConfirmAllUnsavedChangesCore(string action)
    {
        var document = _currentScene as IUnsavedScene;
        var manager = RenderSceneManager.Instance;
        if (document?.HasUnsavedChanges != true && !manager.HasUnsavedChanges) return true;
        var choice = MessageBox.Show(this, $"当前编辑器或地图场景存在未保存的修改。是否全部保存后{action}？\n是：保存；否：放弃修改；取消：继续编辑。",
            "未保存的修改", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (choice == MessageBoxResult.Cancel) return false;
        if (choice == MessageBoxResult.No)
        {
            document?.DiscardChanges();
            foreach (var scene in manager.AllScenes.ToArray()) manager.DiscardSceneChanges(scene.SceneId);
            return true;
        }
        if (document?.HasUnsavedChanges == true && !document.SaveChanges()) return false;
        foreach (var scene in manager.AllScenes.Where(s => s.HasUnsavedChanges).ToArray())
        {
            string path = scene.MapFilePath;
            if (string.IsNullOrEmpty(path))
            {
                var picker = new SaveFileDialog
                {
                    Title = $"保存地图：{scene.SceneName}",
                    Filter = scene.SceneType == RenderSceneType.Test ? "世界地图|*.bin" : "关卡地图|*.btl",
                    InitialDirectory = scene.Project?.AssetsRoot ?? Environment.CurrentDirectory
                };
                if (picker.ShowDialog(this) != true) return false;
                path = picker.FileName;
            }
            try { manager.SaveScene(scene.SceneId, path); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, $"保存失败：{scene.SceneName}", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        return true;
    }

    private void ShowLoadingAndSwitchScene(UserControl newScene)
    {
        // 显示加载图像
        string loadingImagePath = ConfigManager.Instance.GetLoadingBackgroundImagePath();
        Image? loadingImage = null;

        if (!string.IsNullOrEmpty(loadingImagePath) && ProjectFile.Exists(loadingImagePath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(ProjectFile.ResolveRead(loadingImagePath), UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                loadingImage = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0
                };
                _sceneHost.Children.Add(loadingImage);
                loadingImage.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            }
            catch { }
        }

        // 淡出当前场景
        if (_currentScene != null)
        {
            var fo = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fo.Completed += (s, e) =>
            {
                _sceneHost.Children.Remove(_currentScene);
                if (_currentScene is IDisposable d) d.Dispose();

                // 切换到新场景（先隐藏）
                newScene.Opacity = 0;
                _currentScene = newScene;
                CompleteSceneSwitch();
                _sceneHost.Children.Add(newScene);
                QueueDocumentContext();

                // 给新场景一点加载时间，然后淡出加载画面，显示新场景
                var loadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                loadTimer.Tick += (s2, e2) =>
                {
                    loadTimer.Stop();

                    // 淡入新场景
                    newScene.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

                    // 淡出并移除加载图像
                    if (loadingImage != null)
                    {
                        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                        fadeOut.Completed += (s3, e3) =>
                        {
                            if (_sceneHost.Children.Contains(loadingImage))
                                _sceneHost.Children.Remove(loadingImage);
                        };
                        loadingImage.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    }
                };
                loadTimer.Start();
            };
            _currentScene.BeginAnimation(UIElement.OpacityProperty, fo);
        }
        else
        {
            newScene.Opacity = 0;
            _currentScene = newScene;
            CompleteSceneSwitch();
            _sceneHost.Children.Add(newScene);
            QueueDocumentContext();
            newScene.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

            if (loadingImage != null)
            {
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                fadeOut.Completed += (s3, e3) =>
                {
                    if (_sceneHost.Children.Contains(loadingImage))
                        _sceneHost.Children.Remove(loadingImage);
                };
                loadingImage.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }
    }

    private void FadeInScene(UserControl scene)
    {
        QueueDocumentContext();
        scene.Opacity = 0;
        scene.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
    }

    private void CompleteSceneSwitch()
    {
        _isSwitchingScene = false;
        _sceneHost.IsEnabled = true;
        _saveDocument.IsEnabled = true;
    }

    private void FadeOutScene(Action onComplete)
    {
        if (_currentScene == null) { onComplete(); return; }
        var fo = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
        fo.Completed += (s, e) => onComplete();
        _currentScene.BeginAnimation(UIElement.OpacityProperty, fo);
    }

    #region 相机控制（转发到当前渲染场景）

    /// <summary>
    /// 把当前渲染场景的相机中心移动到指定格子（行列坐标）。
    /// 当前不是渲染场景、或坐标越界时返回 false。
    /// </summary>
    public bool MoveCameraToHex(int col, int row)
        => _currentScene is RenderSceneBase scene && scene.MoveCameraToHex(col, row);

    /// <summary>
    /// 把当前渲染场景的相机中心移动到指定格子（格子序号 = row * MapWidth + col）。
    /// 当前不是渲染场景、或序号越界时返回 false。
    /// </summary>
    public bool MoveCameraToHexIndex(int hexIndex)
        => _currentScene is RenderSceneBase scene && scene.MoveCameraToHexIndex(hexIndex);

    #endregion

    public void CloseApplication() => Close();
    public void MinimizeWindow() => WindowState = WindowState.Minimized;
    public void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateWindowControlsState();
    }
    public void ReturnToMainScene() => SwitchToBeginScene();

    /// <summary>
    /// 从 TacticalMapEditScene 返回：有来源目录（= 从资源管理器打开）就回资源管理器，否则回主场景
    /// </summary>
    public void ReturnFromTacticalEditor(string? sourceDirectory)
    {
        if (!string.IsNullOrEmpty(sourceDirectory))
        {
            // 来源是资源管理器：新建 AssetBrowserScene（自动默认定位到上次/对应分类即可，因为缓存已是全局的 AssetManager.Default）
            var scene = new AssetBrowserScene(this);
            SetCurrentScene(scene);
        }
        else
        {
            ReturnToMainScene();
        }
    }

    private static Color ColorFromInt(int val) =>
        Color.FromRgb(
            (byte)((val >> 16) & 0xFF),
            (byte)((val >> 8) & 0xFF),
            (byte)(val & 0xFF));
}
