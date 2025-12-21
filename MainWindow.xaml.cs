using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using BlueSapphire.Builder.Services;
using BlueSapphire.Builder.ViewModels;

namespace BlueSapphire.Builder
{
    public partial class MainWindow : Window
    {
        private const string ConfigFileName = "builder_config_v5.json"; // 配置文件版本
        private readonly BuilderService _builderService = new BuilderService();

        // 核心 ViewModel 实例
        private readonly MainViewModel _viewModel = new MainViewModel();

        public MainWindow()
        {
            InitializeComponent();

            // 🔥 设置数据上下文，实现绑定
            DataContext = _viewModel;

            // 绑定 Service 事件
            _builderService.LogReceived += (s, e) => Dispatcher.Invoke(() => AppendLog(e.Message, e.IsError));

            // 进度条直接更新 ViewModel
            _builderService.ProgressChanged += (s, val) => Dispatcher.Invoke(() => {
                _viewModel.ProgressValue = val;
                _viewModel.ProgressText = val >= 100 ? "构建完成" : $"处理中... {val}%";
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

                    // 将配置加载到 ViewModel，界面会自动刷新
                    _viewModel.LoadFromConfig(config!);

                    // 特殊处理：如果 InnoPath 为空，尝试自动查找
                    if (string.IsNullOrEmpty(_viewModel.InnoSetupPath))
                    {
                        _viewModel.InnoSetupPath = PathHelper.FindInnoSetup();
                    }
                    return;
                }
                catch { /* 忽略损坏的配置 */ }
            }

            // 首次运行默认值
            _viewModel.AppName = "BlueSapphire";
            _viewModel.InnoSetupPath = PathHelper.FindInnoSetup() ?? "请手动选择 ISCC.exe";
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 从 ViewModel 导出配置并保存
            var config = _viewModel.ToConfig();
            File.WriteAllText(ConfigFileName, JsonSerializer.Serialize(config));
        }

        // === 事件处理 ===

        private async void BtnBuild_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Text = "";
            _viewModel.ProgressValue = 0;
            _viewModel.ProgressText = "初始化...";
            _viewModel.IsBuilding = true; // 自动禁用按钮

            var currentConfig = _viewModel.ToConfig();

            try
            {
                await _builderService.BuildAsync(currentConfig);

                MessageBox.Show($"构建成功！\n输出目录: {currentConfig.SetupOutputDir}", "恭喜");
                if (currentConfig.MakeInstaller && Directory.Exists(currentConfig.SetupOutputDir))
                {
                    Process.Start("explorer.exe", currentConfig.SetupOutputDir!);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[严重错误] {ex.Message}", true);
                MessageBox.Show($"构建失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _viewModel.IsBuilding = false; // 恢复按钮
            }
        }

        // === 窗口控制按钮 (沉浸式标题栏) ===

        private void BtnMin_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // === 控制台工具栏按钮 (新增) ===

        private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtLog.Text))
            {
                Clipboard.SetText(TxtLog.Text);
                // 这里可选：给个小提示，或者改变一下按钮样式作为反馈
            }
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Text = "";
        }

        // === 辅助方法 (更新 ViewModel) ===

        private string? PickFolder(string? currentPath)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "请选择文件夹",
                Multiselect = false
            };

            if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                dialog.InitialDirectory = currentPath;

            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        // 文件选择器 (ISCC.exe)
        private void BtnBrowseInno_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Inno Setup Compiler|ISCC.exe" };
            if (dialog.ShowDialog() == true) _viewModel.InnoSetupPath = dialog.FileName;
        }

        // 文件选择器 (.iss)
        private void BtnBrowseIss_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Inno Setup Script|*.iss" };
            if (!string.IsNullOrEmpty(_viewModel.ProjectPath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(_viewModel.ProjectPath);
            }
            if (dialog.ShowDialog() == true) _viewModel.IssScriptPath = dialog.FileName;
        }

        private void BtnBrowseProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "C# 项目文件|*.csproj" };
            if (dialog.ShowDialog() == true) _viewModel.ProjectPath = dialog.FileName;
        }

        private void BtnBrowseRaw_Click(object sender, RoutedEventArgs e)
            => _viewModel.RawOutputDir = PickFolder(_viewModel.RawOutputDir) ?? _viewModel.RawOutputDir;

        private void BtnBrowseSetup_Click(object sender, RoutedEventArgs e)
            => _viewModel.SetupOutputDir = PickFolder(_viewModel.SetupOutputDir) ?? _viewModel.SetupOutputDir;

        private void BtnGenID_Click(object sender, RoutedEventArgs e)
            => _viewModel.AppID = "{{" + Guid.NewGuid().ToString().ToUpper() + "}";

        private void AppendLog(string msg, bool isError)
        {
            TxtLog.AppendText(msg + "\n");
            TxtLog.ScrollToEnd();
        }
    }
}