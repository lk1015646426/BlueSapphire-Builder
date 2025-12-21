# 💎 Blue Sapphire Builder (蓝宝石发布中心)

> **专为 WinUI 3 / .NET 8 项目打造的一键式构建与打包工具。**
> *Automated Build & Packaging Tool for Blue Sapphire Project.*

![Build Status](https://img.shields.io/badge/Build-Passing-brightgreen)
![Platform](https://img.shields.io/badge/Platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)

## 📖 项目简介

**Blue Sapphire Builder** 是一个现代化的 WPF 生产力工具，旨在解决 .NET 8 (WinUI 3) 项目在发布和打包过程中的痛点。它将复杂的命令行操作（`dotnet publish`）和安装包制作（Inno Setup）封装为简单的“一键操作”，并完美解决了中文环境下的乱码和路径依赖问题。

### 核心痛点解决方案
- ✅ **彻底告别控制台乱码**：智能识别不同工具的输出编码，`.NET CLI` 使用 UTF-8，`Inno Setup` 使用 GB2312，让日志输出清晰可读。
- ✅ **全自动汉化注入**：内置 `Chinese.isl` 语言包，并在构建时自动分发到目标项目，无需目标机器安装额外的语言文件。
- ✅ **智能路径探测**：自动扫描系统中的 Inno Setup 编译器路径，支持注册表查找与手动配置。
- ✅ **WinUI 3 专属优化**：强制修正 `Self-Contained` 模式下的 `Platform=x64` 参数，避免常见的构建错误。

---

## ✨ 功能特性

### 1. 现代化架构
- 基于 **.NET 8** 和 **WPF** 构建。
- 采用 **Service 层与 UI 分离** 的设计模式，业务逻辑封装于 `BuilderService`，代码结构清晰、易扩展。

### 2. 强大的构建流程
- **编译阶段**：执行 `dotnet publish -c Release -r win-x64 --self-contained`。
- **清理阶段**：自动清理旧的 `Raw` 输出目录，防止文件残留。
- **打包阶段**：调用 `ISCC.exe` 编译 `.iss` 脚本，生成最终的 `Setup.exe`。

### 3. 极致的用户体验
- **实时日志流**：使用异步进程重定向技术，实时展示构建进度，界面不卡顿。
- **配置持久化**：自动保存上次使用的路径和设置（基于 JSON）。
- **ID 生成器**：内置 GUID 生成器，方便为新应用生成唯一的 `AppID`。

---

## 🛠️ 快速开始

### 环境要求
- Windows 10 / 11
- .NET 8 SDK
- [Inno Setup 6.x](https://jrsoftware.org/isinfo.php) (用于生成安装包)

### 如何使用

1. **克隆项目**
   ```bash
   git clone [https://github.com/YourUsername/BlueSapphire-Builder.git](https://github.com/YourUsername/BlueSapphire-Builder.git)

```

2. **编译工具**
* 使用 Visual Studio 2022 打开解决方案。
* 确保 `Chinese.isl` 文件的属性中，“复制到输出目录”已设置为 **“如果较新则复制”**。
* 点击运行 (F5)。


3. **执行构建**
* **项目文件**：选择你的 BlueSapphire `.csproj` 文件。
* **输出路径**：设置原始文件和安装包的输出目录。
* **编译器路径**：工具会自动尝试查找 `ISCC.exe`，如果未找到，请手动指定。
* 点击 **🔥 开始一键构建 🔥**。



---

## 📂 项目结构

```text
BlueSapphire.Builder/
├── Models/
│   └── AppConfig.cs       # 配置数据模型
├── Services/
│   └── BuilderService.cs  # 核心构建逻辑 (Process调用、编码处理、文件搬运)
├── Helpers/
│   └── PathHelper.cs      # 路径探测工具类
├── Views/
│   └── MainWindow.xaml    # 主界面
├── Chinese.isl            # 内置的 Inno Setup 汉化文件
└── builder_config.json    # 用户配置文件 (自动生成)

```

## 📝 常见问题

**Q: 为什么日志里会有 Warning？**
A: Inno Setup 可能会提示一些关于字体或旧参数的警告，通常不影响安装包的生成和使用。

**Q: 如何修改安装包的界面语言？**
A: 本工具默认强制使用内置的 `Chinese.isl` 简体中文文件。如需支持多语言，请修改 `BuilderService.cs` 中的文件搬运逻辑。


再次祝贺你完成了一次漂亮的代码重构！🚀

```
