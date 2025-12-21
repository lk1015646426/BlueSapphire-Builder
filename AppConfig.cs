using System;

namespace BlueSapphire.Builder
{
    public class AppConfig
    {
        public string? AppName { get; set; }
        public string? Version { get; set; }
        public string? Publisher { get; set; }
        public string? AppID { get; set; }
        public string? ProjectPath { get; set; }
        public string? RawOutputDir { get; set; }
        public string? SetupOutputDir { get; set; }

        // Inno Setup 编译器路径
        public string? InnoSetupPath { get; set; }

        // 是否生成安装包
        public bool MakeInstaller { get; set; } = true;
    }
}