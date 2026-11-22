namespace MFAWPF.Helper;

/// <summary>
/// 启动参数。
/// --silent / -silent / /silent：最小化启动、不抢焦点、不激活已有窗口。
/// --instance N：多开实例名（如 1 / 3 → MuMu 设备），同设备共用 mutex/任务栏图标。
/// </summary>
public static class AppLaunchOptions
{
    public static bool Silent { get; private set; }

    /// <summary>空表示默认实例；非空时使用独立 mutex / config_instN。</summary>
    public static string InstanceId { get; private set; } = "";

    /// <summary>是否由命令行显式指定了 --instance（显式指定时不自动换号）。</summary>
    public static bool InstanceIdFromArgs { get; private set; }

    public static void Parse(IEnumerable<string>? args)
    {
        Silent = false;
        InstanceId = "";
        InstanceIdFromArgs = false;
        if (args is null)
        {
            return;
        }

        var list = args.Select(a => (a ?? "").Trim().Trim('"')).Where(a => a.Length > 0).ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var arg = list[i];
            if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("-silent", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("/silent", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase))
            {
                Silent = true;
                continue;
            }

            if (arg.StartsWith("--instance=", StringComparison.OrdinalIgnoreCase))
            {
                InstanceId = SanitizeInstanceId(arg["--instance=".Length..]);
                InstanceIdFromArgs = InstanceId.Length > 0;
                continue;
            }

            if (arg.Equals("--instance", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("-instance", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("/instance", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < list.Count)
                {
                    InstanceId = SanitizeInstanceId(list[++i]);
                    InstanceIdFromArgs = InstanceId.Length > 0;
                }
            }
        }
    }

    /// <summary>自动多开时写入实例号（未在命令行指定时）。</summary>
    public static void AssignInstanceId(string instanceId)
    {
        InstanceId = SanitizeInstanceId(instanceId);
    }

    private static string SanitizeInstanceId(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        // 仅保留字母数字与下划线，避免 mutex 名非法字符
        var chars = raw.Trim().Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray();
        return new string(chars);
    }
}
