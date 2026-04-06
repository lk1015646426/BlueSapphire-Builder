using CommunityToolkit.Mvvm.ComponentModel;

namespace BlueSapphire.Builder.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty] private string? _appName;
        [ObservableProperty] private string? _version = "1.0.0";
        [ObservableProperty] private string? _publisher;
        [ObservableProperty] private string? _appID;
        [ObservableProperty] private string? _projectPath;
        [ObservableProperty] private string? _publishOutputDir;
        [ObservableProperty] private string? _setupOutputDir;
        [ObservableProperty] private string? _innoSetupPath;
        [ObservableProperty] private string? _issScriptPath;
        [ObservableProperty] private bool _makeInstaller = true;

        [ObservableProperty] private double _progressValue;
        [ObservableProperty] private string _progressText = "准备就绪";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotBuilding))]
        private bool _isBuilding;

        public bool IsNotBuilding => !IsBuilding;

        public void LoadFromConfig(AppConfig config)
        {
            if (config == null)
            {
                return;
            }

            config.Normalize();

            AppName = config.AppName;
            Version = config.Version;
            Publisher = config.Publisher;
            AppID = config.AppID;
            ProjectPath = config.ProjectPath;
            PublishOutputDir = config.PublishOutputDir;
            SetupOutputDir = config.SetupOutputDir;
            InnoSetupPath = config.InnoSetupPath;
            IssScriptPath = config.IssScriptPath;
            MakeInstaller = config.MakeInstaller;
        }

        public AppConfig ToConfig()
        {
            return new AppConfig
            {
                AppName = AppName,
                Version = Version,
                Publisher = Publisher,
                AppID = AppID,
                ProjectPath = ProjectPath,
                PublishOutputDir = PublishOutputDir,
                SetupOutputDir = SetupOutputDir,
                InnoSetupPath = InnoSetupPath,
                IssScriptPath = IssScriptPath,
                MakeInstaller = MakeInstaller
            };
        }
    }
}
