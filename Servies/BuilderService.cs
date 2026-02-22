using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using BlueSapphire.Builder;

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
        public event EventHandler<LogEventArgs>? LogReceived;
        public event EventHandler<double>? ProgressChanged;

        public async Task BuildAsync(AppConfig config)
        {
            // 1. 基础校验
            if (!File.Exists(config.ProjectPath)) throw new FileNotFoundException("找不到项目文件 (.csproj)");
            if (string.IsNullOrWhiteSpace(config.RawOutputDir)) throw new ArgumentException("未设置原始输出目录");

            // ====================================================================
            // [正规军做法：编译前数据装填]
            // 在 dotnet publish 开始之前，把数据同步到源码的 Assets 目录下。
            // 配合 .csproj 里的 <Content> 声明，编译器会自动把它带进安装包。
            // ====================================================================
            SendLog(">>> [0/2] 正在同步跃迁记录数据 (DevMatrixLog.json)...", false);
            ReportProgress(5);
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string sourceLogPath = Path.Combine(localAppData, "BlueSapphire", "DevMatrixLog.json");

                if (File.Exists(sourceLogPath))
                {
                    string projDir = Path.GetDirectoryName(config.ProjectPath)!;
                    string targetAssetsDir = Path.Combine(projDir, "Assets");

                    if (!Directory.Exists(targetAssetsDir))
                    {
                        Directory.CreateDirectory(targetAssetsDir);
                    }

                    string targetLogPath = Path.Combine(targetAssetsDir, "DevMatrixLog.json");
                    File.Copy(sourceLogPath, targetLogPath, true);
                    SendLog($"✅ 成功注入开发日志到项目资源: {targetLogPath}");
                }
                else
                {
                    SendLog($"⚠️ 警告: 未在本地找到开发日志，本次发布的程序将无内置更新记录。", true);
                }
            }
            catch (Exception ex)
            {
                SendLog($"❌ 同步开发日志失败: {ex.Message}", true);
            }
            // ====================================================================

            // 2. 编译阶段 (.NET Publish)
            SendLog(">>> [1/2] 正在编译 .NET 核心...", false);
            ReportProgress(20);

            if (Directory.Exists(config.RawOutputDir))
            {
                try
                {
                    Directory.Delete(config.RawOutputDir, true);
                }
                catch (IOException)
                {
                    SendLog("⚠️ 警告: 无法清理旧目录，文件可能被占用。", true);
                }
            }

            var publishArgs = $"publish \"{config.ProjectPath}\" -c Release -r win-x64 --self-contained true -o \"{config.RawOutputDir}\" /p:Version={config.Version} /p:Platform=x64";

            await RunCommandAsync("dotnet", publishArgs, Encoding.UTF8);

            SendLog(">>> 编译成功！原始文件已生成。", false);
            ReportProgress(50);

            // 3. 打包阶段 (Inno Setup)
            if (config.MakeInstaller)
            {
                if (string.IsNullOrWhiteSpace(config.InnoSetupPath) || !File.Exists(config.InnoSetupPath))
                    throw new FileNotFoundException("未找到 Inno Setup 编译器 (ISCC.exe)！");

                SendLog(">>> [2/2] 正在构建安装包...", false);

                string? projDir = Path.GetDirectoryName(config.ProjectPath);

                string issPath = config.IssScriptPath;
                if (string.IsNullOrWhiteSpace(issPath))
                {
                    issPath = Path.Combine(projDir!, "installer.iss");
                    SendLog($"未指定 ISS 脚本，尝试默认路径: {issPath}");
                }

                if (!File.Exists(issPath))
                    throw new FileNotFoundException($"找不到安装脚本模板：{issPath}");

                string sourceIsl = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chinese.isl");
                string targetIsl = Path.Combine(Path.GetDirectoryName(issPath)!, "Chinese.isl");

                if (File.Exists(sourceIsl))
                {
                    try { File.Copy(sourceIsl, targetIsl, true); } catch { }
                    SendLog($"已自动部署汉化文件: {targetIsl}");
                }

                var isccArgs = $"/dSourcePath=\"{config.RawOutputDir}\" " +
                               $"/dMyAppName=\"{config.AppName}\" " +
                               $"/dMyAppVersion=\"{config.Version}\" " +
                               $"/dMyAppPublisher=\"{config.Publisher}\" " +
                               $"/dMyAppId=\"{config.AppID}\" " +
                               $"/O\"{config.SetupOutputDir}\" " +
                               $"/F\"{config.AppName}_Setup_v{config.Version}\" " +
                               $"\"{issPath}\"";

                await RunCommandAsync(config.InnoSetupPath, isccArgs, Encoding.GetEncoding("GB2312"));

                SendLog(">>> 安装包制作完成！", false);
            }

            ReportProgress(100);
        }

        private Task RunCommandAsync(string fileName, string arguments, Encoding encoding)
        {
            var tcs = new TaskCompletionSource<bool>();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = encoding,
                    StandardErrorEncoding = encoding
                },
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (s, e) => { if (e.Data != null) SendLog(CleanAnsi(e.Data)); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) SendLog("ERROR: " + CleanAnsi(e.Data), true); };

            process.Exited += (s, e) =>
            {
                if (process.ExitCode == 0) tcs.SetResult(true);
                else tcs.SetException(new Exception($"进程异常退出 (Code: {process.ExitCode})"));
                process.Dispose();
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return tcs.Task;
        }

        private void SendLog(string msg, bool isError = false) => LogReceived?.Invoke(this, new LogEventArgs(msg, isError));
        private void ReportProgress(double value) => ProgressChanged?.Invoke(this, value);

        private static string CleanAnsi(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return Regex.Replace(input, @"\x1B\[[^@-~]*[@-~]", string.Empty);
        }
    }
}