using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BlueSapphire.Builder.Helpers
{
    /// <summary>
    /// 原子文件写入工具：先写到临时文件，再原子重命名，避免进程崩溃留下半截 JSON。
    /// 多实例并发时用 cross-process 互斥锁串行化。
    /// </summary>
    public static class SafeFileWriter
    {
        /// <summary>
        /// 原子写入文本文件：写到 .tmp → File.Replace 覆盖目标文件。
        /// </summary>
        public static void WriteAllText(string path, string content)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = path + ".tmp";
            string backupPath = path + ".bak";

            // 使用 Mutex 防止多实例并发写同一文件
            string mutexName = "Global\\BlueSapphire_" + Math.Abs(path.ToLowerInvariant().GetHashCode()).ToString(CultureInfo.InvariantCulture);
            using var mutex = new Mutex(false, mutexName);

            try
            {
                if (!mutex.WaitOne(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException($"获取文件锁超时，另一实例可能正在写：{path}");
                }

                // 写到临时文件
                File.WriteAllText(tempPath, content, new UTF8Encoding(false));

                // 备份原文件（若存在）
                if (File.Exists(path))
                {
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                    File.Replace(tempPath, path, backupPath);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            finally
            {
                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch { /* 已释放或未获取 */ }
                }
            }
        }

        /// <summary>
        /// 原子写入 JSON 序列化结果。
        /// </summary>
        public static void WriteJson<T>(string path, T value, JsonSerializerOptions? options = null)
        {
            string json = JsonSerializer.Serialize(value, options);
            WriteAllText(path, json);
        }

        /// <summary>
        /// 读取文本文件，若文件损坏抛 IOException 由调用方决定降级策略。
        /// </summary>
        public static string ReadAllText(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"文件不存在：{path}", path);
            }

            string content = File.ReadAllText(path, new UTF8Encoding(false));

            // 简单合法性检查：非空 JSON 应该以 { 或 [ 开头
            if (!string.IsNullOrWhiteSpace(content))
            {
                string trimmed = content.TrimStart();
                if (trimmed[0] != '{' && trimmed[0] != '[')
                {
                    throw new IOException($"文件内容不是合法 JSON，可能损坏：{path}");
                }
            }

            return content;
        }
    }
}
