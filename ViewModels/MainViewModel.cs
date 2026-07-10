using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using BlueSapphire.Builder.Helpers;
using BlueSapphire.Builder.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace BlueSapphire.Builder.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private bool _isRefreshingAndroidMetadata;

        // ===== 打包预设管理 =====
        public PresetService Presets { get; } = new();

        // 预设名列表，供左侧栏 ListBox 绑定
        public ObservableCollection<string> PresetNames { get; } = new();

        [ObservableProperty]
        private string? _selectedPresetName;

        partial void OnSelectedPresetNameChanged(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            // 选中预设 → 加载其配置到当前界面
            BuildPreset? preset = Presets.Find(value);
            if (preset != null)
            {
                LoadFromConfig(preset.Config);
                PresetName = value;
                RefreshMetadata();
            }
        }

        /// <summary>构建成功后调用：若当前配置已有 PresetName 则更新，否则返回 null 由 UI 弹命名框。</summary>
        public string? GetCurrentPresetName() => PresetName;

        /// <summary>以指定名称保存当前配置为预设，并刷新列表。</summary>
        public void SaveCurrentAsPreset(string name)
        {
            Presets.Save(name, ToConfig());
            PresetName = name;
            RefreshPresetNames();
            if (!PresetNames.Contains(name))
            {
                PresetNames.Add(name);
            }
        }

        /// <summary>删除预设并刷新列表。</summary>
        public bool DeletePreset(string name)
        {
            bool ok = Presets.Delete(name);
            if (ok)
            {
                RefreshPresetNames();
                if (string.Equals(PresetName, name, StringComparison.OrdinalIgnoreCase))
                {
                    PresetName = null;
                }
            }
            return ok;
        }

        public void RefreshPresetNames()
        {
            PresetNames.Clear();
            foreach (var p in Presets.Presets.OrderBy(x => x.Name))
            {
                PresetNames.Add(p.Name);
            }
        }

        public MainViewModel()
        {
            PropertyChanged += (s, e) =>
            {
                if (_isRefreshingAndroidMetadata)
                {
                    return;
                }

                if (e.PropertyName == nameof(AndroidProjectRoot) ||
                    e.PropertyName == nameof(AndroidModuleName) ||
                    e.PropertyName == nameof(AndroidBuildType) ||
                    e.PropertyName == nameof(AndroidApkOutputDir) ||
                    e.PropertyName == nameof(TargetPlatform))
                {
                    RefreshAndroidProjectMetadata();
                }

                if (e.PropertyName == nameof(WindowsIconPath))
                {
                    RefreshWindowsIconMetadata();
                }

                if (e.PropertyName == nameof(AndroidIconPath))
                {
                    RefreshAndroidIconMetadata();
                }
            };
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsWindowsTarget))]
        [NotifyPropertyChangedFor(nameof(IsAndroidTarget))]
        [NotifyPropertyChangedFor(nameof(BuildButtonText))]
        [NotifyPropertyChangedFor(nameof(AppName))]
        [NotifyPropertyChangedFor(nameof(Version))]
        [NotifyPropertyChangedFor(nameof(Publisher))]
        [NotifyPropertyChangedFor(nameof(IsWindowsCustomBuild))]
        [NotifyPropertyChangedFor(nameof(IsWindowsDotNetBuild))]
        private string _targetPlatform = "Windows";

        private string? _windowsAppName = "BlueSapphire";
        private string? _windowsVersion = "1.0.0";
        private string? _windowsPublisher;
        private string? _androidAppName;
        private string? _androidVersion = "1.0.0";
        private string? _androidPublisher;

        [ObservableProperty] private string? _appID;
[ObservableProperty] private string? _presetName;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsWindowsCustomBuild))]
        [NotifyPropertyChangedFor(nameof(IsWindowsDotNetBuild))]
        private string? _projectPath;
        [ObservableProperty] private string? _rawOutputDir;
        [ObservableProperty] private string? _setupOutputDir;
        [ObservableProperty] private string? _innoSetupPath;
        [ObservableProperty] private string? _issScriptPath;
        [ObservableProperty] private string? _windowsIconPath;
        [ObservableProperty] private string _windowsIconStatus = "未设置图标，将使用项目默认图标";
        [ObservableProperty] private bool _windowsIconReady;
        [ObservableProperty] private bool _makeInstaller = true;
        [ObservableProperty] private string? _androidProjectRoot;
        [ObservableProperty] private string _androidModuleName = "app";
        [ObservableProperty] private string? _androidIconPath;
        [ObservableProperty] private string _androidIconStatus = "未设置图标，将使用项目默认启动图标";
        [ObservableProperty] private bool _androidIconReady;
        [ObservableProperty] private string? _androidApkOutputDir;
[ObservableProperty] private string? _windowsBuildCommand;
[ObservableProperty] private string? _windowsBuildWorkingDir;
[ObservableProperty] private string? _windowsBuildArtifactDir;


        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BuildButtonText))]
        private string _androidBuildType = "Debug";

        [ObservableProperty] private string _androidDetectedModulesSummary = "等待识别 Android 应用模块";
        [ObservableProperty] private string _androidSigningStatus = "尚未检测签名状态";
        [ObservableProperty] private string _androidOutputPathPreview = "尚未识别 APK 输出路径";
        [ObservableProperty] private bool _androidReleaseSigningReady;
        [ObservableProperty] private string? _lastAndroidBuildType;

        [ObservableProperty] private double _progressValue;
        [ObservableProperty] private string _progressText = "准备就绪";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotBuilding))]
        private bool _isBuilding;

        public bool IsNotBuilding => !IsBuilding;
        public bool IsWindowsTarget => string.Equals(TargetPlatform, "Windows", StringComparison.OrdinalIgnoreCase);
        public bool IsAndroidTarget => string.Equals(TargetPlatform, "Android", StringComparison.OrdinalIgnoreCase);

        // 非 .csproj 工程（Tauri/Node）走自定义命令构建旁路，UI 据此切换显示。
        public bool IsWindowsCustomBuild =>
            IsWindowsTarget &&
            !string.IsNullOrEmpty(ProjectPath) &&
            !string.Equals(System.IO.Path.GetExtension(ProjectPath), ".csproj", System.StringComparison.OrdinalIgnoreCase);

        // .NET 工程（.csproj）才需要 ISCC / ISS / RawOutput 这些字段。
        public bool IsWindowsDotNetBuild => IsWindowsTarget && !IsWindowsCustomBuild;

        // UI 反向可见性绑定用：非自定义构建（即 .csproj 模式）
        public bool IsNotWindowsCustomBuild => IsWindowsTarget && !IsWindowsCustomBuild;

        // UI 颜色 DataTrigger 绑定用别名
        public bool AndroidSigningReady => AndroidReleaseSigningReady;

        // UI 文案别名
        public string? AndroidApkOutputPreview => AndroidOutputPathPreview;

        public string BuildButtonText => IsAndroidTarget
            ? (string.Equals(AndroidBuildType, "Release", StringComparison.OrdinalIgnoreCase)
                ? "📦  生成 Release APK"
                : "🧪  生成 Debug APK")
            : "🚀  启动 Windows 构建序列";

        public string? AppName
        {
            get => IsAndroidTarget ? AndroidAppName : WindowsAppName;
            set
            {
                if (IsAndroidTarget)
                {
                    AndroidAppName = value;
                }
                else
                {
                    WindowsAppName = value;
                }
            }
        }

        public string? Version
        {
            get => IsAndroidTarget ? AndroidVersion : WindowsVersion;
            set
            {
                if (IsAndroidTarget)
                {
                    AndroidVersion = value;
                }
                else
                {
                    WindowsVersion = value;
                }
            }
        }

        public string? Publisher
        {
            get => IsAndroidTarget ? AndroidPublisher : WindowsPublisher;
            set
            {
                if (IsAndroidTarget)
                {
                    AndroidPublisher = value;
                }
                else
                {
                    WindowsPublisher = value;
                }
            }
        }

        public string? WindowsAppName
        {
            get => _windowsAppName;
            set => SetScopedMetadata(ref _windowsAppName, value, nameof(WindowsAppName), nameof(AppName), IsWindowsTarget);
        }

        public string? WindowsVersion
        {
            get => _windowsVersion;
            set => SetScopedMetadata(ref _windowsVersion, value, nameof(WindowsVersion), nameof(Version), IsWindowsTarget);
        }

        public string? WindowsPublisher
        {
            get => _windowsPublisher;
            set => SetScopedMetadata(ref _windowsPublisher, value, nameof(WindowsPublisher), nameof(Publisher), IsWindowsTarget);
        }

        public string? AndroidAppName
        {
            get => _androidAppName;
            set => SetScopedMetadata(ref _androidAppName, value, nameof(AndroidAppName), nameof(AppName), IsAndroidTarget);
        }

        public string? AndroidVersion
        {
            get => _androidVersion;
            set => SetScopedMetadata(ref _androidVersion, value, nameof(AndroidVersion), nameof(Version), IsAndroidTarget);
        }

        public string? AndroidPublisher
        {
            get => _androidPublisher;
            set => SetScopedMetadata(ref _androidPublisher, value, nameof(AndroidPublisher), nameof(Publisher), IsAndroidTarget);
        }

        private void SetScopedMetadata(
            ref string? field,
            string? value,
            string propertyName,
            string facadePropertyName,
            bool affectsCurrentScope)
        {
            if (SetProperty(ref field, value, propertyName) && affectsCurrentScope)
            {
                OnPropertyChanged(facadePropertyName);
            }
        }

        public void LoadFromConfig(AppConfig? config)
        {
            if (config == null)
            {
                return;
            }

            WindowsAppName = FirstValue(config.WindowsAppName, config.AppName, "BlueSapphire");
            WindowsVersion = FirstValue(config.WindowsVersion, config.Version, "1.0.0");
            WindowsPublisher = FirstValue(config.WindowsPublisher, config.Publisher);

            AndroidAppName = FirstValue(config.AndroidAppName, config.AppName);
            AndroidVersion = FirstValue(config.AndroidVersion, config.Version, "1.0.0");
            AndroidPublisher = FirstValue(config.AndroidPublisher, config.Publisher);

            AppID = config.AppID;
            ProjectPath = config.ProjectPath;
            RawOutputDir = config.RawOutputDir;
            SetupOutputDir = config.SetupOutputDir;
            InnoSetupPath = config.InnoSetupPath;
            IssScriptPath = config.IssScriptPath;
            WindowsIconPath = config.WindowsIconPath;
            MakeInstaller = config.MakeInstaller;
            AndroidProjectRoot = config.AndroidProjectRoot;
            AndroidModuleName = string.IsNullOrWhiteSpace(config.AndroidModuleName) ? "app" : config.AndroidModuleName;
            AndroidBuildType = string.IsNullOrWhiteSpace(config.AndroidBuildType) ? "Debug" : config.AndroidBuildType;
            AndroidIconPath = config.AndroidIconPath;
            AndroidApkOutputDir = config.AndroidApkOutputDir;
            WindowsBuildCommand = config.WindowsBuildCommand;
            WindowsBuildWorkingDir = config.WindowsBuildWorkingDir;
            WindowsBuildArtifactDir = config.WindowsBuildArtifactDir;
            LastAndroidBuildType = config.LastAndroidBuildType;
            TargetPlatform = string.IsNullOrWhiteSpace(config.TargetPlatform) ? "Windows" : config.TargetPlatform;
            PresetName = config.PresetName;
        }

        public AppConfig ToConfig()
        {
            return new AppConfig
            {
                TargetPlatform = TargetPlatform,
                PresetName = PresetName,
                AppName = AppName,
                Version = Version,
                Publisher = Publisher,
                WindowsAppName = WindowsAppName,
                WindowsVersion = WindowsVersion,
                WindowsPublisher = WindowsPublisher,
                AndroidAppName = AndroidAppName,
                AndroidVersion = AndroidVersion,
                AndroidPublisher = AndroidPublisher,
                AppID = AppID,
                ProjectPath = ProjectPath,
                RawOutputDir = RawOutputDir,
                SetupOutputDir = SetupOutputDir,
                InnoSetupPath = InnoSetupPath,
                IssScriptPath = IssScriptPath,
                WindowsIconPath = WindowsIconPath,
                MakeInstaller = MakeInstaller,
                AndroidProjectRoot = AndroidProjectRoot,
                AndroidModuleName = AndroidModuleName,
                AndroidBuildType = AndroidBuildType,
                AndroidIconPath = AndroidIconPath,
                AndroidApkOutputDir = AndroidApkOutputDir,
                WindowsBuildCommand = WindowsBuildCommand,
                WindowsBuildWorkingDir = WindowsBuildWorkingDir,
                WindowsBuildArtifactDir = WindowsBuildArtifactDir,
                LastAndroidBuildType = LastAndroidBuildType,
            };
        }

        private static string? FirstValue(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        public void RefreshWindowsIconMetadata()
        {
            IconInspectionResult inspection = IconAssetHelper.InspectWindowsIcon(WindowsIconPath);
            WindowsIconStatus = inspection.StatusMessage;
            WindowsIconReady = inspection.IsValid;
        }

        public void RefreshAndroidIconMetadata()
        {
            IconInspectionResult inspection = IconAssetHelper.InspectAndroidIcon(AndroidIconPath);
            AndroidIconStatus = inspection.StatusMessage;
            AndroidIconReady = inspection.IsValid;
        }

        public void RefreshAndroidProjectMetadata()
        {
            _isRefreshingAndroidMetadata = true;
            try
            {
                if (string.IsNullOrWhiteSpace(AndroidProjectRoot))
                {
                    AndroidDetectedModulesSummary = "请选择包含 gradlew.bat 的 Android 项目根目录";
                    AndroidSigningStatus = "尚未检测签名状态";
                    AndroidOutputPathPreview = "尚未识别 APK 输出路径";
                    AndroidReleaseSigningReady = false;
                    return;
                }

                AndroidProjectInfo info = AndroidProjectHelper.InspectProject(
                    AndroidProjectRoot,
                    AndroidModuleName,
                    AndroidBuildType);

                if (!string.IsNullOrWhiteSpace(info.PreferredModuleName) &&
                    !string.Equals(AndroidModuleName, info.PreferredModuleName, StringComparison.OrdinalIgnoreCase))
                {
                    AndroidModuleName = info.PreferredModuleName;
                }

                AndroidDetectedModulesSummary = info.DetectedModulesSummary;
                AndroidSigningStatus = info.SigningStatus;
                AndroidOutputPathPreview = GetAndroidOutputPreview(info.OutputPathPreview, AndroidApkOutputDir);
                AndroidReleaseSigningReady = info.ReleaseSigningReady;
            }
            catch (Exception ex)
            {
                AndroidDetectedModulesSummary = $"识别失败：{ex.Message}";
                AndroidSigningStatus = "签名状态暂不可用";
                AndroidOutputPathPreview = "APK 输出路径暂不可用";
                AndroidReleaseSigningReady = false;
            }
            finally
            {
                _isRefreshingAndroidMetadata = false;
            }
        }

        public void RefreshMetadata()
        {
            RefreshWindowsIconMetadata();
            RefreshAndroidIconMetadata();
            RefreshAndroidProjectMetadata();
        }

        private static string GetAndroidOutputPreview(string detectedOutputPath, string? customOutputDirectory)
        {
            if (string.IsNullOrWhiteSpace(customOutputDirectory))
            {
                return detectedOutputPath;
            }

            string outputDirectory = Path.GetFullPath(customOutputDirectory);
            return Path.Combine(outputDirectory, Path.GetFileName(detectedOutputPath));
        }
    }
}