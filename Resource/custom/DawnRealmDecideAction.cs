using System;
using System.Collections.Generic;
using System.Threading;
using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAWPF.Custom;
using MFAWPF.Extensions;
using MFAWPF.Extensions.Maa;
using MFAWPF.Helper;

namespace MFAWPF.CustomRuntime;

/// <summary>
/// 黎明界地图：按策略优先级点选可走节点。
/// </summary>
public class DawnRealmDecideAction : MaaCustomAction
{
    public override string Name { get; set; } = nameof(DawnRealmDecideAction);

    protected override void ErrorHandle(IMaaContext context, RunArgs args)
    {
        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
    }

    protected override bool Execute(IMaaContext context, RunArgs args)
    {
        DawnRealmConfig.ApplyParam(DawnRealmConfig.ReadActionParam(args));
        LoggerService.LogInfo($"[DawnRealm] Decide strategy={DawnRealmConfig.Strategy} guild={DawnRealmConfig.Guild}");

        var image = context.GetImage();
        // 结算 / 撤退优先处理
        if (context.OCR("通關", image, out var hit, 200, 80, 880, 200) ||
            context.OCR("通关", image, out hit, 200, 80, 880, 200) ||
            context.OCR("挑戰結果", image, out hit, 200, 80, 880, 200) ||
            context.OCR("挑战结果", image, out hit, 200, 80, 880, 200))
        {
            DawnRealmConfig.CompletedRuns++;
            LoggerService.LogInfo($"[DawnRealm] settle runs={DawnRealmConfig.CompletedRuns}");
            context.OverrideNext(args.NodeName, ["黎明界结算"]);
            return true;
        }

        if (DawnRealmConfig.ShouldStopByRunCount())
        {
            LoggerService.LogInfo("[DawnRealm] max runs reached, exit");
            context.OverrideNext(args.NodeName, ["黎明界回主页"]);
            return true;
        }

        if (context.OCR("全滅", image, out hit, 200, 100, 880, 240) ||
            context.OCR("全灭", image, out hit, 200, 100, 880, 240))
        {
            context.OverrideNext(args.NodeName, ["黎明界失败退出"]);
            return true;
        }

        // 战斗中：点 AUTO 区域或等待
        if (context.OCR("AUTO", image, out hit, 1000, 600, 260, 100) ||
            context.OCR("自動", image, out hit, 1000, 600, 260, 100))
        {
            context.OverrideNext(args.NodeName, ["黎明界战斗中"]);
            return true;
        }

        // 遗物 / 招募 / 商店界面分流
        if (context.OCR("遺物", image, out hit, 0, 0, 1280, 200) ||
            context.OCR("遗物", image, out hit, 0, 0, 1280, 200) ||
            context.OCR("選擇遺物", image, out hit, 200, 0, 880, 160))
        {
            context.OverrideNext(args.NodeName, ["黎明界选遗物"]);
            return true;
        }

        if (context.OCR("招募", image, out hit, 0, 0, 1280, 200) ||
            context.OCR("選擇夥伴", image, out hit, 200, 0, 880, 160) ||
            context.OCR("选择伙伴", image, out hit, 200, 0, 880, 160))
        {
            context.OverrideNext(args.NodeName, ["黎明界选角色"]);
            return true;
        }

        if (context.OCR("商店", image, out hit, 400, 0, 480, 160) &&
            (context.OCR("購買", image, out _, 0, 500, 1280, 200) || context.OCR("购买", image, out _, 0, 500, 1280, 200)))
        {
            context.OverrideNext(args.NodeName, ["黎明界商店"]);
            return true;
        }

        // 地图节点：按优先级 OCR 点击
        foreach (var label in DawnRealmConfig.NodePriorityLabels())
        {
            if (TryClickLabel(context, image, label))
            {
                Thread.Sleep(600);
                context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
                return true;
            }
        }

        // 通用弹窗 / 对话
        if (context.OCR("跳過", image, out hit, 900, 0, 360, 120) ||
            context.OCR("跳过", image, out hit, 900, 0, 360, 120) ||
            context.OCR("關閉", image, out hit, 0, 500, 1280, 220) ||
            context.OCR("关闭", image, out hit, 0, 500, 1280, 220) ||
            context.OCR("知道了", image, out hit, 0, 400, 1280, 300))
        {
            context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            Thread.Sleep(400);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 兜底：点屏幕中部可走格，再点确认类按钮
        if (context.OCR("挑戰", image, out hit, 0, 500, 1280, 200) ||
            context.OCR("挑战", image, out hit, 0, 500, 1280, 200) ||
            context.OCR("進入", image, out hit, 0, 500, 1280, 200) ||
            context.OCR("进入", image, out hit, 0, 500, 1280, 200) ||
            context.OCR("確定", image, out hit, 0, 500, 1280, 200) ||
            context.OCR("确定", image, out hit, 0, 500, 1280, 200) ||
            context.OCR("下一步", image, out hit, 0, 500, 1280, 200))
        {
            context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            Thread.Sleep(500);
        }
        else
        {
            // 点地图中心偏右，尝试推进（横屏 1280x720）
            context.Click(720, 360);
            Thread.Sleep(400);
        }

        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
        return true;
    }

    private static bool TryClickLabel(IMaaContext context, IMaaImageBuffer image, string label)
    {
        // 地图中部 ROI
        if (context.OCR(label, image, out var detail, 120, 100, 1040, 480) && detail.HitBox != null)
        {
            LoggerService.LogInfo($"[DawnRealm] click node '{label}'");
            context.Click(detail.HitBox.X + detail.HitBox.Width / 2, detail.HitBox.Y + detail.HitBox.Height / 2);
            return true;
        }
        return false;
    }
}
