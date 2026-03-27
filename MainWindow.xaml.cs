using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading; // 🌟 引入定时器
using BlueSapphire.Builder.Services;
using BlueSapphire.Builder.ViewModels;

namespace BlueSapphire.Builder
{
    public partial class MainWindow : Window
    {
        private const string ConfigFileName = "builder_config_v5.json";
        private readonly BuilderService _builderService = new BuilderService();
        private readonly MainViewModel _viewModel = new MainViewModel();

        // 🌟 新增：用于丝滑滚动动画的变量
        private double _targetProgress = 0;
        private double _currentDisplayProgress = 0;
        private DispatcherTimer _smoothTimer = new DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;

            _builderService.LogReceived += (s, e) => Dispatcher.BeginInvoke(() => AppendLog(e.Message, e.IsError));

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
                _viewModel.ProgressText = val >= 100 ? ">>> 序列构建完成" : $"流式处理中...";
            });
            // ==========================================

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (File.Exists(ConfigFileName))
            {
                try
                {
                    var json = File.ReadAllText(ConfigFileName);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    _viewModel.LoadFromConfig(config!);

                    if (string.IsNullOrEmpty(_viewModel.InnoSetupPath))
                    {
                        _viewModel.InnoSetupPath = PathHelper.FindInnoSetup();
                    }
                    return;
                }
                catch { }
            }
            _viewModel.AppName = "BlueSapphire";
            _viewModel.InnoSetupPath = PathHelper.FindInnoSetup() ?? "请手动选择 ISCC.exe";
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var config = _viewModel.ToConfig();
            File.WriteAllText(ConfigFileName, JsonSerializer.Serialize(config));
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
                await _builderService.BuildAsync(currentConfig);

                // 构建成功后强制推满进度条
                _targetProgress = 100;

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

        private void BtnClearLog_Click(object sender, RoutedEventArgs e) => TxtLog.Document.Blocks.Clear();

        private string? PickFolder(string? currentPath)
        {
            var dialog = new OpenFolderDialog { Title = "请选择文件夹", Multiselect = false };
            if (!string.IsNullOrEmpty(currentPath) && Directory.Exists(currentPath))
                dialog.InitialDirectory = currentPath;
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }

        private void BtnBrowseInno_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Inno Setup Compiler|ISCC.exe" };
            if (dialog.ShowDialog() == true) _viewModel.InnoSetupPath = dialog.FileName;
        }

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
            if (dialog.ShowDialog() == true)
            {
                _viewModel.ProjectPath = dialog.FileName;
                string projDir = Path.GetDirectoryName(dialog.FileName)!;
                _viewModel.RawOutputDir = Path.Combine(projDir, "bin", "Publish");
                _viewModel.SetupOutputDir = Path.Combine(projDir, "bin", "Installer");

                if (string.IsNullOrWhiteSpace(_viewModel.AppName) || _viewModel.AppName == "BlueSapphire")
                {
                    _viewModel.AppName = Path.GetFileNameWithoutExtension(dialog.FileName);
                }
            }
        }

        private void BtnBrowseRaw_Click(object sender, RoutedEventArgs e) => _viewModel.RawOutputDir = PickFolder(_viewModel.RawOutputDir) ?? _viewModel.RawOutputDir;
        private void BtnBrowseSetup_Click(object sender, RoutedEventArgs e) => _viewModel.SetupOutputDir = PickFolder(_viewModel.SetupOutputDir) ?? _viewModel.SetupOutputDir;
        private void BtnGenID_Click(object sender, RoutedEventArgs e) => _viewModel.AppID = "{{" + Guid.NewGuid().ToString().ToUpper() + "}";

        private void AppendLog(string msg, bool isError)
        {
            var run = new System.Windows.Documents.Run(msg);
            run.Foreground = isError
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 50, 50))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 65));

            var paragraph = new System.Windows.Documents.Paragraph(run);
            paragraph.Margin = new Thickness(0);

            TxtLog.Document.Blocks.Add(paragraph);
            TxtLog.ScrollToEnd();
        }
    }
}