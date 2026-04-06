# 💎 Blue Sapphire Cyber Builder (蓝宝石·赛博构建终端)

> **WinUI 3 / .NET 8 项目专属的现代化构建与发布中心 [Cyberpunk Edition]**
> *Next-Gen Automated Build & Packaging Terminal for Blue Sapphire Project.*

![Build Status](https://img.shields.io/badge/Build-Passing-00F0FF)
![Platform](https://img.shields.io/badge/Platform-Windows-BC13FE)
![.NET](https://img.shields.io/badge/.NET-8.0-64748B)

## 📖 项目简介

**Blue Sapphire Cyber** 是原构建工具的**全方位重构版本**。它不仅是一个生产力工具，更是一个拥有极致视觉体验的“黑客终端”。

它可以将繁琐的 `.NET CLI` 命令和 `Inno Setup` 打包流程封装为一键操作，并以沉浸式的赛博朋克风格呈现。

### 核心进化 (Cyber Evolution)
- 🌌 **沉浸式赛博 UI**：全新的深色磨砂玻璃界面、霓虹光效呼吸按钮、流光进度条，带来 3A 级的交互体验。
- ⚡ **MVVM 架构重构**：采用标准的 MVVM 模式解耦逻辑与界面，代码更健壮、易维护。
- 🛠️ **增强型控制台**：内置日志清洗（自动过滤乱码）、一键复制、一键清空功能。
- 🔧 **灵活配置**：支持自定义 `.iss` 安装脚本路径，不再局限于默认模板。

---

## ✨ 功能特性 (Features)

### 1. 极致视觉与交互
- **HUD 风格界面**：自定义无边框窗口 (WindowChrome)，配合放射状渐变背景。
- **动态反馈**：按钮悬停呼吸光效、进度条流光动画、幽灵玻璃质感边框。
- **深色模式**：全全局深色调，专为长时间开发的护眼设计。

### 2. 强大的构建管线
- **智能编译**：自动执行 `dotnet publish -c Release -r win-x64 --self-contained`。
- **Publish-Only 打包**：安装包阶段只接受 `dotnet publish` 生成的发布目录，拒绝直接打包仓库根目录。
- **环境自检**：自动探测 Inno Setup 编译器路径，支持注册表与常用路径扫描。
- **自动汉化**：构建时自动注入 `Chinese.isl`，确保安装包界面为中文。

### 3. 生产力工具箱
- **AppID 生成器**：内置 GUID 生成算法，一键生成项目唯一标识。
- **实时日志流**：异步重定向进程输出，像黑客电影一样实时滚动的绿光/蓝光日志。
- **配置持久化**：自动记忆上次使用的工程路径和输出目录。

---

## 🛠️ 快速开始

### 环境要求
- Windows 10 / 11 (建议开启透明效果以获得最佳体验)
- .NET 8 SDK
- [Inno Setup 6.x](https://jrsoftware.org/isinfo.php)

### 如何使用

1. **启动终端**
   运行 `BlueSapphire.Builder.exe`，进入赛博构建中心。

2. **载入数据**
   - **核心元数据**：输入软件名称、版本号。
   - **环境配置**：选择 `.csproj` 项目文件和 `ISCC.exe` 编译器。
   - **脚本选择**：(可选) 指定自定义的 `.iss` 安装脚本。
   - **发布目录**：Builder 会把 `dotnet publish` 的输出目录作为唯一安装源传给 Inno Setup。

3. **启动序列**
   点击巨大的 **[🚀 启动构建序列]** 按钮。
   > *观察流光进度条和控制台日志，等待构建完成。*

---

## 📂 项目结构 (Refactored)

```text
BlueSapphire.Builder/
├── Models/
│   └── AppConfig.cs       # [Data] 配置数据模型
├── ViewModels/            # [MVVM] 视图模型层
│   └── MainViewModel.cs   # 核心交互逻辑与状态绑定
├── Services/
│   └── BuilderService.cs  # [Core] 构建服务 (Process/IO/Encoding)
├── Views/
│   └── MainWindow.xaml    # [UI] 赛博朋克风格主界面
├── App.xaml               # [Style] 全局资源 (颜色/画刷/控件模板)
└── builder_config.json    # 用户配置文件
