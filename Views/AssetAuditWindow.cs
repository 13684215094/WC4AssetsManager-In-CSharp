using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using WC4MapEditor.Core.Analyzers;

namespace WC4MapEditor.Views;

public sealed class AssetAuditWindow : Window
{
    private readonly string _assetsRoot;
    private readonly JsonArray? _snapshot;
    private readonly TextBox _baseRoot = new() { MinWidth = 200, VerticalContentAlignment = VerticalAlignment.Center, Foreground = Brushes.Black };
    private readonly TextBox _generalId = new() { Width = 100, VerticalContentAlignment = VerticalAlignment.Center, Foreground = Brushes.Black };
    private readonly ComboBox _severity = new() { Width = 110, ItemsSource = new[] { "全部", "error", "warning" }, SelectedIndex = 0, Foreground = Brushes.Black };
    private readonly CheckBox _maps = new() { Content = "BTL 已部署部队", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _scope = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly DataGrid _diagnostics = GridFor("Severity", "Code", "Source", "Row", "Id", "Field", "Message");
    private readonly DataGrid _references = GridFor("Source", "Row", "Id", "Field", "Target", "TargetField", "Value", "Status");
    private readonly Button _scan = new() { Content = "检查", MinWidth = 75, Margin = new Thickness(10, 0, 0, 0), Foreground = Brushes.Black };
    private readonly Button _export = new() { Content = "导出 JSON", MinWidth = 100, Margin = new Thickness(10, 0, 0, 0), IsEnabled = false, Foreground = Brushes.Black };
    private readonly CancellationTokenSource _cancel = new();
    private AssetAuditReport? _report;

    public AssetAuditWindow(Window owner, string assetsRoot, int? generalId = null, JsonArray? generalSnapshot = null)
    {
        Owner = owner;
        Title = "资源数据检查";
        Width = 1100; Height = 700; MinWidth = 650; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(37, 37, 38));
        Foreground = Brushes.White;
        _assetsRoot = assetsRoot;
        _snapshot = generalSnapshot;

        var root = new DockPanel { Margin = new Thickness(12) };
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        header.Children.Add(new TextBlock { Text = assetsRoot, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var baseRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var label = new TextBlock { Text = "基础资源目录", Width = 100, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(label, Dock.Left); baseRow.Children.Add(label);
        var browse = new Button { Content = "...", Width = 32, ToolTip = "选择匹配版本的基础 assets", Margin = new Thickness(8, 0, 0, 0), Foreground = Brushes.Black };
        DockPanel.SetDock(browse, Dock.Right); baseRow.Children.Add(browse);
        baseRow.Children.Add(_baseRoot); header.Children.Add(baseRow);
        browse.Click += (_, _) =>
        {
            var picker = new OpenFolderDialog { Title = "基础资源目录" };
            if (picker.ShowDialog(this) == true) _baseRoot.Text = picker.FolderName;
        };
        var toolbar = new WrapPanel();
        toolbar.Children.Add(_maps); toolbar.Children.Add(_scan); toolbar.Children.Add(_export);
        toolbar.Children.Add(new TextBlock { Text = "  级别 ", VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(_severity);
        toolbar.Children.Add(new TextBlock { Text = "  将领 ID ", VerticalAlignment = VerticalAlignment.Center });
        toolbar.Children.Add(_generalId);
        header.Children.Add(toolbar); header.Children.Add(_status);
        root.Children.Add(header);
        DockPanel.SetDock(_scope, Dock.Bottom); root.Children.Add(_scope);
        var tabs = new TabControl { Foreground = Brushes.Black };
        tabs.Items.Add(new TabItem { Header = "检查结果", Content = _diagnostics });
        tabs.Items.Add(new TabItem { Header = "将领被引用位置", Content = _references });
        root.Children.Add(tabs); Content = root;
        if (generalId.HasValue) { _generalId.Text = generalId.Value.ToString(); tabs.SelectedIndex = 1; }
        _severity.SelectionChanged += (_, _) => Filter();
        _generalId.TextChanged += (_, _) => Filter();
        _scan.Click += async (_, _) => await Scan();
        _export.Click += (_, _) => Export();
        Loaded += async (_, _) => await Scan();
        Closed += (_, _) => _cancel.Cancel();
    }

    private async Task Scan()
    {
        if (!_scan.IsEnabled) return;
        _scan.IsEnabled = false; _export.IsEnabled = false;
        _report = null; _diagnostics.ItemsSource = null; _references.ItemsSource = null;
        _status.Text = "检查中..."; _scope.Text = "";
        string? baseRoot = string.IsNullOrWhiteSpace(_baseRoot.Text) ? null : _baseRoot.Text.Trim();
        bool maps = _maps.IsChecked == true;
        try
        {
            var report = await Task.Run(() => new AssetRelationshipAnalyzer().Analyze(_assetsRoot,
                baseRoot, maps, _snapshot, _cancel.Token), _cancel.Token);
            if (_cancel.IsCancellationRequested) return;
            _report = report;
            _status.Text = $"{report.Tables.Count} 张表，{report.MapsChecked} 个地图，{report.Errors} 项错误，{report.Warnings} 项警告" +
                (report.UsesUnsavedGenerals ? "；将领来源：未保存的编辑快照" : "；将领来源：磁盘");
            _scope.Text = string.Join(Environment.NewLine, report.Coverage);
            _export.IsEnabled = true;
            Filter();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _status.Text = ex.Message; }
        finally { _scan.IsEnabled = true; }
    }

    private void Filter()
    {
        if (_report == null) return;
        string severity = _severity.SelectedItem as string ?? "全部";
        _diagnostics.ItemsSource = _report.Diagnostics.Where(d => severity == "全部" || d.Severity == severity).ToList();
        bool empty = string.IsNullOrWhiteSpace(_generalId.Text);
        if (!empty && (!int.TryParse(_generalId.Text, out int value) || value < 0))
        {
            _generalId.BorderBrush = Brushes.IndianRed;
            _references.ItemsSource = Array.Empty<AssetReference>();
            return;
        }
        _generalId.ClearValue(BorderBrushProperty);
        _references.ItemsSource = empty ? _report.References.Where(r =>
            r.Target == "GeneralSettings.json" || r.Target == "GeneralTitleSettings.json" && r.TargetField == "GeneralId").ToList() :
            _report.GeneralReferences(int.Parse(_generalId.Text)).ToList();
    }

    private void Export()
    {
        if (_report == null) return;
        var picker = new SaveFileDialog { Filter = "JSON 报告|*.json", FileName = "wc4-data-audit.json" };
        if (picker.ShowDialog(this) != true) return;
        try { _report.Save(picker.FileName); _status.Text = $"已导出：{picker.FileName}"; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static DataGrid GridFor(params string[] columns)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
            CanUserDeleteRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true,
            HeadersVisibility = DataGridHeadersVisibility.Column, RowHeight = 28,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Foreground = Brushes.Black
        };
        foreach (string name in columns)
            grid.Columns.Add(new DataGridTextColumn { Header = name switch
                {
                    "Severity" => "级别", "Code" => "诊断码", "Source" => "来源", "Row" => "行号",
                    "Field" => "字段", "Message" => "详情", "Target" => "目标表", "TargetField" => "目标字段",
                    "Value" => "引用值", "Status" => "状态", _ => name
                }, Binding = new Binding(name),
                Width = name is "Message" or "Source" ? new DataGridLength(240) : DataGridLength.SizeToHeader });
        return grid;
    }
}
