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
            if (!File.Exists(config.ProjectPath)) throw new FileNotFoundException("找不到项目文件 (.csproj)");
            if (string.IsNullOrWhiteSpace(config.RawOutputDir)) throw new ArgumentException("未设置原始输出目录");

            SendLog(">>> [0/2] 正在同步跃迁记录数据 (DevMatrixLog.json)...", false);
            ReportProgress(5);
            // ...(中间的复制开发日志的 try-catch 逻辑保持不变)...

            SendLog(">>> [1/2] 正在编译 .NET 核心...", false);

            // 清理旧目录逻辑保持不变...

            var publishArgs = $"publish \"{config.ProjectPath}\" -c Release -r win-x64 --self-contained true -o \"{config.RawOutputDir}\" /p:Version={config.Version} /p:Platform=x64";

            // ✅ 极客优化：将 dotnet 编译进度映射到 5% ~ 50%，强制使用 GBK 编码读取
            await RunCommandAsync("dotnet", publishArgs, System.Text.Encoding.UTF8, 5.0, 50.0);

            SendLog(">>> 编译成功！原始文件已生成。", false);

            if (config.MakeInstaller)
            {
                // ... (中间的 Inno Setup 路径检查逻辑保持不变) ...

                // ✅ 消除警告1：使用 ?? "" 确保 issPath 绝对不会是 null
                string issPath = config.IssScriptPath ?? "";

                // 如果界面上没选 iss 文件（留空了），就自动拼接项目根目录下的 installer.iss
                if (string.IsNullOrWhiteSpace(issPath) && !string.IsNullOrWhiteSpace(config.ProjectPath))
                {
                    // Path.GetDirectoryName 可能返回 null，加上判空保护
                    string? projDir = System.IO.Path.GetDirectoryName(config.ProjectPath);
                    if (projDir != null) // ✅ 消除警告2：确保 projDir 不为 null 后再组合路径
                    {
                        issPath = System.IO.Path.Combine(projDir, "installer.iss");
                    }
                }

                var isccArgs = $"/dSourcePath=\"{config.RawOutputDir}\" " +
                               $"/dMyAppName=\"{config.AppName}\" " +
                               $"/dMyAppVersion=\"{config.Version}\" " +
                               $"/dMyAppPublisher=\"{config.Publisher}\" " +
                               $"/dMyAppId=\"{config.AppID}\" " +
                               $"/O\"{config.SetupOutputDir}\" " +
                               $"/F\"{config.AppName}_Setup_v{config.Version}\" " +
                               $"\"{issPath}\"";

                // ✅ 消除警告3：提前拦截 null 值，防止传入 RunCommandAsync
                if (string.IsNullOrWhiteSpace(config.InnoSetupPath))
                {
                    throw new Exception("Inno Setup 路径未配置，无法生成安装包！请在界面中指定 ISCC.exe 的位置。");
                }

                // ✅ 极客优化：使用 ! 操作符告诉编译器，这里 InnoSetupPath 绝对不可能为 null 了
                await RunCommandAsync(config.InnoSetupPath!, isccArgs, System.Text.Encoding.UTF8, 50.0, 99.0);

                SendLog(">>> 安装包制作完成！", false);
            }
            {
                // ... (中间的 Inno Setup 路径检查逻辑保持不变) ...

                // ✅ 修复：在这里补上 issPath 的定义和获取逻辑
                string issPath = config.IssScriptPath;

                // 如果界面上没选 iss 文件（留空了），就自动拼接项目根目录下的 installer.iss
                if (string.IsNullOrWhiteSpace(issPath) && !string.IsNullOrWhiteSpace(config.ProjectPath))
                {
                    string projDir = System.IO.Path.GetDirectoryName(config.ProjectPath);
                    issPath = System.IO.Path.Combine(projDir, "installer.iss");
                }

                var isccArgs = $"/dSourcePath=\"{config.RawOutputDir}\" " +
                               $"/dMyAppName=\"{config.AppName}\" " +
                               $"/dMyAppVersion=\"{config.Version}\" " +
                               $"/dMyAppPublisher=\"{config.Publisher}\" " +
                               $"/dMyAppId=\"{config.AppID}\" " +
                               $"/O\"{config.SetupOutputDir}\" " +
                               $"/F\"{config.AppName}_Setup_v{config.Version}\" " +
                               $"\"{issPath}\"";

                // ✅ 极客优化：将 Inno Setup 打包进度映射到 50% ~ 99%
                await RunCommandAsync(config.InnoSetupPath, isccArgs, System.Text.Encoding.UTF8, 50.0, 99.0);

                SendLog(">>> 安装包制作完成！", false);
            }

            ReportProgress(100);
        }

        // ✅ 核心重构：支持平滑进度计算的底层命令执行器
        private async Task RunCommandAsync(string fileName, string arguments, System.Text.Encoding encoding, double startProgress, double endProgress)
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

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    // 1. 发送日志到前端
                    LogReceived?.Invoke(this, new LogEventArgs(e.Data, false));

                    // 2. 核心魔法：芝诺的乌龟（永远达不到终点的平滑算法）
                    // 每次输出一行日志，进度条就前进【剩余空间的 3%】
                    // 这样一开始跑得快，越往后越慢，但【绝对不会卡死】，也【绝对不会倒退】
                    double remaining = endProgress - currentProgress;
                    currentProgress += remaining * 0.03;

                    ProgressChanged?.Invoke(this, currentProgress);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    LogReceived?.Invoke(this, new LogEventArgs(e.Data, true));
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

            // 命令真正执行完毕时，才把进度实打实地推到当前阶段的终点 (比如 50% 或 99%)
            ProgressChanged?.Invoke(this, endProgress);
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