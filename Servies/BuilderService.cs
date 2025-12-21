using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BlueSapphire.Builder; // 引用 AppConfig

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

            // 2. 编译阶段 (.NET Publish)
            SendLog(">>> [1/2] 正在编译 .NET 核心...", false);
            ReportProgress(20);

            if (Directory.Exists(config.RawOutputDir))
                Directory.Delete(config.RawOutputDir, true);

            // WinUI 3 必须指定 Platform=x64
            var publishArgs = $"publish \"{config.ProjectPath}\" -c Release -r win-x64 --self-contained true -o \"{config.RawOutputDir}\" /p:Version={config.Version} /p:Platform=x64";

            // 使用 UTF8 调用 dotnet
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
                string issPath = Path.Combine(projDir!, "installer.iss");

                if (!File.Exists(issPath))
                    throw new FileNotFoundException($"找不到安装脚本模板：{issPath}");

                // 🔥【核心新增】自动搬运汉化文件
                // 从 Builder 的运行目录 -> 复制到 -> BlueSapphire 项目目录
                string sourceIsl = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chinese.isl");
                string targetIsl = Path.Combine(projDir!, "Chinese.isl");

                if (File.Exists(sourceIsl))
                {
                    File.Copy(sourceIsl, targetIsl, true); // true = 覆盖已存在的文件
                    SendLog($"已自动部署汉化文件: {targetIsl}");
                }
                else
                {
                    SendLog("⚠️ 警告: 在 Builder 目录下未找到 Chinese.isl，打包可能缺少汉化。", true);
                }

                // 组装参数
                var isccArgs = $"/dSourcePath=\"{config.RawOutputDir}\" " +
                               $"/dMyAppName=\"{config.AppName}\" " +
                               $"/dMyAppVersion=\"{config.Version}\" " +
                               $"/dMyAppPublisher=\"{config.Publisher}\" " +
                               $"/dMyAppId=\"{config.AppID}\" " +
                               $"/O\"{config.SetupOutputDir}\" " +
                               $"/F\"{config.AppName}_Setup_v{config.Version}\" " +
                               $"\"{issPath}\"";

                // 使用 GB2312 调用 Inno Setup
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

            process.OutputDataReceived += (s, e) => { if (e.Data != null) SendLog(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) SendLog("ERROR: " + e.Data, true); };

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
    }
}