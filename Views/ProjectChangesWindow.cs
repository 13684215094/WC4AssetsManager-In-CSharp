using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WC4MapEditor.Core.Assets;

namespace WC4MapEditor.Views;

internal sealed class ProjectChangesWindow : Window
{
    private sealed record Row(ProjectOutputChange Change)
    {
        public string Path => Change.RelativePath;
        public string Status => Change.Kind switch
        {
            ProjectOutputChangeKind.Added => "输出新增", ProjectOutputChangeKind.Modified => "输出覆盖（内容不同）",
            ProjectOutputChangeKind.Identical => "与来源字节相同", _ => "无法比较"
        };
        public long Bytes => Change.OutputBytes;
    }
    private readonly GameProjectWorkspace _project;
    private readonly CancellationTokenSource _cancel = new();
    private readonly ListView _list = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox _search = new() { Width = 260, Margin = new Thickness(6), ToolTip = "按相对路径搜索" };
    private readonly ComboBox _kind = new() { Width = 170, Margin = new Thickness(6) };
    private readonly Button _refresh = new() { Content = "重新比较输出", Margin = new Thickness(6), Padding = new Thickness(10, 5, 10, 5) };
    private IReadOnlyList<ProjectOutputChange> _changes = [];
    private bool _closed;

    public ProjectChangesWindow(Window owner, GameProjectWorkspace project)
    {
        Owner = owner; _project = project; Title = "项目输出变更";
        Width = 980; Height = 640; MinWidth = 650; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(28, 32, 37));
        var root = new DockPanel { Margin = new Thickness(14) };
        var note = new TextBlock { Text = $"来源：{project.SourcePath}\n输出：{project.OutputRoot}\n仅核对已写入输出的文件；内存中未保存的草稿不在此清单中。比较按需进行，不修改文件。",
            Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        var toolbar = new WrapPanel(); toolbar.Children.Add(_search);
        _kind.ItemsSource = new[] { "全部输出", "内容不同 / 新增", "与来源相同", "无法比较" }; _kind.SelectedIndex = 0;
        toolbar.Children.Add(_kind); toolbar.Children.Add(_refresh);
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);
        DockPanel.SetDock(_details, Dock.Bottom); root.Children.Add(_details);
        var columns = new GridView();
        columns.Columns.Add(new GridViewColumn { Header = "状态", Width = 180, DisplayMemberBinding = new Binding(nameof(Row.Status)) });
        columns.Columns.Add(new GridViewColumn { Header = "输出字节", Width = 100, DisplayMemberBinding = new Binding(nameof(Row.Bytes)) });
        columns.Columns.Add(new GridViewColumn { Header = "相对路径", Width = 570, DisplayMemberBinding = new Binding(nameof(Row.Path)) });
        _list.View = columns; root.Children.Add(_list); Content = root;
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is Row row)
                _details.Text = $"{row.Status}\n来源：{row.Change.SourcePath}\n输出：{row.Change.OutputPath}" +
                    (row.Change.Error == null ? "" : $"\n{row.Change.Error}");
            else _details.Text = "";
        };
        _refresh.Click += async (_, _) => await Scan();
        _search.TextChanged += (_, _) => Filter(); _kind.SelectionChanged += (_, _) => Filter();
        Loaded += async (_, _) => await Scan();
        Closed += (_, _) => { _closed = true; _cancel.Cancel(); _cancel.Dispose(); };
    }

    private async Task Scan()
    {
        if (!_refresh.IsEnabled || _closed) return;
        _refresh.IsEnabled = false; _status.Text = "正在比较输出与来源...";
        try
        {
            var token = _cancel.Token;
            var changes = await Task.Run(() => ProjectOutputChanges.Scan(_project, token), token);
            if (_closed) return;
            _changes = changes; Filter();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!_closed) _status.Text = $"比较失败：{ex.Message}"; }
        finally { if (!_closed) _refresh.IsEnabled = true; }
    }

    private void Filter()
    {
        string query = _search.Text.Trim();
        var filtered = _changes.Where(change => query.Length == 0 || change.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(change => _kind.SelectedIndex switch
            {
                1 => change.Kind is ProjectOutputChangeKind.Added or ProjectOutputChangeKind.Modified,
                2 => change.Kind == ProjectOutputChangeKind.Identical, 3 => change.Kind == ProjectOutputChangeKind.Unreadable, _ => true
            }).Select(change => new Row(change)).ToArray();
        string? selected = (_list.SelectedItem as Row)?.Path;
        _list.ItemsSource = filtered;
        _list.SelectedItem = filtered.FirstOrDefault(row => row.Path == selected);
        if (_list.SelectedItem == null) _details.Text = "";
        _status.Text = $"显示 {filtered.Length} / {_changes.Count} 项 · 新增 {_changes.Count(row => row.Kind == ProjectOutputChangeKind.Added)} · " +
            $"覆盖 {_changes.Count(row => row.Kind == ProjectOutputChangeKind.Modified)} · 相同 {_changes.Count(row => row.Kind == ProjectOutputChangeKind.Identical)} · 无法比较 {_changes.Count(row => row.Kind == ProjectOutputChangeKind.Unreadable)}";
    }
}
