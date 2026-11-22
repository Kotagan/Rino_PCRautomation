using System.Threading;
using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAWPF.Custom;
using MFAWPF.Extensions;
using MFAWPF.Extensions.Maa;
using MFAWPF.Helper;

namespace MFAWPF.CustomRuntime;

/// <summary>
/// 遗物选择：难度&lt;5 偏好弱体4/9/15；难度≥5 优先守护、会心。
/// </summary>
public class DawnRealmRelicPickAction : MaaCustomAction
{
    public override string Name { get; set; } = nameof(DawnRealmRelicPickAction);

    protected override void ErrorHandle(IMaaContext context, RunArgs args)
    {
        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
    }

    protected override bool Execute(IMaaContext context, RunArgs args)
    {
        DawnRealmConfig.ApplyParam(DawnRealmConfig.ReadActionParam(args));
        var image = context.GetImage();

        // 遗物效果结算弹窗，不是三选一
        if (context.OCR("遺物效果", image, out _, 200, 20, 880, 200) ||
            context.OCR("已透過", image, out _, 200, 60, 880, 200) ||
            context.OCR("已透过", image, out _, 200, 60, 880, 200))
        {
            LoggerService.LogInfo("[DawnRealm] relic effect popup -> close");
            context.Click(640, 560);
            Thread.Sleep(200);
            context.Click(640, 600);
            Thread.Sleep(400);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        var priorities = DawnRealmConfig.GetRelicPriority();
        LoggerService.LogInfo(
            $"[DawnRealm] relic policy diff={DawnRealmConfig.Difficulty} " +
            $"mode={(DawnRealmConfig.Difficulty < 5 ? "weakness-4/9/15" : "guard-crit")}");

        foreach (var key in priorities)
        {
            if (context.OCR(key, image, out var detail, 80, 120, 1120, 420) && detail.HitBox != null)
            {
                LoggerService.LogInfo($"[DawnRealm] pick relic '{key}'");
                context.Click(detail.HitBox.X + detail.HitBox.Width / 2, detail.HitBox.Y + detail.HitBox.Height / 2);
                Thread.Sleep(400);
                Confirm(context);
                context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
                return true;
            }
        }

        // 无偏好命中：点中间候选
        LoggerService.LogInfo("[DawnRealm] relic fallback center");
        context.Click(640, 320);
        Thread.Sleep(400);
        Confirm(context);
        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
        return true;
    }

    private static void Confirm(IMaaContext context)
    {
        var image = context.GetImage();
        if (context.OCR("確定", image, out var hit, 0, 520, 1280, 200) ||
            context.OCR("确定", image, out hit, 0, 520, 1280, 200) ||
            context.OCR("選擇", image, out hit, 0, 520, 1280, 200) ||
            context.OCR("选择", image, out hit, 0, 520, 1280, 200) ||
            context.OCR("決定", image, out hit, 0, 520, 1280, 200))
        {
            context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            Thread.Sleep(500);
        }
        else
        {
            context.Click(900, 620);
            Thread.Sleep(400);
        }
    }
}
