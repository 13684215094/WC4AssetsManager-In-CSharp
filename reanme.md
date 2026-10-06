# WC4 地图编辑器现状、上游差异与游戏资源对照

- 审计日期：2026-10-02（UTC）
- 工作目录：`/home/j60100428/WC4AssetsManager-In-CSharp`
- 指定上游：`https://github.com/2132937983/WC4AssetsManager-In-CSharp.git`，`6.0` 分支
- 对照资料：`/home/j60100428/game`

> 文件名按本次要求保留为 `reanme.md`。第 1-7 节和附录记录更新前的只读审计快照；第 8 节记录首次整合；第 9-10 节记录 2026-10-06 的远端核对、压缩与内容差异；第 11-13 节记录随后遇到的网络及权限限制；第 14 节记录网络恢复后的实际同步。前文数字以各节审计当时的 Git 对象和工作树为准，不能当成当前未提交修改统计。

> 当前结论（2026-10-06）：本地 `6.0` 已在上游 `7d31340` 上整合源码和 fork 资源，整合提交为 `51c8486`。通过本机代理实时确认上游仍为 `7d31340`、fork 仍为 `00fba1e`；`git pull --rebase upstream 6.0` 已成功，分支无须重放。fork 的 10 个提交不应笼统称为用户的私有提交，详见第 11 节。

## 1. 核心结论

1. **当前项目修改有两层。** 本地 `6.0` 的已提交基线是 `00fba1e6cd00c98c9dcdbfef73bf6ba408b9de09`；其上还有 44 个已跟踪文件修改和 18 个未跟踪新增文件，共 62 个项目变更文件。已跟踪文件净差异为 `+1258/-3052` 行；未跟踪文件另有 2607 行，不能从 `git diff --stat` 中看到。本文档 `reanme.md` 是本次审计后新增的第 19 个未跟踪文件，属于报告本身，不计入前述项目变更。所有项目变更文件均未提交。
2. **主要工程方向是可靠读写和安全编辑。** BTL 和 World 格式读写、地图变换/撤销/保存、将领与兵种资料、资源关系审计、CLI 和回归测试都有实质变化。`StageParser`、`ConquestParser` 的大量删除是共享 codec 替代重复实现，不是简单移除战役/征服能力。
3. **与指定 GitHub 上游存在实质冲突。** 上游 `6.0` 当前为 `7d3134091ec2c797e614cc6e39789af8a35db34a`，2026-09-18 的新历史；本地 `00fba1e` 与它没有共同祖先，无法快进或做普通三方合并。两树 312 个同路径文件中 66 个内容不同；当前工作树的 62 个改动里有 22 个落在这些分歧路径上。即便先提交本地工作，直接执行 `git merge` 仍需显式允许无关历史并手动处理这些文件。
4. **上游也新增了功能。** 它有新的 JSON 编辑场景、布局编辑器、Lua 脚本工程、地图生成器和 BTL 检查器，不能把上游视为单纯的旧版。反过来，上游的旧 BTL 偏移/版本分支与本地已验证的统一布局不同；直接取上游解析器会回退本地的保真修复。
5. **`game` 是关联证据仓，不是本 C# 仓的上游。** 其中含 1.26/1.28 资源、1.30 APK 解包研究、148x60 地图实验和 1.30 数据迁移候选。它本身有未提交修改，本次只读取它，不把这些实验内容归为 C# 编辑器的已实现能力。
6. **当前本地验证通过。** .NET 10.0.401 下 48 组测试全部通过，覆盖 4092 个真实 BTL 逐字节往返和 4 张真实将领表/4810 行语义往返；CLI 与 Windows `win-x64` 交叉构建均为 0 警告、0 错误。Windows GUI 和 Android 真机行为仍需分别验收。

## 2. 审计口径与仓库关系

### 2.1 三个不同对象

| 对象 | 当前标识 | 用途及注意点 |
|---|---|---|
| 本地源码仓 `HEAD` | `6.0 @ 00fba1e` | 本报告“改了什么”的已提交基线；其工作树另有 62 个文件未提交 |
| 当前 `origin` | 指向 `13684215094` 的 `wget.la` 代理 URL，`origin/6.0 @ 00fba1e` | 本地配置的远端，不等同于用户指定的 GitHub 上游 |
| 指定 GitHub 上游 | `6.0 @ 7d31340`，默认分支也是 `6.0` | 通过 `git ls-remote` 和 `git fetch` 实际核对；本次 fetch 只更新 `FETCH_HEAD` |
| 相邻 `game` 仓 | `main @ bf81d56`，也有未提交文件 | 游戏资源、逆向脚本、独立地图实验与文档；不是编辑器 Git 历史的一部分 |

`git merge-base HEAD FETCH_HEAD` 返回非零：**没有共同祖先**。`git log --left-right HEAD...FETCH_HEAD` 因此只是在列出两条互不相关的历史，不能把它解读为“本地落后上游两次提交”。GitHub 上游仅有本次远程查询返回的 `6.0` 分支；其顶端提交信息为“新增json数据解析场景”，父提交为 `089712c` 的“Initial commit”。本地已有 10 个提交的独立历史。

### 2.2 如何复核

```bash
cd /home/j60100428/WC4AssetsManager-In-CSharp
git status --short --branch
git diff --stat HEAD
git ls-files --others --exclude-standard
git ls-remote --symref https://github.com/2132937983/WC4AssetsManager-In-CSharp.git HEAD 'refs/heads/*'
git fetch --no-tags https://github.com/2132937983/WC4AssetsManager-In-CSharp.git refs/heads/6.0
git merge-base HEAD FETCH_HEAD  # 无共同祖先时返回非零
git diff --name-status HEAD FETCH_HEAD
```

不要把 `git diff HEAD FETCH_HEAD` 当作可直接套用的补丁。它是在两个无共同祖先的树之间比较最终文件内容；显示的删除可能只是上游重建时没有带过旧资源，不表示应该删除本地资源。审计时两树分别有 519 和 367 个跟踪文件：312 个路径相同，207 个只在本地基线，55 个只在指定上游。同路径的 312 个中 246 个对象完全相同、66 个不同。当前工作树内容与上游比较则有 88 个同路径文件不同，因为部分本地未提交修改又改变了原先相同的文件。

## 3. 当前工作树究竟修改了什么

### 3.1 总量和组成

| 范围 | 文件数 | 可见行数/说明 |
|---|---:|---|
| 已跟踪文件 `M` | 44 | `git diff --stat`: 新增 1258、删除 3052 |
| 未跟踪新增文件 | 18 | 当前文件合计 2607 行；不含在 `git diff --stat` 中 |
| 工作树合计 | 62 | 若将未跟踪文件按当前内容视为新增，约为新增 3865、删除 3052 行 |

此表固定统计**写报告之前**的项目状态。写入本报告后，`git status` 会显示 44 个 `M` 和 19 个未跟踪文件；第 19 个就是 `reanme.md`。

这些改动目前**没有新提交**。现有 `.gitignore` 采用默认忽略、按扩展名放行的写法；新改动增加 `.csproj`、`.md`、`.props`、manifest 等放行规则，并忽略 `bin/obj/tmp`。新增 `Directory.Build.props` 把中间和输出文件放到仓内 `tmp/`，避免旧的已跟踪 `obj/` 文件干扰编译；`WC4MapEditor.csproj` 排除测试代码，并允许缺少可选图标时继续构建。`app.manifest` 补充 DPI/长路径等 Windows 声明。`README.md` 是本地新增的使用与边界说明。报告文件 `reanme.md` 只记录审计结论，不改变构建输入。

### 3.2 BTL、World 与保存链

核心变化从“多个入口各自算区段偏移”收敛为一条明确链路：

```text
StageParser / ConquestParser / CLI / GUI
                    |
                BTLParser
                    |
                BtlLayout  -> MapData -> MapTransform / 编辑器
                    |
                AtomicFile -> 目标文件
```

- 新增 `WC4MapEditor.Core/Parsers/BTL/BtlLayout.cs`。它以 128 字节头计算完整区段，区分实际面积和头部平面容量，按 v1=48/80、v2/v3=64/104 字节解释部队/援军，并在每段累加时检查负计数、整数溢出和 256 MiB 文件预算。
- 修改 `WC4MapEditor.Core/Parsers/BTL/BTLParser.cs`。加载要求文件长度与头部推导布局**精确相等**；保存保留省份/归属 padding、Placement A/B 边界、`0x6c` opaque 记录、v3 extra 字节和模型 reserved 字节；保存前重新校验集合与版本。
- 新增 `WC4MapEditor.Core/Parsers/BTL/BattleParser.cs` 作为旧接口兼容层；`StageParser.cs`、`ConquestParser.cs` 变为很薄的入口。两文件分别约从 700/940 行缩减为 19/20 行，这是 3052 行删除的主要来源。
- 修改 `WC4MapEditor.Core/Parsers/World/WorldParser.cs`，按 YSAE/v4 读写 32 位 LE 宽高、每格 16 字节主平面和独立 2 字节尾平面；头、尺寸与**完整文件长度**不符就拒绝。
- 新增 `WC4MapEditor.Core/Parsers/AtomicFile.cs`。单文件先完整序列化、写同目录临时文件再替换；将领 JSON 与头像 XML 的多文件保存使用暂存/可恢复失败回滚。多文件流程没有断电级事务保证。
- `Army`、`Army_3`、`Building`、`Legion`、`Reinforcement`、`Terrain` 等模型补齐保留字段和坐标检查；`BTLAnalyzer` 改为读取共享布局，避免报告与保存器给出不同偏移。

这批修改解决的是编辑器对**已知格式**的错位、漏字段和截断风险。逐字节往返证明未编辑数据可保真，但不自动证明每个未知字段的业务含义已知。

### 3.3 地图变换、撤销与坐标限制

新增 `MapLimits.cs`、`MapTransform.cs`，修改 `MapData.cs`、`TerrainModifier.cs`、`MapChangeCommands.cs`、`UndoManager.cs`、`EditModeManager.cs` 及部署模式。扩边、裁剪、缩放先处理候选副本，并同步移动地形、省份、归属、建筑、部队、陷阱、援军、空军、方案、首都、放置列表与相关头部计数；失败不提交半成品。撤销/重做使用完整 `MapData` 快照，并给历史设置独立内存预算。部队/陷阱 0..32767、建筑 0..65535 的磁盘坐标限制在写入前检查。

编辑器自己的面积上限为 100 万格；BTL/World 文件上限 256 MiB；缩放系数要求有限且在 0.1..10。它们是**编辑器安全预算及字段编码约束**，不是游戏引擎对地图大小的承诺。关联外部 world 的征服 BTL 当前只允许宽度和原点不变的**底部追加**；左右/顶部移动、裁剪、缩放需要外部 world 联动事务，本实现直接拒绝。包含非空 opaque/extra 段时也拒绝会改动坐标的变换。大范围自动生成部队/陷阱有比面积预算更严格的坐标界限。

### 3.4 将领、兵种、缓存和资源关系审计

- `GeneralSettingParser.cs`、`GeneralSettingData.cs`、`Views/GeneralEditScene.cs` 处理 JSON 中 ID 0、null/缺省/未知字段、重复 ID 的对象级选择、`ResetSkills` 计数、真实 `Photo` 键、头像 XML 未知节点/属性保留、失败加载阻止覆盖和异步头像预览取消。双文件保存先序列化/暂存再替换。
- `AssetManager.cs`、`AssetCache.cs`、`ConfigManager.cs` 与新 `AssetSettings.cs`、`GeneralSkillRules.cs` 修正资源根和语言缓存，按 `ArmySettings.Army` 查兵种族、按 `SkillSettings.Level` 查技能级别。`Resource/Config/GeneralSpecialtyTemplates.json` 用真实存在的技能 ID 替换无效种子。
- `ArmyModifier.cs`、`ArmyV3Modifier.cs` 修复国家范围自动分配、已用 ID 汇总、军衔字段与五个 BTL 技能槽约束。十技能 JSON 能保留/编辑；这**没有**扩充 BTL 五槽、游戏 UI 或 Android SO。
- 新增 `AssetRelationshipAnalyzer.cs`、`Views/AssetAuditWindow.cs`，并在资源浏览器、将领编辑器和 CLI 提供只读数据检查。当前明确覆盖 32 条 JSON 关系和可选 BTL 已部署部队；援军字段含义未确认，审计计数但不对其引用作断言。overlay 是整文件覆盖 base，不按行 ID 合并；坏 overlay 不回退到 base。
- `WC4MapEditor.Cli/Program.cs` 补齐大 world 的详细导出、v2 扩展记录与 `int` 坐标，并提供 `asset audit` 的诊断、过滤和 JSON 输出。CLI JSON 是检查格式，不是无损 JSON→BTL 格式。

### 3.5 逐文件索引

下表列出本次 62 个工作树文件，`M` 为已跟踪修改，`A` 为未跟踪新增。更细的调用链、字段与设计动机，可交叉阅读 `game/docs/MAP_EDITOR_CHANGE_REPORT.md` 与 `game/docs/MAP_EDITOR_FILE_BY_FILE.md`；这两份也是 `game` 工作树中的未提交文档，不能代替本报告对 Git 状态的实测。

| 模块 | `M` 文件 | `A` 文件 |
|---|---|---|
| 工程/说明 | `.gitignore`、`WC4MapEditor.csproj` | `Directory.Build.props`、`README.md`、`app.manifest` |
| 二进制解析/保存 | `WC4MapEditor.Core/Analyzers/BTLAnalyzer.cs`、`WC4MapEditor.Core/Parsers/BTL/BTLArmyModule.cs`、`WC4MapEditor.Core/Parsers/BTL/BTLParser.cs`、`WC4MapEditor.Core/Parsers/BTL/BTLReinforcementModule.cs`、`WC4MapEditor.Core/Parsers/Conquest/ConquestParser.cs`、`WC4MapEditor.Core/Parsers/Stage/StageParser.cs`、`WC4MapEditor.Core/Parsers/World/WorldParser.cs` | `WC4MapEditor.Core/Parsers/BTL/BattleParser.cs`、`WC4MapEditor.Core/Parsers/BTL/BtlLayout.cs`、`WC4MapEditor.Core/Parsers/AtomicFile.cs` |
| 地图状态/模型 | `WC4MapEditor.Core/Models/Army.cs`、`Army_3.cs`、`BTLHeader.cs`、`Building.cs`、`Legion.cs`、`MapData.cs`、`Reinforcement.cs`、`Terrain.cs`、`Trap.cs`（本行后 8 个短名均在同一 Models 目录） | `WC4MapEditor.Core/Models/MapLimits.cs`、`WC4MapEditor.Core/Models/MapTransform.cs` |
| 操作/撤销/部署 | `WC4MapEditor.Core/Commands/CliCommandHost.cs`、`MapChangeCommands.cs`、`UndoManager.cs`；`WC4MapEditor.Core/Mode/ArmyDeployMode.cs`、`BuildingDeployMode.cs`、`ReinforcementDeployMode.cs`；`WC4MapEditor.Core/Modifiers/BuildingModifier.cs`、`EditModeManager.cs`、`TerrainModifier.cs`、`TrapModifier.cs`；`Views/BeginScene.xaml.cs`、`Views/RenderSceneBase.cs` | 无 |
| 将领/资产/渲染 | `Resource/Config/GeneralSpecialtyTemplates.json`、`Views/GeneralEditScene.cs`；`WC4MapEditor.Core/Assets/AssetCache.cs`、`AssetManager.cs`；`WC4MapEditor.Core/Config/ConfigManager.cs`；`WC4MapEditor.Core/Models/GeneralSettingData.cs`；`WC4MapEditor.Core/Parsers/General/GeneralSettingParser.cs`；`WC4MapEditor.Core/Modifiers/ArmyModifier.cs`、`ArmyV3Modifier.cs`；`WC4MapEditor.Rendering/Skia/ArmyRender.cs`、`ReinforceRender.cs`、`ReinforceRenderNew.cs` | `WC4MapEditor.Core/Models/AssetSettings.cs`、`WC4MapEditor.Core/Models/GeneralSkillRules.cs` |
| 检查/CLI/UI | `Views/AssetBrowserScene.xaml.cs`、`WC4MapEditor.Cli/Program.cs` | `WC4MapEditor.Core/Analyzers/AssetRelationshipAnalyzer.cs`、`Views/AssetAuditWindow.cs` |
| 测试 | 无 | `WC4MapEditor.Tests/WC4MapEditor.Tests.csproj`、`Program.cs`、`TransformTests.cs`、`CliTests.cs`、`GeneralDataTests.cs`、`AssetAuditTests.cs`（短名均在 Tests 目录） |

## 4. 指定上游的内容与冲突

### 4.1 上游新增了什么

指定 GitHub 上游顶端新增了多个 WPF 编辑场景：兵种、ArmyBuff、集团军及事件、设施、征服事件、国家科技、技能、布局等；还增加 `WC4MapEditor.Scripting/` 的 Lua 工程、`Core/Scripting/` 脚本适配、地图省份生成服务、`BTLFormatChecker`、`BTLRuleChecker`、若干 JSON/XML/图集/布局解析器及 CLI 命令。`Views/RenderSceneBase.cs` 在上游约 4551 行，本地工作树约 3026 行，说明它还改了大量 UI 入口与交互，并非小规模补丁。上游 `Resource/WC4DATA/assets/json/CountryTechSettings.json` 也与本地内容不同，属于实际数据选择冲突。

上游跟踪的资源文件较少：本地 `HEAD` 的 `Resource/` 为 254 个路径，上游为 97 个；上游新增的一批编辑场景可能依赖仓库没有一并带齐的图片、XML 或外部工具。文件树对比不能判断运行包资源是否完整，合并前需要用匹配资源副本做 GUI 验收。当前两树对比里本地独有 207 个路径，含大量旧资源和历史 `obj`；上游独有 55 个路径，主要为新 Core/View/脚本代码。不要机械地把所有“只在本地”文件删掉。

### 4.2 机械合并会发生什么

因为两个提交没有共同祖先，Git 默认拒绝合并。以空树作为**诊断用假设基线**执行只读 `git merge-tree <empty-tree> HEAD FETCH_HEAD`，得到 66 个 `added in both` 同路径冲突；这与两树 66 个相同路径、不同 blob 的统计一致。它只是潜在冲突清单，**没有执行真实合并**，也没有检测后续 C# 编译或业务语义冲突。

| 冲突层 | 实测数量 | 解释 |
|---|---:|---|
| 两个已提交树的同路径内容差异 | 66 | 无共同基线时均需人工择取/融合；按路径分组：Core 34、Views 14、Rendering 11、Services 2、工程/资源等 5 |
| 当前未提交文件叠加上述路径 | 22 | 最容易误覆盖本地修复；例如 `MapData`、`StageParser`、`ConquestParser`、将领解析器、地图编辑 GUI |
| 当前未提交文件、上游该路径内容相同于本地 `HEAD` | 22 | 对上游来说仍有新的本地工作树改动；不是 Git 树级冲突，但应用时要保存 |
| 当前 18 个未跟踪新增文件 | 18 | 上游树中均无同路径文件；仍需检查新上游类型/接口/构建包含规则 |
| 两树同路径且对象完全相同 | 246 | 文件本身无树级内容冲突；调用方改变后仍可能有集成问题 |

66 个冲突路径按层次可以这样定位：

- 工程/服务/数据：`.gitignore`、`App.xaml.cs`、`WC4MapEditor.csproj`、`Resource/WC4DATA/assets/json/CountryTechSettings.json`、`Services/RenderSceneManager.cs`、`Services/WpfDialogService.cs`；以及 Core/Rendering 的两个 `.csproj`。
- UI：`Views/BeginScene.xaml.cs`、`Views/RenderSceneBase.cs`、`Views/MainWindow.xaml.cs`、`Views/MapRenderScene.cs`、5 个对话框，以及 Assist 中的归属、笔刷、建筑、调试、军团归属窗口。
- Core：命令/配置/地理坐标、若干 Mode、`MapData`/Army/Building/Legion/TerrainData、各 Modifier、`StageParser`/`ConquestParser`/`GeneralSettingParser`、场景管理和服务接口。
- Rendering：渲染器接口、主渲染器、战术地图编辑、归属/建筑/军团/省份/调试/Skia 核心渲染和视图图层 provider。

逐个路径可用 `git diff --name-status HEAD FETCH_HEAD` 与 `git diff HEAD FETCH_HEAD -- <path>` 复核。模拟命令的空树对象为 Git 标准 `4b825dc642cb6eb9a060e54bf8d69288fbee4904`；本机 Git 2.25.1 不支持新版 `git merge-tree --write-tree`，所以这里没有声称进行了一次真实可编译合并。

### 4.3 需要优先解决的真实集成风险

| 优先级 | 交叉点 | 已看到的证据与实际风险 | 合并时的处理方向 |
|---|---|---|---|
| 高 | BTL 主解析与新检查器 | 本地 `BtlLayout` 在 v2 用 64/104 字节、地形按实际面积、省份/归属按容量；上游 `BTLArmyModule`/`BTLReinforcementModule` 的 `>=3` 分支仍把 v2 当 v1。上游 `BTLFormatChecker`/`BTLRuleChecker` 又用 `StageOffsets`/`ConquestOffsets` 及旧步长算地址 | 保留并复用本地共享布局；将上游检查器改成只读取该布局，再以 v1/v2/v3 与 map ID 0/1 语料校验 |
| 高 | `StageParser` / `ConquestParser` 与上游场景 | 上游场景、场景管理器继续调用 `LoadToMapData` / `SaveFromMapData`，本地兼容层保有该 API；但上游其他新工具还可能直接依赖旧 parser 内部集合、偏移和错误行为 | 按调用点逐一编译修复，不恢复第二套保存逻辑；确保所有保存仍走严格 codec 和原子写 |
| 高 | `RenderSceneBase` / `MapData` / 变换 | 上游新 GUI 仍直接通过 `TerrainModifier.ResizeMap/ScaleMap` 和 `RecordMapResizeChange` 接入；本地变换限制 world-backed BTL 为底部追加，禁止不安全坐标搬移 | 合并 UI 时保留失败提示、完整撤销快照与变换约束；在 Windows 对新场景运行扩图/撤销/另存验收 |
| 高 | `GeneralSettingParser` 与将领界面 | 上游同路径 parser 的加载失败逻辑含“坏行跳过后继续”；本地修复避免用部分列表覆盖原 JSON，并保留未知字段、重复 ID、头像 XML | 保留本地数据保护，适配上游新增视图的调用；用 ID 0、重复 ID、十技能与失败加载样本回归 |
| 中 | `CountryTechSettings.json` | 上游与本地同名资源有 127 行新增/71 行删除，示例 `ResearchLv`、`Lines`、成本等值不同；这是数据内容冲突，不是排版冲突 | 先确定资源所属游戏版本和预期数据源，再按记录 ID/字段比对，不能盲目 `ours/theirs` 覆盖 |
| 中 | 构建图与资源路径 | 上游 Core 新引用 `WC4MapEditor.Scripting`，主工程增加脚本/场景/发布配置；本地 `Directory.Build.props` 移动输出并排除测试源码 | 保留两个项目引用、生成排除规则和新脚本拷贝规则；清洁构建及 Windows 运行时核查资源路径 |
| 中 | 内存与数据兼容 | 上游 `BTLRuleChecker.Fix` 可直接修改二进制，而本地 `asset audit` 是只读；两者的严重度和可信字段范围不同 | 区分只读报告与显式修复入口；对未知字段、援军和版本差异避免静默修补 |

上游 `WorldParser` 可见对宽高采用 `UInt16`、部分头不符时只打印警告，和本地按 YSAE/v4 的 32 位、严格长度校验不是同一个保存语义。合并时应以真实 world 样本和完整往返测试确定最终实现。

## 5. 与 `/home/j60100428/game` 的对应关系

### 5.1 为什么这份资料有用

`game/docs/MAP_EDITOR_CHANGE_REPORT.md` 和 `game/docs/MAP_EDITOR_FILE_BY_FILE.md` 已从原始资源、编译版编辑器和项目调用链梳理本地 62 文件修改；本次重新核对的 44+18 统计、48 组 C# 测试结果与其中记录一致。`game/docs/BTL_MAP_EXPANSION.md` 给出 BTL 原生解析、`148x50`→`148x60` 及配套 world/图集的实验边界；`game/docs/WC4_1.30_ANALYSIS.md` 和 `game/docs/WC4_1.30_MIGRATION.md` 则是 1.30 APK/数据迁移研究。它们与 C# 仓的源码变更相关，但归属不同。

### 5.2 直接核对到的实物

| `game` 中的对象 | 本轮直接观察 | 对 C# 项目的意义 |
|---|---|---|
| `patches/wc4/map-height-148x60/assets/stage/conquest1.btl` | `148x60`、map ID 1、原点 `(0,2)`、容量 8880、91,868 字节；C# CLI 可解析并给出各段偏移 | 支持“编辑器可识别/报告该候选 BTL”；不是 Android 可玩证明 |
| 同一候选的 `assets/world.bin` | C# 详细 CLI 导出为 `148x64`、9472 个地形格 | 与征服窗口上下边距匹配；扩大 BTL 本身不足以扩 world |
| `tmp/wc4-1.30.0/apk/assets/stage/conquest1.btl` | Python 只读工具报告原版 `148x50`、容量 7400、87,428 字节，无损往返 | 1.30 原资源仍是旧尺寸；不能把实验候选误称为官方 1.30 地图 |
| `patches/wc4/1.30-data-migration/assets` + 1.30 APK base | 当前 C# CLI 只读审计覆盖 81 表、1423 张地图；8 个 error、0 warning；82,852 条援军记录跳过字段断言 | 8 个诊断是候选/官方 1.30 数据状态，不是 C# 编译失败；详见下段 |
| `map_5.13_Dev/` | 包含编译编辑器和 `Maps/world2.bin` 等语料 | 可作为格式/资源对照；本次没有运行其 EXE 或修改该目录 |

本次 1.30 候选审计的 8 个错误具体为 `ArmySettings` 的 `349001..349004` 四条 `Feature/FeatureLevel` 长度不一致，以及 `GeneralSettings` 的 `25510..25513` 四条五技能记录 `SkillsMax=0`。`game/docs/WC4_1.30_MIGRATION.md` 标记这些为官方基线已存在的问题；本轮只读命令没有替它们自动修复。审计只覆盖明示的 32 条 JSON 关系、已部署 BTL 部队及可解释字段，不覆盖所有图片、XML、存档、事件和原生代码引用。

`game/patches/wc4/map-height-148x60/` 是地图加载边界实验，文件配齐九个 BTL、world、普通/HD 索引和画布配置。其新增南部地形与国家内容仍有占位；`game/docs/BTL_MAP_EXPANSION.md` 的 CPU 探针、预检只能支持已测试的加载/索引结论，不代表 Android 镜头、寻路、AI、回合和存读档已验收。1.30 数据迁移候选也不是可安装 APK，没有配套 1.30 SO/UI/签名。**C# 仓里的 `MapTransform` 和 `asset audit` 不会自动制作这些游戏运行时资源。**

### 5.3 两个仓库当前都有未提交状态

本 C# 工作树有上述 62 个文件；`game` 的 `README.md`、多份 `docs/`、`tools/wc4_excel.py`、测试以及 1.30 迁移脚本/候选等也有未提交内容。它的远端和提交历史独立于本 C# 仓。本次仅读取 `game` 的资源、脚本与报告，未执行迁移构建或覆盖候选包。后续如果需要同时整理两仓历史，应分别保存工作树快照/提交，再做独立审查。

## 6. 本轮实测与结论边界

| 检查 | 本轮命令/方法 | 结果 | 不能据此推出 |
|---|---|---|---|
| 工作树完整性 | `git status --porcelain -uall`、`git diff --check` | 审计前 44 `M` + 18 新文件；差异空白检查通过 | 未提交代码已经可以安全合入上游 |
| C# 回归 | `/home/j60100428/game/tmp/dotnet/dotnet run --project WC4MapEditor.Tests -- --corpus /home/j60100428/game` | **48 passed, 0 failed**；4092 BTL 逐字节一致；4 表/4810 将领行语义一致 | 1.30 全部地图、Windows GUI、Android 游戏均已验证 |
| CLI 构建 | `.NET 10.0.401 dotnet build WC4MapEditor.Cli/WC4MapEditor.Cli.csproj --no-restore` | 0 warning，0 error | 上游新 CLI 代码已经集成 |
| Windows 目标交叉构建 | `dotnet build WC4MapEditor.csproj -r win-x64 -p:EnableWindowsTargeting=true --no-restore` | 0 warning，0 error | WPF 已在 Windows 实机运行、所有贴图/外部工具齐备 |
| 1.30 候选审计 | `asset audit <候选 assets> --base <1.30 APK assets> --include-maps` | 81 表、1423 BTL、8 errors、0 warnings；只读检查因错误诊断返回非零 | C# 编译失败，或候选已可安装 |
| 地图实验文件 | C# CLI `conquest ... --analyze`、`world ... --detailed`，Python `wc4_btl.py inspect` | 148x60 BTL 及 148x64 world 可被当前工具读取；原 1.30 征服为 148x50 | Android 所有地图流程支持扩高 |
| 指定上游 | `ls-remote`、fetch 到 `FETCH_HEAD`、树对象比较、无关历史树合并模拟 | 没有共同祖先；66 个同路径内容冲突 | 已完成真实 merge 或解决编译/运行语义冲突 |

测试的真实 BTL 语料入口是 `game/wc4/` 下的文件，不包括 `game/tmp/wc4-1.30.0/apk/assets` 中的全部新官方地图；1.30 本轮另做了只读审计和单个征服样本检查。这样区分能防止把 4092 份旧版/整合包往返结果错误推广到所有 1.30 文件。

## 7. 建议的合入与验收顺序

1. **先冻结并标识两边基线。** 本地 62 个未提交文件应先形成可审查的独立提交或快照；上游以固定 SHA `7d31340` 作为整合目标。不要在当前脏工作树上直接 `git pull`、`git merge --allow-unrelated-histories` 或以 `ours/theirs` 大面积覆盖。
2. **先统一格式核心，再接上游功能。** 以当前 `BtlLayout`/`BTLParser`、YSAE/v4、`MapTransform`、`AtomicFile` 为待保留的行为基线，迁入上游新增场景/检查器/脚本时逐个适配接口；特别要移除检查器的第二套 `StageOffsets`/`ConquestOffsets` 和 v2 旧步长。
3. **针对 66 个冲突文件逐项择取。** 优先 parser、`MapData`、`RenderSceneBase`、`GeneralSettingParser`、工程文件和 `CountryTechSettings.json`；后二者既涉及编译/发布，也涉及资产版本选择。编译成功之后还要比较二进制保存结果，不把自动合并无冲突当作语义正确。
4. **建立整合验收矩阵。** 本地 48 组测试、4092 BTL、world 与将领语料必须继续通过；新增上游场景至少验证加载/编辑/另存/撤销；把上游 BTL 检查器跑在 v1/v2/v3、map ID 0/1、非 8 对齐、opaque/extra 样本上，确保报告偏移等于保存偏移。
5. **最后做目标平台验收。** Windows 上用匹配的完整 Resource/Maps 副本检查 WPF 界面、脚本、图集与 DPI；Android 上若使用 `game` 的地图候选，再独立检查镜头、点击、寻路、AI、回合和存档，不把 C# 交叉构建视为游戏运行验证。

本次没有执行上游合并，因此“有没有冲突”的结论是**已经确认历史冲突和 66 个文件级冲突，并识别了高风险语义交叉点**；精确的最终编译报错和运行表现须在隔离整合分支完成取舍后才能确定。

## 附录 A：66 个同路径冲突文件

以下按 Git 路径排序；“工作树再修改”表示该路径除本地 `HEAD` 与上游的差异外，还叠加了本地未提交编辑。66 个中有 22 个属于这一类，其余 44 个虽未被本轮工作树修改，仍存在两个提交树之间的内容差异。

| 路径 | 工作树再修改 |
|---|---|
| `.gitignore` | 是 |
| `App.xaml.cs` | 否 |
| `Resource/WC4DATA/assets/json/CountryTechSettings.json` | 否 |
| `Services/RenderSceneManager.cs` | 否 |
| `Services/WpfDialogService.cs` | 否 |
| `Views/Assist/BelongListWindow.cs` | 否 |
| `Views/Assist/BrushSettingsWindow.cs` | 否 |
| `Views/Assist/BuildingSettingWindow.cs` | 否 |
| `Views/Assist/DebugConsoleWindow.cs` | 否 |
| `Views/Assist/LegionBelongListWindow.cs` | 否 |
| `Views/BeginScene.xaml.cs` | 是 |
| `Views/Dialogs/ConfirmDialog.cs` | 否 |
| `Views/Dialogs/DoubleInputDialog.cs` | 否 |
| `Views/Dialogs/NotificationDialog.cs` | 否 |
| `Views/Dialogs/ObjectPropertiesDialog.cs` | 否 |
| `Views/Dialogs/SingleInputDialog.cs` | 否 |
| `Views/MainWindow.xaml.cs` | 否 |
| `Views/MapRenderScene.cs` | 否 |
| `Views/RenderSceneBase.cs` | 是 |
| `WC4MapEditor.Cli/Program.cs` | 是 |
| `WC4MapEditor.Core/Commands/CliCommandHost.cs` | 是 |
| `WC4MapEditor.Core/Commands/CommandManager.cs` | 否 |
| `WC4MapEditor.Core/Commands/MapChangeCommands.cs` | 是 |
| `WC4MapEditor.Core/Config/ConfigManager.cs` | 是 |
| `WC4MapEditor.Core/Config/SettingTxtParser.cs` | 否 |
| `WC4MapEditor.Core/Geo/GeoCoordinateCalculator.cs` | 否 |
| `WC4MapEditor.Core/Mode/ArmyDeployMode.cs` | 是 |
| `WC4MapEditor.Core/Mode/BelongEditMode.cs` | 否 |
| `WC4MapEditor.Core/Mode/BuildingDeployMode.cs` | 是 |
| `WC4MapEditor.Core/Mode/IModeHandler.cs` | 否 |
| `WC4MapEditor.Core/Mode/LegionEditMode.cs` | 否 |
| `WC4MapEditor.Core/Mode/ModeContext.cs` | 否 |
| `WC4MapEditor.Core/Mode/ProvinceEditMode.cs` | 否 |
| `WC4MapEditor.Core/Mode/TerrainPaintMode.cs` | 否 |
| `WC4MapEditor.Core/Models/Army.cs` | 是 |
| `WC4MapEditor.Core/Models/Building.cs` | 是 |
| `WC4MapEditor.Core/Models/Legion.cs` | 是 |
| `WC4MapEditor.Core/Models/MapData.cs` | 是 |
| `WC4MapEditor.Core/Models/TerrainData.cs` | 否 |
| `WC4MapEditor.Core/Modifiers/ArmyModifier.cs` | 是 |
| `WC4MapEditor.Core/Modifiers/ArmyV3Modifier.cs` | 是 |
| `WC4MapEditor.Core/Modifiers/BelongModifier.cs` | 否 |
| `WC4MapEditor.Core/Modifiers/BuildingModifier.cs` | 是 |
| `WC4MapEditor.Core/Modifiers/EditModeManager.cs` | 是 |
| `WC4MapEditor.Core/Modifiers/LegionModifier.cs` | 否 |
| `WC4MapEditor.Core/Modifiers/ProvinceModifier.cs` | 否 |
| `WC4MapEditor.Core/Modifiers/TerrainModifier.cs` | 是 |
| `WC4MapEditor.Core/Parsers/Conquest/ConquestParser.cs` | 是 |
| `WC4MapEditor.Core/Parsers/General/GeneralSettingParser.cs` | 是 |
| `WC4MapEditor.Core/Parsers/Stage/StageParser.cs` | 是 |
| `WC4MapEditor.Core/SceneManagement/RenderSceneManager.cs` | 否 |
| `WC4MapEditor.Core/Services/IDialogService.cs` | 否 |
| `WC4MapEditor.Core/Services/IViewLayerImageProvider.cs` | 否 |
| `WC4MapEditor.Core/WC4MapEditor.Core.csproj` | 否 |
| `WC4MapEditor.Rendering/IRenderEngine.cs` | 否 |
| `WC4MapEditor.Rendering/Imaging/TacticalMapEditor.cs` | 否 |
| `WC4MapEditor.Rendering/MainRender.cs` | 否 |
| `WC4MapEditor.Rendering/Skia/BelongFlagRender.cs` | 否 |
| `WC4MapEditor.Rendering/Skia/BuildingRender.cs` | 否 |
| `WC4MapEditor.Rendering/Skia/DebugConsole.cs` | 否 |
| `WC4MapEditor.Rendering/Skia/LegionDomainRender.cs` | 否 |
| `WC4MapEditor.Rendering/Skia/ProvinceRender.cs` | 否 |
| `WC4MapEditor.Rendering/Skia/SkiaRenderEngine.cs` | 否 |
| `WC4MapEditor.Rendering/Skia/SkiaViewLayerImageProvider.cs` | 否 |
| `WC4MapEditor.Rendering/WC4MapEditor.Rendering.csproj` | 否 |
| `WC4MapEditor.csproj` | 是 |

## 附录 B：62 个项目变更文件的 Git 明细

行数为相对于本地 `HEAD` 的已跟踪文件 `git diff --numstat`；新增文件按当前换行数统计。上游关系比较的是本地 **`HEAD`** 和指定上游的同路径 blob：`不同` 为附录 A 中的树级冲突，`同` 表示基线文件相同但当前工作树已修改，`无` 表示上游没有该路径。报告自身不在此表。

| 状态 | 文件 | + | - | 上游关系 |
|---|---|---:|---:|---|
| M | `.gitignore` | 10 | 1 | 不同 |
| M | `Resource/Config/GeneralSpecialtyTemplates.json` | 6 | 6 | 同 |
| M | `Views/AssetBrowserScene.xaml.cs` | 23 | 8 | 同 |
| M | `Views/BeginScene.xaml.cs` | 5 | 9 | 不同 |
| M | `Views/GeneralEditScene.cs` | 186 | 127 | 同 |
| M | `Views/RenderSceneBase.cs` | 13 | 8 | 不同 |
| M | `WC4MapEditor.Cli/Program.cs` | 82 | 33 | 不同 |
| M | `WC4MapEditor.Core/Analyzers/BTLAnalyzer.cs` | 46 | 230 | 同 |
| M | `WC4MapEditor.Core/Assets/AssetCache.cs` | 7 | 1 | 同 |
| M | `WC4MapEditor.Core/Assets/AssetManager.cs` | 60 | 21 | 同 |
| M | `WC4MapEditor.Core/Commands/CliCommandHost.cs` | 11 | 7 | 不同 |
| M | `WC4MapEditor.Core/Commands/MapChangeCommands.cs` | 22 | 45 | 不同 |
| M | `WC4MapEditor.Core/Commands/UndoManager.cs` | 18 | 6 | 同 |
| M | `WC4MapEditor.Core/Config/ConfigManager.cs` | 34 | 16 | 不同 |
| M | `WC4MapEditor.Core/Mode/ArmyDeployMode.cs` | 8 | 2 | 不同 |
| M | `WC4MapEditor.Core/Mode/BuildingDeployMode.cs` | 7 | 2 | 不同 |
| M | `WC4MapEditor.Core/Mode/ReinforcementDeployMode.cs` | 1 | 1 | 同 |
| M | `WC4MapEditor.Core/Models/Army.cs` | 9 | 13 | 不同 |
| M | `WC4MapEditor.Core/Models/Army_3.cs` | 8 | 2 | 同 |
| M | `WC4MapEditor.Core/Models/BTLHeader.cs` | 2 | 1 | 同 |
| M | `WC4MapEditor.Core/Models/Building.cs` | 12 | 4 | 不同 |
| M | `WC4MapEditor.Core/Models/GeneralSettingData.cs` | 3 | 1 | 同 |
| M | `WC4MapEditor.Core/Models/Legion.cs` | 3 | 1 | 不同 |
| M | `WC4MapEditor.Core/Models/MapData.cs` | 114 | 49 | 不同 |
| M | `WC4MapEditor.Core/Models/Reinforcement.cs` | 2 | 0 | 同 |
| M | `WC4MapEditor.Core/Models/Terrain.cs` | 8 | 2 | 同 |
| M | `WC4MapEditor.Core/Models/Trap.cs` | 1 | 1 | 同 |
| M | `WC4MapEditor.Core/Modifiers/ArmyModifier.cs` | 49 | 57 | 不同 |
| M | `WC4MapEditor.Core/Modifiers/ArmyV3Modifier.cs` | 48 | 56 | 不同 |
| M | `WC4MapEditor.Core/Modifiers/BuildingModifier.cs` | 8 | 5 | 不同 |
| M | `WC4MapEditor.Core/Modifiers/EditModeManager.cs` | 23 | 10 | 不同 |
| M | `WC4MapEditor.Core/Modifiers/TerrainModifier.cs` | 18 | 214 | 不同 |
| M | `WC4MapEditor.Core/Modifiers/TrapModifier.cs` | 27 | 11 | 同 |
| M | `WC4MapEditor.Core/Parsers/BTL/BTLArmyModule.cs` | 2 | 2 | 同 |
| M | `WC4MapEditor.Core/Parsers/BTL/BTLParser.cs` | 146 | 172 | 同 |
| M | `WC4MapEditor.Core/Parsers/BTL/BTLReinforcementModule.cs` | 2 | 2 | 同 |
| M | `WC4MapEditor.Core/Parsers/Conquest/ConquestParser.cs` | 9 | 929 | 不同 |
| M | `WC4MapEditor.Core/Parsers/General/GeneralSettingParser.cs` | 162 | 65 | 不同 |
| M | `WC4MapEditor.Core/Parsers/Stage/StageParser.cs` | 6 | 696 | 不同 |
| M | `WC4MapEditor.Core/Parsers/World/WorldParser.cs` | 48 | 228 | 同 |
| M | `WC4MapEditor.Rendering/Skia/ArmyRender.cs` | 2 | 2 | 同 |
| M | `WC4MapEditor.Rendering/Skia/ReinforceRender.cs` | 2 | 2 | 同 |
| M | `WC4MapEditor.Rendering/Skia/ReinforceRenderNew.cs` | 2 | 2 | 同 |
| M | `WC4MapEditor.csproj` | 3 | 2 | 不同 |
| A | `Directory.Build.props` | 7 | 0 | 无 |
| A | `README.md` | 272 | 0 | 无 |
| A | `Views/AssetAuditWindow.cs` | 149 | 0 | 无 |
| A | `WC4MapEditor.Core/Analyzers/AssetRelationshipAnalyzer.cs` | 375 | 0 | 无 |
| A | `WC4MapEditor.Core/Models/AssetSettings.cs` | 20 | 0 | 无 |
| A | `WC4MapEditor.Core/Models/GeneralSkillRules.cs` | 73 | 0 | 无 |
| A | `WC4MapEditor.Core/Models/MapLimits.cs` | 40 | 0 | 无 |
| A | `WC4MapEditor.Core/Models/MapTransform.cs` | 142 | 0 | 无 |
| A | `WC4MapEditor.Core/Parsers/AtomicFile.cs` | 80 | 0 | 无 |
| A | `WC4MapEditor.Core/Parsers/BTL/BattleParser.cs` | 159 | 0 | 无 |
| A | `WC4MapEditor.Core/Parsers/BTL/BtlLayout.cs` | 60 | 0 | 无 |
| A | `WC4MapEditor.Tests/AssetAuditTests.cs` | 230 | 0 | 无 |
| A | `WC4MapEditor.Tests/CliTests.cs` | 87 | 0 | 无 |
| A | `WC4MapEditor.Tests/GeneralDataTests.cs` | 503 | 0 | 无 |
| A | `WC4MapEditor.Tests/Program.cs` | 152 | 0 | 无 |
| A | `WC4MapEditor.Tests/TransformTests.cs` | 223 | 0 | 无 |
| A | `WC4MapEditor.Tests/WC4MapEditor.Tests.csproj` | 12 | 0 | 无 |
| A | `app.manifest` | 23 | 0 | 无 |

## 8. 本轮上游更新结果（2026-10-02）

本节覆盖前文“尚未合并”的历史快照；前文的 00fba1e/7d31340 对比仍保留，便于追溯本次决策。

### 8.1 保存与 rebase

- 已将更新前本地工作保存为提交 `50283f4`，并保留备份分支 `backup/local-6.0-before-upstream-20261002`。
- 用户指定的 GitHub fork 已配置为 `origin`，当前 `origin/6.0` 为 `7d3134091ec2c797e614cc6e39789af8a35db34a`；原来的代理远端改名为 `legacy-origin`，没有推送任何内容。
- 由于本地历史与 fork 没有共同祖先，`git rebase --onto origin/6.0 --root` 会在根提交产生大量 add/add 冲突；最终采用“fork 两个提交作为基线，只重放已保存本地提交”的 rebase，结果分支为 `integration/rebase-origin-6.0-20261002`。
- rebase 过程中手工保留本地可靠格式链（`BtlLayout`、BTL/World codec、地图变换、撤销和保存保护）以及上游 JSON 编辑场景、布局编辑器、Lua 工程和资源工具。
- fork 基线缺少本地原有的 159 个非生成路径，已从备份分支恢复，包括 7.7 MiB 的 `Resource` XML/INI、`setting.txt` 和内存计划文档；旧跟踪 `obj/` 生成文件没有恢复。

整合后的提交链为：

```text
089712c Initial commit
  -> 7d31340 新增json数据解析场景
  -> 58236ab Save local editor format, audit, and test work
  -> 31d2e1d Restore local resource metadata and settings absent from fork
  -> 806368a Align fork BTL checks and repairs with shared layout
  -> d170a58 Document upstream rebase and validation results
```

### 8.2 合并后的修复

上游新增 `BTLFormatChecker`/`BTLRuleChecker` 原本使用旧的 `StageOffsets` 和 v1 部队解释。整合提交 `806368a` 做了以下适配：

- 格式检查和规则修复统一使用 `BtlLayout`，按 v1/v2/v3、战役/征服及完整尾段计算偏移和文件长度。
- v2/v3 的 64 字节部队记录不再交给 v1 修复器序列化；外部 world 的征服图不再强行改所有者编码或海上建筑类型。
- 截断、容量对齐和外部 world 样本加入回归测试；`fix` 命令使用 `AtomicFile` 写回，并先保留 `<file>.bak`。

### 8.3 当前验证结果

在隔离整合工作树执行：

| 检查 | 结果 |
|---|---|
| `dotnet build WC4MapEditor.Cli/WC4MapEditor.Cli.csproj --no-restore` | 通过；存在仓库既有 nullable/未使用字段警告，无错误 |
| `dotnet build WC4MapEditor.Tests/WC4MapEditor.Tests.csproj --no-restore` | 通过；存在同一组既有警告，无错误 |
| `dotnet build WC4MapEditor.csproj -r win-x64 -p:EnableWindowsTargeting=true --no-restore` | 通过；WPF `win-x64` 交叉构建无错误 |
| `dotnet run --project WC4MapEditor.Tests -- --corpus /home/j60100428/game` | **52 passed, 0 failed**；4092 份 BTL 逐字节一致，world/将领/资源审计通过 |
| 工作树冲突状态 | `git ls-files -u` 为 0；整合分支干净 |

`/home/j60100428/game` 只作为只读语料使用，未覆盖其工作树修改。GUI 仍需 Windows 实机操作验收；Android 游戏运行时、镜头/寻路/AI/存档也不由这些 C# 测试证明。

### 8.4 主分支切换状态

主仓 `6.0` 已切换到整合提交链并跟踪 `origin/6.0`。`git pull --rebase origin 6.0` 已成功返回“Current branch 6.0 is up to date”；本地工作树干净，当前相对 fork 领先本地提交。更新前的 `50283f4` 仍由 `backup/local-6.0-before-upstream-20261002` 保留。整个过程没有向 GitHub 推送，也没有修改 `/home/j60100428/game`。

## 9. 重新核对两远端与压缩历史（2026-10-06）

第 8 节是 2026-10-02 的操作记录，其中的 `origin` 当时被设为 2132937983 仓；本轮已把远端命名改回通常的关系：`origin` 是 13684215094 fork，`upstream` 是 2132937983 仓。本地 `6.0` 跟踪 `upstream/6.0`，推送目标单独配置为 `origin`。本轮没有推送。

`git stash list` 为空，因为上次使用的是普通 Git 提交：更新前工作树由 `50283f4` 保存，随后在 2132937983 的 `7d31340` 上整合成 5 个本地提交。旧历史分别由 `backup/local-6.0-before-upstream-20261002` 和 `backup/6.0-before-squash-20261006` 保留，可供逐提交核查。

重新从 GitHub 直连 fetch 后，fork `6.0` 为 `00fba1e`（10 个提交），上游 `6.0` 为 `7d31340`（2 个提交）。两条历史的根提交分别是 `fa093e1` 和 `089712c`；`git merge-base origin/6.0 upstream/6.0` 无结果。GitHub 的 fork 关系和 ahead/behind 页面不等于当前 Git 对象仍有共同祖先；上游 2026-09-18 的 `Initial commit` 表明其历史已重新建立。页面的“10 ahead / 2 behind”与两边各自的提交数相符。

从文件树看，压缩前整合结果相对上游改动 225 个路径（新增 179、修改 46；`git diff --stat` 为 +125945/-3911，其中大量行数来自恢复的 XML/INI 资源）。相对 fork 改动 211 个路径（新增 75、修改 88、删除 48）；48 个删除全部是旧跟踪的 `obj/` 编译生成文件。fork 中其余跟踪路径均保留在整合树中。

本轮将上游之上的本地整合提交压成一个提交，使后续 `git pull --rebase` 能直接以 `upstream/6.0` 为基线同步。压缩只改提交历史，不改变已验证的源码和资源文件树；`backup/6.0-before-squash-20261006` 保存压缩前完整提交链。

中间压缩产生的提交为 `2494b1d`，父提交是上游 `7d31340`。随后把本报告补充也纳入同一棵树，最终提交仍以 `7d31340` 为父提交；因此 `upstream/6.0..6.0` 恰好只有 1 个提交。中间提交和五提交整合链均保留在备份分支中。远端命名为 `origin=13684215094`、`upstream=2132937983`，本地分支跟踪 upstream，push remote 指向 origin；没有执行 push。

最终验证：测试项目 restore 后运行 `dotnet run --project WC4MapEditor.Tests -- --corpus /home/j60100428/game` 为 **52 passed, 0 failed**；CLI build 成功；WPF `win-x64` 交叉构建成功（9 个既有警告、0 错误）。

## 10. fork 提交历史与最终文件树的逐项核对（2026-10-06）

### 10.1 “全部合并成一个提交”的确切含义

`origin/6.0` 有 10 个提交，`upstream/6.0` 有 2 个提交，根提交分别为 `fa093e1`、`089712c`。两者的 Git 提交图没有共同祖先；`git rev-list --left-right --count upstream/6.0...origin/6.0` 返回 `2 10`。GitHub 的 fork 元数据和 ahead/behind 计数与这一结果并不矛盾：平台仍记录 fork 关系，但当前 Git 提交图已被上游的新 `Initial commit` 切断。首次整合后的 `6.0` 把 fork 的最终源码/资源与上游功能按冲突取舍组合，压成了**上游 `7d31340` 之后的 1 个本地提交**。这不是把 fork 原有 10 个提交原样 rebase 到新历史上；原始 10 个提交仍可从 `origin/6.0` 查看，本地更新前的工作提交可从 `backup/local-6.0-before-upstream-20261002` 查看，五提交整合链可从 `backup/6.0-before-squash-20261006` 查看。

`git stash list` 没有记录是预期结果。上次通过 `git commit` 把未提交工作保存为 `50283f4`，并没有运行 `git stash`。执行过 `git pull --rebase upstream 6.0`，由于本地单提交已经以最新的上游 `7d31340` 为父提交，该命令没有需要重放的新上游提交。此次重新直连 fetch 两个远端，仍分别为 `00fba1e` 与 `7d31340`，并在隔离工作树试做无关历史合并：Git 给出 88 个 `add/add` 冲突（其中不少是本地修复后的源码），没有把该试验性合并带进主工作树。

### 10.2 对 fork、上游和整合树做 blob 级比较

以下统计均排除旧 `obj/` 生成文件，比较对象是 `origin/6.0=00fba1e`、`upstream/6.0=7d31340` 和压缩后的本地文件树。三个对象的路径数量分别是 519、367、546。fork 的 519 个路径在整合树中保留 471 个；缺少的 48 个全部是旧版跟踪的 `obj/` 生成文件。fork 相对上游独有的 159 个**非生成**路径在整合树中逐 blob 相同，包含 `setting.txt` 和大批 `Resource/` XML/INI。

| fork 与本地整合树的关系 | 路径数 | 含义 |
|---|---:|---|
| 相同 blob | 383 | fork 文件内容原样保留；其中 159 个为上游没有的非生成文件 |
| 不同 blob，整合树等于上游 | 44 | 人工选择上游版本；不能称为 fork 文件内容原样保留 |
| fork、上游、整合树三者均不同 | 22 | 本地格式/保存修复与 fork、上游原版都不同 |
| fork 等于上游、整合树不同 | 22 | 在两边共同基线上叠加了本地修复 |
| fork 路径在整合树缺失 | 48 | 均为跟踪的 `obj/` 生成文件，已排除出源码提交 |

前次只统计“fork 路径是否存在”，会漏掉这 44 个内容选择。再次核对**差异方向**后，44 个里有 43 个源码/工程文件满足：上游初始提交 `089712c` 与上游顶端 `7d31340` 的 blob 完全一样，fork 的 blob 则不同。因此这些并非后来被上游修改的文件，而是上游重建历史时采用了另一套源码。叉路的功能并非全部可简单取并集：例如 fork 的 `SettingTxtParser` 没有上游的 section 对象回填，fork 的场景管理器没有从缓存恢复原始保存路径的保护，fork 的军团/省份模式缺少上游的大量编辑入口。用 fork 整文件覆盖曾在隔离验证中导致脚本工程引用、灰度图类型和模式回调等编译错误；主工作树保留上游实现。局部行为差异仍需 Windows GUI 场景验收，不能从编译成功推断两边所有交互都相同。

22 个三方内容各异的文件里，20 个在上游初始提交与顶端完全相同，整合树在上游之上加入了本地修复。例子：`Views/RenderSceneBase.cs` 保留上游新场景和脚本命令，补上缩放/扩图失败提示；`WC4MapEditor.Core/Modifiers/BuildingModifier.cs` 在写入前拒绝超过 65535 的建筑坐标；`WC4MapEditor.Core/Parsers/Stage/StageParser.cs` 与 `ConquestParser.cs` 采用本地共享 BTL codec。`Views/BeginScene.xaml.cs` 的 fork 与上游初始树相同，上游顶端加入多个 JSON 编辑入口；整合树保留这些入口，修正新建地图尺寸检查和保存链。`.gitignore` 在三方均不同，整合树保留输出目录排除规则。三方都不同不代表逐行已合并；最终取舍以整合树和测试结果为准。

### 10.3 国家科技资源的实际分歧

`Resource/WC4DATA/assets/json/CountryTechSettings.json` 是这 44 个文件中唯一满足 **fork 等于上游初始提交、上游顶端已改动** 的路径。fork 版有 259 个唯一 ID，且 JSON 对象序列与 `/home/j60100428/game/wc4/世界征服者_解密_4_1.28.0/assets/json/CountryTechSettings.json` 完全相同；上游顶端版有 260 个唯一 ID，新增 `10115`，并在既有的 20 条记录中改变 `NeedId`、`ResearchLv`、`Position` 或 `Lines`。`game` 内可见的 1.26、1.28 资源以及 1.30 官方 APK 前 259 条均与 fork 版一致。整合树仍采用上游版，以保持其新 JSON 编辑场景对应的数据；若编辑目标是这些原始游戏包，则应从相应游戏资源单独选取 259 条版本，不应把上游版默认为官方数据。这是明确的资源内容取舍，不能称为无冲突同步。

### 10.4 当前可复核状态与边界

```bash
git remote -v
git stash list
git log --oneline upstream/6.0..6.0
git merge-base origin/6.0 upstream/6.0  # 无输出，退出码 1
git merge-base 6.0 upstream/6.0         # 7d31340
git diff --name-status origin/6.0 6.0
git diff --check upstream/6.0 6.0
```

报告补充后再次确认测试、CLI 构建、WPF 交叉构建和 `git pull --rebase upstream 6.0`；报告和整合树已纳入同一个最终提交。没有推送 `origin`；远端 fork 仍指向原有的 `00fba1e`。`/home/j60100428/game` 只作为只读对照和测试语料，不属于本次 Git 历史压缩的范围。

## 11. 再次选择上游还是 fork（2026-10-06，只读核对）

### 11.1 核对范围与网络边界

本轮核对期间没有改分支、提交、远端配置或 `/home/j60100428/game`，也没有向任何远端推送；核对完成后只更新了本报告文件。报告更新前的 `6.0` 是干净的 `51c8486`，父提交为 `upstream/6.0 @ 7d31340`，比该上游引用领先 1 个本地整合提交；`origin/6.0` 仍指向 `00fba1e`。为直接检查两棵目录树，把**本地已缓存的 Git 对象**分别用 `git archive` 展开到 `/tmp/wc4-compare-20261006/upstream` 和 `/tmp/wc4-compare-20261006/fork`。这些是指定提交的源码快照，不是本轮新 `git clone` 的实时远端副本。

本轮 `git ls-remote` 分别试了两个 GitHub HTTPS 地址、上游 SSH 地址、`gitclone.com` 和 `wget.la` 代理地址；均在 DNS 解析阶段失败（`Could not resolve host` / `Could not resolve hostname`），SSH 未到认证阶段。`origin/6.0` 的 reflog 记录于 2026-10-06 11:45 UTC 获取为 `00fba1e`；`upstream/6.0` 的对象先前已获取，2026-10-06 11:48 UTC 改为现名，仍是 `7d31340`。因此以下是对**这两个已取得的提交**的确切比较，不保证远端此刻没有新提交。本环境不能写 `/home/j60100428` 的新目录；此次不在该处创建克隆。`/tmp` 的快照只是便于人工检查，后续可以重新生成。

### 11.2 提交关系：fork 不是一组已证明由用户写的私有提交

| 仓库引用 | 根提交 / 顶端 | 历史与作者信息 |
|---|---|---|
| `origin/6.0`，13684215094 fork | `fa093e1` / `00fba1e` | 共 10 个提交。前 9 个作者署名为 `2132937983 <2132937983@qq.com>`；第 10 个 `00fba1e` 署名 `MioPhas <2132937983@qq.com>`，GitHub 代为提交，删除 13 个 `source/` 文件。署名不能证明实际操作者。 |
| `upstream/6.0`，2132937983 原仓 | `089712c` / `7d31340` | 共 2 个提交，均署名 `2132937983`。`089712c` 是 2026-09-18 的无父 `Initial commit`；`7d31340` 另新增 33 个路径、修改 3 个路径。 |

`git merge-base origin/6.0 upstream/6.0` 为空；`git rev-list --left-right --count origin/6.0...upstream/6.0` 为 `10 2`。GitHub 的 fork 关联及页面上的 “10 ahead / 2 behind” 与这个计数一致，但不能把它理解成可直接从共同祖先做三方 rebase。更准确的说法是：**当前两个分支的 Git 历史已断开，代码内容仍显然有关联**。上游新根 `089712c` 与 fork 顶端有 308 个同路径，其中 244 个 blob 完全一样，64 个不同；上游新根还有 26 个 fork 没有的路径。这个证据支持“上游以一份项目快照重新建立历史，并带有另一套源码”的判断；没有合并提交，也没有证据表明上游用普通 Git merge 吸收了 fork 的 10 个提交。不能从这些对象推断快照具体从谁的工作目录制作。

过去第 10 节标题“私有提交”指 fork 分支相对新上游历史独有的提交，容易误解为用户亲自写的提交，现已更正。**署名为本地账号的编辑器工作**另见 `50283f4` 及其备份分支；当前整合是 `51c8486`。`git stash list` 为空，是因为保存方式为提交而非 stash。

### 11.3 最终文件树逐项分类

| 比较项 | 路径数 | 具体内容 |
|---|---:|---|
| fork 跟踪路径 | 519 | `00fba1e` 的文件树。 |
| 上游跟踪路径 | 367 | `7d31340` 的文件树。 |
| 同路径、blob 一致 | 246 | 说明大量内容确实共享。 |
| 同路径、blob 不同 | 66 | 涉及 34 个 `WC4MapEditor.Core/`、14 个 `Views/`、11 个 `WC4MapEditor.Rendering/`，另有工程、CLI、服务和一份 JSON 资源。 |
| fork 独有 | 207 | 157 个 `Resource/` 文件，48 个历史误跟踪的 `obj/` 编译产物，另有 `setting.txt` 和 `docs/memory-optimization-plan.md`；**没有 fork 独有的 `.cs` 源码路径**。 |
| 上游独有 | 55 | 20 个 `Views/`、24 个 Core、7 个 Lua 工程文件，外加一个渲染类、`dist/map_places.json` 和两个 `source/` 资料文件。 |

上游第二个提交才加入了 JSON/布局等编辑场景；上游新根本身已经有 Lua 脚本工程、BTL 格式/规则检查器、省份生成器、首都渲染等。fork 的 157 个资源路径包括 `layout.xml`、多语言 `stringtable_*.ini`、大量图集/XML，而上游代码仍引用 `setting.txt`、资源根和布局文件。直接只留纯上游文件树，会缺少这些本地可用的数据和配置；但资源来自哪个游戏版本仍要按具体目标检查。48 个 `obj/` 文件是生成物，不能作为保留 fork 的理由。

同路径差异中，上游有实际功能和修正，例如 `SettingTxtParser` 回填解析后的配置对象；fork 版本缺少这一步。上游 `ProvinceEditMode`/省份生成服务提供手绘边界和图片识别入口，fork 版本没有。fork `LegionModifier` 中的 `AddCapital`/`RemoveCapital`/`ToggleCapital` 没有在上游同文件出现，但上游把它们迁入独立的 `CapitalModifier` 并在 `LegionEditMode` 调用；这不是功能消失。另一方面，不能把所有 66 个同路径差异都判为上游更正确，尤其是游戏资源与二进制保存细节。

`CountryTechSettings.json` 是明确的数据分歧：fork 有 259 条记录，JSON 对象列表与 `game/wc4/世界征服者_解密_4_1.28.0` 对应文件相等；上游有 260 条（新增 ID `10115`），另修改 20 条既有记录的 `NeedId`、`ResearchLv`、`Position` 或 `Lines`。`game` 内可见的 1.26/1.28 版本均与 fork 的 259 条列表相等；1.30 已解包 JSON 的前 259 条也相等。比较的是解析后的对象内容，不是字节/排版相同。当前整合树选择了上游 JSON，因此处理这些原始游戏包时，应明确指定目标版本的数据文件。

### 11.4 对当前整合树的核验与建议

当前 `6.0` 的 546 个跟踪路径中，fork 独有的 **159 个非 `obj/` 路径全部保留**；fork 原有的 48 个 `obj/` 路径已排除。66 个同路径分歧里，44 个文件采用上游 blob，22 个是两边都不相同的本地整合版本。另有 20 个只在本地整合树的路径，主要是 BTL/World 安全读写、地图变换、资产审计、测试及文档。上游独有的 55 个路径也都在整合树中。因而这个整合是**上游源码基线 + fork 非生成资源/配置 + 本地修复**，不是对 fork 全部旧行为的逐行兼容承诺。

**建议继续以当前 `6.0 @ 51c8486` 为开发基线，并以 `upstream/6.0` 跟踪原仓源码。** 不建议切回纯 `origin/6.0`：它缺少上游新增编辑场景、解析器和工具；也不建议直接重置到纯 `upstream/6.0`：会丢掉 fork 的 157 个资源文件、`setting.txt` 及本地已验证的读写修复。保留 `origin/6.0 @ 00fba1e` 和既有备份分支作为可追溯的历史，不应未经核对就强推覆盖 fork 远端。若需要交付给特定游戏版本，把资源版本选择与源码更新分开处理，优先检查上述 JSON 数据差异。

此前在网络可用时验证过当前整合树：52 个测试通过，4092 份 BTL 逐字节往返，CLI 与 Windows WPF 交叉构建成功。本轮运行环境没有可调用的 `dotnet`，未重复构建；Windows GUI 功能还需实机操作验收。本轮报告是唯一的工作树修改，尚未提交。远端网络恢复后，先对两仓执行只读 `git ls-remote`/`git fetch` 并确认 SHA；若 SHA 变化，需要按新对象重做上述比较，再决定是否 rebase 或推送。

## 12. 本地已整合上游后的资源与生成文件整理（2026-10-06）

本轮再次尝试读取 GitHub 和 `gitclone.com` 的上游 `6.0`，均在 DNS 解析阶段失败，未能获取**实时**新提交。当前本地 `HEAD=51c8486` 的父提交已经是此前取得的 `upstream/6.0=7d31340`，故该提交所含的编辑场景、布局工具、Lua 工程等代码已在本地；本轮没有声称已同步网络不可达时可能出现的新上游提交。`git pull --rebase upstream 6.0` 因本报告的未提交改动而拒绝开始；即使先保存改动，也仍需网络可用才能核实并拉取实时远端。

fork 相对缓存上游独有的 157 个 `Resource/` 文件共 9,391,219 字节：149 个 XML、7 个 INI、1 个 TXT。152 个位于 `Resource/WC4DATA/assets/`，包括 `layout.xml`/`layout_x.xml`、动画与单位/地形图集索引、多语言字符串表、配置与教程；4 个 `Resource/Texture/` 是海岸/灰度/建筑标识图集描述；`Resource/Geo/jiuzhou.txt` 是地理坐标资料。它们是游戏或编辑器的数据，不是编译产物。按文件名在 `/home/j60100428/game` 中找到 153 个对应路径，但内容版本不一定相同；抽查 `layout.xml`、`stringtable_cn.ini`、`def_map.xml`、`image_flags_hd.xml` 均与该目录的 1.28 原版字节不同。这些资源原本已由 `51c8486` 整合提交保留；本轮不删除、不重复制提交。该仓缺少被图集 XML 引用的 PNG 图片，保留描述文件也不等于 Windows GUI 的美术资源已完整。

fork 旧历史里的 48 个 `obj/` 路径都是 .NET/MSBuild 生成源码：`*.AssemblyAttributes.cs`、`*.AssemblyInfo.cs`、`*.GlobalUsings.g.cs`、`App.g.cs` 和 WPF 视图 `*.g.cs`。当前 `6.0` 的 `git ls-files` 中已没有任何 `obj/` 路径，旧文件也不在工作树磁盘上；它们在先前的整合中就已舍弃，无需再制造一次删除提交。已有 `.gitignore` 覆盖 `**/obj/`、`**/bin/`、`/tmp/`，`Directory.Build.props` 还把中间/输出文件放在 `tmp/obj` 和 `tmp/bin`。本轮只补充 `.vs/`、`.idea/`、`TestResults/`、`coverage/`、`artifacts/`、`publish/` 及 IDE 用户文件/包产物的忽略规则。

本轮操作环境的 `.git` 是只读挂载：`git add`/`git commit` 均在创建 `.git/index.lock` 时返回 `Read-only file system`。因此本轮的 `.gitignore` 和报告修改暂时只能留在工作树，无法按要求创建独立提交或完成新的 `git pull --rebase`。待 Git 元数据恢复可写、网络可解析后，先保存这两个文件，再 `git fetch upstream 6.0` 核对顶端 SHA，随后在干净工作树上 rebase；如新上游改变了同路径源码/资源，需解决冲突并重新构建测试。

## 13. VPN 启动尝试与当前执行环境边界（2026-10-06）

已按 `/home/j60100428/README.md` 阅读 VPN 手动管理流程，先把 `.gitignore` 和本报告的未提交差异备份到 `/tmp/wc4-before-vpn-20261006.patch`。该次 Codex 执行环境并非服务器完整的 systemd 会话：PID 1 是 `codex-linux-sandbox`，`sudo .../clash-verge-mihomo.sh start` 在 sudo 权限插件失败，直接以 root 调用同一脚本则被 `systemctl` 报 `Failed to connect to bus: Operation not permitted`。按 unit 文件的 `ExecStart` 临时直启 Mihomo 核心也无法绑定 `127.0.0.1:7897/9097/5335`，所有 `listen` 均返回 `socket: operation not permitted`，进程随后退出；没有留下代理监听或后台进程。尝试通过该端口运行 Git 得到 `Couldn't connect to server`，直接访问 GitHub 仍是 DNS 失败。已在临时 shell 执行 `proxy_off`，检查代理变量为 0；`stop` 因同一 systemd 限制无法调用，但已确认无 Mihomo 进程或端口。此次没有获得新远端对象，也没有执行成功的 `git pull --rebase`。这些错误来自当前 Codex 沙箱权限，不能据此判断服务器上的 VPN 安装或节点本身损坏。

## 14. 代理恢复后的实时同步（2026-10-06）

服务器上已有运行中的 Mihomo，`127.0.0.1:7897` 正在监听。本轮对每条 Git HTTPS 命令单独设置 `http.proxy=http://127.0.0.1:7897`，没有改全局 Git 代理或启动、停止 VPN 服务。`git ls-remote` 对 GitHub 两仓的 `refs/heads/6.0` 查询成功：2132937983 上游为 `7d3134091ec2c797e614cc6e39789af8a35db34a`，13684215094 fork 为 `00fba1e6cd00c98c9dcdbfef73bf6ba408b9de09`。随后分别 `git fetch --no-tags` 两仓，同名远端跟踪引用与实时结果一致。第 11-13 节记录的网络和 Git 元数据限制属于此前的执行环境，现已不再阻止本轮操作。

先将已有 `.gitignore` 和报告修改保存为本地提交 `2cf53b8`，父提交仍是整合提交 `51c8486`。然后在干净工作树运行 `git -c http.proxy=http://127.0.0.1:7897 pull --rebase upstream 6.0`，Git 返回 `Current branch 6.0 is up to date.`。本次没有新上游提交或冲突，因而没有对源码、资源或游戏数据做新的合并。整合工作仍集中在 `51c8486` 一个提交；`2cf53b8` 只记录忽略规则和审计报告。旧 fork 的 48 个 `obj/` 生成路径已在整合提交中排除，157 个独有 `Resource/` 路径仍保留。

本轮代码未变；环境中没有可执行的 `dotnet`，因此未重复运行 .NET 测试或构建。先前的 52 个通过测试及 Windows WPF 交叉构建结果见第 9 节，Windows GUI 仍需实机验收。没有向 `origin` 推送；fork 的历史与当前上游没有共同祖先，推送前应单独决定远端分支的迁移方式。
