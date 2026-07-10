using System;

namespace BlueSapphire.Builder
{
    public class AppConfig
    {
        public string TargetPlatform { get; set; } = "Windows";
        public string? PresetName { get; set; }
        public string? AppName { get; set; }
        public string? Version { get; set; }
        public string? Publisher { get; set; }
        public string? WindowsAppName { get; set; }
        public string? WindowsVersion { get; set; }
        public string? WindowsPublisher { get; set; }
        public string? WindowsIconPath { get; set; }
        public string? AndroidAppName { get; set; }
        public string? AndroidVersion { get; set; }
        public string? AndroidPublisher { get; set; }
        public string? AndroidIconPath { get; set; }
        public string? AppID { get; set; }
        public string? ProjectPath { get; set; }
        public string? RawOutputDir { get; set; }
        public string? SetupOutputDir { get; set; }

        // Inno Setup 编译器路径
        public string? InnoSetupPath { get; set; }

        // [新增] 自定义安装脚本路径 (.iss)
        public string? IssScriptPath { get; set; }

        // 是否生成安装包
        public bool MakeInstaller { get; set; } = true;

        // Android Studio / Gradle 项目根目录
        public string? AndroidProjectRoot { get; set; }

        // Android 主模块名，通常是 app
        public string AndroidModuleName { get; set; } = "app";

        // Android APK 构建类型：Debug / Release
        public string AndroidBuildType { get; set; } = "Debug";

        // Android APK 额外输出目录（可选）
        public string? AndroidApkOutputDir { get; set; }

        // [新增] Windows 自定义构建模式：当 ProjectPath 不是 .csproj 时启用。
        // 留空则默认 "npm run tauri build"，用于 Tauri / Node 等非 .NET 工程。
        public string? WindowsBuildCommand { get; set; }

        // [新增] 自定义构建的工作目录（可空）。留空时按 ProjectPath 所在目录推导。
        public string? WindowsBuildWorkingDir { get; set; }

        // [新增] 自定义构建产物搜索目录。构建完成后在此目录递归查找 .exe / .msi 安装包。
        // Tauri 默认为 src-tauri/target/release/bundle
        public string? WindowsBuildArtifactDir { get; set; }

        // 记录上次 Android 构建类型，用于检测签名方案切换
        public string? LastAndroidBuildType { get; set; }
    }
}