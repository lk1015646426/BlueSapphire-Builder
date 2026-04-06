using System.Text.Json.Serialization;

namespace BlueSapphire.Builder
{
    public class AppConfig
    {
        public string? AppName { get; set; }
        public string? Version { get; set; }
        public string? Publisher { get; set; }
        public string? AppID { get; set; }
        public string? ProjectPath { get; set; }
        public string? PublishOutputDir { get; set; }
        public string? SetupOutputDir { get; set; }
        public string? InnoSetupPath { get; set; }
        public string? IssScriptPath { get; set; }
        public bool MakeInstaller { get; set; } = true;

        [JsonPropertyName("RawOutputDir")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? LegacyRawOutputDir { get; set; }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(PublishOutputDir) && !string.IsNullOrWhiteSpace(LegacyRawOutputDir))
            {
                PublishOutputDir = LegacyRawOutputDir;
            }

            LegacyRawOutputDir = null;
        }
    }
}
