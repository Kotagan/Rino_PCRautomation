using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MFAWPF.Helper;
using MFAWPF.Models;
using Newtonsoft.Json;

namespace MFAWPF.Services;

/// <summary>
/// Service for persisting and loading task sequence presets.
/// </summary>
public class TaskPresetStorageService
{
    private static readonly object FileLock = new();

    private static string PresetsFilePath =>
        Path.Combine(AppContext.BaseDirectory, "config", "task_presets.json");

    /// <summary>
    /// Loads all saved task presets from storage.
    /// </summary>
    public List<TaskPreset> LoadPresets()
    {
        lock (FileLock)
        {
            try
            {
                var filePath = PresetsFilePath;
                if (!File.Exists(filePath))
                {
                    return new List<TaskPreset>();
                }

                var json = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<TaskPreset>();
                }

                var presets = JsonConvert.DeserializeObject<List<TaskPreset>>(json);
                return presets ?? new List<TaskPreset>();
            }
            catch (Exception ex)
            {
                LoggerService.LogError(ex);
                return new List<TaskPreset>();
            }
        }
    }

    /// <summary>
    /// Saves all presets to storage.
    /// </summary>
    public void SavePresets(IEnumerable<TaskPreset> presets)
    {
        lock (FileLock)
        {
            try
            {
                var configDir = Path.Combine(AppContext.BaseDirectory, "config");
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                var json = JsonConvert.SerializeObject(presets, Formatting.Indented);
                File.WriteAllText(PresetsFilePath, json);
            }
            catch (Exception ex)
            {
                LoggerService.LogError(ex);
            }
        }
    }

    /// <summary>
    /// Saves or updates a single preset.
    /// </summary>
    public void SavePreset(TaskPreset preset)
    {
        var presets = LoadPresets();
        var existingIndex = presets.FindIndex(p => p.Name.Equals(preset.Name, StringComparison.OrdinalIgnoreCase));
        preset.UpdatedAt = DateTime.Now;

        if (existingIndex >= 0)
        {
            presets[existingIndex] = preset;
        }
        else
        {
            presets.Add(preset);
        }

        SavePresets(presets);
    }

    /// <summary>
    /// Deletes a preset by name.
    /// </summary>
    public bool DeletePreset(string presetName)
    {
        var presets = LoadPresets();
        var removed = presets.RemoveAll(p => p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            SavePresets(presets);
            return true;
        }

        return false;
    }
}
