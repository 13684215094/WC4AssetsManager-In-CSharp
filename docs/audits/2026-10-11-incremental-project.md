# 2026-10-11：增量项目与审计问题修复矩阵

> 后续图片与配置节点：[图片加载审计](2026-10-11-resource-loading.md) 追加图集路径/像素/缓存修复、417 PKM 只读预览与 81 表字段编辑。R09 已推进到 PKM 可解码预览，编码写回、完整海岸/对象美术及 R13 联动仍未完成；本报告的 104 项统计保留为当时验证。

本节点在 `6.0` 的 `8fd01cc` 基础上实现。用户要求输出只包含新增或修改文件，并修复 [2026-10-08 审计](2026-10-08-project-matrix.md) 的问题。本文追加当前结论；旧报告的完整副本、82 条测试和当时的缺口保留为历史。

**新建项目已改为增量输出，真实 1.30.0 地图和主要配置无修改保存不产生资源副本。R01–R08、R10、R11 的具体缺陷已有修复与相应核心回归或构建证据；R12 缓存失效已修，其显示/点击部分仍待 Windows 核对；R09 和 R13 的完整功能仍未实现。**

原项目 `/home/j60100428/game/wc4/World Conqueror 4_1.30.0` 只读使用。测试在 `tmp/tests` 下生成独立输出或合成输入，不写原目录。详细操作、manifest、旧项目兼容、发布边界见 [增量输出说明](../projects/wc4-1.30.0/incremental-output.md)。

## 1. 增量加载与保存矩阵

| 编号 | 操作或故障 | 当前结果 | 验证证据与限制 |
|---|---|---|---|
| Q01 | 选择完整解包项目 | 创建 v2 项目；没有全量复制 | 真实来源 4,580 文件；刚创建仅一个 manifest 文件和空 assets 根 |
| Q02 | 直接选 assets 或仓库型目录 | 保持对应资源相对路径 | 支持 assets、WC4DATA/assets、Resource/WC4DATA/assets；路径回归 |
| Q03 | 原资源未在输出出现 | 逐文件回退读取原目录 | 地图、配置、图集、字体、文本及目录枚举使用逻辑路径解析 |
| Q04 | 一个目录只有部分输出 | 合并同目录中的来源和输出文件 | 图集图片单独覆盖时仍能读取来源 XML；同 helper 热更新像素测试 |
| Q05 | 原文件无修改保存 | 不生成资源输出 | 全部 1,383 项目 BTL、将领及各主要专用配置；BOM JSON、XML 排版合成测试 |
| Q06 | 修改原文件 | 输出完整修改文件，保持相对路径 | BTL、world、JSON、文本、图片及 XML 回归 |
| Q07 | 新增文件或地图 | 仅创建新文件及所需目录 | 嵌套新增、首次保存路径与重开索引测试 |
| Q08 | 保存后恢复原内容 | 删除对应输出项，回退来源 | JSON、文本、带 BOM/CRLF/空白的字符串表；保留其他新增文件 |
| Q09 | 继续打开 | 保留覆盖项，合并来源资源 | v2 重开与 source/override/new 三种读取路径测试 |
| Q10 | 原目录不存在 | v2 拒绝重开；v1 可打开旧完整副本 | manifest 版本兼容测试；不自动迁移旧副本 |
| Q11 | 相同、互相包含、越界路径 | 明确拒绝 | 创建、输出校验、manifest 逃逸测试；描述文件不允许作为资源输出 |
| Q12 | 来源/输出含链接或后来插入链接 | 明确拒绝 | 创建/重开、递归枚举、DirectoryExists、输出校验回归；Windows 链接交互未实机 |
| Q13 | 非空输出、取消或创建失败 | 不覆盖已有项目，不发布部分结果 | staging、取消、非空输出用例；异常进程中止不承诺自动恢复 |
| Q14 | 一个批次包含不可保存文件 | 不发布部分候选；失败保留内存 dirty | 国家两表、JSON/XML、头像和国旗/图集候选保存回归；I/O 回滚不是断电事务 |
| Q15 | 外部修改已加载配置 | 拒绝覆盖并提示重新加载 | JSON、将领、头像 XML、文本冲突测试；不是所有地图/脚本的并发编辑保护 |
| Q16 | 多编辑器修改字符串表 | 不同键重新准备时合并，同键冲突拒绝 | 旧合并用例及新同键、候选准备后外部修改用例 |
| Q17 | 打开通用文本而未修改 | 不复制、不保存 | 内置 UTF-8 文本文档回归；GUI 页通过交叉构建 |
| Q18 | 图像像素未变 | 复用符合扩展名的原编码，不新增输出 | 原 WebP 字节回归；扩展名与实际编码不符时重新编码测试 |
| Q19 | 原项目保护 | 原目录全部文件哈希不变 | 真实项目创建、全部地图无修改保存、配置保存及两张地图编辑后逐文件 SHA-256 对比 |
| Q20 | 完整游戏发布 | 输出需叠加到独立的完整同版本目录 | 未实现 APK 合成/打包/签名，不提供删除原文件的 tombstone |

资源类型支持范围沿用 [1.30.0 项目资料](../projects/wc4-1.30.0/README.md) 和旧审计。增量加载不增加 PKM、音频或任意辅助 BIN 的编辑能力。

## 2. 原审计 R01–R13 处理结果

| 编号 | 当前状态 | 本次实施 | 证据与仍有限制 |
|---|---|---|---|
| R01 | 已实现，GUI 待实机 | `IUnsavedScene` 接入地图、专用配置、文本、图集、布局和头像/国旗页；主窗口关闭、返回、项目切换检查 dirty，保存失败阻止离开 | 核心 dirty、成功/失败保存回归；WPF 构建。全部保存逐文档执行，不是整个项目的原子事务；未覆盖断电恢复 |
| R02 | 核心已验证，GUI 待实机 | 国家删除先构造两表候选，新增先构造国家表候选；成功后发布内存列表；保存、真实名称同步与新增失败展示原因；国家国旗复制批量准备 | 删除失败两表无部分输出、内存保留、重试成功、插入冲突测试；UI 不再忽略 bool 后误报成功。删除国家仍不自动修复所有其他业务引用 |
| R03 | 已验证 | 半身像写 generalphoto，圆图/head 写 heads；优先原 WebP，否则 PNG，读取与保存规则一致 | 来源 WebP 路径、编码、heads 目录和路径逃逸测试；Windows 头像实际显示待验收 |
| R04 | 已验证 | 头像/旧国旗全部 resize 和编码成功后批量保存；任何必要子输出失败均 Success=false | 非法尺寸及不可写父路径测试，无部分输出；原文件不使用 OpenWrite 留尾部 |
| R05 | 核心批次已验证，完整 UI 待实机 | 战术、HD 两组图片/XML 先准备再共同保存；准备阶段恢复 live 对象，失败不留下半组更新 | 两 atlas 准备和失败批次用例、原图/布局内存不变；MakeFlagWorkflow 与国家复制路径静态复核、交叉构建 |
| R06 | 已验证具体保存路径 | JSON、将领及头像/兵种 XML 增加磁盘版本检查；文本/布局同样检查；字符串表同键冲突拒绝、不同键合并 | 外部 JSON/XML/文本不覆盖、pending dirty 保留；batch 成功才更新 baseline，另存为不误接受原文件 baseline。未实现自动三方合并或所有图片/地图的冲突检测 |
| R07 | 已验证，历史回收提示待实机 | 历史跟随 MapData；同/跨场景使用 canonical FileState；切换预算内保留 undo/redo，缓存恢复路径和 dirty 不变 | 多地图 undo/redo、独立文档、磁盘缓存及历史淘汰测试；超预算回收旧历史并提示，不跨进程保存撤销历史 |
| R08 | 已验证估算预算 | 所有 IUndoableCommand 提供 EstimatedBytes，undo/redo 栈均计入；批量建筑/部队 delegate 补估算；超预算操作仍 dirty | 各命令预算、失败 undo 不丢 command、过大操作与 Clear 不误标 dirty 回归。256 MiB 是估算历史/非活动文档预算，不是总内存硬限制；固定大图性能基准仍待完成 |
| R09 | 未实现完整功能 | 本节点仅确保原资源可增量读取与索引 | 417 份 PKM/ETC、全部对象美术和实际项目海岸纹理适配仍缺；保留占位/颜色，不宣称完整预览 |
| R10 | 核心已验证，GUI 待实机 | world 模式加入 ProvinceEdit | world 省份操作、undo/redo、增量保存及重载测试；鼠标省区绘制待 Windows 验证 |
| R11 | 核心已验证，GUI 待实机 | 编辑模式依据 FileKind/MapNumber 决定，自带地形征服文件名不再禁用地形绘制 | conquest-local 与 stage-external 模式测试；场景显示分类可继续依据文件名，资源可编辑性依据模型 |
| R12 | 部分已验证 | TerrainHelper 检查 AssetManager.Revision，释放自有裁切和 surface、重读配置/纹理 | 同一长期 helper 在只覆盖图片、XML 仍来自源时更新像素；奇数 capture 原点的对象/点击、refx/refy 和缩放仍待实机对照 |
| R13 | 未实现 | 保留外部 world BTL 只读地形、capture 与尺寸限制，非法变化保存/缓存拒绝 | 既有 310 张外部地图与失败缓存回归；区域写回、world/BTL 联动、扩展和独立战役转换继续按 demand 的 P3 实施 |

## 3. 场景、失败与文件生命周期

| 场景 | 修复后行为 | 实际验证边界 |
|---|---|---|
| 无修改直接保存 | 不生成资源；格式重排通常不会成为 JSON/XML 的修改 | 地图字节、JSON/XML 内容和同像素编码测试；未知布局节点保真未全量验证 |
| 保存成功或保存失败 | 全批次成功才接受文件指纹和模型 baseline；失败保留待保存状态 | rollback/retry 和外部冲突用例；正常清理失败不误报资源未保存，会记调试日志 |
| 外部变更发生在候选准备之后 | 发布前再次校验已准备文档版本，拒绝陈旧候选 | JSON 和字符串表测试；不能保证跨程序在极短检查/替换间隙中的锁定 |
| 退出、返回或切项目 | 提示保存/放弃/取消；取消与失败保留当前编辑 | 静态入口检查和 WPF 构建；对话框、焦点与文件选择需要 Windows 实机 |
| 地图切换与恢复 | 有历史时保留同一地图实例和 history；无历史时允许磁盘恢复 | 实际 WPF scene manager 通过测试项目复用，无需启动 WPF |
| 场景历史超预算 | 按 LRU 淘汰非活动地图；保护当前和即将切入的地图，激活后执行预算；缓存保留 dirty 与模型 | 零预算切换测试；回收提示已接入标签 tooltip，未测 Windows 显示 |
| 超大操作不能进入历史 | 修改仍标 dirty；清空历史不会把已保存文档误标 dirty | ContentChanged 与 StateChanged 分离回归 |
| 来源文件在以后改变 | 尚无覆盖的资源读取新来源，覆盖资源优先输出；已加载受保护文档保存会检出版本变化 | 项目不记录全来源快照，建议来源固定；无法将稀疏输出当作自足版本归档 |

## 4. 测试结果与复现

本机 SDK：`.NET 10.0.401`。Linux Skia 使用与 `SkiaSharp 3.119.2` 匹配的 `libSkiaSharp.so`。验证命令：

```bash
# 合成与渲染回归
LD_LIBRARY_PATH="$PWD/tmp/skia-linux${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
  /home/j60100428/game/tmp/dotnet/dotnet run \
  --project WC4MapEditor.Tests --no-restore -- --rendering

# 包含本机真实语料
LD_LIBRARY_PATH="$PWD/tmp/skia-linux${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
  /home/j60100428/game/tmp/dotnet/dotnet run \
  --project WC4MapEditor.Tests --no-restore -- \
  --corpus /home/j60100428/game --rendering

# Windows WPF 交叉构建
/home/j60100428/game/tmp/dotnet/dotnet build WC4MapEditor.csproj \
  -r win-x64 -p:EnableWindowsTargeting=true --no-restore -v:q
```

| 验证 | 2026-10-11 结果 |
|---|---|
| 合成/渲染 | 96 passed, 0 failed |
| 全部语料与渲染 | 104 passed, 0 failed；当前 5,523 张 BTL 逐字节往返一致，world 语料往返通过 |
| 真实增量项目 | 4,580 来源文件未复制、源 SHA-256 不变；1,383 BTL（310 外部底图）与主要配置无修改保存只留下 manifest；两张地图修改后共三个输出文件 |
| 专用 JSON | 16 张可写表与 ConquerSettings 只读对照，共 6,908 行；350 个未建模属性出现次数保留 |
| 将领与地形 | 5 张将领表共 6,035 行语义保真；190 个地形变体裁切像素一致 |
| Windows WPF | win-x64 构建成功，0 errors；nullable、未使用字段等警告仍存在 |
| Windows 界面 / Android 游戏 | 未运行实机，不把构建、文件往返或像素测试当作游戏验收 |

新增/扩展的测试入口：[IncrementalProjectTests](../../WC4MapEditor.Tests/IncrementalProjectTests.cs)、[GameProjectTests](../../WC4MapEditor.Tests/GameProjectTests.cs)、[ProjectSceneTests](../../WC4MapEditor.Tests/ProjectSceneTests.cs)、[ProjectRenderingTests](../../WC4MapEditor.Tests/ProjectRenderingTests.cs)、[ImageEditorAuditTests](../../WC4MapEditor.Tests/ImageEditorAuditTests.cs)。日志位于被忽略的 `tmp/incremental-tests.log`、`tmp/incremental-full-tests.log`、`tmp/incremental-wpf-build.log`，不属于提交资源。

## 5. 后续验收

1. Windows 验证加载/重开稀疏项目、中文/长路径、各页面无修改保存与恢复原值；逐次检查输出文件清单。
2. 验证关闭、返回、切项目的保存/放弃/取消和失败提示；检查不同场景的撤销/重做、历史回收提示，以及所有需先应用的属性输入。
3. 核对头像/国旗的实际目录、WebP/PNG、两组图集和 XML；保存后检查显示与热更新。
4. 对照奇数 capture 原点、右边界环绕、格中心、点击和实际锚点；R12 未核实部分单独记录。
5. 按需求继续实现 PKM/对象/海岸、P0 其他入口、P1 工具面板、P2 制图算法和性能基准、P3 联动；在独立完整打包目录和原游戏中分别验收。
