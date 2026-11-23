using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAWPF.Helper;


namespace MFAWPF.Configuration;

public partial class MFAConfiguration : ObservableObject
{
    [ObservableProperty] public string _name;
    [ObservableProperty] public string _fileName;
    [ObservableProperty] public Dictionary<string, object> _config;


    [RelayCommand]
    public void DeleteConfiguration(MFAConfiguration configuration)
    {
        var configName = configuration.Name;
        if (!ConfigurationHelper.Configs.Any(c => c.Name.Equals(configName, StringComparison.OrdinalIgnoreCase)))
        {
            LoggerService.LogError($"Configuration {configName} does not exist");
            return;
        }

        if (ConfigurationHelper.ConfigName == configName)
        {
            LoggerService.LogError($"Configuration {configName} is current configuration, cannot delete");
            return;
        }
        Instances.SettingsViewModel.ConfigurationList.Remove(configuration);
        // 文件名可能是 config.json（显示名 Default），优先按 FileName 删除
        var fileKey = string.IsNullOrWhiteSpace(configuration.FileName)
            ? configName
            : Path.GetFileNameWithoutExtension(configuration.FileName);
        ConfigurationHelper.DeleteConfig(fileKey);
    }

    public string DisplayName => Name;
    public bool IsDeletable => Instances.SettingsViewModel?.ConfigurationList?.Count > 1 && ConfigurationHelper.ConfigName != Name;

    public override string ToString()
        => Name;
}
