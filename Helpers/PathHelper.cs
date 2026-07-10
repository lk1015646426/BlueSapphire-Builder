using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
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
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // 注册表权限或访问失败：继续尝试默认路径兜底，不吞其他异常
            }

            // 兜底方案：扫描常见盘符的 Program Files 目录，适配装在 C/D/E/F 盘的用户
            string[] driveLetters = { "C", "D", "E", "F" };
            string[] subPaths =
            {
                Path.Combine("Program Files (x86)", "Inno Setup 6", "ISCC.exe"),
                Path.Combine("Program Files", "Inno Setup 6", "ISCC.exe")
            };

            foreach (string drive in driveLetters)
            {
                foreach (string sub in subPaths)
                {
                    string candidate = Path.Combine(drive + ":\\", sub);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        // ──────────────────────────────────────────────
        //  项目级文件自动探测
        // ──────────────────────────────────────────────

        /// <summary>
        /// 在项目目录及常见子目录中查找 .iss 安装脚本。
        /// 策略：先扫常见目录名，再全目录递归（限 2 层深度），取第一个匹配。
        /// </summary>
        public static string? FindIssScript(string projectDir)
        {
            if (!Directory.Exists(projectDir)) return null;

            // 优先搜索常见安装脚本目录
            string[] preferDirs = { "installer", "setup", "inno", "iss", "scripts", "build" };
            foreach (string sub in preferDirs)
            {
                string dir = Path.Combine(projectDir, sub);
                if (!Directory.Exists(dir)) continue;
                var hit = Directory.GetFiles(dir, "*.iss", SearchOption.TopDirectoryOnly);
                if (hit.Length > 0) return hit[0];
            }

            // 递归扫描项目目录（限 2 层深度，避免大仓库卡顿）
            var results = SafeEnumerateFiles(projectDir, "*.iss", maxDepth: 2);
            return results.FirstOrDefault();
        }

        /// <summary>
        /// 查找 .ico 图标文件。
        /// 策略：先尝试从 .csproj 解析 ApplicationIcon，再扫常见资源目录，最后递归。
        /// </summary>
        public static string? FindIcon(string projectDir, string? csprojPath = null)
        {
            // 1. 从 .csproj 的 ApplicationIcon 属性解析
            if (!string.IsNullOrEmpty(csprojPath) && File.Exists(csprojPath))
            {
                string? fromCsproj = ParseCsprojApplicationIcon(csprojPath, projectDir);
                if (!string.IsNullOrEmpty(fromCsproj) && File.Exists(fromCsproj))
                    return fromCsproj;
            }

            if (!Directory.Exists(projectDir)) return null;

            // 2. 优先搜索常见资源目录
            string[] preferDirs = { "assets", "resources", "icons", "res", "images", "wwwroot", "static" };
            foreach (string sub in preferDirs)
            {
                string dir = Path.Combine(projectDir, sub);
                if (!Directory.Exists(dir)) continue;
                var hit = Directory.GetFiles(dir, "*.ico", SearchOption.TopDirectoryOnly);
                if (hit.Length > 0) return hit[0];
            }

            // 3. 项目根目录
            var rootHit = Directory.GetFiles(projectDir, "*.ico", SearchOption.TopDirectoryOnly);
            if (rootHit.Length > 0) return rootHit[0];

            // 4. 递归扫描（限 2 层）
            var results = SafeEnumerateFiles(projectDir, "*.ico", maxDepth: 2);
            return results.FirstOrDefault();
        }

        /// <summary>
        /// 为 Android 项目自动探测 .ico 图标文件。
        /// 策略：优先搜索 app/src/main/res 和常见 mipmap 目录，再扫项目根及常见资源目录。
        /// </summary>
        public static string? FindAndroidIcon(string projectRoot)
        {
            if (!Directory.Exists(projectRoot)) return null;

            // 1. Android 标准资源目录：app/src/main/res/mipmap-*
            string resDir = Path.Combine(projectRoot, "app", "src", "main", "res");
            if (Directory.Exists(resDir))
            {
                // mipmap 目录优先（Android 推荐的启动图标位置）
                foreach (string dir in Directory.GetDirectories(resDir, "mipmap*", SearchOption.TopDirectoryOnly))
                {
                    var hit = Directory.GetFiles(dir, "*.ico", SearchOption.TopDirectoryOnly);
                    if (hit.Length > 0) return hit[0];
                }
                // drawable 目录兜底
                foreach (string dir in Directory.GetDirectories(resDir, "drawable*", SearchOption.TopDirectoryOnly))
                {
                    var hit = Directory.GetFiles(dir, "*.ico", SearchOption.TopDirectoryOnly);
                    if (hit.Length > 0) return hit[0];
                }
            }

            // 2. 项目根目录及常见资源目录
            string[] preferDirs = { "assets", "resources", "icons", "res", "images", "static", "app" };
            foreach (string sub in preferDirs)
            {
                string dir = Path.Combine(projectRoot, sub);
                if (!Directory.Exists(dir)) continue;
                var hit = Directory.GetFiles(dir, "*.ico", SearchOption.TopDirectoryOnly);
                if (hit.Length > 0) return hit[0];
            }

            // 3. 项目根目录
            var rootHit = Directory.GetFiles(projectRoot, "*.ico", SearchOption.TopDirectoryOnly);
            if (rootHit.Length > 0) return rootHit[0];

            // 4. 递归扫描（限 2 层，覆盖 app/icons 等非标准位置）
            var results = SafeEnumerateFiles(projectRoot, "*.ico", maxDepth: 2);
            return results.FirstOrDefault();
        }

        /// <summary>
        /// 为 Tauri / Node 项目自动探测构建命令。
        /// 策略：Tauri → cargo tauri build；package.json 有 build 脚本 → npm run build；
        /// 根目录有 build.bat/build.cmd → 直接使用。
        /// </summary>
        public static string? FindCustomBuildCommand(string projectDir)
        {
            if (!Directory.Exists(projectDir)) return null;

            // Tauri 项目：检测 src-tauri/tauri.conf.json
            string tauriConf = Path.Combine(projectDir, "src-tauri", "tauri.conf.json");
            if (File.Exists(tauriConf))
                return "cargo tauri build";

            // Node 项目：检测 package.json
            string pkgJson = Path.Combine(projectDir, "package.json");
            if (File.Exists(pkgJson))
            {
                try
                {
                    string json = File.ReadAllText(pkgJson);
                    // 简单检测是否有 build 脚本（不做完整 JSON 解析，避免依赖）
                    if (json.Contains("\"build\"", StringComparison.OrdinalIgnoreCase))
                        return "npm run build";
                }
                catch { /* 读取失败则跳过 */ }
            }

            // 根目录的构建脚本
            string[] buildScripts = { "build.bat", "build.cmd", "build.ps1", "make.bat" };
            foreach (string script in buildScripts)
            {
                string path = Path.Combine(projectDir, script);
                if (File.Exists(path)) return path;
            }

            return null;
        }

        /// <summary>
        /// 为 Tauri / Node 项目自动探测工作目录（通常是项目根目录）。
        /// </summary>
        public static string? FindWorkingDir(string projectDir)
        {
            if (!Directory.Exists(projectDir)) return null;

            // Tauri 项目优先用 src-tauri 目录
            string tauriDir = Path.Combine(projectDir, "src-tauri");
            if (Directory.Exists(tauriDir)) return projectDir;

            return projectDir;
        }

        // ──────────────────────────────────────────────
        //  内部工具
        // ──────────────────────────────────────────────

        /// <summary>
        /// 安全递归枚举文件，限制深度防止大仓库性能问题。
        /// </summary>
        private static IEnumerable<string> SafeEnumerateFiles(string root, string pattern, int maxDepth)
        {
            var stack = new Stack<(string dir, int depth)>();
            stack.Push((root, 0));

            while (stack.Count > 0)
            {
                var (dir, depth) = stack.Pop();
                IEnumerable<string> files;
                IEnumerable<string> subDirs;

                try
                {
                    files = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
                    subDirs = Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly);
                }
                catch (UnauthorizedAccessException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                catch (IOException) { continue; }

                foreach (string f in files)
                    yield return f;

                if (depth < maxDepth)
                {
                    foreach (string sd in subDirs)
                        stack.Push((sd, depth + 1));
                }
            }
        }

        /// <summary>
        /// 从 .csproj XML 中解析 ApplicationIcon 属性。
        /// </summary>
        private static string? ParseCsprojApplicationIcon(string csprojPath, string projectDir)
        {
            try
            {
                var doc = XDocument.Load(csprojPath);
                // 查找 <ApplicationIcon>xxx.ico</ApplicationIcon>
                var elem = doc.Descendants().FirstOrDefault(e =>
                    e.Name.LocalName == "ApplicationIcon");
                if (elem != null && !string.IsNullOrWhiteSpace(elem.Value))
                {
                    string iconRel = elem.Value.Trim();
                    // 相对路径 → 绝对路径
                    string iconAbs = Path.IsPathRooted(iconRel)
                        ? iconRel
                        : Path.Combine(projectDir, iconRel);
                    return iconAbs;
                }
            }
            catch
            {
                // XML 解析失败：静默跳过，交给后续文件扫描兜底
            }
            return null;
        }
    }
}