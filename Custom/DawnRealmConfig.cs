using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace MFAWPF.Custom;

/// <summary>
/// 黎明界肉鸽运行时配置（由 pipeline option / custom_action_param 注入）。
/// </summary>
public static class DawnRealmConfig
{
    public const string StrategyStable = "stable";
    public const string StrategyScore = "score";

    /// <summary>stable | score</summary>
    public static string Strategy { get; private set; } = StrategyStable;

    /// <summary>同行公会显示名（繁中）</summary>
    public static string Guild { get; private set; } = "美食殿堂";

    /// <summary>目标难度，1+</summary>
    public static int Difficulty { get; private set; } = 5;

    /// <summary>0 = 用完通行证为止</summary>
    public static int MaxRuns { get; private set; } = 0;

    public static int CompletedRuns { get; set; }

    /// <summary>角色属性优先级（高到低）</summary>
    public static IReadOnlyList<string> AttrPriority { get; private set; } =
        ["光", "火", "暗", "風", "风", "水"];

    /// <summary>职阶关键词优先级</summary>
    public static IReadOnlyList<string> RolePriority { get; private set; } =
        ["坦克", "防衛", "防御", "補師", "补师", "回復", "回复", "輸出", "输出"];

    /// <summary>
    /// 遗物优先级：难度 &lt; 5 偏好弱体（冲 4/9/15）；难度 ≥ 5 用守护/会心。
    /// </summary>
    public static IReadOnlyList<string> GetRelicPriority()
    {
        if (Difficulty < 5)
        {
            // 低难度：弱体叠层 4→9→15；关键词尽量匹配 UI 上的弱体标记
            return
            [
                "弱體+15", "弱体+15", "弱體15", "弱体15",
                "弱體+9", "弱体+9", "弱體9", "弱体9",
                "弱體+4", "弱体+4", "弱體4", "弱体4",
                "弱體", "弱体",
                "會心", "会心", "守护", "加速"
            ];
        }

        return ["守护", "會心", "会心", "弱體", "弱体", "加速"];
    }

    public static void ApplyParam(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            var obj = JObject.Parse(json);
            if (obj["strategy"] != null)
                Strategy = obj["strategy"]!.ToString();
            if (obj["guild"] != null)
                Guild = obj["guild"]!.ToString();
            if (obj["difficulty"] != null && int.TryParse(obj["difficulty"]!.ToString(), out var d))
                Difficulty = Math.Max(1, d);
            if (obj["max_runs"] != null && int.TryParse(obj["max_runs"]!.ToString(), out var m))
                MaxRuns = Math.Max(0, m);
        }
        catch
        {
            // ignore bad param
        }
    }

    public static string? ReadActionParam(object args)
    {
        var t = args.GetType();
        foreach (var name in new[] { "CustomActionParam", "ActionParam", "Param" })
        {
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            var v = p?.GetValue(args);
            if (v != null)
                return v.ToString();
        }
        return null;
    }

    public static void ResetRunCounter() => CompletedRuns = 0;

    public static bool ShouldStopByRunCount() =>
        MaxRuns > 0 && CompletedRuns >= MaxRuns;

    /// <summary>
    /// 稳定通关：EX>遗物>招募>商店>事件>小怪
    /// 凹分：提高 EX 权重（排更前）
    /// </summary>
    public static IReadOnlyList<string> NodePriorityLabels()
    {
        if (Strategy == StrategyScore)
        {
            return ["EX", "遗物", "寶箱", "宝箱", "招募", "公主", "商店", "事件", "小怪", "戰鬥", "战斗"];
        }
        return ["EX", "遗物", "招募", "公主", "商店", "事件", "小怪", "戰鬥", "战斗"];
    }
}
