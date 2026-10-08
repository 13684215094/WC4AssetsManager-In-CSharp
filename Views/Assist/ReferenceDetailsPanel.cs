using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.Skill;

namespace WC4MapEditor.Views.Assist;

internal sealed class ReferenceDetailsPanel : StackPanel
{
    private readonly bool _features;
    private readonly ComboBox _locale = new() { Width = 115, Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _cards = new();
    private IReadOnlyList<int> _ids = [];
    private IReadOnlyList<int> _levels = [];
    private IReadOnlyList<ArmyFeatureData> _featureRows = [];
    private long _revision = -1;
    public event Action<int>? EditRequested;
    public event Action<ReferenceSlotLists>? SlotsChanged;
    public Func<bool>? CanChange { get; set; }

    public ReferenceDetailsPanel(bool features = false)
    {
        _features = features; Margin = new Thickness(8, 4, 8, 8);
        _locale.Width = features ? 115 : 210;
        _locale.ItemsSource = new[] { "简体中文", features ? "繁体中文" : "繁体中文（技能文本草稿）", "English" }; _locale.SelectedIndex = 0;
        _locale.SelectionChanged += (_, _) => Refresh();
        Children.Add(_locale); Children.Add(_cards);
    }

    public void Update(IReadOnlyList<int>? ids, IReadOnlyList<int>? levels = null)
    {
        _ids = ids ?? []; _levels = levels ?? []; Refresh();
    }

    public void ShowInvalid(string message)
    {
        _cards.Children.Clear(); AddText(_cards, message, Brushes.LightSalmon);
    }

    private string? Lookup(string key)
    {
        if (_locale.SelectedIndex == 1 && key.StartsWith("skill_"))
        {
            var parser = SkillSettingParser.Instance;
            if (key.StartsWith("skill_name_") && int.TryParse(key[11..], out int type)) return parser.GetSkillName(type) is { Length: > 0 } name ? name : null;
            if (key.StartsWith("skill_info_") && int.TryParse(key[11..], out type)) return parser.GetSkillDesc(type) is { Length: > 0 } description ? description : null;
        }
        string locale = _locale.SelectedIndex switch { 1 => "tw", 2 => "en", _ => "cn" };
        return AssetManager.Default.GetStringTable(locale).GetValueOrDefault(key);
    }

    private void Refresh()
    {
        _cards.Children.Clear();
        try
        {
            if (_features && _revision != AssetManager.Default.Revision)
            {
                var issues = new List<string>();
                var rows = GameUiPreviewResources.ReadRows<ArmyFeatureData>(AssetManager.Default, "json/ArmyFeatureSettings.json", issues);
                if (issues.Count > 0) throw new InvalidOperationException(string.Join("\n", issues));
                _featureRows = rows;
                _revision = AssetManager.Default.Revision;
            }
            var add = new Button { Content = _features ? "添加特性" : "添加技能槽", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 8) };
            add.Click += (_, _) => Choose(_ids.Count); _cards.Children.Add(add);
            if (_ids.Count == 0) AddText(_cards, _features ? "未配置特性。" : "未配置技能。", Brushes.Gray);
            if (_features && _levels.Count != 0 && _levels.Count != _ids.Count)
                AddText(_cards, $"Feature 有 {_ids.Count} 项、FeatureLevel 有 {_levels.Count} 项，原始内容保留。请先在高级数组中明确修正后再调整槽位。", Brushes.LightSalmon);
            for (int i = 0; i < _ids.Count; i++)
            {
                var detail = _features ? ProjectDataDescriptions.Feature(_ids[i], i < _levels.Count ? _levels[i] : null, _featureRows, Lookup)
                    : ProjectDataDescriptions.Skill(_ids[i], SkillSettingParser.Instance.All, Lookup);
                var card = new StackPanel();
                AddText(card, $"{i + 1}. {detail.Title}", Brushes.LightGoldenrodYellow);
                AddText(card, detail.Description, Brushes.White);
                AddText(card, detail.Parameters, Brushes.LightGray);
                if (detail.Warning != null) AddText(card, detail.Warning, Brushes.LightSalmon);
                if (detail.RecordId is int id)
                {
                    var button = new Button { Content = _features ? "编辑此特性配置" : "编辑此技能", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
                    button.Click += (_, _) => EditRequested?.Invoke(id); card.Children.Add(button);
                }
                int index = i;
                var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                ActionButton("替换 / 等级", () => Choose(index));
                ActionButton("上移", () => Change(() => ReferenceSlots.Move(_ids, _levels, index, index - 1)), index > 0);
                ActionButton("下移", () => Change(() => ReferenceSlots.Move(_ids, _levels, index, index + 1)), index + 1 < _ids.Count);
                if (!_features) ActionButton("清空为 Id=0", () => Change(() => ReferenceSlots.Set(_ids, _levels, index, 0, null)));
                ActionButton("移除槽位", () => Change(() => ReferenceSlots.Remove(_ids, _levels, index)));
                card.Children.Add(actions);
                void ActionButton(string text, Action action, bool enabled = true)
                {
                    var button = new Button { Content = text, IsEnabled = enabled, Margin = new Thickness(0, 4, 6, 0), Padding = new Thickness(6, 3, 6, 3) };
                    button.Click += (_, _) => action(); actions.Children.Add(button);
                }
                _cards.Children.Add(new Border { Child = card, Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8),
                    BorderThickness = new Thickness(1), BorderBrush = Brushes.DimGray, Background = new SolidColorBrush(Color.FromRgb(35, 39, 42)) });
            }
        }
        catch (Exception ex) { ShowInvalid($"读取说明失败：{ex.Message}"); }
    }

    private void Choose(int index)
    {
        if (CanChange?.Invoke() == false) return;
        var owner = Window.GetWindow(this);
        if (owner == null) return;
        var picker = new ProjectReferencePicker(owner, _features, _featureRows, Lookup, _features && _levels.Count == 0);
        if (picker.ShowDialog() != true || picker.Selection is not { } selection) return;
        Change(() => ReferenceSlots.Set(_ids, _levels, index, selection.Reference, selection.Level));
    }

    private void Change(Func<ReferenceSlotLists> change)
    {
        if (CanChange?.Invoke() == false) return;
        try { SlotsChanged?.Invoke(change()); }
        catch (InvalidOperationException ex) { MessageBox.Show(Window.GetWindow(this), ex.Message, "未调整槽位", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static void AddText(Panel panel, string text, Brush color)
    {
        if (text.Length > 0) panel.Children.Add(new TextBlock { Text = text, Foreground = color, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
    }
}
