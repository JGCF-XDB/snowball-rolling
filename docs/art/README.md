# 雪山竞技场与雪球建模设定

供 Unity URP 低多边形雪球竞技游戏制作环境和雪球模型时参考。平台目标直径约 **22 m**，雪球初始半径约 **0.85 m**。

![雪山竞技场环境、模块拆分与雪球多视图建模参考](snow-arena-modeling-reference.png)

- [设定图原图](snow-arena-modeling-reference.png)：1672 × 941 像素，约 16:9 横版。
- [生成与修订提示词](snow-arena-modeling-reference-prompt.txt)：保留初始要求及两轮技术视图修订，便于后续迭代。
- 来源：2026-11-09 在项目设计对话中使用内置生图功能生成。

## 图面分区

左侧约三分之二展示竞技场完整场景、平台俯视与侧向分层参考，以及岩石、积雪、冰层、冰晶边缘、雪松和远景山峰六类模块。右侧约三分之一展示雪球的 3/4 透视、正面、侧面、背面、顶视参考，以及小、中、大三个成长阶段。

## 环境建模

- 平台中央保持宽阔、平坦、可滚动和碰撞的连续表面。环形滚动痕迹通过表面材质表现，避免形成影响玩法的凸起或障碍。
- 平台从上到下分为松软白色积雪、半透明浅蓝冰层和厚重深蓝灰岩层。各层轮廓可略有不规则，交界与厚度需要清晰可读。
- 边缘采用可复用的低多边形冰晶段，控制尖角密度，保留完整的平台轮廓。
- 雪松与孤立岩石小岛布置在竞技场外围。远景山峰采用深青蓝山体、白色雪顶和冷色雾气形成层次。
- 岩石、雪帽、冰层、冰晶段、雪松和远景山峰分别制作模块，方便组合、替换与控制场景复杂度。

## 雪球建模

- 基础形体接近球体，轮廓饱满厚重，使用自然、适度的低多边形切面，保证外观适合滚动。
- 以卷入的积雪带、轻微螺旋纹和少量附着雪块表达滚动方向；细小冰晶与浅蓝阴影作为局部细节，避免尖刺和过深裂缝。
- 各方向视图与成长阶段沿用同一套形体和材质语言。最终正交视图应从同一个三维模型导出，确保结构一致。
- 小、中、大仅表达成长的视觉变化，未指定中型和大型的固定半径。具体成长规则以游戏配置和实现为准。
- 球根物体保持 `(1, 1, 1)` 缩放；对接现有 `SnowballBody` 时由其更新球形碰撞半径与视觉子物体尺寸。视觉模型的雪块和纹理细节应围绕球形碰撞体设计。
- 不添加五官、手脚、帽子、装饰物或装备。

## 风格与交付

采用 stylized low-poly、chunky geometric shapes、clean silhouette 和 faceted surfaces，以 Unity URP 与移动端预算为目标。主色为雪白、冰蓝、深青蓝及少量蓝灰岩石色；使用冷色环境光和柔和阴影，保留轻微童话感。

积雪、压实雪球、冰和岩石需要区分 PBR 材质表现：雪偏柔软哑光，雪球更紧实，冰有适度透光感，岩石保持粗粝的块面。以清晰轮廓和大切面为主，避免用密集几何堆叠微小颗粒。

建模交付时分别组织平台结构、外围环境模块与雪球模型，保持统一单位、比例和材质命名。二维设定图用于确定美术方向，平台尺寸、碰撞与出生安全范围需在实际 Unity 场景中核对。

## 游戏内模型

正式场景为 [`Match.unity`](../../Assets/SnowballArena/Scenes/Match.unity)。已根据设定图重建可直接运行的程序化 3D 网格，并保存为 Unity 原生资源：

![正式对局中滚动和成长的雪球与雪山环境](gameplay-preview.png)

- [`ReferencePlatform.asset`](../../Assets/SnowballArena/Art/ReferencePlatform.asset)：22 m 雪台的积雪、冰层、岩层与垂挂冰晶；玩法碰撞仍使用独立的平坦平台网格。
- [`ReferenceSnowball.asset`](../../Assets/SnowballArena/Art/ReferenceSnowball.asset)：单位直径的低多边形雪球，卷雪螺旋带、雪块和冰晶合并在同一个网格中，使用两种材质。由现有 `SnowballBody` 随半径缩放。
- [`SnowCedar.prefab`](../../Assets/SnowballArena/Prefabs/SnowCedar.prefab)、[`FloatingIslet.prefab`](../../Assets/SnowballArena/Prefabs/FloatingIslet.prefab) 和 `SnowMountain0/1/2.prefab`：可复用的外围环境模块，均不带玩法碰撞体。
- [`ReferenceMeshes.cs`](../../Assets/SnowballArena/Editor/ReferenceMeshes.cs)：确定性的网格构造代码；[`ModelingReferenceBuilder.cs`](../../Assets/SnowballArena/Editor/ModelingReferenceBuilder.cs)：材质、预制体、正式场景和预览图的生成入口。

在 Unity 中执行 `Snowball Arena > Apply Modeling Reference` 可重新生成。它会更新上述生成资源与正式对局中的美术对象；再次执行会替换已有生成内容，资源 GUID 保持稳定。调整生成后的资源前，可先另存为独立资产以保留手工修改。

批处理入口 `SnowballArena.Editor.ModelingReferenceBuilder.ApplyAndBuild` 会生成资源、检查碰撞网格与出生位置、执行现有物理验证并构建 `Builds/Windows/SnowballArena.exe`。预览与检查报告输出到被 Git 忽略的 `TestResults/ModelingReference`。
