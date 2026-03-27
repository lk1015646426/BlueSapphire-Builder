using CommunityToolkit.Mvvm.ComponentModel;
// 如果 AppConfig 在其他文件夹（比如 Models），这里可能还需要加上类似 using BlueSapphire.Builder.Models; 的引用
// 根据你的实际情况保留或添加

namespace BlueSapphire.Builder.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // === 数据属性 ===
        [ObservableProperty] private string? _appName;
        [ObservableProperty] private string? _version = "1.0.0";
        [ObservableProperty] private string? _publisher;
        [ObservableProperty] private string? _appID; // ✅ 改为大写 D，匹配 XAML 和 Config
        [ObservableProperty] private string? _projectPath;
        [ObservableProperty] private string? _rawOutputDir;
        [ObservableProperty] private string? _setupOutputDir;
        [ObservableProperty] private string? _innoSetupPath;
        [ObservableProperty] private string? _issScriptPath;
        [ObservableProperty] private bool _makeInstaller = true;

        // === UI 状态属性 ===
        [ObservableProperty] private double _progressValue;
        [ObservableProperty] private string _progressText = "准备就绪";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotBuilding))]
        private bool _isBuilding = false;

        public bool IsNotBuilding => !IsBuilding;

        // === 辅助方法：从 Config 加载 ===
        public void LoadFromConfig(AppConfig config)
        {
            if (config == null) return;
            this.AppName = config.AppName;
            this.Version = config.Version;
            this.Publisher = config.Publisher;
            this.AppID = config.AppID; // ✅ 使用大写 D
            this.ProjectPath = config.ProjectPath;
            this.RawOutputDir = config.RawOutputDir;
            this.SetupOutputDir = config.SetupOutputDir;
            this.InnoSetupPath = config.InnoSetupPath;
            this.IssScriptPath = config.IssScriptPath;
            this.MakeInstaller = config.MakeInstaller;
        }

        // === 辅助方法：导出为 Config ===
        public AppConfig ToConfig()
        {
            return new AppConfig
            {
                AppName = this.AppName,
                Version = this.Version,
                Publisher = this.Publisher,
                AppID = this.AppID, // ✅ 使用大写 D
                ProjectPath = this.ProjectPath,
                RawOutputDir = this.RawOutputDir,
                SetupOutputDir = this.SetupOutputDir,
                InnoSetupPath = this.InnoSetupPath,
                IssScriptPath = this.IssScriptPath,
                MakeInstaller = this.MakeInstaller
            };
        }
    }
}