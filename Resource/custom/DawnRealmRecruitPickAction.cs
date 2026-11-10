using System.Threading;
using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAWPF.Custom;
using MFAWPF.Extensions;
using MFAWPF.Extensions.Maa;
using MFAWPF.Helper;

namespace MFAWPF.CustomRuntime;

/// <summary>
/// 招募/选角：属性优先光、火，职阶优先坦/奶。
/// </summary>
public class DawnRealmRecruitPickAction : MaaCustomAction
{
    public override string Name { get; set; } = nameof(DawnRealmRecruitPickAction);

    protected override void ErrorHandle(IMaaContext context, RunArgs args)
    {
        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
    }

    protected override bool Execute(IMaaContext context, RunArgs args)
    {
        DawnRealmConfig.ApplyParam(DawnRealmConfig.ReadActionParam(args));
        var image = context.GetImage();

        // 先按属性（光/火）
        foreach (var attr in DawnRealmConfig.AttrPriority)
        {
            if (context.OCR(attr, image, out var detail, 60, 80, 1160, 500) && detail.HitBox != null)
            {
                LoggerService.LogInfo($"[DawnRealm] recruit attr '{attr}'");
                context.Click(detail.HitBox.X + detail.HitBox.Width / 2, detail.HitBox.Y + detail.HitBox.Height / 2);
                Thread.Sleep(350);
                image = context.GetImage();
                break;
            }
        }

        // 再按职阶
        image = context.GetImage();
        foreach (var role in DawnRealmConfig.RolePriority)
        {
            if (context.OCR(role, image, out var detail, 60, 80, 1160, 500) && detail.HitBox != null)
            {
                LoggerService.LogInfo($"[DawnRealm] recruit role '{role}'");
                context.Click(detail.HitBox.X + detail.HitBox.Width / 2, detail.HitBox.Y + detail.HitBox.Height / 2);
                Thread.Sleep(350);
                break;
            }
        }

        // 点一个中间角色卡并确认
        context.Click(640, 360);
        Thread.Sleep(300);
        image = context.GetImage();
        if (context.OCR("確定", image, out var hit, 0, 520, 1280, 200) ||
            context.OCR("确定", image, out hit, 0, 520, 1280, 200) ||
            context.OCR("招募", image, out hit, 0, 520, 1280, 200) ||
            context.OCR("選擇", image, out hit, 0, 520, 1280, 200) ||
            context.OCR("选择", image, out hit, 0, 520, 1280, 200))
        {
            context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
        }
        else
        {
            context.Click(960, 620);
        }

        Thread.Sleep(500);
        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
        return true;
    }
}
