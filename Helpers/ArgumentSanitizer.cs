using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace BlueSapphire.Builder.Helpers
{
    /// <summary>
    /// 命令行参数转义与校验工具：用于 dotnet publish / ISCC 等外部进程的参数拼装。
    /// 防止用户输入的版本号、应用名等字段包含特殊字符导致命令注入或编译失败。
    /// </summary>
    public static class ArgumentSanitizer
    {
        /// <summary>
        /// 转义 ISCC 的 /d 参数值。Inno Setup 预处理器的 define 值若包含空格或特殊字符，
        /// 需用双引号包裹，且内部双引号需用 \" 转义。
        /// </summary>
        public static string EscapeInnoDefine(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string escaped = value.Replace("\"", "\\\"");
            return $"\"{escaped}\"";
        }

        /// <summary>
        /// 校验并返回 dotnet publish 可接受的 Version 字符串。
        /// 允许 SemVer 形式（1.0.0 / 1.0.0-beta / 1.0.0+build.123），拒绝非法字符。
        /// </summary>
        /// <exception cref="ArgumentException">版本号非法时抛出。</exception>
        public static string ValidateDotNetVersion(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException("版本号不能为空");
            }

            string trimmed = version.Trim();

            // NuGet/SemVer 合法字符集：字母、数字、点、加号、减号
            // 拒绝 ; | & $ ` ' " < > ( ) 等可能影响 shell 的字符
            foreach (char c in trimmed)
            {
                if (!IsValidVersionChar(c))
                {
                    throw new ArgumentException(
                        $"版本号包含非法字符 '{c}'：{trimmed}。" +
                        "版本号仅允许字母、数字、点、加号、减号（如 1.0.0 / 1.0.0-beta / 1.0.0+build.1）。");
                }
            }

            return trimmed;
        }

        /// <summary>
        /// 校验 Inno Setup 的 AppID 是否为合法的 {{GUID}} 形式。
        /// 合法形式如 {{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
        /// </summary>
        public static string ValidateInnoAppId(string? appId)
        {
            if (string.IsNullOrWhiteSpace(appId))
            {
                throw new ArgumentException("AppID 不能为空");
            }

            string trimmed = appId.Trim();

            // 兼容用户直接填 GUID 或带 {{}} 包裹的形式
            string guidPart = trimmed.StartsWith("{{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal)
                ? trimmed.Substring(2, trimmed.Length - 3)
                : trimmed.Trim('{', '}');

            if (!Guid.TryParse(guidPart, out _))
            {
                throw new ArgumentException(
                    $"AppID 不是合法的 GUID 格式：{trimmed}。请点击「生成标识」按钮生成新的 GUID。");
            }

            return $"{{{{{guidPart}}}";
        }

        /// <summary>
        /// 校验路径不含命令注入字符。返回标准化后的完整路径。
        /// 拒绝包含 |、回车、换行等字符的路径。
        /// </summary>
        public static string ValidatePath(string? path, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException($"{fieldName}不能为空");
            }

            string trimmed = path.Trim();

            // 路径中不应出现的字符（Windows 文件系统限制 + 命令注入风险）
            char[] forbidden = { '|', '\r', '\n', '\t' };
            foreach (char c in forbidden)
            {
                if (trimmed.IndexOf(c) >= 0)
                {
                    throw new ArgumentException($"{fieldName}包含非法字符 '{c}'：{trimmed}");
                }
            }

            try
            {
                return Path.GetFullPath(trimmed);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ArgumentException($"{fieldName}不是合法路径：{trimmed}。{ex.Message}");
            }
        }

        private static bool IsValidVersionChar(char c)
        {
            // SemVer prerelease/build metadata 允许 [0-9A-Za-z-.+]
            return (c >= '0' && c <= '9')
                || (c >= 'A' && c <= 'Z')
                || (c >= 'a' && c <= 'z')
                || c == '.' || c == '-' || c == '+';
        }
    }
}
