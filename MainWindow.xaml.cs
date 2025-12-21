using Microsoft.Win32; // 使用原生 WPF 对话框
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using BlueSapphire.Builder.Services; // <--- 必须添加这行，才能识别 BuilderService
namespace BlueSapphire.Builder
{
    public partial class MainWindow : Window
    {
        private const string ConfigFileName = "builder_config_v4.json"; // 升级配置文件版本
        private readonly BuilderService _builderService = new BuilderService();

        public MainWindow()
        {
            InitializeComponent();

            // 绑定 Service 事件
            _builderService.LogReceived += (s, e) => Dispatcher.Invoke(() => AppendLog(e.Message, e.IsError));
            _builderService.ProgressChanged += (s, val) => Dispatcher.Invoke(() => {
                BuildProgress.Value = val;
                TxtProgressText.Text = val >= 100 ? "构建完成" : $"处理中... {val}%";
            });

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 加载配置
            if (File.Exists(ConfigFileName))
            {
                try
                {
                    var json = File.ReadAllText(ConfigFileName);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        // 还原 UI ... (省略部分简单赋值)
                        TxtAppName.Text = config.AppName;
                        TxtVersion.Text = config.Version;
                        TxtPublisher.Text = config.Publisher;
                        TxtAppID.Text = config.AppID;
                        TxtProjectFile.Text = config.ProjectPath;
                        TxtRawDir.Text = config.RawOutputDir;
                        TxtSetupDir.Text = config.SetupOutputDir;
                        ChkMakeInstaller.IsChecked = config.MakeInstaller;

                        // [新增] 还原或自动查找 ISCC
                        TxtInnoPath.Text = !string.IsNullOrEmpty(config.InnoSetupPath) && File.Exists(config.InnoSetupPath)
                            ? config.InnoSetupPath
                            : PathHelper.FindInnoSetup(); // 自动查找
                        return;
                    }
                }
                catch { /* 忽略损坏的配置 */ }
            }

            // 首次运行默认值
            TxtAppName.Text = "BlueSapphire";
            TxtInnoPath.Text = PathHelper.FindInnoSetup() ?? "请手动选择 ISCC.exe";
            // AutoFindProjectFile(); // (保留你原本的自动查找逻辑)
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 保存配置
            var config = new AppConfig
            {
                AppName = TxtAppName.Text,
                Version = TxtVersion.Text,
                Publisher = TxtPublisher.Text,
                AppID = TxtAppID.Text,
                ProjectPath = TxtProjectFile.Text,
                RawOutputDir = TxtRawDir.Text,
                SetupOutputDir = TxtSetupDir.Text,
                MakeInstaller = ChkMakeInstaller.IsChecked == true,
                InnoSetupPath = TxtInnoPath.Text // 保存路径
            };
            File.WriteAllText(ConfigFileName, JsonSerializer.Serialize(config));
        }

        // === 事件处理 ===

        private async void BtnBuild_Click(object sender, RoutedEventArgs e)
        {
            BtnBuild.IsEnabled = false;
            TxtLog.Text = "";
            BuildProgress.Value = 0;
            TxtProgressText.Text = "初始化...";

            // 收集当前 UI 数据到 Config 对象
            var currentConfig = new AppConfig
            {
                AppName = TxtAppName.Text,
                Version = TxtVersion.Text,
                Publisher = TxtPublisher.Text,
                AppID = TxtAppID.Text,
                ProjectPath = TxtProjectFile.Text,
                RawOutputDir = TxtRawDir.Text,
                SetupOutputDir = TxtSetupDir.Text,
                MakeInstaller = ChkMakeInstaller.IsChecked == true,
                InnoSetupPath = TxtInnoPath.Text
            };

            try
            {
                // 调用 Service 执行，UI 不再关心具体是用 Process 还是什么
                await _builderService.BuildAsync(currentConfig);

                MessageBox.Show($"构建成功！\n输出目录: {currentConfig.SetupOutputDir}", "恭喜");
                Process.Start("explorer.exe", currentConfig.SetupOutputDir!);
            }
            catch (Exception ex)
            {
                AppendLog($"[严重错误] {ex.Message}", true);
                MessageBox.Show("构建失败，请检查日志。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnBuild.IsEnabled = true;
            }
        }

        // === 辅助方法 ===

        // 使用 .NET 8 原生 WPF 文件夹选择器 (替代 WinForms)
        private string? PickFolder(string currentPath)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "请选择文件夹",
                Multiselect = false
            };

            if (Directory.Exists(currentPath)) dialog.InitialDirectory = currentPath;

            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        // 文件选择器 (ISCC.exe)
        private void BtnBrowseInno_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Inno Setup Compiler|ISCC.exe" };
            if (dialog.ShowDialog() == true) TxtInnoPath.Text = dialog.FileName;
        }

        // 文件夹选择器调用示例
        private void BtnBrowseRaw_Click(object sender, RoutedEventArgs e) => TxtRawDir.Text = PickFolder(TxtRawDir.Text) ?? TxtRawDir.Text;
        private void BtnBrowseSetup_Click(object sender, RoutedEventArgs e) => TxtSetupDir.Text = PickFolder(TxtSetupDir.Text) ?? TxtSetupDir.Text;
        private void BtnBrowseProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "C# 项目文件|*.csproj" };
            if (dialog.ShowDialog() == true) TxtProjectFile.Text = dialog.FileName;
        }

        private void AppendLog(string msg, bool isError)
        {
            // 这里可以做颜色区分，比如 Error 变红，暂时简单追加
            TxtLog.AppendText(msg + "\n");
            TxtLog.ScrollToEnd();
        }

        // (保留你的 Guid 生成代码)
        private void BtnGenID_Click(object sender, RoutedEventArgs e) => TxtAppID.Text = "{{" + Guid.NewGuid().ToString().ToUpper() + "}";
    }
}