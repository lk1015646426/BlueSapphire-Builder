using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace BlueSapphire.Builder
{
    public partial class MainWindow : Window
    {
        private const string InnoCompilerPath = @"D:\IDM下载\下载软件\Inno Setup 6\ISCC.exe";
        private const string ConfigFileName = "builder_config_v3.json";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        // === 1. 初始化与配置加载 ===
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (File.Exists(ConfigFileName))
            {
                try
                {
                    var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFileName));
                    if (config != null)
                    {
                        TxtAppName.Text = config.AppName ?? "BlueSapphire";
                        TxtVersion.Text = config.Version ?? "1.0.0";
                        TxtPublisher.Text = config.Publisher ?? "MyStudio";
                        TxtAppID.Text = config.AppID ?? "";
                        TxtProjectFile.Text = config.ProjectPath ?? "";
                        TxtRawDir.Text = config.RawOutputDir ?? "";
                        TxtSetupDir.Text = config.SetupOutputDir ?? "";
                        return;
                    }
                }
                catch { }
            }
            TxtAppName.Text = "BlueSapphire";
            AutoFindProjectFile();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var config = new AppConfig
            {
                AppName = TxtAppName.Text,
                Version = TxtVersion.Text,
                Publisher = TxtPublisher.Text,
                AppID = TxtAppID.Text,
                ProjectPath = TxtProjectFile.Text,
                RawOutputDir = TxtRawDir.Text,
                SetupOutputDir = TxtSetupDir.Text
            };
            File.WriteAllText(ConfigFileName, JsonSerializer.Serialize(config));
        }

        private void AutoFindProjectFile()
        {
            string? current = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 5; i++)
            {
                if (!string.IsNullOrEmpty(current) && Directory.Exists(current))
                {
                    var files = Directory.GetFiles(current, "BlueSapphire.csproj", SearchOption.AllDirectories);
                    if (files.Length > 0)
                    {
                        TxtProjectFile.Text = files[0];
                        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                        TxtRawDir.Text = Path.Combine(desktop, "BlueSapphire_RawFiles");
                        TxtSetupDir.Text = Path.Combine(desktop, "BlueSapphire_Installer");
                        return;
                    }
                    current = Directory.GetParent(current)?.FullName;
                }
            }
        }

        private void BtnGenID_Click(object sender, RoutedEventArgs e)
        {
            TxtAppID.Text = "{{" + Guid.NewGuid().ToString().ToUpper() + "}";
        }

        // === 2. 文件夹选择器 ===
        private void BtnBrowseProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "C# 项目文件|*.csproj" };
            if (dialog.ShowDialog() == true) TxtProjectFile.Text = dialog.FileName;
        }

        private void BtnBrowseRaw_Click(object sender, RoutedEventArgs e) => TxtRawDir.Text = PickFolder(TxtRawDir.Text) ?? TxtRawDir.Text;
        private void BtnBrowseSetup_Click(object sender, RoutedEventArgs e) => TxtSetupDir.Text = PickFolder(TxtSetupDir.Text) ?? TxtSetupDir.Text;

        private string? PickFolder(string path)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            dialog.UseDescriptionForTitle = true;
            if (Directory.Exists(path)) dialog.SelectedPath = path;
            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }

        // === 3. 核心构建逻辑 ===
        private async void BtnBuild_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(TxtProjectFile.Text)) { Alert("找不到 .csproj 文件！"); return; }
            if (string.IsNullOrWhiteSpace(TxtAppID.Text)) { Alert("请填写 AppID！"); return; }
            if (string.IsNullOrWhiteSpace(TxtRawDir.Text)) { Alert("请选择原始程序输出位置！"); return; }
            if (string.IsNullOrWhiteSpace(TxtSetupDir.Text)) { Alert("请选择安装包输出位置！"); return; }

            BtnBuild.IsEnabled = false;
            TxtLog.Text = "";
            BuildProgress.Value = 0;
            TxtProgressText.Text = "初始化中...";

            try
            {
                string projectPath = TxtProjectFile.Text;
                string version = TxtVersion.Text;
                string rawDir = TxtRawDir.Text;
                string setupDir = TxtSetupDir.Text;

                // --- 阶段 1: 编译 ---
                Log(">>> [1/2] 开始编译 .NET 程序...", true);
                BuildProgress.IsIndeterminate = true;
                TxtProgressText.Text = "正在执行 dotnet publish...";

                if (Directory.Exists(rawDir)) Directory.Delete(rawDir, true);

                await RunCommandRealtime("dotnet",
                    $"publish \"{projectPath}\" -c Release -r win-x64 --self-contained true -o \"{rawDir}\" /p:Version={version}");

                Log(">>> 编译完成！文件已输出到 Raw 目录。", true);

                // --- 阶段 2: 打包 ---
                if (ChkMakeInstaller.IsChecked == true)
                {
                    Log(">>> [2/2] 开始制作安装包...", true);
                    TxtProgressText.Text = "正在调用 Inno Setup 打包...";

                    if (!File.Exists(InnoCompilerPath)) throw new FileNotFoundException("找不到 Inno Setup 编译器！");

                    string? projDir = Path.GetDirectoryName(projectPath);
                    if (projDir == null) throw new Exception("项目路径异常");
                    string issPath = Path.Combine(projDir, "installer.iss");

                    string args = $"/dSourcePath=\"{rawDir}\" " +
                                  $"/dMyAppName=\"{TxtAppName.Text}\" " +
                                  $"/dMyAppVersion=\"{version}\" " +
                                  $"/dMyAppPublisher=\"{TxtPublisher.Text}\" " +
                                  $"/dMyAppId=\"{TxtAppID.Text}\" " +
                                  $"/O\"{setupDir}\" " +
                                  $"/F\"{TxtAppName.Text}_Setup_v{version}\" " +
                                  $"\"{issPath}\"";

                    await RunCommandRealtime(InnoCompilerPath, args);
                    Log(">>> 打包完成！", true);
                }

                BuildProgress.IsIndeterminate = false;
                BuildProgress.Value = 100;
                TxtProgressText.Text = "构建成功";
                System.Windows.MessageBox.Show($"构建成功！\n安装包位置: {setupDir}", "恭喜");
                Process.Start("explorer.exe", setupDir);
            }
            catch (Exception ex)
            {
                BuildProgress.IsIndeterminate = false;
                BuildProgress.Value = 0;
                TxtProgressText.Text = "构建失败";
                Log($"\n[严重错误] {ex.Message}");
                Alert("构建过程中出错，请查看下方日志详情。");
            }
            finally
            {
                BtnBuild.IsEnabled = true;
            }
        }

        // === 4. 修复乱码的核心方法 ===
        private Task RunCommandRealtime(string fileName, string arguments)
        {
            var tcs = new TaskCompletionSource<bool>();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,

                    // 🔥【核心修复】强制使用 GB2312 (中文编码) 读取输出 🔥
                    // 注意：这需要 App.xaml.cs 里的注册代码配合才能生效
                    StandardOutputEncoding = System.Text.Encoding.GetEncoding("GB2312"),
                    StandardErrorEncoding = System.Text.Encoding.GetEncoding("GB2312")
                },
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (s, e) => { if (e.Data != null) Dispatcher.Invoke(() => Log(e.Data)); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) Dispatcher.Invoke(() => Log("ERROR: " + e.Data)); };

            process.Exited += (s, e) =>
            {
                if (process.ExitCode == 0) tcs.SetResult(true);
                else tcs.SetException(new Exception($"进程退出码非零 ({process.ExitCode})"));
                process.Dispose();
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return tcs.Task;
        }

        private void Log(string msg, bool highlight = false)
        {
            if (highlight) TxtLog.AppendText($"\n================ {msg} ================\n");
            else TxtLog.AppendText(msg + "\n");
            TxtLog.ScrollToEnd();
        }

        private void Alert(string msg) => System.Windows.MessageBox.Show(msg, "提示");
    }

    public class AppConfig
    {
        public string? AppName { get; set; }
        public string? Version { get; set; }
        public string? Publisher { get; set; }
        public string? AppID { get; set; }
        public string? ProjectPath { get; set; }
        public string? RawOutputDir { get; set; }
        public string? SetupOutputDir { get; set; }
    }
}