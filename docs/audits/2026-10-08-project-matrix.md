# 2026-10-08：1.30.0 游戏项目与全模块审计矩阵

## 1. 结论与审计边界

**用户提出的“给出游戏项目目录，加载地图等资源，手动修改，保存到另一目录并保持结构”已具备基础实现，并通过真实项目的复制、地图读写和主要配置保存验证。全部资源可视化编辑、world/BTL 联动和 Windows 实际操作尚未完成验收，因此不能把整项需求标为完全完成。**

本次以 `/home/j60100428/game/wc4/World Conqueror 4_1.30.0` 为主要目标，原目录只读，修改和保存测试均使用 `tmp/tests` 下的副本。实现前代码基线为 `6.0` 的 `a498238`；项目加载功能、本次修复和中文资料纳入 2026-10-08 的同一个提交。审计期间未联网更新远端，`upstream/6.0` 的已获取引用是 `7d31340`。

这里的“全量矩阵”指按需求、资源类型、模块、生命周期、故障和运行平台逐项列出状态；不代表所有 UI 分支、所有 JSON 字段、所有 Android 行为都被自动测试。不能将测试条数换算为完成百分比。

| 状态 | 本文含义 |
|---|---|
| 已验证 | 有本次自动测试或真实语料结果；具体范围见证据列 |
| 已实现，待实机 | 有代码与构建结果，尚未在 Windows 操作相应界面 |
| 部分满足 | 可用的路径已实现，另有明确未实现或受限部分 |
| 明确拒绝 | 程序检测不支持的情况，报错并保留输入；不是功能已实现 |
| 未实现 | 未发现需求所要求的完整入口或流程 |
| 待核实 | 静态风险或游戏语义缺少运行证据，不断言已经发生 |

修复后完整回归为 **82 passed, 0 failed**。最后一次 Windows `win-x64` 交叉构建成功，**0 个错误**；nullable、未使用字段等警告仍存在。Linux 可以验证 Core、CLI、图片服务与场景缓存逻辑，不能实际运行 WPF，也不能验证 Android 游戏。

## 2. 真实项目资产盘点

该解包目录的项目身份、资源关系和操作流程另见 [1.30.0 项目说明](../projects/wc4-1.30.0/README.md)，全部 JSON/config 清单、目录统计和关键文件指纹见 [资源索引](../projects/wc4-1.30.0/resource-index.md)。这些资料补充资产事实，不改变本报告的功能完成状态或验收范围。

| 范围 | 文件数 | 字节数 | 验证用途 |
|---|---:|---:|---|
| 整个解包项目 | 4,580 | 278,847,745 | 全量复制、相对路径、源目录保护 |
| `assets` | 3,474 | 241,964,843 | 资源索引、地图、配置、图片 |
| `assets/stage` | 1,383 | 按具体文件读取 | 1,073 张本地地形 BTL、310 张引用 world 的 BTL |
| `assets/json` | 81 | 按具体文件读取 | 关系审计；其中主要专用编辑器验证见下一表 |
| 全部 assets JSON | 84 | 按具体文件读取 | 包含不在 `json/` 下的 JSON，不等于 84 张专用可编辑表 |

| 资源后缀 | 数量 | 当前用途与支持边界 |
|---|---:|---|
| `.btl` | 1,383 | 项目文档逐张加载和无修改保存，字节一致 |
| `.webp` / `.png` | 900 / 360 | 可解码图片与图集入口；未逐张验证完整 UI 编辑 |
| `.pkm` | 417 | 复制、索引保留；PKM/ETC 解码尚未接入 |
| `.xml` | 144 | 已有图集、地形、兵种位置、头像、布局等解析器；任意 XML 通过外部文本编辑 |
| `.bin` | 51 | 完整 YSAE/v4 world 与辅助 BIN 区分；辅助 BIN 没有通用结构编辑器 |
| `.wav` / `.mp3` | 61 / 2 | 复制、浏览；没有本轮音频编辑功能 |
| `.fnt` / `.otf` | 29 / 7 | 字体资源加载；没有字体制作功能 |
| `.strings` / `.ini` | 8 / 7 | 外部编辑；部分专用编辑器使用 `stringtable_tw.ini` |
| `.fsh` / `.vsh` | 8 / 8 | 文件保留、外部编辑；没有 shader 编译与游戏验证 |
| `.shp` 及其他 | 2 及少量 | 保留与浏览；不承诺解析未定义格式 |

真实 `world.bin` 为 YSAE/v4，**148 × 54**。310 张外部底图 BTL 均使用 `MapNumber=1`，其中 164 张 capture 的 X 原点为奇数。两份横跨右边界的样本为：

| 样本 | capture 原点 | 尺寸 | 结果 |
|---|---|---|---|
| `frontier80407.btl` | `(133,18)` | `18×12` | 横向取模读取底图，BTL 无修改保存字节一致 |
| `stage60023.btl` | `(147,4)` | `10×8` | 横向取模读取底图，BTL 无修改保存字节一致 |

字节和像素验证不包含 capture 奇偶原点的画布锚点、对象中心和点击位置验收；这部分仍需 Windows 与游戏对照。

## 3. 用户项目加载需求矩阵

| 编号 | 需求或操作 | 当前状态 | 实现与证据 | 限制 |
|---|---|---|---|---|
| Q01 | 选择完整 1.30.0 解包根目录 | 已实现，待实机 | `GameProjectWorkspace.FindAssetsRelativePath`；首页、资源浏览器入口 | Windows 目录选择交互未验收 |
| Q02 | 兼容直接选 assets 或仓库型资源目录 | 已验证 | 支持 `assets`、`WC4DATA/assets`、`Resource/WC4DATA/assets`；合成用例 | 自定义布局需明确可识别资源根 |
| Q03 | 保存到另一个目录 | 已验证 | 4,580 个文件复制，源与输出互不包含 | 首次输出需新建或为空，不做增量 overlay |
| Q04 | 保持所有相对目录结构 | 已验证 | 复制 assets 外文件、嵌套目录、空目录；逐文件 SHA-256 一致 | 新增 `.wc4-project.json`，打包时可排除 |
| Q05 | 编辑不会覆盖原项目 | 已验证，限定项目入口 | 地图、图集和导出校验输出；选择源地图映射至副本；副本修改后源哈希不变 | 独立 CLI、外部程序及自定义脚本按各自路径运行 |
| Q06 | 可取消复制，不留下部分成品 | 已验证 | 同级 staging 复制后发布；取消清理用例 | 非正常进程中止不提供自动恢复承诺 |
| Q07 | 继续打开以前的编辑结果 | 已验证 | manifest 重开，不重新复制；CLI create/info 用例 | 不做 APK 打包、签名或游戏安装 |
| Q08 | 浏览和加载项目地图 | 已验证 | 1,383 张 BTL 与完整 world；辅助 BIN 单独分类 | 未知地图格式、外部底图编号不自动猜测 |
| Q09 | 手动改本地地形 BTL | 部分满足 | 既有地形、对象、省区、归属模式；数据保存、撤销测试 | 全部鼠标/属性窗口操作待 Windows；文件名为 conquest 的自带地形图存在模式限制 |
| Q10 | 手动改 world | 部分满足 | 完整地形、省份两平面 codec；地形编辑和保存测试 | world UI 只开放地形模式，没有省份编辑入口 |
| Q11 | 编辑引用 world 的 BTL | 部分满足 | capture 底图预览；对象、省区、归属写 BTL | 不支持在 BTL 场景改 world 地形、capture、尺寸或 MapNumber；保存/缓存明确拒绝 |
| Q12 | 配置、图片等资源可编辑 | 部分满足 | 既有专用 JSON/图集/布局入口绑定副本；通用文本打开副本 | 专用 UI 只展示模型字段；PKM、辅助 BIN、音频等无相应编辑流程 |
| Q13 | 切项目后不读写旧资源根 | 部分满足 | 重载解析器与图集/字体缓存；旧 JSON 编辑器保存被拒绝；扫描失败保留旧索引 | 各编辑器未保存状态和退出确认尚未统一 |
| Q14 | Save / Ctrl+S 按原结构输出 | 已实现，核心已验证 | `ProjectMapDocument.Save`、WPF 保存入口；BTL/world 重读、场景路径测试 | WPF 快捷键及文件对话框待实机 |
| Q15 | 编辑后能在游戏中正常使用 | 待实机与游戏 | 当前验证文件结构、数据保真、像素与构建 | 不包含游戏 AI、寻路、镜头、存档、事件或 native SO 验收 |

相关源码：[项目副本](../../WC4MapEditor.Core/Assets/GameProjectWorkspace.cs)、[地图文档](../../WC4MapEditor.Core/Assets/ProjectMapDocument.cs)、[项目会话](../../Services/GameProjectSession.cs)、[地图场景](../../Views/RenderSceneBase.cs)、[场景管理](../../Services/RenderSceneManager.cs)。

## 4. 模块与资源编辑矩阵

| 模块 | 加载/展示 | 修改/保存 | 本次证据 | 当前缺口 |
|---|---|---|---|---|
| BTL v1/v2/v3 | 共用布局与严格长度校验 | 保留 padding、opaque、extra；版本化单位记录 | 全语料 5,523 张逐字节往返；边界与变换用例 | 未知坐标区段限制变换；游戏行为未验 |
| YSAE/v4 world | 完整 16 字节地形与 2 字节省份平面 | codec 保真；UI 可改地形 | world 合成与真实语料往返、项目修改重读 | world UI 省份入口缺失，辅助地图纹理不同步 |
| 外部 world BTL | 自动填充 capture，支持右边界环绕 | BTL 对象/省区/归属；外部地形只读保护 | 全部 310 张字节一致、跨边界样本、失败缓存保护 | 多文档联动、区域写回、capture 变换未实现 |
| 六边格、河流、笔刷、省区 | flat-top、odd-column-down | 本轮统一邻接/距离，完整格撤销 | 奇偶列/宽、负列、边界、非方形、河流对边、flood fill | 有限/环绕策略仍分散；点击和所有选区算法尚未全覆盖 |
| 地图尺寸变换 | 已有扩边、裁剪、缩放 | 候选副本、对象同步、完整快照撤销 | 既有 TransformTests 与版本回归 | 游戏大图支持未验；项目外部 BTL 禁止尺寸变化 |
| 将领 JSON / 头像 XML | 已有专用 UI，Photo/EName 查询 | 原始字段合并、JSON/XML 暂存保存 | 5 张表 6,035 行语义一致；头像 XML 用例 | 外部并发编辑冲突、退出保护；头像制作输出见 R03 |
| 兵种 / 增益 / 技能 / 科技 | 模型字段专用 UI | 本轮接入保真 JSON 合并 | 真实表无修改保存、实际技能字段编辑 | 未建模字段不在面板；业务跨表引用变更不自动修复 |
| 集团军与事件五表 | 既有多表 UI | 全部候选序列化后批量暂存保存 | 多表失败、未知字段、真实 5 表回归 | 全业务语义、事件脚本和原游戏未验 |
| 国家 / 征服国家 | 既有编辑器；ConquerSettings 只读参考 | 两张可写表独立保存 | JSON 保真与重载 | 删除国家跨表操作仍可能部分保存，UI 反馈见 R02 |
| stringtable | 多个专用解析器实例 | 合并本实例 changed/deleted keys；组合保存纳入暂存 | 两实例依次修改不回退其他键；换项目重置实例 | 同键冲突仍按后保存覆盖；其他语言并未自动同步 |
| `def_armypos.xml` | 完整候选解析、有效根与 scale 校验 | 修改实际改变的属性，保留未知节点/属性 | 元数据保留与损坏 XML 阻止部分保存 | 其他单位定义 XML 主要由外部编辑 |
| 地形图集 | 按 def_mapterrain 的 idx/image 裁切 | 190 个变体使用项目像素 | terrain_hd、plant_hd、buildings_hd 逐像素比对 | 游戏锚点、比例与图集修改后全部场景缓存需实机核对 |
| 普通图片 / 战术图集 | 失败加载保持 unloaded；成功解析图块 | PNG/WebP/JPEG 按扩展名编码；图片/XML 暂存保存 | 解码失败、像素、格式、XML 未知元数据、失败恢复 | BMP 等需另存支持格式；有损压缩不承诺重编码像素相同 |
| HD 国旗图集 | 复用完整 TacticalMapEditor 文档 | 保留非旗帜项、Texture、refx/refy 和未知属性 | 导入、HD 非旗帜项、失败保存回归 | 跨战术与 HD 两组图集没有统一事务 |
| 将领头像 / 旧国旗制作 | 裁切、尺寸输出 | 仍有旧多输出保存服务 | 静态审计 | 输出路径/格式优先级、子输出失败反馈见 R03/R04 |
| 建筑 / 兵种 / 海岸绘制 | 既有渲染器及内置 split PNG/占位 | 项目地形裁切已接入；对象编辑继续复用 | 代码路径、地形像素及配置测试 | 417 个 PKM、全部对象美术、海岸实际项目纹理尚未接入 |
| 布局编辑器 | 既有宽松布局解析与 Skia 预览 | XML 导出、PNG 导出校验输出范围 | WPF 构建与静态路径检查 | 未做全布局往返/游戏 UI 对照，不能承诺所有节点保真 |
| 图像识别 / OCR / 省份生成 / 地理参考 | 上游工具入口保留 | 部分结果进入现有命令；导出路径校验 | 静态复核与构建 | 识别质量、性能、全部命令事务/撤销未新增端到端测试 |
| Lua / 控制台 | 已有脚本契约和实际 Core 操作 | 调用已有地图操作；项目最终保存会校验 | 编译、既有命令与变换测试 | 没有本轮全脚本回归；失败脚本不承诺整体回滚 |
| CLI / 资源关系审计 | project create/info、分析与只读审计 | 独立文件命令遵循显式输出参数 | CLI 回归、1.30.0 只读审计 | 不自动绑定 GUI 项目、不重打包、不自动修复全部引用 |
| WPF / DI / 主窗口 / 工程 | 项目入口、进度、场景、发布复制规则 | 核心服务复用 | win-x64 交叉构建成功 | 实际窗口、DPI、中文/长路径、未保存退出需验收/修复 |
| 文档 / 历史 | 中文现状、需求和节点索引 | 历史原样归档，README 追加当前节点 | 三个提交快照逐字节校验 | 节点记录不是发布版本；旧链接按当时上下文理解 |

### 4.1 专用 JSON 表的真实保存范围

本轮检查 17 张表、6,908 行。**16 张可写表实际经专用解析器保存，再与原 JSON 做语义深比较；ConquerSettings 为只读参考，只进行加载/复制后对照，没有声称测试其保存 API。** 空白、缩进和注释不属于 JSON 语义保真范围。

| 表 | 行数 | 保存入口或角色 | 原文件存在但模型未定义的字段出现次数 |
|---|---:|---|---:|
| ArmySettings | 736 | ArmySettingParser.SaveAll | 0 |
| ArmyBuffSettings | 172 | ArmyBuffSettingParser.SaveAll | 0 |
| SkillSettings | 901 | SkillSettingParser.SaveAll | 0 |
| CountryTechSettings | 375 | CountryTechSettingParser.SaveAll | 0 |
| ArmyGroupEventSettings | 310 | ArmyGroupEventSettingParser.SaveAll | 310（StageId） |
| EventBuffSettings | 72 | EventBuffSettingParser.SaveAll | 0 |
| ConquerEventSettings | 159 | ConquerEventSettingParser.SaveAll | 0 |
| BuildingSettings | 18 | BuildingFacilitySettingParser.SaveAll | 0 |
| FacilitySettings | 33 | BuildingFacilitySettingParser.SaveAll | 0 |
| CountrySettings | 60 | CountrySettingParser.SaveCountries | 0 |
| ConquerCountrySettings | 356 | CountrySettingParser.SaveConquerCountries | 0 |
| ConquerSettings | 10 | 只读征服参考数据 | 40 |
| ArmyGroupSettings | 278 | ArmyGroupSettingParser.SaveAll | 0 |
| ArmyGroupReinforcementSettings | 1,648 | ArmyGroupSettingParser.SaveAll | 0 |
| EventSettings | 188 | ArmyGroupSettingParser.SaveAll | 0 |
| EventStageSettings | 731 | ArmyGroupSettingParser.SaveAll | 0 |
| EventCalendarSettings | 861 | ArmyGroupSettingParser.SaveAll | 0 |
| 合计 | 6,908 | 16 张实际保存、1 张只读 | 350 |

“字段出现次数”为各行未建模 property 的总和，不是 350 种字段。可写表的 310 个 StageId 原先会被 typed 序列化丢失，本轮已保留；只读 ConquerSettings 的 40 次出现不属于已修复的保存路径。修改一条真实 SkillSettings 的 CostMedal 后，仅该字段发生语义变化。

## 5. 已复现并修复的问题

优先级按数据丢失或错误修改的影响划分；“已修复”限定表中路径。

| 编号 | 优先级 | 原问题 | 本轮修复 | 证据与文件 |
|---|---|---|---|---|
| F01 | 高 | 奇数宽地图按线性索引奇偶计算河流/省区邻接，写错邻格；HexCoord 与画布不一致 | Camera、HexCoord、brush distance、river、province 采用奇数列偏移规则；负奇数列修正 | `AuditRegressionTests`：奇偶宽与六边对边；[HexCoord](../../WC4MapEditor.Core/Models/HexCoord.cs)、[TerrainModifier](../../WC4MapEditor.Core/Modifiers/TerrainModifier.cs) |
| F02 | 高 | F4、F5、选区移动先修改后取快照，无法撤销 | 修改放入 RecordMultiCellChange，记录完整 16 字节；地形/省区记录回调失败回滚本平面与 dirty | F4/F5/move 一次 undo/redo，失败无部分修改；[TerrainPaintMode](../../WC4MapEditor.Core/Mode/TerrainPaintMode.cs)、[EditModeManager](../../WC4MapEditor.Core/Modifiers/EditModeManager.cs) |
| F03 | 高 | 专用 JSON 保存丢未知字段，改动无关 null/缺省/token，失败加载仍可写旧/部分列表 | JsonTableFile 保存原行和 modeled baseline；按行对象身份合并实际修改；失败/缺失/资源根变化拒绝保存 | 16 张真实可写表、重复 ID、null、缺省、损坏表和旧根测试；[JsonTableFile](../../WC4MapEditor.Core/Parsers/JsonTableFile.cs) |
| F04 | 高 | 多表保存先写前表，后表失败时留下部分结果；兵种 XML unknown 元数据丢失或失败被忽略 | 全部先序列化，JSON/XML/待保存 strings 一起暂存替换；保留兵种 XML 原节点与未改属性 | 多表/损坏 XML 失败保护；[AtomicFile](../../WC4MapEditor.Core/Parsers/AtomicFile.cs)、[ArmySettingParser](../../WC4MapEditor.Core/Parsers/ArmySetting/ArmySettingParser.cs) |
| F05 | 中 | 两个 stringtable 实例依次保存会回退前一个实例的修改；国家实例跨项目残留 | 读最新磁盘并合并本实例待改键；保存成功后 accept；国家 Reload 重置字符串实例 | 两实例不同键依次编辑用例；[StringTableParser](../../WC4MapEditor.Core/Config/StringTableParser.cs) |
| F06 | 高 | 图像解码失败仍可保存占位；扩展名与编码不一致；图集 XML 丢 Texture/锚点/未知信息；导入丢旧图块 | 失败保持 unloaded；显式解码/图块检查；按输出扩展编码；保留原 XML；图片/XML 成对暂存，失败恢复内存 | `ImageEditorAuditTests`；[AtlasCompositeRenderer](../../WC4MapEditor.Rendering/Imaging/AtlasCompositeRenderer.cs)、[TacticalMapEditor](../../WC4MapEditor.Rendering/Imaging/TacticalMapEditor.cs)、[HdAtlasEditor](../../WC4MapEditor.Rendering/Imaging/HdAtlasEditor.cs) |
| F07 | 中 | 资源扫描中途失败清掉旧索引；外部底图重复 Attach 重新覆盖修改/基线 | 完成扫描后再发布；Attach 复用已有文档并先 ValidateChanges | 锁定 BIN 扫描失败、reattach 保留编辑/拒绝非法修改；[AssetCache](../../WC4MapEditor.Core/Assets/AssetCache.cs)、[ProjectMapDocument](../../WC4MapEditor.Core/Assets/ProjectMapDocument.cs) |
| F08 | 中 | 国旗制作忽略部分 atlas load/save 和独立图片保存失败 | MakeFlagWorkflow 相关失败抛出到结果，禁止最终误报全部成功 | 静态复核与构建；底层图片/XML 失败回归。跨两组 atlas 整体回滚仍未实现；[FlagEditorService](../../WC4MapEditor.Rendering/Imaging/FlagEditorService.cs) |

本轮新增回归最初确实暴露了失败，修复后全部通过。不能将 F02 的地形/省区回调回滚说成所有命令都已事务化，也不能将 F04 说成整个项目保存具有断电级事务。

关于普通图片：WPF 当前 OnLoaded 会为无图块图片创建整图对象。因此原空对象服务路径的风险不能描述为“所有普通图片保存都会透明”。本轮增加了空对象保留 source 像素的保护；明确修复的是失败解码、格式不一致、元数据和导入保存路径。

## 6. 尚存问题与优先处理顺序

以下为本次分析后仍保留的缺陷、功能缺口或待核实项，未隐藏在“全部通过”结论中。

| 编号 | 优先级/性质 | 触发条件与影响 | 定位依据 | 当前处理建议 |
|---|---|---|---|---|
| R01 | 高，数据丢失风险 | 编辑未保存时关闭主窗口；OnClosing 直接动画后 Shutdown，没有统一 dirty 确认。部分配置编辑器返回也没有保存确认 | [MainWindow.OnClosing](../../Views/MainWindow.xaml.cs)、各 EditScene 返回入口 | 统一文档 dirty、关闭/返回/项目切换保护；目前离开前显式保存，不能把场景缓存当存盘 |
| R02 | 高，错误反馈/部分保存 | 国家 SaveAndReload 忽略 SaveCountries 的 bool，失败仍提示“已更新”；RemoveCountry 顺序写两张表，忽略失败并返回 true，可能只改一表 | [CountryEditScene](../../Views/CountryEditScene.cs)、[CountrySettingParser.RemoveCountry](../../WC4MapEditor.Core/Parsers/Country/CountrySettingParser.cs) | 删除国家需两表一起校验/暂存、失败保持模型；UI 必须检查保存结果并显示原因 |
| R03 | 高，修改不可见 | 头像服务把 general_circle/head 都输出到 GeneralPhotoDir，而 GetHeadPath 读 HeadsDir 的 general_circle；半身像保存 PNG，但查询先用既有 WebP，新图可能不显示 | [GeneralPhotoEditScene](../../Views/GeneralPhotoEditScene.cs)、[ImageEditorService.SaveGeneralImages](../../WC4MapEditor.Rendering/Imaging/ImageEditorService.cs)、[GeneralSettingParser](../../WC4MapEditor.Core/Parsers/General/GeneralSettingParser.cs) | 按实际游戏目录/命名和原扩展统一生成、读取与覆盖规则；当前手工导入需核对具体路径 |
| R04 | 中，错误反馈 | 旧 SaveGeneralImages/SaveFlagImages 的圆图、缩略图写入失败不影响 Success=true | [ImageEditorService](../../WC4MapEditor.Rendering/Imaging/ImageEditorService.cs)、[FlagEditorService.SaveFlagImages](../../WC4MapEditor.Rendering/Imaging/FlagEditorService.cs) | 所有要求输出均先准备并检查；失败不提示完整成功，避免部分产物 |
| R05 | 中，部分保存 | MakeFlagWorkflow 先更新战术图集，再更新 HD 图集；第二组失败时第一组已经保存 | [FlagEditorService.MakeFlagWorkflow](../../WC4MapEditor.Rendering/Imaging/FlagEditorService.cs) | 改为两组图像/XML 全部准备后提交；当前失败结果提示不能代替跨图集回滚 |
| R06 | 中，外部修改丢失风险 | 外部文本编辑器修改已加载 JSON，再在专用 UI 保存；JsonTableFile/将领解析器仍基于加载时 baseline | [JsonTableFile.Serialize](../../WC4MapEditor.Core/Parsers/JsonTableFile.cs)、[GeneralSettingParser](../../WC4MapEditor.Core/Parsers/General/GeneralSettingParser.cs) | 添加磁盘版本/冲突检测；目前外部改表后先刷新/重新打开，同键 strings 也无冲突提示 |
| R07 | 中，历史丢失 | 场景缓存保留 map、path、dirty，但 OpenFile 会清 UndoManager；恢复地图没有完整撤销历史 | [FileStateManager.OpenFile](../../WC4MapEditor.Core/Commands/FileStateManager.cs)、[RenderSceneManager](../../Services/RenderSceneManager.cs) | 让撤销历史跟随文档，或在界面明确说明；当前缓存测试只保证模型和保存路径 |
| R08 | 中，内存预算缺口 | 普通地形/省区大范围编辑可存大量快照；UndoManager 的 byte budget 只计算 MapResizeCommand | [UndoManager.TrimHistory](../../WC4MapEditor.Core/Commands/UndoManager.cs) | 为所有命令统一估算占用、回收历史并做固定场景基准；条数上限不等于总内存上限 |
| R09 | 中，功能缺口 | 417 个 PKM 不能解码；海岸 helper 仍查内置 MapCoast，全部建筑/兵种资源未接入项目图集 | [CoastHelper](../../WC4MapEditor.Rendering/Helpers/CoastHelper.cs)、图像加载与对象渲染路径 | 适配 PKM/ETC、锚点、完整对象资源；目前占位/颜色不证明项目完整观感 |
| R10 | 中，功能缺口 | world codec 有省份平面，BuildSceneModeMap 的 world 仅 TerrainPaint | [EditModeManager.BuildSceneModeMap](../../WC4MapEditor.Core/Modifiers/EditModeManager.cs) | 增加 world 省份入口与保存/撤销验证；当前不能说 world 全平面均可 GUI 编辑 |
| R11 | 低，自定义地图缺口 | 本地地形 BTL 若命名为 conquest，会进 ConquestRenderScene，仍没有 TerrainPaint；1.30.0 的 9 张 conquest 均为外部图，因此该语料未触发 | [ConquestRenderScene](../../Views/ConquestRenderScene.cs)、[RenderSceneBase.EditingSceneType](../../Views/RenderSceneBase.cs) | 可按 FileKind/MapNumber 决定模式；不要单凭文件名控制自带地形可编辑性 |
| R12 | 待核实，显示/缓存 | 奇数 capture X 的对象/点击偏移、refx/refy 比例；同项目图集保存后长期存在的 TerrainHelper 自有裁切缓存不检查 revision | [TerrainHelper](../../WC4MapEditor.Rendering/Helpers/TerrainHelper.cs)、Camera/绘制入口 | Windows 对照截图/格中心和热更新；必要时重建场景/重新加载资源，未确认所有分支自动更新 |
| R13 | 规划缺口 | world/BTL 同步修改、区域写回、扩展和独立战役转换未实现 | [ProjectMapDocument](../../WC4MapEditor.Core/Assets/ProjectMapDocument.cs)、[demand 第 6 节](../../demand.md#6-p3worldbtl-关联编辑) | 建立关联文档后逐版本验证；当前只读限制继续保留 |

资源根切换已有确认与刷新，但它不是覆盖所有配置编辑器和主窗口退出的统一未保存保护。AtomicFile 对可恢复 I/O 故障尝试回滚；若回滚也失败会保留备份并抛 AggregateException，仍不是跨进程、断电或所有文件系统上的整体事务。

## 7. 加载、编辑、保存及故障矩阵

| 生命周期/故障 | 已验证路径 | 结果 | 未覆盖范围 |
|---|---|---|---|
| 项目创建成功 | 完整复制、逐文件哈希、空目录、mtime、manifest | 输出结构与源一致；只增加项目 metadata | Windows 权限、中文/超长路径及磁盘不足实机 |
| 相同/互相包含目录 | Core 路径校验 | 拒绝创建，保护源目录 | 不提供输入目录链接支持 |
| 已有非空输出 | create / CLI 回归 | 拒绝覆盖；已有修改不变 | 用户应使用 open/info，不用 create 覆盖 |
| 取消 / 目录链接 | staging 与链接回归 | 不发布部分项目，正常失败/取消清理 | 非正常终止后的临时目录回收 |
| 重开 | manifest / CLI / 文件重读 | 保留已保存结果，不重拷来源 | 并发操作同一项目的冲突检测 |
| 场景切换与缓存 | 实际 WPF 场景管理源码在 net10.0 测试中编译 | path、dirty、新地图、已保存后缓存失效正确 | 未验证真实控件交互；undo 历史未保留 |
| 无法保存外部地形 | ProjectMapDocument / scene cache | 报错并保留内存；阻止切换 | 不能直接修改/写回 world 区域 |
| 重复 Attach | 带 pending edits 的地图 | 不重新覆盖修改或重置 baseline | 跨输出项目复用 map 明确拒绝 |
| 无修改二进制保存 | 1,383 项目 BTL、5,523 总语料、world | 字节一致 | 不代表所有修改操作的游戏结果正确 |
| 无修改 JSON 保存 | 16 张可写表、将领语料 | 语义一致，unknown/null/缺省/重复 ID 保留 | 空白/注释不保真；外部并发修改无检测 |
| 损坏/缺失 JSON/XML | 合成失败用例 | 禁止用旧/部分数据覆盖；多表先校验 | 业务上跨 ID/跨表编辑是否有效需关系审计 |
| 扫描中途 I/O 失败 | 锁定 BIN 测试 | 保留旧 root/index/loaded/revision | 项目会话重载的所有异常分支没有 WPF 实机覆盖 |
| 普通图片/图集保存 | 解码、空 objects、实际编码、XML metadata、pair 失败 | 有效 source 才保存；失败保留原盘与候选内存 | JPEG/WebP 有损重编码、所有真实图集 UI 未逐一验收 |
| 关闭/返回 | 主窗口与编辑器静态检查 | R01 尚未解决 | 未保存数据不可依靠进程内缓存恢复 |
| 打包/运行游戏 | 未执行 | 无游戏兼容结论 | APK 签名/安装、UI、地图、AI、存档、native SO |

## 8. demand.md 全需求矩阵

| 需求节点 | 当前状态 | 本轮进展 | 剩余验收或实现 |
|---|---|---|---|
| 项目目录加载与独立输出 | 基础实现已验证 | 完整副本、项目会话、地图文档、缓存和渲染资源 | R01–R13 与 Windows/游戏验收 |
| P0 / 统一坐标 | 部分完成 | HexCoord、Camera 负列、brush distance、river/province 邻接 | 所有选择/精确点击、海岸与有限/环绕边界共用策略 |
| P0 / 修改撤销 | 部分完成 | F4、F5、选区移动完整格 undo/redo；地形/省区失败回滚 | 所有批量对象/平面、脚本整体失败、统一命令入口 |
| P1 / 工程职责与旧入口 | 未整体完成 | 项目副本与 JSON/图集保存服务分离 | 大场景拆分、DI/单例、旧模块/XAML/脚本依赖核实 |
| P1 / Android 基础资源 | 部分替代路径 | 项目真实 190 地形变体裁切 | 未迁移 Android PNG/许可资料；完整海岸/建筑/兵种仍缺 |
| P1 / 集中工具和属性面板 | 未实现 | 复用已有快捷键与对象属性入口 | 统一可见面板、锁定/连续放置、模式和 dirty 展示 |
| P2 / 魔棒连通选区 | 未实现 | province flood fill 邻接修复是现有修改操作 | 区域预览/选择/合并、同组+变体规则、完整撤销 |
| P2 / 固定 seed 随机地图 | 未实现 | 无新增生成服务 | 算法版本、参数、可重复生成、一次撤销 |
| P2 / 参考图填绘 | 未实现完整流程 | 上游图像识别与视图层保留 | 文档绑定已绘格状态、坐标、变换、撤销、保存约定 |
| P2 / 16 字节地形样本库 | 未实现 | 完整格撤销基础修复 | 样本来源、预览、覆盖策略、特殊装饰/河流保留 |
| P2 / 落笔局部海岸修复 | 未实现 | F4/F5 可撤销 | 局部区域/邻居计算、边界策略与一次笔画合并 |
| P2 / 渲染和历史预算 | 部分基础已有 | 缓存裁切、视口与快照限制保留 | 全命令 bytes、像素预算、固定场景实测与回收策略 |
| P2 / APK/ZIP 内地图浏览 | 未实现 | 只支持解包目录 | 归档条目/提取预算、越界保护、预览与明确另存 |
| P3 / world/BTL 关联编辑 | 仅底图预览与保护 | 自动捕获 world 地形、跨右边界读取、非法保存拒绝 | 区域写回、多个 dirty 文档、坐标变换和联合撤销 |
| P3 / 扩展与独立战役导出 | 未实现 | 既有单文件变换保留限制 | 完整对象/容量/未知段转换、样本及游戏验收 |

不把“有旧入口”“可以复制文件”“byte roundtrip 通过”标成需求完成。P0 的剩余工作仍是新地图制作算法的前置条件。

## 9. 只读资源关系审计的结果

本次 CLI 扫描了 **81 张 JSON 表、1,383 张地图**，提取的 **155,576 条显式引用均 resolved**。审计输出 **1,066 条 error 规则诊断、0 条 warning**：

| 诊断 | 数量 | 真实情况 | 解释与行动 |
|---|---:|---|---|
| skill-max | 1,062 | GeneralSettings 中 SkillsMax=0 的记录仍有已有 Skills；全表 SkillsMax=0 共 1,063 行 | 当前审计器将 0 当数量上限；NPC/固定将领的游戏语义未确认，不能据此清技能或宣称文件损坏 |
| feature-length | 4 | Id 349001–349004：Feature 4 个值，FeatureLevel 5 个值 | 原始文件确实存在数组长度差异；是否应忽略多余 level 或补 Feature 需核对游戏规则 |

`asset audit` 返回 `1` 代表存在 error 诊断，不代表扫描进程崩溃。**本次没有修改或自动修复原游戏资源。** 规则与游戏真实语义之间的差异需单独分析，不能为了让报告归零而删除有效数据。

已声明的范围是 32 类显式 JSON 关系及 BTL 部署部队；**82,843 条援军记录因字段语义未确认而跳过**。事件/opaque/native/save 引用、图片/XML/本地化完整性和 Android 运行不在该审计器的完整覆盖范围。

本机原始报告位于被忽略的 `tmp/audit-wc4-1300-20261008.json`，日志为 `tmp/audit-wc4-1300-20261008.log`。本文保存数量、分类与解释，避免将大体量机器路径报告当作发行资源。

## 10. 验证记录与复现

| 验证 | 本次结果 | 记录 |
|---|---|---|
| 含真实语料和渲染的完整回归 | 82 passed, 0 failed | `tmp/audit-final-tests.log` |
| 本轮合成/渲染回归 | 74 passed, 0 failed | `tmp/audit-final-smoke.log`；不含真实语料的 8 条测试，亦纳入最后一次 82 条完整回归 |
| Windows win-x64 交叉构建 | 成功，16 warnings、0 errors | `tmp/audit-wpf-final.log`；增量构建的 warnings 数量不代表全工程清零 |
| 1.30.0 源目录保护 | 4,580 文件复制/保存后源 SHA-256 不变 | `GameProjectTests.RunCorpus` |
| 项目地图 | 1,383 字节一致，1,073 local / 310 external | `GameProjectTests.RunCorpus` |
| 总 BTL 语料 | 5,523 字节一致 | `Program.cs --corpus` |
| 配置 | 16 张可写表实际保存，1 张只读；共 6,908 行 | `EditorDataAuditTests.RunCorpus` |
| 地形图片 | 190 个变体与原图集裁切像素一致 | `ProjectRenderingTests.RunCorpus` |
| 将领语料 | 5 张表，6,035 行语义一致 | `GeneralDataTests.RunCorpus` |
| Windows UI / Android 游戏 | 未执行 | 构建、数据和像素结果不替代这两项 |

本机 SDK 为 `/home/j60100428/game/tmp/dotnet/dotnet`，版本 10.0.401。图片测试需要 SkiaSharp 3.119.2 原生库；本机使用被忽略的 `tmp/skia-linux/libSkiaSharp.so`。

```bash
# 从仓库根运行；无本机 SDK 路径时使用已安装的 dotnet
LD_LIBRARY_PATH="$PWD/tmp/skia-linux${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
  /home/j60100428/game/tmp/dotnet/dotnet run \
  --project WC4MapEditor.Tests --no-restore -- \
  --corpus /home/j60100428/game --rendering

/home/j60100428/game/tmp/dotnet/dotnet build WC4MapEditor.csproj \
  -r win-x64 -p:EnableWindowsTargeting=true --no-restore

# 输出路径需是未存在的新文件
/home/j60100428/game/tmp/dotnet/dotnet run --project WC4MapEditor.Cli -- \
  asset audit "/home/j60100428/game/wc4/World Conqueror 4_1.30.0/assets" \
  --include-maps --output tmp/another-wc4-1300-audit.json
```

## 11. 下一节点的验收顺序

1. 优先处理 R01–R06：统一未保存退出、国家跨表删除/失败反馈、头像真实路径与编码、多输出失败、JSON 外部修改冲突。
2. Windows 运行完整输出目录中的 `WC4MapEditor.exe`，测试中文与长路径项目目录、复制取消、继续编辑和切项目；保存前后确认源不变。
3. 在自带地形 BTL 中实际绘制、河流、F4/F5、选区移动、对象部署和属性修改；一次撤销/重做，Ctrl+S、重开对照。
4. 核对 world 及两个跨右边界 capture 的格中心、点击和对象位置；修改 world 后显式保存、重新打开 BTL；非法关联修改需保留内存并报错。
5. 逐专用编辑器检查保存失败、切场景 dirty、外部文本刷新；检查 WebP/PNG/JSON/XML 实际目录、图集 metadata 和热更新，不仅看成功提示。
6. 完善 PKM/对象显示与 P0 剩余部分后，按 demand 的阶段进入工具面板、地图制作、内存/性能和关联编辑；APK 打包后单独做原游戏验收。

历史节点及原 README 快照见[历史索引](../history/README.md)。后续节点追加新矩阵和完成记录，历史文件保持原样。
