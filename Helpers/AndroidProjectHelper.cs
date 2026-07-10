using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlueSapphire.Builder.Helpers
{
    public sealed class AndroidProjectInfo
    {
        public IReadOnlyList<string> ApplicationModules { get; init; } = Array.Empty<string>();
        public string? PreferredModuleName { get; init; }
        public string DetectedModulesSummary { get; init; } = "未检测到 Android 应用模块";
        public string SigningStatus { get; init; } = "尚未检测签名状态";
        public string OutputPathPreview { get; init; } = "尚未识别 APK 输出路径";
        public bool ReleaseSigningReady { get; init; }
    }

    public static class AndroidProjectHelper
    {
        private static readonly string[] RequiredKeystoreKeys =
        {
            "storeFile",
            "storePassword",
            "keyAlias",
            "keyPassword"
        };

        public static AndroidProjectInfo InspectProject(string projectRoot, string? selectedModuleName, string? buildType)
        {
            string normalizedRoot = Path.GetFullPath(projectRoot);
            EnsureValidProjectRoot(normalizedRoot);

            IReadOnlyList<string> applicationModules = FindApplicationModules(normalizedRoot);
            string normalizedBuildType = NormalizeBuildType(buildType);

            string? preferredModule = null;
            if (applicationModules.Count > 0)
            {
                string desiredModule = NormalizeModuleName(selectedModuleName);
                preferredModule = applicationModules.FirstOrDefault(module =>
                    string.Equals(module, desiredModule, StringComparison.OrdinalIgnoreCase))
                    ?? applicationModules[0];
            }

            string signingStatus = BuildSigningStatus(normalizedRoot, preferredModule);
            string outputPreview = preferredModule == null
                ? "未找到可打包的 application 模块，暂时无法预估 APK 输出路径"
                : GetApkOutputPreview(normalizedRoot, preferredModule, normalizedBuildType);

            return new AndroidProjectInfo
            {
                ApplicationModules = applicationModules,
                PreferredModuleName = preferredModule,
                DetectedModulesSummary = applicationModules.Count == 0
                    ? "未检测到 application 模块"
                    : string.Join("  /  ", applicationModules),
                SigningStatus = signingStatus,
                OutputPathPreview = outputPreview,
                ReleaseSigningReady = IsReleaseSigningReady(normalizedRoot, preferredModule)
            };
        }

        public static string NormalizeModuleName(string? moduleName)
        {
            string normalized = string.IsNullOrWhiteSpace(moduleName) ? "app" : moduleName.Trim();
            normalized = normalized.Trim(':').Replace('\\', ':').Replace('/', ':');

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("Android 模块名不能为空");
            }

            return normalized;
        }

        public static string NormalizeBuildType(string? buildType)
        {
            if (string.Equals(buildType, "Release", StringComparison.OrdinalIgnoreCase))
            {
                return "Release";
            }

            return "Debug";
        }

        public static string ResolveModuleDirectory(string projectRoot, string moduleName)
        {
            string normalizedRoot = Path.GetFullPath(projectRoot);
            string normalizedModule = NormalizeModuleName(moduleName);

            string moduleDirectory = normalizedRoot;
            foreach (string segment in normalizedModule.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                moduleDirectory = Path.Combine(moduleDirectory, segment);
            }

            if (!Directory.Exists(moduleDirectory))
            {
                throw new DirectoryNotFoundException($"找不到 Android 模块目录：{moduleDirectory}");
            }

            if (GetBuildScriptPath(moduleDirectory) == null)
            {
                throw new FileNotFoundException($"模块目录中缺少 build.gradle(.kts)：{moduleDirectory}");
            }

            return moduleDirectory;
        }

        public static string ResolveLatestApkPath(string moduleDirectory, string buildType)
        {
            string outputsRoot = Path.Combine(moduleDirectory, "build", "outputs", "apk");
            if (!Directory.Exists(outputsRoot))
            {
                throw new DirectoryNotFoundException($"未找到 APK 输出目录：{outputsRoot}");
            }

            string variantFolderName = NormalizeBuildType(buildType).ToLowerInvariant();
            string variantDirectory = Path.Combine(outputsRoot, variantFolderName);

            string[] candidates = Directory.GetFiles(outputsRoot, "*.apk", SearchOption.AllDirectories);
            string? apkPath = candidates
                .Where(path =>
                    path.Contains($"{Path.DirectorySeparatorChar}{variantFolderName}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith($"-{variantFolderName}.apk", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
                ?? candidates.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();

            if (string.IsNullOrWhiteSpace(apkPath))
            {
                throw new FileNotFoundException($"Gradle 已执行完成，但在 {variantDirectory} 下未找到 APK 文件。");
            }

            return apkPath;
        }

        private static void EnsureValidProjectRoot(string projectRoot)
        {
            if (!Directory.Exists(projectRoot))
            {
                throw new DirectoryNotFoundException($"找不到 Android 项目目录：{projectRoot}");
            }

            if (!File.Exists(Path.Combine(projectRoot, "gradlew.bat")))
            {
                throw new FileNotFoundException($"找不到 gradlew.bat：{Path.Combine(projectRoot, "gradlew.bat")}");
            }

            if (GetSettingsFilePath(projectRoot) == null)
            {
                throw new FileNotFoundException("当前目录不是标准 Android Gradle 根目录，缺少 settings.gradle(.kts)");
            }
        }

        private static IReadOnlyList<string> FindApplicationModules(string projectRoot)
        {
            string settingsFile = GetSettingsFilePath(projectRoot)
                ?? throw new FileNotFoundException("缺少 settings.gradle(.kts)");

            string settingsContent = File.ReadAllText(settingsFile);
            var modules = Regex.Matches(settingsContent, """["'](:[^"']+)["']""")
                .Cast<Match>()
                .Select(match => match.Groups[1].Value.Trim().Trim(':'))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return modules
                .Where(module => IsApplicationModule(projectRoot, module))
                .ToList();
        }

        private static bool IsApplicationModule(string projectRoot, string moduleName)
        {
            try
            {
                string moduleDirectory = ResolveModuleDirectory(projectRoot, moduleName);
                string buildScriptPath = GetBuildScriptPath(moduleDirectory)!;
                string content = File.ReadAllText(buildScriptPath);
                return content.Contains("com.android.application", StringComparison.OrdinalIgnoreCase) ||
                       content.Contains("libs.plugins.android.application", StringComparison.OrdinalIgnoreCase) ||
                       content.Contains("applicationId", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildSigningStatus(string projectRoot, string? preferredModule)
        {
            if (preferredModule == null)
            {
                return "未识别到 application 模块，暂时无法判断签名状态";
            }

            bool releaseReady = IsReleaseSigningReady(projectRoot, preferredModule);
            return releaseReady
                ? "Debug 使用系统默认 debug 签名；Release 已检测到 keystore 正式签名"
                : "Debug 使用系统默认 debug 签名；Release 尚未准备好正式签名";
        }

        private static bool IsReleaseSigningReady(string projectRoot, string? preferredModule)
        {
            if (preferredModule == null)
            {
                return false;
            }

            string moduleDirectory = ResolveModuleDirectory(projectRoot, preferredModule);
            string buildScriptPath = GetBuildScriptPath(moduleDirectory)
                ?? throw new FileNotFoundException($"模块目录中缺少 build.gradle(.kts)：{moduleDirectory}");

            string buildContent = File.ReadAllText(buildScriptPath);
            bool releaseUsesSigningConfig =
                buildContent.Contains("signingConfig = signingConfigs.findByName(\"release\")", StringComparison.OrdinalIgnoreCase) ||
                buildContent.Contains("signingConfig = signingConfigs.getByName(\"release\")", StringComparison.OrdinalIgnoreCase) ||
                buildContent.Contains("signingConfig = signingConfigs.release", StringComparison.OrdinalIgnoreCase) ||
                buildContent.Contains("signingConfig signingConfigs.release", StringComparison.OrdinalIgnoreCase);

            string keystorePropertiesPath = Path.Combine(projectRoot, "keystore.properties");
            if (!releaseUsesSigningConfig || !File.Exists(keystorePropertiesPath))
            {
                return false;
            }

            var values = ParsePropertiesFile(keystorePropertiesPath);
            return RequiredKeystoreKeys.All(key =>
                values.TryGetValue(key, out string? value) &&
                !string.IsNullOrWhiteSpace(value));
        }

        private static string GetApkOutputPreview(string projectRoot, string preferredModule, string buildType)
        {
            string moduleDirectory = ResolveModuleDirectory(projectRoot, preferredModule);
            string normalizedBuildType = NormalizeBuildType(buildType);

            try
            {
                return ResolveLatestApkPath(moduleDirectory, normalizedBuildType);
            }
            catch
            {
                string moduleLeaf = preferredModule.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
                string variantFolder = normalizedBuildType.ToLowerInvariant();
                return Path.Combine(moduleDirectory, "build", "outputs", "apk", variantFolder, $"{moduleLeaf}-{variantFolder}.apk");
            }
        }

        private static string? GetSettingsFilePath(string projectRoot)
        {
            string ktsPath = Path.Combine(projectRoot, "settings.gradle.kts");
            if (File.Exists(ktsPath))
            {
                return ktsPath;
            }

            string gradlePath = Path.Combine(projectRoot, "settings.gradle");
            return File.Exists(gradlePath) ? gradlePath : null;
        }

        private static string? GetBuildScriptPath(string moduleDirectory)
        {
            string ktsPath = Path.Combine(moduleDirectory, "build.gradle.kts");
            if (File.Exists(ktsPath))
            {
                return ktsPath;
            }

            string gradlePath = Path.Combine(moduleDirectory, "build.gradle");
            return File.Exists(gradlePath) ? gradlePath : null;
        }

        private static Dictionary<string, string> ParsePropertiesFile(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line[..separatorIndex].Trim();
                string value = line[(separatorIndex + 1)..].Trim();
                values[key] = value;
            }

            return values;
        }
    }
}
