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
```

保存 Key 后清空输入框，再点「测试连接」。
