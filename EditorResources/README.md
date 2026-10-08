# 编辑器自用资源

这里保存编辑器的操作预设，不保存《世界征服者 4》的地图、配置表、图片、语言包或安装包。游戏资源必须从用户选择的目录或 APK 加载，保存时仅把新增/修改文件按原相对路径写入增量输出目录。

## 当前保留的预设

| 路径 | 用途 |
|---|---|
| `Config/GeneralInCountry.json` | 将领自动分配的国家筛选预设 |
| `Config/GeneralRandomTemplates.json` | 自动生成将领的随机参数模板 |
| `Config/GeneralSpecialtyTemplates.json` | 将领专长的技能候选模板 |
| `Config/LegionLvSetting.json` | 自动部署的军团等级预设 |
| `Config/MaxFormation.json` | 自动部署的编队数量预设 |

这五份文件于2026-10-12从 `Resource/Config` 迁入，内容逐字节保留。它们不是项目的 `GeneralSettings`、`ArmySettings`、`SkillSettings` 等游戏表，也不会自动覆盖那些表。换游戏版本后，使用预设时仍应核对项目中的 ID 和字段关系。

Core 工程把 `Config/*.json` 复制到 build/publish 的 `EditorResources/Config`；根目录 `setting.txt` 是另一份保留的编辑器配置，复制到程序旁。本文不进入发布包。

将来若补充工具自己的背景或图标，可放在 `Texture/`，不得把游戏版本数据放回这里。来源规则、删除清单和验证结果见 [资源清理审计](../docs/audits/2026-10-12-resource-cleanup.md)。

同日按用户要求更正资源清理范围：项目已有对应项才删除，本仓独有16份资料已恢复到原 `Resource/source/dist` 路径，见 [独有资源说明](../Resource/README.md)和 [更正审计](../docs/audits/2026-10-12-resource-retention.md)。这五份预设继续只保留当前一套，游戏运行时读取所选项目的规则不变。
