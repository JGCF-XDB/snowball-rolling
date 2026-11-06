# 雪山滚雪球

当前里程碑：执行规划的步骤 1～3，单雪球操控原型。

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

尚未实现：距离成长、机器人、人数选择、正式胜负结算、炮台、平台运动，以及完整雪山美术。这些按后续规划逐步接入。

## 可调参数与扩展

- `Assets/SnowballArena/Configs/DefaultSnowball.asset`：半径、密度、驱动力、地面阻力、支撑查询参数。
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

4. 固定质量下的双球碰撞验证。
5. 滚动距离成长、质量与惯量更新。
6. 单局比赛、淘汰和结算。
7. 机器人与安全出生。
8. 人数选择与完整界面。
9. 异形平台扩展验证。
10. 雪山美术与反馈。
11. 整体验收和正式 Windows 试玩包。
