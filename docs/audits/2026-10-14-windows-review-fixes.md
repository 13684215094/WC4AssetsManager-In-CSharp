# Windows 实测问题修复与验证矩阵（2026-10-14）

修复依据是用户在Windows上生成的 [1.30.0实测报告](2026-10-13-windows-1.30.0-review.md)和 [41条编译警告CSV](2026-10-13-build-warnings.csv)。原报告以提交 `13481cd` 为核查对象，记录145通过、2失败及41警告；两份原文件保留，不用本次结果改写历史证据。

本节点在 `6.0` 工作区继续修复，保留前一轮 [将领UI预览](2026-10-13-general-ui-preview.md)。来源为 `/home/j60100428/game/wc4/World Conqueror 4_1.30.0` 及同级APK，全部只读。修复核验阶段未提交；用户随后确认合并至前一笔提交并推送，提交规则见第8节。

## 1. 当前结论

- R01–R07、R09–R11的实现已修复并有自动回归；R08落实为可选OCR声明和安装提示，二进制与离线运行仍缺。
- 将领技能和兵种特性新增名称、实际等级、介绍、参数及定位编辑入口，数据和文本均读所选项目，不新增内置游戏美术。
- 定向回归 **19 passed、0 failed、0 skipped**；包含真实语料和Skia的完整回归 **176 passed、0 failed、0 skipped**，真实APK也参与通过。
- Windows Release / win-x64 / self-contained重新编译发布成功，**0编译错误、0警告诊断**；原CSV的41个诊断位置已处理。
- 来源 **4,580个文件的集合与SHA-256不变**；新增/修改只写独立测试输出。默认缺APK时明确skip，要求样本时缺失明确失败。
- 本机为Linux，未运行本次WPF窗口/真实键盘操作或Android游戏；交叉发布和Core/Skia验证不能代替这两类验收。

## 2. R01–R11修复矩阵

| 编号 | 修复与生产入口 | 证据 | 仍需实机核验 |
|---|---|---|---|
| R01 / P1 | [MapData](../../WC4MapEditor.Core/Models/MapData.cs)集中raw↔local转换；[ProjectMapDocument](../../WC4MapEditor.Core/Assets/ProjectMapDocument.cs)加载world宽度；相机、笔刷、对象/省区/旗帜层、列表、CLI、脚本及各修改器使用同一规则 | 310张外部地图、5,988建筑、15,617部署部队全部落入窗口，无修改字节一致；stage10001、conquest1、跨右边界stage60023修改/新增/保存/重开；对象像素与点击一致 | WPF实际鼠标选取、奇数capture视觉、游戏重载；未确认事件/opaque语义不纳入完成范围 |
| R02 / P1 | [EditModeManager](../../WC4MapEditor.Core/Modifiers/EditModeManager.cs)建筑操作前后快照包含Buildings、Provinces、Belongs；失败回滚、无变化不记录；redo恢复固定结果 | 真stage10004通过生产模式命令覆盖、apply、撤销及复制其他内容后重做；未知建筑字段与归属恢复 | Windows连续覆盖/撤销/重做及保存重开 |
| R03 / P1 | [BuildingDeployMode](../../WC4MapEditor.Core/Mode/BuildingDeployMode.cs)单建筑、仅等级、建筑组共用快照；仅成功变化发修改通知 | 等级、多选、部分越界、空剪贴板、no-op、失败不入历史；没有UndoManager时仍检测变化/异常回滚 | Windows多选快捷键及历史面板 |
| R04 / P1 | [BuildingModifier](../../WC4MapEditor.Core/Modifiers/BuildingModifier.cs)剪贴板采用Empty/Single/Levels/Group状态；成功复制替换状态，等级复制清旧组；组用HexCoord键 | 多选后复制等级再粘贴，只改变等级；失败复制不破坏前一份有效内容；redo不读新剪贴板 | 快速交替单建筑/组/等级复制 |
| R05 / P1 | 清空setting跟踪模板；[ConfigManager](../../WC4MapEditor.Core/Config/ConfigManager.cs)读取本机 `WC4_EDITOR_AI_API_KEY`；[Core工程](../../WC4MapEditor.Core/WC4MapEditor.Core.csproj)构建/发布前检查非空模板 | 模板及最终包值为空；合成非空模板触发预期失败且日志无值；环境覆盖/清除回归 | 服务端撤销旧实际密钥；历史未重写，见第6节 |
| R06 / P2 | [KeyCodes](../../WC4MapEditor.Core/Mode/IModeHandler.cs)修正D0=34…D9=43的WPF契约 | 测试以独立的WPF字面值35、36、37等匹配绑定并触发正确模式，避免同一错误常量自证 | 实际键盘1/2/3/4/5/6/8；未建立全平台键枚举转换层 |
| R07 / P2 | [首页](../../Views/BeginScene.Project.cs)复用扫描；[GameProjectSession](../../Services/GameProjectSession.cs)后台激活/索引并报告进度；[AssetCache](../../WC4MapEditor.Core/Assets/AssetCache.cs)完成后才发布索引 | 相同root普通Scan不改Revision，显式刷新找新增文件；取消激活保留原项目、Revision和字符串草稿 | Windows加载/取消响应与耗时重新测量；未给出本机耗时替代原7.3–10.4秒数据 |
| R08 / P2 | [ExternalToolRunner](../../WC4MapEditor.Core/Services/ExternalToolRunner.cs)明确为可选扩展，首页与P键提示Lib安装路径 | 实际包未携带两个工具，缺失提示与能力检查一致 | **未提供OCR二进制，未完成离线OCR**；安装后需实际运行 |
| R09 / P2 | [BuildingNameResolver](../../WC4MapEditor.Core/Services/BuildingNameResolver.cs)只接受-1或0..65535；[弹窗](../../Views/Assist/BuildingSettingWindow.cs)捕获范围错误保留输入 | -2、65536、int超范围/大整数字符串拒绝；-1、0、65535符合约定 | Windows弹窗输入/修正/保存 |
| R10 / P2 | [LayoutDocument](../../WC4MapEditor.Core/Parsers/Layout/LayoutDocument.cs)属性和文本解码实体，序列化仅转义一次 | 标准XML验证保存后的属性/文本语义；amp/lt/gt/quot/apos、十进制/十六进制实体及连续两次保存稳定 | 带实体的导入布局及页面文本实际展示 |
| R11 / P2 | [AssetRelationshipAnalyzer](../../WC4MapEditor.Core/Analyzers/AssetRelationshipAnalyzer.cs)不将Type3/SkillsMax0组合套入玩家预算；其他容量/引用仍检测 | NPC用例不报skill-max，玩家和进修超出仍报错；原1,062条误报来源未修改 | 具体游戏版本的NPC语义仍需原生核对，不把该例外泛化为任何零上限都合法 |

### 2.1 坐标修复范围

`MapNumber=0`继续使用本地坐标；外部BTL的对象记录不改写成本地索引，运行时保存world宽度作为转换上下文。局部地形/省份/归属平面用本地索引，ProvinceValue等格子引用用原始世界索引。深克隆及场景绑定保留上下文。

```text
rawIndex = (MapClipY + localRow) * worldWidth
         + (MapClipX + localCol) % worldWidth

localCol = (rawIndex % worldWidth - MapClipX + worldWidth) % worldWidth
localRow = rawIndex / worldWidth - MapClipY
localPlaneIndex = localRow * localWidth + localCol
```

stage10001的raw784为本地(11,0)，conquest1的raw348为本地(52,0)。world奇数列偏移同步用于相机、命中、笔刷、邻居、叠加层和省区生成，避免captureX为奇数时画面和选取错半格。地图对象移动后清除旧建筑坐标索引。

新增建筑/部队、陷阱、援军、空军及已建模引用使用反向转换；窄整数记录批量生成前按raw范围检查。首都切换、手绘和背景省区生成已测，省会引用不在1..65534时写入前拒绝，避免ushort截断和部分生成。该测试不代表所有原始援军/事件运行语义完成验证。

旧 [项目说明4.4节](../projects/wc4-1.30.0/README.md#44-capture坐标与关联文件)错误陈述已按日期更正并引用原实测。外部BTL地形仍需单独编辑world并重开，不提供多文档联动写回。

### 2.2 草稿与生命周期

报告5.1的核心探针继续修复为按表刷新：[RefreshEditorFor](../../WC4MapEditor.Core/Assets/GameProjectDataSession.cs)只使保存表对应的干净解析器失效，多表解析器按其完整表组定位。无关解析器和同项目字符串草稿保留；相关解析器已有草稿时也保留，让现有文件版本检查拒绝覆盖冲突。

新增联合用例：未保存技能值和文本 → 保存另一JSON → 草稿仍在 → 通用编辑器保存同一技能表 → 原草稿保存失败且未覆盖40 → 明确重载读到40 → 再保存50 → 干净解析器更新到50 → 切项目不带旧草稿。没有通过清空解析器或放宽版本检查掩盖冲突。

兵种无效列表/数值整组校验后才应用；无效输入阻止保存和切记录。重载/放弃先清旧控件状态，再恢复当前记录及列表，避免旧输入重新写回刚重载的数据。后台项目激活返回UI线程后才清WPF场景和图片缓存。

报告5.2的一次工作集采样没有泄漏结论。本节点没有据此清空所有缓存，也没有完成原生内存预算与反复操作基准；此项保留为测量工作，不能标成已修复泄漏。

资源搜索改匹配含扩展名的相对路径，`coastmask_hd.pkm`可检索；项目入口及来源/输出输入增加可访问名称。可访问标签的真实读屏/UI Automation仍需Windows复核。

## 3. 41条警告处理

本次消除警告使用契约和生命周期修复，没有逐调用点添加 `!` 或NoWarn。

| 原诊断 | 数量 | 处理 |
|---|---:|---|
| CS8604 状态/控制台消息 | 27 | [ModifierResult](../../WC4MapEditor.Core/Modifiers/IModifier.cs)的Message始终非空，`default`/无提示Ok返回空字符串 |
| CS8604 海岸landMask | 1 | [CoastMaskProcessor](../../WC4MapEditor.Rendering/Helpers/CoastMaskProcessor.cs)在完整缓存tuple成功分支且持锁渲染；不让Dispose/刷新并发释放已取出的遮罩 |
| CS8602 控制台字体 | 1 | [DebugConsole](../../WC4MapEditor.Rendering/Skia/DebugConsole.cs)检查缺失/Disposed生命周期，Dispose后清字体引用 |
| CS8602 布局几何控件 | 3 | [LayoutEditScene.Panels](../../Views/LayoutEditScene.Panels.cs)一次检查X/Y/W/H四个控件，避免只证实X |
| CS8600 JSON行遍历 | 1 | [JsonTableDocument](../../WC4MapEditor.Core/Assets/JsonTableDocument.cs)用JsonNode?交给已有可空验证，不凭前置检查假定编译器已收窄类型 |
| CS8629 建筑弹窗取Value | 1 | 建筑模式新建/已有路径用完整快照与明确对象构造，移除可能可空的Value访问 |
| CS0414 未用字段 | 5 | 移除四个渲染间距常量；建筑剪贴板重构为明确状态，移除重复布尔标志 |
| CS0219 未用局部变量 | 1 | 移除SkiaRenderEngine的baseHexSize |
| CS0169 未用字段 | 1 | 移除ConfirmDialog._dialogPanel |
| 合计 | **41** | 干净Release自包含发布0警告/0错误；原CSV内容保留 |

## 4. 技能与兵种特性可读说明

实现为 [ProjectDataDescriptions](../../WC4MapEditor.Core/Models/ProjectDataDescriptions.cs)与WPF共用 [ReferenceDetailsPanel](../../Views/Assist/ReferenceDetailsPanel.cs)，分别接入将领Skills、兵种Feature/FeatureLevel下方。操作见 [资源编辑第10节](../projects/wc4-1.30.0/resource-editing.md#10-2026-10-14-技能特性说明与实测修复)。

| 功能 | 数据规则 | 已核验 |
|---|---|---|
| 将领技能名称/等级 | Skills→SkillSettings.Id→Type/Level；文本按skill_name/skill_info的Type键 | 实际钢铁洪流Type24；合成Lv6及草稿效果改变后描述同步 |
| 三种语言 | 默认cn，tw使用技能文本草稿，en读项目语言；不自动跨语言保存 | 缺名称/介绍明确提示；未知Id不替换；Id0为空槽 |
| 技能效果文本 | 已确定单参数SkillEffect/ActivatesChance、%%及颜色片段 | 概率家族回归继续通过；多参数未知部分保留占位符并警告 |
| 特性名称/等级 | Feature是Type，FeatureLevel是对应等级，查询ArmyFeatureSettings | Type1的Lv1/Lv2效果60/75；缺等级、重复Type/Level、数组数量差异有提示 |
| 特性介绍 | 优先feat_desc_Type_LvLevel，再通用文本；{0}=chance、{1}=effect、{2}=second | Type13/Lv1触发率50，Type47/Lv3两效果参数绑定 |
| FeatureLevel原本为空 | 使用该Type最低配置等级，明确标为参考 | 不补造JSON等级，不断言实际运行Lv1；提示科技/运行状态影响 |
| 定位编辑 | 技能按钮选Id，特性按钮打开并选ArmyFeatureSettings.Id | 编译及Core数据解析通过；WPF导航/保存确认需实机验收 |

等级ID、槽位和特性Type分别处理。新增技能Lv6可显示和保存，不能由预览认定Android战斗、升级费用、解锁或存档支持已经完成。

## 5. 测试入口与证据

### 5.1 原Windows两项失败

| 原问题 | 当前处理 | 验证 |
|---|---|---|
| `tmp/tests`混合分隔符导致路径相等断言失败 | fixture使用 `"tmp", "tests"` 独立路径段；双方GetFullPath，并按平台区分大小写 | 原APK提取缓存/输出分离、损坏修复、原包指纹、继续项目等行为断言仍保留且通过 |
| 默认真实APK样本不存在却计为产品失败 | runner增加 `--apk`、`--require-apk`，区分pass/fail/skip并列原因 | 本机真实APK通过；模拟默认缺样本exit0/1skip；模拟require缺样本exit1/1fail |
| 参数缺少值 | --filter/--corpus/--apk检查后明确提示并exit2 | 不再因args越界给误导堆栈 |

默认样本可选不表示跳过即可宣布真实APK验证通过。CI若要求端到端APK应使用 `--require-apk`，或显式传入 `--apk`；显式路径不存在同样失败。

### 5.2 本次结果

| 验证 | 结果 | 本机证据 |
|---|---|---|
| 测试工程build | 0错误/0警告 | `tmp/windows-review-tests-build.log` |
| Windows报告定向操作回归 | **19通过/0失败/0跳过** | `tmp/windows-review-targeted.log`；[WindowsReviewTests](../../WC4MapEditor.Tests/WindowsReviewTests.cs) |
| 完整Core/Skia/真实语料回归 | **176通过/0失败/0跳过** | `tmp/windows-review-regression.log` |
| 全部外部地图对象 | 310张、5,988建筑、15,617部署部队正确定位，原字节一致 | 同一回归的计数断言，不只验证能打开 |
| 原有语料/预览 | 5,523 BTL、81表/22,055行、1,677图片/417PKM、94图集/6,675矩形、190变体、6张将领UI及真实APK通过 | 同一完整回归；独立ETC1参考在本机可用，不能回写为原Windows报告当时可用 |
| APK默认缺样本 | exit0，0通过/0失败/1跳过，有具体原因 | `tmp/windows-review-apk-optional.log` |
| APK明确要求但缺失 | **预期exit1**，0通过/1失败/0跳过 | `tmp/windows-review-apk-required.log`，不是产品回归失败 |
| APK显式路径缺失 | **预期exit1**，0通过/1失败/0跳过 | `tmp/windows-review-apk-explicit.log`，不会静默跳过用户指定样本 |
| --apk缺少值 | **预期exit2**，明确参数提示 | `tmp/windows-review-args.log` |
| 非空密钥模板门禁 | **预期exit1**，安全错误且无值 | `tmp/windows-review-credential-gate.log`，合成输入无真实密钥 |
| 最终Windows Release自包含发布 | 0错误/0警告，13个运行文件（不含5个PDB） | `tmp/windows-review-publish.log`；`tmp/publish/windows-review-20261014/` |
| 解包来源指纹 | 4,580文件，0新增/0删除/0内容改变 | `tmp/windows-review-source-integrity.json`，对照先前只读基准 |

日志和发布目录位于被忽略的tmp，本篇记录结果和复核方式；不将原游戏资源或日志自动纳入版本控制。

### 5.3 复核命令

本机已有便携SDK和Skia，无需下载依赖或开启代理：

```bash
/home/j60100428/game/tmp/dotnet/dotnet build \
  WC4MapEditor.Tests/WC4MapEditor.Tests.csproj --no-restore -v:minimal

LD_LIBRARY_PATH="$PWD/tmp/skia-linux" \
  /home/j60100428/game/tmp/dotnet/dotnet run \
  --project WC4MapEditor.Tests --no-build -- \
  --rendering --corpus /home/j60100428/game

/home/j60100428/game/tmp/dotnet/dotnet publish WC4MapEditor.csproj \
  -c Release -r win-x64 --self-contained true --no-restore \
  -p:EnableWindowsTargeting=true -t:Rebuild,Publish \
  -o tmp/publish/windows-review-20261014 -v:minimal
```

Windows复核可使用本机安装的SDK或原报告的便携SDK，在仓根运行：

```powershell
dotnet build WC4MapEditor.Tests/WC4MapEditor.Tests.csproj
dotnet run --project WC4MapEditor.Tests --no-build -- `
  --rendering --corpus 'G:\document\githup\game' `
  --apk 'G:\document\githup\game\wc4\World Conqueror 4_1.30.0.apk' --require-apk
dotnet publish WC4MapEditor.csproj -c Release -r win-x64 --self-contained true `
  -t:Rebuild,Publish -o tmp/publish/windows-review-20261014
```

无原APK时，不传--apk/--require-apk会报告真实APK用例skip；不要改fixture或删行为断言把失败隐藏。

## 6. 发布与凭据

完整复制 `tmp/publish/windows-review-20261014/` 到Windows运行WC4MapEditor.exe。该包自包含.NET 10 Desktop运行库；同目录的Skia/WPF原生DLL、setting和五份EditorResources预设需要保留。PDB可单独存档，不是运行必需文件。没有Resource旧游戏数据、截图或项目美术随包复制。

本机复测ZIP为 `tmp/packages/WC4MapEditor-win-x64-20261014-windows-review.zip`，包含13个运行文件，66,263,086字节，移除PDB。ZIP完整性与内部空密钥模板已核验，SHA-256为 `ea6381fe733d25b07f13e3c9e1c9827d0d3dc34ed67bd1c78942409ca80df3d5`。压缩包只保留在tmp，不纳入提交。

跟踪模板AIApiKey为空，运行时密钥只读本机 `WC4_EDITOR_AI_API_KEY`。构建目标在非空模板时中止，不输出值；不应通过修改门禁来把密钥打入包。**原历史包含的实际密钥仍需在服务端撤销/更换，清空当前模板不能消除历史暴露。** 本次没有验证密钥有效性、展示原值或重写历史。

OCR工具应安装在EXE旁Lib目录，名称为ocr_map.exe和gen_buildings_zme.exe；当前仓和本包缺二进制，首页与建筑P键展示原因。没有受控安装来源/运行证据，因此该能力维持未完成离线验证。

## 7. 来源异常与剩余验收

| 来源观察 | 本节点处理 |
|---|---|
| 豹2 Id349001..349004的Feature4项/FeatureLevel5项 | 继续保留，特性卡片提示数量差异，关系审计仍报告 |
| 总部305062→不存在的305063 | 保留源值与预览诊断，不补造升级节点 |
| 原项目无半身像 | 按用户要求保持原状，使用当前角色已有头像；不补生成图、不计为解码修复 |
| Type3/SkillsMax0且有技能的1,062条记录 | 只修检查规则适用性，不清空技能或改源数据 |
| APK/外部BTL支持边界 | APK仍是提取/增量编辑；不重建签名；world联动、全部事件/援军/native/存档未全部建模 |

优先Windows验收：

| 操作 | 预期 |
|---|---|
| 加载目录或APK → 返回首页数次 | 一次完整索引，返回首页不出现新的强制扫描；取消另一个项目加载后保留原会话 |
| stage10001、conquest1、stage60023 → 建筑/部队列表与点击 | 对象在正确窗口格；奇数capture画面与鼠标一致；改动/新增/保存/重开不移位 |
| stage10004 → 覆盖/仅等级/多选粘贴 → undo/redo | 原建筑、归属、省区完整恢复；更换剪贴板不影响redo；空粘贴不增加历史/dirty |
| 按1、2、3、4、5、6、8 | 分别触发页面支持的地形/省份/建筑/单位/归属/军团/援军模式，外部图仍隐藏地形编辑 |
| 将领Skills → 修改Id → 编辑此技能 | 名称、Lv、介绍和参数立即更新，导航定位正确；未保存确认正常 |
| 兵种Feature/FeatureLevel → 修正非法输入 → 编辑特性 | 对应Type+Level解释正确，无级别明确参考；错误输入不部分保存 |
| 同项目字符串/技能草稿 → 通用表保存/资源刷新 | 草稿保留；同表冲突拒绝覆盖且保留双方，显式重载后再编辑 |
| 带实体的布局导入 → 修改宽度 → 保存两次 | 显示及标准XML读取的文本不二次转义 |
| 窗口/地图/预览反复进入退出 | 无旧项目图片或旧命令上下文；测量受管堆/原生位图与Dispose预算，不能只看一次工作集 |
| 安装可选OCR工具后P键生成 | 单独记录工具版本、模型、离线结果；当前包不能先行宣布通过 |

Android需复核增量资源合入后的地图对象位置、省会/归属、技能Lv6升级/战斗/存档及原始异常数据的实际表现。当前Linux结果证实编码、编辑操作和静态渲染路径，未完成上述实机操作。

## 8. 合并提交与远端同步（2026-10-14）

按用户明确要求，第19–20节点的工作区代码、测试和中文资料合并进上一笔 `13481cd9ad241e3d8cdfcd3786863cfd4979e293`，父提交保持 `a498238477b644a64bb07ff4e672ead48f0bf296`，不追加第二笔修改提交。原始Windows报告和CSV原样纳入，不因新提交替换其历史核查对象。

远端目标为 `origin/6.0`。推送前已fetch确认远端仍是原 `13481cd`，使用指定旧SHA的 `--force-with-lease` 保护远端后续提交；代理按本机说明仅在fetch/push核验期间启动，结束关闭。最终提交SHA与同步结果以 `git log -1`、`git status -sb` 和远端分支SHA查询为准。发布ZIP、运行日志及游戏资源仍留在忽略目录，不纳入提交。
