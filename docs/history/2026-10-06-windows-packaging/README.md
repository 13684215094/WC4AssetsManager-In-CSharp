# WC4 地图编辑器：本地整合版说明

本仓库构建的是《世界征服者 4》资源和地图的 **Windows WPF 桌面编辑器**，不是 Android 游戏、APK 或原生库。WPF 主程序目标框架为 `net10.0-windows`；Core、Rendering、CLI 和测试目标框架为 `net10.0`。二进制解析、命令行工具和回归测试可在 Linux 上运行，WPF 界面仍需在 Windows 上验收。

这份 README 以 **当前本地 `6.0` 相对指定上游 `6.0` 的差异** 为主线，记录改动的用途、使用方法和边界。上游分支本身没有 `README.md`；本文也是本地新增文件。

| 对象 | 固定比较引用 |
|---|---|
| 指定上游 | `2132937983/WC4AssetsManager-In-CSharp` 的 `6.0`，提交 `7d31340` |
| 当前本地 | `6.0`，以 `7d31340` 为上游基线 |
| fork | `13684215094/WC4AssetsManager-In-CSharp` 的 `6.0`，提交 `00fba1e` |
| 游戏对照资料 | `/home/j60100428/game`，测试和核对时只读使用 |

本地整合提交 `51c8486` 以 `7d31340` 为父提交；后续 `2cf53b8` 补充忽略规则和审计报告。`origin/6.0` 与 `upstream/6.0` 没有共同祖先，因此这里的“同步上游”指在上游源码基线上保留本地修复和 fork 的非生成资源，不是把 fork 的十个旧提交逐个重放。2026-10-06 经代理核对两个远端 SHA 后，执行 `git pull --rebase upstream 6.0` 返回 `Current branch 6.0 is up to date.`；2026-10-07 再次启动代理并 fetch，确认两远端仍分别为 `00fba1e` 和 `7d31340`。后文的“上游”均指这一已获取的 `upstream/6.0`。

## 1. 与上游相比，改动有多少

写本文前执行 `git diff --name-status upstream/6.0...HEAD`，共有 **225 个差异路径：新增 179 个，修改 46 个，没有删除路径**。

| 范围 | 新增 | 修改 | 主要内容 |
|---|---:|---:|---|
| `Resource/` | 157 | 1 | 保留 fork 的 XML/INI/TXT 数据；改技能模板 |
| `WC4MapEditor.Core/` | 9 | 35 | BTL/World 读写、地图变换、将领/兵种、资源审计 |
| `Views/` | 1 | 4 | 审计窗口、创建地图、将领界面、地图命令反馈 |
| `WC4MapEditor.Rendering/` | 0 | 3 | 将领头像查找 |
| `WC4MapEditor.Cli/` | 0 | 1 | 详细导出、资源审计和修复写回 |
| `WC4MapEditor.Tests/` | 6 | 0 | 测试项目及回归用例 |
| 工程、配置、文档 | 6 | 2 | 输出目录、忽略规则、manifest、README 等 |
| **合计** | **179** | **46** | 统计包含文档和大体量资源，不等于代码行数 |

上游已有的 Lua 工程、JSON/布局编辑场景、省份生成器、WPF 主界面等仍保留；它们不应被误写成本地新增功能。完整的逐文件差异可在文末用 Git 命令复核。

## 2. BTL、World 格式及安全保存

### 2.1 统一 BTL 布局

本地新增 `BtlLayout`，并让 `BTLParser`、`StageParser`、`ConquestParser`、`BTLAnalyzer` 及上游的格式/规则检查器共用一套区段布局。主要变化：

- 固定 128 字节头部，按头部计数计算军团、地形、省份、归属、建筑、部队、陷阱、方案、天气、事件、援军、空袭、Placement A/B、首都、opaque、策略建筑、空援和 v3 extra 的位置与完整文件长度。
- v1 的部队和援军分别使用 48/80 字节记录；v2、v3 使用 64/104 字节记录。原先仅把 v3 当新版记录的部署入口也已修正，避免 v2 数据被 v1 序列化覆盖。
- 地形区按实际地图面积处理，省份/归属区按头部容量处理，并保留容量对齐的 padding。`MapNumber == 0` 的 BTL 包含本地地形；引用外部 world 的征服 BTL 不把外部地形误读成文件内区段。
- 加载要求文件大小与完整布局精确匹配。负计数、溢出、截断、容量不足和尾部多余数据会报错，避免“能打开但保存后丢数据”。
- 无修改往返时保留 reserved 字节、opaque 记录、v3 extra、Placement A/B 边界、对齐 padding 及旧文件的零容量头部行为。
- `BTLFormatChecker`、`BTLRuleChecker` 改为读取共享布局。v2/v3 部队不会交给 v1 规则修复器写回；外部 world 的所有者编码、海上建筑也不再被无条件改动。

`StageParser`、`ConquestParser` 的大段重复偏移/保存代码已由兼容入口和共享 codec 替代，战役、征服调用接口仍保留。主要文件：

- `WC4MapEditor.Core/Parsers/BTL/BtlLayout.cs`、`BTLParser.cs`、`BattleParser.cs`、`BTLFormatChecker.cs`、`BTLRuleChecker.cs`
- `WC4MapEditor.Core/Parsers/Stage/StageParser.cs`、`WC4MapEditor.Core/Parsers/Conquest/ConquestParser.cs`
- `WC4MapEditor.Core/Analyzers/BTLAnalyzer.cs`

### 2.2 World 文件

`WC4MapEditor.Core/Parsers/World/WorldParser.cs` 按 YSAE/v4 读写：头部宽高为 32 位小端整数；每格有 16 字节地形平面及独立的 2 字节省份平面。期望文件长度为 `16 + 面积 * 18` 字节，magic、版本、尺寸或长度不符即拒绝读取。此修复保留原始数据中的非零字段。

### 2.3 写文件保护

新增 `WC4MapEditor.Core/Parsers/AtomicFile.cs`：单文件先完成序列化、写同目录临时文件并刷新，再替换目标；无效地图不会先截断原文件。将领 JSON 与头像 XML 的双文件保存先分别暂存，发生可恢复 I/O 错误时尝试回滚已替换文件。这个过程 **不是断电级事务**，操作原始游戏资源前仍要自行保留独立副本。CLI 的 BTL 修复入口先写 `<文件>.bak`，再原子写回。

## 3. 地图变换、大小和坐标

新增 `MapLimits.cs`、`MapTransform.cs`，并修改 `MapData.cs`、`TerrainModifier.cs`、`UndoManager.cs`、`MapChangeCommands.cs`、部署模式和地图 WPF 入口。扩边、裁剪和缩放会对候选副本同步处理地形、省份、归属、建筑、部队、陷阱、援军、空袭、方案、首都、Placement A/B 和头部计数；成功才替换当前地图并写入撤销记录。碰撞、越界或编码失败时原地图不变。

| 限制 | 本地规则 |
|---|---|
| 地图面积 | 正宽高，最多 1,000,000 格 |
| BTL/world 文件 | 最多 256 MiB，且必须符合已知布局 |
| 变换与撤销快照 | 单独按 256 MiB 预算控制，旧历史可能被丢弃 |
| 部队、陷阱坐标 | 有符号短整数：`0..32767` |
| 建筑坐标 | 无符号短整数：`0..65535` |
| 缩放比例 | 有限数值，`0.1..10.0`；目标格使用最近格采样 |

原创建窗口按每边 `10..500` 限制尺寸；现在调用统一面积校验。这些数字是 **编辑器的内存预算和二进制字段限制**，不是 Android 游戏能运行任意大地图的证明。超出字段编码范围的单位、陷阱、建筑在插入前直接拒绝，不再保存成被截断的坐标。

引用外部 world 的 BTL（`MapNumber != 0`）目前只允许 **宽度和原点不变的底部追加**。左右/顶部移动、裁剪及缩放需要同步修改外部 world 和伴随资源，因此在本编辑器中被拒绝。带非空 opaque/extra 区段的地图也会阻止改变坐标的变换，因为这些未知数据可能含坐标引用。地图控制台命令示例：

```text
resize_map down 10 ocean
scale_map 1.5
```

WPF 控制台现在报告变换失败原因；失败不再显示“完成”，也不会留下部分修改或错误撤销记录。地图尺寸变化不会自动延长外部 world、图集、地图文字、镜头或 Android 原生代码。

## 4. 将领、兵种及数据保护

本地调整 `GeneralSettingParser.cs`、`GeneralSettingData.cs`、`Views/GeneralEditScene.cs`，新增 `GeneralSkillRules.cs`、`LegacyJsonConverters.cs`，并修正三个 Skia 部队/援军渲染器的头像查找。

- 将领 JSON 保留 ID 0、null、缺省属性、未知字段和重复 ID；选择/删除按实际行处理，不把重复 ID 静默合并。
- `ResetSkills` 作为数量处理，真实数据中的 5、10 都可保留；加载失败不会以部分解析结果覆盖原 JSON。
- 技能 ID token、数字范围、军衔和 BTL 五个技能槽在写入前校验。十技能 JSON 可编辑/保存，但 **没有扩充 BTL 五槽或游戏 UI/SO**。
- 头像以 `Photo` 为优先键，兼容 `EName`；头像 XML 未知节点和属性得到保留，异步预览随选择变化取消。
- 新将领使用未占用的正 ID 和五个默认技能槽；只补编辑器数据/头像位置，不会自动添加美术、解锁条件或跨文件引用。
- 兵种族按 `ArmySettings.Army` 查找，而不是完整记录的 `Id`；技能等级从 `SkillSettings.Level` 读取。模板中的无效占位技能 ID `1..50` 已换成实际资源里的 ID。
- 自动分配会跨部队和援军汇总已用将领 ID，按所有权组过滤并去重，正确写 `Rank`，清理空槽等级；v1/v3 均遵守五技能槽约束。
- 资源扫描、清空、强制重载和保存会刷新相关缓存；语言缓存按 locale 区分。切换资源根后必须重载将领编辑器，防止保存到旧目录。

相关文件还包括 `AssetManager.cs`、`AssetCache.cs`、`ConfigManager.cs`、`AssetSettings.cs`、`ArmyModifier.cs`、`ArmyV3Modifier.cs`、`Resource/Config/GeneralSpecialtyTemplates.json`。

## 5. 资源关系审计及 CLI

新增 `WC4MapEditor.Core/Analyzers/AssetRelationshipAnalyzer.cs` 和 `Views/AssetAuditWindow.cs`，在资源浏览器、将领界面及 CLI 中加入 **只读的数据检查/查询引用**。审计明确覆盖 32 类 JSON 关系，包含将领技能、晋升、称号、关卡、兵种特性/增益、精英、设施、奖励和部分商店关系；可选扫描 `stage/**/*.btl` 中已部署的部队。

援军字段含义尚未充分确认：报告计数并标明跳过，不对其引用作确定断言。overlay 按 **整文件** 覆盖 base，不按 ID 合并；损坏的 overlay 不会静默回退到 base。审计可读未保存的将领编辑快照，但不写入输入资产。导出报告必须位于输入目录之外的新路径，拒绝覆盖已有文件或经过目录链接。

命令示例：

```bash
dotnet run --project WC4MapEditor.Cli -- asset audit /path/to/assets
dotnet run --project WC4MapEditor.Cli -- asset audit /path/to/overlay/assets \
  --base /path/to/base/assets --include-maps
dotnet run --project WC4MapEditor.Cli -- asset audit /path/to/assets \
  --general 1903 --output tmp/general-1903-audit.json
```

审计退出码：`0` 没有 error 诊断，`1` 有 error，`2` 调用、读取或导出失败。它不是删除将领、迁移所有引用或自动修复资源的安全闸门。

CLI 还修正了大 world 的完整详细 JSON 导出、v2 扩展部队字段和整型坐标，并提供战役、征服、world 的分析命令：

```bash
dotnet run --project WC4MapEditor.Cli -- stage stage.btl --detailed -o stage.json
dotnet run --project WC4MapEditor.Cli -- conquest conquest1.btl --analyze
dotnet run --project WC4MapEditor.Cli -- world world.bin --detailed -o world.json
```

CLI JSON 用于检查和展示，**不是无损 JSON→BTL 导入格式**。数据保真应使用二进制 codec 的逐字节往返测试。

## 6. fork 资源、工程和版本差异

相对上游新增的 **157 个 `Resource/` 文件**均与 fork `00fba1e` 逐 blob 相同：`Resource/WC4DATA/assets/` 下 152 个，`Resource/Texture/` 下 4 个，另有 `Resource/Geo/jiuzhou.txt`。它们包括 `layout.xml`、`layout_x.xml`、多语言字符串表、动画/单位/地形/教程 XML、图集描述及地理坐标资料。`setting.txt` 和本地内存计划文档也从 fork 的非生成文件中保留。当前 `Resource/` 有 255 个跟踪路径，上游有 98 个。

这些新增文件主要是 **XML/INI/TXT 描述和配置**。仓库没有配套 PNG/JPG/ICO 美术文件，保留 XML 不等于 WPF 可以显示所有图片。资源应与目标游戏版本匹配；不能只按文件名混用 1.28、1.30 和 mod 数据。尤其是 `CountryTechSettings.json`：当前仍采用 **上游的 260 条版本**，它不是本地相对上游的改动；fork/1.28 版本为 259 条，另外还有既有字段差异。编辑原始游戏包时应选用相应版本的资源，而不是把上游版本默认视为官方数据。

构建和仓库管理方面：

- 新增 `Directory.Build.props`，把中间文件与输出放在被忽略的 `tmp/obj`、`tmp/bin`；本地整合树不跟踪旧 fork 的 48 个 `obj/` 生成文件。
- `WC4MapEditor.csproj` 排除测试源码；可选图标 `name.ico` 不存在时不再阻断编译。WPF/Core/Rendering/脚本工程的项目关系继续保留。
- 新增 `app.manifest`，声明 PerMonitorV2 DPI、长路径及 Windows 兼容性；`.gitignore` 增加 IDE、测试、覆盖率、发布和 NuGet 产物规则。
- `docs/memory-optimization-plan.md` 是本地计划，`reanme.md` 是详细仓库审计与历史记录，不属于上游代码功能。

## 7. 构建、测试及已验证的边界

需要 .NET 10 SDK。本机可使用 `/home/j60100428/game/tmp/dotnet/dotnet`（版本 `10.0.401`）；以下命令假设 `dotnet` 已加入 `PATH`：

```bash
dotnet build WC4MapEditor.Cli/WC4MapEditor.Cli.csproj
dotnet run --project WC4MapEditor.Tests -- --corpus /home/j60100428/game
dotnet build WC4MapEditor.csproj -r win-x64 -p:EnableWindowsTargeting=true
```

测试语料参数 `--corpus` 可省略，省略后仍运行合成回归用例。2026-10-07 在当前代码上实际运行含语料的测试，结果为 **52 passed, 0 failed**；`/home/j60100428/game/wc4/` 中 4092 份 BTL 保存后逐字节一致，world 文件、4 张将领表（4810 行）、头像 XML 和资源审计用例通过。测试覆盖 v1/v2/v3、map ID 0/1、padding/opaque/extra、非方形地图、扩边/裁剪/缩放、撤销/重做、越界拒绝、CLI 导出和 overlay 资源。测试过程显示既有的 nullable 和未使用字段警告，没有测试失败。

同日运行上面的 Windows `win-x64` 交叉构建，结果为 **成功，9 个警告、0 个错误**。这 9 个警告来自 WPF 项目中可能为空的引用和未使用字段。Linux 的交叉构建只能检查能否编译，不能在 Linux 上运行 WPF。Windows 的 Debug 输出位于：

```text
tmp/bin/WC4MapEditor/Debug/net10.0-windows/win-x64/
```

启动 `WC4MapEditor.exe` 时需保留整个输出目录，并提供与目标游戏版本匹配的 `Resource/`、`setting.txt`、必要的 `Maps/` 和原生依赖。Debug 构建不是自包含发布包，仍需要 .NET 10 Desktop Runtime。

尚需 Windows 实机验收地图加载、渲染、编辑、保存、脚本和撤销流程。当前测试 **不能证明 Android 游戏支持任意尺寸地图**，也不覆盖其镜头、点击、寻路、AI、回合、存档、事件和原生 SO。关联 game 仓的 148×60 地图是实验候选，不是已经补全南半球的可发布地图。

## 8. 逐文件核对入口

代码差异集中在以下路径；各模块的原因与行为已在第 2–6 节说明。

| 模块 | 相对上游变化的文件 |
|---|---|
| 二进制读写 | `BtlLayout.cs`、`BTLParser.cs`、`BattleParser.cs`、`StageParser.cs`、`ConquestParser.cs`、`WorldParser.cs`、`AtomicFile.cs`、`BTLAnalyzer.cs`、`BTLFormatChecker.cs`、`BTLRuleChecker.cs`，以及版本化部队/援军模块 |
| 地图状态与编辑 | `MapLimits.cs`、`MapTransform.cs`、`MapData.cs`、`MapChangeCommands.cs`、`UndoManager.cs`、`TerrainModifier.cs`、`EditModeManager.cs`、`BuildingModifier.cs`、`TrapModifier.cs`、三个部署模式及部分二进制模型 |
| 将领与资产 | `GeneralSettingParser.cs`、`LegacyJsonConverters.cs`、`GeneralSkillRules.cs`、`GeneralSettingData.cs`、`AssetManager.cs`、`AssetCache.cs`、`AssetSettings.cs`、`ConfigManager.cs`、`ArmyModifier.cs`、`ArmyV3Modifier.cs`、技能模板 |
| UI 与渲染 | `AssetAuditWindow.cs`、`AssetBrowserScene.xaml.cs`、`BeginScene.xaml.cs`、`GeneralEditScene.cs`、`RenderSceneBase.cs`、`ArmyRender.cs`、`ReinforceRender.cs`、`ReinforceRenderNew.cs` |
| CLI 与测试 | `WC4MapEditor.Cli/Program.cs`，`WC4MapEditor.Tests/` 的项目文件及五个 C# 测试文件 |
| 工程与报告 | `.gitignore`、`Directory.Build.props`、`WC4MapEditor.csproj`、`app.manifest`、`README.md`、`reanme.md` 和内存计划 |

若需获得 **全部 225 个确切路径**及原始补丁，以当前本地 HEAD 执行：

```bash
git status --short --branch
git log --oneline upstream/6.0..HEAD
git diff --stat upstream/6.0...HEAD
git diff --name-status upstream/6.0...HEAD
git diff upstream/6.0...HEAD -- README.md
git diff upstream/6.0...HEAD -- WC4MapEditor.Core/Parsers/BTL/BTLParser.cs
git diff upstream/6.0...HEAD -- Resource/Config/GeneralSpecialtyTemplates.json
git ls-files | rg '(^|/)(obj|bin)/'   # 应没有输出
```

上游若有新提交，应重新确认其 SHA、复核共享 BTL 布局和资源版本，然后再 rebase、构建和运行回归测试。旧 fork 与上游无共同祖先，不应通过大范围 `ours/theirs` 覆盖来代替逐项核对。
