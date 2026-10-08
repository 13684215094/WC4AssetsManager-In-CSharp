# 2026-10-12：Windows 复现报告复核与修复

本节点针对用户在 Windows 上对 `8fd01cc` 的八项复现报告，复核当前 `6.0` 工作区，并修复仍存在的操作路径问题。工作区已有 2026-10-11 的增量保存、项目首页、图片加载、APK、科技和技能修改；本次在其上继续调整，保留已有修改。这里的“修复前”指本轮开始时的工作区，不等于用户测试的旧提交。

**八项报告都值得处理。本轮开始时，第 1、5、7 项仍存在；第 2、4、6 项已有部分保护，但还有需要修正的路径；第 3 项的版本检查与第 8 项的新建入口限制已在前一节点实现，本轮补充实际操作回归并完善新建文档流程。** 当前实现和自动回归通过，Windows 界面的实际点击验收仍需重新执行。

## 1. 八项问题矩阵

| 编号 | 用户复现 | 修复前工作区状态 | 本节点处理 | 自动证据与边界 |
|---|---|---|---|---|
| W01 / 高 | A→B 后 `army remove-all` 清空 A；格子信息也来自 A | 仍存在。同类场景只换了画面地图，命令与格子服务没有一起绑定 | 打开、新建、初次加载、同类切换统一绑定地图、文件状态、命令、撤销、格子信息和编辑模式；清除旧选区与框选。退出只解除仍属于自己的地图 | 生产绑定测试确认命令仅删除 B 的部队、格子宽度来自 B、修改器读 B，撤销/重做互不影响；切回 A 保留历史 |
| W02 / 高 | 资源浏览器刷新丢失未保存技能修改 | 离页/关闭已有保存、放弃、取消保护；刷新仍通过重启会话清理解析器与地图场景 | 单独的 `RefreshAssets()` 只重新扫描文件和清图片缓存，保留项目、配置解析器与地图文档；状态提示外部配置变化需进入对应编辑器重载 | 技能 Level 2→9 的草稿和解析器实例在刷新后保持；外部修改后刷新也不会把旧草稿强行写回磁盘 |
| W03 / 中 | 外部更新未知字段、新增记录后，专用编辑器保存覆盖它们 | 前一节点已增加 SHA-256 文件版本检查及提交前再次检查 | 本轮使用实际技能解析器验证拒绝冲突、保留草稿、明确重载后重新修改并保存；不自动合并整张 JSON 表 | 外部 Unknown 字段和新增行在保存失败后字节不变；重载、再修改 Level 保存后两项仍保留 |
| W04 / 中 | HD XML 损坏，制旗失败但战术图集已改写 | 前一节点已准备两组候选并统一提交；HD PNG 的查找仍依赖 XML，半组缺失可能被误认为无图集 | 图片与 XML 独立查找；任何半组缺失或解析错误先报错；两组图片/XML 全部准备、验证后一次批量保存 | 完整制旗工作流覆盖损坏 HD XML、XML 目标是目录、HD 目录被文件阻塞及修复阻塞后重试；失败不发布战术输出，成功生成四文件 |
| W05 / 中 | 同名导入清掉锚点；复制红块粘成目标处绿块 | 仍存在。UI 绕开底层同名导入；剪贴板没有源像素 | UI 统一调用底层导入，保留位置、锚点与 XML 元数据；复制时分别快照对象属性和像素，粘贴使用独立像素与唯一名称 | 同名导入保留 `(7,-3)`；复制后替换/删除原块，反复粘贴到绿色区域仍为复制时的红色；保存、重开像素与元数据正确 |
| W06 / 中 | 图集保存后重新初始化仍显示旧图 | 前一节点已给共享图块缓存增加 Revision 检查；部分渲染器因 `IsInitialized` 判断跳过重载，另有旗帜副本和未找到缓存 | 部队/援军先初始化缓存；首都、归属、陷阱、空军、战略设施旗帜的副本按 Revision 失效并重读；释放旧缩放图 | 同一 TerrainHelper 的图片覆盖、战术头像/国家图片及实际首都/归属/陷阱渲染缓存回归通过；红→蓝及新增原缺旗帜可恢复 |
| W07 / 中 | 删除唯一图块后，XML 清空但保存恢复原图片 | 仍存在。对象数为零被当作普通图片 | 文档显式保存 `IsAtlas`；合成器必须接收文档类型；零对象图集输出透明画布并写空 XML；界面不再为它补整图对象 | 删除唯一块、保存、重开后仍是空图集，所有像素透明；普通图片在没有图集对象时仍保留原图 |
| W08 / 中 | 新建 MapNumber=2 保存成功，重开却不支持 | 前一节点已约束编号 1 / 活动项目 world.bin；创建和保存逻辑还可进一步集中 | 新建入口使用 `ProjectMapDocument.CreateConquest()`，固定有效 world.bin、编号 1、上下各两行边距，保存经同一项目文档验证；高度不足提前拒绝 | 合成 world 创建、保存、重开后地形、编号、军团正确；编号 2 和高度不大于 4 拒绝。不扩展任意编号底图支持 |

所有问题都通过用户所描述的操作顺序或对应生产服务验证；不能以单纯的读写往返替代操作回归。图片测试直接调用 UI 使用的导入/剪贴板/合成服务，地图测试调用 UI 使用的统一绑定入口；这些测试没有启动 WPF 窗口。

## 2. 继续排查发现的问题

### 2.1 画面对象仍可能来自旧地图或旧相机

`SkiaRenderEngine` 中的建筑、部队、陷阱、援军、空军等渲染器在构造时持有地图和相机，之前只要实例存在就继续复用。即使 W01 的命令上下文正确，画面仍可能显示 A 的对象。

现在地图或相机实例变化时统一释放这组渲染器，按新的地图与相机重新创建，并清地图相关缓存。回归把复用引擎的 B 图结果与新引擎逐像素比较，同时验证只替换相机、删除 B 的最后对象。建筑名称开关改为由引擎保存，避免懒创建或切图重建后恢复默认显示。

### 2.2 旧场景留下全局回调与控制台命令

错误事件、控制台可见性、CLI 修改通知与字符串修改事件改为命名处理器，退出/销毁时解除；错误恢复策略也按场景注册、解除，避免反复进入后积累。

场景注册的控制台命令记录自己的处理器，退出时只删除仍指向自己的注册。独立回归确认旧场景退出不会移除新场景替换后的 `undo` 命令。该处理保留现有场景管理结构，没有把整个 4,600 余行的页面改写成新框架。

### 2.3 BTL v3 控制台删除与撤销

`army list` 和 `army remove-all` 之前只读写旧版 `Armies`，忽略 v3 的 `ArmiesV3`。本次按 BTL 版本选择实际列表，部队/陷阱批量删除接入当前文档撤销历史并标记未保存。

回归覆盖 v3 列表、清空、撤销和重做；A→B 删除也使用 B 的历史。其他控制台命令与整段脚本并未全部事务化或统一为一次撤销，不能据此声明所有控制台编辑都具备完整撤销。

### 2.4 图块改名与负锚点

属性对话框之前直接修改对象，可能让名称索引仍指向旧名；负 `refy` 也被错误限制。现在使用草稿确认、统一更新和重名检查，名称索引与像素键同步；`RefX/RefY` 接受完整整数范围，不以图块尺寸作为边界。

XML 原节点由 `OriginalName` 关联，改名后保留未知属性，保存成功才更新该关联。回归覆盖两次改名、重名拒绝、`refy=-3` 和未知属性保留。锚点实际在游戏中的摆放效果仍需实机核对。

## 3. 代码与回归入口

| 范围 | 生产实现 | 回归入口 |
|---|---|---|
| 地图/命令/格子/撤销绑定 | [MapEditingBinding](../../WC4MapEditor.Core/Services/MapEditingBinding.cs)、[地图页面](../../Views/RenderSceneBase.cs)、[命令注册](../../WC4MapEditor.Core/Commands/CommandManager.cs) | [ProjectSceneTests](../../WC4MapEditor.Tests/ProjectSceneTests.cs) |
| 文件列表刷新、保留解析器 | [数据会话](../../WC4MapEditor.Core/Assets/GameProjectDataSession.cs)、[桌面会话](../../Services/GameProjectSession.cs)、[资源浏览器](../../Views/AssetBrowserScene.xaml.cs) | [ProjectWorkflowTests](../../WC4MapEditor.Tests/ProjectWorkflowTests.cs) |
| JSON 冲突 | [JsonTableFile](../../WC4MapEditor.Core/Parsers/JsonTableFile.cs)、[FileVersionGuard](../../WC4MapEditor.Core/Parsers/FileVersionGuard.cs)、[提交跟踪](../../WC4MapEditor.Core/Parsers/ResourceSaveTracker.cs) | ProjectWorkflowTests、EditorDataAuditTests |
| 制旗、导入、复制、改名、空图集 | [FlagEditorService](../../WC4MapEditor.Rendering/Imaging/FlagEditorService.cs)、[TacticalMapEditor](../../WC4MapEditor.Rendering/Imaging/TacticalMapEditor.cs)、[AtlasObjectClipboard](../../WC4MapEditor.Rendering/Imaging/AtlasObjectClipboard.cs)、[AtlasCompositeRenderer](../../WC4MapEditor.Rendering/Imaging/AtlasCompositeRenderer.cs)、[图集界面](../../Views/TacticalMapEditScene.cs) | [ImageEditorAuditTests](../../WC4MapEditor.Tests/ImageEditorAuditTests.cs) |
| 地图/相机渲染绑定与图片刷新 | [SkiaRenderEngine](../../WC4MapEditor.Rendering/Skia/SkiaRenderEngine.cs)、[TacticalMapImageCache](../../WC4MapEditor.Rendering/Skia/TacticalMapImageCache.cs)、各旗帜与部队渲染器 | [ProjectRenderingTests](../../WC4MapEditor.Tests/ProjectRenderingTests.cs)、[ResourceImageTests](../../WC4MapEditor.Tests/ResourceImageTests.cs) |
| 新建征服 | [ProjectMapDocument](../../WC4MapEditor.Core/Assets/ProjectMapDocument.cs)、[新建入口](../../Views/BeginScene.xaml.cs) | ProjectWorkflowTests |

## 4. 验证记录

完整回归日志为 `tmp/windows-audit-corpus-final.log`，Windows 构建日志为 `tmp/windows-audit-build-verified.log`。这些本机日志被 Git 忽略，不作为游戏发布资源。

| 验证 | 结果与含义 |
|---|---|
| 包含真实语料、渲染和本轮操作路径的完整回归 | **140 passed，0 failed**；覆盖前一节点全部 129 项与本轮 11 项新增操作回归 |
| Windows 交叉构建 | 最后一次 `win-x64` / WPF 构建成功，**0 错误、5 条警告**，为既有 nullable/未使用字段警告；增量编译不表示全工程仅余五条警告 |
| 真实地图 | 1.30.0 的 1,383 张 BTL，其中 310 张外部底图；全语料 5,523 张 BTL 逐字节往返一致 |
| 真实项目与配置 | 3,474 assets；81 张表 / 22,055 行；主要专用表的未知字段继续保留，未改文件不复制 |
| 真实图片与科技/APK | 1,677 图片、417 PKM、94 图集 / 6,675 矩形、60 国家两组旗帜、191 科技节点和真实 APK 加载/增量保存继续通过 |
| 来源保护 | 原目录 **4,580 个文件，0 修改、0 新增、0 删除**，SHA-256 与本轮开始前快照相同；真实 APK 测试前后 SHA-256 相同 |
| Windows / Android 运行 | 本机 Linux，没有启动 WPF 或 Android；构建与核心/渲染回归不替代实际界面和游戏验收 |

本机 APK 的 SHA-256：`14b8de3a5a2578083bdb7a6cadcec44211299f022636df947a5f8ee045118709`。

复现命令（从仓库根运行，测试与构建串行执行）：

```bash
LD_LIBRARY_PATH="$PWD/tmp/skia-linux${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
  /home/j60100428/game/tmp/dotnet/dotnet run \
  --project WC4MapEditor.Tests --no-restore -- \
  --corpus /home/j60100428/game --rendering

/home/j60100428/game/tmp/dotnet/dotnet build WC4MapEditor.csproj \
  -r win-x64 -p:EnableWindowsTargeting=true --no-restore
```

测试和 WPF 构建共享部分 .NET 中间文件，不能并行运行；本轮并行尝试曾出现 `.deps.json` 文件竞争，随后已改为串行验证。这个环境竞争不计作应用的保存或加载缺陷。

## 5. Windows 操作验收清单

在新的独立输出目录验收，完整使用 publish 目录及 `libSkiaSharp.dll`。游戏来源路径可以是目录或 APK，源文件不参与写入。

| 场景 | 操作 | 预期 |
|---|---|---|
| A→B→命令 | 打开两张同类型地图，A 修改一次地形，切 B 执行 `army remove-all`，查看格子信息，撤销/重做，再切回 A | 只修改 B；格子信息与画面都是 B；A 的修改与撤销历史保留 |
| 编辑→刷新 | 修改技能后返回资源页；离页提示选保存/取消分别核对，资源页刷新后再次打开；新加外部文件再刷新 | 未明确放弃的草稿不会静默丢失；新增文件可见；刷新不会清空缓存地图场景 |
| 外部 JSON 修改 | 打开技能页，外部编辑有效 JSON 的未知字段并加行，再在技能页保存 | 显示冲突，磁盘文件和当前草稿保留；明确重载后再改、保存，外部字段和新行仍存在 |
| 制旗后半段失败 | 在测试项目中破坏输出 HD XML 或阻塞 HD 目标路径，制作国旗，再恢复并重试 | 失败时战术图片/XML 没有先被改写；提示实际错误，重试后两组共四文件正确 |
| 同名导入与锚点 | 图块设 `(7,-3)`，导入同名新图片，保存、重开 | 保留位置和锚点；新尺寸/像素正确；XML 未知元数据保留 |
| 复制→粘贴 | 复制红色图块，替换/删除原块，再在绿色区域粘贴两次，保存、重开 | 两次都是复制时红色像素；名称唯一；不是目的坐标原像素 |
| 保存→地图预览 | 修改已有旗帜/战术图块并保存，重新进入地图，查看首都/归属/陷阱及部队；再添加原缺条目 | 显示新图；原“未找到”结果不持续缓存；无需用破坏编辑状态的刷新绕过 |
| 删除最后图块 | 单块图集删至零对象，保存、重开 | XML 为空图集，透明像素不恢复；没有自动整图对象 |
| 新建征服 | 在项目内新建，保存、重开；另外用缺 world 或过小 world 的测试项目 | 正常项目始终编号 1 并使用活动 world；无效项目先报错，不生成无法重开的编号 2 |
| 场景生命周期 | 多次进入/返回地图，使用 undo/goto/F3，切图后改变相机与建筑名称开关 | 命令与事件只作用于当前页面；开关、对象和相机匹配当前地图 |

## 6. 保存与结构的剩余边界

- 文档历史、绑定和场景缓存本轮已统一关键入口，但地图页面仍较大；完整分拆为独立文档控制器属于后续结构工作。
- 关联文件保存有提交前校验、暂存与常规 I/O 失败回滚，不是断电级或多个进程之间的事务；版本检查不能承诺消除检查完成之后所有跨进程写入竞争。
- JSON 冲突会拒绝并要求重载，不自动三方合并。重载前应自己保留需要重做的修改。
- 文件列表刷新保留文档快照，不等于所有配置、地图和字符串草稿自动接收外部变化；显式重载仍是配置更新入口。
- 本轮不补源项目不存在的半身像，不扩展任意 world 编号，不新增 APK 重建/签名或 PKM 编码，也不声明 world/BTL 联动写回已经完成。

操作补充见 [图片与配置编辑](../projects/wc4-1.30.0/resource-editing.md#7-2026-10-12-操作规则补充)。旧节点和历史 README 快照保留；本节点仍为未提交、未推送的工作区修改。
