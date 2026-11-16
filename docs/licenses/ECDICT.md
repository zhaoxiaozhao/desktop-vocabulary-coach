# 第三方数据来源与许可

## ECDICT

- 项目：ECDICT — Free English to Chinese Dictionary Database
- 地址：https://github.com/skywind3000/ECDICT
- 用途：生成本项目内置的 5,000 词离线种子词库（`data/seed/words.seed.json`）
- 许可证：MIT（Copyright (c) 2025 Linwei）

许可全文见 [ECDICT-LICENSE.txt](ECDICT-LICENSE.txt)。

生成脚本：`tools/SeedBuilder`。应用运行时不解码、不联网、不再分发 ECDICT 原始 CSV，
只使用按 `data/seed/README.md` 规则裁剪后的子集。
