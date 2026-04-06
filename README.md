# BlueSapphire-Builder

BlueSapphire-Builder 是 BlueSapphire 的配套发布工具，用来把本地构建流程收口成一条稳定链路：

1. `dotnet publish`
2. `Inno Setup`
3. 输出正式安装包

它不是通用 IDE，也不是面向最终用户的程序，而是面向打包环境的构建前端。

## 版本信息

- 当前文档版本：`1.0.4`
- 当前定位：`BlueSapphire` 专用发布工具
- 当前发布目标：`win-x64 self-contained` 安装包

### 1.0.4 更新摘要

- Builder 已切换为 `publish-only` 打包模式
- `RawOutputDir` 已迁移为 `PublishOutputDir`
- 构建前校验与日志输出已补齐
- `Chinese.isl` 已更新到 Inno Setup 6 可用版本

## 当前定位

- 只服务于 `BlueSapphire` 的正式发布
- 只接受 `publish 输出目录` 作为安装源
- 不允许直接对仓库根目录做安装包编译

## 运行要求

本机需要：

- `Windows 10 / 11`
- `.NET 8 SDK`
- `Inno Setup 6`

最终用户机器不需要安装这些环境，只有打包机器需要。

## 核心能力

- 一键执行 `dotnet publish -c Release -r win-x64 --self-contained true`
- 一键调用 `ISCC.exe` 生成安装包
- 自动校验 `publish` 目录是否合法
- 自动阻止把项目根目录当成发布目录
- 允许指定自定义 `installer.iss`
- 自动保存最近一次构建配置
- 兼容旧配置里的 `RawOutputDir`，自动迁移到 `PublishOutputDir`

## 配置项

当前配置模型对应 [AppConfig.cs](C:/Users/10156/Desktop/蓝宝石工具开发/new/BlueSapphire-Builder/AppConfig.cs)：

- `AppName`
- `Version`
- `Publisher`
- `AppID`
- `ProjectPath`
- `PublishOutputDir`
- `SetupOutputDir`
- `InnoSetupPath`
- `IssScriptPath`
- `MakeInstaller`

兼容迁移：

- 旧字段 `RawOutputDir` 仍可被读取
- 保存配置时只写入 `PublishOutputDir`

## 使用方式

### 1. 选择项目

把 `BlueSapphire.csproj` 作为 `ProjectPath`。

### 2. 选择发布目录

`PublishOutputDir` 是 `dotnet publish` 的正式输出目录，也是安装包唯一允许的输入目录。

不要把它设置成：

- 项目根目录
- 磁盘根目录
- `bin\Debug`

### 3. 选择安装器

设置：

- `InnoSetupPath` 指向 `ISCC.exe`
- `IssScriptPath` 指向 `BlueSapphire` 仓库里的 `installer.iss`

### 4. 构建

点击构建后，Builder 会按顺序执行：

1. 校验输入路径
2. 清空旧发布目录
3. 执行 `dotnet publish`
4. 检查 `BlueSapphire.exe` 是否存在
5. 检查 `publish` 目录里是否混入测试/源码残留
6. 调用 `ISCC.exe` 生成安装包

## 防呆规则

Builder 会主动阻止这些错误场景：

- `PublishOutputDir` 指向项目根目录
- `PublishOutputDir` 指向磁盘根目录
- `PublishOutputDir` 指向 `bin\Debug`
- 发布目录里缺少 `BlueSapphire.exe`
- 发布目录里混入 `BlueSapphire.Tests`
- 发布目录里混入 `TestData`
- 发布目录里混入 `.git`
- 发布目录里混入 `obj`
- 找不到 `installer.iss`
- 找不到 `ISCC.exe`

## 日志

构建日志会明确输出：

- `ProjectPath`
- `PublishOutputDir`
- `IssScriptPath`
- `SetupOutputDir`
- `InnoSetupPath`

这样可以直接核对本次构建到底用了哪组路径。

## 项目结构

```text
BlueSapphire-Builder/
├── AppConfig.cs               # 构建配置模型
├── MainWindow.xaml            # 主界面
├── MainWindow.xaml.cs         # UI 交互与配置持久化
├── Servies/BuilderService.cs  # 构建与打包核心服务
├── ViewModels/MainViewModel.cs
├── Chinese.isl                # Inno Setup 中文语言文件
└── BlueSapphire.Builder.csproj
```

## 推荐工作流

如果你只维护 BlueSapphire，建议固定使用这条链路：

1. 在 `BlueSapphire` 仓库完成代码
2. 在 Builder 中选择 `BlueSapphire.csproj`
3. 让 Builder 产出 `publish` 目录
4. 让 Builder 调用 `installer.iss` 输出安装包

这样可以避免再次回到“直接对仓库根目录打包”的旧方式。

## 截图展示

当前仓库已经预留正式截图目录：

- `docs/screenshots/main-window.png`
- `docs/screenshots/build-success.png`

当前主界面图如下：

### 主界面

![BlueSapphire Builder Main Window](docs/screenshots/main-window.png)

补充说明：

- `docs/screenshots/build-success.png` 已保留在仓库中，后续可以替换成最终版构建成功截图
- 推荐最终展示内容仍然是：项目配置区、构建日志区、构建完成状态

## 安装包下载说明

如果你给 Builder 单独发布版本，建议统一放到 GitHub Releases：

- 下载地址：[BlueSapphire-Builder Releases](https://github.com/lk1015646426/BlueSapphire-Builder/releases)
- 建议发布标签：`v1.0.4`

如果当前还没有单独发布安装包，也可以直接源码运行：

```powershell
git clone https://github.com/lk1015646426/BlueSapphire-Builder.git
cd BlueSapphire-Builder
dotnet build BlueSapphire.Builder.sln -c Release
```

Builder 只用于打包环境，因此下载或构建后请在具备以下条件的机器上使用：

- 已安装 `.NET 8 SDK`
- 已安装 `Inno Setup 6`
- 本机能访问 `BlueSapphire.csproj`

## 常见问题 FAQ

### 1. 为什么 Builder 不允许把项目根目录当发布目录？

因为安装包阶段现在只接受 `dotnet publish` 的纯净输出目录。直接指向仓库根目录会把源码、测试和临时文件一起打进去，属于高风险错误配置。

### 2. 为什么要从 `RawOutputDir` 迁移到 `PublishOutputDir`？

因为旧字段语义不够明确。现在 Builder 把发布目录明确命名为 `PublishOutputDir`，并且只把它当成正式安装源。

### 3. Builder 会不会覆盖我之前的旧配置？

会自动兼容读取旧配置里的 `RawOutputDir`，但保存时只写新字段 `PublishOutputDir`。这是一次向前迁移，不会继续保留旧键。

### 4. 为什么 Builder 找不到 `ISCC.exe`？

说明本机没有安装 `Inno Setup 6`，或者路径没有配置到正确的 `ISCC.exe`。Builder 只负责调用 Inno Setup，不会替你安装它。

### 5. 目标用户电脑也需要安装 `.NET SDK` 和 `Inno Setup` 吗？

不需要。`.NET SDK` 和 `Inno Setup` 只属于打包环境依赖，不属于最终用户运行 BlueSapphire 的前置条件。

## 已知限制

- Builder 仍依赖本机已安装的 `Inno Setup 6`
- README 截图目录已经预留，但正式主界面截图仍需后续替换进 `docs/screenshots/`
- Builder 的目标是发布环境，不是面向最终用户的安装程序

## 后续路线

- 继续收口 Builder 的路径探测与日志体验
- 继续优化与 GitHub Release 的联动体验
- 在正式发布前补入最终版界面截图
