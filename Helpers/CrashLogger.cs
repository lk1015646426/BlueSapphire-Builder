using System;
using System.IO;

namespace BlueSapphire.Builder.Helpers
{
    /// <summary>
    /// 全局崩溃日志：把未捕获异常写入 %APPDATA%\BlueSapphire.Builder\crash.log，
    /// 超过 2MB 自动轮转为 .old。所有 IO 错误被静默吞掉，避免崩溃日志本身再抛异常导致二次崩溃。
    /// </summary>
    public static class CrashLogger
    {
        private static readonly string CrashLogFolder = GetCrashLogFolder();
        private static readonly string CrashLogPath = Path.Combine(CrashLogFolder, "crash.log");
        private const long MaxCrashLogSize = 2 * 1024 * 1024; // 2MB

        public static string GetCrashLogPath() => CrashLogPath;

        private static string GetCrashLogFolder()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "BlueSapphire.Builder");
            try
            {
                Directory.CreateDirectory(folder);
                return folder;
            }
            catch
            {
                // 回退到 exe 同目录（便携模式）
                return AppContext.BaseDirectory;
            }
        }

        /// <summary>记录一条崩溃信息。永不抛异常。</summary>
        public static void LogCrash(Exception ex, string source)
        {
            try
            {
                string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex}\n\n";

                // 轮转：超过 MaxCrashLogSize 时旧文件改名 .old，新文件从头开始
                if (File.Exists(CrashLogPath))
                {
                    var fi = new FileInfo(CrashLogPath);
                    if (fi.Length > MaxCrashLogSize)
                    {
                        File.Move(CrashLogPath, CrashLogPath + ".old", overwrite: true);
                    }
                }

                File.AppendAllText(CrashLogPath, entry);
            }
            catch
            {
                // 崩溃日志本身失败时静默吞掉，绝不能再抛
            }
        }
    }
}
