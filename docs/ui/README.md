# 决战雪山之巅：圆笔手写 UI

采用已选定的 A 方案：雪白填充、山蓝细描边、自然圆笔字形。首页删除“滚动成长·碰撞求生”，由透明字标、两行提示和开始按钮组成；教程是一屏三张示意图；人数选择的对局按钮为“现在开始吧！”，0 人时为“开始练习”。

## 项目文件

- `Assets/Resources/UI/GameTitle.png`：内置 image_gen 生成的透明手写字标“决战雪山之巅”。原图保留透明安全边，`WinterMenuView` 用可见笔画 UV 显示，无需改动源图。
- `Assets/Resources/UI/Handwriting/`：逐项制作的六组透明手写文字图集，共 51 项，覆盖标题、按钮、教程说明、提示文案、人数数字及白色描边倒计时数字。正常界面直接显示这些字图，不使用系统字体代替设计字形。
- `HandwrittenTextAtlas.cs`：按照 `Lettering.json` 的 UV 显示原始字图；动态人数与获胜编号用手写数字拼合，范围仍为 0～15。每张字图按原始宽高比等比显示。
- `Assets/Resources/UI/LXGWMarkerGothic-Regular.ttf`：异常或未来新增文案的备用字库，不承担正常界面字形。来源 https://github.com/lxgw/LxgwMarkerGothic ，原始 OFL 1.1 授权与作者名单保存在同目录及 Windows 包的 `ThirdPartyNotices` 中。
- `Assets/Resources/Tutorial/Cannon.png`：Unity 生产模型出膛 40 ms 的 1920×1080 渲染截图，包含炮口闪光、炮身、完整炮弹，取景中没有雪球。
- `WinterMenuView.cs`：界面样式、布局、轻微手绘轮廓和快捷键键帽。
- `MatchController.cs`：保留页面流程、人数范围、倒计时、观战、胜负和获胜演出逻辑；只更换绘制入口。

`ArtSource/UI/lettering-source.json` 保存每项文字、格位和完整内置图像生成提示词；`ApprovedHandletteredStyle.png` 保存选定的风格参考。运行 `python Tools/UI/import_lettering.py` 从透明通道测量可见笔画、检查格位边界并更新 UV 元数据；脚本不改变 PNG 像素。之后再运行 Unity 构建入口。

PNG、运行时 `Lettering.json` 与对应 `.meta` 是游戏必需资源，随源码提交；构建包、验证截图、Python 缓存以及可重新生成的 `ArtSource/UI/lettering-bounds.json` 报告仅保留在本地。

颜色：正文山蓝 `(0.19, 0.37, 0.53)`，描边 `(0.34, 0.50, 0.66)`，近白底色。对局和观战不增加常驻 HUD。结算只保留“再来一局 / R”和“返回菜单 / Esc”。

## 构建与实机审阅

菜单 `Snowball Arena > Prepare Handwritten UI and Build` 配置图像导入并构建。命令行入口为 `SnowballArena.Editor.HandwrittenUiBuilder.Build`。输出仍为 `Builds/Windows/SnowballArena.exe`，窗口名为“决战雪山之巅”。

开发包参数 `--ui-review <输出目录>` 自动保存 12 张包含真实 OnGUI 层的运行截图与 `ui-review.txt`，覆盖首页、教程、人数选择（含 15 人与 0 人）、倒计时、对局、获胜演出以及四种结算（含两位数获胜编号）。正常截图的 `missingLettering` 应为 0。此模式需要可见且未最小化的游戏窗口，不要附加 `-nographics` 或隐藏窗口；推荐窗口模式运行。普通游戏不启用审阅模式。
