# 内置种子词库

`words.seed.json` 是本应用首次启动时导入 SQLite 的离线词库，共 **18,914** 个常用英文单词（约 2 万）。

## 来源与许可

- 数据来源：[ECDICT](https://github.com/skywind3000/ECDICT)（Free English to Chinese Dictionary Database）
- 许可证：MIT，全文见 [docs/licenses/ECDICT-LICENSE.txt](../../docs/licenses/ECDICT-LICENSE.txt)
- 处理方式：由本仓库的 `tools/SeedBuilder` 从 ECDICT `ecdict.csv` 中筛选、裁剪并转换，不包含 ECDICT 之外的数据。

MIT 许可证要求保留版权与许可声明；本项目在仓库与安装包中附带上述许可文件。

## 筛选规则

`tools/SeedBuilder` 按以下规则生成（可重复执行）：

1. 仅保留 `^[a-z][a-z'-]*[a-z]$` 的小写英文单词，长度 2–24。
2. 排除高频虚词与代词（the/and/be/himself/something 等，见工具内 `stopWords`）。
3. 排除词频排名前 300 的极基础词，避免「每日新词」出现 the 之类的卡片。
4. 必须有中文释义；丢弃带 `[计]`、`[网络]` 等领域标记的释义行。
5. 每个单词最多保留 2 条中文释义（拆分词性前缀，如 `n.`、`vt.`）。
6. 只保留常见词：牛津 3000 核心词、柯林斯星级、中考/高考/四六级/考研/雅思/托福/GRE 标签，
   或词频排名 ≤ 8000。
7. 按当代语料库词频（缺失时用 BNC）升序取前 20,000 个（通过筛选的候选约 1.89 万）。

难度映射：

| 词频排名 | 难度 |
| --- | --- |
| ≤ 2000 | 1 |
| ≤ 6000 | 2 |
| ≤ 12000 | 3 |
| > 12000 | 4 |

当前分布：难度 1 = 1559，难度 2 = 3868，难度 3 = 4935，难度 4 = 8552。

## 重新生成

```powershell
# 1. 获取 ECDICT 原始 CSV（约 66 MB）
curl.exe -L --fail -o "$env:TEMP\ecdict.csv" `
  "https://raw.githubusercontent.com/skywind3000/ECDICT/master/ecdict.csv"

# 2. 生成种子词库（第 3 个参数为词条数，第 4 个为种子版本）
dotnet run --project tools/SeedBuilder -- `
  "$env:TEMP\ecdict.csv" "data/seed/words.seed.json" 20000 3
```

更新词库后请**递增种子版本**（`version` 字段）。应用启动时会比较数据库中的
`AppSettings["seed.version"]`，版本变化才会重新导入；导入只更新词条与释义，
不会覆盖 `UserWordProgress`、`ReviewLogs` 或 `LearningSessions`。

> 0.8.0 起种子版本为 `3`：从 5,000 词升级时首次启动会新增约 1.39 万词（实测约 3 秒），
> 已学进度不受影响。

## 离线句库（sentences.seed.json）

`sentences.seed.json` 是句子填空练习的离线兜底内容，目前约 70 条口语/语法句：

- `word`：目标词（用于查找词库卡片与记录复习进度）
- `sentence`：含一个 `____` 的英文句子
- `answer`：填入空格的答案（可以是词形变化，如 went / hearing）
- `translationZh`：中文翻译
- `pattern`：固定句式/语法点
- `grammarNote`：中文语法说明
- `distractors`：3 个语法位置合适的干扰项

句库与词库互相独立：`go / take / see` 等最高频 300 词被词库排除，但仍可出现在句子练习中；
这类句子只练习、不写入 SM-2 复习进度。启用 AI 时优先使用 AI 生成的句子，AI 关闭或失败时回退到此文件。

## 已知限制

- ECDICT 未提供例句字段，因此种子词库没有例句；卡片会隐藏例句区域。后续可由 AI 增强或用户词库补充。
- 音标来自 ECDICT，个别为 ASCII 近似（如 `sei`），不是严格 IPA。
- 释义为词典式释义，未按用户水平改写（Sprint 6 可选用 AI 增强）。
