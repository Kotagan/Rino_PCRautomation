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
/// 开局招募：美食殿堂用 unitId 实机模板点选 优衣(1002)/怜(1003)/日和(1001)，满 3/3 邀请。
/// 模板优先级：整格 bootcell → boot_u → bootface；仍不足则 catalog 补满。
/// 禁止全表点选、禁止 OCR 人名、禁止死坐标。
/// </summary>
public class DawnRealmRecruitPickAction : MaaCustomAction
{
    public override string Name { get; set; } = nameof(DawnRealmRecruitPickAction);

    private static readonly (int UnitId, string Label, string[] Templates)[] BootUnits =
    [
        (1002, "优衣", ["dawnrealm/chars/bootcell_u1002.png", "dawnrealm/chars/boot_u1002.png", "dawnrealm/chars/bootface_u1002.png"]),
        (1003, "怜", ["dawnrealm/chars/bootcell_u1003.png", "dawnrealm/chars/boot_u1003.png", "dawnrealm/chars/bootface_u1003.png"]),
        (1001, "日和", ["dawnrealm/chars/bootcell_u1001.png", "dawnrealm/chars/boot_u1001.png", "dawnrealm/chars/bootface_u1001.png"]),
    ];

    protected override void ErrorHandle(IMaaContext context, RunArgs args)
    {
        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
    }

    protected override bool Execute(IMaaContext context, RunArgs args)
    {
        DawnRealmConfig.ApplyParam(DawnRealmConfig.ReadActionParam(args));
        LoggerService.LogInfo($"[DawnRealm] recruit pick guild={DawnRealmConfig.Guild} mode=boot-templates-v6");

        DismissSplashIfNeeded(context);
        DismissLimitToast(context);

        // 加入奖励页误入：直接交回 Decide 点「選擇」
        var probe = context.GetImage();
        if (context.OCR("還有1次", probe, out _) || context.OCR("还有1次", probe, out _) ||
            context.OCR("角色加入獎勵", probe, out _) || context.OCR("加入獎勵", probe, out _))
        {
            LoggerService.LogInfo("[DawnRealm] recruit skip: join-reward page");
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        if (CountSelected(context) >= 3)
        {
            LoggerService.LogInfo("[DawnRealm] recruit already 3/3, invite");
            ConfirmInvite(context);
            context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
            return true;
        }

        var bootIds = DawnRealmCharCatalog.GetBootPreferUnitIds();
        if (bootIds.Count > 0)
        {
            PickBootTrio(context, bootIds);
            // 开局三人未齐 / 中途招募页没有这三人：用评级库补满，避免 0/3 空转
            if (CountSelected(context) < 3)
            {
                LoggerService.LogInfo("[DawnRealm] recruit boot incomplete -> catalog fill");
                PickByCatalog(context, clearFirst: false);
            }
        }
        else
        {
            PickByCatalog(context, clearFirst: true);
        }

        Thread.Sleep(500);
        DismissSplashIfNeeded(context);
        context.OverrideNext(args.NodeName, ["黎明界地图循环"]);
        return true;
    }

    private static void PickBootTrio(IMaaContext context, IReadOnlyList<int> bootIds)
    {
        if (CountSelected(context) > 0)
            ClearByBootTemplates(context, bootIds);

        ClickFilter(context, "全部");
        Thread.Sleep(450);

        var selected = CountSelected(context);
        foreach (var (unitId, label, templates) in BootUnits)
        {
            if (!BootHas(bootIds, unitId)) continue;
            if (selected >= 3) break;
            if (TryClickBootTemplates(context, templates, label, ref selected))
                LoggerService.LogInfo($"[DawnRealm] recruit got {label} id={unitId} -> {selected}/3");
            else
                LoggerService.LogInfo($"[DawnRealm] recruit MISS template {label} id={unitId}");
        }

        selected = CountSelected(context);
        LoggerService.LogInfo($"[DawnRealm] recruit boot selected={selected}/3");
        if (selected >= 3)
            ConfirmInvite(context);
    }

    private static void PickByCatalog(IMaaContext context, bool clearFirst)
    {
        if (clearFirst && CountSelected(context) > 0)
            ClearByBootTemplates(context, Array.Empty<int>());

        var selected = CountSelected(context);
        foreach (var attr in new[] { "光", "火", "" })
        {
            if (selected >= 3) break;
            ClickFilter(context, string.IsNullOrEmpty(attr) ? "全部" : attr);
            Thread.Sleep(400);
            selected = DawnRealmCharCatalog.ClickRecruitByPriority(
                context, selected, need: 3, attrFilter: string.IsNullOrEmpty(attr) ? null : attr,
                recount: () => CountSelected(context), maxTry: 12);
        }

        selected = CountSelected(context);
        LoggerService.LogInfo($"[DawnRealm] recruit catalog final selected={selected}/3");
        if (selected >= 3)
            ConfirmInvite(context);
        else
            LoggerService.LogInfo("[DawnRealm] recruit incomplete, will retry next loop");
    }

    private static bool BootHas(IReadOnlyList<int> ids, int unitId)
    {
        for (var i = 0; i < ids.Count; i++)
            if (ids[i] == unitId) return true;
        return false;
    }

    /// <summary>用 boot 模板取消已选（点到则计数下降；误选则立刻再点撤销）。</summary>
    private static void ClearByBootTemplates(IMaaContext context, IReadOnlyList<int> bootIds)
    {
        ClickFilter(context, "全部");
        Thread.Sleep(350);
        for (var round = 0; round < 5 && CountSelected(context) > 0; round++)
        {
            var before = CountSelected(context);
            var progressed = false;
            foreach (var (unitId, label, templates) in BootUnits)
            {
                if (bootIds.Count > 0 && !BootHas(bootIds, unitId)) continue;
                if (!TryFindBoot(context, templates, out var cx, out var cy))
                    continue;
                context.Click(cx, cy);
                Thread.Sleep(320);
                DismissLimitToast(context);
                var now = CountSelected(context);
                if (now < before)
                {
                    LoggerService.LogInfo($"[DawnRealm] recruit clear {label} @{cx},{cy} -> {now}");
                    before = now;
                    progressed = true;
                    if (before == 0) break;
                }
                else if (now > before)
                {
                    context.Click(cx, cy);
                    Thread.Sleep(280);
                    before = CountSelected(context);
                }
            }
            if (!progressed) break;
        }
        LoggerService.LogInfo($"[DawnRealm] recruit after clear selected={CountSelected(context)}");
    }

    private static bool TryClickBootTemplates(IMaaContext context, string[] templates, string label, ref int selected)
    {
        if (!TryFindBoot(context, templates, out var cx, out var cy))
            return false;

        var before = selected;
        LoggerService.LogInfo($"[DawnRealm] recruit click {label} @{cx},{cy}");
        context.Click(cx, cy);
        Thread.Sleep(450);
        DismissLimitToast(context);
        selected = CountSelected(context);
        if (selected > before) return true;
        if (selected < before)
        {
            context.Click(cx, cy);
            Thread.Sleep(300);
            selected = CountSelected(context);
        }
        return selected > before;
    }

    private static bool TryFindBoot(IMaaContext context, string[] templates, out int cx, out int cy)
    {
        cx = cy = 0;
        foreach (var template in templates)
        {
            if (TryFindBoot(context, template, out cx, out cy))
                return true;
        }
        return false;
    }

    private static bool TryFindBoot(IMaaContext context, string template, out int cx, out int cy)
    {
        cx = cy = 0;
        const int roiX = 20, roiY = 140, roiW = 1040, roiH = 340;
        RecognitionDetail? detail = null;
        try
        {
            var image = context.GetImage();
            // 实机裁剪模板：由高到低试阈值
            if (!context.TemplateMatch(template, image, out detail, 0.78, roiX, roiY, roiW, roiH) &&
                !context.TemplateMatch(template, image, out detail, 0.70, roiX, roiY, roiW, roiH) &&
                !context.TemplateMatch(template, image, out detail, 0.62, roiX, roiY, roiW, roiH) &&
                !context.TemplateMatch(template, image, out detail, 0.55, roiX, roiY, roiW, roiH))
                return false;
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo($"[DawnRealm] recruit template err {template}: {ex.Message}");
            return false;
        }

        if (detail?.HitBox == null) return false;
        cx = detail.HitBox.X + detail.HitBox.Width / 2;
        cy = detail.HitBox.Y + detail.HitBox.Height / 2;
        return true;
    }

    private static void ConfirmInvite(IMaaContext context)
    {
        if (ClickOcr(context, 800, 560, 460, 150, "進行邀請", "进行邀请", "邀請", "邀请"))
            return;
        context.Click(1100, 640);
        Thread.Sleep(400);
    }

    private static bool ClickFilter(IMaaContext context, string name)
    {
        var image = context.GetImage();
        if (context.OCR(name, image, out var fh, 40, 90, 700, 80) && fh.HitBox != null &&
            fh.HitBox.Y is > 90 and < 180)
        {
            context.Click(fh.HitBox.X + fh.HitBox.Width / 2, fh.HitBox.Y + fh.HitBox.Height / 2);
            LoggerService.LogInfo($"[DawnRealm] recruit filter '{name}'");
            return true;
        }
        context.Click(name == "光" ? 460 : name == "火" ? 200 : 100, 130);
        LoggerService.LogInfo($"[DawnRealm] recruit filter coord '{name}'");
        return true;
    }

    private static int CountSelected(IMaaContext context)
    {
        var image = context.GetImage();
        foreach (var (label, n) in new[]
                 {
                     ("3/3", 3), ("3 / 3", 3), ("３/３", 3), ("3／3", 3),
                     ("2/3", 2), ("2 / 3", 2), ("２/３", 2), ("2／3", 2),
                     ("1/3", 1), ("1 / 3", 1), ("１/３", 1), ("1／3", 1),
                     ("0/3", 0), ("0 / 3", 0), ("０/３", 0),
                 })
        {
            if (context.OCR(label, image, out _, 900, 10, 360, 120))
                return n;
        }
        return 0;
    }

    private static void DismissLimitToast(IMaaContext context)
    {
        var image = context.GetImage();
        if (context.OCR("只能選擇", image, out _) || context.OCR("只能选择", image, out _))
        {
            context.Click(800, 450);
            Thread.Sleep(250);
        }
    }

    private static void DismissSplashIfNeeded(IMaaContext context)
    {
        var image = context.GetImage();
        var onGrid = context.OCR("選擇角色", image, out _, 0, 0, 1000, 140) ||
                     context.OCR("進行邀請", image, out _, 800, 560, 460, 160) ||
                     context.OCR("地圖", image, out _, 0, 600, 280, 120);
        if (!onGrid)
        {
            context.Click(80, 40);
            Thread.Sleep(300);
            context.Click(640, 680);
            Thread.Sleep(400);
        }
    }

    private static bool ClickOcr(IMaaContext context, int x, int y, int w, int h, params string[] labels)
    {
        var image = context.GetImage();
        foreach (var label in labels)
        {
            if (context.OCR(label, image, out RecognitionDetail detail, x, y, w, h) && detail.HitBox != null)
            {
                context.Click(detail.HitBox.X + detail.HitBox.Width / 2, detail.HitBox.Y + detail.HitBox.Height / 2);
                Thread.Sleep(350);
                return true;
            }
        }
        return false;
    }
}
