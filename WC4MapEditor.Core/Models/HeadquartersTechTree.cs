using System.Text.Json.Nodes;
using System.Text.Json;
using WC4MapEditor.Core.Parsers.Layout;

namespace WC4MapEditor.Core.Models;

public sealed record HeadquartersTechGrid(string LayoutName, double Width, double Height,
    double CellWidth, double CellHeight, int ItemCount, double CardWidth)
{
    public int Columns => Math.Max(1, (int)Math.Round(Width / CellWidth));
    public int Rows => Math.Max(1, (int)Math.Ceiling(ItemCount / (double)Columns));

    public static HeadquartersTechGrid FromLayout(string text, string name)
    {
        var document = new LayoutDocument(); document.Load(text, name);
        var form = document.Forms.FirstOrDefault(node => node.Get("id") == "form_technique")
            ?? throw new InvalidDataException("布局没有 form_technique 总部科技表单。");
        var repeater = Walk(form).FirstOrDefault(node => node.Tag == "Repeater" && node.Get("id") == "rpt_items")
            ?? throw new InvalidDataException("科技表单没有 rpt_items 网格。");
        double width = Dimension(repeater, "width"), height = Dimension(repeater, "height");
        double cellWidth = Dimension(repeater, "itemwidth"), cellHeight = Dimension(repeater, "itemheight");
        if (!int.TryParse(repeater.Get("itemcount"), out int count) || count <= 0 || count > 10_000 ||
            width / cellWidth > 1_000 || height / cellHeight > 1_000)
            throw new InvalidDataException("科技网格尺寸或 itemcount 无效。");
        var card = Walk(repeater).FirstOrDefault(node => node.Get("id") == "tech_item");
        double cardWidth = card?.Num("width") ?? double.NaN;
        if (!double.IsFinite(cardWidth) || cardWidth <= 0) cardWidth = cellWidth;
        return new(name, width, height, cellWidth, cellHeight, count, cardWidth);
    }

    private static double Dimension(LayoutNode node, string field)
    {
        double value = node.Num(field);
        if (!double.IsFinite(value) || value <= 0 || value > 100_000)
            throw new InvalidDataException($"科技网格 {field} 无效。");
        return value;
    }

    private static IEnumerable<LayoutNode> Walk(LayoutNode node)
    {
        yield return node;
        foreach (var child in node.Children) foreach (var descendant in Walk(child)) yield return descendant;
    }
}

public sealed record HeadquartersTechNode(int RowIndex, int Id, int Type, int Level, int Category,
    int GridRow, int GridColumn, int UpgradeId, IReadOnlyList<int> Needs, double X, double Y);
public sealed record HeadquartersTechEdge(int FromId, int ToId, bool IsUpgrade);
public sealed record HeadquartersTechTree(IReadOnlyList<HeadquartersTechNode> Nodes,
    IReadOnlyList<HeadquartersTechEdge> Edges, IReadOnlyList<string> Issues)
{
    public static HeadquartersTechTree Build(JsonArray rows, HeadquartersTechGrid grid)
    {
        var nodes = new List<HeadquartersTechNode>();
        var issues = new List<string>();
        for (int index = 0; index < rows.Count; index++)
        {
            try
            {
                var row = rows[index] as JsonObject ?? throw new InvalidDataException("需要对象记录");
                int id = ReadInt(row["Id"], "Id"), type = ReadInt(row["Type"], "Type");
                int level = ReadInt(row["Level"], "Level"), category = ReadInt(row["Categorys"], "Categorys");
                if (row["Position"] is not JsonArray position || position.Count != 2)
                    throw new InvalidDataException("Position 需要两个整数：[行,列]");
                // Real 1.30.0 has [1..4, 1..10] positions in a 10-column repeater.
                int gridRow = ReadInt(position[0], "Position[0]"), column = ReadInt(position[1], "Position[1]");
                int upgrade = ReadInt(row["UpgradeID"], "UpgradeID");
                if (row["NeedID"] is not JsonArray need) throw new InvalidDataException("NeedID 需要整数数组");
                int[] needs = need.Select(node => ReadInt(node, "NeedID")).ToArray();
                if (gridRow <= 0 || column <= 0 || gridRow > grid.Rows || column > grid.Columns ||
                    (long)(gridRow - 1) * grid.Columns + column > grid.ItemCount)
                    issues.Add($"Id={id} 的 Position=[{gridRow},{column}] 超出 {grid.Rows} 行 × {grid.Columns} 列 / {grid.ItemCount} 项网格。");
                if ((double)gridRow * grid.CellHeight > grid.Height)
                    issues.Add($"Id={id} 所在行超出布局高度，可能裁切。");
                double x = (column - 1.0) * grid.CellWidth, y = (gridRow - 1.0) * grid.CellHeight;
                nodes.Add(new(index, id, type, level, category, gridRow, column, upgrade, needs, x, y));
                if (row["TechDesc"] is JsonArray desc && row["TechValue"] is JsonArray values && desc.Count != values.Count)
                    issues.Add($"Id={id} 的 TechDesc / TechValue 数量不同。");
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or FormatException or OverflowException)
            { issues.Add($"第 {index + 1} 行：{ex.Message}"); }
        }
        foreach (var duplicate in nodes.GroupBy(node => node.Id).Where(group => group.Count() > 1))
            issues.Add($"科技 Id={duplicate.Key} 重复，预览关系不明确。");
        // The game's tech_item template has firIcon/secIcon: two different types can share one cell.
        foreach (var collision in nodes.GroupBy(node => (node.Category, node.GridRow, node.GridColumn))
            .Where(group => group.Count() > 2 || group.GroupBy(node => node.Type).Any(types => types.Count() > 1)))
            issues.Add($"分组 {collision.Key.Category} 的位置 [{collision.Key.GridRow},{collision.Key.GridColumn}] 重叠：{string.Join(", ", collision.Select(node => node.Id))}。");
        var lookup = nodes.GroupBy(node => node.Id).ToDictionary(group => group.Key, group => group.First());
        var edges = new List<HeadquartersTechEdge>();
        foreach (var node in nodes)
        {
            if (node.UpgradeId != 0) AddEdge(node.Id, node.UpgradeId, true);
            foreach (int id in node.Needs.Where(id => id != 0)) AddEdge(id, node.Id, false);
        }
        foreach (bool upgrade in new[] { true, false })
        {
            var relationships = edges.Where(edge => edge.IsUpgrade == upgrade).Distinct().ToArray();
            var counts = lookup.Keys.ToDictionary(id => id, _ => 0);
            var successors = relationships.GroupBy(edge => edge.FromId).ToDictionary(group => group.Key, group => group.Select(edge => edge.ToId).ToArray());
            foreach (var edge in relationships) counts[edge.ToId]++;
            var queue = new Queue<int>(counts.Where(pair => pair.Value == 0).Select(pair => pair.Key));
            int visited = 0;
            while (queue.TryDequeue(out int id))
            {
                visited++;
                if (successors.TryGetValue(id, out var next)) foreach (int target in next) if (--counts[target] == 0) queue.Enqueue(target);
            }
            if (visited < lookup.Count) issues.Add($"{(upgrade ? "UpgradeID 升级链" : "NeedID 依赖")}存在环。");
        }
        return new(nodes, edges.Distinct().ToArray(), issues);

        void AddEdge(int from, int to, bool upgrade)
        {
            if (!lookup.ContainsKey(from) || !lookup.ContainsKey(to)) issues.Add($"{(upgrade ? "UpgradeID" : "NeedID")} 引用缺失：{from} → {to}。");
            else edges.Add(new(from, to, upgrade));
        }
    }

    public static int ReadInt(JsonNode? node, string field)
    {
        if (node?.GetValueKind() == JsonValueKind.Number)
        {
            using var document = JsonDocument.Parse(node.ToJsonString());
            if (document.RootElement.TryGetInt32(out int result)) return result;
        }
        throw new InvalidDataException($"{field} 需要 32 位整数。");
    }
}
