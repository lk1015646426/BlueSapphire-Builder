using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlueSapphire.Builder.Services
{
    public class LogEventArgs : EventArgs
    {
        public string Message { get; }
        public bool IsError { get; }

        public LogEventArgs(string message, bool isError = false)
        {
            Message = message;
            IsError = isError;
        }
    }

    public class BuilderService
    {
        private static readonly string[] ForbiddenPublishEntries =
        {
            "BlueSapphire.Tests",
            "TestData",
            ".git",
            "obj"
        };

        public event EventHandler<LogEventArgs>? LogReceived;
        public event EventHandler<double>? ProgressChanged;

        public async Task BuildAsync(AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            config.Normalize();

            string projectPath = RequireExistingFile(config.ProjectPath, "找不到项目文件 (.csproj)");
            string projectDirectory = Path.GetDirectoryName(projectPath) ?? throw new InvalidOperationException("无法解析项目目录。");
            string appName = RequireValue(config.AppName, "未设置应用名称");
            string version = RequireValue(config.Version, "未设置版本号");
            string publisher = RequireValue(config.Publisher, "未设置发布组织/公司");
            string appId = RequireValue(config.AppID, "未设置应用唯一识别码");
            string publishOutputDir = RequireDirectoryPath(config.PublishOutputDir, "未设置发布产物目录");
            string setupOutputDir = RequireDirectoryPath(config.SetupOutputDir, "未设置安装包输出目录");
            string issPath = ResolveIssScriptPath(config.IssScriptPath, projectDirectory);

            ValidatePublishOutputDirectory(projectDirectory, publishOutputDir);

            string? innoSetupPath = null;
            if (config.MakeInstaller)
            {
                ValidateOutputDirectory(projectDirectory, setupOutputDir, "安装包输出目录");
                innoSetupPath = RequireExistingFile(config.InnoSetupPath, "Inno Setup 路径未配置，无法生成安装包。");
            }

            SendLog($">>> ProjectPath: {projectPath}");
            SendLog($">>> PublishOutputDir: {publishOutputDir}");
            SendLog($">>> IssScriptPath: {issPath}");
            SendLog($">>> SetupOutputDir: {setupOutputDir}");
            if (config.MakeInstaller)
            {
                SendLog($">>> InnoSetupPath: {innoSetupPath}");
            }

            PrepareOutputDirectory(publishOutputDir);

            SendLog(">>> [1/2] 正在编译 .NET 核心...");
            ReportProgress(5);

            string publishArgs =
                $"publish \"{projectPath}\" -c Release -r win-x64 --self-contained true " +
                $"-p:WindowsPackageType=None -p:Version={version} -p:Platform=x64 -o \"{publishOutputDir}\"";

            await RunCommandAsync("dotnet", publishArgs, Encoding.UTF8, 5.0, 50.0);

            ValidatePublishedArtifacts(publishOutputDir, appName);
            SendLog(">>> 编译成功！发布产物已生成。");

            if (config.MakeInstaller)
            {
                PrepareOutputDirectory(setupOutputDir);
                SendLog(">>> [2/2] 正在编译安装包...");

                string installerBaseName = $"{appName}_Setup_v{version}";
                string isccArgs =
                    $"/dSourcePath=\"{publishOutputDir}\" " +
                    $"/dMyAppName=\"{appName}\" " +
                    $"/dMyAppVersion=\"{version}\" " +
                    $"/dMyAppPublisher=\"{publisher}\" " +
                    $"/dMyAppId=\"{appId}\" " +
                    $"/O\"{setupOutputDir}\" " +
                    $"/F\"{installerBaseName}\" " +
                    $"\"{issPath}\"";

                await RunCommandAsync(innoSetupPath!, isccArgs, Encoding.UTF8, 50.0, 99.0);

                string installerPath = Path.Combine(setupOutputDir, installerBaseName + ".exe");
                if (!File.Exists(installerPath))
                {
                    throw new FileNotFoundException("安装包生成失败，未找到目标安装包。", installerPath);
                }

                SendLog(">>> 安装包制作完成！");
            }

            ReportProgress(100);
        }

        private async Task RunCommandAsync(string fileName, string arguments, Encoding encoding, double startProgress, double endProgress)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding
            };

            using var process = new Process { StartInfo = psi };
            double currentProgress = startProgress;

            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                {
                    return;
                }

                SendLog(e.Data);
                double remaining = endProgress - currentProgress;
                currentProgress += remaining * 0.03;
                ReportProgress(currentProgress);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    SendLog(e.Data, true);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception($"命令执行失败，退出代码：{process.ExitCode}");
            }

            ReportProgress(endProgress);
        }

        private static string ResolveIssScriptPath(string? issScriptPath, string projectDirectory)
        {
            string resolvedPath = string.IsNullOrWhiteSpace(issScriptPath)
                ? Path.Combine(projectDirectory, "installer.iss")
                : issScriptPath;

            return RequireExistingFile(resolvedPath, "找不到安装脚本 (.iss)");
        }

        private static void ValidatePublishOutputDirectory(string projectDirectory, string publishOutputDir)
        {
            ValidateOutputDirectory(projectDirectory, publishOutputDir, "发布产物目录");

            if (PathsEqual(projectDirectory, publishOutputDir))
            {
                throw new InvalidOperationException("发布产物目录不能直接指向项目根目录。");
            }

            string normalizedPath = EnsureTrailingSeparator(Path.GetFullPath(publishOutputDir)).ToLowerInvariant();
            if (normalizedPath.Contains(@"\bin\debug\"))
            {
                throw new InvalidOperationException("发布产物目录不能指向 Debug 输出目录。");
            }
        }

        private static void ValidateOutputDirectory(string projectDirectory, string outputDirectory, string displayName)
        {
            string fullPath = Path.GetFullPath(outputDirectory);
            string rootPath = Path.GetPathRoot(fullPath) ?? string.Empty;
            if (PathsEqual(fullPath, rootPath))
            {
                throw new InvalidOperationException($"{displayName}不能直接指向磁盘根目录。");
            }

            if (PathsEqual(fullPath, projectDirectory))
            {
                throw new InvalidOperationException($"{displayName}不能直接指向项目根目录。");
            }
        }

        private static void ValidatePublishedArtifacts(string publishOutputDir, string appName)
        {
            string appExecutable = Path.Combine(publishOutputDir, appName + ".exe");
            if (!File.Exists(appExecutable))
            {
                throw new FileNotFoundException("发布产物目录中未找到应用程序主文件。", appExecutable);
            }

            foreach (string entryName in ForbiddenPublishEntries)
            {
                string path = Path.Combine(publishOutputDir, entryName);
                if (Directory.Exists(path) || File.Exists(path))
                {
                    throw new InvalidOperationException($"发布产物目录中检测到不应打包的内容: {entryName}");
                }
            }

            if (File.Exists(Path.Combine(publishOutputDir, "BlueSapphire.csproj")))
            {
                throw new InvalidOperationException("发布产物目录中检测到项目源码文件，路径配置错误。");
            }
        }

        private static void PrepareOutputDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(path))
                {
                    string fullPath = Path.GetFullPath(entry);
                    var attributes = File.GetAttributes(fullPath);
                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        Directory.Delete(fullPath, true);
                    }
                    else
                    {
                        File.Delete(fullPath);
                    }
                }
            }
            else
            {
                Directory.CreateDirectory(path);
            }
        }

        private static string RequireExistingFile(string? path, string errorMessage)
        {
            string fullPath = RequireValue(path, errorMessage);
            fullPath = Path.GetFullPath(fullPath);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(errorMessage, fullPath);
            }

            return fullPath;
        }

        private static string RequireDirectoryPath(string? path, string errorMessage)
        {
            string fullPath = RequireValue(path, errorMessage);
            return Path.GetFullPath(fullPath);
        }

        private static string RequireValue(string? value, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(errorMessage);
            }

            return value.Trim();
        }

        private static bool PathsEqual(string left, string right)
        {
            string normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private void SendLog(string message, bool isError = false) => LogReceived?.Invoke(this, new LogEventArgs(message, isError));
        private void ReportProgress(double value) => ProgressChanged?.Invoke(this, value);
    }
}
