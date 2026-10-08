using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WC4MapEditor.Views.Assist;

// Keep fields and preview side by side when space permits; use tabs at the minimum window size.
internal sealed class EditorPreviewPane : UserControl
{
    private readonly FrameworkElement _editor;
    private readonly FrameworkElement _preview;
    private readonly Grid _wide = new();
    private readonly TabControl _tabs = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly TabItem _editorTab = new() { Header = "属性编辑" };
    private readonly TabItem _previewTab = new() { Header = "游戏 UI 预览" };
    private bool? _isWide;

    public EditorPreviewPane(FrameworkElement editor, FrameworkElement preview)
    {
        _editor = editor; _preview = preview;
        _wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 425 });
        _wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        _wide.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star), MinWidth = 320 });
        var splitter = new GridSplitter { Width = 4, Background = Brushes.DimGray,
            HorizontalAlignment = HorizontalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        Grid.SetColumn(splitter, 1); _wide.Children.Add(splitter);
        _tabs.Items.Add(_editorTab); _tabs.Items.Add(_previewTab);
        SizeChanged += (_, _) => ArrangePanes(); Loaded += (_, _) => ArrangePanes();
    }

    private void ArrangePanes()
    {
        bool wide = ActualWidth >= 820;
        if (_isWide == wide) return;
        _isWide = wide;
        _wide.Children.Remove(_editor); _wide.Children.Remove(_preview);
        _editorTab.Content = null; _previewTab.Content = null;
        if (wide)
        {
            Grid.SetColumn(_editor, 0); Grid.SetColumn(_preview, 2);
            _wide.Children.Add(_editor); _wide.Children.Add(_preview); Content = _wide;
        }
        else { _editorTab.Content = _editor; _previewTab.Content = _preview; Content = _tabs; }
    }
}
