using System.IO;
using System.Text.RegularExpressions;
using MFAWPF.Configuration;
using Newtonsoft.Json.Linq;

namespace MFAWPF.Helper;

/// <summary>
/// 按模拟器/设备身份归组：同一设备 → 同一 mutex / 任务栏 AUMID。
/// 台服约定：MuMu 多开号 1 → 16416，3 → 16480。
/// </summary>
public static class DeviceIdentity
{
    /// <summary>稳定设备键，如 MuMu1 / MuMu3 / serial_xxx / default。</summary>
    public static string DeviceKey { get; private set; } = "default";

    /// <summary>任务栏 AppUserModelID，同设备相同。</summary>
    public static string AppUserModelId => $"Rino.Device.{DeviceKey}";

    public static void Resolve()
    {
        DeviceKey = ResolveCore();
        LoggerService.LogInfo($"Device identity key={DeviceKey} aumid={AppUserModelId}");
    }

    private static string ResolveCore()
    {
        // 1) --instance 映射到已知 MuMu 设备
        if (TryMapInstanceToMuMu(AppLaunchOptions.InstanceId, out var fromInstance))
            return fromInstance;

        // 2) 读当前实例配置里的 EmulatorConfig / AdbSerial（LoadConfig 前后都可用）
        if (TryReadFromConfigDir(ConfigurationHelper.GetConfigDirectory(), out var fromConfig))
            return fromConfig;

        // 3) 默认 config 目录再试一次
        var shared = Path.Combine(AppContext.BaseDirectory, "config");
        if (!string.Equals(ConfigurationHelper.GetConfigDirectory(), shared, StringComparison.OrdinalIgnoreCase)
            && TryReadFromConfigDir(shared, out var fromShared))
            return fromShared;

        // 4) 非标准实例号：仍按 instance 区分，避免互抢
        if (!string.IsNullOrEmpty(AppLaunchOptions.InstanceId))
            return "Inst" + AppLaunchOptions.InstanceId;

        return "default";
    }

    public static bool TryMapInstanceToMuMu(string? instanceId, out string deviceKey)
    {
        deviceKey = "";
        if (instanceId == "1")
        {
            deviceKey = "MuMu1";
            return true;
        }
        if (instanceId == "3")
        {
            deviceKey = "MuMu3";
            return true;
        }
        return false;
    }

    /// <summary>
    /// 解析当前应对应的 MuMu 多开序号（用于自动拉起模拟器）。
    /// </summary>
    public static bool TryResolveMuMuIndex(out int index)
    {
        index = 0;
        if (AppLaunchOptions.InstanceId == "1")
        {
            index = 1;
            return true;
        }
        if (AppLaunchOptions.InstanceId == "3")
        {
            index = 3;
            return true;
        }

        if (!string.IsNullOrEmpty(DeviceKey)
            && DeviceKey.StartsWith("MuMu", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(DeviceKey.AsSpan(4), out var fromKey)
            && fromKey >= 0)
        {
            index = fromKey;
            return true;
        }

        var emu = ConfigurationHelper.GetValue(ConfigurationKeys.EmulatorConfig, string.Empty);
        if (!string.IsNullOrWhiteSpace(emu))
        {
            if (int.TryParse(emu.Trim(), out var fromEmu) && fromEmu >= 0)
            {
                index = fromEmu;
                return true;
            }

            var m = Regex.Match(emu, "\"index\"\\s*:\\s*(\\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var fromJson))
            {
                index = fromJson;
                return true;
            }
        }

        // 从当前 config 目录再扫一遍 AdbSerial / mumu index
        if (TryReadFromConfigDir(ConfigurationHelper.GetConfigDirectory(), out var keyFromConfig)
            && keyFromConfig.StartsWith("MuMu", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(keyFromConfig.AsSpan(4), out var fromConfigDir))
        {
            index = fromConfigDir;
            return true;
        }

        return false;
    }

    public static string? GetMuMuAdbSerial(int index) => index switch
    {
        1 => "127.0.0.1:16416",
        3 => "127.0.0.1:16480",
        _ => null
    };

    /// <summary>自动多开时优先占用的设备实例号。</summary>
    public static IEnumerable<string> PreferredAutoInstanceIds()
    {
        yield return "1";
        yield return "3";
        for (var i = 2; i <= 16; i++)
        {
            if (i is 1 or 3) continue;
            yield return i.ToString();
        }
    }

    private static bool TryReadFromConfigDir(string configDir, out string deviceKey)
    {
        deviceKey = "";
        try
        {
            if (!Directory.Exists(configDir))
                return false;

            foreach (var file in Directory.EnumerateFiles(configDir, "*.json"))
            {
                var name = Path.GetFileName(file);
                if (name.Contains("maa", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("task_presets", StringComparison.OrdinalIgnoreCase))
                    continue;

                JObject obj;
                try
                {
                    obj = JObject.Parse(File.ReadAllText(file));
                }
                catch
                {
                    continue;
                }

                var emu = obj.Value<string>("EmulatorConfig")
                          ?? obj["AdbDevice"]?["Config"]?.ToString();
                if (!string.IsNullOrWhiteSpace(emu) && int.TryParse(emu.Trim(), out var idx) && idx >= 0)
                {
                    deviceKey = "MuMu" + idx;
                    return true;
                }

                // EmulatorConfig 可能只在 mumu extras JSON 字符串里
                var deviceCfg = obj["AdbDevice"]?["Config"]?.ToString();
                if (!string.IsNullOrWhiteSpace(deviceCfg))
                {
                    var m = Regex.Match(deviceCfg, "\"index\"\\s*:\\s*(\\d+)");
                    if (m.Success)
                    {
                        deviceKey = "MuMu" + m.Groups[1].Value;
                        return true;
                    }
                }

                var serial = obj.Value<string>("AdbSerial")
                             ?? obj["AdbDevice"]?["AdbSerial"]?.ToString();
                if (!string.IsNullOrWhiteSpace(serial))
                {
                    var safe = Regex.Replace(serial, @"[^A-Za-z0-9]+", "_").Trim('_');
                    if (safe.Length > 0)
                    {
                        deviceKey = "Serial_" + safe;
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo($"Device identity peek config failed: {ex.Message}");
        }

        return false;
    }
}
