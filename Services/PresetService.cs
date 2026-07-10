using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlueSapphire.Builder.Helpers;

namespace BlueSapphire.Builder.Services
{
    /// <summary>
    /// 打包预设：一份命名的完整 AppConfig 快照，方便多项目一键切换。
    /// </summary>
    public sealed class BuildPreset
    {
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public AppConfig Config { get; set; } = new();
    }

    /// <summary>
    /// 预设的增删改查 + 本地 JSON 持久化。
    /// 文件：builder_presets.json，存于 %APPDATA%\BlueSapphire.Builder。
    /// </summary>
    public sealed class PresetService
    {
        private const string PresetFileName = "builder_presets.json";
        private const int CurrentSchemaVersion = 1;
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly string _filePath;
        private List<BuildPreset> _presets = new();
        private readonly object _lock = new();

        public event EventHandler? PresetsChanged;

        public PresetService(string? filePath = null)
        {
            // 优先 %APPDATA%\BlueSapphire.Builder
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "BlueSapphire.Builder");
            string defaultPath;
            try
            {
                Directory.CreateDirectory(appFolder);
                defaultPath = Path.Combine(appFolder, PresetFileName);
            }
            catch
            {
                defaultPath = Path.GetFullPath(PresetFileName);
            }

            _filePath = string.IsNullOrWhiteSpace(filePath) ? defaultPath : Path.GetFullPath(filePath);
            Load();
        }

        public IReadOnlyList<BuildPreset> Presets
        {
            get
            {
                lock (_lock)
                {
                    return _presets.ToList();
                }
            }
        }

        public BuildPreset? Find(string name)
        {
            lock (_lock)
            {
                return _presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>保存或更新一条预设。同名则覆盖。</summary>
        public void Save(string name, AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("预设名称不能为空");
            }

            lock (_lock)
            {
                var existing = _presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Config = CloneConfig(config);
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                }
                else
                {
                    _presets.Add(new BuildPreset
                    {
                        Name = name,
                        Config = CloneConfig(config),
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    });
                }
            }

            Persist();
        }

        public bool Delete(string name)
        {
            bool removed;
            lock (_lock)
            {
                var target = _presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                {
                    return false;
                }
                removed = _presets.Remove(target);
            }

            if (removed)
            {
                Persist();
            }
            return removed;
        }

        public void Rename(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                throw new ArgumentException("新名称不能为空");
            }

            lock (_lock)
            {
                if (_presets.Any(p => string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"已存在同名预设：{newName}");
                }
                var target = _presets.FirstOrDefault(p => string.Equals(p.Name, oldName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"找不到预设：{oldName}");
                target.Name = newName;
                target.UpdatedAtUtc = DateTime.UtcNow;
            }

            Persist();
        }

        private static AppConfig CloneConfig(AppConfig config)
        {
            // 通过 JSON 往返做深拷贝，避免引用共享导致后续修改污染预设
            string json = JsonSerializer.Serialize(config, SerializerOptions);
            return JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? new AppConfig();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return;
                }
                string json = SafeFileWriter.ReadAllText(_filePath);
                var doc = JsonSerializer.Deserialize<List<BuildPreset>>(json, SerializerOptions);
                if (doc != null)
                {
                    lock (_lock)
                    {
                        _presets = doc;
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // 损坏的预设文件：备份后重置，不阻断启动
                try
                {
                    if (File.Exists(_filePath))
                    {
                        File.Move(_filePath, _filePath + ".corrupt", overwrite: true);
                    }
                }
                catch { /* 备份失败不影响主流程 */ }
                lock (_lock)
                {
                    _presets = new List<BuildPreset>();
                }
            }
        }

        private void Persist()
        {
            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_presets, SerializerOptions);
            }
            try
            {
                SafeFileWriter.WriteAllText(_filePath, json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
            {
                // 持久化失败不抛，避免阻断构建流程；内存中预设仍可用
                // 调用方可通过 PresetsChanged 事件感知
            }
            PresetsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}