# 更新记录 (CHANGELOG)

本文件记录 Blue Sapphire Builder 的所有重要变更。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

---

## [1.2.0] - 2026-07-10

### 新增

#### 环境自动检测
- **Windows .NET 项目**：选择 .csproj 后自动探测 ISCC.exe、.iss 安装脚本、.ico 图标
- **Windows 自定义构建**：自动识别 Tauri / Node 项目，推断构建命令（`cargo tauri build` / `npm run build`）和工作目录
- **Android 项目**：选择根目录后自动检测 gradlew、settings.gradle、应用模块、签名状态、启动图标
- **图标智能查找**：支持从 .csproj 的 `<ApplicationIcon>` 属性解析图标路径；Android 项目优先搜索 mipmap 目录
- 所有探测结果实时反馈到日志面板，找到的项绿色提示，找不到的项黄色警告并引导用户手动指定

#### Android 签名方案切换检测
- 自动记录上次构建类型（Debug / Release）
- 检测到构建类型切换时，在日志中弹出醒目警告框，提示用户需先卸载旧版 APK
- 包含卸载引导（手机设置卸载 / `adb uninstall` 命令）

#### 预设保存对话框重构
- 深色主题风格，与主界面视觉统一
- 保存前展示完整配置摘要（目标平台、应用名、版本号、项目路径、签名状态等）
- 未设置的项以灰色"（未设置）"标识，一目了然
- 主按钮使用青蓝渐变，次按钮使用描边样式

#### UI 引导提示
- 目标平台按钮增加图标（🪟 / 🤖）和前置条件说明
- 鼠标悬停显示详细工具提示（适用项目类型、输出格式、环境要求）
- Android 项目根目录下方增加引导文字
- 应用模块输入框增加工具提示，说明可手动修改

### 修复

#### 关键崩溃
- **P0 修复**：`IsWindowsCustomBuild` 只读属性被 TwoWay 绑定导致 UI 线程异常崩溃，改为 OneWay 绑定 + 禁用交互
- 修复命令注入风险，ISCC 参数转义，原子化 JSON 写入

#### 按钮文字截断
- "删除" / "另存为" 按钮文字被 `Height` + `Padding` 裁剪为不可见，改为自适应高度
- "Debug" / "Release" 按钮同样修复，文字显示为 "--" 的问题已解决

#### 文字不可见
- 预设列表项文字使用系统默认暗色，在深色背景上几乎不可见，改为浅白色显示
- 选中状态文字变为青色高亮

#### APK 输出路径
- Android 配置区缺少 APK 输出目录的输入控件，用户无法手动指定，已新增 PathPicker
- 修改输出目录后实时刷新预览路径

### 优化

#### 圆角体系升级
- 卡片容器 4px → 10px
- 输入框 2px → 8px
- 主按钮 2px → 10px，次按钮 2px → 8px
- 模式切换按钮 3px → 10px
- PathPicker 补全自定义模板，浏览按钮增加 hover / pressed 状态
- 复选框 12px/1px → 13px/3px
- 全局 ToolTip 深色化（深色背景 + 青色边框 + 圆角）

#### 架构改进
- `Servies/` 目录重命名为 `Services/`（修正拼写错误）
- 新增 `Controls/PathPicker.xaml` UserControl，统一路径选择交互
- 新增 `Helpers/` 目录：PathHelper、AndroidProjectHelper、IconAssetHelper、CrashLogger、SafeFileWriter 等
- 新增 `Services/PresetService.cs` 预设管理服务

---

## [1.1.0] - 2026-07-09

### 新增
- MVVM 架构重构，引入 CommunityToolkit.Mvvm
- 赛博朋克深色 UI 主题
- 平滑进度条动画引擎
- 构建耗时历史记录与加权估算
- Android 项目支持（Gradle 构建、模块自动识别、签名检测）
- Inno Setup 安装包生成
- Windows 自定义构建模式（Tauri / Node）
- 崩溃日志记录到 `%APPDATA%\BlueSapphire.Builder\crash.log`

### 修复
- 9 项 P0 关键问题修复（命令注入、参数转义、原子写入等）

---

## [1.0.0] - 2026-07-08

### 新增
- 项目初始版本
- Windows .NET 项目打包（.csproj → dotnet publish → ISCC 安装包）
- 基础 UI 界面与配置持久化
