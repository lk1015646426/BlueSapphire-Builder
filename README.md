# BlueSapphire-Builder 完整更新说明

> 本文档为项目唯一文档，由 README 与 CHANGELOG 合并而成。
> 项目版本：`1.2.0` ｜ 文档修订：`1.2.0-doc1`（2026-08-15）

---

## 一、项目概述

BlueSapphire-Builder 是 BlueSapphire 的配套发布工具，把本地构建流程收口成一条稳定链路，支持三条构建管线：

1. **Windows .NET**：`dotnet publish` → `Inno Setup` → 输出正式安装包
2. **Windows 自定义构建**：Tauri / Node 等非 .NET 工程执行原生构建命令 → 归集安装包
3. **Android**：`gradlew assemble` → 输出 Debug / Release APK

它不是通用 IDE，也不是面向最终用户的程序，而是面向打包环境的构建前端。

---

## 二、版本更新记录

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循[语义化版本](https://semver.org/zh-CN/)。

### [1.2.0] - 2026-07-10

#### 新增

**环境自动检测**
- **Windows .NET 项目**：选择 .csproj 后自动探测 ISCC.exe、.iss 安装脚本、.ico 图标
- **Windows 自定义构建**：自动识别 Tauri / Node 项目，推断构建命令（`cargo tauri build` / `npm run build`）和工作目录
- **Android 项目**：选择根目录后自动检测 gradlew、settings.gradle、应用模块、签名状态、启动图标
- **图标智能查找**：支持从 .csproj 的 `<ApplicationIcon>` 属性解析图标路径；Android 项目优先搜索 mipmap 目录
- 所有探测结果实时反馈到日志面板，找到的项绿色提示，找不到的项黄色警告并引导用户手动指定

**Android 签名方案切换检测**
- 自动记录上次构建类型（Debug / Release）
- 检测到构建类型切换时，在日志中弹出醒目警告框，提示用户需先卸载旧版 APK
- 包含卸载引导（手机设置卸载 / `adb uninstall` 命令）

**预设保存对话框重构**
- 深色主题风格，与主界面视觉统一
- 保存前展示完整配置摘要（目标平台、应用名、版本号、项目路径、签名状态等）
- 未设置的项以灰色"（未设置）"标识，一目了然
- 主按钮使用青蓝渐变，次按钮使用描边样式

**UI 引导提示**
- 目标平台按钮增加图标（🪟 / 🤖）和前置条件说明
- 鼠标悬停显示详细工具提示（适用项目类型、输出格式、环境要求）
- Android 项目根目录下方增加引导文字
- 应用模块输入框增加工具提示，说明可手动修改

#### 修复

**关键崩溃**
- **P0 修复**：`IsWindowsCustomBuild` 只读属性被 TwoWay 绑定导致 UI 线程异常崩溃，改为 OneWay 绑定 + 禁用交互
- 修复命令注入风险，ISCC 参数转义，原子化 JSON 写入

**按钮文字截断**
- "删除" / "另存为" 按钮文字被 `Height` + `Padding` 裁剪为不可见，改为自适应高度
- "Debug" / "Release" 按钮同样修复，文字显示为 "--" 的问题已解决

**文字不可见**
- 预设列表项文字使用系统默认暗色，在深色背景上几乎不可见，改为浅白色显示
- 选中状态文字变为青色高亮

**APK 输出路径**
- Android 配置区缺少 APK 输出目录的输入控件，用户无法手动指定，已新增 PathPicker
- 修改输出目录后实时刷新预览路径

#### 优化

**圆角体系升级**
- 卡片容器 4px → 10px；输入框 2px → 8px
- 主按钮 2px → 10px，次按钮 2px → 8px；模式切换按钮 3px → 10px
- PathPicker 补全自定义模板，浏览按钮增加 hover / pressed 状态
- 复选框 12px/1px → 13px/3px
- 全局 ToolTip 深色化（深色背景 + 青色边框 + 圆角）

**架构改进**
- `Servies/` 目录重命名为 `Services/`（修正拼写错误）
- 新增 `Controls/PathPicker.xaml` UserControl，统一路径选择交互
- 新增 `Helpers/` 目录：PathHelper、AndroidProjectHelper、IconAssetHelper、CrashLogger、SafeFileWriter 等
- 新增 `Services/PresetService.cs` 预设管理服务

### [1.1.0] - 2026-07-09

#### 新增
- MVVM 架构重构，引入 CommunityToolkit.Mvvm
- 赛博朋克深色 UI 主题
- 平滑进度条动画引擎
- 构建耗时历史记录与加权估算
- Android 项目支持（Gradle 构建、模块自动识别、签名检测）
- Inno Setup 安装包生成
- Windows 自定义构建模式（Tauri / Node）
- 崩溃日志记录到 `%APPDATA%\BlueSapphire.Builder\crash.log`

#### 修复
- 9 项 P0 关键问题修复（命令注入、参数转义、原子写入等）

### [1.0.0] - 2026-07-08

#### 新增
- 项目初始版本
- Windows .NET 项目打包（.csproj → dotnet publish → ISCC 安装包）
- 基础 UI 界面与配置持久化

### [文档修订 1.2.0-doc1] - 2026-08-15

本次对 README 与代码不一致问题的修正记录：

**删除的失实描述**
- 移除 "`RawOutputDir` 已迁移为 `PublishOutputDir`" 的说法——代码中 `PublishOutputDir` 完全不存在，实际字段仍为 `RawOutputDir`
- 移除 "发布目录混入 `BlueSapphire.Tests` / `TestData` / `.git` / `obj` 检测"——代码中无此逻辑

**修正的错误**
- 文档版本号 1.0.4 → 1.2.0，与 CHANGELOG 对齐
- 项目结构图修正 `Servies` 拼写错误，补全 `Helpers/`（8 个文件）、`Controls/`、`Services/PresetService.cs`
- `AppConfig.cs` 失效链接（原指向不存在的 `new/` 路径）改为相对路径
- 防呆规则替换为代码中实际存在的校验逻辑

**补齐的缺失能力描述**
- 三条构建管线（Windows .NET / Tauri/Node 自定义 / Android Gradle）
- 环境自动检测（ISCC 注册表探测、.iss/图标查找、Android 模块/签名检测）
- 打包预设系统、进度历史估算引擎、30 分钟超时保护
- 完整配置字段清单（与 `AppConfig.cs` 逐一对应，按管线分组）
- 持久化文件位置（`%APPDATA%\BlueSapphire.Builder` 下三个 JSON 文件）

---

## 三、核心能力

### 三条构建管线

- **Windows .NET**：一键执行 `dotnet publish -c Release -r win-x64 --self-contained true`，再调用 `ISCC.exe` 生成安装包
- **Windows 自定义构建**：识别非 `.csproj` 工程（如 `package.json`），默认执行 `npm run tauri build`，构建完成后按目录语义（`bundle/nsis`、`bundle/msi`、文件名含 `setup`）递归归集 `.exe` / `.msi` 安装包
- **Android**：自动检测应用模块与签名状态，执行 `gradlew assemble{Debug|Release}`，生成 APK 并可归集到自定义输出目录

### 环境自动检测

- 选择 `.csproj` 后自动探测 `ISCC.exe`（注册表 + 常见安装路径）、`.iss` 安装脚本、`.ico` 图标（含从 `<ApplicationIcon>` 解析）
- 自动识别 Tauri / Node 项目并推断构建命令与工作目录
- 选择 Android 根目录后自动检测 `gradlew`、应用模块、签名状态、启动图标
- 探测结果实时输出到日志面板：找到的项绿色提示，缺失的项黄色警告并引导手动指定

### 构建体验

- 基于最近 3 次成功构建的进度估算（耗时曲线 + 产物字节数/文件数加权），进度条平滑且只前进不回退
- 构建阶段 30 分钟超时保护，超时强制终止进程树
- 打包预设：命名的完整配置快照，多项目一键切换
- 配置防抖自动保存（800ms），崩溃或强关不丢配置

---

## 四、运行要求

打包机需要（按使用的构建管线按需安装）：

- `Windows 10 / 11`
- `.NET 8 SDK`（Windows .NET 管线）
- `Inno Setup 6`（生成安装包）
- `Node.js / npm`（Tauri / Node 工程）
- `Android SDK` 与项目自带 `gradlew`（Android 管线）

最终用户机器不需要安装这些环境，只有打包机器需要。

---

## 五、配置项

当前配置模型对应 [AppConfig.cs](AppConfig.cs)：

- 通用：`TargetPlatform`、`PresetName`、`AppName`、`Version`、`Publisher`、`AppID`、`ProjectPath`、`SetupOutputDir`、`MakeInstaller`
- Windows .NET：`RawOutputDir`、`InnoSetupPath`、`IssScriptPath`、`WindowsAppName`、`WindowsVersion`、`WindowsPublisher`、`WindowsIconPath`
- Windows 自定义构建：`WindowsBuildCommand`、`WindowsBuildWorkingDir`、`WindowsBuildArtifactDir`
- Android：`AndroidProjectRoot`、`AndroidModuleName`、`AndroidBuildType`、`AndroidApkOutputDir`、`AndroidAppName`、`AndroidVersion`、`AndroidPublisher`、`AndroidIconPath`、`LastAndroidBuildType`

### 持久化位置

所有数据存于 `%APPDATA%\BlueSapphire.Builder`（写入失败时回退 exe 同目录，便携模式）：

- `builder_config.json`：当前配置
- `builder_presets.json`：打包预设列表
- `builder_progress_history_v1.json`：构建进度历史基线
- `crash.log`：崩溃日志

---

## 六、使用方式

### 1. 选择目标平台

主界面切换 Windows / Android，界面字段随平台自动切换。

### 2. Windows .NET 项目

- `ProjectPath` 指向 `.csproj`
- `RawOutputDir` 是 `dotnet publish` 的输出目录，也是安装包唯一允许的输入目录

不要把它设置成：

- 项目根目录
- 磁盘根目录
- `bin\Debug`

### 3. Windows Tauri / Node 项目

- `ProjectPath` 指向 `package.json` 等非 `.csproj` 文件，自动切换为自定义构建模式
- 可选配置 `WindowsBuildCommand`（默认 `npm run tauri build`）、`WindowsBuildWorkingDir`、`WindowsBuildArtifactDir`（默认 `src-tauri/target/release/bundle`）

### 4. Android 项目

- `AndroidProjectRoot` 指向包含 `gradlew.bat` 的项目根目录
- 模块名默认 `app`，可手动修改；构建类型可选 Debug / Release

### 5. 构建

点击构建后，按所选管线执行（以 Windows .NET 为例）：

1. 校验输入路径（`AppConfigValidator`）
2. 安全检查输出目录，清空旧发布目录
3. 执行 `dotnet publish`
4. 校验 `{AppName}.exe` 是否真实生成（防止增量编译假成功）
5. 调用 `ISCC.exe` 生成安装包

---

## 七、防呆规则

Builder 会主动阻止这些错误场景：

- 输出目录与项目目录（或产物目录）相同或为其上级目录——拒绝执行，防止递归删除源码
- `SetupOutputDir` 与 `RawOutputDir` 相同
- `dotnet publish` 退出码为 0 但输出目录缺少 `{AppName}.exe`
- 自定义构建完成后在产物目录找不到 `.exe` / `.msi`
- 版本号 / AppID 含命令注入字符（`ArgumentSanitizer` 校验）
- 找不到 `installer.iss` 或 `ISCC.exe`
- Android Debug ↔ Release 签名方案切换时，日志弹出醒目警告提醒先卸载旧版 APK

---

## 八、项目结构

```text
BlueSapphire.Builder/
├── README.md                     # 本文档（项目唯一文档）
├── docs/
│   └── screenshots/              # 截图目录
├── AppConfig.cs                  # 构建配置模型
├── MainWindow.xaml(.cs)          # 主界面与配置持久化
├── App.xaml(.cs)                 # 应用入口与全局样式
├── InputBoxDialog.cs             # 预设命名等输入对话框
├── Chinese.isl                   # Inno Setup 中文语言文件
├── Controls/
│   └── PathPicker.xaml(.cs)      # 可复用路径选择控件
├── Helpers/
│   ├── AndroidProjectHelper.cs   # Android 模块/签名/APK 路径探测
│   ├── AppConfigValidator.cs     # 构建前配置校验
│   ├── ArgumentSanitizer.cs      # 命令注入防护与参数校验
│   ├── BuildProgressHistoryStore.cs # 构建进度历史基线存储
│   ├── CrashLogger.cs            # 崩溃日志
│   ├── IconAssetHelper.cs        # Windows/Android 图标处理
│   ├── PathHelper.cs             # ISCC/.iss/图标自动探测
│   └── SafeFileWriter.cs         # 原子化文件写入
├── Services/
│   ├── BuilderService.cs         # 构建与打包核心服务（三条管线）
│   └── PresetService.cs          # 打包预设持久化
└── ViewModels/
    └── MainViewModel.cs          # 主界面状态中枢
```

---

## 九、常见问题 FAQ

### 1. 为什么 Builder 不允许把项目根目录当输出目录？

输出目录在构建前会被整体清空重建。如果指向仓库根目录，会直接删除源码，属于高风险错误配置，因此安全检查会直接拒绝。

### 2. Tauri 项目构建完成后为什么提示找不到安装包？

默认在 `src-tauri/target/release/bundle` 下按 `nsis` → `msi` → 文件名含 `setup` 的顺序查找。若你的工程输出位置不同，请手动指定 `WindowsBuildArtifactDir`。

### 3. Builder 会不会覆盖我之前的旧配置？

配置按当前字段自动保存与加载，损坏的配置/预设文件会自动备份为 `.corrupt` 后重置，不会阻断启动。

### 4. 为什么 Builder 找不到 `ISCC.exe`？

说明本机没有安装 `Inno Setup 6`，或未安装在常见路径。Builder 只负责调用 Inno Setup，不会替你安装它；可在界面手动指定 `ISCC.exe` 路径。

### 5. Android 构建提示签名方案变更怎么办？

Debug 和 Release 使用不同的签名密钥，无法直接覆盖安装。按日志提示先在手机上卸载旧版 APK（设置 → 应用 → 卸载，或 `adb uninstall <包名>`），再安装本次构建的 APK。

### 6. 目标用户电脑也需要安装 `.NET SDK` 和 `Inno Setup` 吗？

不需要。这些都属于打包环境依赖，不属于最终用户运行产物的前置条件（Windows .NET 管线产出 self-contained 安装包）。

---

## 十、已知限制与后续路线

### 已知限制

- Builder 依赖本机已安装的目标工具链（.NET SDK / Inno Setup / Node / Android SDK），不负责安装
- 正式主界面截图仍需后续替换进 `docs/screenshots/`
- Builder 的目标是发布环境，不是面向最终用户的安装程序

### 后续路线

- 继续收口 Builder 的路径探测与日志体验
- 继续优化与 GitHub Release 的联动体验
- 在正式发布前补入最终版界面截图

---

## 十一、源码运行与下载

如果给 Builder 单独发布版本，建议统一放到 GitHub Releases：

- 下载地址：[BlueSapphire-Builder Releases](https://github.com/lk1015646426/BlueSapphire-Builder/releases)

如果当前还没有单独发布安装包，也可以直接源码运行：

```powershell
git clone https://github.com/lk1015646426/BlueSapphire-Builder.git
cd BlueSapphire-Builder
dotnet build BlueSapphire.Builder.sln -c Release
```
