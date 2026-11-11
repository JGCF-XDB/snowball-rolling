# 炮弹命中雪球：爆裂特效研究与三方案设计

> 2026-11-11 · 分支 `feat/cannon-snowball-explosion-preview` · 状态：调研设计归档；已选定 B 并接入对局，Windows 包验证见 [效果说明](README.md)
>
> 范围：**仅视觉**。不改玩法规则、不加碰撞体、不加伤害；本文记录设计阶段的规格、时间盒与验收标准，实际实现与验证结果以效果说明为准。所有数字均可落到本仓库现有实现或文末参考来源。
>
> 结论先行：推荐 **B 冰晶碎裂**。三套方案共用同一个分层系统与统一时间盒 **0–40 ms 闪光/核心 → 40–200 ms 主扩散 → 200–900 ms 余韵**；B 作为默认预设，A 作为低配/降级预设，C 作为"重手感"预设，只换参数不换代码结构。

---

## 0. 一页结论

### 0.1 三方案时间盒总表

| 阶段 | 时间窗（ms） | A 雪雾爆裂 | B 冰晶碎裂 | C 雪崩冲击 |
| --- | --- | --- | --- | --- |
| 预备（anticipation） | 命中前 | 无（由炮台 3 次闪烁预警承担） | 同左 | 同左 |
| 1 闪光 / 核心 | **0–40** | 冷白闪光 0–25 | 高亮闪白 0–30 + 冰蓝反光 | 闪光 0–40 + 方向锥起步 |
| 2 主扩散 | **40–200** | 雪雾团 25–200 | 碎块抛散 40–220 | 锥 0–50 / 地浪 30–180 |
| 3 余韵 | **200–900** | 雾团消散 200–500 | 碎块缩小 200–700 | 余浪/雪尘 250–900 |
| 总时长 | — | **0.50 s** | **0.75 s** | **0.90 s** |
| 镜头抖动 | — | 0.07–0.12，0.10 s | 0.10–0.16，0.12 s | 0.18–0.30，0.16–0.22 s |
| 顿帧 | — | 无 | 20–35 ms | 20–35 ms（偏上限） |
| 音频 | — | 现有 0.55 s 爆炸 + 柔化雪爆层 | 现有爆炸 + 冰晶碎裂层 | 现有爆炸 + 低频延时尾 |

> 注：表内时间均为"从命中判定那一物理步开始"的墙钟时间；60 fps 下 1 帧 ≈ 16.7 ms，40 ms ≈ 2.4 帧，200 ms = 12 帧，900 ms = 54 帧。120 Hz 物理下分别为 ≈4.8 / 24 / 108 个物理步。

### 0.2 推荐

**B 冰晶碎裂**。理由集中在一句话：**雪球本身就是"雪 + 冰晶切面 + 身份色描边"的三层结构，让它碎成同一套几何语言的冰晶块，是唯一能同时说清"这是雪球被打碎了""是哪一颗雪球""在 60° 俯视下依然看得见"的方案**。A 的白色雪雾在白色雪台前对比度最低，C 的方向锥容易被误读成"炮口冲击波"而不是"雪球炸开"。详见第 5 节。

---

## 1. 现状：为什么现在"平"

### 1.1 当前命中链路

一次炮弹命中依次发生（同一 `FixedUpdate` 调用栈内）：

1. [`CannonDirector.Advance`](../../../Assets/SnowballArena/Scripts/Arena/CannonDirector.cs) 分段推进，`FindHit` 求出最早命中，取 `impactPoint = hitCannon.ShotPosition`。
2. `EmitImpact(impactPoint, direction)`：实例化 `CannonArt.impactPrefab`（`ImpactB.prefab`），挂 `CannonBurst`，加入 `bursts` 列表。
3. `ArenaFeedback.CannonExplosion(impactPoint)`：播放 0.55 s 炮弹爆炸音效。
4. `match.Eliminate(victim, EliminationCause.Cannon)`：在 [`MatchController.Eliminate`](../../../Assets/SnowballArena/Scripts/Core/MatchController.cs) 里 `participant.body.gameObject.SetActive(false)` —— **整颗雪球在同一帧消失**。
5. `SpawnCannon(hitCannon)`：炮台换位，继续预警。

关键事实：**爆裂发生在雪球消失之前，且爆裂不挂在雪球下**（挂在 `CannonDirector` 下），所以特效本身能存活；但雪球本体没有任何"被打碎"的过渡信息。

### 1.2 现有反馈数值（本仓库实测）

| 反馈 | 现值 | 位置 |
| --- | --- | --- |
| 冰晶余烬爆裂 | `CannonBurst.Lifetime = 0.30 s`，12 片预设网格碎片，按索引黄金角 2.399963 rad 抛散，径向速度 2.8 m/s + 下坠项 `2·t²`（等效重力 ≈4 m/s²），缩放线性收到 0 | [CannonBurst.cs](../../../Assets/SnowballArena/Scripts/Presentation/CannonBurst.cs)、[CannonAssetBuilder.cs](../../../Assets/SnowballArena/Editor/CannonAssetBuilder.cs)（12 片，按 1:2 混用 IceFragment / EmberFragment） |
| 球撞球雪雾 | `startLifetime 0.45 s`，单粒子 0.25–0.6 s，最多 800 粒子，最大力度追加 20 粒"外环" | [ArenaFeedback.cs](../../../Assets/SnowballArena/Scripts/Presentation/ArenaFeedback.cs) |
| 镜头抖动 | 强度阈值 0.62，幅度 0.07–0.30，时长 `0.10 + 0.07t` = 0.10–0.17 s；只偏移 `localPosition`，不旋转 | [ArenaFeedback.cs](../../../Assets/SnowballArena/Scripts/Presentation/ArenaFeedback.cs)、[CameraShake.cs](../../../Assets/SnowballArena/Scripts/Presentation/CameraShake.cs) |
| 顿帧 | 强度阈值 0.90，20–35 ms，`timeScale → 0.15` 后恢复；批处理模式直接跳过 | 同上 |
| 炮弹爆炸音 | 0.55 s（OpenGameArt，Michel Baradari） | [README 音效表](../../../README.md) |
| 雪球被击中的镜头反馈 | **没有**：`CannonExplosion` 只放声音，不震屏、不顿帧 | `CannonDirector` 第 123 行调用 |

### 1.3 为什么"平"：五个具体原因

1. **信息断档**：雪球从"完整"直接跳到"不存在"，中间没有"体积被撑开 / 结构解体"的 2–3 帧；玩家看到的是"消失"，不是"被打碎"。
2. **爆裂尺度错位**：`EmitImpact` 里 `burst.transform.localScale = projectileRadius / art.projectileRadius`，即按**炮弹半径 0.30 m** 缩放，与被害雪球半径（0.85–2.5 m）无关。爆裂直径约 1–2 m，而大球直径可达 5 m，视觉上像"球旁边放了一小簇烟花"。
3. **方向性未利用**：`impactPoint` 带了炮弹方向，但现有爆裂是各向同性的，没有把"炮弹从哪来"的能量方向传给画面。
4. **对比度先天不足**：雪台、雪球、雪雾都是近白色（雪球 `_BaseColor ≈ (0.97, 0.99, 1.0)`），而 **Bloom 强度为 0、Tonemapping 为 None**（见 4.5），没有光晕帮忙提亮，白色特效在白背景上天然"发灰"。
5. **可读性竞争**：16 颗球同屏（1 玩家 + 最多 15 机器人），每颗都有身份色描边；命中瞬间若没有"身份色"参与，玩家分不清"哪颗球没了"。

### 1.4 不能动的部分

- `Eliminate` 的规则语义（存活数、胜负、观战、击落归属、音效）一律不改。
- `CannonDirector` 的计时、换位、命中判定、单发单目标规则一律不改。
- 特效对象必须**无碰撞体、无 Rigidbody、无伤害、无 Layer 变化**，且必须在 `CannonDirector.Stop()`（结算、重开、返回菜单、销毁）时被清理。
- 现有 `CannonBurst` 的 44 项 `--cannon-smoke` 断言（含 `ActiveEffectCount` 计数与"短寿命后归零"）不能被破坏。

---

## 2. 参考研究

### 2.1 三段式结构：wind-up / climax / fall-off

VFX Apprentice 把特效时间拆成 **wind-up（蓄势）→ climax（爆发）→ fall off（消散）** 三段，并区分 **impulse（瞬时脉冲，跳过蓄势）** 与 **rhythm（节奏型）** 两类模式；对攻击类特效给出的经验是：**节奏快于心跳（>60 BPM）更"攻击性"**，慢于心跳更"正向/邀请"（[The Soul of Effects: What is Timing in VFX?](https://www.vfxapprentice.com/blog/the-soul-of-effects-what-is-timing-in-vfx)）。

对本作的直接结论：**炮弹命中的"蓄势"已经由炮台的三次闪烁预警提供了**（`flashHalfPeriod=0.5 s`，共 3 s 预警），所以命中特效本身应当是 **impulse 型，不需要再加前摇**，只在爆发后做有层次的 fall-off。

### 2.2 100 ms 定律：闪光不是"短一点的长特效"

Detwiler 的 [The hundred millisecond flash law](https://andrewdetwiler.com/sdf/notes/the-flash-law)（基于 Bloch 定律，1885）指出：**低于约 100 ms 时，视觉系统积分的是能量而不是瞬时亮度**，即 `I × t ≈ 常数`。换算到 60 fps：

- 1 帧（16.7 ms）闪光要达到 6 帧（100 ms）闪光的**约 6 倍强度**；
- 2 帧（33.3 ms）需要 3 倍，4 帧（66.7 ms）需要 1.5 倍；
- 超过 100 ms 后，加时长不再增加"冲击"，只增加"存在感"。

因此本作应把闪白控制在 **0–40 ms（约 2–3 帧）**，并通过提高线性光下的亮度峰值（而不是拉长）来保证"打中了"的观感。同一来源也警告：**作者一律用秒，不要用帧**，否则换帧率后强度就变了。

### 2.3 衰减形状：指数 + 1–2 帧攻击段

[The shape of a punch](https://andrewdetwiler.com/sdf/notes/punch-envelope) 给出可执行的曲线规范：

- **指数衰减，不要线性**；线性衰减读起来像"数值被动画化"，不像"事件发生"；
- 冲击的 `tau`（衰减到 37%）取 **100–150 ms**，可见拖尾约为 **3 个 tau（300–450 ms）**；
- 攻击段 **1–2 帧**（示例 22 ms）；超过约 50 ms 就变成"蓄力/鼓包"；
- 同一事件的所有反馈（闪白、震屏、缩放、色偏、音频发送）应共享**同一条包络曲线**，只调一个数。

这条建议直接决定了第 5 节的实现方式：三层视觉 + 震屏 + 顿帧共用一条 `envelope(t)`，而不是各自拍脑袋。

### 2.4 可读性：把 VFX 当"辅助信息"，用三轴控制信息密度

CEDEC 2026 的 [《GRANBLUE FANTASY: Relink》VFX 设计与可读性](https://cedec.cesa.or.jp/2026/timetable/detail/s698020b3129f2/) 把 VFX 从"演出"重新定义为支撑"看→判断→输入"的**辅助信息**，并提出用 **空间方向、能量量、时间方向** 三个轴控制画面信息密度——这正是三套方案可以拉开差异的三个维度：

- A 能量低、空间各向同性、时间短；
- B 能量中、空间各向同性但**剪影变化最大**、时间中等；
- C 能量高、空间**强方向性**、时间最长。

同一场演讲也点明高密度战斗的典型症状："太亮、看不见、读不出"，与本作 16 球同屏的风险一致。

### 2.5 风格化处理：分层色调、剪影细分、自发光

VFX Voice 的 [The Return of Hand-Drawn and Stylized Effects Animation](https://vfxvoice.com/the-return-of-hand-drawn-and-stylized-effects-animation/) 记录了多个"设计爆炸"而非"模拟爆炸"的做法，可直接迁移到低多边形项目：

- 把爆炸拆成 **smoke tone / smoke layer / hotter layer** 多层（Sony Imageworks）；
- 消散时**不是整体降透明度，而是把大块剪影细分成更小的块**（Pixar《Turning Red》"panda poof"），并用幂律分布错开每个块的细分时间；
- 二维/风格化元素放在 **card（面片）** 上，做**自发光**，从主场景光里独立出来（Unreal/实时项目）；
- "把速度作弊到面向相机的平面"以保住剪影可读性。

对本作：**碎块要"由整到碎"地细分消散，而不是简单缩小淡出**；闪光/高光片用自发光 unlit 材质，不依赖场景光或后处理。

### 2.6 引擎侧：单次爆发，而不是持续发射

Unity VFX Graph 文档把爆发定义为 **Single/Periodic Burst**（瞬间生成一批粒子），与 Constant Rate 相对（[Spawn Blocks](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.2/manual/Spawn.html)、[Visual Effect Graph 总览](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.7/manual/index.html)）。粒子系统主模块则用 **Start Lifetime / Start Speed / Start Size / Start Color** 等描述每个粒子的出生参数（[Particle System Main module](https://docs.unity.com/en-us/engine/6000.3/manual/visual-effects/particle-systems/particle-system-modules/part-sys-main-module)）。

**重要工程约束：本仓库 `Packages/manifest.json` 没有安装 `com.unity.visualeffectgraph`**，只有粒子系统模块与手写 URP shader。所以上面的 VFX Graph 文档只能作为"单次 burst"的语义参考；实现必须落在 **粒子系统 + 手写网格 + 手写 unlit shader** 上（与现有 `CannonBurst` 一致）。

### 2.7 性能与过绘制：先看 URP 资产，再谈粒子数

Unity 官方 [配置 URP 以获得更好的性能](https://docs.unity3d.com/cn/current/Manual/urp/configure-for-better-performance.html) 建议：优先关掉不需要的 MSAA、Depth/Opaque Texture、HDR、软阴影与多余相机；开 SRP Batcher；PC 上可用 Depth Priming 减少被遮挡像素的着色。

本项目的实际配置（[PC_RPAsset.asset](../../../Assets/Settings/PC_RPAsset.asset)、[Match.unity](../../../Assets/SnowballArena/Scenes/Match.unity)）：

- 主相机：透视，**竖直 FOV 34°**，俯角 **60°**，位置 `(0, 37.19, −22.25)`，near 0.15 / far 260，culling mask `4294967291`（排除远景 layer 2）。
- URP：HDR **开**、`m_MSAA: 1`（**关**）、Render Scale 1、SRP Batcher **开**、软阴影 **开**、主光阴影 2048、阴影距离 120。
- 主相机 `UniversalAdditionalCameraData`：后处理 **开**、抗锯齿 **SMAA (2)**、需要深度纹理。
- 但全局 [DefaultVolumeProfile.asset](../../../Assets/Settings/DefaultVolumeProfile.asset) 里 **Bloom 强度 = 0、Tonemapping = None** —— 等于**没有可用光晕**。

结论：爆裂的亮度必须**自发光硬写**；过绘制预算应按"3 个炮台、最多 3 个并发命中"来算，而不是按 16 颗球；单个爆裂的屏幕覆盖建议 **≤ 2–3%**（见 4.3）。

### 2.8 打击停顿与镜头：短促型，不要拖

Scitepress 的 [An Evaluation Research on Dynamic Hit Stop Using Eye Gaze](https://www.scitepress.org/publishedPapers/2024/124614/pdf/index.html) 在 VR 动作游戏里测了 0.0–0.7 s 的顿帧，得到"舒适均值约 0.39 s"的边界结论。注意语境差异：那是"敌人消失动画被拉长"的长顿帧；本作采用的是 **20–35 ms、timeScale 0.15** 的短促型顿帧（手感上更接近格斗游戏的 micro-freeze），不应参考 0.39 s。

镜头方面，"Art of Screenshake"（Jan Willem Nijman，[Internet Archive](https://archive.org/details/the-art-of-screenshake)）与 [Vlambeer 的访谈](https://www.gamedeveloper.com/design/vlambeer-co-founder-shares-advice-on-building-better-action-games) 的核心观点是：**反馈要过量、要可读、要为"发生了什么"服务**，而不是无差别地抖屏。本作相机是固定构图（`CameraShake` 只偏移位置、不旋转），所以"重手感"只能靠 **幅度 + 时长 + 顿帧 + 特效尺度**，不能靠旋转/推拉。

### 2.9 动画原则：VFX 版的"十二原则"

VFX Apprentice 的 [FX Timing Principles](https://www.vfxapprentice.com/courses/fx-timing-principles) 把动画原则落到特效上：**帧数、straight-ahead vs keyframe、ease-in/out、anticipation、impulse & rhythm、follow-through、arcs、stretch & smear**。

映射到本作：

- **anticipation**：已由炮台预警承担，不重复；
- **follow-through**：碎块落回雪面后的二次小弹跳/雪尘（200–700 ms）；
- **arcs**：碎块必须走抛物线，不能直线飞散（现有 `CannonBurst` 已经带重力，保留该做法）；
- **stretch & smear**：C 方案的方向锥可视为 smear 的 3D 版本，但只在 0–50 ms 存在；
- **staging**：所有元素面向 60° 相机可读（见 4.4）。

GDC Vault 的 [Animation Bootcamp: The First Person Animation of 'Overwatch'](https://gdcvault.com/play/1024319/) 提供了另一条佐证：在固定视角下保持角色/事件的**剪影与可读性**优先于细节。

### 2.10 本作时间盒的数值依据

| 数值 | 用途 | 依据 |
| --- | --- | --- |
| **0–40 ms** | 闪光/核心窗口 | <100 ms 能量积分（[闪白定律](https://andrewdetwiler.com/sdf/notes/the-flash-law)）；攻击段 1–2 帧、示例 22 ms，>50 ms 变蓄力（[punch envelope](https://andrewdetwiler.com/sdf/notes/punch-envelope)） |
| **40–200 ms** | 主扩散窗口 | tau 100–150 ms 下，200 ms ≈ 1.3–2 tau，主体元素已走完约 74–86% 行程（同上） |
| **200–900 ms** | 余韵窗口 | 可见拖尾 ≈ 3 tau（300–450 ms）；本作现有 0.30 s / 0.45 s / 0.55 s 三档时长 |
| **0.10–0.22 s** | 镜头抖动时长 | 现有 0.10–0.17 s，任务给定上限 0.22 s |
| **0.07–0.30** | 镜头抖动幅度 | 现有 `shakeMinAmplitude` / `shakeMaxAmplitude` |
| **20–35 ms** | 顿帧 | 现有 `hitstopMinSeconds` / `hitstopMaxSeconds`（timeScale 0.15，批处理跳过） |
| **4.8 / 24 / 108 物理步** | 120 Hz 下的步数 | `ProjectSettings/TimeManager.asset` Fixed Timestep `0.008333334` |

---

## 3. 三套方案

三套方案共享同一套"图层 + 时间盒"骨架，只在**元素种类、方向性、能量与时长**上不同。以下 `r` = 被害雪球半径（0.85–2.5 m），半径比 `k = r / 0.85`（1.0–2.94）；具体缩放指数见 4.2。

### 3.1 A 雪雾爆裂（soft snow powder puff + shockwave ring）

**视觉意图**：雪球像一团被炮弹"吹散"的粉雪，先是一下冷白闪光，随后一团柔和的雪雾向外膨胀、上浮、散开，地面压出一圈很淡的冲击环。整体轻柔、便宜、最不抢戏。

**元素清单**

| 层 | 元素 | 数量 | 尺寸 | 运动 | 寿命 |
| --- | --- | --- | --- | --- | --- |
| L1 | 冷白闪光面片（unlit 自发光） | 1 | 直径 1.6–2.0 r | 面向相机，0–25 ms 峰值，60 ms 内归零 | 0.06 s |
| L2 | 雪雾团（面向相机软边面片） | 18–26 | 单片 0.35–0.7 r | 径向 1.2–3.0 m/s + 上浮 0.8 m/s，阻尼 | 0.30–0.50 s |
| L3 | 冲击环（地面圆环） | 1 | 外径 0.4 → 2.6 r | 25–120 ms 扩张，之后淡出；**抬高 0.04 m 避免 z-fight** | 0.35 s |
| L4 | 低空雪尘 | 8–12 | 0.4–0.8 r | 贴地外扩 0.3–0.8 m/s，缓慢淡出 | 0.35–0.50 s |

**时间包络**

| 时间窗 | 事件 | 曲线 |
| --- | --- | --- |
| 0–25 ms | 闪白起亮到峰值 | 攻击段 1–2 帧，ease-out |
| 0–60 ms | 闪白回落 | 指数，tau ≈ 20–25 ms |
| 25–200 ms | 雾团主体膨胀 | 快出慢收，scale 0.3 → 1.0（归一化） |
| 60–300 ms | 冲击环扩张并淡出 | 120 ms 到最大，再 180 ms 淡出 |
| 200–500 ms | 雾团/雪尘消散 | 幂律错峰淡出 |
| **总长** | **0.50 s** | — |

**镜头/重手感**：抖动 0.07–0.12、0.10 s；**不加顿帧**（保留"轻"的定位）；音频在现有 0.55 s 爆炸上叠一层柔化雪爆。

**失败模式**

- 白雾压在白雪台上，**对比度最低**，16 球同屏时容易和滚动雪雾、地形雾气混淆；
- 剪影变化小，80 ms 后画面趋于"一团白"，读不出"碎"；
- 若不加大尺度，2.5 m 大球会显得"只冒了一小股烟"；
- 冲击环在 60° 俯视下被压成约 1:2 椭圆（见 4.4），环形信息损失。

### 3.2 B 冰晶碎裂（冰晶碎块 + 微光 + 亮闪）——推荐

**视觉意图**：雪球被炮弹从内部"炸开"：先是 2–3 帧的高亮冷白闪光，紧接着球体自身的低多边形壳裂成 8–14 块冰晶碎块，沿抛物线翻飞、带小光点，底部同时有一层雪雾托住剪影；碎块在余韵段由整到碎、逐渐缩小消失。**信息重点：这颗球碎了。**

**元素清单**

| 层 | 元素 | 数量 | 尺寸 | 运动 | 寿命 |
| --- | --- | --- | --- | --- | --- |
| L1 | 高亮闪白面片（可 HDR >1） | 1 | 直径 2.0–2.4 r | 0–30 ms 峰值，80 ms 归零 | 0.08 s |
| L2 | **雪球壳碎块**（复用 `IceFragment` / 雪球切面网格） | 8–14（随 r 增至 14） | 0.18–0.45 r | 初速 2.5–5.5 m/s，方向按索引黄金角 + 向上偏置，重力 1.2–1.6，带自转 | 0.40–0.70 s |
| L3 | 微光片（unlit 自发光小面片） | 6–10 | 0.05–0.10 r | 随碎块飞出，起亮后指数衰减，不做反复通断 | 0.12 s |
| L4 | 雪雾底层（托剪影） | 10–16 | 0.3–0.6 r | 径向 1.0–2.2 m/s，上浮 0.6 m/s | 0.30–0.45 s |
| L5 | **身份色碎光**（取被害球的描边色） | 2–4 | 0.10–0.18 r | 与 L2 同轨迹，但材质用 `SnowballIdentityOutline` 色 | 0.50–0.70 s |
| L6 | 地面雪尘环 | 1 | 外径 0.5 → 2.2 r | 抬高 0.04 m，180 ms 扩张后淡出 | 0.40 s |

**时间包络**

| 时间窗 | 事件 | 曲线 |
| --- | --- | --- |
| 0–30 ms | 闪白峰值（1–2 帧攻击段） | 指数衰减，tau ≈ 25 ms |
| 0–40 ms | 壳"裂开"：碎块从球心瞬时获得初速 | 速度即给，位置随后积分 |
| 40–220 ms | 碎块主扩散，微光起亮后衰减 | 速度 × 阻尼衰减，重力接管 |
| 200–700 ms | 碎块由整到碎：先细分/缩小，再淡出 | 幂律错峰，禁止整体线性淡出 |
| 200–450 ms | 雪雾底层消散 | 幂律 |
| **总长** | **0.75 s** | — |

**镜头/重手感**：抖动 0.10–0.16、0.12 s；顿帧 **20–35 ms**（取 25–30 ms）；音频 = 现有爆炸 + 冰晶细碎声（高频、短、可复用现有 impact 素材变调）。

**失败模式**

- 碎块与炮弹自身的冰晶余烬（`ImpactB.prefab` 的 IceFragment/EmberFragment）**同源同材质**，若不加身份色/尺寸区分，会读成"炮弹又炸了一次"；
- 碎块数量随 r 线性增长会让 2.5 m 大球在 16 球局里变成"一场小冰雹"，遮挡邻球；
- 无 Bloom 时，微光片必须靠高亮度和极小面积"骗"出闪烁，不能靠光晕；
- 碎块落地若无二次弹跳/雪尘，会"凭空消失"。

### 3.3 C 雪崩冲击（沿炮弹方向的方向锥 + 宽地浪 + 更重镜头）

**视觉意图**：把炮弹的动能"推"进雪球：闪光之后，沿炮弹飞行方向甩出一个短促的锥形冲击（smear 的 3D 版本），雪球像被铲飞一样朝同方向炸开，地面荡开一圈更宽的地浪，镜头给一次更重的踢击。强调方向与力量。

**元素清单**

| 层 | 元素 | 数量 | 尺寸 | 运动 | 寿命 |
| --- | --- | --- | --- | --- | --- |
| L1 | 闪光 | 1 | 2.2–2.6 r | 0–40 ms | 0.09 s |
| L2 | **方向锥**（半透明 unlit 网格，沿炮弹方向） | 1 | 长 2.2–2.6 r，半角 32° | 0–50 ms 从 0.2 → 1.0 展开，180 ms 淡出 | 0.20 s |
| L3 | 定向碎块 + 雪团 | 10–16 | 0.15–0.4 r | 速度沿锥轴偏置 + 30° 内散开，重力 1.4 | 0.45–0.70 s |
| L4 | **宽地浪**（环/圆盘组合，抬高 0.06–0.10 m） | 1 环 + 1 盘 | 外径 0.8 → 3.2–3.6 r | 30–180 ms 扩张，400 ms 淡出 | 0.40 s |
| L5 | 长尾雪尘/雾带 | 12–20 | 0.4–0.9 r | 沿方向拖尾 0.5–1.5 m/s，缓慢上浮 | 0.50–0.90 s |

**时间包络**

| 时间窗 | 事件 | 曲线 |
| --- | --- | --- |
| 0–40 ms | 闪光 + 锥起步 | 锥 scale 0.2 → 0.6 |
| 30–180 ms | 地浪扩张 | 快速外扩，指数减速 |
| 40–250 ms | 定向碎块与雪团 | 沿轴初速 3.5–6.0 m/s |
| 250–900 ms | 雪尘拖尾沉降 | 慢速淡出，最长尾 |
| **总长** | **0.90 s** | — |

**镜头/重手感**：抖动 0.18–0.30、0.16–0.22 s（贴现有上限）；顿帧 20–35 ms（偏上限 30–35）；音频 = 现有爆炸 + 低频延时尾（可复用 `cannon_explosion.wav` 尾音）。

**失败模式**

- 方向锥是**白色半透明体**，在 60° 俯视下与炮弹拖尾、炮口闪光语义重叠，容易被读成"炮又开了一炮"；
- 0.90 s 的长尾在 16 球局里**遮挡邻球**，且 3 个炮台连续命中会持续糊屏；
- 宽地浪在 60° 俯视下被压成椭圆，"宽"的信息损失大半；
- 更重的镜头/顿帧与现有"球撞球重击"奖励阈值（0.90）混淆，削弱"球撞球"的稀有感；
- 若锥体网格面向不随相机，低多边形切面的硬边会在 60° 下出现刺眼的直边。

### 3.4 横向对比

| 维度 | A 雪雾爆裂 | B 冰晶碎裂 | C 雪崩冲击 |
| --- | --- | --- | --- |
| 语义（"雪球碎了"） | 中（像吹散） | **高** | 中（像被铲飞） |
| 60° 俯视可读性 | 低（白对白、体积软） | **高**（硬边剪影、垂直抛散） | 中（扁平地浪被压扁） |
| 16 球辨识度 | 低 | **高**（身份色碎光） | 中（长尾遮挡） |
| 与现有资源复用 | 高（雪雾材质） | **最高**（`ImpactB` 网格/材质/`CannonBurst` 逻辑） | 中（需新锥体/地浪网格） |
| 性能/过绘制 | **最低** | 低 | 中 |
| 实施成本 | 低 | 低–中 | 中 |
| 风格匹配（低多边形粉彩） | 中 | **高** | 中 |
| 与炮台语义冲突 | 低 | 中（需身份色区分） | **高**（像炮口冲击） |
| 失败风险 | 中 | **低** | 高 |

---

## 4. 代码与工程约束

### 4.1 纯视觉边界

- 特效对象：`MonoBehaviour` + `MeshFilter/MeshRenderer` 或 `ParticleSystem`，**无 Collider、无 Rigidbody、无 AudioSource 之外的游戏组件**。
- 不写 `SnowballBody`/`MatchController` 的规则字段；不参与命中判定；不改 `Eliminate` 的语义。
- 音效只走现有的 `ArenaFeedback` 通道（3D 定位、10 路 voice 池）。
- 允许的代码改动仅限：`CannonDirector.EmitImpact` 的**签名扩展/调用参数**、新特效类、`CannonDirector.Stop()` 的**视觉效果清理时机**、以及新增的 `ArenaFeedback` 视觉/手感入口。以上均不改变玩法结果。

### 4.2 尺寸自适应

- `SnowballSettings` 没有最大半径（[DefaultSnowball.asset](../../../Assets/SnowballArena/Configs/DefaultSnowball.asset)：`initialRadius 0.85`、`volumePerMetre 0.16`），任务口径按 **0.85–2.5 m** 设计；实现时用 `victim.body.Radius` 实时取值，不写死。
- 缩放策略：
  - **碎块/闪光/身份碎光：线性 ∝ r**（它们就是雪球本体，体积必须跟着球走）；
  - **雪雾/雪尘/地浪：∝ r^0.8，并设硬上限**（例如云团直径 ≤ 8 m），防止大球把屏幕糊死；
  - 数量：碎块 8 → 14（随 r 增加），雾片 10 → 26，**总粒子数设上限 300/次**。
- 参考值（碎块/闪光按 `r` 线性，雾/环按 `(r/0.85)^0.8`）：r=0.85 → 闪光 1.9 m、碎块 0.15–0.38 m；r=2.5 → 闪光 5.5 m、碎块 0.45–1.1 m。
- 特效**中心取雪球球心**（`victim.body.RigidBody.position`），不是弹道接触点：因为被移除的是整颗球，爆点应在球心，球心位置在 `Eliminate` 之前仍可读。

### 4.3 16 球同屏的可读性预算

按主相机 43.3 m 物距、竖直 FOV 34° 估算：画面竖直覆盖约 **26.5 m**，1080p 下约 **40.7 px/m**。

| 雪球半径 | 直径（m） | 屏幕直径（px） | 屏幕面积占比 |
| --- | --- | --- | --- |
| 0.85 m | 1.70 | ≈ 69 | ≈ 0.18% |
| 1.50 m | 3.00 | ≈ 122 | ≈ 0.56% |
| 2.50 m | 5.00 | ≈ 204 | ≈ 1.58% |

设计推论：

- 特效包围盒至少 **≈ 1.2–1.5 倍球直径**，否则小球"看不见"；建议闪光直径 **2.0–2.4 r**（取 2.2 r 时，0.85 m 球约 76 px、2.5 m 球约 224 px）。
- 单次爆裂覆盖 **≤ 2–3% 屏幕**；16 颗球全部满尺寸时也才占约 25% 屏幕面积，特效必须**短、局部、有对比**，不能全屏。
- 玩家与机器人共 16 个身份色（`SnowballAppearance.Palette`，16 项），身份色碎光是最便宜的"这是几号球"提示。

### 4.4 60° 固定相机的画面几何

- 主相机俯角 60°（旋转四元数 `(0.5, 0, 0, 0.866)`），到平台中心约 **43.3 m**。
- 地面上的圆，在画面里沿景深方向被压缩为 **cos 60° = 0.5**，即近似 **1:2 的椭圆**。
- 因此：**平铺地面环（冲击环/地浪）的信息量只有一半**；竖向抛散的碎块、上浮的雾团、面向相机的面片可读性最好。
- 地面层必须抬高避免 z-fighting：现有 `CannonWarningVisual` 的经验值是**雪面上方 0.035 m**，新地浪建议 **≥ 0.04 m**。
- 固定构图意味着**不能用旋转/推拉表达重量**；只能靠幅度、时长、顿帧和元素尺度（`CameraShake` 只改 `localPosition`，且 `offset.y *= 0.35`）。

### 4.5 材质与风格（URP 低多边形粉彩）

- 现状：主相机后处理开，但全局 profile **Bloom 强度 0、Tonemapping None**；HDR 开、MSAA 关、SMAA 开。→ **不要依赖 Bloom 提亮**，闪光用 unlit 自发光（HDR 颜色可 >1，但最终会 clamp，靠面积与对比取胜）。
- 复用资源：`IceFragment` / `EmberFragment` 网格、`CannonBurst` 的确定性抛散逻辑、`IceRim` / `IceEdge` / `Snowball` / `ReferencePackedSnow` 材质、`SnowballIdentityOutline` 的描边色。
- 颜色语言：**雪球爆裂用冷白 + 冰蓝**（与雪台同族但更冷、更亮），**不要用炮台的橙红信号色**，否则会被读成"炮又开火了"。身份色只做小面积碎光。
- 对比度兜底：雪台接近白色，建议雾团底层用略偏冷灰蓝的色调（例如线性空间下亮度低 10–18%），或加一层极淡的接触阴影盘（alpha ≤ 0.15），保证在白背景上仍有形状。
- 风格化手法优先：硬边切面、由整到碎的剪影细分、自发光小面片；避免依赖密集贴图与软粒子（本作未启用软粒子）。

### 4.6 确定性（Editor 批处理可渲染）

本仓库的自动化验收用 `--cannon-smoke` 等在**批处理模式**下按固定物理步推进并截图，所以：

- 特效必须由 **`Advance(float seconds)` 显式步进**（与 `CannonBurst` 一致），**不能用 `Update()` + `Time.deltaTime`**，也不能依赖 `ParticleSystem` 的自动模拟做关键画面。
- 随机数必须**可复现**：用索引哈希/黄金角（现有 `2.399963f`）或固定种子；禁止在关键帧里用 `UnityEngine.Random`。
- `CameraShake` 内部用 `Random.insideUnitSphere`，批处理截图时会导致像素抖动 → 建议 `Application.isBatchMode` 下改用确定性包络或直接跳过震屏。
- `ArenaFeedback.Hitstop` 已在批处理模式跳过（`if (Application.isBatchMode) return;`），保持不变。
- 可复用的做法：纯网格碎块（`CannonBurst`）+ 可选粒子只用于"实机模式"的底层雪雾；两者的时间都由同一个 `age` 驱动。

### 4.7 生命周期与清理（含一个现有隐患）

现状：`CannonDirector.Stop()` 会 `Destroy` 所有 `bursts`；`ResetRound()` 调 `Stop()`；结算 `ResolveEliminations` 进入 `Results` 时也调 `Cannon.Stop()`。

**隐患（实测代码路径）**：`MatchController.FixedUpdate` 先 `Cannon.Advance()`（生成命中爆裂），紧接着 `ResolveEliminations()`；若这次命中正好淘汰掉最后一名对手，`Phase` 立刻变 `Results` 并 `Cannon.Stop()` → **同帧刚生成的爆裂被立即销毁，玩家看不到终局那一击**。现有 `CannonBurst` 已经受影响。

建议（仅视觉，不改规则）：把 `Stop()` 拆成 **`StopSpawning()`（立即停止生成/清炮台）** 与 **视觉清理（让已有爆裂跑完寿命或延迟 1–2 帧）**；或在 `Stop()` 里只清 `Stage != Inactive` 的炮台，把 `bursts` 交给一个独立的 `effectsHost`，在 `Results` 阶段继续 `Advance` 到寿命结束。无论哪种，`ActiveEffectCount` 必须在寿终后归零，否则现有 smoke 断言会失败。

### 4.8 资源与命名建议

| 资源 | 建议 |
| --- | --- |
| 预制体 | `SnowballBurstB.prefab`（主）、`SnowballBurstA/C.prefab`（预设），或一个预制体 + `SnowballBurstStyle` 枚举 |
| 脚本 | `Scripts/Presentation/SnowballBurst.cs`（`Lifetime`、`fragments`、`Advance(seconds)`） |
| 材质 | `SnowballBurstFlash.mat`（unlit additive）、`SnowballBurstPowder.mat`（unlit alpha）、`SnowballBurstRing.mat`（unlit alpha） |
| 网格 | 复用 `IceFragment`/`EmberFragment`/`FacetedSnowball`；新增仅锥体/环形面片 |
| 不新增 | 包依赖、渲染管线设置、场景 Volume、Layer |

---

## 5. 推荐方案与实施计划

### 5.1 为什么是 B

1. **语义最正确**：雪球由雪、冰晶切面、身份描边三层构成；碎成冰晶块是"同一套几何语言"的解体，玩家不需要学习新符号。
2. **60° 俯视下最可读**：碎块是硬边低多边形、垂直抛散，不会被地面压扁；A 的雾团和白台同色，C 的地浪被压成 1:2 椭圆。
3. **白背景上对比最好**：冰蓝切面 + 身份色碎光在白雪台前有冷暖/明度差；且**不依赖 Bloom**（Bloom 强度为 0）。
4. **复用度最高**：`ImpactB` 的 12 片网格与材质、`CannonBurst.Advance` 的确定性抛散、身份色材质的取色方式都可直接复用，新增代码最少。
5. **风险可控**：若 16 球时过花，只需关掉碎块层即降级为 A（同一骨架），不需要第二套实现。
6. **重手感可后置**：B 的顿帧取 25–30 ms，不上 C 的 30–35 ms，避免与"球撞球重击"的稀有奖励冲突。

### 5.2 图层与顺序

按"先发生的先画、后画的在上"组织；每层共享同一条 `envelope(t)`：

| 顺序 | 层 | 内容 | 时间 | 说明 |
| --- | --- | --- | --- | --- |
| 0 | 音频 | 现有 `CannonExplosion` 0.55 s | 0–550 ms | 不改 |
| 1 | L1 闪光 | 冷白/unlit 面片（可 HDR） | 0–80 ms | 覆盖"球消失"的那一帧 |
| 2 | L2 炮弹残骸 | 现有 `CannonBurst`（12 片、0.30 s、按 0.3 m 缩放）作为子层 | 0–300 ms | 保留"是炮弹打的"信息 |
| 3 | L3 雪球碎块 | 8–14 片，线性 ∝ r | 0–700 ms | 主角 |
| 4 | L4 雪雾底层 | 10–16 片，∝ r^0.8 | 0–450 ms | 托剪影、补体积 |
| 5 | L5 地面环 | 抬高 ≥0.04 m | 0–400 ms | 压扁可接受，仅作陪衬 |
| 6 | L6 身份碎光 | 2–4 片，被害球描边色 | 0–700 ms | 16 球辨识 |
| 7 | 镜头/顿帧 | `CameraShake.Shake` + `ArenaFeedback.Hitstop` | 0.10–0.16 s / 25–30 ms | 批处理下跳过震屏与顿帧 |

更新顺序：`envelope` 先算 → 闪光 → 残骸 → 碎块 → 雾 → 环 → 身份碎光 → 相机/顿帧；**统一在命中那一帧生成**，之后只由 `Advance(seconds)` 推进。

### 5.3 实施步骤

1. **Block-in（灰模）**：用灰色球/方块按 0–40 / 40–200 / 200–900 三段做时间验证，先确定节奏，再上材质（这正是 VFX Apprentice 建议的流程）。
2. **接入点**：扩展 `CannonDirector.EmitImpact`，传入 `victim.body.RigidBody.position`、`victim.body.Radius`、炮弹方向、样式枚举；保持"先 EmitImpact 再 Eliminate"的顺序。
3. **新类**：`SnowballBurst` 复用 `CannonBurst` 的 `fragments`/`Advance` 模式，增加 `style` 与 `radius`；加入 `bursts` 列表，`ActiveEffectCount` 自动统计。
4. **清理修复**：拆分 `Stop()` 的"停止生成"与"视觉清理"，保证终局一击能渲染完、且寿终后 `ActiveEffectCount == 0`。
5. **手感**：只在 B 预设启用 25–30 ms 顿帧与 0.10–0.16 s 震屏；批处理跳过。
6. **极端尺寸**：分别用 r=0.85 / 1.5 / 2.5 截图核对屏幕占比与遮挡。
7. **16 球压力**：1+15 机器人跑 30 s，截图检查遮挡、身份辨识、是否有残留。
8. **验收**：跑 `--cannon-smoke`（44 项）确认无回归；补充特效寿命/清理断言；截图归档到 `TestResults`。

### 5.4 默认参数（建议值，可调）

| 参数 | 建议 |
| --- | --- |
| `FlashDuration` | 0.06–0.08 s（峰值 0–30 ms） |
| `FlashDiameter` | 2.0–2.4 r |
| `ChunkCount` | 8（r=0.85）→ 14（r=2.5），线性插值 |
| `ChunkSize` | 0.18–0.45 r |
| `ChunkSpeed` | 2.5–5.5 m/s |
| `ChunkGravity` | 1.2–1.6（`Vector3.down` 乘系数） |
| `ChunkLifetime` | 0.40–0.70 s |
| `PowderCount` | 10–16（∝ r^0.8） |
| `PowderLifetime` | 0.30–0.45 s |
| `RingDiameter` | 0.5 → 2.2 r |
| `IdentityShardCount` | 2–4 |
| `Shake` | 0.10–0.16，0.12 s |
| `Hitstop` | 0.025–0.030 s |
| `TotalLifetime` | 0.75 s（B）/ 0.50 s（A）/ 0.90 s（C） |

### 5.5 验收清单

- [ ] 三方案均可通过枚举切换，**默认 B**。
- [ ] 0–40 / 40–200 / 200–900 三段在 60 fps 逐帧可辨；闪白峰值出现在第 1–2 帧。
- [ ] r=0.85 与 r=2.5 两档都成立，特效直径 ≈ 2.0–3.0 倍球直径。
- [ ] 16 球同屏不遮挡关键操作信息；能指出"哪颗球被击碎"。
- [ ] 无 Bloom 时依然成立；无全屏闪光；重复闪烁 ≤ 3 Hz。
- [ ] 无碰撞体/无伤害/无玩法变化；`--cannon-smoke` 44 项通过。
- [ ] 结算、重开、返回菜单、对局结束均无残留（`ActiveEffectCount` 归零）。
- [ ] 终局一击可见（4.7 的清理隐患已修）。
- [ ] 批处理模式下画面可复现（无 `UnityEngine.Random` 参与关键画面）。

---

## 6. 如何审阅预览

预览文件将由 Lead 放入本目录 `docs/art/cannon-snowball-explosion/`（本文只定义预期与标准，不预先声称文件存在）。

### 6.1 预期文件

| 文件 | 用途 |
| --- | --- |
| `00-comparison.jpg/gif` | A/B/C 同镜头横向对比 |
| `A/B/C-design-board.jpg` | 分镜/元素拆解图板（参考 [暗色 B 大炮与炮弹](../selected-cannon-b/README.md) 的组织方式） |
| `A/B/C-gameplay60.png` | **60° 实机相机**下的画面（最重要的审阅依据） |
| `A/B/C-motion.mp4` 或 `.gif` | 减速/逐帧，便于核对三段时序 |
| `render-report.txt` / `validation.txt` | 渲染参数、镜头、耗时、检查项 |
| `README.md` | 选型结论与接入说明 |

### 6.2 审阅方法

1. **只看 60° 实机相机图**，不看工作室特写；特写只能看材质与切面。
2. **逐帧核对**：闪白是否在 2–3 帧内到峰值；200 ms 时主体是否已扩散到位；700–900 ms 是否干净收尾。
3. **两个极端尺寸**：r=0.85 与 r≈2.5 各看一遍。
4. **16 球局**：1+15 机器人截图，检查遮挡、身份辨识、与炮台橙色信号的混淆。
5. **可读性体检**：关掉 Volume/后处理后是否依然成立；灰度化后剪影是否清楚；是否有全屏闪白。
6. **清理**：对局结束/重开后截图，确认没有残留特效。

### 6.3 通过标准

- 3 秒内能说出"**哪颗球、被什么打碎、从哪个方向来**"；
- 命中瞬间既能盖住"球消失"的突兀，又不糊住邻球；
- 与现有 0.30 s 冰晶余烬、0.45 s 雪雾、0.10–0.17 s 震屏、20–35 ms 顿帧的语言一致；
- 低多边形粉彩风格统一，无写实烟雾/贴图感。

---

## 7. 风险与开放问题

| # | 风险 | 影响 | 缓解 |
| --- | --- | --- | --- |
| 1 | **终局一击被 `Cannon.Stop()` 同帧销毁** | 结算前最后一击看不见 | 拆分"停止生成"与"视觉清理"（4.7） |
| 2 | 白对白，对比度不足 | 特效"发灰" | 冷灰蓝底层 + 身份色碎光 + 轻微接触阴影；灰度化验收 |
| 3 | 无 Bloom/Tonemapping | 亮度峰值不够 | 自发光面片、小面积高亮、0–40 ms 短促峰值 |
| 4 | 2.5 m 大球特效过大 | 遮挡邻球、16 球局糊屏 | 雾/环 ∝ r^0.8 + 硬上限；碎块数量封顶 14 |
| 5 | 与炮弹自身冰晶余烬同材质 | 读成"炮弹又炸了" | 尺寸/数量/身份色区分；`CannonBurst` 缩到 0.3 m 子层 |
| 6 | 非确定性（`Random`/自动模拟/震屏） | 批处理截图不可复现 | 全部走 `Advance(seconds)`；固定种子；批处理跳过震屏 |
| 7 | 顿帧叠加/帧率变化 | 连续命中时节奏拖沓 | 沿用"只延长不叠加"的现有逻辑；上限 35 ms |
| 8 | 60° 下地面环被压成 1:2 椭圆 | "冲击波"信息损失 | 抬高环、增加竖向元素、以碎块/雾为主体 |
| 9 | 三方案并行预览时混淆 | 选型沟通成本 | 统一构图/构图标注 A/B/C，同一相机同一帧 |
| 10 | 未来球最大半径继续增长 | 规格失效 | 参数全部由 `victim.body.Radius` 推导，不写死 |

开放问题（需 Lead/用户定）：

- B 的顿帧是否只用于玩家被击中或玩家击碎对手？当前建议"所有炮弹击杀统一 25–30 ms"。
- 是否保留现有 0.30 s `CannonBurst` 作为子层？建议保留（"炮弹残骸"），但缩到 0.3 m。
- 终局一击是否要额外加强（例如 C 的锥体只在最后一击出现）？建议先不做，等 16 球可读性验证后再定。

---

## 附录 A：参考来源

| # | 标题 | 链接 | 本文用于 |
| --- | --- | --- | --- |
| 1 | The Soul of Effects: What is Timing in VFX?（VFX Apprentice） | [vfxapprentice.com](https://www.vfxapprentice.com/blog/the-soul-of-effects-what-is-timing-in-vfx) | wind-up/climax/fall-off、impulse vs rhythm、心跳节奏 |
| 2 | FX Timing Principles（VFX Apprentice，Dan Elder） | [vfxapprentice.com](https://www.vfxapprentice.com/courses/fx-timing-principles) | 帧数、ease、anticipation、follow-through、arcs、stretch & smear |
| 3 | The shape of a punch（Andrew Detwiler） | [andrewdetwiler.com](https://andrewdetwiler.com/sdf/notes/punch-envelope) | 指数衰减、tau 100–150 ms、1–2 帧攻击段、tail≈3 tau、作者用秒 |
| 4 | The hundred millisecond flash law（Andrew Detwiler） | [andrewdetwiler.com](https://andrewdetwiler.com/sdf/notes/the-flash-law) | <100 ms 能量积分、1 帧需 6 倍强度、3 Hz 闪烁安全 |
| 5 | Real Time VFX — 关于 timing 的讨论 | [realtimevfx.com](https://realtimevfx.com/t/are-there-any-experienced-vfx-artists-here-who-excel-at-timing/28575/6) | 从业者对 timing 的共识（主次元素配合） |
| 6 | Unity — Visual Effect Graph 文档 | [docs.unity3d.com](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.7/manual/index.html) | 事件式特效、可复用子图（语义参考；本项目未装该包） |
| 7 | Unity — VFX Graph Spawn Blocks | [docs.unity3d.com](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.2/manual/Spawn.html) | Single Burst vs Constant Rate |
| 8 | Unity — Particle System Main module | [docs.unity.com](https://docs.unity.com/en-us/engine/6000.3/manual/visual-effects/particle-systems/particle-system-modules/part-sys-main-module) | Start Lifetime/Speed/Size/Color 等模块语义 |
| 9 | Unity — 配置 URP 以获得更好的性能 | [docs.unity3d.com](https://docs.unity3d.com/cn/current/Manual/urp/configure-for-better-performance.html) | MSAA/HDR/Depth/Opaque/SRP Batcher/Depth Priming 取舍 |
| 10 | VFX Voice — The Return of Hand-Drawn and Stylized Effects Animation | [vfxvoice.com](https://vfxvoice.com/the-return-of-hand-drawn-and-stylized-effects-animation/) | 分层色调、剪影细分消散、card 自发光、速度作弊到相机平面 |
| 11 | CEDEC 2026 — 艺术与游戏设计之间的 VFX 设计（GBF Relink） | [cedec.cesa.or.jp](https://cedec.cesa.or.jp/2026/timetable/detail/s698020b3129f2/) | VFX 作为"辅助信息"、空间/能量/时间三轴信息密度 |
| 12 | 80.lv — Chilling Ice & Frost VFX | [80.lv](https://80.lv/articles/check-out-these-chilling-ice-frost-vfx-unveiled-by-3d-artist) | 风格化冰晶/霜花 VFX 的观感参考 |
| 13 | GameRes — 游戏体验中的三层反馈结构 | [gameres.com](https://www.gameres.com/819894.html) | 物理反馈层、能量引导视线、夸张与对比 |
| 14 | Tomizawa & Ishikawa 2024 — An Evaluation Research on Dynamic Hit Stop Using Eye Gaze | [scitepress.org](https://www.scitepress.org/publishedPapers/2024/124614/pdf/index.html) | 顿帧舒适边界实验（0–0.7 s，均值 ≈0.39 s，语境不同） |
| 15 | GDC Vault — Animation Bootcamp: The First Person Animation of 'Overwatch' | [gdcvault.com](https://gdcvault.com/play/1024319/) | 固定视角下的剪影与可读性优先 |
| 16 | The Art of Screenshake（Jan Willem Nijman） | [archive.org](https://archive.org/details/the-art-of-screenshake) | 反馈过量但可读、为"发生了什么"服务 |

## 附录 B：本仓库依据速查

| 依据 | 位置 |
| --- | --- |
| 命中链路与 `EmitImpact` | [CannonDirector.cs](../../../Assets/SnowballArena/Scripts/Arena/CannonDirector.cs) |
| 立即隐藏雪球 | [MatchController.cs](../../../Assets/SnowballArena/Scripts/Core/MatchController.cs) `Eliminate` |
| 0.30 s 冰晶余烬 / 12 片 | [CannonBurst.cs](../../../Assets/SnowballArena/Scripts/Presentation/CannonBurst.cs)、[CannonAssetBuilder.cs](../../../Assets/SnowballArena/Editor/CannonAssetBuilder.cs) |
| 0.45 s 雪雾 / 震屏 / 顿帧 | [ArenaFeedback.cs](../../../Assets/SnowballArena/Scripts/Presentation/ArenaFeedback.cs)、[CameraShake.cs](../../../Assets/SnowballArena/Scripts/Presentation/CameraShake.cs) |
| 碰撞手感/挤压 | [SnowballFeedback.cs](../../../Assets/SnowballArena/Scripts/Presentation/SnowballFeedback.cs) |
| 16 身份色 | [SnowballAppearance.cs](../../../Assets/SnowballArena/Scripts/Presentation/SnowballAppearance.cs) |
| 0.85 m 初始半径、无上限 | [DefaultSnowball.asset](../../../Assets/SnowballArena/Configs/DefaultSnowball.asset)、[SnowballSettings.cs](../../../Assets/SnowballArena/Scripts/Snowball/SnowballSettings.cs) |
| 1/120 物理步 | [TimeManager.asset](../../../ProjectSettings/TimeManager.asset) |
| 60°/FOV34 相机、后处理开关 | [Match.unity](../../../Assets/SnowballArena/Scenes/Match.unity) |
| Bloom 0 / Tonemapping None / MSAA 关 | [DefaultVolumeProfile.asset](../../../Assets/Settings/DefaultVolumeProfile.asset)、[PC_RPAsset.asset](../../../Assets/Settings/PC_RPAsset.asset) |
| 44 项炮弹断言 | `Assets/SnowballArena/Scripts/Core/CannonSmokeProbe.cs`、[README](../../../README.md) |
