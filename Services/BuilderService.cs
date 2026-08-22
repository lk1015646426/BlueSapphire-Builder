using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BlueSapphire.Builder;
using BlueSapphire.Builder.Helpers;

namespace BlueSapphire.Builder.Services
{
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class LogEventArgs : EventArgs
    {
        public string Message { get; }
        public LogLevel Level { get; }
        // 向后兼容：Error 级别视作 IsError=true
        public bool IsError => Level == LogLevel.Error;

        public LogEventArgs(string message, bool isError = false)
            : this(message, isError ? LogLevel.Error : LogLevel.Info)
        {
        }

        public LogEventArgs(string message, LogLevel level)
        {
            Message = message;
            Level = level;
        }
    }

    public class BuildResult
    {
        public string? OutputDirectory { get; init; }
        public string? PrimaryOutputPath { get; init; }
    }

    internal sealed class StageExecutionResult
    {
        public string StageKey { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public long DurationMilliseconds { get; init; }
        public long FinalOutputBytes { get; init; }
        public int FinalOutputFileCount { get; init; }
        public List<StageProgressSample> Samples { get; init; } = new();
    }

    internal readonly record struct OutputSnapshot(long OutputBytes, int OutputFileCount);

    public class BuilderService
    {
        private readonly BuildProgressHistoryStore _progressHistoryStore = new BuildProgressHistoryStore();

        // 构建阶段超时：30 分钟，避免进程卡死导致 UI 永久无响应
        private static readonly TimeSpan StageTimeout = TimeSpan.FromMinutes(30);

        public event EventHandler<LogEventArgs>? LogReceived;
        public event EventHandler<double>? ProgressChanged;

        public async Task<BuildResult> BuildAsync(AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            // 构建前一次性校验所有字段合法性
            AppConfigValidator.ValidateAndThrow(config);

            string buildKey = BuildProgressHistoryStore.CreateBuildKey(config);
            string buildDescription = BuildProgressHistoryStore.DescribeBuild(config);
            BuildProgressBaseline? baseline = _progressHistoryStore.Load(buildKey, 3);
            BuildProgressProfile? previousProfile = baseline?.Profile;
            var buildStopwatch = Stopwatch.StartNew();
            var stageResults = new List<StageExecutionResult>();

            if (previousProfile != null)
            {
                SendLog($">>> 已加载最近 {baseline!.SourceRunCount} 次成功构建的加权基线：{buildDescription} | 参考耗时 {FormatDuration(previousProfile.TotalDurationMilliseconds)}", false);
            }
            else
            {
                SendLog($">>> 当前构建暂无历史基线：{buildDescription} | 本次将采集数据，最近 3 次成功构建会参与后续估算", false);
            }

            BuildResult result = string.Equals(config.TargetPlatform, "Android", StringComparison.OrdinalIgnoreCase)
                ? await BuildAndroidAsync(config, previousProfile, stageResults)
                : await BuildWindowsAsync(config, previousProfile, stageResults);

            buildStopwatch.Stop();

            try
            {
                SaveBuildProgressProfile(config, buildKey, buildDescription, buildStopwatch.ElapsedMilliseconds, stageResults);
                SendLog($">>> 本次历史基线已更新，总耗时 {FormatDuration(buildStopwatch.ElapsedMilliseconds)}；后续会按最近 3 次成功构建做加权估算", LogLevel.Success);
            }
            catch (Exception ex)
            {
                SendLog($">>> 历史基线写入失败：{ex.Message}", LogLevel.Warning);
            }

            ReportProgress(100);
            return result;
        }

        private async Task<BuildResult> BuildWindowsAsync(
            AppConfig config,
            BuildProgressProfile? previousProfile,
            List<StageExecutionResult> stageResults)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (string.IsNullOrWhiteSpace(config.ProjectPath) || !File.Exists(config.ProjectPath))
            {
                throw new FileNotFoundException("找不到项目文件 (.csproj 或 package.json)");
            }

            // 非 .csproj 工程（如 Tauri / Node 的 package.json）走自定义命令构建旁路，
            // 让原生工具链自己打包，蓝宝石只负责触发 + 归集产物。
            string projectExt = Path.GetExtension(config.ProjectPath);
            if (!string.Equals(projectExt, ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return await BuildWindowsCustomAsync(config, previousProfile, stageResults);
            }

            if (string.IsNullOrWhiteSpace(config.RawOutputDir))
            {
                throw new ArgumentException("未设置原始输出目录");
            }

            string projectPath = ArgumentSanitizer.ValidatePath(config.ProjectPath, "项目文件路径");
            string rawOutputDir = ArgumentSanitizer.ValidatePath(config.RawOutputDir, "编译产物输出目录");
            string? windowsIconPath = IconAssetHelper.ResolveOptionalIconPath(config.WindowsIconPath);

            // 准备输出目录前先做安全检查，防止用户误设为工程目录导致源码被删
            string projectDir = Path.GetDirectoryName(projectPath)!;
            AssertSafeOutputDirectory(rawOutputDir, projectDir);

            SendLog(">>> [0/2] 正在准备输出目录...", false);
            ReportProgress(5);
            PrepareOutputDirectory(rawOutputDir);

            SendLog(">>> [1/2] 正在编译 .NET 核心...", false);

            var stagePlan = config.MakeInstaller
                ? CreateStagePlan(previousProfile, 5.0, 99.0,
                    ("windows-publish", 45.0),
                    ("windows-installer", 49.0))
                : CreateStagePlan(previousProfile, 5.0, 99.0,
                    ("windows-publish", 94.0));

            // 版本号校验通过 ArgumentSanitizer 保证不含 shell 注入字符
            string validatedVersion = ArgumentSanitizer.ValidateDotNetVersion(config.Version);

            var publishArgs = new List<string>
            {
                "publish",
                projectPath,
                "-c", "Release",
                "-r", "win-x64",
                "--self-contained", "true",
                "-o", rawOutputDir,
                $"/p:Version={validatedVersion}",
                "/p:Platform=x64"
            };

            if (!string.IsNullOrWhiteSpace(windowsIconPath))
            {
                publishArgs.Add($"/p:ApplicationIcon={windowsIconPath}");
                SendLog($">>> 已启用 Windows 图标：{Path.GetFileName(windowsIconPath)}", false);
            }

            StageExecutionResult publishStage = await RunCommandAsync(
                "dotnet",
                publishArgs,
                Encoding.UTF8,
                stagePlan["windows-publish"].StartProgress,
                stagePlan["windows-publish"].EndProgress,
                "windows-publish",
                ".NET Publish",
                monitorDirectory: rawOutputDir,
                progressReference: FindStageProfile(previousProfile, "windows-publish"));
            stageResults.Add(publishStage);

            // 安全验证：即使 dotnet publish 退出码为 0，也确认 exe 真的生成到了输出目录。
            // 防止增量编译/文件锁等边缘情况导致 exe 缺失，ISCC 报模糊的 "SourcePath must contain..." 错误。
            string expectedExeName = $"{config.AppName ?? "BlueSapphire"}.exe";
            string expectedExePath = Path.Combine(rawOutputDir, expectedExeName);
            if (!File.Exists(expectedExePath))
            {
                throw new FileNotFoundException(
                    $"dotnet publish 已完成（退出码 0）但输出目录中未找到 {expectedExeName}。" +
                    $"可能原因：有进程锁定了输出文件，或增量编译异常。" +
                    $"请关闭正在运行的应用后重试，或先执行 dotnet clean。预期路径：{expectedExePath}");
            }

            SendLog(">>> 编译成功！原始文件已生成。", LogLevel.Success);

            if (config.MakeInstaller)
            {
                if (string.IsNullOrWhiteSpace(config.SetupOutputDir))
                {
                    throw new ArgumentException("未设置安装包输出目录");
                }

                string setupOutputDir = ArgumentSanitizer.ValidatePath(config.SetupOutputDir, "安装包输出目录");
                string innoSetupPath = ResolveInnoSetupPath(config.InnoSetupPath);
                string issPath = ResolveIssPath(projectPath, config.IssScriptPath);

                if (!string.IsNullOrWhiteSpace(windowsIconPath) &&
                    !IconAssetHelper.SupportsInstallerIconDefine(issPath))
                {
                    throw new InvalidOperationException(
                        $"当前安装脚本未接入正式图标宏 MySetupIconFile，无法应用安装包图标：{issPath}");
                }

                // 安装包输出目录同样要做安全检查
                AssertSafeOutputDirectory(setupOutputDir, projectDir, rawOutputDir);
                PrepareOutputDirectory(setupOutputDir);
                SendLog(">>> [2/2] 正在生成 Inno Setup 安装包...", false);

                if (!string.IsNullOrWhiteSpace(windowsIconPath))
                {
                    SendLog(">>> 安装包图标已通过正式脚本宏参数接入。", false);
                }

                // ISCC /d 参数值传裸值：含空格时由 ProcessStartInfo.ArgumentList 整体加引号。
                // 不要用 EscapeInnoDefine 包裹内层双引号 —— ISCC 命令行会把引号当值的一部分。
                string safeAppName = config.AppName ?? "App";
                string safePublisher = config.Publisher ?? "Unknown";
                string safeAppId = !string.IsNullOrWhiteSpace(config.AppID)
                    ? ArgumentSanitizer.ValidateInnoAppId(config.AppID)
                    : "{{" + Guid.NewGuid().ToString().ToUpper() + "}";

                var isccArgs = new List<string>();
                if (!string.IsNullOrWhiteSpace(windowsIconPath))
                {
                    isccArgs.Add($"/dMySetupIconFile={windowsIconPath}");
                }
                isccArgs.Add($"/dSourcePath={rawOutputDir}");
                // 注意：ISCC 的 /d 命令行指令不把双引号当字符串分隔符，而是当作值的一部分。
                // 因此 /d 参数值必须传裸值，不能像 #define 那样用 "..." 包裹。
                // 含空格的值由 ProcessStartInfo.ArgumentList 自动整体加引号处理。
                // 旧实现误用 EscapeInnoDefine（给值加了内层双引号），导致 ISCC 拼出的路径变成
                // release\"BlueSapphire".exe 而找不到文件，触发 "SourcePath must contain..." 错误。
                isccArgs.Add($"/dMyAppName={safeAppName}");
                isccArgs.Add($"/dMyAppVersion={validatedVersion}");
                isccArgs.Add($"/dMyAppPublisher={safePublisher}");
                isccArgs.Add($"/dMyAppId={safeAppId}");
                isccArgs.Add($"/O{setupOutputDir}");
                isccArgs.Add($"/F{safeAppName}_Setup_v{validatedVersion}");
                isccArgs.Add(issPath);

                StageExecutionResult installerStage = await RunCommandAsync(
                    innoSetupPath,
                    isccArgs,
                    Encoding.UTF8,
                    stagePlan["windows-installer"].StartProgress,
                    stagePlan["windows-installer"].EndProgress,
                    "windows-installer",
                    "Inno Setup Installer",
                    monitorDirectory: setupOutputDir,
                    progressReference: FindStageProfile(previousProfile, "windows-installer"));
                stageResults.Add(installerStage);

                SendLog(">>> 安装包制作完成！", LogLevel.Success);
            }

            string outputDirectory = config.MakeInstaller
                ? Path.GetFullPath(config.SetupOutputDir!)
                : rawOutputDir;

            return new BuildResult
            {
                OutputDirectory = outputDirectory
            };
        }


        /// <summary>
        /// Windows 自定义命令构建旁路：用于 Tauri / Node 等非 .NET 工程。
        /// 在工程目录执行用户配置的构建命令（默认 npm run tauri build），
        /// 由原生工具链自行产出安装包，再递归搜索 .exe / .msi 归集到 SetupOutputDir。
        /// </summary>
        private async Task<BuildResult> BuildWindowsCustomAsync(
            AppConfig config,
            BuildProgressProfile? previousProfile,
            List<StageExecutionResult> stageResults)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (string.IsNullOrWhiteSpace(config.SetupOutputDir))
            {
                throw new ArgumentException("未设置安装包输出目录");
            }

            string projectPath = Path.GetFullPath(config.ProjectPath!);
            string projectDir = Path.GetDirectoryName(projectPath)!;

            // 工作目录：优先用户配置，否则用 ProjectPath 所在目录
            string workingDir = !string.IsNullOrWhiteSpace(config.WindowsBuildWorkingDir)
                ? Path.GetFullPath(config.WindowsBuildWorkingDir)
                : projectDir;

            if (!Directory.Exists(workingDir))
            {
                throw new DirectoryNotFoundException($"构建工作目录不存在：{workingDir}");
            }

            // 构建命令：留空时默认 npm run tauri build
            string buildCommand = !string.IsNullOrWhiteSpace(config.WindowsBuildCommand)
                ? config.WindowsBuildCommand!
                : "npm run tauri build";

            // 产物搜索目录：留空时默认 src-tauri/target/release/bundle
            string artifactDir = !string.IsNullOrWhiteSpace(config.WindowsBuildArtifactDir)
                ? Path.GetFullPath(config.WindowsBuildArtifactDir)
                : Path.GetFullPath(Path.Combine(projectDir, "src-tauri", "target", "release", "bundle"));

            // 进度监听用 target 目录（产物 bundle 前期不存在，但 target 下 .o/.rmeta 增长可反映进度）
            string monitorTarget = Path.GetFullPath(Path.Combine(projectDir, "src-tauri", "target"));
            string setupOutputDir = Path.GetFullPath(config.SetupOutputDir);
            PrepareOutputDirectory(setupOutputDir);
            AssertSafeOutputDirectory(setupOutputDir, workingDir, artifactDir, projectDir);

            SendLog($">>> 检测到非 .NET 工程（{Path.GetFileName(projectPath)}），启用自定义命令构建。", false);
            SendLog($">>> 工作目录：{workingDir}", false);
            SendLog($">>> 构建命令：{buildCommand}", false);
            SendLog($">>> 产物搜索目录：{artifactDir}", false);
            ReportProgress(5);

            // 通过 cmd /c 执行，保证 npm/tauri 等 PATH 中的工具可被找到
            var stagePlan = CreateStagePlan(previousProfile, 5.0, 95.0,
                ("windows-custom-build", 90.0));

            // raw 模式：整个 "/c <命令>" 作为单个 Arguments 字符串交给 cmd，避免 ArgumentList 对带空格/&&/引号的命令二次加引号
            string rawArgs = "/c " + buildCommand;
            StageExecutionResult buildStage = await RunCommandAsync(
                "cmd.exe",
                new[] { rawArgs },
                Encoding.UTF8,
                stagePlan["windows-custom-build"].StartProgress,
                stagePlan["windows-custom-build"].EndProgress,
                "windows-custom-build",
                "自定义构建",
                workingDirectory: workingDir,
                monitorDirectory: monitorTarget,
                progressReference: FindStageProfile(previousProfile, "windows-custom-build"),
                useRawArguments: true);

            SendLog(">>> 构建命令执行完成，正在归集安装包产物...", false);
            ReportProgress(96);

            // 在产物目录递归搜索安装包（优先体积最大的 .exe，其次 .msi）
            string? primaryArtifact = FindInstallerArtifact(artifactDir);

            if (primaryArtifact == null)
            {
                throw new InvalidOperationException(
                    $"未在产物目录找到安装包 (.exe / .msi)：{artifactDir}。" +
                    "请检查构建命令是否正确，或手动指定 WindowsBuildArtifactDir。");
            }

            string finalPath = Path.Combine(setupOutputDir, Path.GetFileName(primaryArtifact));
            // Tauri 产物刚生成时常被 NSIS 进程或杀软扫描占用，做有限次重试
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    File.Copy(primaryArtifact, finalPath, overwrite: true);
                    break;
                }
                catch (IOException) when (attempt < 5)
                {
                    SendLog($">>> 产物文件被占用，{500 * (int)Math.Pow(2, attempt)}ms 后重试...", LogLevel.Warning);
                    await Task.Delay(500 * (int)Math.Pow(2, attempt));
                }
            }
            SendLog($">>> 安装包已归集：{finalPath}", LogLevel.Success);
            ReportProgress(99);

            return new BuildResult
            {
                OutputDirectory = setupOutputDir,
                PrimaryOutputPath = finalPath
            };
        }

        /// <summary>
        /// 在指定目录递归查找安装包产物，优先体积最大的 .exe，其次 .msi。
        /// </summary>
        /// <summary>
        /// 在产物目录递归查找安装包产物。优先按目录语义筛选，避免误选主程序或无关 exe：
        ///   1) bundle/nsis/*.exe （Tauri NSIS 安装包）
        ///   2) bundle/msi/*.msi
        ///   3) 文件名含 setup/-setup 的 .exe/.msi
        ///   4) 兜底：递归所有 .exe/.msi 按体积降序，但排除与 searchDir 同级或更上层的主程序裸 exe
        /// </summary>
        private static string? FindInstallerArtifact(string searchDir)
        {
            if (!Directory.Exists(searchDir))
            {
                return null;
            }
 
            StringComparer cmp = StringComparer.OrdinalIgnoreCase;
 
            // 1) Tauri 默认 NSIS 输出位置
            string nsisDir = Path.Combine(searchDir, "nsis");
            var nsisHit = Directory.EnumerateFiles(nsisDir, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(f => !IsLikelyMainExecutable(f))
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
            if (nsisHit != null) return nsisHit;
 
            // 2) MSI
            string msiDir = Path.Combine(searchDir, "msi");
            var msiHit = Directory.EnumerateFiles(msiDir, "*.msi", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
            if (msiHit != null) return msiHit;
 
            // 3) 任意目录下文件名含 setup 的安装包
            var setupHit = Directory.EnumerateFiles(searchDir, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string ext = Path.GetExtension(f);
                    if (!(cmp.Equals(ext, ".exe") || cmp.Equals(ext, ".msi"))) return false;
                    return Path.GetFileNameWithoutExtension(f).IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
            if (setupHit != null) return setupHit;
 
            // 4) 兜底：递归排除主程序后按体积取最大
            return Directory.EnumerateFiles(searchDir, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string ext = Path.GetExtension(f);
                    return (cmp.Equals(ext, ".exe") || cmp.Equals(ext, ".msi")) && !IsLikelyMainExecutable(f);
                })
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
        }
 
        /// <summary>
        /// 判断某 exe 是否像"主程序本体"而非安装包：紧邻 bundle 目录（即 target/release 下的裸 exe）
        /// 且文件名不含 setup。这类是 Tauri/Cargo 产出的可执行主程序，不能当安装包分发。
        /// </summary>
        private static bool IsLikelyMainExecutable(string filePath)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(filePath);
                if (name.IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                string? dir = Path.GetDirectoryName(filePath);
                string? parent = string.IsNullOrEmpty(dir) ? null : Path.GetDirectoryName(dir);
                string? grand = string.IsNullOrEmpty(parent) ? null : Path.GetFileName(parent);
                // bundle 的父目录是 release，主程序裸 exe 直接落在 release 下（即 bundle 的同级）
                return string.Equals(grand, "release", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
        private async Task<BuildResult> BuildAndroidAsync(
            AppConfig config,
            BuildProgressProfile? previousProfile,
            List<StageExecutionResult> stageResults)
        {
            if (string.IsNullOrWhiteSpace(config.AndroidProjectRoot))
            {
                throw new ArgumentException("未设置 Android 项目根目录");
            }

            string projectRoot = Path.GetFullPath(config.AndroidProjectRoot);
            AndroidProjectInfo projectInfo = AndroidProjectHelper.InspectProject(projectRoot, config.AndroidModuleName, config.AndroidBuildType);
            string moduleName = projectInfo.PreferredModuleName
                ?? throw new InvalidOperationException("未检测到可用于打包 APK 的 Android application 模块。");
            string buildType = AndroidProjectHelper.NormalizeBuildType(config.AndroidBuildType);
            string moduleDirectory = AndroidProjectHelper.ResolveModuleDirectory(projectRoot, moduleName);
            string? androidIconPath = IconAssetHelper.ResolveOptionalIconPath(config.AndroidIconPath);
            string taskName = $":{moduleName}:assemble{buildType}";

            SendLog(">>> [0/2] 正在检查 Android Gradle 环境...", false);
            ReportProgress(5);
            SendLog($">>> 模块：{moduleName} | 构建类型：{buildType} APK", false);
            SendLog($">>> 签名状态：{projectInfo.SigningStatus}", false);

            // 签名方案切换检测：Debug ↔ Release 使用不同签名密钥，无法直接覆盖安装
            if (!string.IsNullOrWhiteSpace(config.LastAndroidBuildType) &&
                !string.Equals(config.LastAndroidBuildType, buildType, StringComparison.OrdinalIgnoreCase))
            {
                SendLog("", LogLevel.Warning);
                SendLog($"╔══════════════════════════════════════════════════════════════╗", LogLevel.Warning);
                SendLog($"║ ⚠️  签名方案已变更：{config.LastAndroidBuildType} → {buildType}", LogLevel.Warning);
                SendLog($"║                                                               ║", LogLevel.Warning);
                SendLog($"║  Debug 和 Release 使用不同的签名密钥，直接覆盖安装会失败。    ║", LogLevel.Warning);
                SendLog($"║  请先在手机上卸载旧版 APK，再安装本次构建的 APK。             ║", LogLevel.Warning);
                SendLog($"║                                                               ║", LogLevel.Warning);
                SendLog($"║  卸载方法：设置 → 应用 → 找到该应用 → 卸载                   ║", LogLevel.Warning);
                SendLog($"║         或执行：adb uninstall <包名>                          ║", LogLevel.Warning);
                SendLog($"╚══════════════════════════════════════════════════════════════╝", LogLevel.Warning);
                SendLog("", LogLevel.Warning);
            }
            else if (string.IsNullOrWhiteSpace(config.LastAndroidBuildType))
            {
                // 首次构建提醒
                if (buildType == "Release")
                {
                    SendLog(">>> 提示：本次为 Release 构建，如手机上已安装 Debug 版本，需先卸载旧版。", LogLevel.Warning);
                }
            }

            if (!string.IsNullOrWhiteSpace(androidIconPath))
            {
                AndroidIconApplyResult iconApplyResult = IconAssetHelper.ApplyAndroidLauncherIcons(projectRoot, moduleName, androidIconPath);
                SendLog($">>> 已生成 Android 启动图标资源：{iconApplyResult.LauncherResourceName} ({iconApplyResult.GeneratedFiles.Count} 个文件)", LogLevel.Success);
            }
            else if (IconAssetHelper.TryRestoreAndroidLauncherIcons(projectRoot, moduleName))
            {
                SendLog(">>> 未设置 Android 自定义图标，已恢复项目原始启动图标引用。", false);
            }

            SendLog($">>> [1/2] 正在执行 Gradle 任务 {taskName} ...", false);

            var stagePlan = CreateStagePlan(previousProfile, 5.0, 99.0,
                ("android-gradle", 94.0));

            var gradleArgs = new List<string>
            {
                "/c",
                "gradlew.bat",
                taskName,
                "--console=plain"
            };

            StageExecutionResult gradleStage = await RunCommandAsync(
                "cmd.exe",
                gradleArgs,
                Encoding.UTF8,
                stagePlan["android-gradle"].StartProgress,
                stagePlan["android-gradle"].EndProgress,
                "android-gradle",
                $"Android {buildType} APK",
                workingDirectory: projectRoot,
                monitorDirectory: Path.Combine(moduleDirectory, "build"),
                progressReference: FindStageProfile(previousProfile, "android-gradle"));
            stageResults.Add(gradleStage);

            string apkPath = AndroidProjectHelper.ResolveLatestApkPath(moduleDirectory, buildType);
            string finalApkPath = PrepareAndroidOutput(apkPath, config.AndroidApkOutputDir);
            SendLog($">>> APK 生成完成：{finalApkPath}", LogLevel.Success);

            return new BuildResult
            {
                OutputDirectory = Path.GetDirectoryName(finalApkPath),
                PrimaryOutputPath = finalApkPath
            };
        }

        private async Task<StageExecutionResult> RunCommandAsync(
            string fileName,
            IEnumerable<string> arguments,
            Encoding encoding,
            double startProgress,
            double endProgress,
            string stageKey,
            string stageDisplayName,
            string? workingDirectory = null,
            string? monitorDirectory = null,
            StageProgressProfile? progressReference = null,
            bool useRawArguments = false)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding
            };


            if (useRawArguments)
            {
                // raw 模式：把第一个参数整体作为 Arguments 字符串，避免 ArgumentList 对含空格/&&/引号的命令二次加引号
                psi.Arguments = arguments.FirstOrDefault() ?? string.Empty;
            }
            else
            {
                foreach (var arg in arguments)
                {
                    psi.ArgumentList.Add(arg);
                }
            }

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }

            using var process = new Process { StartInfo = psi };
            using var samplingCancellation = new CancellationTokenSource();
            using var timeoutCancellation = new CancellationTokenSource(StageTimeout);

            // 合并采样取消与超时取消
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                samplingCancellation.Token, timeoutCancellation.Token);

            DateTime stageStartUtc = DateTime.UtcNow;
            var stageStopwatch = Stopwatch.StartNew();
            var samples = new List<StageProgressSample>();
            // 使用 Interlocked 保证多线程下进度值的原子更新
            double currentProgress = startProgress;
            bool usesHistoricalReference = progressReference != null;

            process.OutputDataReceived += (sender, e) =>
            {
                string cleaned = CleanAnsi(e.Data);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    SendLog(cleaned, false);

                    if (!usesHistoricalReference)
                    {
                        // 线程安全地更新 currentProgress 并上报
                        double snapshot = Interlocked.CompareExchange(ref currentProgress, 0, 0);
                        double remaining = endProgress - snapshot;
                        double newValue = snapshot + remaining * 0.03;
                        Interlocked.Exchange(ref currentProgress, newValue);
                        ReportProgress(newValue);
                    }
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                string cleaned = CleanAnsi(e.Data);
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    SendLog(cleaned, true);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Task samplingTask = TrackStageProgressAsync(
                startProgress,
                endProgress,
                stageStartUtc,
                stageStopwatch,
                monitorDirectory,
                progressReference,
                samples,
                value =>
                {
                    // 线程安全地推进进度（只允许前进）
                    double snapshot;
                    do
                    {
                        snapshot = Interlocked.CompareExchange(ref currentProgress, 0, 0);
                        if (value <= snapshot)
                        {
                            return;
                        }
                    } while (Interlocked.CompareExchange(ref currentProgress, value, snapshot) != snapshot);

                    ReportProgress(value);
                },
                linkedCancellation.Token);

            // WaitForExitAsync 接受取消令牌，超时后能强制中断等待
            bool timedOut = false;
            try
            {
                await process.WaitForExitAsync(linkedCancellation.Token);
            }
            catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)
            {
                timedOut = true;
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
            }

            samplingCancellation.Cancel();

            try
            {
                await samplingTask;
            }
            catch (OperationCanceledException)
            {
            }

            stageStopwatch.Stop();

            if (timedOut)
            {
                throw new TimeoutException(
                    $"构建阶段超时（{StageTimeout.TotalMinutes:F0} 分钟），已强制终止进程：" +
                    $"{stageDisplayName}。可能是网络异常或子进程死锁。");
            }

            OutputSnapshot finalSnapshot = CaptureOutputSnapshot(monitorDirectory, stageStartUtc);
            samples.Add(new StageProgressSample
            {
                ElapsedMilliseconds = stageStopwatch.ElapsedMilliseconds,
                OutputBytes = finalSnapshot.OutputBytes,
                OutputFileCount = finalSnapshot.OutputFileCount
            });

            if (process.ExitCode != 0)
            {
                throw new Exception($"命令执行失败，退出代码：{process.ExitCode}");
            }

            ReportProgress(endProgress);

            return new StageExecutionResult
            {
                StageKey = stageKey,
                DisplayName = stageDisplayName,
                DurationMilliseconds = stageStopwatch.ElapsedMilliseconds,
                FinalOutputBytes = finalSnapshot.OutputBytes,
                FinalOutputFileCount = finalSnapshot.OutputFileCount,
                Samples = samples
                    .OrderBy(sample => sample.ElapsedMilliseconds)
                    .GroupBy(sample => sample.ElapsedMilliseconds)
                    .Select(group => group.Last())
                    .ToList()
            };
        }

        private void SendLog(string msg, bool isError = false)
            => LogReceived?.Invoke(this, new LogEventArgs(msg, isError ? LogLevel.Error : LogLevel.Info));

        private void SendLog(string msg, LogLevel level)
            => LogReceived?.Invoke(this, new LogEventArgs(msg, level));

        private void ReportProgress(double value) => ProgressChanged?.Invoke(this, value);

        private void SaveBuildProgressProfile(
            AppConfig config,
            string buildKey,
            string description,
            long totalDurationMilliseconds,
            List<StageExecutionResult> stageResults)
        {
            if (stageResults.Count == 0)
            {
                return;
            }

            var profile = new BuildProgressProfile
            {
                BuildKey = buildKey,
                TargetPlatform = string.Equals(config.TargetPlatform, "Android", StringComparison.OrdinalIgnoreCase) ? "Android" : "Windows",
                Description = description,
                UpdatedAtUtc = DateTime.UtcNow,
                TotalDurationMilliseconds = totalDurationMilliseconds,
                Stages = stageResults.Select(stage => new StageProgressProfile
                {
                    StageKey = stage.StageKey,
                    DisplayName = stage.DisplayName,
                    DurationMilliseconds = stage.DurationMilliseconds,
                    FinalOutputBytes = stage.FinalOutputBytes,
                    FinalOutputFileCount = stage.FinalOutputFileCount,
                    Samples = stage.Samples.Select(sample => new StageProgressSample
                    {
                        ElapsedMilliseconds = sample.ElapsedMilliseconds,
                        OutputBytes = sample.OutputBytes,
                        OutputFileCount = sample.OutputFileCount
                    }).ToList()
                }).ToList()
            };

            _progressHistoryStore.Save(profile);
        }

        private static Dictionary<string, (double StartProgress, double EndProgress)> CreateStagePlan(
            BuildProgressProfile? previousProfile,
            double startProgress,
            double endProgress,
            params (string StageKey, double FallbackWeight)[] stages)
        {
            var plan = new Dictionary<string, (double StartProgress, double EndProgress)>(StringComparer.OrdinalIgnoreCase);
            if (stages.Length == 0)
            {
                return plan;
            }

            bool canUseHistory = previousProfile != null &&
                stages.All(stage => (FindStageProfile(previousProfile, stage.StageKey)?.DurationMilliseconds ?? 0) > 0);

            double totalWeight = stages.Sum(stage =>
                canUseHistory
                    ? FindStageProfile(previousProfile, stage.StageKey)!.DurationMilliseconds
                    : stage.FallbackWeight);

            double cursor = startProgress;
            for (int index = 0; index < stages.Length; index++)
            {
                (string stageKey, double fallbackWeight) = stages[index];
                double weight = canUseHistory
                    ? FindStageProfile(previousProfile, stageKey)!.DurationMilliseconds
                    : fallbackWeight;
                double segment = index == stages.Length - 1
                    ? endProgress - cursor
                    : (endProgress - startProgress) * (weight / totalWeight);

                plan[stageKey] = (cursor, cursor + segment);
                cursor += segment;
            }

            return plan;
        }

        private static StageProgressProfile? FindStageProfile(BuildProgressProfile? profile, string stageKey)
        {
            return profile?.Stages.FirstOrDefault(stage =>
                string.Equals(stage.StageKey, stageKey, StringComparison.OrdinalIgnoreCase));
        }

        private async Task TrackStageProgressAsync(
            double startProgress,
            double endProgress,
            DateTime stageStartUtc,
            Stopwatch stageStopwatch,
            string? monitorDirectory,
            StageProgressProfile? progressReference,
            List<StageProgressSample> samples,
            Action<double> reportProgress,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(500, cancellationToken);

                OutputSnapshot snapshot = CaptureOutputSnapshot(monitorDirectory, stageStartUtc);
                samples.Add(new StageProgressSample
                {
                    ElapsedMilliseconds = stageStopwatch.ElapsedMilliseconds,
                    OutputBytes = snapshot.OutputBytes,
                    OutputFileCount = snapshot.OutputFileCount
                });

                if (progressReference == null)
                {
                    continue;
                }

                double stageRatio = EstimateStageProgressRatio(progressReference, stageStopwatch.ElapsedMilliseconds, snapshot);
                double nextProgress = startProgress + ((endProgress - startProgress) * stageRatio);
                nextProgress = Math.Min(endProgress - 0.3, nextProgress);
                reportProgress(nextProgress);
            }
        }

        private static double EstimateStageProgressRatio(
            StageProgressProfile reference,
            long elapsedMilliseconds,
            OutputSnapshot currentSnapshot)
        {
            double timeRatio = reference.DurationMilliseconds > 0
                ? Math.Clamp((double)elapsedMilliseconds / reference.DurationMilliseconds, 0.0, 1.2)
                : 0.0;
            double historicalCurveRatio = GetHistoricalCurveRatio(reference, elapsedMilliseconds);
            double currentOutputRatio = GetCurrentOutputRatio(reference, currentSnapshot);

            if (historicalCurveRatio <= 0 && currentOutputRatio <= 0)
            {
                return Math.Clamp(timeRatio * 0.92, 0.0, 0.98);
            }

            double estimate = (historicalCurveRatio * 0.55) +
                              (currentOutputRatio * 0.30) +
                              (Math.Min(timeRatio, 1.0) * 0.15);

            if (timeRatio > 1.0)
            {
                estimate = Math.Max(estimate, 0.82 + Math.Min(0.15, (timeRatio - 1.0) * 0.18));
            }

            return Math.Clamp(estimate, 0.0, 0.98);
        }

        private static double GetHistoricalCurveRatio(StageProgressProfile reference, long elapsedMilliseconds)
        {
            if (reference.Samples.Count == 0)
            {
                return reference.DurationMilliseconds <= 0
                    ? 0
                    : Math.Clamp((double)elapsedMilliseconds / reference.DurationMilliseconds, 0.0, 0.98);
            }

            List<StageProgressSample> orderedSamples = reference.Samples
                .OrderBy(sample => sample.ElapsedMilliseconds)
                .ToList();

            if (elapsedMilliseconds <= orderedSamples[0].ElapsedMilliseconds)
            {
                return GetSampleRatio(reference, orderedSamples[0]);
            }

            for (int index = 1; index < orderedSamples.Count; index++)
            {
                StageProgressSample previous = orderedSamples[index - 1];
                StageProgressSample next = orderedSamples[index];
                if (elapsedMilliseconds > next.ElapsedMilliseconds)
                {
                    continue;
                }

                long span = Math.Max(1, next.ElapsedMilliseconds - previous.ElapsedMilliseconds);
                double t = (double)(elapsedMilliseconds - previous.ElapsedMilliseconds) / span;
                double previousRatio = GetSampleRatio(reference, previous);
                double nextRatio = GetSampleRatio(reference, next);
                return previousRatio + ((nextRatio - previousRatio) * t);
            }

            double lastRatio = GetSampleRatio(reference, orderedSamples[^1]);
            double timeRatio = reference.DurationMilliseconds > 0
                ? Math.Clamp((double)elapsedMilliseconds / reference.DurationMilliseconds, 0.0, 0.98)
                : 0.0;
            return Math.Max(lastRatio, timeRatio);
        }

        private static double GetSampleRatio(StageProgressProfile reference, StageProgressSample sample)
        {
            double bytesRatio = reference.FinalOutputBytes > 0
                ? (double)sample.OutputBytes / reference.FinalOutputBytes
                : -1;
            double fileRatio = reference.FinalOutputFileCount > 0
                ? (double)sample.OutputFileCount / reference.FinalOutputFileCount
                : -1;
            double fallbackRatio = reference.DurationMilliseconds > 0
                ? (double)sample.ElapsedMilliseconds / reference.DurationMilliseconds
                : 0;

            return CombineProgressRatio(bytesRatio, fileRatio, fallbackRatio);
        }

        private static double GetCurrentOutputRatio(StageProgressProfile reference, OutputSnapshot snapshot)
        {
            double bytesRatio = reference.FinalOutputBytes > 0
                ? (double)snapshot.OutputBytes / reference.FinalOutputBytes
                : -1;
            double fileRatio = reference.FinalOutputFileCount > 0
                ? (double)snapshot.OutputFileCount / reference.FinalOutputFileCount
                : -1;
            return CombineProgressRatio(bytesRatio, fileRatio, -1);
        }

        private static double CombineProgressRatio(double bytesRatio, double fileRatio, double fallbackRatio)
        {
            bool hasBytes = bytesRatio >= 0;
            bool hasFiles = fileRatio >= 0;

            if (hasBytes && hasFiles)
            {
                return Math.Clamp((bytesRatio * 0.7) + (fileRatio * 0.3), 0.0, 1.0);
            }

            if (hasBytes)
            {
                return Math.Clamp(bytesRatio, 0.0, 1.0);
            }

            if (hasFiles)
            {
                return Math.Clamp(fileRatio, 0.0, 1.0);
            }

            return Math.Clamp(fallbackRatio, 0.0, 1.0);
        }

        private static OutputSnapshot CaptureOutputSnapshot(string? monitorDirectory, DateTime stageStartUtc)
        {
            if (string.IsNullOrWhiteSpace(monitorDirectory) || !Directory.Exists(monitorDirectory))
            {
                return new OutputSnapshot(0, 0);
            }

            long outputBytes = 0;
            int outputFileCount = 0;
            DateTime threshold = stageStartUtc.AddSeconds(-1);

            try
            {
                foreach (string filePath in Directory.EnumerateFiles(monitorDirectory, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (!fileInfo.Exists || fileInfo.LastWriteTimeUtc < threshold)
                        {
                            continue;
                        }

                        outputBytes += fileInfo.Length;
                        outputFileCount++;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
                return new OutputSnapshot(outputBytes, outputFileCount);
            }

            return new OutputSnapshot(outputBytes, outputFileCount);
        }

        private static string FormatDuration(long durationMilliseconds)
        {
            if (durationMilliseconds < 1000)
            {
                return $"{durationMilliseconds}ms";
            }

            var duration = TimeSpan.FromMilliseconds(durationMilliseconds);
            return duration.TotalMinutes >= 1
                ? $"{(int)duration.TotalMinutes}分{duration.Seconds:D2}秒"
                : $"{duration.Seconds}.{duration.Milliseconds / 100:D1}秒";
        }

        private static void PrepareOutputDirectory(string outputDir)
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
            }

            Directory.CreateDirectory(outputDir);
        }


        /// <summary>
        /// 防止用户把安装包输出目录误设为工程/工作/产物目录的自身或上级，
        /// 否则 PrepareOutputDirectory 的递归删除会清掉源码或 target 产物树。
        /// </summary>
        private static void AssertSafeOutputDirectory(string outputDir, params string[] protectedDirs)
        {
            string norm(string? p) => string.IsNullOrWhiteSpace(p) ? string.Empty : Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string o = norm(outputDir);
            foreach (string pd in protectedDirs)
            {
                string n = norm(pd);
                if (n.Length == 0) continue;
                if (string.Equals(o, n, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"安装包输出目录与受保护目录重叠，拒绝执行以免删除源数据：{o}");
                }
                // outputDir 是某受保护目录的上级也不行（会递归删除其下所有内容）
                if (o.Length < n.Length && n.StartsWith(o + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"安装包输出目录是工程/产物目录的上级，拒绝执行以免误删：{o} 包含 {n}");
                }
            }
        }

        private static string ResolveInnoSetupPath(string? innoSetupPath)
        {
            if (string.IsNullOrWhiteSpace(innoSetupPath))
            {
                throw new FileNotFoundException("Inno Setup 路径未配置，无法生成安装包。请先选择 ISCC.exe。");
            }

            string fullPath = Path.GetFullPath(innoSetupPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"找不到 Inno Setup 编译器：{fullPath}");
            }

            return fullPath;
        }

        private static string ResolveIssPath(string projectPath, string? issScriptPath)
        {
            string? resolvedPath = issScriptPath;

            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                string projectDir = Path.GetDirectoryName(projectPath)
                    ?? throw new DirectoryNotFoundException("无法定位项目目录，无法自动查找 installer.iss。");

                resolvedPath = Path.Combine(projectDir, "installer.iss");
            }

            string fullPath = Path.GetFullPath(resolvedPath!);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"找不到 Inno Setup 脚本：{fullPath}");
            }

            return fullPath;
        }

        private static string PrepareAndroidOutput(string apkPath, string? customOutputDirectory)
        {
            string sourceApkPath = Path.GetFullPath(apkPath);

            if (string.IsNullOrWhiteSpace(customOutputDirectory))
            {
                return sourceApkPath;
            }

            string outputDirectory = Path.GetFullPath(customOutputDirectory);
            Directory.CreateDirectory(outputDirectory);

            string destinationPath = Path.Combine(outputDirectory, Path.GetFileName(sourceApkPath));
            if (string.Equals(sourceApkPath, destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                return sourceApkPath;
            }

            File.Copy(sourceApkPath, destinationPath, true);
            return destinationPath;
        }

        private static string CleanAnsi(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }

            return Regex.Replace(input, @"\x1B\[[^@-~]*[@-~]", string.Empty);
        }
    }
}