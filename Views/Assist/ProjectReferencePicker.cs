using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.Skill;

namespace WC4MapEditor.Views.Assist;

internal sealed record ReferenceChoice(int Reference, int? Level);

internal sealed class ProjectReferencePicker : Window
{
    private sealed record Family(int Type, string Name, string Search) { public override string ToString() => $"{Name} · Type {Type}"; }
    private sealed record Choice(int Reference, int? Level, string Label, ProjectDataDescription Detail)
    { public override string ToString() => Label; }
    public ReferenceChoice? Selection { get; private set; }

    public ProjectReferencePicker(Window owner, bool features, IReadOnlyList<ArmyFeatureData> featureRows,
        Func<string, string?> lookup, bool runtimeLevels)
    {
        Owner = owner; Title = features ? "选择兵种特性" : "选择将领技能";
        Width = 790; Height = 620; MinWidth = 620; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(30, 34, 39)); Foreground = Brushes.White;
        var root = new DockPanel { Margin = new Thickness(14) };
        var search = new TextBox { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(8), ToolTip = "搜索名称、介绍、Type 或记录 Id" };
        var heading = new TextBlock { Text = features
                ? runtimeLevels ? "当前 FeatureLevel 为空，保留运行时等级。若要指定等级，请先在兵种页填写与 Feature 一一对应的完整 FeatureLevel。"
                    : "先选特性 Type，再选项目已配置的等级。"
                : "先选技能类型，再选项目已配置的等级；保存的是技能记录 Id。",
            Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var apply = new Button { Content = "使用所选项", IsDefault = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(6), IsEnabled = false };
        var cancel = new Button { Content = "取消", IsCancel = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(6) };
        actions.Children.Add(apply); actions.Children.Add(cancel); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270), MinWidth = 180 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var families = new ListBox { Margin = new Thickness(0, 0, 12, 0) };
        body.Children.Add(families);
        var details = new DockPanel(); Grid.SetColumn(details, 1); body.Children.Add(details);
        var levels = new ListBox { MaxHeight = 180, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(levels, Dock.Top); details.Children.Add(levels);
        var description = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        details.Children.Add(new ScrollViewer { Content = description, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(body); Content = root;
        var skills = SkillSettingParser.Instance.All;
        var allFamilies = features
            ? featureRows.GroupBy(row => row.Type).Select(group => new Family(group.Key, lookup($"feat_name_{group.Key}") ?? group.First().Name,
                string.Join(" ", group.Select(row => $"{row.Id} {row.Name} {lookup($"feat_desc_{group.Key}_Lv{row.Level}")}")) + " " + lookup($"feat_desc_{group.Key}"))).OrderBy(row => row.Type).ToArray()
            : skills.Where(row => row.Id != 0).GroupBy(row => row.Type).Select(group => new Family(group.Key,
                lookup($"skill_name_{group.Key}") ?? group.First().Name,
                string.Join(" ", group.Select(row => $"{row.Id} {row.Name}")) + " " + lookup($"skill_info_{group.Key}"))).OrderBy(row => row.Type).ToArray();
        void Filter()
        {
            string query = search.Text.Trim();
            families.ItemsSource = allFamilies.Where(row => query.Length == 0 ||
                $"{row.Type} {row.Name} {row.Search}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            families.SelectedIndex = families.Items.Count > 0 ? 0 : -1;
        }
        families.SelectionChanged += (_, _) =>
        {
            levels.Items.Clear(); description.Text = ""; apply.IsEnabled = false;
            if (families.SelectedItem is not Family family) return;
            if (features)
            {
                if (runtimeLevels)
                {
                    var detail = ProjectDataDescriptions.Feature(family.Type, null, featureRows, lookup);
                    levels.Items.Add(new Choice(family.Type, null, "不显式设置等级（保留 FeatureLevel 为空）", detail));
                }
                else foreach (var row in featureRows.Where(row => row.Type == family.Type).OrderBy(row => row.Level))
                    levels.Items.Add(new Choice(row.Type, row.Level, $"Lv{row.Level} · 记录 #{row.Id}",
                        ProjectDataDescriptions.Feature(row.Type, row.Level, featureRows, lookup)));
            }
            else foreach (var row in skills.Where(row => row.Type == family.Type && row.Id != 0).OrderBy(row => row.Level))
                levels.Items.Add(new Choice(row.Id, null, $"Lv{row.Level} · #{row.Id}", ProjectDataDescriptions.Skill(row.Id, skills, lookup)));
            levels.SelectedIndex = levels.Items.Count > 0 ? 0 : -1;
        };
        levels.SelectionChanged += (_, _) =>
        {
            apply.IsEnabled = levels.SelectedItem is Choice;
            if (levels.SelectedItem is Choice choice)
                description.Text = $"{choice.Detail.Title}\n\n{choice.Detail.Description}\n\n{choice.Detail.Parameters}\n\n{choice.Detail.Warning}";
        };
        apply.Click += (_, _) =>
        {
            if (levels.SelectedItem is not Choice choice) return;
            Selection = new(choice.Reference, choice.Level); DialogResult = true;
        };
        search.TextChanged += (_, _) => Filter(); Filter(); Loaded += (_, _) => search.Focus();
    }
}
