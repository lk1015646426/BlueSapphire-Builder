using System;
using System.Collections.Generic;
using System.IO;

namespace BlueSapphire.Builder.Helpers
{
    /// <summary>
    /// AppConfig 业务校验：在构建前一次性校验所有字段合法性，给出友好错误提示。
    /// </summary>
    public static class AppConfigValidator
    {
        /// <summary>
        /// 校验整个构建配置，返回错误列表。空列表表示配置合法。
        /// </summary>
        public static List<string> Validate(AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            var errors = new List<string>();

            bool isAndroid = string.Equals(config.TargetPlatform, "Android", StringComparison.OrdinalIgnoreCase);

            if (isAndroid)
            {
                ValidateAndroid(config, errors);
            }
            else
            {
                ValidateWindows(config, errors);
            }

            return errors;
        }

        /// <summary>
        /// 校验并抛出聚合异常，便于 UI 层统一显示。
        /// </summary>
        public static void ValidateAndThrow(AppConfig config)
        {
            var errors = Validate(config);
            if (errors.Count > 0)
            {
                throw new ArgumentException("配置校验失败：\n  - " + string.Join("\n  - ", errors));
            }
        }

        private static void ValidateWindows(AppConfig config, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(config.ProjectPath))
            {
                errors.Add("未选择项目文件 (.csproj 或 package.json)");
                return;
            }

            if (!File.Exists(config.ProjectPath))
            {
                errors.Add($"项目文件不存在：{config.ProjectPath}");
            }

            bool isCsproj = string.Equals(Path.GetExtension(config.ProjectPath), ".csproj", StringComparison.OrdinalIgnoreCase);

            if (isCsproj)
            {
                if (string.IsNullOrWhiteSpace(config.RawOutputDir))
                {
                    errors.Add("未设置编译产物输出目录 (RawOutputDir)");
                }

                if (config.MakeInstaller)
                {
                    if (string.IsNullOrWhiteSpace(config.SetupOutputDir))
                    {
                        errors.Add("未设置安装包输出目录 (SetupOutputDir)");
                    }

                    if (!string.IsNullOrWhiteSpace(config.SetupOutputDir) && !string.IsNullOrWhiteSpace(config.RawOutputDir)
                        && string.Equals(Normalize(config.SetupOutputDir), Normalize(config.RawOutputDir), StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add("安装包输出目录不能与编译产物目录相同");
                    }
                }
            }
            else
            {
                // Tauri / Node 自定义构建
                if (string.IsNullOrWhiteSpace(config.SetupOutputDir))
                {
                    errors.Add("未设置安装包归集目录 (SetupOutputDir)");
                }
            }

            // 通用版本号校验
            if (!string.IsNullOrWhiteSpace(config.Version))
            {
                try
                {
                    ArgumentSanitizer.ValidateDotNetVersion(config.Version);
                }
                catch (ArgumentException ex)
                {
                    errors.Add(ex.Message);
                }
            }
        }

        private static void ValidateAndroid(AppConfig config, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(config.AndroidProjectRoot))
            {
                errors.Add("未设置 Android 项目根目录");
                return;
            }

            if (!Directory.Exists(config.AndroidProjectRoot))
            {
                errors.Add($"Android 项目根目录不存在：{config.AndroidProjectRoot}");
            }
        }

        private static string Normalize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToLowerInvariant();
            }
            catch
            {
                return (path ?? string.Empty).ToLowerInvariant();
            }
        }
    }
}
