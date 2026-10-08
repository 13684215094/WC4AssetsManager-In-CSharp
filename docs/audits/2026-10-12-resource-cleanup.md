# 2026-10-12：指定项目资源核验、旧资源清理与继续排查

> 后续更正：用户要求只清理目标项目已有的重复项、本仓独有项保留。本文记录的253份删除中，16份独有文件已从独立远端工作区恢复；最终去重237份，约15.58 MiB。本文删除统计保留为当时节点，当前清单、分支隔离和核验见 [独有资源恢复审计](2026-10-12-resource-retention.md)。

本节点接续 [Windows 复现复核](2026-10-12-windows-regressions.md)，检查“游戏数据和图片都来自用户指定的目录或 APK”是否落实到扫描、配置、地图渲染、图片编辑和发布。清理前保留工作区快照，已有的增量输出、项目首页、APK、科技预览、技能续级和八项 Windows 问题修复全部继续保留。

**清理前确实还有旧资源和读取入口。** 首页已经要求加载项目，但 Core/CLI 的默认扫描、地图海陆底纹与海岸、部分建筑/兵种标记仍依赖安装目录。仅删除文件会让图片继续缺失，因此先改读取路径和缓存，再删除冗余文件。本节点完成后，游戏配置、美术及本地化只读当前项目；未加载时不会自动选择仓库里的旧版本。

## 1. 来源与保存矩阵

| 输入或场景 | 当前行为 | 核验结果 |
|---|---|---|
| 启动、尚未指定来源 | 首页填写来源，游戏编辑入口待加载后出现；Core 未加载时索引为空，获取活动资源根明确报错 | 不自动扫描本仓或 EXE 旁的游戏数据 |
| 游戏解包根目录 | 探测所选目录内的 `assets`，加载地图、JSON、XML、图片与语言资源 | 真实 1.30.0 的 3,474 个 assets 可读取 |
| 直接选择 `assets` 目录 | 以这个目录为游戏资源根，按来源的相对结构读写覆盖文件 | 保留既有项目支持 |
| APK 文件 | 验证并解包到独立缓存，以缓存作为只读来源；增量输出保存在用户指定目录 | 原 APK 内容不变；不把解包副本放进输出目录 |
| 已有增量输出目录 | 读取 `.wc4-project.json`，重新连接原来源和覆盖目录；覆盖文件优先 | CLI 查询也能看到来源文件及已保存覆盖 |
| 用户显式选择旧布局目录 | 兼容所选路径下的 `WC4DATA/assets` 或 `Resource/WC4DATA/assets` | 这是指定输入的结构兼容，不会自行寻找仓库旧资源 |
| 新项目默认输出 | 来源同级 `<项目名>-edited`；例如 `World Conqueror 4_1.30.0-edited` | 只新增 manifest 和实际新增/修改的文件；未改文件不复制 |
| 恢复文件原内容并保存 | 移除该覆盖文件，继续读取来源版本 | 前一节点的增量保存回归继续通过 |
| 来源缺图或缺表 | 保持缺失状态、显示工具已有占位或明确失败 | 不串用上一个项目的图，也不补原游戏本来没有的半身像 |

`asset scan`、`asset summary`、`asset list`、`asset query` 的 `path` 现在均为必填。每次命令根据本次路径选择来源；漏填为参数错误，读取失败返回 `2`，不会静默复用进程上一次加载的项目。

```bash
dotnet run --project WC4MapEditor.Cli -- asset summary \
  "/home/j60100428/game/wc4/World Conqueror 4_1.30.0"
dotnet run --project WC4MapEditor.Cli -- asset summary \
  "/home/j60100428/game/wc4/World Conqueror 4_1.30.0.apk"
dotnet run --project WC4MapEditor.Cli -- asset list \
  "/home/j60100428/game/wc4/World Conqueror 4_1.30.0-edited"
```

这些路径是本机样本，实际使用时换成自己的目录。APK 加载不包含 APK 重建、签名和安装流程。

## 2. 清理清单与保留依据

删除 **253 个冗余资源文件，共 17,535,947 字节，约 16.7 MiB**。字节数按本轮开始时工作区里的普通文件内容计算，不包括 Git 历史、目录开销和未跟踪构建产物。

| 删除范围 | 文件数 | 字节数 | 原因及替代来源 |
|---|---:|---:|---|
| `Resource/WC4DATA/` | 237 | 16,331,549 | 旧游戏 JSON、XML、INI 等没有完整配套美术；全部改读用户所选项目，避免混用不同版本 |
| `Resource/Geo/` | 5 | 760,840 | 没有生产调用的地点参考数据，部分内容乱码或不能解析；保留代码中的地理能力与现有分析资料，旧文件可从 Git 历史查看 |
| `Resource/Music/` | 1 | 958 | 无使用入口的播放列表，列出的音乐文件不在仓库；移除配置及无用读取逻辑 |
| `Resource/Texture/` | 7 | 41,761 | 旧海岸/遮罩/地形/标记映射缺配套图片，现从当前项目图集和纹理读取；无用背景配置删除 |
| `source/` | 2 | 31,305 | 没有生产引用的旧 BTL/JSON 参考草稿，部分内容非法；格式说明保留在 docs 和 codec 中 |
| `dist/` | 1 | 369,534 | 无运行或构建引用的地点结果文件；不混入发布目录 |
| **合计** | **253** | **17,535,947** | 旧内容仍可从 Git 历史恢复；没有删除用户给定的游戏项目 |

另删除没有调用的 `TextureDiskCache` 类和 DI 注册；这个代码文件不计入上表的资源数量。

以下 **五份编辑器预设必须保留**，从 `Resource/Config` 迁到 `EditorResources/Config`，内容逐字节不变：

| 文件 | 编辑器用途 |
|---|---|
| `GeneralInCountry.json` | 将领分配的国家筛选预设 |
| `GeneralRandomTemplates.json` | 自动生成将领的随机参数模板 |
| `GeneralSpecialtyTemplates.json` | 将领专长的技能候选模板 |
| `LegionLvSetting.json` | 自动部署的军团等级预设 |
| `MaxFormation.json` | 自动部署的编队数量预设 |

它们是编辑器操作默认值，不是加载项目的游戏配置表。将领、兵种、技能、国家和科技实际记录仍来自项目；预设不会自动覆盖项目 JSON。`setting.txt` 同样保留且内容不变。

Core 工程显式声明这五份预设和 `setting.txt` 的 build/publish 复制规则，CLI、测试与 WPF 输出都能获取。WPF 不再复制 `Resource/**`、旧地图或不存在的脚本目录；`.gitignore` 忽略旧 `Resource`、`source`、`dist` 和本机 `Cache`，避免重新混入。工具背景/图标若以后提供，应放 `EditorResources/Texture`，不得用它存放游戏数据。

删除工作区文件会减少当前检出和新发布包的内容，不会缩小已有 Git 历史。清理后的包应发布到新目录，覆盖旧发布目录不会自动移除旧包留下的文件。

## 3. 本节点继续排查与修复矩阵

| 编号 | 确认的问题 | 修复 | 证据 |
|---|---|---|---|
| C01 | GUI 已要求加载，但 Core/CLI 仍能隐式扫本仓默认数据 | 移除 `ScanDefault`、默认资源开关和默认路径；`Scan(path)` 只处理显式输入 | 未加载索引为空、漏填失败、同进程 A→B 查询使用 B |
| C02 | 资源更新时地形初始化调用整个 `ConfigManager.Reload()`，可能清掉字符串草稿 | 游戏 lookup 按 Revision 单独重建；字符串解析器按活动文件路径跟随文档，同来源刷新保留实例 | 修改城市名称→保存地形/刷新→草稿仍在；切来源正确更新 terrain/ID lookup |
| C03 | 海陆底纹、海岸与遮罩仍读安装目录，删除旧资源后显示不完整 | `ProjectTextureLoader` 读取项目 WebP/PNG/PKM；`CoastHelper` 读取 `coast_hd` 和 `coastmask_hd`，保留原邻接规则 | 合成 A/B 底纹与图集切换、真实 1.30.0 底纹/两套海岸加载通过 |
| C04 | 海岸磁盘缓存只用旧安装文件指纹，缺来源可能共用缓存；同尺寸地图沿用旧邻接数组 | 指纹包含有效项目 XML、图页、陆地纹理的实际路径与 SHA-256；缺来源不读旧缓存；切地图重算邻接，取消旧任务 | 缺海岸项目不串图，原缓存不可代替缺失源文件；图集刷新结果和新实例一致 |
| C05 | 建筑、设施、兵种及部分标记仍从旧 `StageMark` 路径取图 | 建筑读 `buildings_hd`，设施和等级标记读 `battleres_hd`，兵种读 `image/image_legion_icon_hd`；其他标记按项目图集查找后使用工具占位 | 同一地图、同一引擎在 A→B 及红→蓝覆盖后与新引擎逐像素一致 |
| C06 | 图集更新后调用方还可能持有旧裁切或缩放图片 | 引擎按资源 Revision 重建对象渲染器；地形、底图、省份、旗帜及图块副本同步失效和释放 | 图集保存/外部刷新与跨项目回归继续通过 |
| C07 | 默认制旗要求不存在的 `flagoverlay.png`，之前注入 overlay 的测试未覆盖实际默认入口 | 从当前项目标准 52×54 `flag_*` 提取边框/阴影，清内部圆形图案后绘制新旗；无兼容模板明确失败 | 真实默认服务成功，只生成四个覆盖文件；中央新颜色、外圈原像素、大旗/HD 一致和小旗尺寸均通过 |
| C08 | 清理后构建或发布可能漏带操作预设，或继续复制旧游戏资源 | 五份预设及 setting 显式复制；移除游戏资源通配和无调用纹理缓存 | Windows build/publish 成功，五预设及 setting 的发布内容与源码相同，没有旧游戏资源目录 |

制旗的模板优先选目标国家、`flag_1`，再选其他兼容的标准尺寸模板。当前圆形掩模基于 1.30.0 标准旗帜尺寸；不同版本或非标准美术需要重新核对。战术大旗、小旗与 HD 仍先全部准备，再统一提交两张图片和两份 XML，保持前一节点的失败保护。

## 4. 代码与回归入口

| 范围 | 入口 |
|---|---|
| 显式来源、项目叠加与 CLI | [AssetManager](../../WC4MapEditor.Core/Assets/AssetManager.cs)、[AssetCache](../../WC4MapEditor.Core/Assets/AssetCache.cs)、[ProjectFiles](../../WC4MapEditor.Core/Assets/ProjectFiles.cs)、[CLI](../../WC4MapEditor.Cli/Program.cs) |
| 预设与字符串/地形 lookup | [ConfigManager](../../WC4MapEditor.Core/Config/ConfigManager.cs)、[TerrainHelper](../../WC4MapEditor.Rendering/Helpers/TerrainHelper.cs)、[编辑器预设说明](../../EditorResources/README.md) |
| 项目图片读取 | [ProjectTextureLoader](../../WC4MapEditor.Rendering/Imaging/ProjectTextureLoader.cs)、[ProjectSpriteLoader](../../WC4MapEditor.Rendering/Imaging/ProjectSpriteLoader.cs)、[BuildingAtlasCache](../../WC4MapEditor.Rendering/Skia/BuildingAtlasCache.cs)、[UnitAtlasCache](../../WC4MapEditor.Rendering/Skia/UnitAtlasCache.cs) |
| 底图、海岸及缓存 | [BackGroundRender](../../WC4MapEditor.Rendering/Skia/BackGroundRender.cs)、[CoastHelper](../../WC4MapEditor.Rendering/Helpers/CoastHelper.cs)、[CoastMaskProcessor](../../WC4MapEditor.Rendering/Helpers/CoastMaskProcessor.cs) |
| 默认制旗 | [FlagEditorService](../../WC4MapEditor.Rendering/Imaging/FlagEditorService.cs) |
| 本节点操作回归 | [ResourceSourceTests](../../WC4MapEditor.Tests/ResourceSourceTests.cs)：3 项 Core、3 项合成渲染、1 项真实语料；前一节点的操作回归继续运行 |

## 5. 验证记录

本机日志位于被忽略的 `tmp/`，不作为发布资源。测试和 Windows 构建共享部分中间输出，按顺序运行。

| 验证 | 结果与限制 |
|---|---|
| 完整回归 | **147 passed，0 failed**，包含前一节点 140 项与新增 7 项；最终日志 `tmp/resource-cleanup-corpus-final.log` |
| 二进制保真 | 全语料 **5,523 张 BTL** 逐字节一致，world 往返通过；1.30.0 的 1,383 张 BTL 继续可加载 |
| 配置与美术 | **81 表 / 22,055 行，1,677 图片 / 417 PKM，94 图集 / 6,675 矩形，190 个地形图片变体**继续通过；APK、科技191节点和技能续级仍通过 |
| Windows WPF 交叉构建 | 最终 `win-x64` build 成功，**0 错误、34 条警告**，含 Core/Rendering/WPF 的 nullable 和未使用字段等；之前一次增量编译为12条，不能当作全工程警告总数。最终日志 `tmp/resource-cleanup-windows-build-final.log` |
| Windows 发布 | Debug、`win-x64`、`self-contained=false`、单文件发布成功，**0 错误**；publish 日志有41条警告诊断行，包含 Core/Rendering/WPF 的 nullable 和未使用字段等。日志 `tmp/resource-cleanup-publish.log` |
| 新发布目录内容 | `tmp/publish-resource-cleanup-20261012/` 共13个文件：EXE、Skia原生库、调试符号、五份预设和 setting；没有 `Resource`、`WC4DATA`、`source`、`dist` 或游戏数据。预设和 setting 逐字节校验一致 |
| 来源保护 | 解包目录 **4,580 文件的集合与 SHA-256 全部不变**，原 APK SHA-256 不变；删除范围只在本仓库 |
| 实机边界 | 本机 Linux，没有运行 WPF 或 Android。交叉构建、发布和渲染回归不等于 Windows 点击流程及游戏运行已验收 |

原 APK SHA-256：`14b8de3a5a2578083bdb7a6cadcec44211299f022636df947a5f8ee045118709`。清理记录为 `tmp/resource-cleanup-removed.json`，预设迁移和来源校验使用本轮开始时的快照；这些本机文件不进入提交。

```bash
LD_LIBRARY_PATH="$PWD/tmp/skia-linux${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
  /home/j60100428/game/tmp/dotnet/dotnet run \
  --project WC4MapEditor.Tests --no-restore -- \
  --corpus /home/j60100428/game --rendering

/home/j60100428/game/tmp/dotnet/dotnet build WC4MapEditor.csproj \
  --no-restore -p:EnableWindowsTargeting=true -r win-x64

/home/j60100428/game/tmp/dotnet/dotnet publish WC4MapEditor.csproj \
  --no-restore -c Debug -r win-x64 --self-contained false \
  -p:EnableWindowsTargeting=true -o tmp/publish-resource-cleanup-20261012
```

发布包需要 Windows .NET 10 Desktop Runtime 和同目录 `libSkiaSharp.dll`，应完整使用发布目录。实际发版可按原工程规则使用 Release，本文只记录本次已执行的 Debug 发布。

## 6. Windows 验收与剩余范围

| 验收操作 | 预期 |
|---|---|
| 把新包放在没有旧 `Resource` 目录的独立位置启动 | 必须选择来源；加载前没有游戏数据或默认游戏编辑页 |
| 分别选择解包目录、直接 assets、APK，指定新的增量输出 | 地图、将领、国家和科技来自对应项目；无修改保存不复制来源资源 |
| 切换两个美术不同的项目，再在输出改图、保存/刷新 | 海陆底纹、海岸、建筑、兵种、设施和旗帜更新；不显示上一个项目的图片 |
| 修改城市名称草稿，再刷新图片或保存地形 | 字符串草稿保留；外部配置变化仍通过对应编辑器明确重载 |
| 不提供额外 overlay，制作一个国家的旗帜 | 复用项目边框/阴影，两套大旗与小旗正确，输出仅两张图片和两份 XML |
| 使用缺图或缺海岸的测试项目 | 使用现有工具占位或说明缺失，不读旧项目、旧包或旧缓存的游戏图片 |

继续按 [前一节点的 Windows 操作矩阵](2026-10-12-windows-regressions.md#5-windows-操作验收清单)核验地图 A→B→命令、刷新草稿、导入/复制/空图集与保存失败。

资源来源问题已落实到本次检查的路径，但以下能力仍有边界：PKM 只读解码，没有编码写回；地图预览未完整复刻游戏动画、全部地形层/偏移与 UI 美术；world/BTL 联动写回、APK重建签名仍未实现。专用和通用配置编辑已有81表支持，不代表所有外键或原生引擎范围都已验证。缺失半身像按源项目原状处理。

本节点和 README 第12–16节的已有修改与前一笔本地提交合并，只保留一个提交；旧测试统计和历史快照保留当时描述。此次未推送，实际提交 SHA 与远端关系以 Git 状态为准。
