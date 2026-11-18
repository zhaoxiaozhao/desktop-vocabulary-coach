# AI 连接排查

日志位置：

```text
%LocalAppData%\DesktopVocabularyCoach\logs\app-yyyyMMdd.log
```

## 1. 先看 HTTP 状态码

日志中会出现：

```text
AI Provider 返回 401（https://open.bigmodel.cn/api/paas/v4 / glm-5.3-flash）
```

| 状态码 | 常见原因 | 处理 |
| --- | --- | --- |
| 401 | Key 无效、Key 与服务商不匹配，或测试时用了输入框里的旧 Key | 清空输入框，重新粘贴并保存；确认服务商与 Base URL 一致 |
| 402 | 账户余额不足（如 OpenCode 付费模型） | 充值或换免费服务 |
| 403 | 权限/地区限制，或免费模型限制 | 换模型或服务商 |
| 404 | Base URL 多写了 `/chat/completions`，或模型名错误 | Base URL 只填到 `/v1` 或 `/v4`；核对模型 ID |
| 429 | 额度/频率限制，或免费模型只允许官方客户端 | 换付费模型或其他服务商 |
| timeout | 网络慢、代理、推理模型输出过长 | 检查代理；推理模型改用非推理模型 |

## 2. 测试连接使用哪个 Key

设置页的「测试连接」逻辑：

```text
输入框有内容 → 用输入框里的 Key
输入框为空   → 用已保存的 Key
```

所以：

- 测试已保存的 Key：**先清空输入框**再点测试
- 输入框里的 Key 是错的：不要点「保存并应用」，否则会覆盖原来有效的 Key
- 多连接时，「测试连接」只测试**当前下拉框选中的连接**及其输入框内容

## 3. 推理模型与 max_tokens

GLM-5.3、GLM-5、GLM-4.5、DeepSeek R1、o1/o3 等会先输出 `reasoning_content`，
如果 `max_tokens` 太小，`content` 会被截断或为空，表现为「无法解析」。

0.9.0 已调整：

- 单词卡片 AI 增强：`max_tokens = 2000`，请求超时 60 秒
- 句子练习可以单独填写「句子练习模型」，留空则跟随上面的模型
- 句子练习建议用 `glm-4-flash`（快、非推理、免费额度）
- 若句子练习模型仍是推理模型，直接使用离线句库并提示

## 4. OpenCode 免费模型

OpenCode 的 `*-free` 模型只允许 OpenCode 官方客户端调用，第三方 App 直连会返回
403/429：

```json
{"type":"error","error":{"type":"FreeUsageLimitError",
"message":"Free usage limit reached. Free models work in OpenCode ..."}}
```

App 里请使用付费模型（账户需有余额），或改用智谱 / 通义 / Gemini 等兼容服务。

## 5. 推荐测试配置

```text
服务商：智谱 GLM
Base URL：https://open.bigmodel.cn/api/paas/v4
模型：glm-4-flash（免费、快）
句子练习模型：glm-4-flash
```

保存 Key 后清空输入框，再点「测试连接」。

## 6. 后台例句生成

- 设置 → AI → 「电脑空闲时后台生成例句」。
- 默认每天 2 组、每组 5 句，空闲 5 分钟后执行。
- 生成结果保存在 `%LocalAppData%\DesktopVocabularyCoach\sentences.generated.json`。
- 句子练习优先使用已生成句子；词卡弹出时也会显示生成例句。
- 推理模型（GLM-5.3 等）不适合批量生成，后台会跳过；请把「句子练习模型」填 `glm-4-flash`。

## 8. 中文翻译里出现 `__`

部分模型会把英文空格原样翻译成 `__`。0.9.0 已处理：

- Prompt 明确要求翻译“填入答案后的完整句子”
- 若仍出现 `__`，用该词的中文释义自动替换
- 无法修复的条目会被过滤，不再进入句子练习/例句库
- 已存在的坏条目在读取时会跳过；可删除 `sentences.generated.json` 重新生成

## 9. 多连接与 Auto 模式

设置 → AI → 「连接」：

```text
[下拉框：默认连接 / 连接 2 …]   [新增] [删除]
□ 自动模式：按列表顺序尝试启用的连接
□ 启用当前连接
连接名称：……
```

- 每个连接独立保存「服务商 + Base URL + 模型 + 句子模型 + Key」，
  Key 保存在 `%LocalAppData%\DesktopVocabularyCoach\ai.<连接Id>.key`（DPAPI）。
- 旧版单连接的 `ai.key` 会自动迁移为「默认连接」。
- **手动模式**只使用下拉框选中的连接；**Auto 模式**按列表顺序尝试所有启用的连接。
- Auto 模式下，401/402/403/429、超时或返回内容为空都会自动换下一个连接；
  全部失败才回退本地词库/离线句库。
- 常见用法：把便宜的国产模型放第一位，把备用服务放第二位，开启 Auto 作为兜底。
- 排查时先确认当前是手动还是 Auto：手动模式下换 Key 不会切到别的连接。
