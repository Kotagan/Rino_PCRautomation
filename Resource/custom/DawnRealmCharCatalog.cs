using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MFAWPF.Custom;
using MFAWPF.Extensions;
using MFAWPF.Helper;

namespace MFAWPF.CustomRuntime;

/// <summary>
/// 黎明界角色头像库 + 优先度。
/// 配置：Resource/base/dawnrealm/char_priority.json / char_tiers.json
/// 头像：Resource/base/image/dawnrealm/chars/*.png
/// 排序：属性光>火 + 评级 S>A>B>C + 6星；美食开局抬高 优衣/怜/日和。
/// </summary>
public static class DawnRealmCharCatalog
{
    public sealed class CharEntry
    {
        public string Id { get; set; } = "";
        public int UnitId { get; set; }
        public int Star { get; set; }
        public string Name { get; set; } = "";
        public string Attr { get; set; } = "";
        public string Tier { get; set; } = "";
        public int Priority { get; set; }
        public string Icon { get; set; } = "";
        public bool Enabled { get; set; } = true;
    }

    private sealed class CatalogFile
    {
        public int Version { get; set; } = 1;
        public List<string> DefaultAttrOrder { get; set; } = ["光", "火"];
        public Dictionary<string, List<int>>? BootPreferByGuild { get; set; }
        public List<CharEntry> Chars { get; set; } = new();
    }

    private static CatalogFile _cache = new();
    private static DateTime _cacheWriteUtc = DateTime.MinValue;
    private static readonly HashSet<string> _capturedHashes = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _lastCaptureUtc = DateTime.MinValue;

    public static string ResourceRoot =>
        Path.Combine(AppContext.BaseDirectory, "Resource", "base");

    public static string CatalogPath =>
        Path.Combine(ResourceRoot, "dawnrealm", "char_priority.json");

    public static string CharsDir =>
        Path.Combine(ResourceRoot, "image", "dawnrealm", "chars");

    public static string InboxDir =>
        Path.Combine(CharsDir, "inbox");

    public static void EnsureLoaded()
    {
        try
        {
            Directory.CreateDirectory(CharsDir);
            Directory.CreateDirectory(InboxDir);
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);

            if (!File.Exists(CatalogPath))
                SaveCatalog(new CatalogFile());

            var write = File.GetLastWriteTimeUtc(CatalogPath);
            if (_cacheWriteUtc != DateTime.MinValue && write == _cacheWriteUtc)
                return;

            var json = File.ReadAllText(CatalogPath);
            var parsed = JsonSerializer.Deserialize<CatalogFile>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            _cache = parsed ?? new CatalogFile();
            _cache.Chars ??= new List<CharEntry>();
            _cache.DefaultAttrOrder ??= ["光", "火"];
            _cacheWriteUtc = write;
            LoggerService.LogInfo($"[DawnRealm] char catalog loaded entries={_cache.Chars.Count}");
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo($"[DawnRealm] char catalog load err: {ex.Message}");
            _cache = new CatalogFile();
        }
    }

    public static IReadOnlyList<CharEntry> GetEnabledByPriority(string? attrFilter = null)
    {
        EnsureLoaded();
        var boot = GetBootPreferUnitIds();
        IEnumerable<CharEntry> q = _cache.Chars
            .Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.Icon));
        if (!string.IsNullOrWhiteSpace(attrFilter) && attrFilter is not "全部")
        {
            q = q.Where(c => string.Equals(c.Attr, attrFilter, StringComparison.Ordinal) ||
                             (attrFilter is "風" or "风" && (c.Attr is "風" or "风")));
        }

        return q
            .OrderByDescending(c => boot.Contains(c.UnitId) ? 1 : 0)
            .ThenByDescending(c => c.Priority)
            .ThenByDescending(c => c.Star)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<int> GetBootPreferUnitIds()
    {
        EnsureLoaded();
        var guild = DawnRealmConfig.Guild ?? "";
        if (_cache.BootPreferByGuild != null)
        {
            foreach (var kv in _cache.BootPreferByGuild)
            {
                if (!string.IsNullOrWhiteSpace(kv.Key) &&
                    guild.Contains(kv.Key, StringComparison.OrdinalIgnoreCase) &&
                    kv.Value is { Count: > 0 })
                    return kv.Value;
            }
        }

        // 默认美食开局：UE / 剑圣怜 / 猫拳日和
        if (guild.Contains("美食", StringComparison.Ordinal))
            return [1002, 1003, 1001];
        return [];
    }

    public static IReadOnlyList<string> DefaultAttrOrder
    {
        get
        {
            EnsureLoaded();
            return _cache.DefaultAttrOrder;
        }
    }

    public static bool HasEnabledIcons()
    {
        EnsureLoaded();
        return GetEnabledByPriority().Any(c => ResolveIconFullPath(c.Icon) != null);
    }

    public static int EnabledIconCount()
    {
        EnsureLoaded();
        return GetEnabledByPriority().Count(c => ResolveIconFullPath(c.Icon) != null);
    }

    public static int ClickByPriority(
        IMaaContext context,
        Func<int> countSlots,
        int currentFilled,
        int target = 5,
        double threshold = 0.78,
        int roiX = 40,
        int roiY = 190,
        int roiW = 760,
        int roiH = 300)
    {
        EnsureLoaded();
        if (currentFilled < 0) currentFilled = 0;
        if (currentFilled >= target) return currentFilled;

        // 全库 200+ 会卡死/刷屏，编队只试优先度最高的一小批
        var list = GetEnabledByPriority().Take(15).ToList();
        if (list.Count == 0) return currentFilled;

        foreach (var ch in list)
        {
            if (currentFilled >= target) break;
            var iconPath = NormalizeIcon(ch.Icon);
            if (ResolveIconFullPath(iconPath) == null) continue;

            var image = context.GetImage();
            RecognitionDetail? detail = null;
            try
            {
                if (!context.TemplateMatch(iconPath, image, out detail, threshold, roiX, roiY, roiW, roiH))
                    continue;
            }
            catch (Exception ex)
            {
                LoggerService.LogInfo($"[DawnRealm] form template err '{ch.Name}': {ex.Message}");
                continue;
            }
            if (detail == null) continue;
            if (detail.HitBox == null) continue;

            var cx = detail.HitBox.X + detail.HitBox.Width / 2;
            var cy = detail.HitBox.Y + detail.HitBox.Height / 2;
            LoggerService.LogInfo(
                $"[DawnRealm] priority pick '{ch.Name}'({ch.Attr}) p={ch.Priority} @{cx},{cy}");
            context.Click(cx, cy);
            Thread.Sleep(350);

            var after = context.GetImage();
            if (!context.OCR("當前的成員", after, out _) &&
                !context.OCR("編組隊伍", after, out _) &&
                !context.OCR("選擇角色", after, out _) &&
                !context.OCR("進行邀請", after, out _))
            {
                context.Click(640, 400);
                Thread.Sleep(300);
            }

            var n = countSlots();
            if (n >= 0)
            {
                if (n < currentFilled)
                {
                    LoggerService.LogInfo(
                        $"[DawnRealm] priority pick deselected ({currentFilled}->{n}), skip rest of batch");
                    return n;
                }
                currentFilled = n;
            }
            else
            {
                currentFilled = Math.Min(target, currentFilled + 1);
            }
        }

        return currentFilled;
    }

    public static int ClickRecruitByPriority(
        IMaaContext context,
        int alreadySelected,
        int need = 3,
        string? attrFilter = null,
        Func<int>? recount = null,
        int maxTry = 20)
    {
        EnsureLoaded();
        var selected = Math.Max(0, alreadySelected);
        var list = GetEnabledByPriority(attrFilter).Take(Math.Max(1, maxTry)).ToList();
        if (list.Count == 0) return selected;

        foreach (var ch in list)
        {
            if (selected >= need) break;
            selected = TryClickEntry(context, ch, selected, need, recount);
        }

        return selected;
    }

    /// <summary>只匹配开局指定 unitId（通常 3 个），避免扫全库卡死/空引用。</summary>
    public static int ClickRecruitBootUnits(
        IMaaContext context,
        int alreadySelected,
        IReadOnlyList<int> unitIds,
        Func<int>? recount = null,
        int need = 3)
    {
        EnsureLoaded();
        var selected = Math.Max(0, alreadySelected);
        if (unitIds.Count == 0) return selected;

        var list = GetEnabledByPriority()
            .Where(c => unitIds.Contains(c.UnitId))
            .OrderBy(c =>
            {
                var i = 0;
                for (; i < unitIds.Count; i++)
                    if (unitIds[i] == c.UnitId) return i;
                return 99;
            })
            .ThenByDescending(c => c.Star)
            .ToList();

        LoggerService.LogInfo($"[DawnRealm] recruit boot-units count={list.Count} ids={string.Join(",", unitIds)}");
        foreach (var ch in list)
        {
            if (selected >= need) break;
            selected = TryClickEntry(context, ch, selected, need, recount);
        }

        return selected;
    }

    public static bool TryClickUnitIcon(
        IMaaContext context,
        int unitId,
        int preferStar = 6,
        Func<int>? recount = null)
    {
        EnsureLoaded();
        var before = recount?.Invoke() ?? 0;
        var ch = GetEnabledByPriority()
            .Where(c => c.UnitId == unitId)
            .OrderByDescending(c => c.Star == preferStar ? 1 : 0)
            .ThenByDescending(c => c.Star)
            .FirstOrDefault();
        if (ch == null) return false;
        var after = TryClickEntry(context, ch, before, before + 1, recount);
        return after > before;
    }

    private static int TryClickEntry(
        IMaaContext context,
        CharEntry ch,
        int selected,
        int need,
        Func<int>? recount)
    {
        if (selected >= need) return selected;
        var iconPath = NormalizeIcon(ch.Icon);
        if (ResolveIconFullPath(iconPath) == null) return selected;

        const int roiX = 40, roiY = 160, roiW = 1200, roiH = 420;
        RecognitionDetail? detail = null;
        try
        {
            var image = context.GetImage();
            if (!context.TemplateMatch(iconPath, image, out detail, 0.70, roiX, roiY, roiW, roiH) &&
                !context.TemplateMatch(iconPath, image, out detail, 0.62, roiX, roiY, roiW, roiH) &&
                !context.TemplateMatch(iconPath, image, out detail, 0.55, roiX, roiY, roiW, roiH))
                return selected;
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo($"[DawnRealm] recruit template err '{ch.Name}': {ex.Message}");
            return selected;
        }

        if (detail?.HitBox == null) return selected;
        var cx = detail.HitBox.X + detail.HitBox.Width / 2;
        var cy = detail.HitBox.Y + detail.HitBox.Height / 2;
        LoggerService.LogInfo(
            $"[DawnRealm] recruit pick '{ch.Name}' id={ch.UnitId} {ch.Attr}{ch.Tier}s{ch.Star} p={ch.Priority} @{cx},{cy}");
        context.Click(cx, cy);
        Thread.Sleep(400);

        var n = recount?.Invoke() ?? selected + 1;
        if (n < selected)
        {
            LoggerService.LogInfo($"[DawnRealm] recruit pick deselected ({selected}->{n}), undo");
            context.Click(cx, cy);
            Thread.Sleep(300);
            return recount?.Invoke() ?? selected;
        }

        return n > selected ? n : Math.Min(need, selected + 1);
    }

    public static void CaptureVisiblePortraits(IMaaImageBuffer image, string tag = "form")
    {
        try
        {
            EnsureLoaded();
            // inbox 已很多时暂停采集，避免刷屏（先判数量，再记节流时间）
            try
            {
                if (Directory.Exists(InboxDir) &&
                    Directory.GetFiles(InboxDir, "*.png").Length > 50)
                    return;
            }
            catch { /* ignore */ }

            if ((DateTime.UtcNow - _lastCaptureUtc).TotalSeconds < 90)
                return;
            _lastCaptureUtc = DateTime.UtcNow;
            if (!image.TryGetEncodedData(out byte[]? data) || data is null || data.Length < 100)
                return;

            var bmpType = ResolveBitmapType();
            if (bmpType == null)
            {
                var rawPath = Path.Combine(InboxDir, $"{tag}_full_{DateTime.Now:HHmmss}.png");
                File.WriteAllBytes(rawPath, data);
                LoggerService.LogInfo($"[DawnRealm] capture full frame -> {Path.GetFileName(rawPath)}");
                return;
            }

            using var ms = new MemoryStream(data);
            var bmpObj = Activator.CreateInstance(bmpType, ms);
            if (bmpObj is not IDisposable disposable) return;

            using (disposable)
            {
                var width = Convert.ToInt32(bmpType.GetProperty("Width")!.GetValue(bmpObj));
                var height = Convert.ToInt32(bmpType.GetProperty("Height")!.GetValue(bmpObj));
                var sx = width / 1280.0;
                var sy = height / 720.0;
                var getPixel = bmpType.GetMethod("GetPixel", [typeof(int), typeof(int)]);
                if (getPixel == null) return;

                (int X, int Y)[] centers =
                [
                    (80, 224), (208, 224), (368, 224), (496, 224), (624, 224), (752, 224),
                    (80, 320), (208, 320), (368, 320), (496, 320), (624, 320), (752, 320),
                ];

                var saved = 0;
                foreach (var (lx, ly) in centers)
                {
                    var cx = (int)(lx * sx);
                    var cy = (int)(ly * sy);
                    const int half = 36;
                    var x1 = Math.Clamp(cx - half, 0, width - 1);
                    var y1 = Math.Clamp(cy - half, 0, height - 1);
                    var x2 = Math.Clamp(cx + half, 0, width - 1);
                    var y2 = Math.Clamp(cy + half, 0, height - 1);
                    var cw = x2 - x1 + 1;
                    var ch = y2 - y1 + 1;
                    if (cw < 40 || ch < 40) continue;

                    long sum = 0, sumSq = 0;
                    var n = 0;
                    var samples = new List<(int R, int G, int B)>();
                    for (var y = y1; y <= y2; y += 2)
                    for (var x = x1; x <= x2; x += 2)
                    {
                        var c = getPixel.Invoke(bmpObj, [x, y])!;
                        var r = Convert.ToInt32(c.GetType().GetProperty("R")!.GetValue(c));
                        var g = Convert.ToInt32(c.GetType().GetProperty("G")!.GetValue(c));
                        var b = Convert.ToInt32(c.GetType().GetProperty("B")!.GetValue(c));
                        var v = r + g + b;
                        sum += v;
                        sumSq += v * v;
                        n++;
                        samples.Add((r, g, b));
                    }
                    if (n < 8) continue;
                    var mean = sum / (double)n;
                    var variance = sumSq / (double)n - mean * mean;
                    if (variance < 500) continue;

                    var hash = SimpleHash(samples);
                    if (!_capturedHashes.Add(hash)) continue;
                    var fileName = $"{tag}_{hash}.png";
                    var outPath = Path.Combine(InboxDir, fileName);
                    if (File.Exists(outPath)) continue;
                    if (Directory.GetFiles(CharsDir, $"*{hash}*.png").Length > 0) continue;

                    if (!SaveCropPng(bmpType, bmpObj, getPixel, x1, y1, cw, ch, outPath))
                        continue;
                    saved++;
                }

                if (saved > 0)
                    LoggerService.LogInfo($"[DawnRealm] captured {saved} portrait(s) -> chars/inbox");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo($"[DawnRealm] capture portraits err: {ex.Message}");
        }
    }

    private static string NormalizeIcon(string icon) =>
        (icon ?? "").Replace('\\', '/').TrimStart('/');

    private static string? ResolveIconFullPath(string icon)
    {
        var norm = NormalizeIcon(icon);
        if (string.IsNullOrWhiteSpace(norm)) return null;
        var full = Path.Combine(ResourceRoot, "image", norm.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? full : null;
    }

    private static bool SaveCropPng(
        Type bmpType,
        object srcBmp,
        MethodInfo getPixel,
        int x1,
        int y1,
        int cw,
        int ch,
        string outPath)
    {
        try
        {
            var ctor = bmpType.GetConstructor([typeof(int), typeof(int)]);
            if (ctor == null) return false;
            var dst = ctor.Invoke([cw, ch]);
            if (dst is not IDisposable disposable) return false;
            using (disposable)
            {
                var colorType = Type.GetType("System.Drawing.Color, System.Drawing.Common")
                                ?? Type.GetType("System.Drawing.Color, System.Drawing");
                MethodInfo? setPixel = null;
                if (colorType != null)
                    setPixel = bmpType.GetMethod("SetPixel", [typeof(int), typeof(int), colorType]);
                if (setPixel == null) return false;

                for (var y = 0; y < ch; y++)
                for (var x = 0; x < cw; x++)
                {
                    var c = getPixel.Invoke(srcBmp, [x1 + x, y1 + y])!;
                    setPixel.Invoke(dst, [x, y, c]);
                }

                var save1 = bmpType.GetMethod("Save", [typeof(string)]);
                save1?.Invoke(dst, [outPath]);
                return File.Exists(outPath);
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo($"[DawnRealm] SaveCropPng err: {ex.Message}");
            return false;
        }
    }

    private static string SimpleHash(List<(int R, int G, int B)> samples)
    {
        unchecked
        {
            long h = 17;
            var step = Math.Max(1, samples.Count / 64);
            for (var i = 0; i < samples.Count; i += step)
            {
                var (r, g, b) = samples[i];
                h = h * 31 + (r >> 3);
                h = h * 31 + (g >> 3);
                h = h * 31 + (b >> 3);
            }
            return Math.Abs(h).ToString("x8");
        }
    }

    private static void SaveCatalog(CatalogFile file)
    {
        var json = JsonSerializer.Serialize(file, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(CatalogPath, json);
        _cache = file;
        _cacheWriteUtc = File.GetLastWriteTimeUtc(CatalogPath);
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
}
