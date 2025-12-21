using System.IO;

namespace BlueSapphire.Builder
{
    public static class PathHelper
    {
        // 自动寻找 Inno Setup 安装路径
        public static string? FindInnoSetup()
        {
            // 1. 尝试常见默认路径
            string[] commonPaths = {
                @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
                @"C:\Program Files\Inno Setup 6\ISCC.exe",
                @"D:\Program Files (x86)\Inno Setup 6\ISCC.exe",
                @"D:\Program Files\Inno Setup 6\ISCC.exe" // 补充一种常见情况
            };

            foreach (var p in commonPaths)
            {
                if (File.Exists(p)) return p;
            }

            return null;
        }
    }
}