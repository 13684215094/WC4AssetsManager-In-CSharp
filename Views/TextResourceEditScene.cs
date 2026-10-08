using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Views;

public sealed class TextResourceEditScene : UserControl, IUnsavedScene
{
    private readonly TextResourceDocument _document;
    private readonly TextBox _text;
    public bool HasUnsavedChanges { get { _document.Text = _text.Text; return _document.HasChanges; } }

    public TextResourceEditScene(MainWindow window, string path)
    {
        _document = new(path);
        var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(31, 31, 31)) };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12) };
        var back = new Button { Content = "返回", Padding = new Thickness(12, 5, 12, 5) };
        back.Click += (_, _) => window.ReturnToMainScene();
        var save = new Button { Content = "保存", Margin = new Thickness(8, 0, 12, 0), Padding = new Thickness(12, 5, 12, 5) };
        save.Click += (_, _) => SaveChanges();
        toolbar.Children.Add(back);
        toolbar.Children.Add(save);
        toolbar.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(path), Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        _text = new TextBox
        {
            Text = _document.Text, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"), FontSize = 14, Background = Brushes.Transparent,
            Foreground = Brushes.White, Padding = new Thickness(12), BorderThickness = new Thickness(0)
        };
        root.Children.Add(_text);
        Content = root;
    }

    public bool SaveChanges()
    {
        try { _document.Text = _text.Text; _document.Save(); return true; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "保存失败，修改仍保留", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }

    public void DiscardChanges() { _document.Discard(); _text.Text = _document.Text; }
}
