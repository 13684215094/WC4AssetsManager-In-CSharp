using System.Text;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Core.Parsers.Stage;

namespace WC4MapEditor.Core.Analyzers;

public class BTLAnalyzer
{
    public string AnalyzeStageParser(StageParser parser) => AnalyzeBattle(parser);

    public string AnalyzeConquestParser(ConquestParser parser) => AnalyzeBattle(parser);

    private static string AnalyzeBattle(BattleParser parser)
    {
        byte[] bytes = BTLParser.SaveToBytes(parser.ToMapData());
        var h = BTLHeader.Parse(bytes);
        var layout = new BtlLayout(h);
        var sb = new StringBuilder();
        sb.AppendLine("= BTL 当前数据序列化地址 =");
        sb.AppendLine($"文件路径: {parser.HexFilePath}");
        sb.AppendLine($"大小: {layout.Length:N0} 字节 (0x{layout.Length:X})");
        sb.AppendLine($"BTL版本: {h.BtlVersion}; 地图资源: {h.MapNumber}");
        sb.AppendLine($"地图尺寸: {h.MapLength} x {h.MapWidth}; 实际格数: {layout.Area}; 平面容量: {layout.Capacity}");

        string[] fields =
        [
            "BtlVersion", "MapNumber", "MapClipX", "MapClipY", "MapLength", "MapWidth",
            "ArmyCount", "BuildingCount", "TroopCount", "PlanCount", "EventCount", "WeatherCount",
            "VictoryCondition", "MinTurns", "MaxTurns", "ReinforcementCount", "AirRaidCount",
            "PlacementA", "PlacementB", "ConqueredFlagPosition", "Unknown3", "Unknown4",
            "SelectableTileCount", "AccumulatedEconomy", "AccumulatedIndustry", "AccumulatedTech",
            "TrapCount", "Unknown5", "StrategyCount", "Unknown6", "Unknown7", "AirSupportCount"
        ];
        sb.AppendLine();
        sb.AppendLine("【BTLHeader】0x00 - 0x7F (128 字节)");
        for (int i = 0; i < fields.Length; i++)
            sb.AppendLine($"  0x{i * 4:X2} {fields[i]}: {BitConverter.ToInt32(bytes, i * 4)}");

        foreach (var section in layout.Sections.Skip(1))
        {
            if (section.Size == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"【{section.Name}】");
            sb.AppendLine($"  起始地址: 0x{section.Offset:X8}");
            sb.AppendLine($"  结束地址: 0x{section.End - 1:X8}");
            sb.AppendLine($"  数量: {section.Count}; 每项: {section.Stride} 字节; 大小: {section.Size} 字节");
        }
        sb.AppendLine();
        sb.AppendLine($"=== 已解析数据总大小: {layout.Length:N0} 字节 ===");
        sb.AppendLine("地形仅存在于 map_id=0，按实际格数存储；省份和归属按容量存储。");
        sb.AppendLine("v1 部队/增援为 48/80 字节；v2/v3 为 64/104 字节。");
        sb.AppendLine("placementA/B 均为 8 字节；opaque 和 v3 extra 原样保留。");
        return sb.ToString();
    }

    public string AnalyzeWorldParser(MapData mapData)
    {
        int area = MapLimits.Area(mapData.MapWidth, mapData.MapHeight);
        if (area != mapData.TerrainCount)
            throw new InvalidDataException("World dimensions do not match its planes.");
        int provinceOffset = checked(16 + area * 16);
        int length = MapLimits.FileSize(16L + area * 18L);
        var sb = new StringBuilder();
        sb.AppendLine("= 世界文件数据段地址信息 =");
        sb.AppendLine($"文件路径: {mapData.FilePath}");
        sb.AppendLine("【WorldHeader】0x00 - 0x0F (16 字节)");
        sb.AppendLine("  0x00 Magic: YSAE");
        sb.AppendLine("  0x04 Version: 4");
        sb.AppendLine($"  0x08 Width (Int32 LE): {mapData.MapWidth}");
        sb.AppendLine($"  0x0C Height (Int32 LE): {mapData.MapHeight}");
        sb.AppendLine($"地形: 0x00000010 - 0x{provinceOffset - 1:X8}; {area} x 16 字节");
        sb.AppendLine($"省份/尾索引: 0x{provinceOffset:X8} - 0x{length - 1:X8}; {area} x 2 字节");
        sb.AppendLine($"总大小: {length:N0} 字节");
        return sb.ToString();
    }
}
