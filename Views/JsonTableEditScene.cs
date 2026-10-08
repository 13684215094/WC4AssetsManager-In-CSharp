using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Views.Assist;

namespace WC4MapEditor.Views;

public sealed class JsonTableEditScene : UserControl, IUnsavedScene
{
    private sealed record RowItem(int Index, string Label);
    private sealed class FieldInput(string name, TextBox text, CheckBox isNull)
    {
        public string Name { get; } = name;
        public TextBox Text { get; } = text;
        public CheckBox IsNull { get; } = isNull;
        public bool Changed { get; set; }
    }

    private readonly MainWindow _window;
    private readonly JsonTableDocument _document;
    private readonly ListBox _rows = new() { DisplayMemberPath = nameof(RowItem.Label), Background = Brushes.Transparent,
        Foreground = Brushes.White, BorderThickness = new Thickness(0) };
    private readonly TextBox _search = new() { Margin = new Thickness(8), Padding = new Thickness(6) };
    private readonly StackPanel _fields = new() { Margin = new Thickness(16) };
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray, Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap };
    private readonly List<FieldInput> _inputs = [];
    private int? _selected;
    private bool _refreshing;
    private readonly HeadquartersTechPreview? _techPreview;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    public bool HasUnsavedChanges => _document.HasChanges || _inputs.Any(input => input.Changed);

    public JsonTableEditScene(MainWindow window, string path, int? preferredId = null)
    {
        _window = window;
        _document = new JsonTableDocument(path);
        if (System.IO.Path.GetFileName(path).Equals("TechnologySettings.json", StringComparison.OrdinalIgnoreCase))
        {
            _techPreview = new HeadquartersTechPreview();
            _techPreview.RefreshRequested += RefreshPreview;
            _techPreview.RowSelected += index =>
            {
                if (!ApplyCurrent()) return;
                _refreshing = true;
                try { _search.Text = ""; }
                finally { _refreshing = false; }
                RefreshRows(index);
            };
        }
        var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(31, 31, 31)) };
        var toolbar = new WrapPanel { Margin = new Thickness(12, 8, 12, 8) };
        AddAction("返回", () => window.ReturnToMainScene());
        AddAction("保存表", () => SaveChanges());
        AddAction("应用当前记录", () => { if (ApplyCurrent()) RefreshRows(_selected); });
        AddAction("新增记录", () => AddRow(false));
        AddAction("克隆记录", () => AddRow(true));
        AddAction("删除记录", DeleteRow);
        AddAction("重载", Reload);
        AddAction("检查已保存数据关系", () => new AssetAuditWindow(window, AssetManager.Default.AssetsRoot).ShowDialog());
        if (_techPreview != null) AddAction("编辑科技页面布局", () =>
        {
            if (window.ConfirmUnsavedDocument(this, "打开科技页面布局"))
                window.SetCurrentScene(new LayoutEditScene(window, "form_technique"));
        });
        toolbar.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(path), Foreground = Brushes.White,
            FontSize = 17, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var note = new TextBlock { Text = "按字段编辑当前项目配置。数组和对象输入 JSON；字符串直接输入文字；null 可单独切换。新增/克隆后请检查引用关系。",
            Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 12, 8) };
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280), MinWidth = 180 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        var left = new DockPanel();
        _search.ToolTip = "搜索 Id、名称或字段值";
        DockPanel.SetDock(_search, Dock.Top); left.Children.Add(_search); left.Children.Add(_rows);
        body.Children.Add(left);
        var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromRgb(62, 62, 66)) };
        Grid.SetColumn(splitter, 1); body.Children.Add(splitter);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _fields };
        if (_techPreview == null) { Grid.SetColumn(scroll, 2); body.Children.Add(scroll); }
        else
        {
            var right = new EditorPreviewPane(scroll, _techPreview);
            Grid.SetColumn(right, 2); body.Children.Add(right);
        }
        root.Children.Add(body); Content = root;
        _rows.SelectionChanged += (_, _) => SelectRow();
        _search.TextChanged += (_, _) => { if (!_refreshing && ApplyCurrent()) RefreshRows(_selected); };
        _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); RefreshPreview(); };
        Unloaded += (_, _) => _previewTimer.Stop();
        int? preferredIndex = null;
        if (preferredId != null)
            for (int index = 0; index < _document.Rows.Count; index++)
                if (_document.GetRow(index)["Id"] is JsonValue id && id.TryGetValue<int>(out int value) && value == preferredId)
                { preferredIndex = index; break; }
        RefreshRows(preferredIndex ?? (_document.Rows.Count == 0 ? null : 0));

        void AddAction(string title, Action action)
        {
            var button = new Button { Content = title, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 6, 4) };
            button.Click += (_, _) => action(); toolbar.Children.Add(button);
        }
    }

    private void RefreshRows(int? select)
    {
        _refreshing = true;
        try
        {
            _rows.Items.Clear();
            for (int i = 0; i < _document.Rows.Count; i++)
            {
                var row = _document.GetRow(i);
                if (_search.Text.Length > 0 && !row.ToJsonString().Contains(_search.Text, StringComparison.OrdinalIgnoreCase)) continue;
                string id = row["Id"]?.ToJsonString() ?? $"行 {i + 1}";
                string name = JsonTableDocument.GetValueText(row["Name"] ?? row["EName"] ?? row["Photo"]);
                var item = new RowItem(i, name == "null" ? id : $"[{id}] {name}");
                _rows.Items.Add(item);
                if (select == i) _rows.SelectedItem = item;
            }
        }
        finally { _refreshing = false; }
        _selected = (_rows.SelectedItem as RowItem)?.Index;
        BuildFields();
        ShowStatus($"{_rows.Items.Count} / {_document.Rows.Count} 条记录 · {(_document.HasChanges ? "存在未保存修改" : "已与磁盘一致")}");
    }

    private void SelectRow()
    {
        if (_refreshing) return;
        if (!ApplyCurrent())
        {
            _refreshing = true;
            _rows.SelectedItem = _rows.Items.Cast<RowItem>().FirstOrDefault(item => item.Index == _selected);
            _refreshing = false;
            return;
        }
        _selected = (_rows.SelectedItem as RowItem)?.Index;
        BuildFields();
    }

    private void BuildFields()
    {
        _fields.Children.Clear(); _inputs.Clear();
        if (_selected is not int index) { RefreshPreview(); return; }
        foreach (var pair in _document.GetRow(index)) AddField(index, pair.Key, pair.Value);
        var add = new Button { Content = "添加字段", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 5, 10, 5) };
        add.Click += (_, _) => AddField();
        _fields.Children.Add(add);
        RefreshPreview();
    }

    private void AddField(int index, string name, JsonNode? value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var heading = new WrapPanel();
        heading.Children.Add(new TextBlock { Text = $"{name}  ·  {value?.GetValueKind().ToString() ?? "Null"}",
            Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var isNull = new CheckBox { Content = "null", IsChecked = value == null, Foreground = Brushes.LightGray, Margin = new Thickness(12, 0, 0, 0) };
        heading.Children.Add(isNull); panel.Children.Add(heading);
        var input = new TextBox { Text = JsonTableDocument.GetValueText(value), IsEnabled = value != null,
            AcceptsReturn = value is JsonArray or JsonObject || value?.GetValueKind() == JsonValueKind.String, TextWrapping = TextWrapping.Wrap,
            MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(6), Margin = new Thickness(0, 4, 0, 0), FontSize = 13 };
        input.ToolTip = _document.GetFieldType(index, name) == null ? "无法从已有记录推断类型，请输入 JSON 值，例如 1、true、[] 或带双引号的字符串。"
            : "保持该字段的类型；整数及同类数组元素会校验。";
        var state = new FieldInput(name, input, isNull); _inputs.Add(state);
        input.TextChanged += (_, _) => { state.Changed = true; SchedulePreview(); };
        isNull.Checked += (_, _) => { state.Changed = true; input.IsEnabled = false; SchedulePreview(); };
        isNull.Unchecked += (_, _) =>
        {
            state.Changed = true; input.IsEnabled = true;
            SchedulePreview();
            if (value == null && input.Text == "null") input.Text = _document.GetFieldType(index, name) switch
            { JsonValueKind.String => "", JsonValueKind.Array => "[]", JsonValueKind.Object => "{}",
                JsonValueKind.Number => "0", JsonValueKind.True or JsonValueKind.False => "false", _ => "null" };
        };
        panel.Children.Add(input); _fields.Children.Add(panel);
    }

    private void AddField()
    {
        if (_selected is not int index || !ApplyCurrent()) return;
        var name = new TextBox { Margin = new Thickness(12), MinWidth = 240 };
        var ok = new Button { Content = "添加", Margin = new Thickness(12), IsDefault = true };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "新字段名（初始值为 null）", Margin = new Thickness(12) });
        panel.Children.Add(name); panel.Children.Add(ok);
        var dialog = new Window { Title = "添加字段", Owner = _window, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        ok.Click += (_, _) =>
        {
            string field = name.Text.Trim();
            if (field.Length == 0 || _document.GetRow(index).ContainsKey(field)) return;
            _document.GetRow(index)[field] = null; dialog.DialogResult = true;
        };
        if (dialog.ShowDialog() == true) BuildFields();
    }

    private bool ApplyCurrent()
    {
        if (_selected is not int index) return true;
        try
        {
            _document.ApplyEdits(index, _inputs.Where(input => input.Changed)
                .Select(input => (input.Name, input.Text.Text, input.IsNull.IsChecked == true)));
            foreach (var input in _inputs) input.Changed = false;
            return true;
        }
        catch (Exception ex) { ShowStatus(ex.Message); MessageBox.Show(_window, ex.Message, "字段输入无效", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
    }

    private void AddRow(bool clone)
    {
        if (!ApplyCurrent() || clone && _selected == null) return;
        try { RefreshRows(_document.AddRow(clone, _selected)); }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }

    private void DeleteRow()
    {
        if (_selected is not int index) return;
        if (MessageBox.Show(_window, "删除当前记录？引用此记录的其他表需自行检查。", "删除记录", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _document.RemoveRow(index); _inputs.Clear();
        RefreshRows(_document.Rows.Count == 0 ? null : Math.Min(index, _document.Rows.Count - 1));
    }

    private void Reload()
    {
        if (HasUnsavedChanges && MessageBox.Show(_window, "重载会放弃这张表的未保存修改，继续？", "重载", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { _document.Reload(); RefreshRows(_document.Rows.Count == 0 ? null : 0); }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }

    public bool SaveChanges()
    {
        if (!ApplyCurrent()) return false;
        try
        {
            bool changed = _document.HasChanges;
            _document.Save();
            if (changed) GameProjectDataSession.RefreshEditorFor(_document.FilePath);
            ShowStatus("已保存变更表；未修改的资源不会复制到输出目录。相关专用编辑器下次打开时更新，已有草稿保留，冲突需重载处理。");
            return true;
        }
        catch (Exception ex) { ShowStatus(ex.Message); MessageBox.Show(_window, ex.Message, "保存失败，修改仍保留", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }

    public void DiscardChanges() { _document.Discard(); RefreshRows(_selected); }
    private void ShowStatus(string text) => _status.Text = text;

    private void SchedulePreview()
    {
        if (_techPreview == null) return;
        _previewTimer.Stop(); _previewTimer.Start();
    }

    private void RefreshPreview()
    {
        if (_techPreview == null) return;
        try
        {
            var snapshot = _document.CreatePreviewRows(_selected, _inputs.Where(input => input.Changed)
                .Select(input => (input.Name, input.Text.Text, input.IsNull.IsChecked == true)));
            _techPreview.Update(snapshot, _selected);
        }
        catch (Exception ex) { _techPreview.ShowInvalidInput(ex.Message); }
    }
}
