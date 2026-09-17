using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Commands;

public sealed class CliCommandHost : ICommandHost
{
    private static readonly object _lock = new();
    private static CliCommandHost? _instance;

    public static CliCommandHost Instance
    {
        get
        {
            lock (_lock)
            {
                _instance ??= new CliCommandHost();
            }
            return _instance;
        }
    }

    private readonly RootCommand _rootCommand;
    private TextWriter _output = Console.Out;

    private CliCommandHost()
    {
        _rootCommand = BuildRootCommand();
    }

    public void SetOutput(TextWriter output) => _output = output;

    public int Execute(string commandLine)
    {
        var args = CommandLineToArgs(commandLine);
        var parseResult = _rootCommand.Parse(args);
        return parseResult.Invoke();
    }

    public (List<string> Errors, string CommandName, string CommandDescription) GetParseErrors(string commandLine)
    {
        var args = CommandLineToArgs(commandLine);
        var parseResult = _rootCommand.Parse(args);
        var errors = new List<string>();
        foreach (var error in parseResult.Errors)
            errors.Add(error.Message);
        var cmd = parseResult.CommandResult.Command;
        return (errors, cmd?.Name ?? "", cmd?.Description ?? "");
    }

    public string GetHelp()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== CLI修改命令 ===");
        foreach (var sub in _rootCommand.Subcommands)
        {
            sb.AppendLine($"  {sub.Name,-12} - {sub.Description}");
            foreach (var sub2 in sub.Subcommands)
                sb.AppendLine($"    {sub.Name} {sub2.Name,-16} - {sub2.Description}");
        }
        sb.AppendLine();
        sb.AppendLine("示例: building add 0 12 -t 11");
        sb.AppendLine("      building add 87 25 -t 15 -n 北京");
        sb.AppendLine("      building remove-by-probability 30 -k");
        sb.AppendLine("      belong set 0 12 5");
        sb.AppendLine("      belong copy 0 12");
        sb.AppendLine("      belong paste 5 8");
        sb.AppendLine("      belong remove 3 4");
        sb.AppendLine("      belong clean-orphan");
        sb.AppendLine("      belong randomize");
        sb.AppendLine("      belong batch-set 3");
        sb.AppendLine("      belong get 89 36");
        sb.AppendLine("      belong set-by-city 北京 1");
        sb.AppendLine("      belong set-province 89 36 1");
        sb.AppendLine("      belong set-region 80 40 100 60 2");
        sb.AppendLine("      belong replace 1 2");
        sb.AppendLine("      belong randomize-by 1");
        sb.AppendLine("      belong remove-border-entities");
        sb.AppendLine("      belong remove-border-armies");
        sb.AppendLine("      terrain greening -p 50");
        return sb.ToString();
    }

    public Command GetRootCommand() => _rootCommand;

    /// <summary>
    /// 去掉行尾注释：'#'、';'、'//' 前面是空白（或位于行首）时，从这里截断。
    /// <para>
    /// 要求"前面是空白"是为了不误伤路径里的符号，
    /// 例如 <c>run "a#b.zme"</c> 里的 # 前面是字母，不会被当成注释。
    /// </para>
    /// </summary>
    public static string StripComment(string line)
    {
        if (string.IsNullOrEmpty(line)) return line;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            bool isCommentStart = c == '#' || c == ';'
                || (c == '/' && i + 1 < line.Length && line[i + 1] == '/');

            if (!isCommentStart) continue;
            if (i > 0 && !char.IsWhiteSpace(line[i - 1])) continue;

            return line[..i].TrimEnd();
        }

        return line;
    }

    private static string[] CommandLineToArgs(string commandLine)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in commandLine)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            else if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
                current.Append(c);
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args.ToArray();
    }

    private RootCommand BuildRootCommand()
    {
        var root = new RootCommand("WC4 Map Editor");

        root.Subcommands.Add(BuildTerrainCommand());
        root.Subcommands.Add(BuildProvinceCommand());
        root.Subcommands.Add(BuildBuildingCommand());
        root.Subcommands.Add(BuildBelongCommand());
        root.Subcommands.Add(BuildLegionCommand());
        root.Subcommands.Add(BuildArmyCommand());
        root.Subcommands.Add(BuildTrapCommand());
        root.Subcommands.Add(BuildInfoCommand());
        root.Subcommands.Add(BuildCheckCommand());
        root.Subcommands.Add(BuildFixCommand());
        root.Subcommands.Add(BuildRunCommand());

        return root;
    }

    private Command BuildTerrainCommand()
    {
        var cmd = new Command("terrain", "地形修改操作");

        var greeningCmd = new Command("greening", "绿化平地");
        var greeningProbOpt = new Option<int>("--probability", "-p") { Description = "绿化概率(0-100)", DefaultValueFactory = _ => 50 };
        greeningCmd.Options.Add(greeningProbOpt);
        greeningCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int prob = Math.Clamp(parseResult.GetValue(greeningProbOpt), 0, 100);
                var result = terrain.ApplyGreening(prob);
                _output.WriteLine(result.Message ?? $"绿化完成，概率={prob}%");
            });
        });

        var randomFlatCmd = new Command("random-flat", "随机平地变体");
        var randomFlatProbOpt = new Option<int>("--probability", "-p") { Description = "随机概率(0-100)", DefaultValueFactory = _ => 50 };
        randomFlatCmd.Options.Add(randomFlatProbOpt);
        randomFlatCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                int prob = Math.Clamp(parseResult.GetValue(randomFlatProbOpt), 0, 100);
                var result = terrain.RandomizeFlatTerrain(prob);
                _output.WriteLine(result.Message ?? $"随机平地变体完成，概率={prob}%");
            });
        });

        var randomVariantCmd = new Command("random-variant", "随机当前层变体");
        var randomVariantProbOpt = new Option<int>("--probability", "-p") { Description = "随机概率(0-100)", DefaultValueFactory = _ => 50 };
        var randomVariantLayerOpt = new Option<int>("--layer", "-l") { Description = "编辑层(1-3)", DefaultValueFactory = _ => 1 };
        randomVariantCmd.Options.Add(randomVariantProbOpt);
        randomVariantCmd.Options.Add(randomVariantLayerOpt);
        randomVariantCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                terrain.EditLayer = Math.Clamp(parseResult.GetValue(randomVariantLayerOpt), 1, 3);
                int prob = Math.Clamp(parseResult.GetValue(randomVariantProbOpt), 0, 100);
                var result = terrain.RandomizeVariant(prob);
                _output.WriteLine(result.Message ?? $"随机变体完成，编辑层={terrain.EditLayer}，概率={prob}%");
            });
        });

        var createCoastCmd = new Command("create-coast", "创建海岸线");
        createCoastCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                var result = terrain.CreateCoast();
                _output.WriteLine(result.Message ?? "海岸线创建完成");
            });
        });

        var processOceanCmd = new Command("process-ocean-layer2", "处理海洋第二层");
        processOceanCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                var result = terrain.ProcessOceanSecondLayer();
                _output.WriteLine(result.Message ?? "海洋第二层处理完成");
            });
        });

        var floodFillCmd = new Command("flood-fill", "洪水填充地形");
        var fillColArg = new Argument<int>("col") { Description = "起始列" };
        var fillRowArg = new Argument<int>("row") { Description = "起始行" };
        var fillTypeArg = new Argument<int>("type") { Description = "替换地形类型" };
        floodFillCmd.Arguments.Add(fillColArg);
        floodFillCmd.Arguments.Add(fillRowArg);
        floodFillCmd.Arguments.Add(fillTypeArg);
        floodFillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var terrain = mgr.GetModifier<TerrainModifier>()!;
                var result = terrain.FloodFill(parseResult.GetValue(fillColArg), parseResult.GetValue(fillRowArg), (byte)parseResult.GetValue(fillTypeArg));
                _output.WriteLine(result.Message ?? "洪水填充完成");
            });
        });

        cmd.Subcommands.Add(greeningCmd);
        cmd.Subcommands.Add(randomFlatCmd);
        cmd.Subcommands.Add(randomVariantCmd);
        cmd.Subcommands.Add(createCoastCmd);
        cmd.Subcommands.Add(processOceanCmd);
        cmd.Subcommands.Add(floodFillCmd);

        return cmd;
    }

    private Command BuildProvinceCommand()
    {
        var cmd = new Command("province", "省份修改操作");

        var clearCmd = new Command("clear", "清空所有省份");
        clearCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.ClearAllProvinces();
                _output.WriteLine(result.Message ?? "所有省份已清空");
            });
        });

        var generateCmd = new Command("generate", "生成孤立省会省区");
        generateCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.GenerateProvincesForIsolatedCapitals();
                _output.WriteLine(result.Message ?? "孤立省会省区生成完成");
            });
        });

        var expandCmd = new Command("expand", "扩展省区填满地图");
        expandCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.ExpandAllProvincesToFillMap();
                _output.WriteLine(result.Message ?? "省区扩展完成");
            });
        });

        var processCmd = new Command("process", "处理孤立和空白省区");
        processCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.ProcessIsolatedAndEmptyProvinces();
                _output.WriteLine(result.Message ?? "孤立和空白省区处理完成");
            });
        });

        var floodFillCmd = new Command("flood-fill", "洪水填充省份");
        var fillColArg = new Argument<int>("col") { Description = "起始列" };
        var fillRowArg = new Argument<int>("row") { Description = "起始行" };
        floodFillCmd.Arguments.Add(fillColArg);
        floodFillCmd.Arguments.Add(fillRowArg);
        floodFillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var province = mgr.GetModifier<ProvinceModifier>()!;
                var result = province.FloodFill(parseResult.GetValue(fillColArg), parseResult.GetValue(fillRowArg));
                _output.WriteLine(result.Message ?? "省份洪水填充完成");
            });
        });

        cmd.Subcommands.Add(clearCmd);
        cmd.Subcommands.Add(generateCmd);
        cmd.Subcommands.Add(expandCmd);
        cmd.Subcommands.Add(processCmd);
        cmd.Subcommands.Add(floodFillCmd);

        return cmd;
    }

    private Command BuildBuildingCommand()
    {
        var cmd = new Command("building", "建筑修改操作");

        var addCmd = new Command("add", "在指定坐标添加建筑");
        var addColArg = new Argument<int>("col") { Description = "列坐标" };
        var addRowArg = new Argument<int>("row") { Description = "行坐标" };
        var addTypeOpt = new Option<int>("--type", "-t") { Description = "建筑类型(11=一级城,12=二级城,...,15=五级城)", DefaultValueFactory = _ => 11 };
        var addNameOpt = new Option<string>("--name", "-n") { Description = "建筑名称(数字ID或中文名称,默认0xFFFF=无名称)", DefaultValueFactory = _ => "-1" };
        var addIndexOpt = new Option<int>("--index", "-i") { Description = "格子索引(优先于坐标)" };
        addCmd.Arguments.Add(addColArg);
        addCmd.Arguments.Add(addRowArg);
        addCmd.Options.Add(addTypeOpt);
        addCmd.Options.Add(addNameOpt);
        addCmd.Options.Add(addIndexOpt);
        addCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int col = parseResult.GetValue(addColArg);
                int row = parseResult.GetValue(addRowArg);
                int buildingType = parseResult.GetValue(addTypeOpt);
                string nameInput = parseResult.GetValue(addNameOpt) ?? "-1";
                ushort name = BuildingNameResolver.Resolve(nameInput);

                if (col < 0 || col >= mapData.MapWidth || row < 0 || row >= mapData.MapHeight)
                {
                    _output.WriteLine($"坐标 ({col},{row}) 超出范围 (0-{mapData.MapWidth - 1},0-{mapData.MapHeight - 1})");
                    return;
                }

                int coordIndex = row * mapData.MapWidth + col;
                var b = Building.CreateDefault(coordIndex);
                b.BuildingType = (byte)buildingType;
                b.Name = name;

                if (coordIndex > 65535)
                    _output.WriteLine($"[警告] 坐标序号 {coordIndex} 超出文件格式上限 65535，保存时将被截断为 {coordIndex & 0xFFFF}，游戏可能无法正确识别此建筑");

                var result = building.Apply(col, row, b);
                _output.WriteLine(result.Message ?? $"已在 ({col},{row}) 添加建筑，类型={buildingType}({b.GetBuildingTypeName()})");
            });
        });

        var addByIdxCmd = new Command("add-by-idx", "按格子索引添加建筑");
        var addIdxArg = new Argument<int>("index") { Description = "格子索引" };
        var addIdxTypeOpt = new Option<int>("--type", "-t") { Description = "建筑类型", DefaultValueFactory = _ => 11 };
        var addIdxNameOpt = new Option<string>("--name", "-n") { Description = "建筑名称(数字ID或中文名称)", DefaultValueFactory = _ => "-1" };
        addByIdxCmd.Arguments.Add(addIdxArg);
        addByIdxCmd.Options.Add(addIdxTypeOpt);
        addByIdxCmd.Options.Add(addIdxNameOpt);
        addByIdxCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int idx = parseResult.GetValue(addIdxArg);
                int buildingType = parseResult.GetValue(addIdxTypeOpt);
                string nameInput = parseResult.GetValue(addIdxNameOpt) ?? "-1";
                ushort name = BuildingNameResolver.Resolve(nameInput);

                if (idx < 0 || idx >= mapData.MapWidth * mapData.MapHeight)
                {
                    _output.WriteLine($"索引 {idx} 超出范围 (0-{mapData.MapWidth * mapData.MapHeight - 1})");
                    return;
                }

                int col = idx % mapData.MapWidth;
                int row = idx / mapData.MapWidth;
                var b = Building.CreateDefault(idx);
                b.BuildingType = (byte)buildingType;
                b.Name = name;

                if (idx > 65535)
                    _output.WriteLine($"[警告] 坐标序号 {idx} 超出文件格式上限 65535，保存时将被截断为 {idx & 0xFFFF}，游戏可能无法正确识别此建筑");

                var result = building.Apply(col, row, b);
                _output.WriteLine(result.Message ?? $"已在索引{idx} ({col},{row}) 添加建筑，类型={buildingType}({b.GetBuildingTypeName()})");
            });
        });

        var removeCmd = new Command("remove", "删除指定坐标的建筑");
        var rmColArg = new Argument<int>("col") { Description = "列坐标" };
        var rmRowArg = new Argument<int>("row") { Description = "行坐标" };
        removeCmd.Arguments.Add(rmColArg);
        removeCmd.Arguments.Add(rmRowArg);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int col = parseResult.GetValue(rmColArg);
                int row = parseResult.GetValue(rmRowArg);
                var result = building.Remove(col, row);
                _output.WriteLine(result.Message ?? $"已删除 ({col},{row}) 的建筑");
            });
        });

        var removeAllCmd = new Command("remove-all", "删除所有建筑");
        removeAllCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                building.RemoveAll();
                _output.WriteLine("所有建筑已删除");
            });
        });

        var removeNonCapitalCmd = new Command("remove-non-capital", "删除非首都建筑");
        removeNonCapitalCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.RemoveNonCapitalBuildings(mapData);
                _output.WriteLine($"已删除 {count} 个非首都建筑");
            });
        });

        var randomizeNamedCmd = new Command("randomize-named", "随机有名称建筑类型");
        randomizeNamedCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                var result = building.RandomizeNamedBuildingTypes(useCondition: false);
                _output.WriteLine(result.Message ?? "已完成");
            });
        });

        var randomizeUnnamedCmd = new Command("randomize-unnamed", "随机无名称建筑类型");
        randomizeUnnamedCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                var result = building.RandomizeUnnamedBuildingTypes(useCondition: false);
                _output.WriteLine(result.Message ?? "已完成");
            });
        });

        var randomizeByBelongCmd = new Command("randomize-by-belong", "按归属随机建筑");
        var belongIdArg = new Argument<int>("belongId") { Description = "归属ID" };
        randomizeByBelongCmd.Arguments.Add(belongIdArg);
        randomizeByBelongCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int belongId = parseResult.GetValue(belongIdArg);
                int count = building.RandomizeBuildingsByBelong(belongId);
                _output.WriteLine($"已按归属{belongId}随机 {count} 个建筑");
            });
        });

        var generateCapitalsCmd = new Command("generate-capitals", "为所有建筑生成首都");
        generateCapitalsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.GenerateCapitalsForAllBuildings(mapData);
                _output.WriteLine($"已生成 {count} 个首都");
            });
        });

        var randomizeOnCapitalsCmd = new Command("randomize-on-capitals", "在首都随机建筑");
        randomizeOnCapitalsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.RandomizeBuildingsOnCapitals(mapData);
                _output.WriteLine($"已在首都随机 {count} 个建筑");
            });
        });

        var smartAppearanceCmd = new Command("smart-appearance", "智能设置建筑外观");
        smartAppearanceCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = building.SmartSetBuildingAppearance(mapData);
                _output.WriteLine($"已智能设置 {count} 个建筑外观");
            });
        });

        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(addByIdxCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(removeAllCmd);
        cmd.Subcommands.Add(removeNonCapitalCmd);
        cmd.Subcommands.Add(randomizeNamedCmd);
        cmd.Subcommands.Add(randomizeUnnamedCmd);
        cmd.Subcommands.Add(randomizeByBelongCmd);
        cmd.Subcommands.Add(generateCapitalsCmd);
        cmd.Subcommands.Add(randomizeOnCapitalsCmd);
        var removeByProbCmd = new Command("remove-by-probability", "按概率删除建筑（每个建筑独立掷骰）");
        var removeProbArg = new Argument<int>("probability") { Description = "删除概率(0-100)" };
        var removeKeepNamedOpt = new Option<bool>("--keep-named", "-k") { Description = "保留有名称的建筑（城市/据点）" };
        removeByProbCmd.Arguments.Add(removeProbArg);
        removeByProbCmd.Options.Add(removeKeepNamedOpt);
        removeByProbCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int prob = Math.Clamp(parseResult.GetValue(removeProbArg), 0, 100);
                bool keepNamed = parseResult.GetValue(removeKeepNamedOpt);

                ModifierResult result = default;
                RecordEntityChange($"按 {prob}% 概率删除建筑",
                    () => result = building.RemoveBuildingsByProbability(prob, keepNamed));
                _output.WriteLine(result.Message ?? $"已删除 {result.AffectedCount} 个建筑");
            });
        });

        cmd.Subcommands.Add(smartAppearanceCmd);
        cmd.Subcommands.Add(removeByProbCmd);

        return cmd;
    }

    private Command BuildBelongCommand()
    {
        var cmd = new Command("belong", "归属修改操作");

        var clearCmd = new Command("clear", "清空所有归属");
        clearCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordBelongChange("清空所有归属", () => count = belong.ClearAllBelongs());
                _output.WriteLine($"已清空 {count} 个格子的归属");
            });
        });

        var setCmd = new Command("set", "设置指定格子归属");
        var setColArg = new Argument<int>("col") { Description = "列" };
        var setRowArg = new Argument<int>("row") { Description = "行" };
        var setBelongArg = new Argument<int>("belong") { Description = "归属值(0-255, 255=无归属)" };
        setCmd.Arguments.Add(setColArg);
        setCmd.Arguments.Add(setRowArg);
        setCmd.Arguments.Add(setBelongArg);
        setCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(setColArg);
                int row = parseResult.GetValue(setRowArg);
                int belongVal = Math.Clamp(parseResult.GetValue(setBelongArg), 0, 255);
                ModifierResult result = default;
                RecordSingleBelongChange(col, row, $"设置归属 ({col},{row})",
                    () => result = belong.SetBelongByCountryId(col, row, belongVal));
                _output.WriteLine(result.Message ?? $"已设置归属 ({col},{row}) = {belongVal}");
            });
        });

        var fillCmd = new Command("fill-from-legion", "按军团ID填充归属到建筑格子");
        var fillLegionIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        fillCmd.Arguments.Add(fillLegionIdArg);
        fillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int legionId = parseResult.GetValue(fillLegionIdArg);
                int count = 0;
                RecordBelongChange($"按军团填充归属 ({legionId})", () =>
                {
                    foreach (var b in mapData.Buildings)
                    {
                        int col = b.Coordinate % mapData.MapWidth;
                        int row = b.Coordinate / mapData.MapWidth;
                        if (mapData.GetBelongValue(col, row) != legionId)
                        {
                            mapData.SetBelongValue(col, row, legionId);
                            count++;
                        }
                    }
                });
                _output.WriteLine($"已将 {count} 个建筑格子归属设为军团 {legionId}");
            });
        });

        var statsCmd = new Command("stats", "归属统计");
        statsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belongCounts = new Dictionary<int, int>();
                for (int i = 0; i < mapData.Belongs.Count; i++)
                {
                    int val = mapData.GetBelongValueByIndex(i);
                    if (val != 0xFF)
                    {
                        if (!belongCounts.ContainsKey(val)) belongCounts[val] = 0;
                        belongCounts[val]++;
                    }
                }
                _output.WriteLine("归属值分布:");
                foreach (var kv in belongCounts.OrderBy(x => x.Key))
                {
                    string name = "";
                    var legionInfo = mapData.ResolveLegionByBelong(kv.Key);
                    if (legionInfo != null) name = $" ({legionInfo.Value.DisplayName})";
                    _output.WriteLine($"  归属={kv.Key}{name}: {kv.Value} 格");
                }
                int emptyCount = mapData.Belongs.Count - belongCounts.Values.Sum();
                _output.WriteLine($"  无归属(0xFF): {emptyCount} 格");
            }, readOnly: true);
        });

        var removeCmd = new Command("remove", "删除指定格子归属");
        var removeColArg = new Argument<int>("col") { Description = "列" };
        var removeRowArg = new Argument<int>("row") { Description = "行" };
        removeCmd.Arguments.Add(removeColArg);
        removeCmd.Arguments.Add(removeRowArg);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(removeColArg);
                int row = parseResult.GetValue(removeRowArg);
                ModifierResult result = default;
                RecordSingleBelongChange(col, row, $"删除归属 ({col},{row})",
                    () => result = belong.Remove(col, row));
                _output.WriteLine(result.Message ?? $"已删除归属 ({col},{row})");
            });
        });

        var copyCmd = new Command("copy", "复制指定格子归属值");
        var copyColArg = new Argument<int>("col") { Description = "列" };
        var copyRowArg = new Argument<int>("row") { Description = "行" };
        copyCmd.Arguments.Add(copyColArg);
        copyCmd.Arguments.Add(copyRowArg);
        copyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(copyColArg);
                int row = parseResult.GetValue(copyRowArg);
                var result = belong.CopyBelongValue(col, row);
                _output.WriteLine(result.Message ?? $"已复制归属 ({col},{row})");
            }, readOnly: true);
        });

        var pasteCmd = new Command("paste", "粘贴归属值到指定格子");
        var pasteColArg = new Argument<int>("col") { Description = "列" };
        var pasteRowArg = new Argument<int>("row") { Description = "行" };
        pasteCmd.Arguments.Add(pasteColArg);
        pasteCmd.Arguments.Add(pasteRowArg);
        pasteCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(pasteColArg);
                int row = parseResult.GetValue(pasteRowArg);
                ModifierResult result = default;
                RecordSingleBelongChange(col, row, $"粘贴归属 ({col},{row})",
                    () => result = belong.PasteBelongValue(col, row));
                _output.WriteLine(result.Message ?? $"已粘贴归属 ({col},{row})");
            });
        });

        var cleanOrphanCmd = new Command("clean-orphan", "清理孤立归属(无建筑/单位的格子)");
        cleanOrphanCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordBelongChange("清理孤立归属", () => count = belong.CleanOrphanBelongs());
                _output.WriteLine($"已清理 {count} 个孤立归属");
            });
        });

        var randomizeCmd = new Command("randomize", "随机化所有归属");
        randomizeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordBelongChange("随机化所有归属",
                    () => count = belong.RandomizeBelongs(new Random()));
                _output.WriteLine($"已随机化 {count} 个格子的归属");
            });
        });

        // 注意：这个命令改的是"已经有归属的格子"（0xFF 与 0 保持不动），
        // 并不是按省区操作 —— 需要按省区请用 belong set-province，
        // 需要连无归属格子一起刷请用 belong set-region。
        var batchSetCmd = new Command("batch-set", "把所有【已有归属】的格子统一改为目标ID（无归属格子不动）");
        var batchTargetArg = new Argument<int>("targetId") { Description = "目标国家ID(0-255)" };
        batchSetCmd.Arguments.Add(batchTargetArg);
        batchSetCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int targetId = Math.Clamp(parseResult.GetValue(batchTargetArg), 0, 255);
                int count = 0;
                RecordBelongChange($"批量设置归属 ({targetId})",
                    () => count = belong.BatchSetBelongByProvince(targetId));
                _output.WriteLine($"已将 {count} 个已有归属的格子改为国家ID {targetId}");
            });
        });

        var removeBorderEntitiesCmd = new Command("remove-border-entities", "删除归属边界上的单位与建筑");
        removeBorderEntitiesCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordEntityChange("删除地图边缘的建筑、单位与陷阱",
                    () => count = belong.RemoveBorderEntities());
                _output.WriteLine($"已删除 {count} 个边界上的单位与建筑");
            });
        });

        var removeBorderArmiesCmd = new Command("remove-border-armies", "删除归属边界上的单位");
        removeBorderArmiesCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordEntityChange("删除地图边缘的单位",
                    () => count = belong.RemoveBorderArmies());
                _output.WriteLine($"已删除 {count} 个边界上的单位");
            });
        });

        // ---- 补齐 Modifier 已有、但此前没有命令入口的归属能力 ----

        var getCmd = new Command("get", "查询指定格子的归属值");
        var getColArg = new Argument<int>("col") { Description = "列" };
        var getRowArg = new Argument<int>("row") { Description = "行" };
        getCmd.Arguments.Add(getColArg);
        getCmd.Arguments.Add(getRowArg);
        getCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(getColArg);
                int row = parseResult.GetValue(getRowArg);

                int? value = belong.GetBelongValue(col, row);
                if (!value.HasValue)
                {
                    _output.WriteLine($"坐标 ({col},{row}) 超出范围");
                    return;
                }

                if (value.Value == 0xFF)
                {
                    _output.WriteLine($"({col},{row}) 归属= 无归属(0xFF)");
                    return;
                }

                var legionInfo = mapData.ResolveLegionByBelong(value.Value);
                string name = legionInfo != null ? $" ({legionInfo.Value.DisplayName})" : "";
                _output.WriteLine($"({col},{row}) 归属= {value.Value}{name}");
            }, readOnly: true);
        });

        var replaceCmd = new Command("replace", "批量替换归属值（把 oldId 的格子全部改成 newId）");
        var replaceOldArg = new Argument<int>("oldId") { Description = "原归属值(0-255)" };
        var replaceNewArg = new Argument<int>("newId") { Description = "新归属值(0-255)" };
        replaceCmd.Arguments.Add(replaceOldArg);
        replaceCmd.Arguments.Add(replaceNewArg);
        replaceCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int oldId = Math.Clamp(parseResult.GetValue(replaceOldArg), 0, 255);
                int newId = Math.Clamp(parseResult.GetValue(replaceNewArg), 0, 255);

                int count = 0;
                RecordBelongChange($"批量替换归属 ({oldId} → {newId})",
                    () => count = belong.ReplaceBelong(oldId, newId));
                _output.WriteLine($"已将 {count} 个格子的归属从 {oldId} 改为 {newId}");
            });
        });

        var randomizeByCmd = new Command("randomize-by", "随机化指定归属值的格子");
        var randomizeByIdArg = new Argument<int>("belongId") { Description = "目标归属值(0-255)" };
        randomizeByCmd.Arguments.Add(randomizeByIdArg);
        randomizeByCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int targetId = Math.Clamp(parseResult.GetValue(randomizeByIdArg), 0, 255);

                int count = 0;
                RecordBelongChange($"随机化归属 ({targetId})",
                    () => count = belong.RandomizeBelongByTargetId(targetId, new Random()));
                _output.WriteLine($"已随机化 {count} 个归属为 {targetId} 的格子");
            });
        });

        var setByCityCmd = new Command("set-by-city", "按城市名称批量设置归属（匹配建筑名称）");
        var cityNameArg = new Argument<string>("cityName") { Description = "城市名称，如 北京" };
        var cityBelongArg = new Argument<int>("belongId") { Description = "归属值(0-255)" };
        setByCityCmd.Arguments.Add(cityNameArg);
        setByCityCmd.Arguments.Add(cityBelongArg);
        setByCityCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                string cityName = parseResult.GetValue(cityNameArg)!;
                int belongId = Math.Clamp(parseResult.GetValue(cityBelongArg), 0, 255);

                // 城市名 → 城市ID：名称必须已经在城市名称表里
                //（即此前用 building add -n 写入过的名字）
                int? cityId = ConfigManager.Instance.GetStringTableParser().FindCityIdByName(cityName);
                if (!cityId.HasValue)
                {
                    _output.WriteLine($"没有名为「{cityName}」的城市：该名称还没写入过城市名称表");
                    return;
                }

                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordBelongChange($"按城市名设置归属 ({cityName} → {belongId})", () =>
                {
                    foreach (var building in mapData.Buildings)
                    {
                        if (building.Name != cityId.Value) continue;
                        int col = building.Coordinate % mapData.MapWidth;
                        int row = building.Coordinate / mapData.MapWidth;
                        belong.SetBelongByCountryId(col, row, belongId);
                        count++;
                    }
                });

                _output.WriteLine(count > 0
                    ? $"已将「{cityName}」所在的 {count} 个格子归属设为 {belongId}"
                    : $"地图上没有名为「{cityName}」的建筑");
            });
        });

        var setRegionCmd = new Command("set-region", "把矩形区域内的格子批量设为指定归属");
        var regionCol1Arg = new Argument<int>("col1") { Description = "角点1 列" };
        var regionRow1Arg = new Argument<int>("row1") { Description = "角点1 行" };
        var regionCol2Arg = new Argument<int>("col2") { Description = "角点2 列" };
        var regionRow2Arg = new Argument<int>("row2") { Description = "角点2 行" };
        var regionBelongArg = new Argument<int>("belongId") { Description = "归属值(0-255)" };
        setRegionCmd.Arguments.Add(regionCol1Arg);
        setRegionCmd.Arguments.Add(regionRow1Arg);
        setRegionCmd.Arguments.Add(regionCol2Arg);
        setRegionCmd.Arguments.Add(regionRow2Arg);
        setRegionCmd.Arguments.Add(regionBelongArg);
        setRegionCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col1 = Math.Min(parseResult.GetValue(regionCol1Arg), parseResult.GetValue(regionCol2Arg));
                int col2 = Math.Max(parseResult.GetValue(regionCol1Arg), parseResult.GetValue(regionCol2Arg));
                int row1 = Math.Min(parseResult.GetValue(regionRow1Arg), parseResult.GetValue(regionRow2Arg));
                int row2 = Math.Max(parseResult.GetValue(regionRow1Arg), parseResult.GetValue(regionRow2Arg));
                int belongId = Math.Clamp(parseResult.GetValue(regionBelongArg), 0, 255);

                if (col1 < 0 || row1 < 0 || col2 >= mapData.MapWidth || row2 >= mapData.MapHeight)
                {
                    _output.WriteLine($"区域超出地图范围（地图 {mapData.MapWidth}x{mapData.MapHeight}）");
                    return;
                }

                int count = (col2 - col1 + 1) * (row2 - row1 + 1);
                RecordBelongChange($"区域设置归属 ({col1},{row1})-({col2},{row2}) → {belongId}", () =>
                {
                    for (int row = row1; row <= row2; row++)
                        for (int col = col1; col <= col2; col++)
                            mapData.SetBelongValue(col, row, belongId);
                });
                _output.WriteLine($"已将 ({col1},{row1})-({col2},{row2}) 共 {count} 个格子的归属设为 {belongId}");
            });
        });

        var setProvinceCmd = new Command("set-province", "按省会格子定位省区，把整片省区设为指定归属");
        var provinceColArg = new Argument<int>("capitalCol") { Description = "省会所在格子的列" };
        var provinceRowArg = new Argument<int>("capitalRow") { Description = "省会所在格子的行" };
        var provinceBelongArg = new Argument<int>("belongId") { Description = "归属值(0-255)" };
        setProvinceCmd.Arguments.Add(provinceColArg);
        setProvinceCmd.Arguments.Add(provinceRowArg);
        setProvinceCmd.Arguments.Add(provinceBelongArg);
        setProvinceCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int capitalCol = parseResult.GetValue(provinceColArg);
                int capitalRow = parseResult.GetValue(provinceRowArg);
                int belongId = Math.Clamp(parseResult.GetValue(provinceBelongArg), 0, 255);

                if (capitalCol < 0 || capitalRow < 0 ||
                    capitalCol >= mapData.MapWidth || capitalRow >= mapData.MapHeight)
                {
                    _output.WriteLine($"坐标 ({capitalCol},{capitalRow}) 超出范围");
                    return;
                }

                // 省区值约定为省会格子的线性索引
                int capitalIndex = capitalRow * mapData.MapWidth + capitalCol;
                int total = mapData.MapWidth * mapData.MapHeight;
                int count = 0;

                RecordBelongChange($"省区设置归属 (省会 {capitalCol},{capitalRow} → {belongId})", () =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        ref var province = ref mapData.GetProvinceRef(i);
                        if (province.ProvinceValue != capitalIndex) continue;

                        mapData.SetBelongValueByIndex(i, belongId);
                        count++;
                    }
                });

                _output.WriteLine(count > 0
                    ? $"已将该省区的 {count} 个格子归属设为 {belongId}"
                    : $"({capitalCol},{capitalRow}) 不是一个省会格子（该格省区值不等于自身索引）");
            });
        });

        cmd.Subcommands.Add(clearCmd);
        cmd.Subcommands.Add(setCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(copyCmd);
        cmd.Subcommands.Add(pasteCmd);
        cmd.Subcommands.Add(fillCmd);
        cmd.Subcommands.Add(statsCmd);
        cmd.Subcommands.Add(cleanOrphanCmd);
        cmd.Subcommands.Add(randomizeCmd);
        cmd.Subcommands.Add(batchSetCmd);
        cmd.Subcommands.Add(removeBorderEntitiesCmd);
        cmd.Subcommands.Add(removeBorderArmiesCmd);
        cmd.Subcommands.Add(getCmd);
        cmd.Subcommands.Add(replaceCmd);
        cmd.Subcommands.Add(randomizeByCmd);
        cmd.Subcommands.Add(setByCityCmd);
        cmd.Subcommands.Add(setRegionCmd);
        cmd.Subcommands.Add(setProvinceCmd);

        return cmd;
    }

    /// <summary>
    /// 归属批量变更的统一入口：非脚本模式下记录撤销。
    /// <para>
    /// 脚本批量执行（<c>run xxx.zme</c>）时直接执行、不记撤销 —— 一份脚本动辄几百条命令，
    /// 每条都存一份全图归属快照会撑爆撤销栈，而且脚本本身属于一次性操作。
    /// </para>
    /// </summary>
    private void RecordBelongChange(string description, Action applyChange)
    {
        if (_batchMode)
        {
            applyChange();
            return;
        }

        EditModeManager.Instance.RecordMultiCellBelongChange(description, applyChange);
    }

    /// <summary>
    /// 单格归属变更的撤销入口。与 <see cref="RecordBelongChange"/> 同理，
    /// 脚本批量执行时不记撤销。
    /// </summary>
    private void RecordSingleBelongChange(int col, int row, string description, Action applyChange)
    {
        if (_batchMode)
        {
            applyChange();
            return;
        }

        EditModeManager.Instance.RecordBelongChange(col, row, description, applyChange);
    }

    /// <summary>
    /// 实体（建筑/单位/陷阱）变更的撤销入口，走建筑快照。
    /// 脚本批量执行时不记撤销。
    /// </summary>
    private void RecordEntityChange(string description, Action applyChange)
    {
        if (_batchMode)
        {
            applyChange();
            return;
        }

        EditModeManager.Instance.RecordBuildingsSnapshot(description, applyChange);
    }

    private Command BuildLegionCommand()
    {
        var cmd = new Command("legion", "军团修改操作");

        var listCmd = new Command("list", "列出所有军团");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legions = mapData.Legions;
                _output.WriteLine($"军团总数: {legions.Count}");
                _output.WriteLine();
                _output.WriteLine($"{"ID",-4} {"名称",-20} {"颜色",-12} {"玩家控制",-10}");
                _output.WriteLine(new string('-', 50));
                foreach (var legion in legions)
                {
                    _output.WriteLine($"{legion.CountryId,-4} {legion.DisplayName,-20} ({legion.ColorR:X2}{legion.ColorG:X2}{legion.ColorB:X2})     {(legion.IsPlayerControlled == 1 ? "是" : "否"),-10}");
                }
            }, readOnly: true);
        });

        var setColorCmd = new Command("set-color", "设置军团颜色");
        var setColorIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        var setColorRArg = new Argument<int>("r") { Description = "红色(0-255)" };
        var setColorGArg = new Argument<int>("g") { Description = "绿色(0-255)" };
        var setColorBArg = new Argument<int>("b") { Description = "蓝色(0-255)" };
        setColorCmd.Arguments.Add(setColorIdArg);
        setColorCmd.Arguments.Add(setColorRArg);
        setColorCmd.Arguments.Add(setColorGArg);
        setColorCmd.Arguments.Add(setColorBArg);
        setColorCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int legionId = parseResult.GetValue(setColorIdArg);
                byte r = (byte)Math.Clamp(parseResult.GetValue(setColorRArg), 0, 255);
                byte g = (byte)Math.Clamp(parseResult.GetValue(setColorGArg), 0, 255);
                byte b = (byte)Math.Clamp(parseResult.GetValue(setColorBArg), 0, 255);
                var result = legion.SetLegionColor(legionId, r, g, b);
                _output.WriteLine(result.Message ?? $"已设置军团 {legionId} 颜色为 ({r},{g},{b})");
            });
        });

        var setActionCmd = new Command("set-action", "设置军团ActionId");
        var setActionIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        var setActionValArg = new Argument<int>("actionId") { Description = "ActionId" };
        setActionCmd.Arguments.Add(setActionIdArg);
        setActionCmd.Arguments.Add(setActionValArg);
        setActionCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int legionId = parseResult.GetValue(setActionIdArg);
                int actionId = parseResult.GetValue(setActionValArg);
                var result = legion.SetLegionActionId(legionId, actionId);
                _output.WriteLine(result.Message ?? $"已设置军团 {legionId} ActionId={actionId}");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(setColorCmd);
        cmd.Subcommands.Add(setActionCmd);

        return cmd;
    }

    private Command BuildArmyCommand()
    {
        var cmd = new Command("army", "部队修改操作");

        var listCmd = new Command("list", "列出所有部队");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var armies = mapData.Armies;
                _output.WriteLine($"部队总数: {armies.Count}");
                if (armies.Count > 0)
                {
                    _output.WriteLine();
                    _output.WriteLine($"{"序号",-6} {"类型",-6} {"坐标",-10} {"军团",-6}");
                    _output.WriteLine(new string('-', 30));
                    for (int i = 0; i < armies.Count; i++)
                    {
                        var army = armies[i];
                        int col = army.Coordinate % mapData.MapWidth;
                        int row = army.Coordinate / mapData.MapWidth;
                        _output.WriteLine($"{i,-6} {army.UnitType,-6} ({col},{row})    {army.LegionId,-6}");
                    }
                }
            }, readOnly: true);
        });

        var removeAllCmd = new Command("remove-all", "删除所有部队");
        removeAllCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int count = mapData.Armies.Count;
                mapData.Armies.Clear();
                _output.WriteLine($"已删除 {count} 个部队");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(removeAllCmd);

        return cmd;
    }

    private Command BuildTrapCommand()
    {
        var cmd = new Command("trap", "陷阱修改操作");

        var listCmd = new Command("list", "列出所有陷阱");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var traps = mapData.Traps;
                _output.WriteLine($"陷阱总数: {traps.Count}");
                if (traps.Count > 0)
                {
                    _output.WriteLine();
                    _output.WriteLine($"{"序号",-6} {"坐标",-10} {"军团",-6} {"组织",-6} {"血量",-6}");
                    _output.WriteLine(new string('-', 40));
                    for (int i = 0; i < traps.Count; i++)
                    {
                        var trap = traps[i];
                        int col = trap.Coordinate % mapData.MapWidth;
                        int row = trap.Coordinate / mapData.MapWidth;
                        _output.WriteLine($"{i,-6} ({col},{row})    {trap.LegionId,-6} {trap.Organization,-6} {trap.Health,-6}");
                    }
                }
            }, readOnly: true);
        });

        var removeAllCmd = new Command("remove-all", "删除所有陷阱");
        removeAllCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int count = mapData.Traps.Count;
                mapData.Traps.Clear();
                _output.WriteLine($"已删除 {count} 个陷阱");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(removeAllCmd);

        return cmd;
    }

    /// <summary>
    /// check 命令：检查 btl 文件的格式完整性。
    /// <para>
    /// 直接读文件、不经过编辑模式，所以不需要 <c>WithModifiers</c> 的写入口
    /// （只在"省略路径、用当前地图"时才去问一下 <c>MapData.FilePath</c>）。
    /// </para>
    /// </summary>
    private Command BuildCheckCommand()
    {
        var cmd = new Command("check", "检查 btl 文件格式与玩法规则（省略路径则检查当前地图）");

        var fileArg = new Argument<string?>("file")
        {
            Description = "要检查的 btl 文件路径；省略则检查当前打开的地图文件",
            Arity = ArgumentArity.ZeroOrOne
        };
        cmd.Arguments.Add(fileArg);

        // 报告可能很长（上千条），默认完整输出到控制台；
        // --out 可另外落一份完整报告到文件，便于检索与比对。
        var outOpt = new Option<string?>("--out", "-o")
        {
            Description = "把完整报告写入指定文件（控制台仍会完整输出）"
        };
        cmd.Options.Add(outOpt);

        cmd.SetAction(parseResult =>
        {
            string? path = parseResult.GetValue(fileArg);
            var lines = new List<string>();

            // 统一收集，末尾一次性输出；指定 --out 时再落盘
            void Log(string s) { lines.Add(s); }

            if (string.IsNullOrWhiteSpace(path))
            {
                // 没给路径 → 用当前地图的文件路径
                string? current = null;
                WithModifiers((mapData, _) => current = mapData.FilePath, readOnly: true);

                if (string.IsNullOrWhiteSpace(current))
                {
                    _output.WriteLine("当前地图还没有对应的文件（可能尚未保存或来自新建）。");
                    _output.WriteLine("用法: check <btl文件路径>");
                    return;
                }

                path = current;
            }

            var report = BTLFormatChecker.CheckFile(path!);

            // ToText 已排好版，整段收进 lines
            foreach (var line in report.ToText().Split('\n'))
                Log(line.TrimEnd('\r'));

            // 规则层：玩法规则校验（对齐 Java 版 Wc4StageDAO.check）。
            // Check 内部在副本上跑一遍修复逻辑，因此不会改动文件。
            try
            {
                var ruleIssues = BTLRuleChecker.Check(File.ReadAllBytes(path!));
                Log("");
                if (ruleIssues.Count == 0)
                {
                    Log("[规则] 玩法规则全部通过");
                }
                else
                {
                    Log($"[规则] 发现 {ruleIssues.Count} 处不符合玩法规则（可用 fix 命令修复）：");
                    // 不截断：问题常常上千条，截断会让人误以为只有列出的这些
                    foreach (var issue in ruleIssues)
                        Log($"  [{issue.Level}] {issue.Message}");
                }
            }
            catch (Exception ex)
            {
                Log($"[规则] 校验失败: {ex.Message}");
            }

            // 首都是独立列表（MapData.Capitals），判定需要建筑数据，
            // 只能对当前打开的地图做；对文件路径的校验不涉及此项。
            if (string.IsNullOrWhiteSpace(parseResult.GetValue(fileArg)))
            {
                try
                {
                    WithModifiers((mapData, _) =>
                    {
                        var capitalIssues = BTLRuleChecker.CheckCapitals(mapData);
                        if (capitalIssues.Count == 0)
                        {
                            Log("[首都] 全部通过");
                            return;
                        }

                        Log($"[首都] 发现 {capitalIssues.Count} 处问题：");
                        foreach (var issue in capitalIssues)
                            Log($"  {issue.Message}");
                    }, readOnly: true);
                }
                catch (Exception ex)
                {
                    Log($"[首都] 校验失败: {ex.Message}");
                }
            }

            // ---- 统一输出 ----
            foreach (var line in lines)
                _output.WriteLine(line);

            // ---- 可选：落盘完整报告 ----
            string? outPath = parseResult.GetValue(outOpt);
            if (string.IsNullOrWhiteSpace(outPath)) return;

            try
            {
                File.WriteAllLines(outPath, lines);
                _output.WriteLine($"报告已写入：{outPath}（共 {lines.Count} 行）");
            }
            catch (Exception ex)
            {
                _output.WriteLine($"写入报告失败: {ex.Message}");
            }
        });

        return cmd;
    }

    /// <summary>
    /// fix 命令：按玩法规则修复 btl（对齐 Java 版纠错）。
    /// <para>
    /// 与 check 不同，这个命令<b>会修改文件</b>，所以写回前先备份为 .bak。
    /// </para>
    /// </summary>
    private Command BuildFixCommand()
    {
        var cmd = new Command("fix", "按玩法规则修复 btl 文件（会写回文件，自动备份为 .bak）");

        var fileArg = new Argument<string?>("file")
        {
            Description = "要修复的 btl 文件路径；省略则修复当前打开的地图文件",
            Arity = ArgumentArity.ZeroOrOne
        };
        cmd.Arguments.Add(fileArg);

        cmd.SetAction(parseResult =>
        {
            string? path = parseResult.GetValue(fileArg);

            // 省略路径时先取当前地图的文件路径，走与「fix <路径>」完全一致的字节层修复；
            // 之后再做只有 MapData 层能做的：首都（需要 Capitals 列表）、
            // 港口方向（需要四邻地形，复用 BuildingModifier 的智能设置）。
            bool alsoFixMapData = false;

            if (string.IsNullOrWhiteSpace(path))
            {
                string current = "";
                WithModifiers((mapData, _) => current = mapData.FilePath, readOnly: true);

                if (string.IsNullOrWhiteSpace(current))
                {
                    _output.WriteLine("当前地图还没有对应的文件（可能尚未保存或来自新建）。");
                    _output.WriteLine("用法: fix <btl文件路径>");
                    return;
                }

                path = current;
                alsoFixMapData = true;
            }

            if (!File.Exists(path))
            {
                _output.WriteLine($"文件不存在: {path}");
                return;
            }

            try
            {
                byte[] data = File.ReadAllBytes(path);

                // 规则层：建筑设施 / 单位参数 / 军团 / 归属 / 区划 等
                int count = BTLRuleChecker.Fix(data);

                // 地形组 ID 纠错（对应 Java 版 E 键 checkMapTerrainIds）。
                // 取不到合法 ID 表时返回 -1，表示未做任何修改，不要计入修复数。
                int terrainFixed = BTLFormatChecker.FixTerrainGroups(data);
                if (terrainFixed > 0) count += terrainFixed;

                File.WriteAllBytes(path, data);

                _output.WriteLine($"已修复 {count} 处，结果已写回：{path}");
                if (terrainFixed < 0)
                    _output.WriteLine("（地形组 ID 检查已跳过：未取到合法地形表）");

                if (alsoFixMapData)
                {
                    WithModifiers((mapData, mgr) =>
                    {
                        int capitals = BTLRuleChecker.FixCapitals(mapData);
                        var building = mgr.GetModifier<BuildingModifier>()!;
                        int appearances = building.SmartSetBuildingAppearance(mapData);

                        _output.WriteLine($"另外修复首都 {capitals} 处、港口方向 {appearances} 处");
                    });
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"修复失败: {ex.Message}");
            }
        });

        return cmd;
    }

    private Command BuildInfoCommand()
    {
        var cmd = new Command("info", "显示地图信息");

        cmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var header = mapData.Header;
                _output.WriteLine("========== 地图信息 ==========");
                _output.WriteLine($"  地图尺寸:    {header.MapWidth} x {header.MapLength}");
                _output.WriteLine($"  总格子数:    {header.TotalTiles}");
                _output.WriteLine($"  军团数:      {mapData.Legions.Count}");
                _output.WriteLine($"  建筑数:      {mapData.Buildings.Count}");
                _output.WriteLine($"  省份数据:    {mapData.Provinces.Count} 格");
                _output.WriteLine($"  归属数据:    {mapData.Belongs.Count} 格");
                _output.WriteLine($"  地形数据:    {mapData.Terrains.Count} 格");

                int provinceCount = mapData.Provinces.Count(p => p.ProvinceValue != 0);
                int belongCount = 0;
                for (int i = 0; i < mapData.Belongs.Count; i++)
                    if (mapData.GetBelongValueByIndex(i) != 0xFF) belongCount++;
                _output.WriteLine($"  有效省份:    {provinceCount} 格");
                _output.WriteLine($"  有效归属:    {belongCount} 格");
                _output.WriteLine("==============================");
            }, readOnly: true);
        });

        return cmd;
    }

    private bool _batchMode;
    private int _batchCommandCount;

    public void BeginBatchMode()
    {
        _batchMode = true;
        _batchCommandCount = 0;
        var ctx = CommandManager.Instance.GetContext();
        ctx?.MapData?.RebuildBuildingCoordIndex();
    }

    public void EndBatchMode()
    {
        _batchMode = false;
        var ctx = CommandManager.Instance.GetContext();
        ctx?.MapData?.InvalidateBuildingCoordIndex();
        if (_batchCommandCount > 0)
            DataModified?.Invoke();
        _batchCommandCount = 0;
    }

    private void WithModifiers(Action<MapData, EditModeManager> action, bool readOnly = false)
    {
        var ctx = CommandManager.Instance.GetContext();
        if (ctx?.MapData == null)
        {
            _output.WriteLine("地图数据未加载");
            return;
        }

        var mapData = ctx.MapData;
        var mgr = EditModeManager.Instance;

        if (!mgr.IsInitialized)
            mgr.Initialize(mapData);

        action(mapData, mgr);

        if (!readOnly)
        {
            if (_batchMode)
                _batchCommandCount++;
            else
                DataModified?.Invoke();
        }
    }

    public event Action? DataModified;

    private Command BuildRunCommand()
    {
        var cmd = new Command("run", "执行脚本文件(.zme)");

        var fileArg = new Argument<string>("file") { Description = "脚本文件路径(.zme)，支持绝对路径或相对于程序目录的路径" };
        cmd.Arguments.Add(fileArg);

        cmd.SetAction(parseResult =>
        {
            var filePath = parseResult.GetValue(fileArg)!;

            if (!Path.IsPathRooted(filePath))
                filePath = Path.Combine(AppContext.BaseDirectory, filePath);

            if (!filePath.EndsWith(".zme", StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine("[脚本] 仅支持 .zme 脚本文件");
                return;
            }

            if (!File.Exists(filePath))
            {
                _output.WriteLine($"[脚本] 文件不存在: {filePath}");
                return;
            }

            _output.WriteLine($"[脚本] 加载脚本文件: {Path.GetFileName(filePath)}");

            var lines = File.ReadAllLines(filePath);
            int executedCount = 0;
            int skippedCount = 0;

            BeginBatchMode();
            try
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();

                    if (string.IsNullOrEmpty(line)) continue;

                    // 支持行尾注释：belong set 1 2 3   # 说明文字
                    line = StripComment(line);
                    if (string.IsNullOrEmpty(line))
                    {
                        skippedCount++;
                        continue;
                    }

                    if (line.StartsWith(';') || line.StartsWith('#') || line.StartsWith("//"))
                    {
                        skippedCount++;
                        continue;
                    }

                    var args = CommandLineToArgs(line);
                    var parse = _rootCommand.Parse(args);
                    parse.Invoke();

                    executedCount++;
                }
            }
            finally
            {
                EndBatchMode();
            }

            _output.WriteLine($"[脚本] 执行完成: {executedCount} 条命令, {skippedCount} 条注释跳过, 共 {lines.Length} 行");
        });

        return cmd;
    }
}