using System.IO;
using Microsoft.Win32;

#pragma warning disable CA1416 // ✅ 极客操作：静音跨平台警告，声明此工具专为 Windows 打造

namespace BlueSapphire.Builder
{
    public static class PathHelper
    {
        // 自动寻找 Inno Setup 安装路径
        public static string? FindInnoSetup()
        {
            try
            {
                // 兼容系统级安装和用户级安装
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1")
                             ?? Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1");

                if (key != null)
                {
                    var installLocation = key.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(installLocation))
                    {
                        string exePath = Path.Combine(installLocation, "ISCC.exe");
                        if (File.Exists(exePath)) return exePath;
                    }
                }
            }
            catch { /* 忽略注册表权限或缺失异常，继续尝试默认路径兜底 */ }

            // 兜底方案：常见的默认安装路径
            string[] commonPaths = {
                @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
                @"C:\Program Files\Inno Setup 6\ISCC.exe",
                @"D:\Program Files (x86)\Inno Setup 6\ISCC.exe",
                @"D:\Program Files\Inno Setup 6\ISCC.exe"
            };

            foreach (var p in commonPaths)
            {
                if (File.Exists(p)) return p;
            }

            return null;
        }
    }
}