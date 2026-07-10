using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading; // 🌟 引入定时器
using BlueSapphire.Builder.Helpers;
using BlueSapphire.Builder.Services;
using BlueSapphire.Builder.ViewModels;

namespace BlueSapphire.Builder
{
    public partial class MainWindow : Window
    {
        // 配置文件改存到 %APPDATA%\BlueSapphire.Builder，便携安装也能写入
        private static readonly string ConfigFolder = GetAppDataFolder();
        private static readonly string ConfigFilePath = Path.Combine(ConfigFolder, "builder_config.json");
        private const string ConfigFileName = "builder_config.json";

        private readonly BuilderService _builderService = new BuilderService();
        private readonly MainViewModel _viewModel = new MainViewModel();

        // 🌟 新增：用于丝滑滚动动画的变量
        private double _targetProgress = 0;
        private double _currentDisplayProgress = 0;
        private DispatcherTimer _smoothTimer = new DispatcherTimer();

        // 日志最大保留行数，避免长时间构建累积过多内存
        private const int MaxLogLines = 2000;

        private static string GetAppDataFolder()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "BlueSapphire.Builder");
            try
            {
                Directory.CreateDirectory(folder);
                return folder;
            }
            catch
            {
                // 回退到 exe 同目录（便携模式）
                return AppContext.BaseDirectory;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;

            // 关键：把 UI 线程 Dispatcher 注入 IconAssetHelper，让后台线程渲染时能调度回 UI 线程
            IconAssetHelper.InitializeDispatcher(Dispatcher);

            _builderService.LogReceived += (s, e) => Dispatcher.BeginInvoke(() => AppendLog(e.Message, e.Level));

            // ==========================================
            // 🚀 极客丝滑进度条引擎 (60FPS Ease-Out 缓动)
            // ==========================================
            _smoothTimer.Interval = TimeSpan.FromMilliseconds(16); // 约等于 60 帧的刷新率
            _smoothTimer.Tick += (s, e) =>
            {
                // 如果当前显示的进度和真实目标进度有差距，就平滑追赶
                if (Math.Abs(_currentDisplayProgress - _targetProgress) > 0.1)
                {
                    // 缓动公式：距离越近，滚动越慢，极具高级感
                    _currentDisplayProgress += (_targetProgress - _currentDisplayProgress) * 0.12;
                    _viewModel.ProgressValue = _currentDisplayProgress;
                }
                else if (_currentDisplayProgress != _targetProgress)
                {
                    // 误差极小时直接对齐
                    _currentDisplayProgress = _targetProgress;
                    _viewModel.ProgressValue = _currentDisplayProgress;
                }
            };
            _smoothTimer.Start();

            _builderService.ProgressChanged += (s, val) => Dispatcher.BeginInvoke(() => {
                // 🛡️ 强制保障锁：进度只能前进，绝对不允许倒退哪怕 0.1%！
                if (val > _targetProgress)
                {
                    _targetProgress = val;
                }
                _viewModel.ProgressText = val >= 100
                    ? ">>> 序列构建完成"
                    : $"智能估算中... {val:0.0}%";
            });
            // ==========================================

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 用绝对路径读取配置，避免依赖 CWD
            string configPath = Path.Combine(ConfigFolder, ConfigFileName);
            if (File.Exists(configPath))
            {
                try
                {
                    string json = SafeFileWriter.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    _viewModel.LoadFromConfig(config);

                    // 仅当用户没设过 InnoSetup 路径时自动检测，不要把提示语当路径写入
                    if (string.IsNullOrWhiteSpace(_viewModel.InnoSetupPath))
                    {
                        _viewModel.InnoSetupPath = PathHelper.FindInnoSetup();
                    }

                    _viewModel.RefreshMetadata();
                    _viewModel.RefreshPresetNames();
                    return;
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    AppendLog($"[配置读取提示] 载入 {ConfigFileName} 失败，将使用默认参数。说明: {ex.Message}", true);
                }
            }
            _viewModel.AppName = "BlueSapphire";
            // 关键修复：找不到 Inno Setup 时不要把提示语写入 ViewModel 当路径
            string? detected = PathHelper.FindInnoSetup();
            if (!string.IsNullOrWhiteSpace(detected))
            {
                _viewModel.InnoSetupPath = detected;
            }
            _viewModel.RefreshMetadata();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                var config = _viewModel.ToConfig();
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                // 原子写：避免进程崩溃留下半截 JSON
                SafeFileWriter.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 持久化失败不应阻断窗口关闭
                AppendLog($"[配置保存提示] {ConfigFileName} 保存失败：{ex.Message}", true);
            }
        }

        private async void BtnBuild_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Document.Blocks.Clear();

            // 重置进度
            _targetProgress = 0;
            _currentDisplayProgress = 0;
            _viewModel.ProgressValue = 0;

            _viewModel.ProgressText = "系统初始化...";
            _viewModel.IsBuilding = true;

            var currentConfig = _viewModel.ToConfig();

            try
            {
                BuildResult result = await _builderService.BuildAsync(currentConfig);

                // 构建成功后记录本次构建类型，用于下次检测签名方案切换
                if (currentConfig.TargetPlatform == "Android")
                {
                    _viewModel.LastAndroidBuildType = currentConfig.AndroidBuildType;
                }

                // 构建成功后强制推满进度条
                _targetProgress = 100;

                string outputPath = result.PrimaryOutputPath ?? result.OutputDirectory ?? "未返回输出路径";
                MessageBox.Show($"构建成功！\n输出位置: {outputPath}", "恭喜");
                OpenOutputLocation(result);
                PromptSavePresetAfterBuild();
            }
            catch (Exception ex)
            {
                AppendLog($"[严重错误] {ex.Message}", true);
                MessageBox.Show($"构建失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _viewModel.IsBuilding = false;
            }
        }

        private void BtnMin_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
        {
            string logText = new System.Windows.Documents.TextRange(TxtLog.Document.ContentStart, TxtLog.Document.ContentEnd).Text;
            if (!string.IsNullOrWhiteSpace(logText)) Clipboard.SetText(logText);
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            _logEntries.Clear();
            TxtLog.Document.Blocks.Clear();
        }

        private string? PickFolder(string? currentPath)
        {
            var dialog = new OpenFolderDialog { Title = "请选择文件夹", Multiselect = false };
            if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                dialog.InitialDirectory = currentPath;
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        private string? PickIconFile(string? currentPath)
        {
            var dialog = new OpenFileDialog { Filter = "图标文件|*.ico" };
            if (!string.IsNullOrWhiteSpace(currentPath) && File.Exists(currentPath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
                dialog.FileName = Path.GetFileName(currentPath);
            }

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private void BtnSelectWindowsMode_Click(object sender, RoutedEventArgs e) => _viewModel.TargetPlatform = "Windows";
        private void BtnSelectAndroidMode_Click(object sender, RoutedEventArgs e) => _viewModel.TargetPlatform = "Android";
        private void BtnSelectAndroidDebugMode_Click(object sender, RoutedEventArgs e) => _viewModel.AndroidBuildType = "Debug";

        // ============ PathPicker 路径变更事件处理 ============
        // 这些方法替代原先的 BtnBrowse* 系列按钮，由 PathPicker 控件触发。

        private void CustomBuildCmdPicker_PathChanged(object? sender, string? e)
            => _viewModel.WindowsBuildCommand = e;

        private void CustomWorkingDirPicker_PathChanged(object? sender, string? e)
            => _viewModel.WindowsBuildWorkingDir = e;

        private void CustomArtifactDirPicker_PathChanged(object? sender, string? e)
            => _viewModel.WindowsBuildArtifactDir = e;

        private void CsprojPicker_PathChanged(object? sender, string? e)
        {
            if (string.IsNullOrWhiteSpace(e)) return;
            _viewModel.ProjectPath = e;
            string projDir = Path.GetDirectoryName(e)!;
            string ext = Path.GetExtension(e);
            bool isCsproj = string.Equals(ext, ".csproj", StringComparison.OrdinalIgnoreCase);

            if (isCsproj)
            {
                _viewModel.RawOutputDir = Path.Combine(projDir, "bin", "Publish");
                _viewModel.SetupOutputDir = Path.Combine(projDir, "bin", "Installer");
            }
            else
            {
                // Tauri / Node 工程
                _viewModel.SetupOutputDir = Path.Combine(projDir, "dist", "Installer");
                if (string.IsNullOrWhiteSpace(_viewModel.WindowsBuildArtifactDir))
                {
                    _viewModel.WindowsBuildArtifactDir = Path.Combine(projDir, "src-tauri", "target", "release", "bundle");
                }
            }

            if (string.IsNullOrWhiteSpace(_viewModel.AppName))
            {
                _viewModel.AppName = Path.GetFileNameWithoutExtension(e);
            }

            // ── 自动探测环境配置 ──
            AutoDetectEnvironment(projDir, isCsproj ? e : null, isCsproj);
        }

        /// <summary>
        /// 选完项目文件后自动探测环境配置（ISCC、.iss、.ico、构建命令等），
        /// 找到的自动填入，找不到的以 Warning 日志反馈。
        /// </summary>
        private void AutoDetectEnvironment(string projDir, string? csprojPath, bool isCsproj)
        {
            AppendLog(">>> 正在自动探测环境配置...", LogLevel.Info);

            // ── .NET 项目路径 ──
            if (isCsproj)
            {
                // ISCC.exe（仅当用户未设过时探测）
                if (string.IsNullOrWhiteSpace(_viewModel.InnoSetupPath))
                {
                    string? iscc = PathHelper.FindInnoSetup();
                    if (!string.IsNullOrEmpty(iscc))
                    {
                        _viewModel.InnoSetupPath = iscc;
                        AppendLog($">>> [自动检测] ISCC.exe → {iscc}", LogLevel.Success);
                    }
                    else
                    {
                        AppendLog(">>> [自动检测] 未找到 Inno Setup (ISCC.exe)，请手动指定。", LogLevel.Warning);
                    }
                }

                // .iss 脚本
                if (string.IsNullOrWhiteSpace(_viewModel.IssScriptPath))
                {
                    string? iss = PathHelper.FindIssScript(projDir);
                    if (!string.IsNullOrEmpty(iss))
                    {
                        _viewModel.IssScriptPath = iss;
                        AppendLog($">>> [自动检测] 安装脚本 → {iss}", LogLevel.Success);
                    }
                    else
                    {
                        AppendLog(">>> [自动检测] 未找到 .iss 安装脚本，请手动指定。", LogLevel.Warning);
                    }
                }

                // .ico 图标
                if (string.IsNullOrWhiteSpace(_viewModel.WindowsIconPath))
                {
                    string? ico = PathHelper.FindIcon(projDir, csprojPath);
                    if (!string.IsNullOrEmpty(ico))
                    {
                        _viewModel.WindowsIconPath = ico;
                        AppendLog($">>> [自动检测] 应用图标 → {ico}", LogLevel.Success);
                    }
                    else
                    {
                        AppendLog(">>> [自动检测] 未找到 .ico 图标，请手动指定。", LogLevel.Warning);
                    }
                }
            }
            else
            {
                // ── Tauri / Node 自定义构建路径 ──
                if (string.IsNullOrWhiteSpace(_viewModel.WindowsBuildCommand))
                {
                    string? cmd = PathHelper.FindCustomBuildCommand(projDir);
                    if (!string.IsNullOrEmpty(cmd))
                    {
                        _viewModel.WindowsBuildCommand = cmd;
                        AppendLog($">>> [自动检测] 构建命令 → {cmd}", LogLevel.Success);
                    }
                    else
                    {
                        AppendLog(">>> [自动检测] 未找到构建命令，请手动指定。", LogLevel.Warning);
                    }
                }

                if (string.IsNullOrWhiteSpace(_viewModel.WindowsBuildWorkingDir))
                {
                    string? workDir = PathHelper.FindWorkingDir(projDir);
                    if (!string.IsNullOrEmpty(workDir))
                    {
                        _viewModel.WindowsBuildWorkingDir = workDir;
                        AppendLog($">>> [自动检测] 工作目录 → {workDir}", LogLevel.Success);
                    }
                }

                // Tauri 项目也检测图标
                if (string.IsNullOrWhiteSpace(_viewModel.WindowsIconPath))
                {
                    string? ico = PathHelper.FindIcon(projDir);
                    if (!string.IsNullOrEmpty(ico))
                    {
                        _viewModel.WindowsIconPath = ico;
                        AppendLog($">>> [自动检测] 应用图标 → {ico}", LogLevel.Success);
                    }
                    else
                    {
                        AppendLog(">>> [自动检测] 未找到 .ico 图标，请手动指定。", LogLevel.Warning);
                    }
                }
            }

            AppendLog(">>> 环境配置探测完成。", LogLevel.Info);
        }

        private void InnoPicker_PathChanged(object? sender, string? e)
            => _viewModel.InnoSetupPath = e;

        private void IssPicker_PathChanged(object? sender, string? e)
            => _viewModel.IssScriptPath = e;

        private void WindowsIconPicker_PathChanged(object? sender, string? e)
            => _viewModel.WindowsIconPath = e;

        private void RawOutputPicker_PathChanged(object? sender, string? e)
            => _viewModel.RawOutputDir = e;

        private void SetupOutputPicker_PathChanged(object? sender, string? e)
            => _viewModel.SetupOutputDir = e;

        private void AndroidRootPicker_PathChanged(object? sender, string? e)
        {
            if (string.IsNullOrWhiteSpace(e)) return;
            _viewModel.AndroidProjectRoot = e;
            if (string.IsNullOrWhiteSpace(_viewModel.AppName))
            {
                _viewModel.AppName = Path.GetFileName(e.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }

            // ── 自动检测 Android 环境配置 ──
            AutoDetectAndroidEnvironment(e);
        }

        /// <summary>
        /// 选择 Android 项目根目录后，自动检测环境（gradlew、模块、签名、图标等），
        /// 结果实时反馈到日志面板。
        /// </summary>
        private void AutoDetectAndroidEnvironment(string projectRoot)
        {
            AppendLog(">>> 正在自动探测 Android 环境配置...", LogLevel.Info);

            // 1. 检查 gradlew.bat
            string gradlewPath = Path.Combine(projectRoot, "gradlew.bat");
            if (File.Exists(gradlewPath))
            {
                AppendLog($">>> [自动检测] Gradle Wrapper → 已找到 gradlew.bat", LogLevel.Success);
            }
            else
            {
                AppendLog(">>> [自动检测] 未找到 gradlew.bat，这可能不是标准 Android 项目根目录。", LogLevel.Warning);
            }

            // 2. 检查 settings.gradle
            string settingsGradle = Path.Combine(projectRoot, "settings.gradle");
            string settingsGradleKts = Path.Combine(projectRoot, "settings.gradle.kts");
            if (File.Exists(settingsGradle) || File.Exists(settingsGradleKts))
            {
                AppendLog(">>> [自动检测] Gradle Settings → 已找到 settings.gradle", LogLevel.Success);
            }
            else
            {
                AppendLog(">>> [自动检测] 未找到 settings.gradle(.kts)，无法识别项目模块结构。", LogLevel.Warning);
            }

            // 3. 触发 ViewModel 的元数据刷新（检测模块名、签名状态、APK 输出路径）
            _viewModel.RefreshAndroidProjectMetadata();

            // 4. 根据刷新结果输出日志
            if (!string.IsNullOrWhiteSpace(_viewModel.AndroidDetectedModulesSummary) &&
                !_viewModel.AndroidDetectedModulesSummary.Contains("请选择") &&
                !_viewModel.AndroidDetectedModulesSummary.Contains("识别失败"))
            {
                AppendLog($">>> [自动检测] 应用模块 → {_viewModel.AndroidModuleName}", LogLevel.Success);
                AppendLog($">>> [自动检测] 模块列表 → {_viewModel.AndroidDetectedModulesSummary}", LogLevel.Info);
            }
            else if (_viewModel.AndroidDetectedModulesSummary?.Contains("识别失败") == true)
            {
                AppendLog($">>> [自动检测] 模块识别失败：{_viewModel.AndroidDetectedModulesSummary}", LogLevel.Warning);
            }

            // 5. 签名状态反馈
            if (_viewModel.AndroidSigningReady)
            {
                AppendLog(">>> [自动检测] Release 签名 → keystore 已就绪", LogLevel.Success);
            }
            else
            {
                AppendLog(">>> [自动检测] Release 签名未就绪，Debug 构建可使用系统默认签名，Release 需手动配置 keystore.properties", LogLevel.Warning);
            }

            // 6. 自动探测图标
            if (string.IsNullOrWhiteSpace(_viewModel.AndroidIconPath))
            {
                string? ico = PathHelper.FindAndroidIcon(projectRoot);
                if (!string.IsNullOrEmpty(ico))
                {
                    _viewModel.AndroidIconPath = ico;
                    AppendLog($">>> [自动检测] 应用图标 → {ico}", LogLevel.Success);
                }
                else
                {
                    AppendLog(">>> [自动检测] 未找到 .ico 图标文件，请手动指定或跳过。", LogLevel.Warning);
                }
            }

            AppendLog(">>> Android 环境配置探测完成。", LogLevel.Info);
        }

        private void AndroidIconPicker_PathChanged(object? sender, string? e)
            => _viewModel.AndroidIconPath = e;

        private void AndroidApkOutputPicker_PathChanged(object? sender, string? e)
        {
            _viewModel.AndroidApkOutputDir = e;
            _viewModel.RefreshAndroidProjectMetadata();
            if (!string.IsNullOrWhiteSpace(e))
                AppendLog($">>> APK 输出目录已设为：{e}", LogLevel.Info);
            else
                AppendLog(">>> APK 输出目录已清空，将使用项目默认 build 目录", LogLevel.Info);
        }

        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            string? name = _viewModel.SelectedPresetName;
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("请先在左侧列表选中一个预设。", "提示");
                return;
            }
            if (MessageBox.Show($"确认删除预设？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            {
                return;
            }
            _viewModel.DeletePreset(name);
        }

        private void BtnSavePresetAs_Click(object sender, RoutedEventArgs e)
        {
            PromptAndSavePreset();
        }


        /// <summary>构建成功后：若当前已有预设名则静默更新；否则询问是否保存为新预设（B 方案）。</summary>
        private void PromptSavePresetAfterBuild()
        {
            if (!string.IsNullOrWhiteSpace(_viewModel.PresetName))
            {
                // 已属于某预设，静默刷新它
                _viewModel.SaveCurrentAsPreset(_viewModel.PresetName!);
                return;
            }
            // 新配置打包成功，询问是否保存为预设以便下次直接选用
            var answer = MessageBox.Show(
                "构建成功。是否将当前配置保存为打包预设，方便下次直接选用？",
                "保存预设",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
            PromptAndSavePreset();
        }

        private void PromptAndSavePreset(string? suggestedName = null)
        {
            string suggestion = suggestedName ?? _viewModel.PresetName ?? _viewModel.AppName ?? string.Empty;

            // 构建配置摘要，让用户在保存前确认内容
            var summary = BuildPresetSummary();

            string? name = InputBoxDialog.Show("请输入预设名称：", "保存打包预设", suggestion, summary);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }
            _viewModel.SaveCurrentAsPreset(name.Trim());
            AppendLog($">>> 预设已保存：{name.Trim()}", LogLevel.Success);
        }

        /// <summary>
        /// 从 ViewModel 当前状态生成配置摘要键值对列表。
        /// </summary>
        private List<KeyValuePair<string, string>> BuildPresetSummary()
        {
            var list = new List<KeyValuePair<string, string>>();

            list.Add(new("目标平台", _viewModel.TargetPlatform));
            list.Add(new("应用名", _viewModel.AppName ?? ""));
            list.Add(new("版本号", _viewModel.Version ?? ""));

            if (string.Equals(_viewModel.TargetPlatform, "Windows", StringComparison.OrdinalIgnoreCase))
            {
                if (_viewModel.IsWindowsCustomBuild)
                {
                    list.Add(new("构建模式", "自定义构建（Tauri/Node）"));
                    list.Add(new("构建命令", _viewModel.WindowsBuildCommand ?? ""));
                    list.Add(new("工作目录", _viewModel.WindowsBuildWorkingDir ?? ""));
                    list.Add(new("产物目录", _viewModel.WindowsBuildArtifactDir ?? ""));
                }
                else
                {
                    list.Add(new("构建模式", ".NET 项目"));
                    list.Add(new("项目文件", _viewModel.ProjectPath ?? ""));
                    list.Add(new("ISCC路径", _viewModel.InnoSetupPath ?? ""));
                    list.Add(new("ISS脚本", _viewModel.IssScriptPath ?? ""));
                    list.Add(new("生成安装包", _viewModel.MakeInstaller ? "是" : "否"));
                }
                list.Add(new("产物输出", _viewModel.RawOutputDir ?? ""));
                list.Add(new("安装包输出", _viewModel.SetupOutputDir ?? ""));
            }
            else
            {
                list.Add(new("项目根目录", _viewModel.AndroidProjectRoot ?? ""));
                list.Add(new("模块名", _viewModel.AndroidModuleName));
                list.Add(new("构建类型", _viewModel.AndroidBuildType));
                list.Add(new("APK输出目录", _viewModel.AndroidApkOutputDir ?? ""));
                list.Add(new("签名状态", _viewModel.AndroidSigningStatus ?? ""));
            }

            list.Add(new("应用图标", _viewModel.WindowsIconPath ?? _viewModel.AndroidIconPath ?? ""));

            return list;
        }
        private void BtnSelectAndroidReleaseMode_Click(object sender, RoutedEventArgs e) => _viewModel.AndroidBuildType = "Release";
        // Inno Setup 的 AppID 需要写成 "{{GUID}" 形式，双左花括号用于转义出字面量 "{"
        private void BtnGenID_Click(object sender, RoutedEventArgs e) => _viewModel.AppID = "{{" + Guid.NewGuid().ToString().ToUpper() + "}";

        // ===== 日志系统：4 级别 + 时间戳 + 筛选 =====
        // 复用静态画刷，避免每条日志分配新画刷导致内存增长；Freeze 让画刷可跨线程使用
        private static readonly System.Windows.Media.SolidColorBrush InfoLogBrush =
            new(System.Windows.Media.Color.FromRgb(0x22, 0xD3, 0xEE));   // 青蓝：普通信息
        private static readonly System.Windows.Media.SolidColorBrush SuccessLogBrush =
            new(System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x41));   // 霓虹绿：成功
        private static readonly System.Windows.Media.SolidColorBrush WarningLogBrush =
            new(System.Windows.Media.Color.FromRgb(0xFA, 0xCC, 0x15));   // 金黄：警告
        private static readonly System.Windows.Media.SolidColorBrush ErrorLogBrush =
            new(System.Windows.Media.Color.FromRgb(0xFF, 0x32, 0x32));   // 霓虹红：错误

        // 全量日志条目缓存：切换筛选时无需重放事件，直接本地重建
        private readonly List<LogEntry> _logEntries = new();
        // 4 个级别筛选开关（默认全开）
        private bool _showInfo = true;
        private bool _showSuccess = true;
        private bool _showWarning = true;
        private bool _showError = true;

        private struct LogEntry
        {
            public string Message;
            public LogLevel Level;
            public DateTime Timestamp;
        }

        static MainWindow()
        {
            InfoLogBrush.Freeze();
            SuccessLogBrush.Freeze();
            WarningLogBrush.Freeze();
            ErrorLogBrush.Freeze();
        }

        // 向后兼容：旧调用点仍用 bool
        private void AppendLog(string msg, bool isError)
            => AppendLog(msg, isError ? LogLevel.Error : LogLevel.Info);

        private void AppendLog(string msg, LogLevel level)
        {
            var entry = new LogEntry
            {
                Message = msg,
                Level = level,
                Timestamp = DateTime.Now
            };
            _logEntries.Add(entry);

            // FIFO 截断缓存
            while (_logEntries.Count > MaxLogLines)
            {
                _logEntries.RemoveAt(0);
            }

            if (IsLevelVisible(level))
            {
                AppendEntryToUi(entry);
            }
        }

        private bool IsLevelVisible(LogLevel level) => level switch
        {
            LogLevel.Info => _showInfo,
            LogLevel.Success => _showSuccess,
            LogLevel.Warning => _showWarning,
            LogLevel.Error => _showError,
            _ => true
        };

        private static string LevelTag(LogLevel level) => level switch
        {
            LogLevel.Info => "INFO",
            LogLevel.Success => " OK ",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERR ",
            _ => "INFO"
        };

        private static System.Windows.Media.SolidColorBrush LevelBrush(LogLevel level) => level switch
        {
            LogLevel.Info => InfoLogBrush,
            LogLevel.Success => SuccessLogBrush,
            LogLevel.Warning => WarningLogBrush,
            LogLevel.Error => ErrorLogBrush,
            _ => InfoLogBrush
        };

        private void AppendEntryToUi(LogEntry entry)
        {
            string line = $"[{entry.Timestamp:HH:mm:ss}] [{LevelTag(entry.Level)}] {entry.Message}";

            var run = new System.Windows.Documents.Run(line)
            {
                Foreground = LevelBrush(entry.Level)
            };
            var paragraph = new System.Windows.Documents.Paragraph(run)
            {
                Margin = new Thickness(0)
            };

            TxtLog.Document.Blocks.Add(paragraph);

            // FIFO 滚动：超过最大行数时删除最旧的段落
            while (TxtLog.Document.Blocks.Count > MaxLogLines)
            {
                TxtLog.Document.Blocks.Remove(TxtLog.Document.Blocks.FirstBlock);
            }

            TxtLog.ScrollToEnd();
        }

        // 切换筛选时重建显示（保留全部缓存，只重渲染可见的）
        private void RebuildLogFromEntries()
        {
            TxtLog.Document.Blocks.Clear();
            foreach (var entry in _logEntries)
            {
                if (IsLevelVisible(entry.Level))
                {
                    AppendEntryToUi(entry);
                }
            }
            TxtLog.ScrollToEnd();
        }

        // UI 4 个 CheckBox 的事件入口
        private void FilterCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox cb)
            {
                return;
            }
            bool isChecked = cb.IsChecked == true;
            switch (cb.Name)
            {
                case "CbInfo": _showInfo = isChecked; break;
                case "CbSuccess": _showSuccess = isChecked; break;
                case "CbWarning": _showWarning = isChecked; break;
                case "CbError": _showError = isChecked; break;
            }
            RebuildLogFromEntries();
        }

        private static void OpenOutputLocation(BuildResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.PrimaryOutputPath) && File.Exists(result.PrimaryOutputPath))
            {
                Process.Start("explorer.exe", $"/select,\"{result.PrimaryOutputPath}\"");
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.OutputDirectory) && Directory.Exists(result.OutputDirectory))
            {
                Process.Start("explorer.exe", result.OutputDirectory);
            }
        }
    }
}
