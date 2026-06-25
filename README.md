# NovaRR（Nova: Remnant Rebuild）

NovaRR（原 nova2）是 [Nova1](https://github.com/Lunatic-Works/Nova)（Unity 视觉小说引擎）的 Godot 4 + C# 重写版，同时也是上游 [Lunatic-Works/Nova2](https://github.com/Lunatic-Works/Nova2) 仓库的实现。

## 想配合 AI 辅助写剧本？看 VVN

如果你想用自然语言描述效果、自动改写 NovaScript 剧本，并实时在跑着的游戏窗口里看到效果，配套工具是 **VVN（Vibe Visual Novel）**：

- VVN 仓库：https://github.com/Jackson-Wang-dev/Vibe-Visual-Novel
- **完整的"如何配合使用"说明在 VVN 仓库的 README 里**，包含环境准备、两个仓库怎么连起来、首次配置、完整工作流程和排错。

VVN 通过本机 TCP（`PreviewBridge`，只在 Debug 构建里编译）远程控制这个工程：热重载剧本（`reload`）、跳转到指定剧情位置预览（`seek`）、按文件和行号定位剧情位置（`locate`）。不想用 VVN，也可以只用下面的方式独立打开、运行这个工程。
VVN 的AI服务在本地运行，尚未挂载到服务器上，所以Deepseek的API Key是透明的。目前为止，里面应该还有9元余额（6/24/2026），大家可以在VVN的设置界面换成自己的API key，也可以用我的（且用且珍惜）。

## 独立运行 NovaRR

### 环境要求

安装最新的 Godot 4（.NET / Mono 版）和匹配的 .NET SDK。

### 打开与运行

用 Godot 打开本目录下的 `project.godot`（第一次打开会自动构建 C# 项目），按 F5 运行。

### 跑测试

```bash
godot --headless --path . --run-tests --quit-on-finish
```

退出码 `0` 表示全部通过，`1` 表示有测试失败。也可以用 VS Code 的 “Debug Tests” / “Debug Current Test” 调试单个测试（需要设置 `GODOT` 环境变量指向 Godot 可执行文件，见 `.vscode/launch.json`）。

## 给继续开发引擎本身的人

- [`porting-guide.md`](porting-guide.md) —— 移植原则、各 Tier 的进度清单、里程碑历史、与 Nova1 的每一处偏离及原因，是引擎内部开发的第一手资料。
- [`novascript-reference.md`](novascript-reference.md) —— 剧本作者/写 `#@export` 运行时函数时用的 NovaScript 语法与函数参考。
- [`CLAUDE.md`](CLAUDE.md) —— 给 AI 协作开发约定的项目规则。

普通剧本作者不需要读以上三份文档——直接用 VVN，或者照 `resources/scenarios/` 下现有的 `.txt` 剧本格式手写即可。
