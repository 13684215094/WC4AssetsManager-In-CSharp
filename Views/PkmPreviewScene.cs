using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Rendering.Imaging;
using WC4MapEditor.Views.Assist;

namespace WC4MapEditor.Views;

public sealed class PkmPreviewScene : UserControl
{
    public PkmPreviewScene(MainWindow window, string path)
    {
        var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(31, 31, 31)) };
        var toolbar = new WrapPanel { Margin = new Thickness(12) };
        var back = new Button { Content = "返回", Padding = new Thickness(12, 5, 12, 5) };
        back.Click += (_, _) => window.ReturnToMainScene();
        toolbar.Children.Add(back);
        using var bitmap = ProjectImageDecoder.Decode(ProjectFiles.ReadAllBytes(path));
        toolbar.Children.Add(new TextBlock
        {
            Text = $"  {System.IO.Path.GetFileName(path)} · {bitmap.Width} × {bitmap.Height} · PKM / ETC1 RGB",
            Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var note = new TextBlock { Text = "只读预览。颜色块和遮罩块分别显示；暂不支持 PKM 编码写回或游戏组合效果。",
            Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        root.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Image { Source = SkiaBitmapSource.Create(bitmap), Stretch = Stretch.None, Margin = new Thickness(12) } });
        Content = root;
    }
}
