using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
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

    /// <summary>Boss 多队编成阶段：0=队1选人，1=队2，2=队3，3+=只开战。点两次同一人会取消。</summary>
    private static int _formTeamPass;

    /// <summary>刚经历过战斗 UI，AUTO 消失后用坐标点「下一步」（OCR 对艺术字 WIN/按钮常失败）。</summary>
    private static bool _seenBattle;

    /// <summary>Boss 战败后：結束 → 撤退（无奖励）撤队回主页。</summary>
    private static bool _withdrawNoReward;

    /// <summary>FAILED 后点「再次挑戰」次数；超过阈值才结束撤队。</summary>
    private static int _failRetryCount;
    private const int MaxFailRetries = 99;

    /// <summary>本轮已确认满编的队伍（bit0/1/2）。未满 5 人不会置位，下轮继续补。</summary>
    private static int _teamFillDoneMask;

    /// <summary>当前队伍连续补人次数，防止空转。</summary>
    private static int _teamFillAttempts;

    /// <summary>避免同一地图坐标连点空转。</summary>
    private static int _lastMapClickX, _lastMapClickY, _sameMapClickCount;

    /// <summary>地图 OCR 失败后的盲点轮询下标。</summary>
    private static int _blindTapIndex;

    /// <summary>人物右侧列点击轮询下标。</summary>
    private static int _nextColTryIndex;

    /// <summary>地图连续无进展次数（点空/死点循环）。</summary>
    private static int _mapStuckCount;

    /// <summary>上次地图有进展的 Tick。</summary>
    private static int _mapLastProgressTick;

    /// <summary>近期无效点击（未弹出移动确认），暂时跳过。</summary>
    private static readonly List<(int X, int Y, int UntilTick)> _deadNodes = new();

    /// <summary>刚点过移动确认，下一屏多半是戰鬥格子（OCR 常挂）。</summary>
    private static bool _expectChallenge;

    protected override void ErrorHandle(IMaaContext context, RunArgs args)
    {
        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
    }

    protected override bool Execute(IMaaContext context, RunArgs args)
    {
        DawnRealmConfig.ApplyParam(DawnRealmConfig.ReadActionParam(args));
        LoggerService.LogInfo($"[DawnRealm] Decide strategy={DawnRealmConfig.Strategy} guild={DawnRealmConfig.Guild}");

        var image = context.GetImage();

        // EX 装备弹窗：必须先取消，否则会误判成编队页死循环补人
        if (context.OCR("設定EX裝備", image, out var hit) ||
            context.OCR("设定EX装备", image, out hit) ||
            context.OCR("確定裝備", image, out hit, 700, 520, 450, 160) ||
            context.OCR("确定装备", image, out hit, 700, 520, 450, 160) ||
            context.OCR("自動裝備", image, out hit, 400, 520, 480, 160) ||
            (context.OCR("EX裝備", image, out hit, 300, 40, 700, 120) &&
             context.OCR("取消", image, out _, 200, 520, 400, 180)))
        {
            LoggerService.LogInfo("[DawnRealm] dismiss EX equip dialog");
            if (context.OCR("取消", image, out hit, 200, 520, 450, 180) && hit.HitBox != null)
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            else
                context.Click(416, 592); // 1600≈(520,740) →1280
            Thread.Sleep(500);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 公告弹窗
        if (context.OCR("公告", image, out hit, 400, 0, 480, 120) ||
            context.OCR("更新情報", image, out hit, 100, 40, 1000, 120) ||
            context.OCR("活動資訊", image, out hit, 100, 40, 1000, 120))
        {
            LoggerService.LogInfo("[DawnRealm] close announcement");
            if (context.OCR("關閉", image, out hit, 300, 560, 680, 140) ||
                context.OCR("关闭", image, out hit, 300, 560, 680, 140))
            {
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            }
            else
            {
                context.Click(640, 650);
                context.PressKey(4);
            }
            Thread.Sleep(800);
            context.OverrideNext(args.NodeName, ["黎明界启动探测", "黎明界地图循环"]);
            return true;
        }

        // 道具/怪物/角色详情/邀请一览等非地图弹窗
        if (context.OCR("道具詳細", image, out hit) ||
            context.OCR("道具详细", image, out hit) ||
            context.OCR("怪物詳細", image, out hit) ||
            context.OCR("怪物详细", image, out hit) ||
            context.OCR("角色詳細", image, out hit) ||
            context.OCR("角色详细", image, out hit) ||
            context.OCR("詳細資訊", image, out hit) ||
            context.OCR("详细资讯", image, out hit) ||
            context.OCR("可裝備的角色", image, out hit) ||
            context.OCR("可装备的角色", image, out hit) ||
            context.OCR("可進行邀請", image, out hit) ||
            context.OCR("可进行邀请", image, out hit) ||
            context.OCR("邀請的角色一覽", image, out hit) ||
            context.OCR("邀请的角色一览", image, out hit))
        {
            LoggerService.LogInfo("[DawnRealm] close item/monster/char detail");
            if (context.OCR("關閉", image, out hit, 200, 500, 880, 200) ||
                context.OCR("关闭", image, out hit, 200, 500, 880, 200))
            {
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            }
            else
            {
                context.Click(640, 650);
                context.PressKey(4);
            }
            Thread.Sleep(600);
            context.OverrideNext(args.NodeName, ["黎明界地图循环", "黎明界页首", "黎明界启动探测"]);
            return true;
        }

        // 扫荡一览：必须取消，绝不能点「全部跳過」
        if (context.OCR("可執行掃蕩", image, out hit) ||
            context.OCR("可执行扫荡", image, out hit) ||
            context.OCR("掃蕩的公會", image, out hit) ||
            context.OCR("每个公会使用", image, out hit) ||
            context.OCR("每個公會使用", image, out hit) ||
            context.OCR("勾選全取消", image, out hit) ||
            (context.OCR("全部跳過", image, out hit) && context.OCR("取消", image, out _, 100, 560, 500, 140)))
        {
            LoggerService.LogInfo("[DawnRealm] cancel sweep menu");
            // ADB tap 在此模拟器上常失效；优先点底部取消，再 BACK
            context.Click(420, 655);
            Thread.Sleep(400);
            context.Click(480, 650);
            Thread.Sleep(400);
            context.PressKey(4); // KEYCODE_BACK
            Thread.Sleep(800);
            context.OverrideNext(args.NodeName, ["黎明界启动探测", "黎明界选择公会", "黎明界页首"]);
            return true;
        }

        // 误入公会选择页：交回流水线
        if (context.OCR("請選擇要同行", image, out _) ||
            context.OCR("同行的公會", image, out _) ||
            context.OCR("同行的公会", image, out _) ||
            context.OCR("進行選擇", image, out _) ||
            context.OCR("进行选择", image, out _) ||
            context.OCR("變更難度", image, out _) ||
            context.OCR("变更难度", image, out _))
        {
            LoggerService.LogInfo("[DawnRealm] -> guild select from Decide");
            context.OverrideNext(args.NodeName, ["黎明界选择公会", "黎明界滑动找公会", "黎明界默认选公会", "黎明界点选公会按钮"]);
            return true;
        }

        // 结算 / 撤退优先处理
        if (context.OCR("通關", image, out hit, 200, 80, 880, 200) ||
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

        // 探索结算 RESULT（勿用「角色加入」匹配，会误伤「角色加入分數」）
        // 注：地图上「查看成果」也会出 RESULT+下一步，点下一步后应继续地图，勿直接回主页
        if (context.OCR("RESULT", image, out hit) ||
            context.OCR("分數細分", image, out hit) ||
            context.OCR("攻略分數", image, out hit) ||
            context.OCR("獲得迷宮代幣", image, out hit))
        {
            _expectChallenge = false;
            LoggerService.LogInfo("[DawnRealm] close RESULT");
            if (context.OCR("下一步", image, out hit) ||
                context.OCR("關閉", image, out hit) ||
                context.OCR("关闭", image, out hit))
            {
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            }
            else
            {
                context.Click(640, 660);
            }
            Thread.Sleep(1000);
            var endRun = context.OCR("帶回獎勵", context.GetImage(), out _) ||
                         context.OCR("带回奖励", context.GetImage(), out _) ||
                         context.OCR("獲得迷宮代幣", context.GetImage(), out _);
            if (endRun)
            {
                context.OverrideNext(args.NodeName, ["黎明界回主页", "黎明界页首", "黎明界结算"]);
            }
            else
            {
                context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            }
            return true;
        }

        // 加入奖励三选一 ≠ 开局招募 3/3（勿进 RecruitPick）
        var isJoinReward = context.OCR("還有1次", image, out _) ||
                           context.OCR("还有1次", image, out _) ||
                           context.OCR("角色加入獎勵", image, out _) ||
                           context.OCR("角色加入奖励", image, out _) ||
                           context.OCR("加入獎勵！", image, out _) ||
                           context.OCR("加入奖励！", image, out _);

        // 招募选角页优先（勿被地图/事件/编队框扫误伤）
        // 文案 OCR 常挂：辅以底栏「地圖」+ 右下邀请蓝钮 / 标题区「3/3」
        var hasRecruitTitle = context.OCR("選擇角色", image, out hit, 200, 0, 1000, 140) ||
                              context.OCR("选择角色", image, out hit, 200, 0, 1000, 140);
        var hasRecruitCount = context.OCR("3/3", image, out hit, 900, 10, 360, 120) ||
                              context.OCR("2/3", image, out hit, 900, 10, 360, 120) ||
                              context.OCR("1/3", image, out hit, 900, 10, 360, 120) ||
                              context.OCR("0/3", image, out hit, 900, 10, 360, 120);
        var hasInviteBtn = context.OCR("進行邀請", image, out hit, 850, 560, 400, 160) ||
                           context.OCR("进行邀请", image, out hit, 850, 560, 400, 160);
        // 单靠邀請蓝钮不够；要有标题或 0~3/3 计数
        var recruitOcr = hasRecruitTitle || hasRecruitCount || (hasInviteBtn && hasRecruitTitle);
        var onRecruit = !isJoinReward && (recruitOcr || LooksLikeRecruitUi(context));
        if (onRecruit)
        {
            LoggerService.LogInfo("[DawnRealm] -> recruit (early)");
            context.OverrideNext(args.NodeName, ["黎明界选角色"]);
            return true;
        }

        // 事件/加入奖励（「選擇」OCR 在台服常失败，出现事件时直接坐标点左卡）
        var isEventChoice = context.OCR("出現事件", image, out hit) ||
                            context.OCR("出现事件", image, out hit) ||
                            context.OCR("角色加入獎勵", image, out hit) ||
                            context.OCR("加入獎勵！", image, out hit) ||
                            context.OCR("將所選的角色", image, out hit) ||
                            context.OCR("将所选的角色", image, out hit) ||
                            context.OCR("邀請為夥伴", image, out hit, 200, 40, 880, 160) ||
                            context.OCR("還有1次", image, out hit) ||
                            context.OCR("还有1次", image, out hit);
        if (isEventChoice)
        {
            LoggerService.LogInfo("[DawnRealm] pick event/join choice");
            var clicked = false;
            if (context.OCR("選擇", image, out var sel, 80, 480, 1120, 200) ||
                context.OCR("选择", image, out sel, 80, 480, 1120, 200))
            {
                context.Click(sel.HitBox.X + sel.HitBox.Width / 2, sel.HitBox.Y + sel.HitBox.Height / 2);
                clicked = true;
            }
            // 「選擇」OCR 常挂：区分双卡事件 / 三卡加入奖励
            if (!clicked)
            {
                var threePick = context.OCR("還有1次", image, out _) ||
                                context.OCR("还有1次", image, out _) ||
                                context.OCR("角色加入獎勵", image, out _) ||
                                context.OCR("加入獎勵", image, out _);
                // 坐标必须在 1280x720 内；台服双卡「選擇」约在卡片下半 y≈560–640
                int[][] pts = threePick
                    ? new[]
                    {
                        new[] { 256, 600 }, new[] { 640, 600 }, new[] { 1024, 600 },
                        new[] { 320, 640 }, new[] { 640, 640 }, new[] { 960, 640 },
                        new[] { 256, 560 }, new[] { 640, 560 }, new[] { 1024, 560 }
                    }
                    : new[]
                    {
                        new[] { 400, 620 }, new[] { 450, 600 }, new[] { 380, 640 },
                        new[] { 880, 620 }, new[] { 440, 560 }
                    };
                foreach (var p in pts)
                {
                    LoggerService.LogInfo($"[DawnRealm] event click @ {p[0]},{p[1]} three={threePick}");
                    context.Click(p[0], p[1]);
                    Thread.Sleep(450);
                    var img2 = context.GetImage();
                    // 必须确认奖励/事件特征消失，不能只靠「出现事件」
                    if (!context.OCR("出现事件", img2, out _) &&
                        !context.OCR("出現事件", img2, out _) &&
                        !context.OCR("還有1次", img2, out _) &&
                        !context.OCR("还有1次", img2, out _) &&
                        !context.OCR("角色加入獎勵", img2, out _))
                    {
                        clicked = true;
                        break;
                    }
                }
            }
            Thread.Sleep(900);
            image = context.GetImage();
            if (context.OCR("確定", image, out hit) || context.OCR("确定", image, out hit) ||
                context.OCR("OK", image, out hit))
            {
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            }
            Thread.Sleep(800);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 角色加入确认弹窗
        if (context.OCR("成為了你的夥伴", image, out hit) ||
            context.OCR("成为了你的伙伴", image, out hit) ||
            context.OCR("成為了夥伴", image, out hit) ||
            context.OCR("成为了夥伴", image, out hit) ||
            context.OCR("成為了你的", image, out hit) ||
            context.OCR("以下角色", image, out hit, 300, 80, 680, 200) ||
            (context.OCR("角色加入", image, out hit, 300, 40, 680, 160) &&
             !context.OCR("角色加入分數", image, out _) &&
             !context.OCR("角色加入獎勵", image, out _) &&
             !context.OCR("還有1次", image, out _) &&
             !context.OCR("選擇", image, out _, 80, 480, 1120, 200)))
        {
            LoggerService.LogInfo("[DawnRealm] close join confirm");
            if (context.OCR("關閉", image, out hit, 200, 450, 880, 220) ||
                context.OCR("关闭", image, out hit, 200, 450, 880, 220))
            {
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            }
            else
            {
                context.Click(640, 620);
                context.PressKey(4);
            }
            Thread.Sleep(700);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 致命弹窗：無法使用此操作 → 回标题重进
        if (context.OCR("無法使用此操作", image, out hit) ||
            context.OCR("无法使用此操作", image, out hit) ||
            context.OCR("前往標題", image, out hit) ||
            context.OCR("前往标题", image, out hit))
        {
            LoggerService.LogInfo("[DawnRealm] fatal dialog -> title");
            _formTeamPass = 0;
            _teamFillDoneMask = 0;
            _teamFillAttempts = 0;
            _seenBattle = false;
            if (context.OCR("前往標題", image, out hit) || context.OCR("前往标题", image, out hit))
            {
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            }
            else
            {
                context.Click(640, 480);
            }
            Thread.Sleep(2000);
            context.OverrideNext(args.NodeName, ["黎明界启动探测", "黎明界进入冒险"]);
            return true;
        }

        // FAILED 结算：优先「再次挑戰」；连续失败才「結束」后撤队
        // 勿单凭 FAILED 字样：战斗中艺术字/伤害会误触发导致乱点
        var retryBtn = context.OCR("再次挑戰", image, out hit) ||
                       context.OCR("再次挑战", image, out hit);
        var failedWord = context.OCR("FAILED", image, out _, 400, 40, 480, 160);
        var endBtn = context.OCR("結束", image, out var endHit, 400, 600, 400, 120) ||
                     context.OCR("结束", image, out endHit, 400, 600, 400, 120);
        if (retryBtn || (failedWord && endBtn))
        {
            _formTeamPass = 0;
            _teamFillDoneMask = 0;
            _teamFillAttempts = 0;
            _seenBattle = false;
            _expectChallenge = false;

            // 一律「再次挑戰」：通关前不走無獎勵撤队
            _failRetryCount++;
            _withdrawNoReward = false;
            LoggerService.LogInfo(
                $"[DawnRealm] FAILED -> 再次挑戰 ({_failRetryCount}, no-withdraw)");
            if (retryBtn && hit.HitBox != null)
            {
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2,
                    hit.HitBox.Y + hit.HitBox.Height / 2);
            }
            else
            {
                // 1600≈(1270,820) →1280≈(1016,656)
                context.Click(1016, 656);
            }
            Thread.Sleep(1500);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 战斗结算 / WIN / 獲得道具（台服艺术字 OCR 常失败，靠多关键词 + 坐标兜底）
        var postBattle = context.OCR("WIN", image, out hit) ||
                         context.OCR("WIN!", image, out hit) ||
                         context.OCR("下一步", image, out hit) ||
                         context.OCR("傷害報告", image, out hit) ||
                         context.OCR("伤害报告", image, out hit) ||
                         context.OCR("獲得道具", image, out hit, 200, 40, 880, 220) ||
                         context.OCR("获得道具", image, out hit, 200, 40, 880, 220);
        if (!postBattle && _seenBattle &&
            !context.OCR("AUTO", image, out _, 1000, 600, 260, 100) &&
            !context.OCR("自動", image, out _, 1000, 600, 260, 100) &&
            !context.OCR("編組隊伍", image, out _) &&
            !context.OCR("當前的成員", image, out _) &&
            !context.OCR("撤退", image, out _) &&
            !context.OCR("區域", image, out _) &&
            !context.OCR("选择角色", image, out _) &&
            !context.OCR("選擇角色", image, out _))
        {
            postBattle = true;
            LoggerService.LogInfo("[DawnRealm] post-battle by battle-flag");
        }
        if (postBattle)
        {
            LoggerService.LogInfo("[DawnRealm] post-battle next");
            _seenBattle = false;
            _withdrawNoReward = false;
            _failRetryCount = 0;
            _teamFillDoneMask = 0;
            if (context.OCR("下一步", image, out hit))
            {
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            }
            else
            {
                // 1280x720：右下「下一步」；多点一次防漏
                foreach (var p in new[] { (1100, 660), (1140, 680), (1080, 640) })
                {
                    context.Click(p.Item1, p.Item2);
                    Thread.Sleep(200);
                }
            }
            Thread.Sleep(900);
            context.OverrideNext(args.NodeName, ["黎明界地图循环", "黎明界结算"]);
            return true;
        }

        // 战斗中：仅认 AUTO/自動（地图也有 MENU，不能用来判断战斗）
        if (context.OCR("AUTO", image, out hit, 1000, 600, 260, 100) ||
            context.OCR("自動", image, out hit, 1000, 600, 260, 100))
        {
            _seenBattle = true;
            _formTeamPass = 0;
            LoggerService.LogInfo("[DawnRealm] in battle, wait");
            Thread.Sleep(1200);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 宝箱开封/奖励结果：点 OK，勿当编队
        if (context.OCR("寶箱開封", image, out _) || context.OCR("宝箱开封", image, out _) ||
            context.OCR("開封了", image, out _) || context.OCR("开封了", image, out _) ||
            context.OCR("帶回的寶箱", image, out _) || context.OCR("带回的宝箱", image, out _))
        {
            LoggerService.LogInfo("[DawnRealm] chest result -> OK");
            if (context.OCR("OK", image, out hit, 400, 500, 480, 200))
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            else
                context.Click(640, 620);
            Thread.Sleep(1000);
            context.OverrideNext(args.NodeName, ["黎明界地图循环", "黎明界页首"]);
            return true;
        }

        // 结束确认/FAILED 弹窗优先：勿把战败结算误判成编队（彩色像素会虚高）
        var failedBanner = context.OCR("FAILED", image, out _, 350, 20, 580, 200);
        var endConfirmUi = context.OCR("結束確認", image, out _) ||
                           context.OCR("结束确认", image, out _) ||
                           failedBanner ||
                           ((context.OCR("撤隊", image, out _) || context.OCR("撤队", image, out _) ||
                             context.OCR("無獎勵", image, out _)) &&
                            context.OCR("取消", image, out _, 200, 400, 450, 220));
        // 战败结束确认：一律点「撤隊(無獎勵)」（含文案误触导致 flag 丢失的情况）
        if (endConfirmUi)
        {
            var hasConfirmTitle = context.OCR("結束確認", image, out _) || context.OCR("结束确认", image, out _);
            // 只在底栏按钮带找「撤隊」；勿用正文「無獎勵」文案（会点到对话框中央）
            var hasWithdrawBtn =
                context.OCR("撤隊", image, out hit, 350, 470, 500, 120) ||
                context.OCR("撤队", image, out hit, 350, 470, 500, 120);
            var dialogOpen = hasConfirmTitle || hasWithdrawBtn || LooksLikeEndConfirmDialog(context);

            if (dialogOpen)
            {
                // 通关中：误开结束确认一律「取消」，绝不点無獎勵撤隊
                if (!_withdrawNoReward)
                {
                    LoggerService.LogInfo("[DawnRealm] end-confirm -> 取消 (keep run)");
                    if (context.OCR("取消", image, out hit, 200, 400, 450, 220))
                        context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
                    else
                        context.Click(420, 500);
                    Thread.Sleep(800);
                    context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
                    return true;
                }

                // 仅显式无奖励撤队流程才确认
                if (context.OCR("OK", image, out hit, 600, 470, 500, 160) ||
                    context.OCR("確定", image, out hit, 600, 470, 500, 160) ||
                    context.OCR("确定", image, out hit, 600, 470, 500, 160))
                {
                    var cx = hit.HitBox.X + hit.HitBox.Width / 2;
                    var cy = hit.HitBox.Y + hit.HitBox.Height / 2;
                    LoggerService.LogInfo($"[DawnRealm] end-confirm OK @{cx},{cy}");
                    context.Click(cx, cy);
                }
                else if (hasWithdrawBtn && hit.HitBox != null && hit.HitBox.Y > 450)
                {
                    var cx = hit.HitBox.X + hit.HitBox.Width / 2;
                    var cy = hit.HitBox.Y + hit.HitBox.Height / 2;
                    LoggerService.LogInfo($"[DawnRealm] click 撤隊 OCR @{cx},{cy}");
                    context.Click(cx, cy);
                }
                else if (LooksLikeEndConfirmOkOnly(context))
                {
                    LoggerService.LogInfo("[DawnRealm] end-confirm OK fallback @786,498");
                    context.Click(786, 498);
                }
                else
                {
                    LoggerService.LogInfo("[DawnRealm] click 撤隊 fallback @641,498");
                    context.Click(641, 498);
                }
                _withdrawNoReward = false;
                Thread.Sleep(1500);
                context.OverrideNext(args.NodeName, ["黎明界地图循环", "黎明界页首", "黎明界回主页"]);
                return true;
            }

            // 仅 FAILED 底栏：永远再次挑戰
            if (context.OCR("再次挑戰", image, out hit) || context.OCR("再次挑战", image, out hit))
            {
                _failRetryCount++;
                _withdrawNoReward = false;
                LoggerService.LogInfo(
                    $"[DawnRealm] FAILED banner -> 再次挑戰 ({_failRetryCount}, no-withdraw)");
                if (hit.HitBox != null)
                    context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
                else
                    context.Click(1016, 656);
                Thread.Sleep(1500);
                context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
                return true;
            }

            // 无重试钮也别点結束撤队：点屏幕中部跳过动画后再判
            LoggerService.LogInfo("[DawnRealm] FAILED banner no retry btn -> wait/tap center");
            context.Click(800, 450);
            Thread.Sleep(1000);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 戰鬥格子页优先于编队：新 UI「屬性獎勵有效/進行挑戰」易误触发编队 OCR
        var onChallengePage = context.OCR("戰鬥格子", image, out _) ||
            context.OCR("战斗格子", image, out _) ||
            context.OCR("進行挑戰", image, out _) ||
            context.OCR("进行挑战", image, out _) ||
            context.OCR("屬性獎勵有效", image, out _) ||
            context.OCR("属性奖励有效", image, out _) ||
            (context.OCR("觀看地圖", image, out _) && LooksLikeChallengeBlueButton(context)) ||
            (context.OCR("观看地图", image, out _) && LooksLikeChallengeBlueButton(context));

        // 编队：編組隊伍 → 点角色 → 戰鬥開始（台服标题 OCR 常全挂，辅以肖像区像素）
        var formOcr = context.OCR("編組隊伍", image, out hit) ||
            context.OCR("编组队伍", image, out hit) ||
            context.OCR("當前的成員", image, out hit) ||
            context.OCR("当前的成员", image, out hit) ||
            context.OCR("總戰力", image, out hit) ||
            context.OCR("总战力", image, out hit) ||
            context.OCR("發動屬性獎勵", image, out hit) ||
            context.OCR("发动属性奖励", image, out hit) ||
            context.OCR("對Boss的有效效果", image, out hit) ||
            context.OCR("对Boss的有效效果", image, out hit) ||
            context.OCR("戰鬥開始", image, out hit) ||
            context.OCR("战斗开始", image, out hit) ||
            context.OCR("尚未編組", image, out hit) ||
            context.OCR("尚未编组", image, out hit) ||
            context.OCR("亦可挑戰", image, out hit) ||
            context.OCR("也可挑战", image, out hit) ||
            (context.OCR("隊伍1", image, out hit, 40, 40, 500, 100) &&
             context.OCR("隊伍2", image, out _, 40, 40, 500, 100));
        // 像素编队启发不可单独采信；招募页肖像网格也会 colorful，必须排除
        var formPixels = LooksLikeTeamFormUi(context);
        var formUi = !endConfirmUi && !onRecruit && !onChallengePage &&
                     (formOcr || (formPixels && (LooksLikeMultiTeamTabs(context) ||
                      context.OCR("隊伍1", image, out _, 40, 40, 500, 100))));
        if (formUi)
        {
            LoggerService.LogInfo($"[DawnRealm] form team and start pass={_formTeamPass} ocr={formOcr}");
            // 关掉「有效效果」过滤
            if (context.OCR("有效效果", image, out hit, 400, 80, 500, 80))
            {
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
                Thread.Sleep(300);
            }

            // 是否 Boss 三队：有顶栏三队黄/白标或开战区时绝不能退化成单队
            image = context.GetImage();
            var tabSel = DetectSelectedTeamTab(context);
            // 仅顶栏三队证据才算 Boss 多队；蓝开战钮/底栏白蓝在单队也会命中，勿单独采信
            var multiTeam = (context.OCR("隊伍1", image, out _, 40, 40, 500, 100) &&
                             context.OCR("隊伍2", image, out _, 40, 40, 500, 100)) ||
                            LooksLikeMultiTeamTabs(context) ||
                            tabSel >= 0 ||
                            ((LooksLikeBossMultiTeamFooter(context) || LooksLikeBattleStartButton(context)) &&
                             (context.OCR("尚未編組", image, out _, 800, 640, 480, 80) ||
                              context.OCR("亦可挑戰", image, out _, 800, 640, 480, 80) ||
                              context.OCR("也可挑战", image, out _, 800, 640, 480, 80)));
            LoggerService.LogInfo($"[DawnRealm] multiTeam={multiTeam} tabSel={tabSel}");

            // pass 残留或 mask 不全时禁止空队开战：先退回队1重编
            if (multiTeam && _formTeamPass > 2)
            {
                if (_teamFillDoneMask != 0b111)
                {
                    LoggerService.LogInfo(
                        $"[DawnRealm] stale pass={_formTeamPass} mask=0b{_teamFillDoneMask:b}, refill from team1");
                    _formTeamPass = 0;
                    _teamFillDoneMask = 0;
                    _teamFillAttempts = 0;
                }
                else
                {
                    EnsureOnTeamTab(context, 0);
                    var s1 = CountFilledTeamSlots(context.GetImage());
                    EnsureOnTeamTab(context, 2);
                    var s3 = CountFilledTeamSlots(context.GetImage());
                    if (s1 < 3 || s3 < 3)
                    {
                        LoggerService.LogInfo($"[DawnRealm] pass done but empty s1={s1} s3={s3}, refill");
                        _formTeamPass = 0;
                        _teamFillDoneMask = 0;
                        _teamFillAttempts = 0;
                    }
                }
            }

            // 单队战：只补当前队后开战。Boss：每队尽量 5 人，未满不切下一队
            if (!multiTeam || _formTeamPass <= 2)
            {
                if (multiTeam)
                {
                    // 顶栏旧坐标会误点到隊3，导致隊1/2空却读到隊3的5人「reuse」
                    if (_formTeamPass == 0)
                        _teamFillDoneMask = 0; // 每轮从隊1重确认，清掉误标的 done
                    if (!EnsureOnTeamTab(context, _formTeamPass))
                    {
                        // 黄标识别失败时仍继续补人（已点过页签坐标），避免死循环
                        LoggerService.LogInfo(
                            $"[DawnRealm] team#{_formTeamPass + 1} tab yellow miss, fill anyway");
                    }
                }
                var before = CountFilledTeamSlots(context.GetImage());
                // 隊1/2：仅当黄标明确停在其它队时禁止 reuse；tab=-1(识别失败)时勿死循环拒切
                if (multiTeam && _formTeamPass < 2 && before >= 5)
                {
                    var tab = DetectSelectedTeamTab(context);
                    if (tab >= 0 && tab != _formTeamPass)
                    {
                        LoggerService.LogInfo(
                            $"[DawnRealm] team#{_formTeamPass + 1} slots=5 but tab=team#{tab + 1}, refuse reuse");
                        _teamFillDoneMask &= ~(1 << _formTeamPass);
                        EnsureOnTeamTab(context, _formTeamPass);
                        before = CountFilledTeamSlots(context.GetImage());
                        var tab2 = DetectSelectedTeamTab(context);
                        if (before >= 5 && tab2 >= 0 && tab2 != _formTeamPass)
                        {
                            // 切不过去就跳到下一队/开战，避免无限 refuse
                            LoggerService.LogInfo(
                                $"[DawnRealm] team switch stuck tab={tab2}, advance pass");
                            if (_formTeamPass < 2)
                            {
                                ClickMultiTeamNext(context, _formTeamPass);
                                _formTeamPass++;
                            }
                            else
                                _formTeamPass = 3;
                            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
                            return true;
                        }
                    }
                    else if (tab < 0)
                    {
                        LoggerService.LogInfo(
                            $"[DawnRealm] team#{_formTeamPass + 1} tab unknown, reuse slots={before}");
                    }
                }
                var slots = EnsureTeamFilledToFive(context, multiTeam ? _formTeamPass : 0);
                // 连续多轮补人不涨才算名册耗尽（首轮常因过滤/点空未涨）
                var rosterExhausted = before >= 0 && slots >= 0 && slots <= before && _teamFillAttempts >= 3;
                if (slots >= 0 && slots < 5 && !rosterExhausted && _teamFillAttempts < 5)
                {
                    LoggerService.LogInfo($"[DawnRealm] team#{_formTeamPass + 1} only {slots}/5, retry fill (attempt {_teamFillAttempts})");
                    context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
                    return true;
                }
                if (rosterExhausted)
                    LoggerService.LogInfo($"[DawnRealm] team#{_formTeamPass + 1} roster exhausted at {slots}/5, continue");

                _teamFillDoneMask |= 1 << _formTeamPass;
                _teamFillAttempts = 0;

                if (!multiTeam)
                {
                    LoggerService.LogInfo($"[DawnRealm] single-team form done slots={slots}, battle start");
                    _formTeamPass = 3; // 跳到开战
                }
                else
                {
                    // 未编满三队时只前进切队；隊3 上蓝钮是开战，勿当「下一队」
                    if (_formTeamPass < 2)
                    {
                        if (!ClickMultiTeamNext(context, _formTeamPass))
                            EnsureOnTeamTab(context, _formTeamPass + 1);
                    }
                    _formTeamPass++;
                    if (_formTeamPass <= 2)
                    {
                        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
                        return true;
                    }
                }
            }

            LoggerService.LogInfo("[DawnRealm] form done, battle start");
            image = context.GetImage();
            if (context.OCR("戰鬥開始", image, out hit) || context.OCR("战斗开始", image, out hit))
            {
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
                LoggerService.LogInfo("[DawnRealm] clicked battle start");
            }
            else
            {
                // 1600 实测蓝钮中心≈(1380,770)→1280≈(1104,616)；勿点到 y>640 警告区/更右空白
                foreach (var p in new[]
                         {
                             (1105, 616), (1120, 620), (1090, 610), (1140, 625),
                             (1080, 615), (1110, 630), (1130, 605)
                         })
                {
                    context.Click(p.Item1, p.Item2);
                    Thread.Sleep(120);
                }
                LoggerService.LogInfo("[DawnRealm] clicked battle start (fallback right)");
            }
            Thread.Sleep(3000);
            _formTeamPass = 0;
            _teamFillDoneMask = 0;
            _teamFillAttempts = 0;
            context.OverrideNext(args.NodeName, ["黎明界地图循环", "黎明界战斗中"]);
            return true;
        }

        // 仅在明确回到地图时重置编队进度（编队 OCR 失败时勿清零）
        if (context.OCR("撤退", image, out _) || context.OCR("區域", image, out _, 400, 0, 480, 80))
        {
            _formTeamPass = 0;
            _teamFillAttempts = 0;
            if (!_withdrawNoReward)
                _teamFillDoneMask = 0;
        }

        // 战斗准备：進行挑戰（台服艺术字 OCR 常全挂，辅以蓝钮像素）
        var challengeOcr = context.OCR("戰鬥格子", image, out hit) ||
            context.OCR("战斗格子", image, out hit) ||
            context.OCR("進行挑戰", image, out hit) ||
            context.OCR("进行挑战", image, out hit) ||
            context.OCR("觀看地圖", image, out hit) ||
            context.OCR("观看地图", image, out hit) ||
            context.OCR("屬性獎勵有效", image, out hit) ||
            context.OCR("属性奖励", image, out hit) ||
            context.OCR("屬性加成", image, out hit) ||
            context.OCR("属性加成", image, out hit);
        // 编队页底栏也有白+蓝钮，禁止在编队页误判挑戰
        var challengeUi = !formUi && (challengeOcr || LooksLikeChallengeBlueButton(context));
        if (challengeUi)
        {
            _expectChallenge = false;
            // Boss 已败：回地图撤队，不要再点進行挑戰
            if (_withdrawNoReward)
            {
                LoggerService.LogInfo("[DawnRealm] after fail: 觀看地圖 then withdraw");
                if (context.OCR("觀看地圖", image, out var backMap) ||
                    context.OCR("观看地图", image, out backMap))
                {
                    context.Click(backMap.HitBox.X + backMap.HitBox.Width / 2,
                        backMap.HitBox.Y + backMap.HitBox.Height / 2);
                }
                else
                {
                    context.Click(850, 640); // 觀看地圖约在蓝钮左侧
                }
                Thread.Sleep(1000);
                context.OverrideNext(args.NodeName, ["黎明界失败退出", "黎明界地图循环"]);
                return true;
            }

            LoggerService.LogInfo("[DawnRealm] start challenge");
            // 优先点「進行挑戰」；失败则点「觀看地圖」右侧的蓝按钮
            if (context.OCR("進行挑戰", image, out var go) ||
                context.OCR("进行挑战", image, out go) ||
                context.OCR("挑戰", image, out go, 800, 560, 480, 160) ||
                context.OCR("挑战", image, out go, 800, 560, 480, 160))
            {
                context.Click(go.HitBox.X + go.HitBox.Width / 2, go.HitBox.Y + go.HitBox.Height / 2);
            }
            else if (context.OCR("觀看地圖", image, out var mapBtn) ||
                     context.OCR("观看地图", image, out mapBtn))
            {
                context.Click(mapBtn.HitBox.X + mapBtn.HitBox.Width + 120,
                    mapBtn.HitBox.Y + mapBtn.HitBox.Height / 2);
            }
            else
            {
                // 1600 实测蓝钮中心≈(1400,760)→1280≈(1120,608)；勿点返回有獎勵
                foreach (var p in new[] { (1120, 608), (1140, 615), (1100, 600), (1160, 620) })
                {
                    context.Click(p.Item1, p.Item2);
                    Thread.Sleep(120);
                }
            }
            Thread.Sleep(1200);
            context.OverrideNext(args.NodeName, ["黎明界地图循环", "黎明界战斗中"]);
            return true;
        }

        // 结束/带回奖励确认框
        if (context.OCR("結束確認", image, out hit) ||
            context.OCR("结束确认", image, out hit) ||
            context.OCR("帶回已獲", image, out hit, 200, 180, 880, 240) ||
            context.OCR("带回已获", image, out hit, 200, 180, 880, 240) ||
            context.OCR("結束目前挑戰", image, out hit, 200, 180, 880, 240) ||
            context.OCR("结束目前挑战", image, out hit, 200, 180, 880, 240) ||
            context.OCR("帶回已獲得的獎勵", image, out hit, 200, 180, 880, 240))
        {
            _expectChallenge = false;
            if (_withdrawNoReward)
            {
                // Boss 失败撤队：点「撤隊(無獎勵)」；勿点右侧「返回(有獎勵)」
                LoggerService.LogInfo("[DawnRealm] confirm withdraw(no reward)");
                if (context.OCR("撤隊", image, out hit, 300, 400, 700, 220) ||
                    context.OCR("撤队", image, out hit, 300, 400, 700, 220) ||
                    context.OCR("無獎勵", image, out hit, 300, 400, 700, 220) ||
                    context.OCR("无奖励", image, out hit, 300, 400, 700, 220) ||
                    context.OCR("撤退", image, out hit, 300, 400, 700, 220) ||
                    context.OCR("OK", image, out hit, 400, 400, 350, 220) ||
                    context.OCR("確定", image, out hit, 400, 400, 350, 220) ||
                    context.OCR("确定", image, out hit, 400, 400, 350, 220))
                {
                    context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
                }
                else
                {
                    // 三钮：取消 | 撤隊(無獎勵) | 返回(有獎勵) → 中间偏左
                    context.Click(560, 500);
                }
                _withdrawNoReward = false;
                Thread.Sleep(1000);
                context.OverrideNext(args.NodeName, ["黎明界失败退出", "黎明界回主页", "黎明界页首"]);
                return true;
            }

            // 误触「返回有獎勵」：点取消
            LoggerService.LogInfo("[DawnRealm] cancel early return");
            if (context.OCR("取消", image, out hit, 200, 400, 450, 220))
            {
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            }
            else
            {
                context.Click(420, 500);
            }
            Thread.Sleep(600);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 移动确认：取消+OK / 確認移動目的地 / 連結印記
        var hasCancel = context.OCR("取消", image, out var cancelHit, 250, 420, 400, 180);
        var hasOk = context.OCR("OK", image, out var okHit, 550, 420, 400, 180);
        var moveDlg = context.OCR("確認移動", image, out _) ||
                      context.OCR("确认移动", image, out _) ||
                      context.OCR("移動目的地", image, out _) ||
                      context.OCR("連結印記", image, out _) ||
                      context.OCR("确定吗", image, out _) ||
                      context.OCR("確定嗎", image, out _);
        if ((hasCancel && hasOk &&
             cancelHit.HitBox != null && okHit.HitBox != null &&
             Math.Abs(cancelHit.HitBox.Y - okHit.HitBox.Y) < 80) ||
            (moveDlg && hasOk && okHit.HitBox != null) ||
            moveDlg)
        {
            var looksBattle = context.OCR("戰鬥", image, out _, 300, 180, 680, 220) ||
                              context.OCR("战斗", image, out _, 300, 180, 680, 220) ||
                              context.OCR("移動到戰鬥", image, out _) ||
                              context.OCR("移动到战斗", image, out _) ||
                              context.OCR("Boss", image, out _, 300, 180, 680, 220) ||
                              context.OCR("BOSS", image, out _, 300, 180, 680, 220) ||
                              context.OCR("NORMAL", image, out _, 300, 180, 680, 220) ||
                              context.OCR("EXTREME", image, out _, 300, 180, 680, 220);
            LoggerService.LogInfo($"[DawnRealm] confirm move OK battle={looksBattle} dlg={moveDlg}");
            NoteMapProgress();
            if (hasOk && okHit.HitBox != null)
            {
                context.Click(okHit.HitBox.X + okHit.HitBox.Width / 2,
                    okHit.HitBox.Y + okHit.HitBox.Height / 2);
            }
            else
            {
                context.Click(820, 560); // 对话框右侧 OK
            }
            // 台服确认框内「戰鬥/NORMAL」OCR 常挂：移动成功后一律允许挑戰页坐标兜底
            _expectChallenge = true;
            Thread.Sleep(900);
            context.OverrideNext(args.NodeName, ["黎明界地图循环", "黎明界战斗中"]);
            return true;
        }

        // 仅战斗格移动后才点進行挑戰；禁止点 (1140,680)=返回有獎勵
        if (_expectChallenge &&
            !context.OCR("撤退", image, out _) &&
            !context.OCR("AUTO", image, out _, 1000, 600, 260, 100) &&
            !context.OCR("取消", image, out _, 250, 420, 400, 180))
        {
            LoggerService.LogInfo("[DawnRealm] expect-challenge fallback click");
            foreach (var p in new[] { (1180, 650), (1200, 640), (1100, 620) })
            {
                context.Click(p.Item1, p.Item2);
                Thread.Sleep(150);
            }
            _expectChallenge = false;
            Thread.Sleep(1000);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 弹窗优先于遗物/招募（避免「迷宮遺物效果」被当成选遗物）
        // 注意：不要点底部「返回(有獎勵)」区域 (约 x>900,y>560)
        if (context.OCR("出發獎勵", image, out hit, 200, 20, 880, 220) ||
            context.OCR("出发奖励", image, out hit, 200, 20, 880, 220) ||
            context.OCR("迷宮遺物效果", image, out hit, 200, 20, 880, 220) ||
            context.OCR("遗物效果", image, out hit, 200, 20, 880, 220) ||
            context.OCR("已取得", image, out hit, 200, 60, 880, 220) ||
            context.OCR("已透過", image, out hit, 200, 60, 880, 220) ||
            context.OCR("已透过", image, out hit, 200, 60, 880, 220) ||
            context.OCR("取得道具", image, out hit, 200, 40, 880, 220) ||
            context.OCR("獲得道具", image, out hit, 200, 40, 880, 220) ||
            context.OCR("获得道具", image, out hit, 200, 40, 880, 220) ||
            context.OCR("獲得寶箱", image, out hit, 200, 40, 880, 220) ||
            context.OCR("获得宝箱", image, out hit, 200, 40, 880, 220) ||
            context.OCR("寶箱開封", image, out hit, 200, 40, 880, 220) ||
            context.OCR("开封结果", image, out hit, 200, 40, 880, 220) ||
            context.OCR("已經領取", image, out hit, 200, 80, 880, 200) ||
            context.OCR("已经领取", image, out hit, 200, 80, 880, 200))
        {
            LoggerService.LogInfo("[DawnRealm] close reward/effect popup");
            if (context.OCR("關閉", image, out hit, 200, 400, 880, 280) ||
                context.OCR("关闭", image, out hit, 200, 400, 880, 280) ||
                context.OCR("OK", image, out hit, 400, 400, 480, 250))
            {
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
                Thread.Sleep(500);
            }
            else
            {
                // 「關閉」OCR 常失败：沿弹窗中线多点，避开右下返回
                foreach (var yy in new[] { 500, 530, 560, 590, 470 })
                {
                    context.Click(640, yy);
                    Thread.Sleep(120);
                }
                // 仍关不掉时用 BACK（台服弹窗通常可退）
                context.PressKey(4);
            }
            Thread.Sleep(700);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 遗物 / 招募 / 商店界面分流
        if (context.OCR("選擇遺物", image, out hit, 200, 0, 880, 160) ||
            context.OCR("选择遗物", image, out hit, 200, 0, 880, 160) ||
            (context.OCR("遺物", image, out hit, 0, 0, 1280, 160) &&
             !context.OCR("效果", image, out _, 200, 40, 880, 200)))
        {
            context.OverrideNext(args.NodeName, ["黎明界选遗物"]);
            return true;
        }

        if (context.OCR("招募", image, out hit, 0, 0, 1280, 200) ||
            context.OCR("選擇夥伴", image, out hit, 200, 0, 880, 160) ||
            context.OCR("选择伙伴", image, out hit, 200, 0, 880, 160) ||
            context.OCR("選擇角色", image, out hit, 200, 0, 880, 160) ||
            context.OCR("选择角色", image, out hit, 200, 0, 880, 160) ||
            context.OCR("進行邀請", image, out hit, 900, 600, 360, 120) ||
            context.OCR("邀请为夥伴", image, out hit, 200, 40, 880, 120) ||
            context.OCR("邀請為夥伴", image, out hit, 200, 40, 880, 120))
        {
            LoggerService.LogInfo("[DawnRealm] -> recruit");
            context.OverrideNext(args.NodeName, ["黎明界选角色"]);
            return true;
        }

        if (context.OCR("商店", image, out hit, 200, 0, 880, 120) ||
            context.OCR("始源盧幣", image, out hit, 200, 560, 880, 160) ||
            context.OCR("始源卢币", image, out hit, 200, 560, 880, 160) ||
            (context.OCR("購買", image, out _, 80, 200, 1120, 400) &&
             context.OCR("關閉", image, out _, 800, 560, 460, 160)))
        {
            LoggerService.LogInfo("[DawnRealm] -> shop");
            context.OverrideNext(args.NodeName, ["黎明界商店"]);
            return true;
        }

        // 无法移动提示
        if (context.OCR("無法移動", image, out hit) ||
            context.OCR("无法移动", image, out hit) ||
            context.OCR("無法移到", image, out hit) ||
            context.OCR("無法移動到這格", image, out hit))
        {
            LoggerService.LogInfo("[DawnRealm] dismiss unreachable toast");
            // 点掉 toast，并把刚点位置拉黑，避免死循环
            context.Click(640, 360);
            Thread.Sleep(200);
            context.Click(640, 300);
            Thread.Sleep(500);
            if (_lastMapClickX > 0)
                _deadNodes.Add((_lastMapClickX, _lastMapClickY, Environment.TickCount + 25000));
            _sameMapClickCount = 99;
            _lastMapClickX = -999;
            _lastMapClickY = -999;
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 仅在迷宫地图内点节点。勿用裸 NORMAL/EVENT：挑戰格子标题也会命中，导致误进地图导航。
        // 排除选角/事件页。
        var notMapOverlay = context.OCR("選擇角色", image, out _, 200, 0, 880, 120) ||
                            context.OCR("出現事件", image, out _) ||
                            context.OCR("角色加入獎勵", image, out _) ||
                            context.OCR("還有1次", image, out _) ||
                            LooksLikeChallengeBlueButton(context);
        var onMap = !notMapOverlay && (
                    context.OCR("撤退", image, out _) ||
                    context.OCR("無獎勵", image, out _) ||
                    context.OCR("无奖励", image, out _) ||
                    context.OCR("黎明界迷宮", image, out _, 0, 0, 400, 80) ||
                    context.OCR("区域", image, out _, 400, 0, 480, 80) ||
                    context.OCR("區域", image, out _, 400, 0, 480, 80));
        if (onMap)
        {
            _seenBattle = false;
            if (_withdrawNoReward)
            {
                LoggerService.LogInfo("[DawnRealm] on map after boss fail -> 撤退(无奖励)");
                if (context.OCR("撤退", image, out hit) ||
                    context.OCR("返回", image, out hit, 900, 560, 380, 160) ||
                    context.OCR("無獎勵", image, out hit) ||
                    context.OCR("无奖励", image, out hit))
                {
                    context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
                }
                else
                {
                    // 地图右下「返回/撤退」区域
                    context.Click(1140, 680);
                }
                Thread.Sleep(900);
                context.OverrideNext(args.NodeName, ["黎明界失败退出", "黎明界确认撤退"]);
                return true;
            }

            // 以人物为基准：右侧下一列可走格
            if (TryClickNextColumnFromChar(context))
            {
                NoteMapProgress();
                Thread.Sleep(900);
                context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
                return true;
            }

            // OCR 标签兜底（禁止点人物左侧/同列已走 EVENT）
            string[] labels =
            [
                "FINAL", "BOSS", "SHOP", "商店", "NORMAL", "小怪", "戰鬥", "战斗",
                "遗物", "寶箱", "宝箱", "招募", "公主", "印記", "印记", "EVENT", "事件"
            ];
            foreach (var label in labels)
            {
                if (TryClickLabel(context, label))
                {
                    Thread.Sleep(1200);
                    var after = context.GetImage();
                    var moved = IsMoveConfirmUi(after, context);
                    if (!moved)
                    {
                        _deadNodes.Add((_lastMapClickX, _lastMapClickY, Environment.TickCount + 45000));
                        LoggerService.LogInfo($"[DawnRealm] mark dead node @{_lastMapClickX},{_lastMapClickY}");
                        NoteMapStuck("label-miss", $"'{label}' @{_lastMapClickX},{_lastMapClickY}");
                    }
                    else
                    {
                        NoteMapProgress();
                    }
                    context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
                    return true;
                }
            }

            NoteMapStuck("ocr-miss", "no next-col / label hit");
            Thread.Sleep(600);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }
        else
        {
            // 禁止盲点右下 (1140,680)：地图上是「返回有獎勵」，会提前结算
            // OCR 常全挂：先区分「進行挑戰」页 vs 编队页，再坐标操作
            // 战斗中 AUTO OCR 偶发漏检时勿进编队逻辑（会点到 MENU/SET）
            if (_seenBattle)
            {
                LoggerService.LogInfo("[DawnRealm] off-map skip while battle-flag");
                Thread.Sleep(800);
                context.OverrideNext(args.NodeName, ["黎明界地图循环", "黎明界战斗中"]);
                return true;
            }
            if (!context.OCR("撤退", image, out _) &&
                !context.OCR("返回", image, out _, 900, 560, 380, 160))
            {
                // 招募选角绝不是编队：点邀请，不要 off-map 补人/开战
                if (LooksLikeRecruitUi(context) ||
                    context.OCR("選擇角色", image, out _, 200, 0, 1000, 140) ||
                    context.OCR("進行邀請", image, out _, 850, 560, 400, 160))
                {
                    LoggerService.LogInfo("[DawnRealm] off-map is recruit, hand off");
                    context.OverrideNext(args.NodeName, ["黎明界选角色"]);
                    return true;
                }

                // Boss戰鬥格子：底部「觀看地圖」(白) + 「進行挑戰」(蓝) — 勿当编队页乱点角色
                // 1600 蓝钮≈(1361,769) →1280≈(1089,615)
                LoggerService.LogInfo("[DawnRealm] off-map: try challenge entry first");
                foreach (var p in new[] { (1090, 615), (1080, 620), (1100, 610), (1050, 600) })
                {
                    context.Click(p.Item1, p.Item2);
                    Thread.Sleep(200);
                }
                Thread.Sleep(900);
                image = context.GetImage();

                // Boss 3 队：每队优先光→火→全部补满；单击补人（勿二次点已选）。
                // 开战按钮 1600≈(1420,755) →1280≈(1136,604)。已就绪时先开战，避免补人点取消。
                LoggerService.LogInfo($"[DawnRealm] off-map: boss 3-team form pass={_formTeamPass}");
                void ClickBattleStart()
                {
                    foreach (var p in new[]
                             {
                                 (1140, 605), (1136, 600), (1150, 610),
                                 (1120, 595), (1160, 615), (1140, 620)
                             })
                    {
                        context.Click(p.Item1, p.Item2);
                        Thread.Sleep(250);
                    }
                }

                // 三队都编过后再开战；mask/槽位不对则清零重编，勿空队开战
                if (_formTeamPass >= 3)
                {
                    if (_teamFillDoneMask != 0b111)
                    {
                        LoggerService.LogInfo(
                            $"[DawnRealm] off-map stale pass mask=0b{_teamFillDoneMask:b}, reset fill");
                        _formTeamPass = 0;
                        _teamFillDoneMask = 0;
                        _teamFillAttempts = 0;
                    }
                    else
                    {
                        LoggerService.LogInfo("[DawnRealm] 3-team done, battle-start");
                        ClickBattleStart();
                        Thread.Sleep(1600);
                        var after = context.GetImage();
                        if (context.OCR("AUTO", after, out _, 1000, 600, 260, 100) ||
                            context.OCR("自動", after, out _, 1000, 600, 260, 100))
                        {
                            _formTeamPass = 0;
                            _teamFillDoneMask = 0;
                            _teamFillAttempts = 0;
                            _seenBattle = true;
                            context.OverrideNext(args.NodeName, ["黎明界地图循环", "黎明界战斗中"]);
                            return true;
                        }
                        ClickBattleStart();
                        Thread.Sleep(1200);
                        context.OverrideNext(args.NodeName, ["黎明界地图循环", "黎明界战斗中"]);
                        return true;
                    }
                }

                if (_formTeamPass <= 2)
                {
                    var offImg = context.GetImage();
                    var multi = context.OCR("隊伍1", offImg, out _, 40, 40, 500, 100) &&
                                context.OCR("隊伍2", offImg, out _, 40, 40, 500, 100);
                    if (multi)
                        SelectTeamTab(context, _formTeamPass);
                    var before = CountFilledTeamSlots(context.GetImage());
                    var slots = EnsureTeamFilledToFive(context, multi ? _formTeamPass : 0);
                    var rosterExhausted = before >= 0 && slots >= 0 && slots <= before && _teamFillAttempts >= 1;
                    if (slots >= 0 && slots < 5 && !rosterExhausted && _teamFillAttempts < 4)
                    {
                        LoggerService.LogInfo($"[DawnRealm] off-map team#{_formTeamPass + 1} {slots}/5, retry");
                        context.OverrideNext(args.NodeName, ["黎明界启动探测", "黎明界地图循环"]);
                        return true;
                    }
                    _teamFillDoneMask |= 1 << _formTeamPass;
                    _teamFillAttempts = 0;
                    if (!multi)
                    {
                        LoggerService.LogInfo($"[DawnRealm] off-map single-team slots={slots}, battle-start");
                        _formTeamPass = 3;
                    }
                    else
                    {
                        context.Click(1080, 615); // 底栏下一队
                        Thread.Sleep(700);
                        _formTeamPass++;
                    }
                }
            }
            else
            {
                LoggerService.LogInfo("[DawnRealm] off-map on map-chrome, re-probe only");
            }
            context.OverrideNext(args.NodeName, ["黎明界启动探测", "黎明界地图循环"]);
            return true;
        }

        // 勿点右上角「跳過」(会进扫荡)。只关真正的弹窗关闭键
        if (context.OCR("關閉", image, out hit, 200, 420, 880, 220) ||
            context.OCR("关闭", image, out hit, 200, 420, 880, 220) ||
            context.OCR("知道了", image, out hit, 0, 400, 1280, 300))
        {
            var cx = hit.HitBox.X + hit.HitBox.Width / 2;
            var cy = hit.HitBox.Y + hit.HitBox.Height / 2;
            if (cx > 900 && cy > 560)
            {
                LoggerService.LogInfo("[DawnRealm] skip risky bottom-right hit");
            }
            else
            {
                context.Click(cx, cy);
                Thread.Sleep(400);
            }
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        // 兜底确认 / OK（禁止点「結束確認」里的 OK）
        if (!context.OCR("結束確認", image, out _) &&
            !context.OCR("结束确认", image, out _) &&
            !context.OCR("帶回已獲", image, out _, 200, 200, 880, 200) &&
            (context.OCR("OK", image, out hit, 500, 400, 500, 250) ||
             context.OCR("挑戰", image, out hit, 0, 500, 1280, 200) ||
             context.OCR("挑战", image, out hit, 0, 500, 1280, 200) ||
             context.OCR("進入", image, out hit, 0, 500, 1280, 200) ||
             context.OCR("进入", image, out hit, 0, 500, 1280, 200) ||
             context.OCR("確定", image, out hit, 0, 500, 1280, 200) ||
             context.OCR("确定", image, out hit, 0, 500, 1280, 200) ||
             context.OCR("下一步", image, out hit, 0, 500, 1280, 200)))
        {
            context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
            Thread.Sleep(500);
            context.OverrideNext(args.NodeName, ["黎明界地图循环", "黎明界战斗中"]);
            return true;
        }

        // 兜底：轻度等待，避免误点返回
        Thread.Sleep(500);
        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
        return true;
    }

    // 顶栏三队中心（1280）；来自 1600 截图文字中心
    private static readonly int[] TeamTabXs = [150, 290, 400];

    /// <summary>
    /// 顶栏隊伍1/2/3。tabs 截图：文字中心 native≈(180,140)/(360,140)/(500,140)→1280≈(144,112)/(288,112)/(400,112)。
    /// </summary>
    private static void SelectTeamTab(IMaaContext context, int teamIndex)
    {
        var x = TeamTabXs[Math.Clamp(teamIndex, 0, 2)];
        foreach (var y in new[] { 112, 105, 118 })
        {
            context.Click(x, y);
            Thread.Sleep(180);
        }
        Thread.Sleep(500);
    }

    /// <summary>读顶栏哪一队是黄底选中；失败返回 -1。</summary>
    private static int DetectSelectedTeamTab(IMaaContext context)
    {
        try
        {
            if (!TryGetBitmap(context, out var bmpType, out var bmpObj, out var getPixel, out var width, out var height))
                return -1;
            using ((IDisposable)bmpObj)
            {
                var sx = width / 1280.0;
                var sy = height / 720.0;
                var best = -1;
                var bestScore = 0;
                for (var i = 0; i < 3; i++)
                {
                    var score = 0;
                    var lx = TeamTabXs[i];
                    foreach (var ly in new[] { 108, 112, 116, 120 })
                    foreach (var dx in new[] { -16, -8, 0, 8, 16 })
                    {
                        var c = getPixel.Invoke(bmpObj, [
                            Math.Clamp((int)((lx + dx) * sx), 0, width - 1),
                            Math.Clamp((int)(ly * sy), 0, height - 1)
                        ])!;
                        var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                        var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                        var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                        // 选中黄底 ≈ (255,230,90)~(255,250,116)
                        if (r > 180 && g > 140 && b < 180 && r > b + 30 && g > b)
                            score++;
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = i;
                    }
                    LoggerService.LogInfo($"[DawnRealm] tab-yellow team#{i + 1} score={score}");
                }
                if (bestScore >= 2)
                {
                    LoggerService.LogInfo($"[DawnRealm] tab-yellow selected=team#{best + 1} score={bestScore}");
                    return best;
                }
            }
        }
        catch { /* ignore */ }
        return -1;
    }

    /// <summary>是否在指定队伍页：黄标顶栏 > 底栏名 > 开战红字。</summary>
    private static bool IsOnTeamTab(IMaaContext context, int teamIndex)
    {
        var yellow = DetectSelectedTeamTab(context);
        if (yellow >= 0)
            return yellow == teamIndex;

        var image = context.GetImage();
        for (var i = 0; i < 3; i++)
        {
            var lab = $"隊伍{i + 1}";
            var lab2 = $"队伍{i + 1}";
            if (context.OCR(lab, image, out _, 40, 520, 300, 90) ||
                context.OCR(lab2, image, out _, 40, 520, 300, 90))
                return i == teamIndex;
        }
        var hasBattleStart = context.OCR("戰鬥開始", image, out _, 900, 560, 380, 160) ||
                             context.OCR("战斗开始", image, out _, 900, 560, 380, 160) ||
                             LooksLikeBattleStartButton(context);
        if (teamIndex == 2)
            return hasBattleStart;
        if (hasBattleStart)
            return false;
        return false;
    }

    /// <summary>隊3→隊2→隊1：底栏回退两次 + 顶栏點隊1（不做 OCR，避免卡死）。</summary>
    private static void RetreatToTeam1(IMaaContext context)
    {
        context.Click(931, 625);
        Thread.Sleep(500);
        context.Click(931, 625);
        Thread.Sleep(500);
        SelectTeamTab(context, 0);
        Thread.Sleep(400);
    }

    private static bool EnsureOnTeamTab(IMaaContext context, int teamIndex)
    {
        teamIndex = Math.Clamp(teamIndex, 0, 2);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var sel = DetectSelectedTeamTab(context);
            var slots = CountFilledTeamSlots(context.GetImage());
            if (sel == teamIndex)
            {
                LoggerService.LogInfo($"[DawnRealm] on team#{teamIndex + 1} ok (yellow) slots={slots}");
                return true;
            }
            // 隊1/2 为空时：槽位 0 且黄标不是隊3，也算切成功
            if (teamIndex < 2 && slots == 0 && sel != 2)
            {
                LoggerService.LogInfo($"[DawnRealm] on team#{teamIndex + 1} ok (empty slots) tab={sel}");
                return true;
            }

            LoggerService.LogInfo(
                $"[DawnRealm] switch -> team#{teamIndex + 1} attempt={attempt + 1} tab={sel} slots={slots}");

            if (teamIndex == 0)
            {
                if (sel == 2 || sel < 0)
                    RetreatToTeam1(context);
                else
                    SelectTeamTab(context, 0);
            }
            else if (teamIndex == 1)
            {
                if (sel == 2 || sel < 0)
                    RetreatToTeam1(context);
                SelectTeamTab(context, 1);
                if (DetectSelectedTeamTab(context) != 1)
                    ClickMultiTeamNext(context, 0);
            }
            else
            {
                SelectTeamTab(context, 2);
                if (DetectSelectedTeamTab(context) != 2)
                {
                    RetreatToTeam1(context);
                    ClickMultiTeamNext(context, 0);
                    ClickMultiTeamNext(context, 1);
                }
            }
            Thread.Sleep(400);
        }

        var finalSel = DetectSelectedTeamTab(context);
        var finalSlots = CountFilledTeamSlots(context.GetImage());
        var ok = finalSel == teamIndex || (teamIndex < 2 && finalSlots == 0 && finalSel != 2);
        LoggerService.LogInfo(
            $"[DawnRealm] switch team#{teamIndex + 1} result={ok} tab={finalSel} slots={finalSlots}");
        return ok;
    }

    /// <summary>隊1/2 底栏蓝钮=下一队；隊3 蓝钮=开战（不能当 next）。</summary>
    private static bool ClickMultiTeamNext(IMaaContext context, int fromTeamIndex)
    {
        if (fromTeamIndex >= 2)
            return false;
        var image = context.GetImage();
        if (context.OCR("隊伍3", image, out var hit, 900, 560, 360, 160) ||
            context.OCR("队伍3", image, out hit, 900, 560, 360, 160) ||
            context.OCR("隊伍2", image, out hit, 900, 560, 360, 160) ||
            context.OCR("队伍2", image, out hit, 900, 560, 360, 160))
        {
            if (hit.HitBox != null && hit.HitBox.Y > 500 && hit.HitBox.X > 1000)
            {
                LoggerService.LogInfo($"[DawnRealm] multi-team next OCR from#{fromTeamIndex + 1}");
                context.Click(hit.HitBox.X + hit.HitBox.Width / 2, hit.HitBox.Y + hit.HitBox.Height / 2);
                Thread.Sleep(700);
                return true;
            }
        }
        // 隊1/2 右侧蓝「下一队」约 (1116,615)；勿点开战区
        context.Click(1116, 615);
        Thread.Sleep(700);
        LoggerService.LogInfo($"[DawnRealm] multi-team next coord from#{fromTeamIndex + 1}");
        return true;
    }

    /// <summary>
    /// 隊3 页：开战蓝钮 + 下方红字提示。隊1/2 右下也是蓝钮（下一队），不能单靠蓝色判断。
    /// </summary>
    private static bool LooksLikeBattleStartButton(IMaaContext context)
    {
        try
        {
            if (!TryGetBitmap(context, out var bmpType, out var bmpObj, out var getPixel, out var width, out var height))
                return false;
            using ((IDisposable)bmpObj)
            {
                var sx = width / 1280.0;
                var sy = height / 720.0;
                int Sample(int lx, int ly, out int r, out int g, out int b)
                {
                    var c = getPixel.Invoke(bmpObj, [
                        Math.Clamp((int)(lx * sx), 0, width - 1),
                        Math.Clamp((int)(ly * sy), 0, height - 1)
                    ])!;
                    r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                    g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                    b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                    return 1;
                }

                var blue = 0;
                foreach (var (lx, ly) in new[] { (1105, 616), (1120, 620), (1090, 610), (1135, 618) })
                {
                    Sample(lx, ly, out var r, out var g, out var b);
                    if (b > 140 && b > r + 40 && r < 100)
                        blue++;
                }
                // 隊3 特有红字「即使仍有隊伍尚未編組…」约 y=656~668
                var red = 0;
                foreach (var (lx, ly) in new[] { (1024, 664), (976, 668), (904, 660), (1100, 656) })
                {
                    Sample(lx, ly, out var r, out var g, out var b);
                    if (r > 200 && g < 160 && b < 160)
                        red++;
                }
                return blue >= 2 && red >= 2;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>补人直到 5 人或尝试耗尽；返回当前槽位数（未知则 -1）。</summary>
    private static int EnsureTeamFilledToFive(IMaaContext context, int teamIndex)
    {
        if ((_teamFillDoneMask & (1 << teamIndex)) != 0)
        {
            var image = context.GetImage();
            // 必须确认当前页就是该队，否则会把隊3的满编当成隊1/2 reuse
            if (!IsOnTeamTab(context, teamIndex))
            {
                LoggerService.LogInfo($"[DawnRealm] team#{teamIndex + 1} mask set but wrong tab, clear & refill");
                _teamFillDoneMask &= ~(1 << teamIndex);
            }
            else
            {
                var n = CountFilledTeamSlots(image);
                LoggerService.LogInfo($"[DawnRealm] team#{teamIndex + 1} marked done, slots={n}");
                return n >= 0 ? n : 5;
            }
        }

        _teamFillAttempts++;
        var filled = FillOneTeamSameElement(context, teamIndex);
        var after = CountFilledTeamSlots(context.GetImage());
        // 防止检测抖动：补人过程已降到 0 却又读成 5
        if (filled >= 0 && after >= 0 && Math.Abs(after - filled) >= 3)
        {
            LoggerService.LogInfo($"[DawnRealm] team#{teamIndex + 1} slot jitter {filled}->{after}, trust fill={filled}");
            after = filled;
        }
        LoggerService.LogInfo($"[DawnRealm] team#{teamIndex + 1} ensure result slots={after}/5 attempt={_teamFillAttempts}");
        return after;
    }

    /// <summary>
    /// 编满当前队伍到 5 人。三队统一：先光 → 再火 → 最后「全部」。
    /// 沿用配队：已满 5 不点；未满只点未点过的肖像（同一格不点第二次）。
    /// 返回结束后的槽位数。
    /// </summary>
    private static int FillOneTeamSameElement(IMaaContext context, int teamIndex)
    {
        // 只走 光→火→全部，避免点穿其它属性把已选人取消
        string[] prefer = ["光", "火", "全部"];

        var image = context.GetImage();
        DawnRealmCharCatalog.CaptureVisiblePortraits(image, $"t{teamIndex + 1}");
        var power = ReadTeamPower(context, image);
        var filled = CountFilledTeamSlots(image);
        // 總戰力=0 时槽位必空；槽位检测偶发采到肖像区会虚高
        if (power == 0 && filled > 0)
        {
            LoggerService.LogInfo($"[DawnRealm] fill team#{teamIndex + 1} power=0 override slots {filled}->0");
            filled = 0;
        }
        else if (filled < 0)
        {
            filled = power > 0 ? 3 : 0;
            LoggerService.LogInfo($"[DawnRealm] fill team#{teamIndex + 1} slotDetect=fail power={power} assume={filled}");
        }
        else
        {
            LoggerService.LogInfo($"[DawnRealm] fill team#{teamIndex + 1} slots={filled}/5 power={power} prefer=光→火→全部");
        }

        if (filled >= 5)
        {
            LoggerService.LogInfo($"[DawnRealm] team#{teamIndex + 1} already full, reuse");
            return filled;
        }

        var startFilled = filled;

        // 头像库条目很少时跳过，避免点到已上阵角色导致取消
        if (DawnRealmCharCatalog.HasEnabledIcons() && DawnRealmCharCatalog.EnabledIconCount() >= 5)
        {
            ApplyElementFilter(context, context.GetImage(), ["光"], teamIndex);
            Thread.Sleep(350);
            filled = DawnRealmCharCatalog.ClickByPriority(
                context,
                () => CountFilledTeamSlots(context.GetImage()),
                filled,
                target: 5);
            if (filled >= 5) return filled;
            if (filled < startFilled)
            {
                LoggerService.LogInfo($"[DawnRealm] team#{teamIndex + 1} icon pick deselected, stop fill at {filled}");
                return filled;
            }
        }

        // 整页框识别选人：一次扫出所有头像中心，每个框只点一次；不够再下滑翻页。
        // 固定网格易对同一大头像点两次→取消上阵。
        foreach (var elem in prefer)
        {
            if (filled >= 5) break;
            LoggerService.LogInfo($"[DawnRealm] team#{teamIndex + 1} {filled}/5 -> filter {elem} (frame-scan)");
            ApplyElementFilter(context, context.GetImage(), [elem], teamIndex);
            Thread.Sleep(450);

            var before = filled;
            filled = FillByRosterFrames(context, filled, maxPages: 4);
            if (filled < before)
            {
                LoggerService.LogInfo($"[DawnRealm] team#{teamIndex + 1} deselected ({before}->{filled}), abort fill");
                break;
            }
            if (filled <= before)
                LoggerService.LogInfo($"[DawnRealm] team#{teamIndex + 1} filter {elem} no gain ({filled}/5)");
        }

        LoggerService.LogInfo($"[DawnRealm] team#{teamIndex + 1} fill done slots={filled}/5");
        return filled;
    }

    /// <summary>
    /// 当前名册可见区一口气识别金/粉边框头像中心，逐个点选；不足则上滑再识别。
    /// 点后若槽位减少（二次点取消），立刻再点同一点撤销取消。
    /// </summary>
    private static int FillByRosterFrames(IMaaContext context, int currentFilled, int maxPages)
    {
        if (currentFilled < 0) currentFilled = 0;
        var dead = new List<(int X, int Y)>(); // 点了不涨或已处理过的框（含取消过的）

        for (var page = 0; page < maxPages && currentFilled < 5; page++)
        {
            var centers = DetectRosterPortraitCenters(context);
            LoggerService.LogInfo($"[DawnRealm] roster frames page={page} count={centers.Count}");
            if (centers.Count == 0)
            {
                if (page == 0)
                {
                    // 识别失败时退回稀疏固定点（仅一排 y=300，避免 300+340 双击取消）
                    LoggerService.LogInfo("[DawnRealm] frame-scan empty, fallback row y=300");
                    var clicksLeft = 8;
                    var clicked = new HashSet<(int X, int Y)>();
                    (int X, int Y)[] fallback =
                    [
                        (130, 300), (300, 300), (470, 300), (640, 300), (800, 300), (960, 300)
                    ];
                    return ClickUntilFull(context, fallback, clicked, currentFilled, ref clicksLeft);
                }
                break;
            }

            var beforePage = currentFilled;
            foreach (var (x, y) in centers)
            {
                if (currentFilled >= 5) break;
                if (dead.Any(p => Math.Abs(p.X - x) < 90 && Math.Abs(p.Y - y) < 90))
                    continue;

                context.Click(x, y);
                Thread.Sleep(420);
                var img = context.GetImage();
                if (context.OCR("角色詳細", img, out _) || context.OCR("詳細資訊", img, out _) ||
                    context.OCR("角色详细", img, out _) || context.OCR("详细资讯", img, out _))
                {
                    LoggerService.LogInfo($"[DawnRealm] frame @{x},{y} opened detail, close");
                    if (context.OCR("關閉", img, out var ch) || context.OCR("关闭", img, out ch))
                        context.Click(ch.HitBox.X + ch.HitBox.Width / 2, ch.HitBox.Y + ch.HitBox.Height / 2);
                    else
                    {
                        context.Click(640, 650);
                        context.PressKey(4);
                    }
                    Thread.Sleep(350);
                    dead.Add((x, y));
                    continue;
                }

                var n = CountFilledTeamSlots(img);
                if (n < 0) continue;
                if (n < currentFilled)
                {
                    // 二次点到已上阵→取消：立刻再点一次补回
                    LoggerService.LogInfo($"[DawnRealm] frame @{x},{y} deselected ({currentFilled}->{n}), undo click");
                    context.Click(x, y);
                    Thread.Sleep(420);
                    n = CountFilledTeamSlots(context.GetImage());
                    if (n >= 0) currentFilled = n;
                    dead.Add((x, y));
                    continue;
                }
                if (n > currentFilled)
                {
                    LoggerService.LogInfo($"[DawnRealm] frame @{x},{y} filled {currentFilled}->{n}");
                    currentFilled = n;
                    dead.Add((x, y)); // 已上阵，本轮勿再点
                }
                else
                {
                    // 未变化：可能已在队里或点空，记死点避免连点取消
                    dead.Add((x, y));
                }
            }

            if (currentFilled >= 5) break;
            if (currentFilled <= beforePage && page > 0)
                break; // 翻页后仍无增长

            LoggerService.LogInfo($"[DawnRealm] roster swipe for more rows (have {currentFilled}/5)");
            context.Swipe(420, 430, 420, 270, 320);
            Thread.Sleep(450);
            // 翻页后坐标变化；保留 dead 里「本页点过」意义不大，但可防未滑开的残留框
        }

        return currentFilled;
    }

    /// <summary>
    /// 名册区（约 y=240~470）一口气检测金/粉边框头像中心，返回 1280 坐标，从上到下、从左到右。
    /// </summary>
    private static List<(int X, int Y)> DetectRosterPortraitCenters(IMaaContext context)
    {
        var result = new List<(int X, int Y)>();
        try
        {
            if (!TryGetBitmap(context, out var bmpType, out var bmpObj, out var getPixel, out var width, out var height))
                return result;
            using ((IDisposable)bmpObj)
            {
                var sx = width / 1280.0;
                var sy = height / 720.0;

                int Rx(int lx) => Math.Clamp((int)(lx * sx), 0, width - 1);
                int Ry(int ly) => Math.Clamp((int)(ly * sy), 0, height - 1);
                void Rgb(int lx, int ly, out int r, out int g, out int b)
                {
                    var c = getPixel.Invoke(bmpObj, [Rx(lx), Ry(ly)])!;
                    r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                    g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                    b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                }
                bool IsFrame(int r, int g, int b) =>
                    (r > 165 && g > 120 && b < 170 && (r - b) > 35 && r + g > 300) ||
                    (r > 175 && g is > 70 and < 190 && b is > 90 and < 210 && r >= g && (r - b) > 20);

                // 粗扫：色度够的候选 → 再看金/粉环
                var cands = new List<(double Score, int X, int Y)>();
                for (var ly = 260; ly <= 455; ly += 10)
                for (var lx = 80; lx <= 1080; lx += 10)
                {
                    // 快速色度预检
                    Rgb(lx, ly, out var r0, out var g0, out var b0);
                    var chroma0 = Math.Abs(r0 - g0) + Math.Abs(g0 - b0) + Math.Abs(r0 - b0);
                    if (chroma0 < 28 || r0 + g0 + b0 < 120) continue;

                    var good = 0;
                    var n = 0;
                    long sum = 0;
                    for (var dy = -20; dy <= 20; dy += 5)
                    for (var dx = -20; dx <= 20; dx += 5)
                    {
                        Rgb(lx + dx, ly + dy, out var r, out var g, out var b);
                        n++;
                        sum += r + g + b;
                        if (Math.Abs(r - g) + Math.Abs(g - b) + Math.Abs(r - b) > 30)
                            good++;
                    }
                    var col = good / (double)(n == 0 ? 1 : n);
                    if (col < 0.18 || sum / (double)(n == 0 ? 1 : n) < 140) continue;

                    var hit = 0;
                    var rn = 0;
                    const int inner = 26;
                    const int outer = 46;
                    for (var dy = -outer; dy <= outer; dy += 4)
                    for (var dx = -outer; dx <= outer; dx += 4)
                    {
                        var adx = Math.Abs(dx);
                        var ady = Math.Abs(dy);
                        if (adx < inner && ady < inner) continue;
                        rn++;
                        Rgb(lx + dx, ly + dy, out var r, out var g, out var b);
                        if (IsFrame(r, g, b)) hit++;
                    }
                    var ring = hit / (double)(rn == 0 ? 1 : rn);
                    if (ring < 0.06) continue;
                    cands.Add((ring * col, lx, ly));
                }

                cands.Sort((a, b) => b.Score.CompareTo(a.Score));
                foreach (var (_, lx, ly) in cands)
                {
                    if (result.Any(p => Math.Abs(p.X - lx) < 100 && Math.Abs(p.Y - ly) < 100))
                        continue;
                    result.Add((lx, ly));
                }

                result.Sort((a, b) =>
                {
                    var ra = a.Y / 55;
                    var rb = b.Y / 55;
                    return ra != rb ? ra.CompareTo(rb) : a.X.CompareTo(b.X);
                });
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo($"[DawnRealm] DetectRosterPortraitCenters err: {ex.Message}");
        }
        return result;
    }

    /// <summary>顶栏是否有多队页签（白/黄底按钮）。</summary>
    private static bool LooksLikeMultiTeamTabs(IMaaContext context)
    {
        try
        {
            if (!TryGetBitmap(context, out var bmpType, out var bmpObj, out var getPixel, out var width, out var height))
                return false;
            using ((IDisposable)bmpObj)
            {
                var sx = width / 1280.0;
                var sy = height / 720.0;
                var tabs = 0;
                foreach (var lx in TeamTabXs)
                {
                    var bright = 0;
                    foreach (var ly in new[] { 108, 112, 116 })
                    {
                        var c = getPixel.Invoke(bmpObj, [
                            Math.Clamp((int)(lx * sx), 0, width - 1),
                            Math.Clamp((int)(ly * sy), 0, height - 1)
                        ])!;
                        var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                        var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                        var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                        if (r + g + b > 500 || (r > 180 && g > 140 && b < 180))
                            bright++;
                    }
                    if (bright >= 2) tabs++;
                }
                if (tabs >= 2)
                    LoggerService.LogInfo($"[DawnRealm] multi-team tabs pixels tabs={tabs}");
                return tabs >= 2;
            }
        }
        catch
        {
            return false;
        }
    }

    private static int ClickUntilFull(
        IMaaContext context,
        (int X, int Y)[] candidates,
        HashSet<(int X, int Y)> clicked,
        int currentFilled,
        ref int clicksLeft)
    {
        if (currentFilled < 0) currentFilled = 0;
        foreach (var (x, y) in candidates)
        {
            if (currentFilled >= 5 || clicksLeft <= 0) break;
            if (!clicked.Add((x, y))) continue;
            context.Click(x, y);
            clicksLeft--;
            Thread.Sleep(450);
            // 若误开角色详情，立刻关掉再继续
            var img = context.GetImage();
            if (context.OCR("角色詳細", img, out _) || context.OCR("詳細資訊", img, out _) ||
                context.OCR("角色详细", img, out _) || context.OCR("详细资讯", img, out _))
            {
                LoggerService.LogInfo($"[DawnRealm] click @{x},{y} opened detail, close");
                if (context.OCR("關閉", img, out var ch) || context.OCR("关闭", img, out ch))
                    context.Click(ch.HitBox.X + ch.HitBox.Width / 2, ch.HitBox.Y + ch.HitBox.Height / 2);
                else
                {
                    context.Click(640, 650);
                    context.PressKey(4);
                }
                Thread.Sleep(400);
                continue;
            }
            var n = CountFilledTeamSlots(img);
            if (n >= 0)
            {
                if (n < currentFilled)
                {
                    LoggerService.LogInfo($"[DawnRealm] click @{x},{y} deselected ({currentFilled}->{n}), undo");
                    context.Click(x, y);
                    Thread.Sleep(420);
                    n = CountFilledTeamSlots(context.GetImage());
                    if (n >= 0) currentFilled = n;
                    continue;
                }
                if (n > currentFilled)
                    LoggerService.LogInfo($"[DawnRealm] click @{x},{y} filled {currentFilled}->{n}");
                currentFilled = n;
            }
        }
        return currentFilled;
    }

    private static void ApplyElementFilter(IMaaContext context, IMaaImageBuffer image, string[] prefer, int teamIndex)
    {
        foreach (var name in prefer)
        {
            if (context.OCR(name, image, out var fh, 60, 180, 700, 60) && fh.HitBox != null &&
                fh.HitBox.Y is > 170 and < 230)
            {
                context.Click(fh.HitBox.X + fh.HitBox.Width / 2, fh.HitBox.Y + fh.HitBox.Height / 2);
                LoggerService.LogInfo($"[DawnRealm] filter OCR '{name}' @ {fh.HitBox.X},{fh.HitBox.Y}");
                return;
            }

            // OCR 失败时按属性点固定坐标（1280；y≈204）
            // 全部≈88，火≈170，水≈250，風≈340，光≈460，闇≈540
            int filterX = name switch
            {
                "全部" => 88,
                "火" => 170,
                "水" => 250,
                "風" or "风" => 340,
                "光" => 460,
                "闇" or "暗" => 540,
                _ => teamIndex == 1 ? 170 : 460
            };
            const int filterY = 204;
            context.Click(filterX, filterY);
            LoggerService.LogInfo($"[DawnRealm] filter coord '{name}' x={filterX},y={filterY}");
            return;
        }
    }

    /// <summary>读底栏「總戰力」附近数字；失败返回 -1。</summary>
    private static long ReadTeamPower(IMaaContext context, IMaaImageBuffer image)
    {
        try
        {
            // 1280：總戰力数字约在左下
            var text = context.GetText(80, 620, 220, 50, image)
                       ?? context.GetText(60, 600, 280, 80, image)
                       ?? string.Empty;
            var m = Regex.Match(text.Replace(",", "").Replace(" ", ""), @"\d{3,}");
            if (m.Success && long.TryParse(m.Value, out var power))
                return power;
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo($"[DawnRealm] ReadTeamPower err: {ex.Message}");
        }
        return -1;
    }

    /// <summary>
    /// 检测底栏 5 个编成槽是否已有角色。空槽颜色平坦；有人则方差大。
    /// 返回 0~5；解码失败返回 -1。
    /// 用反射取 Bitmap，避免 Roslyn 热编译时 System.Drawing 未进引用表（CS0246）。
    /// </summary>
    private static int CountFilledTeamSlots(IMaaImageBuffer image)
    {
        try
        {
            if (!image.TryGetEncodedData(out byte[]? data) || data is null || data.Length < 100)
                return -1;

            var bmpType = ResolveBitmapType();
            if (bmpType == null)
            {
                LoggerService.LogInfo("[DawnRealm] CountFilledTeamSlots: Bitmap type missing");
                return -1;
            }

            using var ms = new MemoryStream(data);
            var bmp = Activator.CreateInstance(bmpType, ms);
            if (bmp is not IDisposable disposable)
                return -1;
            using (disposable)
            {
                var width = Convert.ToInt32(bmpType.GetProperty("Width")!.GetValue(bmp));
                var height = Convert.ToInt32(bmpType.GetProperty("Height")!.GetValue(bmp));
                var getPixel = bmpType.GetMethod("GetPixel", [typeof(int), typeof(int)]);
                if (getPixel == null) return -1;

                var sx = width / 1280.0;
                var sy = height / 720.0;
                // 1280 槽位中心（空队实测）；旧 [112,240,368,496,624] 右三格会采到底栏按钮虚高成 3/5。
                // 1600 空槽约 x≈160/340/520/700/880 →1280≈128/272/416/560/704；y≈725→580。
                int[] xs = [128, 272, 416, 560, 704];
                const int y0 = 580;
                var filled = 0;
                foreach (var x0 in xs)
                {
                    var cx = (int)(x0 * sx);
                    var cy = (int)(y0 * sy);
                    if (SlotLooksFilled(getPixel, bmp, width, height, cx, cy))
                        filled++;
                }
                return filled;
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo($"[DawnRealm] CountFilledTeamSlots err: {ex.Message}");
            return -1;
        }
    }

    private static Type? ResolveBitmapType()
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var n = asm.GetName().Name;
            if (n is not ("System.Drawing" or "System.Drawing.Common")) continue;
            var t = asm.GetType("System.Drawing.Bitmap");
            if (t != null) return t;
        }
        foreach (var name in new[] { "System.Drawing.Common", "System.Drawing" })
        {
            try
            {
                var asm = Assembly.Load(name);
                var t = asm.GetType("System.Drawing.Bitmap");
                if (t != null) return t;
            }
            catch
            {
                // ignore
            }
        }
        return null;
    }

    private static bool SlotLooksFilled(MethodInfo getPixel, object bmp, int width, int height, int cx, int cy)
    {
        var x1 = Math.Clamp(cx - 28, 0, width - 1);
        var y1 = Math.Clamp(cy - 28, 0, height - 1);
        var x2 = Math.Clamp(cx + 28, 0, width - 1);
        var y2 = Math.Clamp(cy + 28, 0, height - 1);
        long sum = 0, sumSq = 0;
        var n = 0;
        for (var y = y1; y <= y2; y += 2)
        {
            for (var x = x1; x <= x2; x += 2)
            {
                var c = getPixel.Invoke(bmp, [x, y])!;
                var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                var v = r + g + b;
                sum += v;
                sumSq += v * v;
                n++;
            }
        }
        if (n < 8) return false;
        var mean = sum / (double)n;
        var variance = sumSq / (double)n - mean * mean;
        // 空槽 std≈2；有人 std≈45+
        return variance > 400; // std>~20
    }

    // 人物右侧候选：多组 X/Y 偏移（列距/高度会变；水晶格常更右更下）
    private static readonly (int Dx, int Dy)[] MapNextColCandidates =
    [
        (340, 40), (340, 120), (340, 200), (340, -40),
        (280, 80), (400, 80), (400, 180), (460, 160),
        (300, 0), (360, 240), (420, 40), (380, -80),
    ];

    private static void NoteMapProgress()
    {
        _mapStuckCount = 0;
        _mapLastProgressTick = Environment.TickCount;
        _nextColTryIndex = 0;
    }

    private static void NoteMapStuck(string reason, string detail)
    {
        _mapStuckCount++;
        if (_mapLastProgressTick == 0)
            _mapLastProgressTick = Environment.TickCount;
        var idleMs = Environment.TickCount - _mapLastProgressTick;
        LoggerService.LogError(
            $"[DawnRealm] MAP-STUCK #{_mapStuckCount} reason={reason} idleMs={idleMs} detail={detail}");

        // 连续卡住：清死点、换下一组偏移，避免死循环同一坐标
        if (_mapStuckCount % 3 == 0)
        {
            _deadNodes.Clear();
            _nextColTryIndex++;
            LoggerService.LogError($"[DawnRealm] MAP-STUCK recover: clear deadNodes, nextColTry={_nextColTryIndex}");
        }
        // 长时间无进展：点一下地图中部刷新，并重置计数防止无限空转无日志
        if (_mapStuckCount >= 12 || idleMs > 90000)
        {
            LoggerService.LogError($"[DawnRealm] MAP-STUCK timeout -> soft reset (stuck={_mapStuckCount}, idle={idleMs}ms)");
            _deadNodes.Clear();
            _mapStuckCount = 0;
            _mapLastProgressTick = Environment.TickCount;
            _nextColTryIndex = 0;
            _lastMapClickX = -999;
            _lastMapClickY = -999;
        }
    }

    private static bool IsMoveConfirmUi(IMaaImageBuffer image, IMaaContext context) =>
        (context.OCR("取消", image, out _, 250, 420, 400, 180) &&
         context.OCR("OK", image, out _, 550, 420, 400, 180)) ||
        context.OCR("確認移動", image, out _) ||
        context.OCR("連結印記", image, out _) ||
        context.OCR("確定嗎", image, out _) ||
        context.OCR("确定吗", image, out _);

    /// <summary>
    /// 招募选角页：底栏有「地圖」圆钮 + 右下「進行邀請」蓝区，且无「戰鬥開始」。
    /// </summary>
    private static bool LooksLikeRecruitUi(IMaaContext context)
    {
        try
        {
            var image = context.GetImage();
            if (context.OCR("戰鬥開始", image, out _) || context.OCR("战斗开始", image, out _) ||
                context.OCR("編組隊伍", image, out _) || context.OCR("编组队伍", image, out _) ||
                context.OCR("還有1次", image, out _) || context.OCR("还有1次", image, out _) ||
                context.OCR("角色加入獎勵", image, out _) || context.OCR("加入獎勵", image, out _))
                return false;
            // 开局招募：要有顶栏 x/3 或明确「選擇角色」，单靠蓝钮易误判加入奖励页
            var hasCounter = context.OCR("3/3", image, out _, 900, 10, 360, 120) ||
                             context.OCR("2/3", image, out _, 900, 10, 360, 120) ||
                             context.OCR("1/3", image, out _, 900, 10, 360, 120) ||
                             context.OCR("0/3", image, out _, 900, 10, 360, 120) ||
                             context.OCR("選擇角色", image, out _, 200, 0, 1000, 140) ||
                             context.OCR("选择角色", image, out _, 200, 0, 1000, 140);
            if (!hasCounter)
                return false;
            if (context.OCR("地圖", image, out _, 0, 600, 200, 120) ||
                context.OCR("进行邀请", image, out _, 850, 560, 400, 160) ||
                context.OCR("進行邀請", image, out _, 850, 560, 400, 160))
                return true;

            if (!TryGetBitmap(context, out var bmpType, out var bmpObj, out var getPixel, out var width, out var height))
                return false;
            using ((IDisposable)bmpObj)
            {
                var sx = width / 1280.0;
                var sy = height / 720.0;
                // 右下邀请蓝钮 1600≈(1370,800)→1280≈(1096,640)
                var blue = 0;
                foreach (var (lx, ly) in new[] { (1080, 630), (1100, 640), (1120, 650), (1060, 635) })
                {
                    var c = getPixel.Invoke(bmpObj, [
                        Math.Clamp((int)(lx * sx), 0, width - 1),
                        Math.Clamp((int)(ly * sy), 0, height - 1)
                    ])!;
                    var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                    var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                    var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                    if (b > 150 && b > r + 40 && r < 130) blue++;
                }
                if (blue >= 3)
                {
                    LoggerService.LogInfo("[DawnRealm] recruit ui pixels (invite blue)");
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }
        return false;
    }

    /// <summary>
    /// 二层确认：右侧大块蓝钮（OK），左侧白取消。
    /// </summary>
    private static bool LooksLikeEndConfirmOkOnly(IMaaContext context)
    {
        try
        {
            if (!TryGetBitmap(context, out var bmpType, out var bmpObj, out var getPixel, out var width, out var height))
                return false;
            using ((IDisposable)bmpObj)
            {
                var sx = width / 1280.0;
                var sy = height / 720.0;
                var blue = 0;
                foreach (var lx in new[] { 720, 760, 800, 840 })
                {
                    var c = getPixel.Invoke(bmpObj, [
                        Math.Clamp((int)(lx * sx), 0, width - 1),
                        Math.Clamp((int)(498 * sy), 0, height - 1)
                    ])!;
                    var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                    var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                    var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                    if (b > 145 && b > r + 40 && r < 130) blue++;
                }
                return blue >= 3;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 结束确认弹窗：中部大块近白底（OCR 常挂时仍可认出）。
    /// </summary>
    private static bool LooksLikeEndConfirmDialog(IMaaContext context)
    {
        try
        {
            if (!TryGetBitmap(context, out var bmpType, out var bmpObj, out var getPixel, out var width, out var height))
                return false;
            using ((IDisposable)bmpObj)
            {
                var sx = width / 1280.0;
                var sy = height / 720.0;
                var white = 0;
                var n = 0;
                for (var ly = 280; ly <= 480; ly += 16)
                for (var lx = 380; lx <= 900; lx += 16)
                {
                    var c = getPixel.Invoke(bmpObj, [
                        Math.Clamp((int)(lx * sx), 0, width - 1),
                        Math.Clamp((int)(ly * sy), 0, height - 1)
                    ])!;
                    var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                    var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                    var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                    n++;
                    if (r > 235 && g > 235 && b > 235) white++;
                }
                var ratio = white / (double)(n == 0 ? 1 : n);
                if (ratio > 0.45)
                    LoggerService.LogInfo($"[DawnRealm] end-confirm dialog pixels white={ratio:F2}");
                return ratio > 0.45;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 编队页：中部角色肖像网格高方差格子多。用于 OCR 全挂时识别编队。
    /// </summary>
    private static bool LooksLikeTeamFormUi(IMaaContext context)
    {
        try
        {
            if (!TryGetBitmap(context, out var bmpType, out var bmpObj, out var getPixel, out var width, out var height))
                return false;
            using ((IDisposable)bmpObj)
            {
                var sx = width / 1280.0;
                var sy = height / 720.0;
                (int X, int Y)[] centers =
                [
                    (80, 224), (208, 224), (368, 224), (496, 224), (624, 224),
                    (80, 320), (208, 320), (368, 320), (496, 320),
                ];
                var colorful = 0;
                foreach (var (lx, ly) in centers)
                {
                    var cx = (int)(lx * sx);
                    var cy = (int)(ly * sy);
                    long sum = 0, sumSq = 0;
                    var n = 0;
                    for (var dy = -16; dy <= 16; dy += 4)
                    for (var dx = -16; dx <= 16; dx += 4)
                    {
                        var c = getPixel.Invoke(bmpObj, [
                            Math.Clamp(cx + dx, 0, width - 1),
                            Math.Clamp(cy + dy, 0, height - 1)
                        ])!;
                        var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                        var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                        var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                        var v = r + g + b;
                        sum += v;
                        sumSq += v * v;
                        n++;
                    }
                    if (n < 8) continue;
                    var mean = sum / (double)n;
                    var variance = sumSq / (double)n - mean * mean;
                    if (variance > 700) colorful++;
                }
                if (colorful >= 4)
                    LoggerService.LogInfo($"[DawnRealm] form roster pixels colorful={colorful}");
                return colorful >= 4;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Boss 三队编队底栏：左白「隊伍N」+ 右蓝「隊伍N+1」（与挑戰页观看/進行位置接近但编队必有肖像区）。
    /// </summary>
    private static bool LooksLikeBossMultiTeamFooter(IMaaContext context)
    {
        try
        {
            if (!TryGetBitmap(context, out var bmpType, out var bmpObj, out var getPixel, out var width, out var height))
                return false;
            using ((IDisposable)bmpObj)
            {
                var sx = width / 1280.0;
                var sy = height / 720.0;
                bool IsWhite(int lx, int ly)
                {
                    var c = getPixel.Invoke(bmpObj, [
                        Math.Clamp((int)(lx * sx), 0, width - 1),
                        Math.Clamp((int)(ly * sy), 0, height - 1)
                    ])!;
                    var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                    var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                    var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                    return r > 180 && g > 180 && b > 180;
                }
                bool IsBlue(int lx, int ly)
                {
                    var c = getPixel.Invoke(bmpObj, [
                        Math.Clamp((int)(lx * sx), 0, width - 1),
                        Math.Clamp((int)(ly * sy), 0, height - 1)
                    ])!;
                    var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                    var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                    var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                    return b > 160 && b > r + 40 && r < 140;
                }
                // 编队切队：白≈(980,615) 蓝≈(1120,615)；开战钮更靠右≈(1200,600)
                var hit = IsWhite(980, 615) && IsBlue(1120, 615) && !IsBlue(1220, 600);
                if (hit)
                    LoggerService.LogInfo("[DawnRealm] boss multi-team footer pixels");
                return hit;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 戰鬥格子页：右下「進行挑戰」蓝钮。台服艺术字 OCR 常全挂时用像素兜底。
    /// 勿与编队页「隊伍3」蓝钮混淆（编队有肖像网格，由调用方排除）。
    /// </summary>
    private static bool LooksLikeChallengeBlueButton(IMaaContext context)
    {
        try
        {
            if (!TryGetBitmap(context, out var bmpType, out var bmpObj, out var getPixel, out var width, out var height))
                return false;
            using ((IDisposable)bmpObj)
            {
                var sx = width / 1280.0;
                var sy = height / 720.0;
                var blueHits = 0;
                var samples = 0;
                // 采样蓝钮中心带；观看地图白钮在其左约 x=900
                foreach (var (lx, ly) in new[]
                         {
                             (1120, 620), (1140, 630), (1160, 615), (1100, 625),
                             (1130, 640), (1150, 605), (1180, 620)
                         })
                {
                    var x = (int)(lx * sx);
                    var y = (int)(ly * sy);
                    var c = getPixel.Invoke(bmpObj, [
                        Math.Clamp(x, 0, width - 1),
                        Math.Clamp(y, 0, height - 1)
                    ])!;
                    var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                    var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                    var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                    samples++;
                    // 進行挑戰蓝鈕：偏亮蓝
                    if (b > 160 && b > r + 40 && b > g + 20 && r < 140)
                        blueHits++;
                }
                // 白钮对照：观看地图约 (900,620) 应偏白
                var wx = (int)(900 * sx);
                var wy = (int)(620 * sy);
                var wc = getPixel.Invoke(bmpObj, [
                    Math.Clamp(wx, 0, width - 1),
                    Math.Clamp(wy, 0, height - 1)
                ])!;
                var wr = Convert.ToInt32(wc.GetType().GetProperty("R")!.GetValue(wc));
                var wg = Convert.ToInt32(wc.GetType().GetProperty("G")!.GetValue(wc));
                var wb = Convert.ToInt32(wc.GetType().GetProperty("B")!.GetValue(wc));
                var whiteLeft = wr > 180 && wg > 180 && wb > 180;
                // 编队页白钮更靠右(~980)；挑戰页观看地图更靠左(~900)
                var formWhite = false;
                {
                    var fx = (int)(980 * sx);
                    var fy = (int)(615 * sy);
                    var fc = getPixel.Invoke(bmpObj, [
                        Math.Clamp(fx, 0, width - 1),
                        Math.Clamp(fy, 0, height - 1)
                    ])!;
                    var fr = Convert.ToInt32(fc.GetType().GetProperty("R")!.GetValue(fc));
                    var fg = Convert.ToInt32(fc.GetType().GetProperty("G")!.GetValue(fc));
                    var fb = Convert.ToInt32(fc.GetType().GetProperty("B")!.GetValue(fc));
                    formWhite = fr > 180 && fg > 180 && fb > 180;
                }
                var hit = blueHits >= 4 && whiteLeft && !formWhite;
                if (hit)
                    LoggerService.LogInfo($"[DawnRealm] challenge blue-btn pixels={blueHits}/{samples} whiteLeft={whiteLeft}");
                return hit;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 以地图人物为基准，点其右侧候选格。每轮只点 1 个；miss 记死点并报 STUCK。
    /// 优先：人物右侧的青色高亮节点；否则按固定偏移表轮询。
    /// </summary>
    private static bool TryClickNextColumnFromChar(IMaaContext context)
    {
        try
        {
            if (!TryFindMapCharacter(context, out var charX, out var charY))
            {
                LoggerService.LogError("[DawnRealm] MAP-STUCK reason=char-not-found");
                _mapStuckCount++;
                return false;
            }

            LoggerService.LogInfo($"[DawnRealm] map char @ {charX},{charY}");
            _deadNodes.RemoveAll(n => n.UntilTick < Environment.TickCount);

            // 1) 人物右侧高亮（青/蓝）优先
            if (TryClickGlowRightOfChar(context, charX, charY))
                return true;

            // 2) 固定偏移表轮询
            for (var i = 0; i < MapNextColCandidates.Length; i++)
            {
                var idx = (_nextColTryIndex + i) % MapNextColCandidates.Length;
                var (dx, dy) = MapNextColCandidates[idx];
                var tx = Math.Clamp(charX + dx, 260, 1050);
                var ty = Math.Clamp(charY + dy, 120, 540);
                if (_deadNodes.Exists(n => Math.Abs(n.X - tx) < 40 && Math.Abs(n.Y - ty) < 40))
                    continue;
                if (Math.Abs(tx - _lastMapClickX) < 30 && Math.Abs(ty - _lastMapClickY) < 30)
                    continue;

                _lastMapClickX = tx;
                _lastMapClickY = ty;
                _nextColTryIndex = idx + 1;
                LoggerService.LogInfo($"[DawnRealm] click next-col @{tx},{ty} (char+{dx},{dy})");
                context.Click(tx, ty);
                Thread.Sleep(1300);
                var after = context.GetImage();
                if (!IsMoveConfirmUi(after, context))
                {
                    _deadNodes.Add((tx, ty, Environment.TickCount + 45000));
                    NoteMapStuck("next-col-miss", $"@{tx},{ty} char={charX},{charY} d=({dx},{dy})");
                    return false;
                }
                return true;
            }

            NoteMapStuck("next-col-exhausted", $"char={charX},{charY} all candidates dead");
        }
        catch (Exception ex)
        {
            LoggerService.LogError($"[DawnRealm] MAP-STUCK reason=next-col-exception detail={ex.Message}");
        }
        return false;
    }

    /// <summary>在人物右侧找青/蓝高亮节点并点一次。</summary>
    private static bool TryClickGlowRightOfChar(IMaaContext context, int charX, int charY)
    {
        if (!TryGetBitmap(context, out var bmpType, out var bmpObj, out var getPixel, out var width, out var height))
            return false;
        using ((IDisposable)bmpObj)
        {
            var sx = width / 1280.0;
            var sy = height / 720.0;
            var scores = new List<(int X, int Y, int Score)>();
            var minX = Math.Max(260, charX + 180);
            var maxX = Math.Min(1050, charX + 520);
            for (var ly = 140; ly <= 520; ly += 10)
            for (var lx = minX; lx <= maxX; lx += 10)
            {
                var x = (int)(lx * sx);
                var y = (int)(ly * sy);
                var glow = 0;
                for (var dy = -8; dy <= 8; dy += 4)
                for (var dx = -8; dx <= 8; dx += 4)
                {
                    var c = getPixel.Invoke(bmpObj, [
                        Math.Clamp(x + dx, 0, width - 1),
                        Math.Clamp(y + dy, 0, height - 1)
                    ])!;
                    var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                    var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                    var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                    // 路径/可走格常见青蓝高亮
                    if (b > 170 && g > 120 && r < 140 && b + g > r * 2)
                        glow++;
                }
                if (glow >= 5)
                    scores.Add((lx, ly, glow));
            }
            if (scores.Count == 0) return false;
            scores.Sort((a, b) => b.Score.CompareTo(a.Score));
            foreach (var (lx, ly, score) in scores)
            {
                if (_deadNodes.Exists(n => Math.Abs(n.X - lx) < 40 && Math.Abs(n.Y - ly) < 40))
                    continue;
                if (clustersTooClose(scores, lx, ly)) { /* keep first of cluster via dead after try */ }
                _lastMapClickX = lx;
                _lastMapClickY = ly;
                LoggerService.LogInfo($"[DawnRealm] click glow-right @{lx},{ly} score={score} char={charX},{charY}");
                context.Click(lx, ly);
                Thread.Sleep(1300);
                var after = context.GetImage();
                if (!IsMoveConfirmUi(after, context))
                {
                    _deadNodes.Add((lx, ly, Environment.TickCount + 45000));
                    NoteMapStuck("glow-miss", $"@{lx},{ly} score={score}");
                    return false;
                }
                return true;
            }
        }
        return false;

        static bool clustersTooClose(List<(int X, int Y, int Score)> _, int __, int ___) => false;
    }

    private static bool TryGetBitmap(
        IMaaContext context,
        out Type bmpType,
        out object bmpObj,
        out MethodInfo getPixel,
        out int width,
        out int height)
    {
        bmpType = null!;
        bmpObj = null!;
        getPixel = null!;
        width = height = 0;
        var image = context.GetImage();
        if (!image.TryGetEncodedData(out byte[]? data) || data is null || data.Length < 100)
            return false;

        Type? t = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var n = asm.GetName().Name;
            if (n is "System.Drawing" or "System.Drawing.Common")
            {
                t = asm.GetType("System.Drawing.Bitmap");
                if (t != null) break;
            }
        }
        if (t == null)
        {
            try { t = Assembly.Load("System.Drawing.Common").GetType("System.Drawing.Bitmap"); }
            catch { return false; }
        }
        if (t == null) return false;

        var ms = new MemoryStream(data);
        var obj = Activator.CreateInstance(t, ms);
        if (obj is not IDisposable) { ms.Dispose(); return false; }
        var gp = t.GetMethod("GetPixel", [typeof(int), typeof(int)]);
        if (gp == null) { ((IDisposable)obj).Dispose(); return false; }

        bmpType = t;
        bmpObj = obj;
        getPixel = gp;
        width = Convert.ToInt32(t.GetProperty("Width")!.GetValue(obj));
        height = Convert.ToInt32(t.GetProperty("Height")!.GetValue(obj));
        return true;
    }

    /// <summary>在地图区找人物锚点（暖色发色簇），返回 1280 坐标。</summary>
    private static bool TryFindMapCharacter(IMaaContext context, out int charX, out int charY)
    {
        charX = 0;
        charY = 0;
        if (!TryGetBitmap(context, out _, out var bmpObj, out var getPixel, out var width, out var height))
            return false;
        using ((IDisposable)bmpObj)
        {
            var sx = width / 1280.0;
            var sy = height / 720.0;
            long sumX = 0, sumY = 0;
            var n = 0;
            // 避开顶栏；人物多在左半地图
            for (var ly = 120; ly <= 500; ly += 4)
            for (var lx = 160; lx <= 700; lx += 4)
            {
                var c = getPixel.Invoke(bmpObj, [(int)(lx * sx), (int)(ly * sy)])!;
                var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                // 橙发更严：避免 UI 暖色误判成固定 (300,204)
                if (!(r > 165 && g is > 60 and < 150 && b < 95 && r > g + 45 && r > b + 70))
                    continue;
                sumX += lx;
                sumY += ly;
                n++;
            }
            if (n < 12) return false;
            charX = (int)(sumX / n);
            charY = (int)(sumY / n);
            return true;
        }
    }

    private static bool TryClickLabel(IMaaContext context, string label)
    {
        // 避免 dynamic 参数导致 OCR 扩展方法无法绑定（CS1973）
        var image = context.GetImage();
        // 有居中「取消/OK」对话框时，不要点对话框里的 NORMAL 文字
        if (context.OCR("取消", image, out _, 250, 420, 400, 180) &&
            context.OCR("OK", image, out _, 550, 420, 400, 180))
        {
            return false;
        }

        // 中列优先；EVENT 额外避开顶部灰色不可走格（cy 过小）
        RecognitionDetail detail = null!;
        var found = context.OCR(label, image, out detail, 300, 220, 550, 360) ||
                    context.OCR(label, image, out detail, 300, 100, 600, 480) ||
                    context.OCR(label, image, out detail, 200, 80, 750, 500) ||
                    context.OCR(label, image, out detail, 80, 80, 1120, 500);
        if (found && detail.HitBox != null)
        {
            var cx = detail.HitBox.X + detail.HitBox.Width / 2;
            var cy = detail.HitBox.Y + detail.HitBox.Height / 2;
            // 底部栏误识别（返回/撤退约 y>600）
            if (cy > 560) return false;
            // 顶部灰 EVENT / 人物同列已走格勿点；过右未连通
            if (cx < 400 || cx > 980) return false;
            if ((label is "EVENT" or "事件") && cy < 180) return false;
            // 清理过期死点
            _deadNodes.RemoveAll(n => n.UntilTick < Environment.TickCount);
            if (_deadNodes.Exists(n => Math.Abs(n.X - cx) < 50 && Math.Abs(n.Y - cy) < 50))
            {
                LoggerService.LogInfo($"[DawnRealm] skip dead node '{label}' @ {cx},{cy}");
                return false;
            }
            // 同一点连点超过 1 次则跳过
            if (Math.Abs(cx - _lastMapClickX) < 40 && Math.Abs(cy - _lastMapClickY) < 40)
            {
                _sameMapClickCount++;
                if (_sameMapClickCount >= 1)
                {
                    LoggerService.LogInfo($"[DawnRealm] skip repeat node '{label}' @ {cx},{cy}");
                    return false;
                }
            }
            else
            {
                _sameMapClickCount = 0;
            }
            _lastMapClickX = cx;
            _lastMapClickY = cy;
            // 标签下方六角底座（曾改 cy-35 导致点空；实测 +40 可点中）
            var tapY = Math.Min(560, cy + 40);
            LoggerService.LogInfo($"[DawnRealm] click node '{label}' @ {cx},{cy} -> {cx},{tapY}");
            context.Click(cx, tapY);
            return true;
        }
        return false;
    }
}
