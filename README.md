# 雪山滚雪球

当前里程碑：执行规划的步骤 1～5，滚动成长与碰撞练习原型。

## 打开与运行

1. 用 Unity **6000.3.28f1** 打开本工程。
2. 打开 `Assets/SnowballArena/Scenes/Arena.unity`。
3. 点击 Play，再点击 Game 画面使其获得键盘焦点。
4. **WASD** 控制屏幕方向；**R** 重置练习。掉出平台后也可以点击“重新练习”。

移动依赖 Input System，项目已启用该系统，不需要改成旧 Input Manager。
如果画面仍是模板场景，请打开 Arena；原 SampleScene 保留不变。

## 本阶段范围

- 圆形实体平台、斜俯视镜头、无人物的雪球。
- 刚体受力移动、接触摩擦带动球体旋转、地面阻力和自然掉落。
- 画面方向的 WASD 输入，斜向归一化；空中不提供驱动力。
- 练习重置与掉落提示。
- 按有效滚动距离增加体积，更新质量和实心球转动惯量；没有玩法大小上限。
- 绿色同尺寸、橙色较大尺寸的被动练习球，可被推动并在滚动时成长。
- 显示玩家半径、质量和有效滚动距离；R 同时重置所有练习球。

尚未实现：机器人、人数选择、正式胜负结算、炮台、平台运动，以及完整雪山美术。这些按后续规划逐步接入。

## 可调参数与扩展

- `Assets/SnowballArena/Configs/DefaultSnowball.asset`：初始半径、密度、驱动力、地面阻力、每米体积增长量和支撑查询参数。
- `Assets/SnowballArena/Configs/CircularArena.asset`：初始圆形场地生成参数、出生点、掉落高度和瞄准中心。
- `Assets/SnowballArena/Materials/SnowContact.physicMaterial`：接触摩擦与恢复系数。
- `ArenaDefinition.previewRadius/thickness` 目前是生成参数。修改后不会自动改写已生成网格；现有场景的几何以 MeshCollider 为准。
- `ArenaSurface` 提供自定义中心、表面速度与机关挂载点；`ArenaQuery` 查询真实碰撞表面，不将移动或落台写死为圆形半径。
- 雪球根物体缩放保持 `(1,1,1)`；碰撞半径和视觉子物体尺寸由 `SnowballBody` 管理。
- 控制驱动力采用 `ForceMode.Force`，不在移动中直接写入速度；质量增加后同样的力自然产生较小加速度。

## 检查与构建

菜单 `Snowball Arena > Validate Prototype Physics` 运行独立物理场景检查，报告输出到 `TestResults/physics-validation.json`。在编辑模式执行。

菜单 `Snowball Arena > Build Windows Prototype` 输出开发版到 `Builds/Windows/SnowballArena.exe`。使用 Mono 后端，便于早期快速验证。必须保留整个输出文件夹，不能仅复制 exe。

`Create Prototype Scene` 用于空工程首次生成。为保护手动修改，已有 Arena 场景时它会拒绝覆盖；正常开发直接编辑现有场景和预制体。

原始工程已建立 Git 基线，Library、Temp、Logs、Builds 和验证输出均不提交。

## 后续顺序

6. 单局比赛、淘汰和结算。
7. 机器人与安全出生。
8. 人数选择与完整界面。
9. 异形平台扩展验证。
10. 雪山美术与反馈。
11. 整体验收和正式 Windows 试玩包。

## 成长与碰撞模型

- 体积增量 = 有效滚动距离 × `volumePerMetre`（默认 0.16 m³/m）；质量 = 密度 × 体积，半径由球体体积反算。
- 有效距离同时受接触面上的实际位移和角速度对应的滚动弧长限制。纯滑动、原地自转、静止和空中运动不成长。
- 采样以平台局部位置为基准；半径增大后的离地修正不计入下一步距离。重新接触平台时重新建立采样基准。
- 新雪按平台表面速度加入，线动量按 `m*v + dm*vSnow` 更新。采用对称积雪近似，新雪没有绕球心的净自转角动量。
- 转动惯量使用实心球公式 `I = 2/5*m*r²`，更新后保持原有自转角动量。
- 为防止增大后的碰撞体陷地，接地增长时球心竖直抬高半径增量。这是几何修正；不修改水平位置、不提供向上的发射速度。它不是完整积雪变形仿真。
- 碰撞交给刚体求解器，不叠加额外击飞力。动量检查在关闭驱动、地面外力和成长的隔离场景进行；真实对局还有地面与收集积雪带来的外部作用。
- 没有设置最大半径；仍受浮点数和物理引擎的有限数值范围约束。已检查半径超过平台半径时仍能继续增长，未声称无限尺寸下稳定。

## 验证入口

- `Snowball Arena > Validate Prototype Physics`：9 项移动、支撑、惯性、掉落、重置和输入方向检查。
- `Snowball Arena > Validate Growth and Collisions`：10 项碰撞、质量、成长、帧分割一致性和重置检查。
- 结果分别保存到 `TestResults/physics-validation.json` 与 `TestResults/growth-collision-validation.json`。
- 开发版可用 `--snowball-smoke <输出目录>` 自动验证虚拟键盘输入、落台和重置。正常启动不启用该工具。
- 隐藏窗口可能没有可截取的屏幕缓冲；此时输出 `*-camera.png`，只包含场景相机画面，不包含 OnGUI 界面，不能当作界面人工验收截图。
