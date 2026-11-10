# 大炮红外警示线

分支：`feat/cannon-infrared-warning-preview`。效果已接入正式游戏，并按后续反馈增加横向颜色渐变、将宽度扩大为初版红外线的 3 倍。

此处图片直接由现有 Match 场景和 B 型大炮模型在 Unity 中渲染。预览工具与正式炮台共用 `CannonWarningVisual` 和 `CannonWarning.mat`，只创建临时对象，不保存场景。原有的大炮计时、炮弹路线与命中规则沿用原实现。

- `01-current-overview.png`：修改前的 0.60 m 实体色带，全景。
- `02-infrared-overview.png`：红外警示线，全景。
- `03-infrared-muzzle-detail.png`：红外警示线，炮口和平台衔接近景。
- `04-current-muzzle-detail.png`：相同角度的修改前效果。
- `05-runtime-warning.png`：更新后的 Windows 开发包实际炮台预警截图。

效果沿宽度从中间的大红色，自然过渡到两侧的浅红色，并在外缘透明淡出。总宽由 0.18 m 扩大至 0.54 m，其中大红细芯约 0.103 m；平铺于雪面上方 0.035 m，两端各从实际平台边界内收 0.18 m。LineRenderer 使用固定的雪面法线，不随相机翻转；Shader 使用正常深度检测，避免覆盖炮身和雪球。保留原有三次亮灭的预警节奏。

Unity 菜单：`Snowball Arena > Preview Infrared Warning`。

批处理重现：

```powershell
& 'D:/Unity/Editors/6000.3.28f1/Editor/Unity.exe' -batchmode -quit -projectPath E:/XYLGameJam/snowball-rolling -executeMethod SnowballArena.Editor.CannonWarningPreview.Render -logFile Logs/infrared-gradient-preview.log
```

正式资源：`Assets/SnowballArena/Resources/CannonWarning.mat` 引用 `Shaders/CannonWarning.shader`，确保 Windows 打包保留该 Shader；炮台符文和炮弹拖尾继续使用原有材质。

开发包回归命令：

```powershell
& './Builds/Windows/SnowballArena.exe' -batchmode --cannon-smoke E:/XYLGameJam/snowball-rolling/TestResults/InfraredGradient -logFile Logs/infrared-gradient-smoke.log
```

专项检查在原有炮击检查上增加了实际警示线网格的落地、平台内收与打包 Shader 支持验证。

验证结果：Windows 开发包构建成功；炮台专项 65 项检查全部通过。完整结果见 [validation.txt](validation.txt)。渐变版的全景、炮口近景和开发包实机图已重新渲染并目视核对：中间大红色、两侧渐变为浅红、外缘自然透明淡出，警示线只覆盖平台雪面且不穿过炮身。`05-runtime-warning.png` 来自本次 Windows 开发包冒烟运行。
