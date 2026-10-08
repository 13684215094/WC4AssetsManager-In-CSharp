# 2026-10-11：图片加载与配置编辑扩展审计

## 1. 范围与结论

本节点针对用户在 Windows 包中发现的“部分资源加载失败、将领和国家图片不显示”，继续检查真实项目 `/home/j60100428/game/wc4/World Conqueror 4_1.30.0`。来源目录只读，所有编辑验证写入仓库 `tmp/tests` 的临时增量输出。

本轮完成图片路径、图集索引、颜色/透明度转换和保存后缓存刷新修复，增加 PKM 只读预览和 JSON 配置表字段编辑。保留首页先加载项目、默认同级 `<项目名>-edited`、仅输出新增/修改文件的约定。

**资源存在、解码及配置读写已经在真实 1.30.0 上验证；Windows WPF 点击显示与 Android 游戏运行仍需实机验收。** 表格编辑通过不等于游戏支持任意新增记录、任意数值或新的引擎行为。

本节点是 `6.0` 工作区上的后续修改，没有提交或推送。前一节点见 [增量审计](2026-10-11-incremental-project.md)，实际操作见 [图片与配置编辑](../projects/wc4-1.30.0/resource-editing.md)。

## 2. 真实资源矩阵

| 检查项 | 真实结果 | 结论 |
|---|---:|---|
| PNG / WebP | 360 / 900 文件 | 使用 Skia 解码，不依赖 Windows 安装 WebP 编解码器 |
| PKM | 417 文件 | 全部 PKM 1.0 / ETC1 RGB；现支持只读显示；不支持编码写回 |
| 图片总数 | 1,677 文件 | 360 PNG + 900 WebP + 417 PKM；逐文件解码通过 |
| 图集 XML | 94 文件 | 包括 assets 根目录和 image 子目录；全部为单纹理页 |
| 图集矩形 | 6,675 条 | 每条裁切范围都在实际图片内；重复分辨率条目分别核查 |
| 将领记录 | 1,189 条、248 个不同头像键 | `Photo` 优先，空时用 `EName`；记录数与图片数不能等同 |
| 半身像缺失 | 262 条记录、80 个不同头像键 | 原项目没有相应 generalphoto 文件，属于资源事实；不能靠修复读取凭空补出 |
| 圆形/战术头像缺失 | 各 1 条记录 | 都是 `Id=0`、`Photo/EName=null` 的空记录；其余 1,188 条有对应资源 |
| 国家图片 | 60 个国家 | 所有 Id 都有 HD 和 tacticalmap 大旗帜；没有通过 Logo 代替国家 Id |
| HD 国旗图集 | 75 条矩形 | 多于 CountrySettings 的 60 条，不能按表行数删掉额外图块 |
| 国家科技图集 | 122 条矩形 | 当前项目是 PNG；同路径 WebP 变体也可用 Skia 显示 |
| JSON 配置表 | 81 表、22,055 条记录 | 全部可由对象数组字段编辑器打开；assets 根级另外三个对象 JSON 仍走文本编辑 |

`CountrySettings.Logo` 与 Id 在不少记录中不同。真实文件逐项证明旗帜使用 `flag_<Id>.png`；Logo 是另一资源字段，本轮没有把它当作旗帜 ID。

## 3. 缺陷与修复矩阵

| 编号 | 证据与影响 | 本轮处理 | 验证边界 |
|---|---|---|---|
| I01 | 通用图集解析只在 assets 根查 `{name}.xml`；`image/image_flags_hd.xml` 等未被正确查找 | 支持 assets 内相对路径；裸名称按根目录及 image 约定查找；保留实际 XML/图片路径和错误原因 | 嵌套图集、单引号 Texture、XML 声明、相对纹理页测试；真实 94 图集 |
| I02 | Texture 的相对目录被丢掉，只取 basename；PNG 字面声明与实际 WebP/PKM 不同 | 按 XML 所在目录解析，保持 stem、优先 WebP，再尝试 PNG/PKM；显式相对目录保留；不搜索任意同名文件 | 同名图片置于不同目录、PNG/WebP 同时存在及缺失纹理回归 |
| I03 | 布局索引显式跳过全部子目录，漏 image 下旗帜/科技等 UI 图集 | 建立可测试的 AtlasResourceIndex，扫描整个活动 assets；同名普通/HD 资源优先 HD | 94 图集索引、国旗/科技引用可解析 |
| I04 | 布局 `image/` 前缀长 6，却用 `[7..]`，多截去首字符；散图查找又丢相对子目录 | 改成 `[6..]`，散图候选保留原始完整相对路径 | image/ 图块回归与真实数据索引 |
| I05 | 将领预览直接把未知 SKBitmap 格式当 Pbgra32；头像/国旗编辑又假定 RGBA 且重复预乘 | 统一转换到 BGRA8888 + Premul，再创建可跨线程使用的冻结 BitmapSource；取消手写交换/重复乘 alpha | RGBA/BGRA × Premul/Unpremul/Opaque、RGB565、半透明像素回归；WPF 界面待 Windows |
| I06 | 国旗、HD 图块、战术头像、布局缓存只在切项目时清；同项目保存仍可能显示旧图或缓存缺失 | 资源 Revision 变化时释放旧页并重新读逐文件叠加；战术解析器同时处理显式 ClearCache；国旗服务有 Dispose | 同服务红→蓝、只输出图片、来源 XML 回退、新旗帜/头像、多个解析器隔离测试 |
| I07 | 国家无图片时旧预览未清；头像空白或 catch 后缺少可见原因；固定宽度预览在窄窗口被裁切 | 清空旧预览，显示缺资源/解码错误；预览自动换行；异步将领图片各控件取消旧请求，离开页面取消加载 | Core 资源缺失/出界测试，UI 窄窗口、快速切换与提示待实机 |
| I08 | 国家科技只用 WPF BitmapImage 读取 PNG，其他项目改成 WebP 会失败 | WebP/PNG 路径选择及 Skia→WPF 统一转换 | 真实科技 PNG 与 WebP 解码；专用树界面待实机 |
| I09 | PKM 被资源浏览器当普通图集图片打开，而 Skia 普通解码器不支持 ETC1 | PKM 1.0 软件解码，资源管理器打开只读预览；布局可解码 PKM 纹理页；禁止当 PNG 写回 | 417 文件与独立 ETC1 解码器像素哈希一致；尺寸、块选择、flip、差分及错误长度测试 |
| I10 | 未覆盖配置只能改整段文本，进修/称号/总部科技等缺字段入口 | 新增通用 JSON 表编辑和项目内快捷入口，按记录/字段操作，支持新增、克隆、删除、新增字段 | 81 表无改保存、4 张扩展表修改/重开/恢复、专用解析器重载 |
| I11 | 新表编辑可能破坏 null、未知字段、整数、重复 Id 或覆盖外部修改 | 保留对象树、字段名称、未知/缺失值及 BOM；字段/数组类型和整数校验；允许原有重复 Id，拒绝新增重复；SHA 冲突检查及项目关闭保护 | 合成保真/克隆隔离/验证失败不部分应用/冲突/还原增量输出回归 |

所有图块裁切使用完整范围校验，出界返回错误，不用截短图片掩盖配置错误。纹理路径被限制在活动 assets 内；缺失资源不读取另一个项目。

## 4. 功能扩展与关系

| 项目内入口 | 表 | 可编辑内容 | 需要另外核对的引用 |
|---|---|---|---|
| 将领进修、称号与传记 | GeneralPromotionSettings，43 条 | 费用、能力上限、Skills/SkillsMax、Photo、AdvanceID/BaseID | GeneralSettings、SkillSettings、图片、进修链闭合 |
| 同一入口 | GeneralTitleSettings，8 条 | GeneralId、EliteType、攻击/生命等加成 | 将领、精英类型、称号本地化 |
| 同一入口 | GeneralStageSettings，40 条 | 开启状态、前置关卡、奖励、位置、GeneralId | `stage/generalstage<Id>.btl`、将领、本地化 |
| 总部科技 | TechnologySettings，191 条 | 资源费用、升级链、NeedID、Position、能力/特性 | HQ、关卡、兵种、特性与前置科技 |
| 全部配置表 | assets/json 下所有对象数组 | 字段修改、记录新增/克隆/删除、搜索、检查重复 Id、保存/重载 | 部分已知关系可用数据检查；未建模关系与 native 限制需人工核对 |

数组编辑是类型校验的字段输入，并非每个系统的专用可视化设计器。新增 Id 只在当前表中避免重号；没有自动创建同 Id 的 BTL、图片、本地化或 native 逻辑。恢复来源值时删掉该表的输出覆盖文件，其余未修改资源不复制。

## 5. 测试证据

| 验证 | 结果 |
|---|---|
| 完整 Core / CLI / Rendering / 真实语料回归 | 120 passed，0 failed |
| 图片覆盖 | 1,677 文件解码通过；94 图集、6,675 图块完整裁切通过；60 个国家两组旗帜通过 |
| PKM 独立比对 | Texture2DDecoder 1.0.6 临时安装用于验证，417 个 BGRA 像素 SHA-256 全部相同；不成为产品依赖 |
| 表编辑覆盖 | 81 表 / 22,055 行打开、无修改保存不产生输出；进修/称号/传记/总部科技修改、重开、恢复通过 |
| 已有地图回归 | 5,523 张 BTL 逐字节往返一致，world 与 190 地形变体像素验证通过 |
| 来源保护 | 4,580 文件 SHA-256、文件集合不变 |
| 文档历史 | 四份 README 历史快照 SHA-256 未变，新增节点保留旧结论和统计 |
| Windows | win-x64 WPF 交叉构建通过；原有 nullable / 未用字段警告保留；实际窗口与游戏未在本机运行 |

测试入口：[ResourceImageTests](../../WC4MapEditor.Tests/ResourceImageTests.cs)、[JsonTableDocumentTests](../../WC4MapEditor.Tests/JsonTableDocumentTests.cs)。日志保存在 `tmp/resource-full-tests-final.log` 与 `tmp/resource-final-build.log`；临时参考哈希为 `tmp/etc1-reference-hashes.json`，均不是发布资源。

主要实现：[图集路径解析](../../WC4MapEditor.Core/Parsers/HdAtlas/HdAtlasParser.cs)、[项目图块索引](../../WC4MapEditor.Core/Assets/AtlasResourceIndex.cs)、[布局图片提供器](../../Views/Assist/LayoutImageProvider.cs)、[像素转换](../../WC4MapEditor.Rendering/Imaging/BitmapPixels.cs)、[WPF 图片桥接](../../Views/Assist/SkiaBitmapSource.cs)、[ETC1 解码](../../WC4MapEditor.Rendering/Imaging/PkmDecoder.cs)、[配置表文档](../../WC4MapEditor.Core/Assets/JsonTableDocument.cs)、[配置表界面](../../Views/JsonTableEditScene.cs)。测试直接复用布局图片提供器，包含项目散图相对子目录、保存后的刷新和导入图集相对 PKM 纹理页。

```bash
LD_LIBRARY_PATH="$PWD/tmp/skia-linux${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" \
  /home/j60100428/game/tmp/dotnet/dotnet run --project WC4MapEditor.Tests \
  --no-restore -- --corpus /home/j60100428/game --rendering

/home/j60100428/game/tmp/dotnet/dotnet build WC4MapEditor.csproj \
  -r win-x64 -p:EnableWindowsTargeting=true --no-restore -v:q
```

## 6. Windows 验收与剩余范围

1. 使用本轮代码重新发布并完整拷贝发布目录；项目设置让 `libSkiaSharp.dll` 放在 EXE 旁，单拷 EXE 不能带上它。当前恢复的依赖中 SkiaSharp 与 Win32 native 都为 3.119.2，没有发现两者版本不匹配。
2. 加载自己的 1.30.0 路径，核对张自忠等将领三种头像，随后选择无半身像的 Christian：圆形/战术头像应显示，半身像应明确提示源项目未提供。
3. 打开德国等国家图和军团归属窗口，修改一幅旗帜后重开；检查透明边缘、红蓝通道和新国家旗帜。来源图片应不变，输出只包含变化的图集/定义。
4. 布局中打开 `image/flag_1.png`、国家科技图标、generalphoto 散图，确认路径前缀及子目录；打开 `map/map1_1@2x.pkm` 与 `image/map/01.pkm`，确认只读图片出现。
5. 通过新入口修改进修费用、称号加成、传记奖励或总部科技费用；保存、重开、恢复原值。验证整数错误、数组元素类型错误、重复 Id、外部改文件及返回/关闭的保存/放弃/取消提示。

R09 因本轮 ETC1 解码而前进到“PKM 只读可预览”，**PKM 编码、美术资源写回、颜色/alpha 图集组合、完整海岸/对象/动画语义仍未完成**。R12 的奇数 capture/锚点与点击核对、R13 的 world/BTL 联动仍沿用前一审计边界。通用 JSON 字段编辑扩大配置覆盖，不能替代任意技能槽/星级/native 功能验收。
