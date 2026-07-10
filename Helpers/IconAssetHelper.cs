using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;

namespace BlueSapphire.Builder.Helpers
{
    public sealed class IconInspectionResult
    {
        public bool IsSpecified { get; init; }
        public bool IsValid { get; init; }
        public string? FullPath { get; init; }
        public int MaxWidth { get; init; }
        public int MaxHeight { get; init; }
        public string StatusMessage { get; init; } = "未设置图标";
    }

    public sealed class AndroidIconApplyResult
    {
        public string ManifestPath { get; init; } = string.Empty;
        public string LauncherResourceName { get; init; } = string.Empty;
        public IReadOnlyList<string> GeneratedFiles { get; init; } = Array.Empty<string>();
    }

    internal sealed class AndroidManifestIconBackupEntry
    {
        public string ProjectRoot { get; set; } = string.Empty;
        public string ModuleName { get; set; } = string.Empty;
        public string ManifestPath { get; set; } = string.Empty;
        public string? OriginalIcon { get; set; }
        public string? OriginalRoundIcon { get; set; }
    }

    internal sealed class AndroidManifestIconBackupDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public List<AndroidManifestIconBackupEntry> Entries { get; set; } = new();
    }

    public static class IconAssetHelper
    {
        private const string LauncherResourceName = "bs_builder_launcher";
        private const string RoundLauncherResourceName = "bs_builder_launcher_round";
        private const string AndroidManifestBackupFileName = "android_icon_manifest_state_v1.json";

        // WPF 视觉树渲染（DrawingVisual/RenderTargetBitmap）必须在 STA 线程执行，
        // 此引用用于把渲染调度回 UI 线程。由 Application 启动时设置。
        private static Dispatcher? _uiDispatcher;
        public static void InitializeDispatcher(Dispatcher dispatcher) => _uiDispatcher = dispatcher;

        private static readonly (string Folder, int Size)[] AndroidLauncherSizes =
        {
            ("mipmap-mdpi", 48),
            ("mipmap-hdpi", 72),
            ("mipmap-xhdpi", 96),
            ("mipmap-xxhdpi", 144),
            ("mipmap-xxxhdpi", 192)
        };

        public static IconInspectionResult InspectWindowsIcon(string? iconPath)
        {
            return InspectIcon(iconPath, "未设置图标，将使用项目默认图标", forAndroid: false);
        }

        public static IconInspectionResult InspectAndroidIcon(string? iconPath)
        {
            return InspectIcon(iconPath, "未设置图标，将使用项目默认启动图标", forAndroid: true);
        }


        public static bool SupportsInstallerIconDefine(string issPath)
        {
            string resolvedIssPath = Path.GetFullPath(issPath);
            if (!File.Exists(resolvedIssPath))
            {
                return false;
            }

            string scriptText = File.ReadAllText(resolvedIssPath);
            return scriptText.Contains("MySetupIconFile", StringComparison.OrdinalIgnoreCase);
        }

        public static AndroidIconApplyResult ApplyAndroidLauncherIcons(string projectRoot, string moduleName, string iconPath)
        {
            string resolvedIconPath = ResolveRequiredIconPath(iconPath);
            string moduleDirectory = AndroidProjectHelper.ResolveModuleDirectory(projectRoot, moduleName);
            string manifestPath = ResolveAndroidManifestPath(moduleDirectory);
            string resRoot = Path.Combine(moduleDirectory, "src", "main", "res");

            // 加载源图与备份原 manifest 引用，失败立即抛出，不修改任何文件
            BitmapSource sourceBitmap = LoadBestBitmapSource(resolvedIconPath);
            RememberOriginalManifestIcons(projectRoot, moduleName, manifestPath);

            var generatedFiles = new List<string>();
            try
            {
                foreach ((string folder, int size) in AndroidLauncherSizes)
                {
                    string targetDirectory = Path.Combine(resRoot, folder);
                    Directory.CreateDirectory(targetDirectory);

                    // 关键：在 UI 线程上渲染 WPF 视觉树，否则会抛 InvalidOperationException
                    BitmapSource resized = RenderOnUiThread(() => ResizeBitmap(sourceBitmap, size));

                    string regularPath = Path.Combine(targetDirectory, $"{LauncherResourceName}.png");
                    string roundPath = Path.Combine(targetDirectory, $"{RoundLauncherResourceName}.png");

                    SaveBitmapAsPng(resized, regularPath);
                    SaveBitmapAsPng(resized, roundPath);

                    generatedFiles.Add(regularPath);
                    generatedFiles.Add(roundPath);
                }

                UpdateAndroidManifestIcons(
                    manifestPath,
                    $"@mipmap/{LauncherResourceName}",
                    $"@mipmap/{RoundLauncherResourceName}");

                return new AndroidIconApplyResult
                {
                    ManifestPath = manifestPath,
                    LauncherResourceName = LauncherResourceName,
                    GeneratedFiles = generatedFiles
                };
            }
            catch
            {
                // 渲染中途失败：回滚已生成的文件，避免污染 Android 工程
                foreach (string file in generatedFiles)
                {
                    TryDeleteFile(file);
                }
                throw;
            }
        }

        public static bool TryRestoreAndroidLauncherIcons(string projectRoot, string moduleName)
        {
            AndroidManifestIconBackupDocument backupDocument = LoadBackupDocument();
            string normalizedProjectRoot = NormalizePath(projectRoot);
            string normalizedModuleName = AndroidProjectHelper.NormalizeModuleName(moduleName);

            AndroidManifestIconBackupEntry? entry = backupDocument.Entries.FirstOrDefault(item =>
                string.Equals(item.ProjectRoot, normalizedProjectRoot, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ModuleName, normalizedModuleName, StringComparison.OrdinalIgnoreCase));

            if (entry == null || !File.Exists(entry.ManifestPath))
            {
                return false;
            }

            UpdateAndroidManifestIcons(entry.ManifestPath, entry.OriginalIcon, entry.OriginalRoundIcon);
            DeleteGeneratedAndroidLauncherIcons(projectRoot, moduleName);
            return true;
        }

        private static IconInspectionResult InspectIcon(string? iconPath, string emptyMessage, bool forAndroid)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return new IconInspectionResult
                {
                    IsSpecified = false,
                    IsValid = false,
                    StatusMessage = emptyMessage
                };
            }

            try
            {
                string resolvedPath = ResolveRequiredIconPath(iconPath);
                BitmapSource source = LoadBestBitmapSource(resolvedPath);
                string statusMessage = BuildStatusMessage(source.PixelWidth, source.PixelHeight, forAndroid);

                return new IconInspectionResult
                {
                    IsSpecified = true,
                    IsValid = true,
                    FullPath = resolvedPath,
                    MaxWidth = source.PixelWidth,
                    MaxHeight = source.PixelHeight,
                    StatusMessage = statusMessage
                };
            }
            catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or InvalidOperationException or IOException)
            {
                return new IconInspectionResult
                {
                    IsSpecified = true,
                    IsValid = false,
                    StatusMessage = $"图标不可用：{ex.Message}"
                };
            }
        }

        public static string? ResolveOptionalIconPath(string? iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                return null;
            }

            return ResolveRequiredIconPath(iconPath);
        }

        private static string ResolveRequiredIconPath(string? iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
            {
                throw new ArgumentException("未选择图标文件");
            }

            string resolvedPath = Path.GetFullPath(iconPath);
            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException($"找不到图标文件：{resolvedPath}");
            }

            if (!string.Equals(Path.GetExtension(resolvedPath), ".ico", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("当前仅支持 .ico 图标文件");
            }

            _ = LoadBestBitmapSource(resolvedPath);
            return resolvedPath;
        }

        private static BitmapSource LoadBestBitmapSource(string iconPath)
        {
            using FileStream stream = File.OpenRead(iconPath);
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapFrame? bestFrame = decoder.Frames
                .Where(frame => frame.PixelWidth > 0 && frame.PixelHeight > 0)
                .OrderByDescending(frame => frame.PixelWidth * frame.PixelHeight)
                .ThenByDescending(frame => frame.Format.BitsPerPixel)
                .FirstOrDefault();

            if (bestFrame == null)
            {
                throw new InvalidOperationException("ICO 文件中没有可用图像帧");
            }

            BitmapSource frame = bestFrame;
            frame.Freeze();
            return frame;
        }

        private static string BuildStatusMessage(int width, int height, bool forAndroid)
        {
            if (!forAndroid)
            {
                return $"图标可用：主帧 {width}x{height}，构建时会嵌入 EXE 并用于安装包。";
            }

            return Math.Min(width, height) >= 192
                ? $"图标可用：主帧 {width}x{height}，构建时会自动生成 APK 启动图标资源。"
                : $"图标可用但分辨率偏小：主帧 {width}x{height}，APK 启动图标可生成，但高分屏可能偏糊。";
        }

        /// <summary>
        /// 在 UI 线程上执行 WPF 渲染操作。DrawingVisual/RenderTargetBitmap 必须在 STA 线程执行。
        /// </summary>
        private static T RenderOnUiThread<T>(Func<T> action)
        {
            if (_uiDispatcher == null || _uiDispatcher.CheckAccess())
            {
                return action();
            }

            T result = default!;
            Exception? captured = null;
            _uiDispatcher.Invoke(() =>
            {
                try { result = action(); }
                catch (Exception ex) { captured = ex; }
            }, DispatcherPriority.Background);

            if (captured != null)
            {
                throw captured;
            }
            return result;
        }

        private static BitmapSource ResizeBitmap(BitmapSource source, int size)
        {
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);

            using (DrawingContext drawingContext = visual.RenderOpen())
            {
                drawingContext.DrawImage(source, new Rect(0, 0, size, size));
            }

            var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();
            return target;
        }

        private static void SaveBitmapAsPng(BitmapSource source, string outputPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using FileStream stream = File.Create(outputPath);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            encoder.Save(stream);
        }

        private static string ResolveAndroidManifestPath(string moduleDirectory)
        {
            string manifestPath = Path.Combine(moduleDirectory, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException($"找不到 AndroidManifest.xml：{manifestPath}");
            }

            return manifestPath;
        }

        private static void UpdateAndroidManifestIcons(string manifestPath, string? iconResource, string? roundIconResource)
        {
            string manifestText = File.ReadAllText(manifestPath);
            manifestText = ReplaceManifestAttribute(manifestText, "icon", iconResource);
            manifestText = ReplaceManifestAttribute(manifestText, "roundIcon", roundIconResource);
            File.WriteAllText(manifestPath, manifestText, new UTF8Encoding(false));
        }

        private static string ReplaceManifestAttribute(string manifestText, string attributeName, string? value)
        {
            string pattern = $@"\s+android:{attributeName}\s*=\s*""[^""]*""";

            if (string.IsNullOrWhiteSpace(value))
            {
                return Regex.Replace(manifestText, pattern, string.Empty, RegexOptions.IgnoreCase);
            }

            string replacement = $" android:{attributeName}=\"{value}\"";
            if (Regex.IsMatch(manifestText, pattern, RegexOptions.IgnoreCase))
            {
                return Regex.Replace(manifestText, pattern, replacement, RegexOptions.IgnoreCase);
            }

            return Regex.Replace(
                manifestText,
                "<application",
                $"<application{replacement}",
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(1));
        }

        private static void RememberOriginalManifestIcons(string projectRoot, string moduleName, string manifestPath)
        {
            AndroidManifestIconBackupDocument document = LoadBackupDocument();
            string normalizedProjectRoot = NormalizePath(projectRoot);
            string normalizedModuleName = AndroidProjectHelper.NormalizeModuleName(moduleName);

            bool exists = document.Entries.Any(entry =>
                string.Equals(entry.ProjectRoot, normalizedProjectRoot, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.ModuleName, normalizedModuleName, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                return;
            }

            XDocument manifestDocument = XDocument.Load(manifestPath, LoadOptions.PreserveWhitespace);
            XElement applicationElement = manifestDocument.Root?.Element("application")
                ?? throw new InvalidOperationException("AndroidManifest.xml 中缺少 <application> 节点。");
            XNamespace androidNs = "http://schemas.android.com/apk/res/android";

            document.Entries.Add(new AndroidManifestIconBackupEntry
            {
                ProjectRoot = normalizedProjectRoot,
                ModuleName = normalizedModuleName,
                ManifestPath = manifestPath,
                OriginalIcon = applicationElement.Attribute(androidNs + "icon")?.Value,
                OriginalRoundIcon = applicationElement.Attribute(androidNs + "roundIcon")?.Value
            });

            SaveBackupDocument(document);
        }

        private static AndroidManifestIconBackupDocument LoadBackupDocument()
        {
            string backupPath = GetBackupFilePath();
            if (!File.Exists(backupPath))
            {
                return new AndroidManifestIconBackupDocument();
            }

            try
            {
                string json = File.ReadAllText(backupPath);
                AndroidManifestIconBackupDocument? doc = JsonSerializer.Deserialize<AndroidManifestIconBackupDocument>(json);
                if (doc == null || doc.SchemaVersion != 1)
                {
                    return new AndroidManifestIconBackupDocument();
                }
                return doc;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // 损坏的备份文档：备份到 .corrupt 后缀后返回空文档
                // 这样用户原 manifest 引用至少不会被破坏
                try
                {
                    if (File.Exists(backupPath))
                    {
                        File.Move(backupPath, backupPath + ".corrupt", overwrite: true);
                    }
                }
                catch { /* 备份失败不影响主流程 */ }
                return new AndroidManifestIconBackupDocument();
            }
        }

        private static void SaveBackupDocument(AndroidManifestIconBackupDocument document)
        {
            string backupPath = GetBackupFilePath();
            string json = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
            SafeFileWriter.WriteAllText(backupPath, json);
        }

        private static string GetBackupFilePath()
        {
            // 优先 %APPDATA%\BlueSapphire.Builder，便携模式回退到 exe 同目录
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "BlueSapphire.Builder");
            try
            {
                Directory.CreateDirectory(appFolder);
                return Path.Combine(appFolder, AndroidManifestBackupFileName);
            }
            catch
            {
                return Path.Combine(AppContext.BaseDirectory, AndroidManifestBackupFileName);
            }
        }

        private static void DeleteGeneratedAndroidLauncherIcons(string projectRoot, string moduleName)
        {
            string moduleDirectory = AndroidProjectHelper.ResolveModuleDirectory(projectRoot, moduleName);
            string resRoot = Path.Combine(moduleDirectory, "src", "main", "res");

            foreach ((string folder, _) in AndroidLauncherSizes)
            {
                string regularPath = Path.Combine(resRoot, folder, $"{LauncherResourceName}.png");
                string roundPath = Path.Combine(resRoot, folder, $"{RoundLauncherResourceName}.png");

                TryDeleteFile(regularPath);
                TryDeleteFile(roundPath);
            }
        }

        private static void TryDeleteFile(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // 文件被占用，忽略
            }
            catch (UnauthorizedAccessException)
            {
                // 无权限，忽略
            }
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant();
        }
    }
}
