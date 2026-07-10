using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BlueSapphire.Builder.Helpers
{
    public sealed class StageProgressSample
    {
        public long ElapsedMilliseconds { get; set; }
        public long OutputBytes { get; set; }
        public int OutputFileCount { get; set; }
    }

    public sealed class StageProgressProfile
    {
        public string StageKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public long DurationMilliseconds { get; set; }
        public long FinalOutputBytes { get; set; }
        public int FinalOutputFileCount { get; set; }
        public List<StageProgressSample> Samples { get; set; } = new();
    }

    public sealed class BuildProgressProfile
    {
        public string BuildKey { get; set; } = string.Empty;
        public string TargetPlatform { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
        public long TotalDurationMilliseconds { get; set; }
        public List<StageProgressProfile> Stages { get; set; } = new();
    }

    public sealed class BuildProgressBaseline
    {
        public BuildProgressProfile Profile { get; init; } = new();
        public int SourceRunCount { get; init; }
    }

    internal sealed class BuildProgressHistoryDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public List<BuildProgressProfile> Profiles { get; set; } = new();
    }

    public sealed class BuildProgressHistoryStore
    {
        private const string HistoryFileName = "builder_progress_history_v1.json";
        private const int MaxProfilesPerBuildKey = 6;
        private const int MaxStoredProfiles = 120;
        private const int CurrentSchemaVersion = 1;
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly string _filePath;

        public BuildProgressHistoryStore(string? filePath = null)
        {
            // 优先 %APPDATA%\BlueSapphire.Builder，便携模式回退到工作目录
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "BlueSapphire.Builder");
            string defaultPath;
            try
            {
                Directory.CreateDirectory(appFolder);
                defaultPath = Path.Combine(appFolder, HistoryFileName);
            }
            catch
            {
                defaultPath = Path.GetFullPath(HistoryFileName);
            }

            _filePath = string.IsNullOrWhiteSpace(filePath) ? defaultPath : Path.GetFullPath(filePath);
        }

        public BuildProgressBaseline? Load(string buildKey, int recentRunCount = 3)
        {
            if (string.IsNullOrWhiteSpace(buildKey) || !File.Exists(_filePath))
            {
                return null;
            }

            try
            {
                string json = SafeFileWriter.ReadAllText(_filePath);
                BuildProgressHistoryDocument? document = JsonSerializer.Deserialize<BuildProgressHistoryDocument>(json);

                // Schema 版本校验：不兼容的版本返回 null，避免破坏旧数据
                if (document == null || document.SchemaVersion != CurrentSchemaVersion)
                {
                    return null;
                }

                List<BuildProgressProfile> recentProfiles = document.Profiles
                    .Where(profile => string.Equals(profile.BuildKey, buildKey, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(profile => profile.UpdatedAtUtc)
                    .Take(Math.Max(1, recentRunCount))
                    .ToList();

                if (recentProfiles.Count == 0)
                {
                    return null;
                }

                return new BuildProgressBaseline
                {
                    Profile = AggregateRecentProfiles(recentProfiles),
                    SourceRunCount = recentProfiles.Count
                };
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // 损坏文件：备份后返回 null，避免后续覆盖写丢失数据
                try
                {
                    if (File.Exists(_filePath))
                    {
                        File.Move(_filePath, _filePath + ".corrupt", overwrite: true);
                    }
                }
                catch { /* 备份失败不影响主流程 */ }
                return null;
            }
        }

        public void Save(BuildProgressProfile profile)
        {
            ArgumentNullException.ThrowIfNull(profile);

            BuildProgressHistoryDocument document = new() { SchemaVersion = CurrentSchemaVersion };
            if (File.Exists(_filePath))
            {
                try
                {
                    string existingJson = SafeFileWriter.ReadAllText(_filePath);
                    BuildProgressHistoryDocument? existing = JsonSerializer.Deserialize<BuildProgressHistoryDocument>(existingJson);
                    if (existing != null && existing.SchemaVersion == CurrentSchemaVersion)
                    {
                        document = existing;
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    // 旧文件损坏：从空文档重新开始
                    document = new BuildProgressHistoryDocument { SchemaVersion = CurrentSchemaVersion };
                }
            }

            document.Profiles.Add(profile);

            document.Profiles = document.Profiles
                .GroupBy(item => item.BuildKey, StringComparer.OrdinalIgnoreCase)
                .SelectMany(group => group
                    .OrderByDescending(item => item.UpdatedAtUtc)
                    .Take(MaxProfilesPerBuildKey))
                .OrderByDescending(item => item.UpdatedAtUtc)
                .Take(MaxStoredProfiles)
                .ToList();

            string outputJson = JsonSerializer.Serialize(document, SerializerOptions);
            // 原子写：写到 .tmp 再 File.Replace，崩溃不会留半截文件
            SafeFileWriter.WriteAllText(_filePath, outputJson);
        }

        public static string CreateBuildKey(AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            return string.Equals(config.TargetPlatform, "Android", StringComparison.OrdinalIgnoreCase)
                ? CreateAndroidKey(config)
                : CreateWindowsKey(config);
        }

        public static string DescribeBuild(AppConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (string.Equals(config.TargetPlatform, "Android", StringComparison.OrdinalIgnoreCase))
            {
                string root = NormalizePath(config.AndroidProjectRoot);
                string module = AndroidProjectHelper.NormalizeModuleName(config.AndroidModuleName);
                string buildType = AndroidProjectHelper.NormalizeBuildType(config.AndroidBuildType);
                return $"Android | {root} | {module} | {buildType} APK";
            }

            string project = NormalizePath(config.ProjectPath);
            return config.MakeInstaller
                ? $"Windows | {project} | Publish + Installer"
                : $"Windows | {project} | Publish Only";
        }

        private static BuildProgressProfile AggregateRecentProfiles(List<BuildProgressProfile> recentProfiles)
        {
            if (recentProfiles.Count == 1)
            {
                return recentProfiles[0];
            }

            double[] weights = BuildDescendingWeights(recentProfiles.Count);
            BuildProgressProfile newestProfile = recentProfiles[0];

            var aggregated = new BuildProgressProfile
            {
                BuildKey = newestProfile.BuildKey,
                TargetPlatform = newestProfile.TargetPlatform,
                Description = newestProfile.Description,
                UpdatedAtUtc = newestProfile.UpdatedAtUtc,
                TotalDurationMilliseconds = WeightedAverageLong(
                    recentProfiles.Select(profile => profile.TotalDurationMilliseconds).ToList(),
                    weights)
            };

            List<string> stageOrder = newestProfile.Stages
                .Select(stage => stage.StageKey)
                .Concat(recentProfiles.SelectMany(profile => profile.Stages.Select(stage => stage.StageKey)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (string stageKey in stageOrder)
            {
                var weightedStageRuns = recentProfiles
                    .Select((profile, index) => new
                    {
                        Stage = profile.Stages.FirstOrDefault(stage =>
                            string.Equals(stage.StageKey, stageKey, StringComparison.OrdinalIgnoreCase)),
                        Weight = weights[index]
                    })
                    .Where(item => item.Stage != null)
                    .ToList();

                if (weightedStageRuns.Count == 0)
                {
                    continue;
                }

                aggregated.Stages.Add(AggregateStageProfiles(
                    stageKey,
                    weightedStageRuns.Select(item => item.Stage!).ToList(),
                    weightedStageRuns.Select(item => item.Weight).ToArray()));
            }

            return aggregated;
        }

        private static StageProgressProfile AggregateStageProfiles(
            string stageKey,
            List<StageProgressProfile> stageRuns,
            double[] weights)
        {
            StageProgressProfile newestStage = stageRuns[0];
            long averagedDuration = WeightedAverageLong(
                stageRuns.Select(stage => stage.DurationMilliseconds).ToList(),
                weights);
            long averagedOutputBytes = WeightedAverageLong(
                stageRuns.Select(stage => stage.FinalOutputBytes).ToList(),
                weights);
            int averagedOutputFileCount = WeightedAverageInt(
                stageRuns.Select(stage => stage.FinalOutputFileCount).ToList(),
                weights);

            int samplePointCount = Math.Clamp(stageRuns.Max(stage => Math.Max(stage.Samples.Count, 1)), 6, 16);
            var aggregatedSamples = new List<StageProgressSample>(samplePointCount);

            for (int index = 0; index < samplePointCount; index++)
            {
                double ratio = samplePointCount == 1 ? 1.0 : (double)index / (samplePointCount - 1);
                List<StageProgressSample> resampled = stageRuns
                    .Select(stage => InterpolateSample(stage, ratio))
                    .ToList();

                aggregatedSamples.Add(new StageProgressSample
                {
                    ElapsedMilliseconds = index == samplePointCount - 1
                        ? averagedDuration
                        : (long)Math.Round(averagedDuration * ratio),
                    OutputBytes = index == samplePointCount - 1
                        ? averagedOutputBytes
                        : WeightedAverageLong(resampled.Select(sample => sample.OutputBytes).ToList(), weights),
                    OutputFileCount = index == samplePointCount - 1
                        ? averagedOutputFileCount
                        : WeightedAverageInt(resampled.Select(sample => sample.OutputFileCount).ToList(), weights)
                });
            }

            return new StageProgressProfile
            {
                StageKey = stageKey,
                DisplayName = newestStage.DisplayName,
                DurationMilliseconds = averagedDuration,
                FinalOutputBytes = averagedOutputBytes,
                FinalOutputFileCount = averagedOutputFileCount,
                Samples = aggregatedSamples
            };
        }

        private static StageProgressSample InterpolateSample(StageProgressProfile stage, double ratio)
        {
            long duration = Math.Max(stage.DurationMilliseconds, 1);
            long targetElapsed = (long)Math.Round(duration * Math.Clamp(ratio, 0.0, 1.0));
            List<StageProgressSample> orderedSamples = stage.Samples
                .OrderBy(sample => sample.ElapsedMilliseconds)
                .ToList();

            if (orderedSamples.Count == 0)
            {
                return new StageProgressSample
                {
                    ElapsedMilliseconds = targetElapsed,
                    OutputBytes = (long)Math.Round(stage.FinalOutputBytes * ratio),
                    OutputFileCount = (int)Math.Round(stage.FinalOutputFileCount * ratio)
                };
            }

            if (orderedSamples[0].ElapsedMilliseconds > 0)
            {
                orderedSamples.Insert(0, new StageProgressSample
                {
                    ElapsedMilliseconds = 0,
                    OutputBytes = 0,
                    OutputFileCount = 0
                });
            }

            StageProgressSample lastSample = orderedSamples[^1];
            if (lastSample.ElapsedMilliseconds < duration)
            {
                orderedSamples.Add(new StageProgressSample
                {
                    ElapsedMilliseconds = duration,
                    OutputBytes = stage.FinalOutputBytes,
                    OutputFileCount = stage.FinalOutputFileCount
                });
            }

            if (targetElapsed <= orderedSamples[0].ElapsedMilliseconds)
            {
                return CloneSampleAtElapsed(orderedSamples[0], targetElapsed);
            }

            for (int index = 1; index < orderedSamples.Count; index++)
            {
                StageProgressSample previous = orderedSamples[index - 1];
                StageProgressSample next = orderedSamples[index];
                if (targetElapsed > next.ElapsedMilliseconds)
                {
                    continue;
                }

                long span = Math.Max(1, next.ElapsedMilliseconds - previous.ElapsedMilliseconds);
                double t = (double)(targetElapsed - previous.ElapsedMilliseconds) / span;
                return new StageProgressSample
                {
                    ElapsedMilliseconds = targetElapsed,
                    OutputBytes = (long)Math.Round(previous.OutputBytes + ((next.OutputBytes - previous.OutputBytes) * t)),
                    OutputFileCount = (int)Math.Round(previous.OutputFileCount + ((next.OutputFileCount - previous.OutputFileCount) * t))
                };
            }

            return CloneSampleAtElapsed(orderedSamples[^1], targetElapsed);
        }

        private static StageProgressSample CloneSampleAtElapsed(StageProgressSample sample, long elapsedMilliseconds)
        {
            return new StageProgressSample
            {
                ElapsedMilliseconds = elapsedMilliseconds,
                OutputBytes = sample.OutputBytes,
                OutputFileCount = sample.OutputFileCount
            };
        }

        private static double[] BuildDescendingWeights(int count)
        {
            return Enumerable.Range(0, count)
                .Select(index => (double)(count - index))
                .ToArray();
        }

        private static long WeightedAverageLong(IReadOnlyList<long> values, IReadOnlyList<double> weights)
        {
            if (values.Count == 0)
            {
                return 0;
            }

            double weightedSum = 0;
            double totalWeight = 0;
            for (int index = 0; index < values.Count; index++)
            {
                double weight = index < weights.Count ? weights[index] : 1;
                weightedSum += values[index] * weight;
                totalWeight += weight;
            }

            return totalWeight <= 0 ? values[0] : (long)Math.Round(weightedSum / totalWeight);
        }

        private static int WeightedAverageInt(IReadOnlyList<int> values, IReadOnlyList<double> weights)
        {
            if (values.Count == 0)
            {
                return 0;
            }

            double weightedSum = 0;
            double totalWeight = 0;
            for (int index = 0; index < values.Count; index++)
            {
                double weight = index < weights.Count ? weights[index] : 1;
                weightedSum += values[index] * weight;
                totalWeight += weight;
            }

            return totalWeight <= 0 ? values[0] : (int)Math.Round(weightedSum / totalWeight);
        }

        private static string CreateWindowsKey(AppConfig config)
        {
            string project = NormalizePath(config.ProjectPath);
            string mode = config.MakeInstaller ? "publish-installer" : "publish-only";
            return $"windows|{project}|{mode}";
        }

        private static string CreateAndroidKey(AppConfig config)
        {
            string root = NormalizePath(config.AndroidProjectRoot);
            string module = AndroidProjectHelper.NormalizeModuleName(config.AndroidModuleName);
            string buildType = AndroidProjectHelper.NormalizeBuildType(config.AndroidBuildType).ToLowerInvariant();
            return $"android|{root}|{module}|{buildType}";
        }

        private static string NormalizePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "<empty>";
            }

            try
            {
                return Path.GetFullPath(path)
                    .Trim()
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToLowerInvariant();
            }
            catch
            {
                return path.Trim().ToLowerInvariant();
            }
        }
    }
}
