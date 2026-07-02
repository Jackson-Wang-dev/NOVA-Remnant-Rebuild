# NovaScript 参考手册

> 用途:给 Claude Code(或人类作者)写 `.txt` 剧本、或给运行时新增 `#@export` 函数时查阅的速查表。
> 这是"怎么写脚本/怎么扩展运行时"的参考,不是移植决策记录——移植背景、子系统现状、每条偏离 Nova1 的理由见 `porting-guide.md`,本文档只摘录跟写脚本直接相关的约束。
> 函数清单是从当前 `nova/sources/gdscript/runtime/*.gd` 里所有 `#@export` 标记的函数手动整理的(自动转发层在 `nova/sources/gdscript/runtime_block.gd`,由 `addons/nova_macro` 在编辑器内重新生成)。新增/改名任何 `#@export` 函数后,记得同步更新本文档对应小节。

---

## 1. 文件结构

一个 `.txt` 剧本由若干**节点(node)**组成,节点之间靠空行或下一个 `label()` 分隔。每个节点内部又被空行切成若干**块(chunk)**,每个块最终对应一条对话记录(`DialogueEntry`)。

### 1.1 三种顶层语法元素

| 语法 | 含义 | 何时执行 |
|---|---|---|
| `@<\| ... \|>` | **即时执行块(eager)** | 解析脚本文件时立刻执行(`label`/`branch`/`jump_to`/`is_start` 系列都必须用这个) |
| `<\| ... \|>` | **延迟执行块(lazy)** | 游戏播放到这一条对话时才执行(`show`/`move`/`play` 等表现/逻辑调用) |
| 纯文本行 | **对话文本** | 同一节点连续的纯文本行会被合并成一条对话的文本(用 `\n` 连接) |

块内是合法的 GDScript 语句(可以多行、可以用局部变量、`if`/`for` 等),直接照搬 `nova/sources/gdscript/runtime/*.gd` 里 `#@export` 暴露的函数名调用即可,不需要任何前缀(`RuntimeBlock` 基类已经把它们全部转发好了)。

```
@<|
label("ch1_room", "宿舍")
is_unlocked_start()
|>
<|
show("bg", "backgrounds/room")
set_box()
|>
这是一条普通对话。

<|
play("bgm", "main_theme", 0.6)
|>
这一条会先放 BGM 再显示文字(同一个块里可以连续调用多个函数)。

测试结束
@<| is_end() |>
```

### 1.2 带属性的块:`@[key=value, ...]<| ... |>` / `[key=value, ...]<| ... |>`

eager/lazy 块都可以在 `<|` 前面加一个 `[...]` 属性列表,目前唯一用到的属性是 `stage`(决定一段**lazy**代码在对话生命周期里的执行时机,对应 `DialogueActionStage`):

| `stage` 取值 | 执行时机 |
|---|---|
| (不写,默认) | 对话被"到达"时立刻执行(`Default`) |
| `"before_checkpoint"` | 在这一步生成存档点之前执行 |
| `"after_dialogue"` | 这一步对话完全结束(下一步开始前)执行 |

```
[stage="after_dialogue"]<|
some_cleanup()
|>
```

目前仓库里的剧本都还没真正用到这个属性(只在 `DialogueEntryParser.cs`/`GetStageName` 里有现成支持),需要"这段代码要在对话结束后才跑"这类需求时才用得上。

### 1.3 节点(label)与流程控制

只能在 **eager 块**里调用:

- `label(name, display_name=null)` —— 声明一个新节点;`display_name` 省略时沿用上一次显式传过的 `display_name`(同一文件里连续几个节点想共享一个章节标题时可以只在第一个写)。
- `is_chapter()` —— 标记为章节(出现在章节选择里)。
- `is_start()` —— 标记为起点(默认锁定,需要先通关过一次或满足条件才解锁)。
- `is_unlocked_start()` —— 起点 + 章节 + 初始即解锁(`is_chapter()+is_start()` 的快捷方式,Colorless 用的几乎都是这个)。
- `is_default_start()` —— 目前是 `is_unlocked_start()` 的同义词(nova2 还没有"跳过章节选择直接开新游戏"的标题页流程)。
- `is_debug()` —— 调试节点(需要按住调试键进入)。
- `jump_to(dest)` —— 无条件跳转到 `dest` 节点,必须在节点末尾调用。
- `branch([...])` —— 给当前节点加分支选项,必须在节点末尾调用、且每个节点只能调用一次。见下方"分支语法"。
- `is_end(name=null)` —— 标记为结局节点;不写 `name` 时用节点自己的名字。**一个文件解析完毕时,如果当前节点还没被显式终结(没调用过 `is_end`/`jump_to`/`branch`),会被自动补一次无名 `is_end()`**,所以文件最后一个节点如果只是想要"普通无名结局",不手动调用也可以;只有"明确这是一个有名字的结局"时才需要显式传 `name`。

> ⚠️ **硬性约束,比上面这条"自动补 `is_end()`"更重要**:这个自动补全只重置节点*类型*,不会把节点末尾那段对话文本解析成 `DialogueEntry`——节点末尾的对话/lazy 块只有在被**紧跟着的下一个 eager 块**(下一个 `label()`,或 `is_end()`/`jump_to()`/`branch()`)触发时才会真正被解析并挂到节点上(`ScriptLoader.ParseScript` 的实现:只有遇到 eager 块才会 flush 累积的 chunk 列表,循环结束后没有任何收尾 flush)。如果一个文件的最后一个节点,在它最后一段对话文本之后**没有任何 eager 块**收尾,这段文本会被静默丢弃,整个节点 `DialogueEntryCount` 变成 0——玩家一进入这个节点就会立刻触发"到达末尾"逻辑(`StepAtEndOfNode`),什么对话都看不到。**结论:每个文件的最后一个节点,即使只是"普通无名结局"也必须显式写一个 `@<| is_end() |>`(或 `jump_to`/`branch`)收尾,不能依赖"反正会自动标记成结局"就省略掉这一行。**(`test_backlog.txt` 曾经漏写过这一行,导致整个节点 0 条对话记录,2026-06-22 审计时发现并修复,见 `porting-guide.md` 决策记录。)

节点名前缀 `l_` 表示"文件局部节点":引擎在注册时自动拼上当前文件名,避免不同文件里同名 `label` 冲突("Overwrite node"报错)。规则如下:

- **文件内部节点必须加 `l_` 前缀**:`label`/`jump_to`/`branch` 的 `dest` 只要是"只在本文件内跳转用的"节点,都必须带 `l_`。
- **全局入口节点不加前缀**:章节入口(如 `label("ch1", "第一章")`)需要从外部可达,必须是全局名,不能带 `l_`。
- **AUTOSTAGE 自动处理**:VVN 的 `build_node_skeleton` 生成骨架时,所有通过 `#node:` 声明的内部节点自动加 `l_` 前缀——`label`、所有 `branch dest`、所有 `jump_to` 目标一并处理,无需手写。主入口 label(由 `base_label` 或用户指定的标签名决定)始终保持全局。

#### 分支语法

```
@<|
branch([
    { dest="node_a", text="选项 A 的按钮文字" },
    { dest="node_b", text="选项 B" },
    { dest="node_c", mode="jump" },                          # 无 UI,满足条件就直接跳走
    { dest="node_d", text="...", mode="show", cond="v_flag > 1" },   # 条件不满足就不出现这个按钮
    { dest="node_e", text="...", mode="enable", cond="v_flag < 2" }, # 条件不满足按钮出现但不能点
])
|>
```

`mode` 取值:`"normal"`(默认,普通可点击选项)/`"jump"`(无 UI,需要配合 `cond`,条件满足就跳走,不能带 `text`/`image`)/`"show"`(`cond` 决定按钮是否出现)/`"enable"`(`cond` 决定按钮能否点击)。`cond` 是一段 GDScript 布尔表达式字符串,可以直接读 `v_x`/`gv_x` 变量(见下文变量章节)。

---

## 2. 对话文本语法

### 2.1 具名对话:`显示名::对话内容`

```
张浅野::这是一句台词
```

也支持隐藏名(用于配音匹配等内部逻辑,跟显示名不同时才需要):

```
张浅野//zhangqianye::这是一句台词
```

全角 `：：` 和半角 `::` 都认。没有 `::` 的纯文本行就是旁白(没有说话人)。同一显示名第一次出现隐藏名后,后续只写显示名也会自动复用那个隐藏名。

### 2.2 文本插值:`{{变量名}}`

```
这是本章节第 {{gv_play_count}} 次开始
```

运行时把 `{{xxx}}` 替换成 `Variables.Get("xxx")` 的当前值;找不到对应变量时**原样保留 `{{xxx}}` 不报错**(对齐 Lua `string.gsub` 替换函数返回 nil 保留原文的行为)。可以出现在对话文本和显示名里。

### 2.3 记忆退化标签:`<mmr>...</mmr>` / `<dmg>...</dmg>`(HyBloom 专属,非上游 Colorless)

给一条对话标注"在回看(Backlog)里随着时间推移会显示成别的内容",模拟角色记忆衰退:

```
<mmr>这句话……内容已经模糊了……</mmr><dmg>[这段记忆已经损坏]</dmg>这是原文,正常游玩时显示这个
```

- 两个标签都是可选的,可以只写一个。
- 标签本身和里面的内容**不会出现在正常对话框里**——解析时被剥除,只保留"原文"那部分用于正常播放。
- 在回看列表里,这条记录"身后"积累的新对话条数(`distance`)达到一个随机阈值(约 10~12 条)后变成 `<mmr>` 里的内容,再过一段(约再 6~8 条)变成 `<dmg>` 里的内容;阈值由该条文本的内容哈希播种,同一句话每次游玩退化节奏一致。
- 实现见 `MemoryTable.cs`/`DialogueEntryParser.cs`/`BacklogViewController.GetDegradedText`。

### 2.4 Markdown 风味(仅 tutorial 用,非正式语法)

- `` `code` `` → 渲染成带等宽字体样式的代码片段。
- `[文字](链接)` → 渲染成可点击的样式化链接。

这两个不是 NovaScript 规范的一部分,只是教程剧本用的便利写法(`DialogueEntryParser.cs` 里可以整段注释掉)。

---

## 3. 变量系统

- `v_` 前缀 = **局部变量**:纯内存,不持久化。读档/跳转靠"从节点树重放脚本"自然重建,所以局部变量天然在每次重新进入章节时归零。
- `gv_` 前缀 = **全局变量**:持久化在存档里(`GlobalSave`),跨章节、跨"重新开始本章节"都不会清零;只能存 `bool`/数字/字符串/`null`,存别的类型会报错。
- 直接用 GDScript 赋值/读取语法,不需要调用任何函数:

```
<|
gv_play_count = (gv_play_count if gv_play_count != null else 0) + 1
v_flag = 0
|>
要加一个 v_flag 吗?当前 v_flag = {{v_flag}}
```

- `branch` 的 `cond` 字符串、`{{}}` 插值都能直接引用这些变量名。

---

## 4. 函数参考(按模块分组)

> 标 `entry` 的参数:省略时默认挂在 `o.anim`(当前对话的动画链根)上;传入上一次调用返回的 `entry` 可以接着挂到那条链的末尾,实现"先做完 A 再做 B"的串行,或者"同一个起点下挂多个并行分支"。能拿到 `entry` 的函数大多会把"刚排进去的那一步"作为返回值,方便继续链。

### 4.1 节点 / 流程(`ScriptLoader`,仅限 eager 块)

见第 1.3 节,不重复列出。

### 4.2 立绘 / 背景 / 转场(`Graphics`)

| 函数 | 签名 | 说明 |
|---|---|---|
| `show` | `show(obj, image_path, coord=null, color=null, duration=null)` | 显示一张图/一个立绘 pose。`obj` 是绑定对象名(`"bg"`/`"fg"`/`"cam"`/某个角色绑定名等)。**`image_path` 用法因 `obj` 类型而异**:<br>• `obj` 是**立绘合成对象**(角色绑定名,如 `"ergong"`):第二参数必须是 **pose 别名**,即 `character.gd` 里 `poses` 字典定义的键名(如 `"normal"`/`"cry"`),引擎再通过 `Character.get_pose` 查出对应的合成部件串。**禁止直接传 `"standings/…"` 路径**——那是引擎内部的合成图层路径,不是 NovaScript 的 API 参数,传了不会报错但角色不会显示。<br>• `obj` 是**非立绘对象**(`"bg"`/`"fg"`/`"cg"` 等):第二参数是 `resources/` 下的图片相对路径(不带扩展名),如 `"backgrounds/room"`。<br>`coord`/`color` 给了就顺带调一次 `move`/`tint`。**`coord` 必须是恰好 5 个元素的数组 `[x, y, scale, rx, ry]`**,缺元素会越界崩溃导致角色不显示;`y` 通常取 `-0.3`(让角色底部对齐画面底边),`rx`/`ry` 通常为 `0`。典型布局参考:单人居中 `[0,-0.3,0.53,0,0]`;两人并排 `[-2,-0.3,0.53,0,0]` 和 `[2,-0.3,0.53,0,0]`;四人 `[-3,-0.3,0.4,0,0]`/`[-1,-0.3,0.4,0,0]`/`[1,-0.3,0.4,0,0]`/`[3,-0.3,0.4,0,0]`;x 绝对值小于 1 的多人布局会导致立绘重叠。 |
| `hide` | `hide(obj)` | 隐藏。 |
| `move` | `move(obj, coord, scale=null, angle=null, duration=null, entry=null)` | `coord` 是 `[x, y, scale, z, angle]` 简写数组(对应位形,5 个槛位都可以传 `null` 跳过那一项),也可以直接传 `Vector3`。`duration` 给了就动画过渡,否则瞬间生效。`obj=="cam"` 时 `scale` 改写相机的 `size`(正交相机缩放),不是真的缩放变换。 |
| `tint` | `tint(obj, color, duration=null, entry=null)` | 染色(`modulate`,立绘对象走专属的 `Modulate`)。`color` 可以是裸数字(广播成 RGB,alpha=1)、`[gray]`/`[gray,alpha]`/`[r,g,b]`/`[r,g,b,a]` 数组,或 `Color`。 |
| `env_tint` | `env_tint(obj, color, duration=null, entry=null)` | 环境光染色,**只对立绘合成对象生效**,跟 `tint` 的 RGB 相乘叠加(不影响 alpha),非立绘对象会警告并跳过。 |
| `vfx` | `vfx(obj, shader_layer, t=1.0, duration=null, properties=null, entry=null)` | 给 `obj`(或 `"cam"` 的某个图层)挂一个 `resources/shaders/<shader_layer>.gdshader` 全屏/局部特效。`shader_layer` 可以是裸名字,或 `[名字, layer_id]`(只有 `"cam"` 才有多层,0~3);传 `null` 清除。`t` 是该 shader 的 `_T` 强度参数,`properties` 是要顺带设置的其它 uniform 字典。 |
| `trans` | `trans(obj, image_name_or_func, shader_name, duration=1.0, properties=null, color2=null, entry=null)` | 旧图→新图的 crossfade 转场。`image_name_or_func` 是新图片路径,或者(仅 `obj=="cam"` 时)一个 `Callable`,会在旧画面还冻着当底图时执行(可以在回调里做几次 `show`/`hide` 拼一个完整换场再揭幕)。 |
| `trans_fade`/`trans_left`/`trans_right`/`trans_up`/`trans_down` | `(obj, image_name_or_func, duration=1.0, entry=null)` | `trans` 的预设包装:整体淡入淡出 / 向左擦除 / 向右擦除 / 向上擦除 / 向下擦除。 |
| `trans2` | `trans2(obj, image_name_or_func, shader_layer, duration=1.0, properties=null, duration2=null, properties2=null, color2=null, entry=null)` | 通用"推高 `_T`(遮住)→中途换内容/触发回调→推回 `_T`(揭示)"序列,复用任意 `vfx()` 用过的 shader(不限于 fade 系列,比如 `radial_blur`)。`image_name_or_func` 可以是 `null`(纯特效,不换图)。 |

`obj` 的常见绑定名:`"bg"`(背景)/`"fg"`(前景/CG 叠加层)/`"cam"`(主摄像机)/`"cg"`(独立 CG 层)/角色立绘的绑定名(脚本里用中文拼音或角色 key,具体看 `game.tscn`/`ObjectBinder` 配置)。
> VFX 约束: `resources/shaders/*.gdshader` 分两条管线。`shader_type canvas_item` 是全屏后处理,只能写成 `vfx("cam", "shader")` 或 `vfx("cam", ["shader", layer])`;比如 `rain`/`color`/`mono`/`shake` 都属于这一类。不要把它们挂到 `"bg"`/`"fg"`/`"cg"`/角色上,否则背景 Sprite3D 会拿到不兼容材质并退成灰黑/纯色画面。需要背景上有雨幕时,用 `vfx("cam", ["rain", 1], ...)` 另开一个 cam 图层,再用 `vfx("cam", [null, 1])` 清掉。`shader_type spatial` 才能用于 `"bg"` 等非 cam 对象。

### 4.3 对话框(`DialogueBox`)

| 函数 | 签名 | 说明 |
|---|---|---|
| `set_box` | `set_box(pos_name="bottom", style_name=null)` | 切换/初始化对话框位置+风格预设。`pos_name`:`"bottom"`/`"top"`/`"center"`/`"left"`/`"right"`/`"full"`/`"hide"`(隐藏,会把"当前对话框"置空,谨慎用)。`style_name` 不传时用该位置自己的默认风格(`"light"`/`"dark"`/`"dark_center"`/`"transparent"`/`"subtitle"` 等)。**每个剧本的第一个 lazy 块通常都要调一次**,否则没有任何对话框可显示。 |
| `set_text_appear` | `set_text_appear(mode=0, char_speed=30.0, fade_duration=0.3)` | 设置后续对话的文字出现方式:`0`=瞬间整段显示,`1`=整段淡入,`2`=逐字出现,`3`=逐字淡入。 |
| `box_alignment` | `box_alignment(mode="left")` | 单独设置当前对话框的文字对齐(`"left"`/`"center"`/`"right"`),不改变其它风格。 |
| `new_page` | `new_page()` | 清空当前对话框已显示的文字(不切换位置/风格)。 |
| `text_delay` | `text_delay(time)` | 给*下一条*追加文字的出现动画加一次性延迟(秒)。 |
| `box_hide_show` | `box_hide_show(duration=1.0, pos_name="bottom", style_name=null)` | 隐藏对话框 `duration` 秒后自动切回指定位置/风格——常用作章节开场"黑屏/过场后对话框淡入"的效果。 |
| `allow_skip_unread` | `allow_skip_unread(value=true)` | 开发者开关:允许"快进(Skip)"跳过玩家从未读过的内容(默认 Skip 遇到没读过的内容会自动停下)。 |
| `stop_auto_skip` | `stop_auto_skip()` | 强制打断当前的 Auto/Skip 状态(比如进小游戏前)。 |

### 4.4 音频(`Audio`)

| 函数 | 签名 | 说明 |
|---|---|---|
| `play` | `play(channel, track_name, vol=0.5, duration=null)` | 播放音轨。`channel` 是绑定名(`"bgm"`/`"bgs"` 等)。给 `duration` 就从 0 渐入到 `vol`。 |
| `stop` | `stop(channel, duration=null)` | 停止播放;给 `duration` 是先渐出到 0(但轨道还留着,没有真正 stop 的回调钩子)。 |
| `volume` | `volume(channel, value, duration=null, entry=null)` | 单独调音量,不换轨。 |
| `fade_in` | `fade_in(channel, track_name, vol=0.5, duration=1.0, entry=null)` | 立即切轨 + 渐入(可挂在某个 `entry` 链上排队)。 |
| `fade_out` | `fade_out(channel, duration=1.0, entry=null)` | 渐出后停止。 |
| `sound` | `sound(track_name, vol=0.5)` | 播放一次性音效(`resources/audio/sound/`)。 |
| `say` | `say(speaker_name, voice_name, delay=0.0, override_auto_voice=true)` | 手动指定这一条对话的配音文件,覆盖自动配音规则。 |
| `auto_voice_on` | `auto_voice_on(name, index)` | 给角色 `name` 开启自动配音:同一对话里只要说话人是 `name` 就自动按 `index` 找配音文件;`index` 可以是裸数字,或 `[前缀, 起始序号]`。 |
| `auto_voice_off` | `auto_voice_off(name)` | 关闭某角色的自动配音。 |
| `auto_voice_off_all` | `auto_voice_off_all()` | 关闭全部角色的自动配音。 |
| `set_auto_voice_delay` | `set_auto_voice_delay(value)` | 设置自动配音的统一延迟(秒)。 |

`channel` 常见取值:`"bgm"`(背景音乐)/`"bgs"`(环境音)/`"voice"`(配音轨)。

### 4.5 立绘头像(`Avatar`)

| 函数 | 签名 | 说明 |
|---|---|---|
| `avatar` | `avatar(pose, color=null)` | 给*当前说话人*显示头像(不需要传角色名,自动取当前对话的说话人)。 |
| `avatar_hide` | `avatar_hide(name=null)` | 隐藏头像;不传 `name` 隐藏当前说话人的。 |
| `avatar_clear` | `avatar_clear()` | 隐藏所有已显示的头像。 |

### 4.6 演出 / 动画调度(`AnimHelper`)

| 函数 | 签名 | 说明 |
|---|---|---|
| `wait` | `wait(duration, entry=null)` | 在动画链上排一段固定延迟。 |
| `wait_all` | `wait_all(target_entry, entry=null)` | 等 `target_entry` 这条链上排的东西全部播完(再强制停掉它),常用来"等某个持续特效结束才继续"。 |
| `loop` | `loop(func_, entry=null)` | 重复调用 `func_(tail)`,`func_` 每次返回新的链尾,返回 `null` 时结束循环。 |
| `anim_hold_begin` / `anim_hold_end` | `()` | 标记一段"长时间挂起"动画轨道(`o.anim_hold`,跟逐对话的 `o.anim` 是独立轨道,不阻塞点击推进)的开始/结束,内部都是清空这条轨道。 |
| `cam_punch` | `cam_punch(entry=null)` | 镶头打击特效(快速下沉回弹 + 缩放回弹)。 |
| `auto_step` | `auto_step()` | 给*当前*这一步对话标记"播完(文字揭示/`o.anim`+`o.anim_hold`/配音 全部不再阻塞)自动推进到下一条,不需要点击,不依赖 Auto 模式"。常搭配播放视频/长演出用。 |

### 4.7 视频(`VideoHelper`)

| 函数 | 签名 | 说明 |
|---|---|---|
| `video` | `video(video_name)` | 加载并显示 `resources/videos/<video_name>.ogv`,但不开始播放。 |
| `video_play` | `video_play(duration=null, entry=null)` | 开始播放;`duration` 不传时默认整段视频长度,返回值可以配合 `entry:_for(...)` 之类阻塞。 |
| `video_hide` | `video_hide()` | 停止播放并隐藏播放器。 |

### 4.8 时间滚动过场(`TimeScrollHelper`,HyBloom 专属)

| 函数 | 签名 | 说明 |
|---|---|---|
| `time_scroll` | `time_scroll(from_year, from_month, from_day, from_hour, from_min, from_sec, to_year, to_month, to_day, to_hour, to_min, to_sec, mid_duration=2.0, entry=null)` | 全屏时间快进过场:日期数字从起始时间级联加速滚动到接近终点,峰值停留约 `mid_duration` 秒,再减速收尾到整秒跳动,最后淡出。返回值已经是阻塞好对应总时长的 `entry`,调用后直接接 `auto_step()` 通常就是想要的效果。 |

```
<|
time_scroll(2024,1,1,8,0,0, 2024,7,15,20,30,0, 2)
auto_step()
|>
从 2024 年 1 月跳到 7 月(全屏过场,播完自动推进,不需要点击)
```

### 4.9 提示框(`AlertHelper`)

| 函数 | 签名 | 说明 |
|---|---|---|
| `alert` | `alert(text)` | 阻塞式提示框(只有"确定"按钮),同时强制打断 Auto/Skip,避免没读到就被跳过。`text` 直接当字面文本显示,不是 i18n key。 |
| `notify` | `notify(text)` | 非阻塞、自动淡出的提示条,不影响 Auto/Skip。 |

---

## 5. 给运行时新增 `#@export` 函数时的硬约束

这几条是从实际踩坑(见 `porting-guide.md` 决策记录)总结出的、**任何新的 GDScript→C# 调用边界**都要遵守的规则,不遵守会在 GDScript 侧报"看起来方法存在但 Nonexistent function"或静默不触发:

1. **不能传 `Callable` 给任何 C# 方法参数**(这个项目锁定的 Godot 4.6.3 mono 上,跨边界的 `Callable` 会变成空的 Method/Target,静默不触发)。需要"延迟执行一个回调"时改用 `SceneTreeTimer`(`(Engine.get_main_loop() as SceneTree).create_timer(t).timeout.connect(callback)`),纯 GDScript→GDScript,不跨边界。
2. **C# 方法签名不能含裸 `params object[]`**(或其它非 Variant 兼容类型),否则从 GDScript 调用直接报方法不存在,即使 C# 内部调用完全正常。
3. **不能是 C# 扩展方法**,只能是真实实例方法——GDScript 的动态分发看不到扩展方法语法糖。
4. **返回类型必须是 Variant 兼容类型**,不能直接返回裸 `ISingleton`(没继承 `RefCounted`/`GodotObject` 的类型)。
5. 新增带 `class_name` 的 `.gd` 文件后,在不方便打开真实 Godot 编辑器的环境下,记得先跑一次 `godot --headless --editor --path . --quit` 重建 `.godot/global_script_class_cache.cfg`,否则 `--run-tests`/正常运行都会报 `Identifier "X" not declared in the current scope`。
6. 新增/改名任何 `#@export` 函数后,正式生成转发层的方式是在 **Godot 编辑器**里打开项目一次(触发 `addons/nova_macro` 的 `_build()`);在不方便开编辑器时可以先手动按现有格式在 `runtime_block.gd` 里追加一段(参考文件里其它模块的写法),编辑器后续重新生成时会原样覆盖,不会冲突。
