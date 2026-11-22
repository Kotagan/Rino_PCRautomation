using System.Threading;
using MaaFramework.Binding;
using MaaFramework.Binding.Custom;
using MFAWPF.Custom;
using MFAWPF.Extensions;
using MFAWPF.Extensions.Maa;
using MFAWPF.Helper;

namespace MFAWPF.CustomRuntime;

/// <summary>
/// 启动时探测当前界面并分流，顺便打日志避免“假卡住”。
/// </summary>
public class DawnRealmBootAction : MaaCustomAction
{
    public override string Name { get; set; } = nameof(DawnRealmBootAction);

    protected override void ErrorHandle(IMaaContext context, RunArgs args)
    {
        context.OverrideNext(args.NodeName, ["黎明界进入冒险"]);
    }

    protected override bool Execute(IMaaContext context, RunArgs args)
    {
        LoggerService.LogInfo("[DawnRealm] boot probe");

        // 真正主页：底栏「主頁」或主页资源栏；冒险页也有底栏主頁时靠「地下城」排除
        if ((Hit(context, "主頁", "主页") || Hit(context, "體力", "体力", "禮物", "礼物")) &&
            !Hit(context, "地下城", "主線", "主线", "高難", "高难", "調查", "调查", "特別地下城") &&
            !Hit(context, "持有的通行證", "持有的通行证", "始源強化", "始源强化"))
        {
            LoggerService.LogInfo("[DawnRealm] -> home, enter adventure");
            context.OverrideNext(args.NodeName, ["黎明界进入冒险", "黎明界点击入口"]);
            return true;
        }

        // 已在冒险页：地下城大厅，或顶栏「冒險」+主線/黎明界入口
        if ((Hit(context, "地下城") && Hit(context, "主線", "主线", "高難", "高难", "特別地下城")) ||
            (Hit(context, "主線冒險", "主线冒险", "探險", "探险") && !Hit(context, "持有的通行證", "持有的通行证")))
        {
            LoggerService.LogInfo("[DawnRealm] -> adventure page");
            context.OverrideNext(args.NodeName, ["黎明界点击入口", "黎明界点击入口模板", "黎明界启动探测"]);
            return true;
        }

        if (Hit(context, "公告", "更新情報", "活動資訊"))
        {
            LoggerService.LogInfo("[DawnRealm] -> announcement");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 先判弹窗/地图，避免「出發獎勵」里的「出發」误判回大厅
        if (Hit(context, "RESULT", "分數細分", "攻略分數", "獲得迷宮代幣"))
        {
            LoggerService.LogInfo("[DawnRealm] -> result");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        if (Hit(context, "取得道具", "獲得寶箱", "获得宝箱", "已經領取", "已经领取", "出發獎勵", "迷宫硬币", "迷宮硬幣"))
        {
            LoggerService.LogInfo("[DawnRealm] -> map popup");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        if (Hit(context, "角色加入獎勵", "還有1次", "还有1次", "成為了你的夥伴", "成為了夥伴", "成为了夥伴"))
        {
            LoggerService.LogInfo("[DawnRealm] -> join reward/confirm");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        if (Hit(context, "WIN", "下一步", "傷害報告", "伤害报告"))
        {
            LoggerService.LogInfo("[DawnRealm] -> post-battle");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        if (Hit(context, "出發獎勵", "出发奖励", "已取得", "帶回獎勵", "带回奖励", "迷宮遺物效果"))
        {
            LoggerService.LogInfo("[DawnRealm] -> reward popup / map");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        if (Hit(context, "進行選擇", "进行选择", "同行的公會", "同行的公会"))
        {
            LoggerService.LogInfo("[DawnRealm] -> guild select");
            context.OverrideNext(args.NodeName, ["黎明界点选公会按钮"]);
            return true;
        }

        if (Hit(context, "編組隊伍", "编组队伍", "戰鬥開始", "战斗开始", "當前的成員",
                "總戰力", "总战力", "發動屬性獎勵", "对Boss的有效效果", "對Boss的有效效果",
                "尚未編組", "尚未编组", "亦可挑戰", "也可挑战"))
        {
            LoggerService.LogInfo("[DawnRealm] -> team form");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        if (Hit(context, "選擇角色", "选择角色", "進行邀請", "邀请为夥伴", "邀請為夥伴"))
        {
            LoggerService.LogInfo("[DawnRealm] -> recruit");
            context.OverrideNext(args.NodeName, ["黎明界选角色"]);
            return true;
        }

        // 扫荡菜单不是地图
        if (Hit(context, "可執行掃蕩", "可执行扫荡", "掃蕩的公會", "全部跳過"))
        {
            LoggerService.LogInfo("[DawnRealm] -> sweep menu");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 结束确认弹窗里也有「撤退/無獎勵」文案，不能当地图
        if (Hit(context, "結束確認", "结束确认", "FAILED"))
        {
            LoggerService.LogInfo("[DawnRealm] -> fail/end-confirm");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 已在迷宫地图内（勿用「有獎勵」；勿用裸 NORMAL：战斗格子标题也会命中）
        if (Hit(context, "區域", "区域") ||
            (Hit(context, "撤退") && !Hit(context, "結束確認", "结束确认", "再次挑戰", "再次挑战")))
        {
            LoggerService.LogInfo("[DawnRealm] -> in-map");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 仅 AUTO/自動 认战斗；裸「戰鬥」会误伤 Boss戰鬥格子 标题
        if (Hit(context, "AUTO", "自動", "自动"))
        {
            // 勿进「黎明界战斗中」DirectHit 流水线：会静默挂死，改由 Decide 轮询
            LoggerService.LogInfo("[DawnRealm] -> battle (decide loop)");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 黎明界大厅：禁止裸匹配「通行證」（主页「賽季通行證」会误伤）
        if (Hit(context, "持有的通行證", "持有的通行证") ||
            Hit(context, "始源強化", "始源强化", "遺物圖鑑", "遗物图鉴") ||
            (Hit(context, "通行證", "通行证") && Hit(context, "變更難度", "变更难度", "掃蕩", "扫荡")))
        {
            LoggerService.LogInfo("[DawnRealm] -> lobby");
            context.OverrideNext(args.NodeName, ["黎明界点击出发", "黎明界点击出发坐标"]);
            return true;
        }

        var image = context.GetImage();

        // 事件/选角页：交给 Decide 点「選擇」
        if (Hit(context, "出現事件", "出现事件", "還有1次", "还有1次", "角色加入獎勵"))
        {
            LoggerService.LogInfo("[DawnRealm] -> event/join choice");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }
        if (context.OCR("選擇", image, out _, 80, 500, 1120, 200) ||
            context.OCR("选择", image, out _, 80, 500, 1120, 200))
        {
            LoggerService.LogInfo("[DawnRealm] -> choice buttons");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 戰鬥格子弹窗（标题含 NORMAL，须先于普通探测）
        if (Hit(context, "進行挑戰", "进行挑战", "觀看地圖", "观看地图", "戰鬥格子", "战斗格子") ||
            Hit(context, "屬性獎勵有效", "属性奖励"))
        {
            LoggerService.LogInfo("[DawnRealm] -> challenge popup");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 冒险页上的黎明界入口
        if (context.OCR("黎明界迷宮", image, out RecognitionDetail entry, 700, 400, 520, 280) ||
            context.OCR("黎明界迷宫", image, out entry, 700, 400, 520, 280) ||
            context.OCR("黎明界", image, out entry, 700, 450, 520, 220))
        {
            LoggerService.LogInfo("[DawnRealm] click adventure entry");
            context.Click(entry.HitBox.X + entry.HitBox.Width / 2, entry.HitBox.Y + entry.HitBox.Height / 2);
            Thread.Sleep(2000);
            context.OverrideNext(args.NodeName, ["黎明界启动探测", "黎明界页首"]);
            return true;
        }

        // 仍未知：先回 Decide（勿盲点進行挑戰，会误伤事件页）
        LoggerService.LogInfo("[DawnRealm] -> decide fallback");
        context.OverrideNext(args.NodeName, ["黎明界地图循环", "黎明界点击入口", "黎明界进入冒险"]);
        return true;
    }

    private static bool Hit(IMaaContext context, params string[] labels)
    {
        var image = context.GetImage();
        foreach (var label in labels)
        {
            if (context.OCR(label, image, out RecognitionDetail detail) && detail.HitBox != null)
            {
                LoggerService.LogInfo($"[DawnRealm] OCR hit '{label}'");
                return true;
            }
        }
        return false;
    }
}
