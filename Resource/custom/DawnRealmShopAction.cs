using System.Threading;
using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAWPF.Custom;
using MFAWPF.Extensions;
using MFAWPF.Extensions.Maa;
using MFAWPF.Helper;

namespace MFAWPF.CustomRuntime;

/// <summary>
/// 商店：稳定通关默认跳过购买；凹分时尝试买一次后离开。
/// </summary>
public class DawnRealmShopAction : MaaCustomAction
{
    public override string Name { get; set; } = nameof(DawnRealmShopAction);

    protected override void ErrorHandle(IMaaContext context, RunArgs args)
    {
        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
    }

    protected override bool Execute(IMaaContext context, RunArgs args)
    {
        DawnRealmConfig.ApplyParam(DawnRealmConfig.ReadActionParam(args));
        var image = context.GetImage();

        if (DawnRealmConfig.Strategy == DawnRealmConfig.StrategyScore)
        {
            // 凹分：尝试点第一个可购项
            if (context.OCR("購買", image, out var buy, 80, 140, 1120, 420) ||
                context.OCR("购买", image, out buy, 80, 140, 1120, 420))
            {
                context.Click(buy.HitBox.X + buy.HitBox.Width / 2, buy.HitBox.Y + buy.HitBox.Height / 2);
                Thread.Sleep(400);
                image = context.GetImage();
                if (context.OCR("確定", image, out var ok, 0, 480, 1280, 220) ||
                    context.OCR("确定", image, out ok, 0, 480, 1280, 220))
                {
                    context.Click(ok.HitBox.X + ok.HitBox.Width / 2, ok.HitBox.Y + ok.HitBox.Height / 2);
                    Thread.Sleep(400);
                }
            }
        }
        else
        {
            LoggerService.LogInfo("[DawnRealm] shop skip (stable)");
        }

        image = context.GetImage();
        // 印记「可進行邀請的角色一覽」会挡住商店离开键
        if (context.OCR("可進行邀請", image, out _) ||
            context.OCR("可进行邀请", image, out _) ||
            context.OCR("角色一覽", image, out _) ||
            context.OCR("角色一览", image, out _))
        {
            LoggerService.LogInfo("[DawnRealm] shop dismiss invite list");
            if (context.OCR("關閉", image, out var closeList, 200, 500, 880, 200) ||
                context.OCR("关闭", image, out closeList, 200, 500, 880, 200))
            {
                context.Click(closeList.HitBox.X + closeList.HitBox.Width / 2,
                    closeList.HitBox.Y + closeList.HitBox.Height / 2);
            }
            else
            {
                context.Click(640, 620);
            }
            Thread.Sleep(500);
            image = context.GetImage();
        }

        if (context.OCR("離開", image, out var leave, 0, 520, 1280, 200) ||
            context.OCR("离开", image, out leave, 0, 520, 1280, 200) ||
            context.OCR("關閉", image, out leave, 800, 560, 460, 160) ||
            context.OCR("关闭", image, out leave, 800, 560, 460, 160))
        {
            context.Click(leave.HitBox.X + leave.HitBox.Width / 2, leave.HitBox.Y + leave.HitBox.Height / 2);
            Thread.Sleep(600);
        }
        else
        {
            context.Click(1100, 660);
            Thread.Sleep(600);
        }

        // 退出确认「商店確認」
        image = context.GetImage();
        if (context.OCR("商店確認", image, out _) ||
            context.OCR("商店确认", image, out _) ||
            context.OCR("無法再次進入", image, out _) ||
            context.OCR("无法再次进入", image, out _))
        {
            LoggerService.LogInfo("[DawnRealm] shop exit confirm OK");
            if (context.OCR("OK", image, out var ok2, 550, 420, 400, 200))
                context.Click(ok2.HitBox.X + ok2.HitBox.Width / 2, ok2.HitBox.Y + ok2.HitBox.Height / 2);
            else
                context.Click(780, 520);
            Thread.Sleep(500);
        }

        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
        return true;
    }
}
