# 缩略图引擎选择器（Thumb Picker）

> 因为电脑上装了好几个看图软件，卸来装去把 Windows 的缩略图搞失效了，jpg/png 全都只显示扩展名徽标图标，查注册表查到头大。只好借用 AI 的力量写了这个小工具，以后想换哪个软件画缩略图，点一下就行。

一个 Windows 小工具，用来**图形化选择"由哪个软件来生成缩略图"**，覆盖 jpg / png / bmp / gif / webp / tif / raw / pdf / docx / mp4 等所有可注册缩略图的格式。

## 为什么做这个

Windows 资源管理器里图片不显示缩略图、只显示扩展名徽标图标时，通常是某个看图软件（2345看图王、美图、QQ影像等）安装或卸载残留，把注册表里登记的"缩略图处理程序"指向了一个已经不存在的组件。

这个工具可以：
- 一键列出本机所有能显示缩略图的格式（约 189 种）和当前负责它的软件；
- 按软件合并展示，告诉你每个软件覆盖了哪些格式；
- 点一下就把某几类格式切换到系统内置照片引擎或另一个看图软件；
- 每次改动前自动备份，随时一键回滚；
- 内置"实测预览"，直接真跑一次缩略图验证改对了没有。

## 下载

到 [Releases](https://github.com/qzwl/thumbnail-picker/releases) 下载 `缩略图引擎选择器.exe`，双击即用。

- 单文件绿色程序，无需安装，无需 .NET SDK；
- 只写 `HKCU\Software\Classes` 用户级注册表，不需要管理员权限；
- 不修改任何看图软件本身，不影响其他账户。

## 界面

- **左侧**：所有格式列表，每行显示当前缩略图引擎、状态（正常/缺失）、来源（系统注册/用户级覆盖）。
- **右侧**：软件下拉列表（按 DLL 合并），选好软件后点"应用到选中格式"即可。
- **底部**：实测预览，选一个图片文件直接看缩略图是否生效。

## 建议用法

- 图片类（jpg / png / bmp / gif / webp / tif）推荐选 **Windows 系统（照片）**，稳定可靠，以后卸载任何看图软件都不受影响。
- PDF、Office 文档、视频、音频保留各自专用组件，系统内置照片引擎不认识它们，硬改会反而不显示。
- 如果某个格式状态是红色"缺失"，就是它登记的看图软件已经卸载或损坏，在右侧选一个可用软件重新指定即可。

## 编译

本项目是单文件 C# WinForms，用 Windows 自带的 csc.exe 即可编译：

```
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe ^
  /nologo /target:winexe /platform:anycpu /optimize+ ^
  /win32icon:src\app.ico ^
  /out:缩略图引擎选择器.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\ThumbPicker.cs
```

## 授权

见 [LICENSE](LICENSE)。个人/非营利用途免费；**商业使用需联系作者授权**；衍生作品版权仍归原作者所有。

## 作者

周木木 · GitHub [@qzwl](https://github.com/qzwl)
