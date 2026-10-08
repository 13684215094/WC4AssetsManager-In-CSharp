# 2026-10-12：按目标项目去重，恢复本仓独有资源

用户明确了资源清理口径：以 `/home/j60100428/game/wc4/World Conqueror 4_1.30.0` 为基准，项目有对应资源才清理本仓重复项；本仓独有文件必须保留。前一份 [资源清理审计](2026-10-12-resource-cleanup.md)将无调用、缺配套图片或格式异常也作为删除理由，范围过大。本节点恢复16个独有文件，保留已有代码修复，当前清理结论以本篇为准。

## 1. 远端来源与分支隔离

| 对象 | 核验结果 |
|---|---|
| 工作分支 | `6.0`，本节点开始时为 `b22b18e`，工作区干净 |
| 拉取来源 | `origin` 的 `6.0`，本次 fetch 后仍为 `8fd01cc6e8e08d649d8847e205e02708baf88c36` |
| 独立恢复分支 | `recovery/remote-resources-20261012`，指向 `origin/6.0` |
| 独立工作区 | `/home/j60100428/WC4AssetsManager-remote-resources-20261012`，用于读取远端原文件；保留供复核 |
| 当前分支的处理 | 只恢复选出的16份资源并更新忽略规则与中文资料，没有合并远端代码；源码、工程、XAML的SHA-256与本节点开始时相同 |
| 代理 | 按 `/home/j60100428/README.md` 手动开启，仅用于fetch；命令退出后已关闭，最终状态为 stopped |

独立工作区里保存远端原状，当前工作分支没有切换到它，也没有被远端检出覆盖。前一节点的一笔提交约定延续，本节点更正合并进前一笔本地提交，本次未推送。

## 2. 去重口径与最终数量

对远端 `Resource/`、`source/`、`dist/` 共 **258个资源文件**进行完整核对，检查对应路径、文件名、SHA-256及必要的XML语义。完整机器可读结果为 [逐文件CSV](2026-10-12-resource-deduplication.csv)，列出远端路径、保留/删除决定、当前路径、项目对应文件、差异类别、字节数与两侧SHA-256。

| 类别 | 数量 | 字节数 | 处理 |
|---|---:|---:|---|
| 已有编辑器预设 | 5 | 11,107 | 继续保留于 `EditorResources/Config`，不恢复第二套重复路径 |
| 本仓独有资源 | **16** | **1,201,341** | 从独立远端工作区恢复到原相对路径，逐字节一致；约1.15 MiB |
| 项目已有对应资源 | **237** | **16,334,606** | 维持删除；运行时读取用户所选项目，约15.58 MiB |
| **总计** | **258** | **17,547,054** | 16独有项+5预设保留，237项目重复项清理 |

前一节点“删除253个/约16.7 MiB”描述当时动作，其中16份独有文件本次已恢复。**最终清理237份/约15.58 MiB**；不以“没有调用、暂时缺配套美术、JSON格式异常”作为删掉独有文件的理由。代码文件 `TextureDiskCache` 的删除不属于资源去重数量。

这里“重复”是目标项目已经有同一资源文件，不等于每份字节相同：237份中3份字节完全相同，112份只差换行符，122份还有内容或版本差异。目标项目是当前开发基准，因此对应游戏表、语言和图集由项目版本提供；旧版本在独立恢复分支保留供对照，不导入当前游戏项目。独有文件既无对应路径，也没有在目标项目其他路径发现相同SHA-256。

两份旧 `Resource/Texture/MapCoast/coast_hd.xml`、`Resource/Texture/MapCoastGrayLevel/coastmask_hd.xml` 对应项目 `assets/coast_hd.xml`、`assets/coastmask_hd.xml`，各90个图块的名称及坐标/尺寸/锚点全部一致，文件只差换行，因此维持清理。不能仅凭所在目录不同就认定独有。

`Resource/WC4DATA`中的两个例外是 `assets/json/RedeemCodes.json` 和 `assets/plant.xml`，目标项目确实没有同名对应文件；即使项目另有 `plant_hd`，也保留这份普通图集定义。

## 3. 恢复的全部16个文件

文件都恢复到原路径，未格式化、重编码、修复内容或增加配套资源。

| 路径 | 字节数 | 说明 |
|---|---:|---|
| [Resource/Geo/gwangju_entities.json](../../Resource/Geo/gwangju_entities.json) | 32,029 | 地点实体参考，119条记录 |
| [Resource/Geo/jiuzhou.txt](../../Resource/Geo/jiuzhou.txt) | 9,539 | 原地理参考文本 |
| [Resource/Geo/jiuzhou_entities.json](../../Resource/Geo/jiuzhou_entities.json) | 46,101 | 地点实体参考，292条记录 |
| [Resource/Geo/shanghai_entities.json](../../Resource/Geo/shanghai_entities.json) | 45,641 | 地点实体参考，251条记录 |
| [Resource/Geo/中东.json](../../Resource/Geo/中东.json) | 627,530 | 地点实体参考，1,359条记录 |
| [Resource/Music/WindowMusic.json](../../Resource/Music/WindowMusic.json) | 958 | 历史音乐列表，保留配置；没有配套MP3 |
| [Resource/Texture/MapCoast/CoastManager.json](../../Resource/Texture/MapCoast/CoastManager.json) | 5,813 | 编辑器海岸映射参考，不恢复重复的游戏海岸XML |
| [Resource/Texture/MapCoastGrayLevel/pm_map.xml](../../Resource/Texture/MapCoastGrayLevel/pm_map.xml) | 3,389 | 精灵锚点参考，XML可解析 |
| [Resource/Texture/MapTerrian/manager.json](../../Resource/Texture/MapTerrian/manager.json) | 12,317 | 编辑器地形映射参考，保留原拼写 |
| [Resource/Texture/StageMark/InformationMark/pm_buildmark.xml](../../Resource/Texture/StageMark/InformationMark/pm_buildmark.xml) | 3,768 | 标记锚点参考，XML可解析 |
| [Resource/Texture/ZSEditor_BackGround/BackGround.json](../../Resource/Texture/ZSEditor_BackGround/BackGround.json) | 680 | 编辑器背景配置参考，没有配套背景图片 |
| [Resource/WC4DATA/assets/json/RedeemCodes.json](../../Resource/WC4DATA/assets/json/RedeemCodes.json) | 1,016 | 目标项目没有对应文件的参考配置 |
| [Resource/WC4DATA/assets/plant.xml](../../Resource/WC4DATA/assets/plant.xml) | 11,721 | 目标项目没有对应文件的普通植物图集定义 |
| [dist/map_places.json](../../dist/map_places.json) | 369,534 | 地点识别结果参考，1,339条记录 |
| [source/Json编辑/core/_mappings_ref.json](../../source/Json编辑/core/_mappings_ref.json) | 14,286 | JSON字段映射参考 |
| [source/docs/btl三次整理(1).json](<../../source/docs/btl三次整理(1).json>) | 17,019 | 带注释等非标准语法的BTL整理草稿，保持原文 |

[Resource目录说明](../../Resource/README.md)解释保存用途。原文件含有的乱码、非标准JSON或缺配套图片/音乐原样保留，表示独有资料仍可查阅，不表示它们已经成为可运行游戏资源或新编辑功能。

## 4. 忽略规则、加载与发布

- `.gitignore` 对上述16份文件及目录说明显式放行，继续忽略已去重的游戏数据和本机缓存，避免恢复文件被静默漏提交。
- 五份编辑器预设继续从 `EditorResources/Config` 读取并复制到build/publish，`setting.txt`保持不变。
- 保留资料不恢复隐式游戏扫描。游戏表、图集、本地化仍从手动指定的目录或APK读取，不因仓库出现 `Resource` 就自行加载。
- 没有把 `Resource/**`、`source/**` 或 `dist/**` 的整目录复制规则加回工程。独有参考文件留在源码仓库，新发布目录不包含这些参考文件，也不包含重复游戏资源。

## 5. 本节点验证

| 验证 | 结果 |
|---|---|
| 资源恢复保真 | 16份恢复文件SHA-256与远端 `8fd01cc` 完全一致，文件总字节数1,201,341 |
| 去重覆盖 | 237份项目已有对应资源在当前工作区均不存在；CSV覆盖全部258份远端资源 |
| 差异检查 | 新文档与忽略规则通过 `git diff --check`；相对远端资源版本的整体差异也通过。恢复文件原有11处空白诊断随原内容保留，没有为了格式检查改写原始资源 |
| 代码保护 | 本节点开始时的全部C#、工程、XAML、props/targets哈希未变化 |
| 加载与渲染回归 | `--rendering` **133 passed，0 failed**；验证独有参考文件存在时未加载仍不扫描、CLI来源必填及跨项目图片/草稿缓存。日志 `tmp/resource-restore-regressions.log` |
| 前一节点真实语料 | 已记录完整147项回归通过，本节点只恢复资源与文档，没有改动codec/图片/保存代码；不把133项误写成再次执行全部语料147项 |
| Windows发布 | 新目录 `tmp/publish-resource-retained-20261012` 的Debug `win-x64`发布成功，0错误、5条警告诊断行；这是本次增量发布结果，不能当作全工程警告总数。输出仍只有程序、Skia、调试符号、五预设和setting，没有 `Resource/source/dist` 参考文件。日志 `tmp/resource-restore-publish.log` |
| 来源保护 | 原目录4,580文件的集合及SHA-256不变，原APK SHA-256仍为 `14b8de3a5a2578083bdb7a6cadcec44211299f022636df947a5f8ee045118709` |
| Windows/Android实机 | 本机Linux，本节点没有实际运行WPF或原游戏；支持边界继续见前一审计 |

旧版本说明与第17节统计保留为历史节点，当前资源保留规则见README第18节。独立恢复分支保留远端原状，当前修正继续合并到前一笔本地提交，本次未推送。
