# 版本节点与历史资料

这里按开发节点保存中文说明。节点目录是历史快照，保留当时的描述、测试结果和限制；当前功能状态以[根 README](../../README.md)、[需求文档](../../demand.md)和[最新审计矩阵](../audits/2026-10-08-project-matrix.md)为准。

## 节点索引

| 日期 | 节点 | 资料 | 来源与范围 |
|---|---|---|---|
| 2026-10-02 | 本地编辑器修复 | [原 README](2026-10-02-local-editor/README.md) | `50283f4`，格式、地图编辑、资产审计和测试的原始记录 |
| 2026-10-06 | 上游 6.0 整合 | [原 README](2026-10-06-upstream-integration/README.md) | `51c8486`，在上游 `7d31340` 基础上整合本地代码与 fork 资源 |
| 2026-10-06 | Windows 构建与规划 | [原 README](2026-10-06-windows-packaging/README.md) | `a498238`，2026-10-06 编写、2026-10-08 提交；目录按编写节点命名，记录 Windows 复制规则、启动边界和需求规划 |
| 2026-10-08 | 游戏项目加载 | [审计前 README 快照](2026-10-08-project-import/README.md) | 审计开始时的工作区快照，记录 1.30.0 项目副本、地图底图和 65 条测试；实现随本次合并提交纳入，快照原文不改 |
| 2026-10-08 | 全模块审计与修复 | [矩阵与复现记录](../audits/2026-10-08-project-matrix.md) | 在前一节点上继续核查、修复、验证；随本次合并提交纳入 |
| 2026-10-08 | 1.30.0 游戏项目资料 | [项目说明](../projects/wc4-1.30.0/README.md)、[资源索引](../projects/wc4-1.30.0/resource-index.md) | 本机真实解包目录的只读盘点、地图/配置/美术关联与关键指纹；随本次合并提交纳入 |

## 维护约定

1. 根 README 保留当前操作、支持范围和版本节点入口；后续完成一个节点时追加记录。
2. 历史节点只追加，不用新结论覆盖旧快照。发现旧结论不准确时，在新节点明确更正并引用证据。
3. 信息过长时先归档到新目录，再从根 README 引用；不能先删除历史内容。
4. 已提交节点的快照由 `git show <SHA>:README.md` 原样恢复，内容逐字节相同；没有修改其旧标题、链接或统计，因此其中的相对链接和“当前”一词应按当时上下文理解。
5. 未提交节点标记为工作区快照，不能把它当作已经推送的发行版本。
6. 构建、格式往返、图片像素、Windows 操作和原游戏运行分别记录，历史通过不能替代新增功能验收。

## 历史保真校验

| 来源 | 文件大小（字节） | SHA-256 |
|---|---:|---|
| `50283f4:README.md` | 16109 | `0507a91d89995678ddeb0a98576b16cc9970bebb82be9252eddf416914763760` |
| `51c8486:README.md` | 16109 | `0507a91d89995678ddeb0a98576b16cc9970bebb82be9252eddf416914763760` |
| `a498238:README.md` | 16900 | `88261f594c87495ef6101a294c754a13c6ee74eab8d60b32fbe6393f51542ac2` |
| 审计前工作区 README | 26136 | `92d4fee33a64433a76ad1fe57edee69cbae0117df005aedfc07fe0469cdac511` |

```bash
git show 50283f4:README.md | cmp - docs/history/2026-10-02-local-editor/README.md
git show 51c8486:README.md | cmp - docs/history/2026-10-06-upstream-integration/README.md
git show a498238:README.md | cmp - docs/history/2026-10-06-windows-packaging/README.md
```

三条命令均无输出且退出 `0` 表示原始快照一致。根目录的 [reanme.md](../../reanme.md) 和 [内存优化计划](../memory-optimization-plan.md) 继续保留。
