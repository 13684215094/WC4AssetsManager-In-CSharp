# 1.30.0 资源索引与核对记录

核对日期：2026-10-08。对象为 `/home/j60100428/game/wc4/World Conqueror 4_1.30.0`，本文从实际文件只读统计，记录目录规模、全部 JSON 配置表、config XML、主要图集和关键文件 SHA-256。

先读 [项目说明](README.md)了解文件关系、地图格式和编辑流程；工具当前是否能编辑这些资源见 [审计矩阵](../../audits/2026-10-08-project-matrix.md)。本索引不是全项目逐文件哈希清单，也不表示列出的每个文件都有专用编辑器。

## 1. 统计口径

- 文件数统计递归普通文件；不把目录算作文件，字节数不包含文件系统分配/目录开销。
- MiB 使用 `1 MiB = 1,048,576 字节`，与十进制 MB 区分。
- 表中顶层目录包含其所有后代文件；“根目录文件”只统计当前根下的直接文件，不与子目录重复。
- 除非列明 assets 相对路径，所有路径均相对解包项目根；文件名和 XML 标签区分大小写。
- 本次没有发现源目录中的符号链接；若换项目后存在链接，编辑器项目复制会拒绝。
- 统计与指纹只描述此份本地样本；另一份同名 1.30.0、渠道包或 mod 应重新核对。

## 2. 项目顶层目录

| 范围 | 文件数 | 字节数 |
| --- | --- | --- |
| `(根目录文件)` | 4 | 16,471,440 |
| `META-INF/` | 3 | 939,792 |
| `assets/` | 3,474 | 241,964,843 |
| `com/` | 4 | 111,030 |
| `kotlin/` | 7 | 28,940 |
| `lib/` | 1 | 11,015,960 |
| `okhttp3/` | 2 | 37,948 |
| `res/` | 1,085 | 8,277,792 |
| **合计** | **4,580** | **278,847,745** |

根目录的四个文件为 `AndroidManifest.xml`、`classes.dex`、`classes2.dex`、`resources.arsc`。`lib/` 只有 `arm64-v8a/libworld-conqueror-4.so`。`res/` 有 840 XML、218 PNG、20 WebP、6 JPG 和 1 SRT；其中 XML 多为 Android 二进制编译资源，与 `assets/config` 文本 XML 的处理方式不同。

## 3. assets 一级目录

| 范围 | 文件数 | 字节数 |
| --- | --- | --- |
| `(根目录文件)` | 208 | 62,017,523 |
| `Base.lproj/` | 2 | 867 |
| `audio/` | 63 | 17,657,220 |
| `config/` | 20 | 443,459 |
| `de.lproj/` | 2 | 939 |
| `dexopt/` | 2 | 2,163 |
| `effect/` | 8 | 15,338 |
| `en.lproj/` | 2 | 867 |
| `es.lproj/` | 2 | 949 |
| `font/` | 65 | 3,930,913 |
| `google/` | 7 | 49,117 |
| `image/` | 1,196 | 64,982,019 |
| `ja.lproj/` | 2 | 959 |
| `json/` | 81 | 4,694,645 |
| `ko.lproj/` | 2 | 890 |
| `map/` | 384 | 50,337,792 |
| `recommend/` | 19 | 1,058,670 |
| `shader/` | 16 | 6,250 |
| `stage/` | 1,383 | 36,735,374 |
| `tutorials/` | 6 | 27,165 |
| `zh_CN.lproj/` | 2 | 859 |
| `zh_TW.lproj/` | 2 | 865 |
| **合计** | **3,474** | **241,964,843** |

八个 `.lproj` 目录合计 16 文件，不能遗漏 Base 或将它们合并成七个目录。assets 根下 208 文件包括 world、图集、单位动画、布局、字符串表及伴随 BIN。

### 3.1 后缀总表

| 后缀 | 数量 |
| --- | --- |
| `.bin` | 51 |
| `.btl` | 1,383 |
| `.fnt` | 29 |
| `.fsh` | 8 |
| `.ini` | 7 |
| `.js` | 1 |
| `.json` | 84 |
| `.mp3` | 2 |
| `.otf` | 7 |
| `.pkm` | 417 |
| `.png` | 360 |
| `.prof` | 1 |
| `.profm` | 1 |
| `.shp` | 2 |
| `.strings` | 8 |
| `.vsh` | 8 |
| `.wav` | 61 |
| `.webp` | 900 |
| `.xml` | 144 |
| **合计** | **3,474** |

这里只做后缀统计，不用后缀判断二进制格式。51 份 BIN 中 `world.bin` 是 YSAE/v4，`map1.bin` 和 `map1_hd.bin` 又是另一布局；`maptext.bin`、单位和动画等伴随 BIN 使用 BILE 头部。417 份 PKM 实际均为 PKM 1.0 / ETC1 RGB，不能按 PNG 解码。

## 4. 全部 81 张 JSON 配置表

路径均相对 `assets/json/`，按文件名排序。记录数为 JSON 顶层数组长度，未按 Id 去重；本样本各行均为对象。下列“索引字段”只列原文件的 Id 与常见区分字段，不是全部属性，也不是外键校验规则。

内容分类根据文件名和静态字段归纳，具体解锁、事件、奖励和数值公式仍以游戏逻辑为准。特别是军团、将领、物品等表里的 Army/Level，不能只因字段同名就假定含义和引用目标一致。

| 文件 | 记录数 | 索引字段 | 内容分类 |
| --- | --- | --- | --- |
| `AchievementSettings.json` | 196 | `Id` | 成就 |
| `AirDefenceSettings.json` | 12 | `Id` / `Level` | 防空 |
| `ArmyBuffSettings.json` | 172 | `Id` / `Level` | 兵种增益 |
| `ArmyFeatureSettings.json` | 461 | `Id` / `Level` | 兵种特性 |
| `ArmyGroupCardSettings.json` | 21 | `Id` | 集团军卡片 |
| `ArmyGroupChallengeSettings.json` | 122 | `Id` / `Level` | 集团军挑战 |
| `ArmyGroupEventSettings.json` | 310 | `Id` / `StageId` | 集团军事件 |
| `ArmyGroupPrizeSettings.json` | 108 | `Id` | 集团军奖励 |
| `ArmyGroupReinforcementSettings.json` | 1,648 | `Id` | 集团军援军 |
| `ArmyGroupSettings.json` | 278 | `Id` / `StageId` | 集团军 |
| `ArmyLevelSettings.json` | 7 | `Level`（无 Id） | 兵种等级 |
| `ArmyNumberSettings.json` | 4 | `Id` | 兵种数量 |
| `ArmySettings.json` | 736 | `Id` / `Army` | 兵种 |
| `ArmySubTypeSettings.json` | 39 | `Id` / `Army` | 兵种子类型 |
| `BuildingSettings.json` | 18 | `Id` | 建筑 |
| `CityFeatureSettings.json` | 16 | `Id` | 城市特性 |
| `CitySettings.json` | 151 | `Id` / `StageId` | 城市 |
| `ConquerChallengeSettings.json` | 161 | `Id` / `Level` | 征服挑战 |
| `ConquerCountrySettings.json` | 356 | `Id` | 征服国家席位 |
| `ConquerEventSettings.json` | 159 | `Id` | 征服事件 |
| `ConquerPassSettings.json` | 71 | `Id` | 征服进度奖励 |
| `ConquerPrizeSettings.json` | 6 | `Id` | 征服奖励 |
| `ConquerScoreSettings.json` | 3 | `Id` | 征服评分 |
| `ConquerSettings.json` | 10 | `Id` | 征服模式 |
| `CorpsSettings.json` | 51 | `Lv`（无 Id） | 军团等级 |
| `CountrySettings.json` | 60 | `Id` | 国家 |
| `CountryTechSettings.json` | 375 | `Id` | 国家科技 |
| `EliteArmySettings.json` | 552 | `Id` | 精英兵种 |
| `EliteBoxSettings.json` | 130 | `Id` | 精英奖励箱 |
| `EliteCalendarSettings.json` | 31 | `Id` | 精英日历 |
| `EliteChallengeSettings.json` | 48 | `Id` / `Level` | 精英挑战 |
| `ElitePassSettings.json` | 1,860 | `Id` / `Level` | 精英进度奖励 |
| `EliteSkinSettings.json` | 8 | `Id` / `Army` | 精英外观 |
| `EventBuffSettings.json` | 72 | `Id` | 活动增益 |
| `EventCalendarSettings.json` | 861 | `Id` | 活动日历 |
| `EventPrizeSettings.json` | 395 | `Id` | 活动奖励 |
| `EventSettings.json` | 188 | `Id` | 活动 |
| `EventStageSettings.json` | 731 | `Id` | 活动关卡 |
| `FacilitySettings.json` | 33 | `Id` / `Level` | 设施 |
| `FeaturedPackSettings.json` | 4 | `Id` | 组合礼包 |
| `FrontierBuffSetting.json` | 15 | `Id` | 前线增益 |
| `FrontierChapterSetting.json` | 10 | `Id` | 前线章节 |
| `FrontierNodeSetting.json` | 34 | `Id` | 前线节点 |
| `FrontierReinforcementSetting.json` | 4,914 | `Id` / `Army` / `StageId` | 前线援军 |
| `FrontierStageSetting.json` | 213 | `Id` | 前线关卡 |
| `GeneralLevelSettings.json` | 11 | `Id` / `Level` | 将领等级 |
| `GeneralMedalSettings.json` | 160 | `Id` / `Level` | 将领勋章 |
| `GeneralNameSettings.json` | 155 | `Id` | 将领名称 |
| `GeneralPhotoSettings.json` | 11 | `Id` | 将领图片配置 |
| `GeneralPromotionSettings.json` | 43 | `Id` | 将领晋升 |
| `GeneralQualitySettings.json` | 4 | `Id` | 将领品质 |
| `GeneralSettings.json` | 1,189 | `Id` | 将领 |
| `GeneralStageSettings.json` | 40 | `Id` | 将领关卡 |
| `GeneralTitleSettings.json` | 8 | `Id` | 将领称号 |
| `GloryChallengeSettings.json` | 61 | `Id` | 荣耀挑战 |
| `GlorySettings.json` | 16 | `Id` | 荣耀 |
| `InvasionGeneralSettings.json` | 2,755 | `Id` | 入侵将领 |
| `InvasionSettings.json` | 10 | `Id` | 入侵 |
| `ItemSettings.json` | 56 | `Id` / `Army` | 物品 |
| `LegendArmySettings.json` | 10 | `Id` / `Army` | 传奇兵种 |
| `LegendChapterSettings.json` | 10 | `Id` | 传奇章节 |
| `LegendStageSettings.json` | 60 | `Id` | 传奇关卡 |
| `LegendTacticsSettings.json` | 45 | `Id` | 传奇战术 |
| `LegionSettings.json` | 19 | `Id` / `Army` | 军团 |
| `LoginRewardSettings.json` | 14 | `Id` | 登录奖励 |
| `MuseumSetting.json` | 3 | `Id` | 博物馆 |
| `PassportPrizeSetting.json` | 31 | `Id` | 通行证奖励 |
| `PaySettings.json` | 159 | `Id` | 付费条目 |
| `QuestRewardSettings.json` | 4 | `Id` | 任务奖励 |
| `QuestSettings.json` | 10 | `Id` | 任务 |
| `ScenarioSettings.json` | 6 | `Id` | 战役组织 |
| `SkillSettings.json` | 901 | `Id` / `Level` | 技能 |
| `StageSettings.json` | 295 | `Id` | 战役关卡 |
| `StarterChallengeSettings.json` | 18 | `Id` | 新手挑战 |
| `StarterPassSettings.json` | 18 | `Id` | 新手进度奖励 |
| `TechResearchSettings.json` | 11 | `Level`（无 Id） | 科技研究 |
| `TechnologySettings.json` | 191 | `Id` / `Level` | 科技 |
| `TradeTaskSettings.json` | 20 | `Id` | 交易任务 |
| `WarZoneSetting.json` | 5 | `Id` | 战区 |
| `WarZoneStageSetting.json` | 25 | `Id` | 战区关卡 |
| `WonderSettings.json` | 31 | `Id` | 奇观 |
| **81 张表合计** | **22,055** |  |  |

### 4.1 表结构例外与工具保存范围

- ArmyLevelSettings、CorpsSettings、TechResearchSettings 原文件没有 Id。保存时保留原行身份，不能人工制造统一 Id 或按缺失 Id 合并。
- ArmySettings 的 736 行包含 103 个不同的 Army 代码；配置 Id 与 BTL 部队类型不是同一个值。
- ArmyGroupEventSettings 的 310 行都有 StageId；专用面板未展示这个字段时，保存也应保留。
- Stage/EventStage/FrontierStage/GeneralStage/LegendStage/WarZoneStage 六族的配置 Id 与本样本 BTL 文件后缀集合一致；Conquer 与 Invasion 的文件命名映射有例外，见 [项目说明第 7 节](README.md#7-json-配置及常见引用)。
- 当前真实保存回归覆盖 16 张专用可写表，另对 ConquerSettings 作只读对照、对 GeneralSettings 作独立验证；81 张表均可浏览不等于 81 张都有完整专用 UI 或跨表迁移。

### 4.2 assets 根目录的三个 JSON

`assets/FPTIDictionary.json`、`assets/countries.json`、`assets/country_fields.json` 顶层均为对象。它们不计入 81 张数组表及 22,055 行，计入 assets 的 84 个 JSON 文件。根目录对象型 JSON 不能直接套用数组表解析器；本资料仅记录文件形态，不展开支付等组件配置内容。

## 5. 全部 20 份 config XML

路径相对 `assets/config/`。数量为根节点的**直接子节点**，不统计递归子节点总数。例如 def_mapterrain 的 24 个 terrain 内还有 193 个 tile，其中 190 个有 image 属性。根名及子节点大小写按原文件保留。

| 文件 | 根节点 | 直接子节点 | 数量 | 内容 |
| --- | --- | --- | --- | --- |
| `ShareAPPCampaign.xml` | `ShareAppCampaign` | `campaign` | 61 | 分享战役信息，含关卡、国家、对手、年份属性 |
| `def_armypos.xml` | `units` | `unit` | 23 | 兵种显示位置/缩放 |
| `def_array.xml` | `Arrays` | `Array` | 6 | 单位阵形与内部 Pos 坐标 |
| `def_capital.xml` | `capitals` | `capital` | 21 | 首都定义 |
| `def_citycoord.xml` | `Citys` | `City` | 179 | 城市坐标 |
| `def_dialogues.xml` | `dialogues` | `dialogue` | 2,361 | 对话定义 |
| `def_effectsanim.xml` | `EffectsAnimations` | `EffectsAnimation` | 107 | 效果动画 |
| `def_elementanim.xml` | `Animations` | `Animation` | 21 | 元素动画 |
| `def_frontierarmy.xml` | `frontier` | `stage` | 213 | 前线关卡军队定义 |
| `def_help.xml` | `help` | `catalog` | 6 | 帮助目录 |
| `def_map.xml` | `maps` | `map` | 1 | 世界底图及伴随资源选择 |
| `def_mapterrain.xml` | `terrains` | `terrain` | 24 | 地形组、类型及图片变体 |
| `def_motion.xml` | `Units` | `Unit` | 287 | 单位动作/动画 |
| `def_portraitpos.xml` | `Portraits` | `general` | 179 | 将领图片位置 |
| `def_strategy.xml` | `strategies` | `strategy` | 267 | 策略定义 |
| `def_strength.xml` | `Defs` | `Army` | 10 | 各兵种 Strength 分档属性 |
| `def_tacticalmap.xml` | `TacticalMaps` | `TacticalMap` | 65 | 战术底图配置 |
| `def_terraintype.xml` | `terraintypes` | `terraintype` | 11 | 地形移动成本与惩罚 |
| `def_tutorials.xml` | `tutorials` | `tutorial` | 6 | 教程配置 |
| `def_warzone_stage.xml` | `warzone` | `stage` | 25 | 战区关卡定义 |

本文没有把“可解析文本 XML”解释为“所有游戏语义均已实现”。专用兵种/将领保存只修改对应位置定义；其他 XML 和 UI 布局的完整保真、原游戏显示及功能要单独验收。

## 6. image 目录与主要图集

### 6.1 image 一级目录

路径相对 `assets/image/`，子目录数包含其后代文件。根目录 99 文件包括若干图集 XML 与图片。

| 范围 | 文件数 | 字节数 |
| --- | --- | --- |
| `(根目录文件)` | 99 | 12,083,837 |
| `bg/` | 12 | 732,238 |
| `card/` | 21 | 2,286,611 |
| `elite/` | 73 | 1,249,988 |
| `event/` | 46 | 2,550,783 |
| `general_skill/` | 136 | 457,405 |
| `generalphoto/` | 169 | 3,714,155 |
| `glory/` | 14 | 4,999,810 |
| `heads/` | 247 | 2,500,605 |
| `help/` | 43 | 1,693,686 |
| `largebg/` | 18 | 200,713 |
| `legend/` | 71 | 5,057,303 |
| `map/` | 13 | 17,301,712 |
| `pass/` | 8 | 119,036 |
| `photo/` | 77 | 2,455,616 |
| `purchase/` | 81 | 1,711,727 |
| `scenario/` | 5 | 305,676 |
| `shareapp/` | 32 | 4,522,313 |
| `wonder_icon/` | 31 | 1,038,805 |
| **合计** | **1,196** | **64,982,019** |

`generalphoto/` 的半身像与 `heads/` 的圆形头像不是同一目录。`photo/`、`generalphoto/` 的名称接近，不能统一写入任意一个目录。目录文件数不等于将领记录数；资源命名通常使用 EName/Photo 字符串，而不是 General Id。

### 6.2 常用图集核对

路径相对 `assets/`。这些图集以 Texture 与 Images 片段保存，统计时临时包根节点，没有修改原 XML。Image 数是图集矩形条目数，不是独立图片文件数。

| 图集 XML | Image 数 | Texture.name | 实际图片 |
| --- | --- | --- | --- |
| `terrain.xml` | 117 | `terrain.png` | `terrain.webp` |
| `terrain_hd.xml` | 117 | `terrain_hd.png` | `terrain_hd.webp` |
| `plant_hd.xml` | 72 | `plant_hd.png` | `plant_hd.png` |
| `buildings_hd.xml` | 40 | `buildings_hd.png` | `buildings_hd.webp` |
| `coast_hd.xml` | 90 | `coast_hd.png` | `coast_hd.pkm` |
| `coastmask_hd.xml` | 90 | `coastmask_hd.png` | `coastmask_hd.pkm` |
| `tacticalmap.xml` | 408 | `tacticalmap.png` | `tacticalmap.webp` |
| `image/image_flags_hd.xml` | 75 | `image_flags_hd.png` | `image/image_flags_hd.webp` |
| `maptext.xml` | 267 | `maptext.png` | `maptext.webp` |

Texture 声明 `.png` 而文件为 WebP/PKM 是此目录的实际约定，不能单凭这种差异判为损坏。图片内的图块名字又是另一层标识，不能按 Image.name 逐个寻找独立文件。

`assets/map/` 另有 384 个 512×512 PKM：`map1_1@2x.pkm` 到 `map1_192@2x.pkm`，以及 `map1_a1@2x.pkm` 到 `map1_a192@2x.pkm`。`image/map/` 的 13 个文件则为 `01.pkm` 到 `13.pkm`。这两处用于不同的显示资源，不能与 1,383 张 BTL 数量相加后当作可部署部队的地图数量。

## 7. 关键文件指纹

路径相对解包项目根，SHA-256 根据本机当前文件完整字节计算。用于核对来源与版本差异；不代表官方签名验证。没有把原游戏文件复制进本仓库。

| 文件 | 字节数 | SHA-256 |
| --- | --- | --- |
| `AndroidManifest.xml` | 18,912 | `f51b5152ec92527ce550f811241f70a081d06f52b8ac1a1537ae8b72ef4f336c` |
| `classes.dex` | 8,061,724 | `cd81b929e39493812aa9b2949638c30f6140ab7e229b42947b0bee2cd23b4bef` |
| `classes2.dex` | 7,845,704 | `21d1853deaec7f63aec9b1745720290fe90c1adb7dd8670e20e37f4a7f73428c` |
| `resources.arsc` | 545,100 | `fb9901b6eaec396cc748466f36862ba29a396e84741f1533fcbe9205932f318c` |
| `lib/arm64-v8a/libworld-conqueror-4.so` | 11,015,960 | `531ee21c21fd994176b6198ae30541b58e3a338fdf192bfc54b93d5498ff974d` |
| `assets/world.bin` | 143,872 | `bab396bff3946e005028a6bb72c0e9d3d9a1de34d6daecf3bfbe7ec35b45b936` |
| `assets/config/def_map.xml` | 185 | `c1e6537ac597287eff8b7cfe824f05e2c4093fd0c5b852a7d29a72b679c4bf41` |
| `assets/config/def_mapterrain.xml` | 10,165 | `71f0a8259bd26f59c0f53f61cced12c8e1dd5d5b2bb4d9d5b47304b78109b717` |
| `assets/json/GeneralSettings.json` | 492,905 | `726f1ab146bc9675d09b0658d10c5c41013d2d127c782226297d2e16e59ae08a` |
| `assets/json/ArmySettings.json` | 350,643 | `47f9857f4aca152b679b102c35fabf79d9dcf216084ab16f13dcb1c21a9db63a` |
| `assets/stage/stage10103.btl` | 12,876 | `8f044ce877e896607547d126a1299c6b3709e6bd5ef816ec2283864cc584dbf2` |
| `assets/stage/conquest1.btl` | 87,428 | `398452e9b3fa43ab36a7a58b0f2065f39920c653decead5b0d1cee285b447316` |

项目经编辑后指纹变化是预期结果，不能再把输出与原始样本的相同指纹当作成功要求。无修改的 binary codec 往返可要求逐字节一致；JSON 保存应检查语义，PNG/WebP 等图片还要区分编码格式、像素与图集元数据。

## 8. 可复现的只读统计

以下 Python 3 命令仅遍历、读取和向终端打印，不创建文件、不修改游戏目录，且只使用标准库。换版本时先修改 root，再与本资料的统计作对照。

```bash
python3 - <<'PY'
from pathlib import Path
from collections import Counter, defaultdict
import hashlib
import json
import struct
import xml.etree.ElementTree as ET

root = Path('/home/j60100428/game/wc4/World Conqueror 4_1.30.0')
assets = root / 'assets'
for label, base in [('项目', root), ('assets', assets)]:
    groups = defaultdict(lambda: [0, 0])
    for path in base.rglob('*'):
        if not path.is_file() or path.is_symlink():
            continue
        relative = path.relative_to(base)
        name = relative.parts[0] if len(relative.parts) > 1 else '(根目录文件)'
        groups[name][0] += 1
        groups[name][1] += path.stat().st_size
    print(label, '文件数', sum(n for n, b in groups.values()),
          '字节数', sum(b for n, b in groups.values()))
    for name, (count, size) in sorted(groups.items()):
        print(name, count, size)

extensions = Counter(p.suffix.lower() for p in assets.rglob('*') if p.is_file())
print('assets 后缀', dict(sorted(extensions.items())))
total = 0
for path in sorted((assets / 'json').glob('*.json')):
    rows = json.loads(path.read_text(encoding='utf-8-sig'))
    if not isinstance(rows, list):
        raise ValueError(f'不是数组表：{path}')
    print(path.name, len(rows))
    total += len(rows)
print('JSON 总行数', total)

for path in sorted((assets / 'config').glob('*.xml')):
    tree = ET.fromstring(path.read_text(encoding='utf-8-sig'))
    print(path.name, tree.tag, len(tree), dict(Counter(x.tag for x in tree)))

versions = Counter()
sources = Counter()
for path in sorted((assets / 'stage').glob('*.btl')):
    with path.open('rb') as stream:
        header = stream.read(128)
    if len(header) != 128:
        raise ValueError(f'头部不足 128 字节：{path}')
    fields = struct.unpack('<32i', header)
    versions[fields[0]] += 1
    sources[fields[1]] += 1
print('BTL 版本', dict(sorted(versions.items())))
print('BTL MapNumber', dict(sorted(sources.items())))

for relative in ['AndroidManifest.xml', 'assets/world.bin',
                 'assets/json/GeneralSettings.json', 'assets/stage/conquest1.btl']:
    data = (root / relative).read_bytes()
    print(relative, len(data), hashlib.sha256(data).hexdigest())
PY
```

仅检查某个文件时也可使用 `stat`、`file`、`sha256sum`；AndroidManifest.xml 是二进制 XML，不能用普通文本搜索作为版本核查依据。上述 BTL 统计只读头部，不代替 [共享布局校验](../../../WC4MapEditor.Core/Parsers/BTL/BtlLayout.cs)或全文件往返验证。

## 9. 后续维护

本资料是 2026-10-08 的项目核对节点。补充新发现时写日期、输入路径/指纹与证据；游戏版本升级后在 `docs/projects/` 下新建对应版本目录，不直接覆盖此份统计。

编辑器功能改动继续记录在根 README 和审计矩阵，历史 README 继续保存在 [版本节点索引](../../history/README.md)。本次统计没有修改原游戏文件，没有重打包或签名；资料与项目加载和审计修复一起纳入 2026-10-08 的本次合并提交。
