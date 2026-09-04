using MFAWPF.Helper;
using MFAWPF.Models.Characters;
using Newtonsoft.Json;
using System.IO;

namespace MFAWPF.Services.Characters;

internal sealed class CharacterSettingsStore
{
    private readonly CharacterStoragePaths _storagePaths;
    private readonly object _syncRoot = new();

    public CharacterSettingsStore(CharacterStoragePaths storagePaths)
    {
        _storagePaths = storagePaths;
    }

    public CharacterSettings Load()
    {
        lock (_syncRoot)
        {
            try
            {
                if (!File.Exists(_storagePaths.ConfigurationPath))
                {
                    return CharacterSettings.CreateDefault();
                }

                var json = File.ReadAllText(_storagePaths.ConfigurationPath);
                var settings = JsonConvert.DeserializeObject<CharacterSettings>(json)
                               ?? CharacterSettings.CreateDefault();
                settings.Normalize();
                return settings;
            }
            catch (Exception ex)
            {
                LoggerService.LogError(ex);
                return CharacterSettings.CreateDefault();
            }
        }
    }

    public void Save(CharacterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_syncRoot)
        {
            settings.Normalize();
            var directory = Path.GetDirectoryName(_storagePaths.ConfigurationPath)
                            ?? throw new DirectoryNotFoundException(_storagePaths.ConfigurationPath);
            Directory.CreateDirectory(directory);
            var temporaryPath = _storagePaths.ConfigurationPath + ".tmp";

            try
            {
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, _storagePaths.ConfigurationPath, true);
            }
            catch (Exception ex)
            {
                LoggerService.LogError(ex);
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (Exception cleanupException)
                    {
                        LoggerService.LogError(cleanupException);
                    }
                }

                throw;
            }
        }
    }
}
