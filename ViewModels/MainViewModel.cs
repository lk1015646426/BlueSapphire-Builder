using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BlueSapphire.Builder.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // 对应 AppConfig 的字段
        private string? _appName;
        private string? _version = "1.0.0";
        private string? _publisher;
        private string? _appId;
        private string? _projectPath;
        private string? _rawOutputDir;
        private string? _setupOutputDir;
        private string? _innoSetupPath;
        private string? _issScriptPath; // [新增]
        private bool _makeInstaller = true;

        // UI 状态
        private double _progressValue;
        private string _progressText = "准备就绪";
        private bool _isBuilding = false;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // === 数据属性 ===

        public string? AppName
        {
            get => _appName;
            set { _appName = value; OnPropertyChanged(); }
        }

        public string? Version
        {
            get => _version;
            set { _version = value; OnPropertyChanged(); }
        }

        public string? Publisher
        {
            get => _publisher;
            set { _publisher = value; OnPropertyChanged(); }
        }

        public string? AppID
        {
            get => _appId;
            set { _appId = value; OnPropertyChanged(); }
        }

        public string? ProjectPath
        {
            get => _projectPath;
            set { _projectPath = value; OnPropertyChanged(); }
        }

        public string? RawOutputDir
        {
            get => _rawOutputDir;
            set { _rawOutputDir = value; OnPropertyChanged(); }
        }

        public string? SetupOutputDir
        {
            get => _setupOutputDir;
            set { _setupOutputDir = value; OnPropertyChanged(); }
        }

        public string? InnoSetupPath
        {
            get => _innoSetupPath;
            set { _innoSetupPath = value; OnPropertyChanged(); }
        }

        public string? IssScriptPath
        {
            get => _issScriptPath;
            set { _issScriptPath = value; OnPropertyChanged(); }
        }

        public bool MakeInstaller
        {
            get => _makeInstaller;
            set { _makeInstaller = value; OnPropertyChanged(); }
        }

        // === UI 状态属性 ===

        public double ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnPropertyChanged(); }
        }

        public string ProgressText
        {
            get => _progressText;
            set { _progressText = value; OnPropertyChanged(); }
        }

        public bool IsBuilding
        {
            get => _isBuilding;
            set
            {
                _isBuilding = value; OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotBuilding));
            } // 用于控制按钮启用
        }

        public bool IsNotBuilding => !IsBuilding;

        // === 辅助方法：从 Config 加载 ===
        public void LoadFromConfig(AppConfig config)
        {
            if (config == null) return;
            AppName = config.AppName;
            Version = config.Version;
            Publisher = config.Publisher;
            AppID = config.AppID;
            ProjectPath = config.ProjectPath;
            RawOutputDir = config.RawOutputDir;
            SetupOutputDir = config.SetupOutputDir;
            InnoSetupPath = config.InnoSetupPath;
            IssScriptPath = config.IssScriptPath;
            MakeInstaller = config.MakeInstaller;
        }

        // === 辅助方法：导出为 Config ===
        public AppConfig ToConfig()
        {
            return new AppConfig
            {
                AppName = AppName,
                Version = Version,
                Publisher = Publisher,
                AppID = AppID,
                ProjectPath = ProjectPath,
                RawOutputDir = RawOutputDir,
                SetupOutputDir = SetupOutputDir,
                InnoSetupPath = InnoSetupPath,
                IssScriptPath = IssScriptPath,
                MakeInstaller = MakeInstaller
            };
        }
    }
}