using MFAWPF.Helper;
using MFAWPF.Models.Characters;
using System.IO;
using System.Windows.Media.Imaging;

namespace MFAWPF.Services.Characters;

internal sealed class CharacterAnimationClip
{
    public static CharacterAnimationClip Empty { get; } = new([], []);

    public CharacterAnimationClip(
        IReadOnlyList<BitmapSource> frames,
        IReadOnlyList<TimeSpan> frameDurations)
    {
        Frames = frames;
        FrameDurations = frameDurations;
    }

    public IReadOnlyList<BitmapSource> Frames { get; }

    public IReadOnlyList<TimeSpan> FrameDurations { get; }

    public bool IsEmpty => Frames.Count == 0;
}

internal sealed class CharacterAnimationLoader
{
    private readonly CharacterStoragePaths _storagePaths;
    private const int MaximumFrameCount = 240;
    private const long MaximumSourceBytes = 64L * 1024 * 1024;
    private const long MaximumTotalPixels = 100_000_000;
    private static readonly TimeSpan DefaultFrameDuration = TimeSpan.FromMilliseconds(100);

    public CharacterAnimationLoader(CharacterStoragePaths storagePaths)
    {
        _storagePaths = storagePaths;
    }

    public CharacterAnimationClip Load(CharacterAnimationDefinition? definition)
    {
        var path = _storagePaths.ResolveAssetPath(definition?.SourcePath ?? string.Empty);
        if (path is null)
        {
            return CharacterAnimationClip.Empty;
        }

        try
        {
            return Directory.Exists(path)
                ? LoadPngSequence(path)
                : LoadFile(path);
        }
        catch (Exception ex)
        {
            LoggerService.LogError($"Failed to load desktop-character animation '{path}': {ex}");
            return CharacterAnimationClip.Empty;
        }
    }

    public BitmapSource? LoadFirstFrame(CharacterAnimationDefinition? definition) =>
        Load(definition).Frames.FirstOrDefault();

    private static CharacterAnimationClip LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            return CharacterAnimationClip.Empty;
        }

        if (new FileInfo(path).Length > MaximumSourceBytes)
        {
            return CharacterAnimationClip.Empty;
        }

        return string.Equals(Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase)
            ? LoadGif(path)
            : LoadStaticImage(path);
    }

    private static CharacterAnimationClip LoadStaticImage(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterAnimationClip.Empty;
        }

        var frame = LoadBitmap(path);
        return IsFrameSafe(frame)
            ? new CharacterAnimationClip([frame], [DefaultFrameDuration])
            : CharacterAnimationClip.Empty;
    }

    private CharacterAnimationClip LoadPngSequence(string directory)
    {
        var frames = new List<BitmapSource>();
        var durations = new List<TimeSpan>();
        long totalPixels = 0;

        foreach (var path in Directory.EnumerateFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                     .Take(MaximumFrameCount))
        {
            if (new FileInfo(path).Length > MaximumSourceBytes ||
                !_storagePaths.TryCreateRelativePath(path, out var relativePath))
            {
                continue;
            }

            var safePath = _storagePaths.ResolveAssetPath(relativePath);
            if (safePath is null)
            {
                continue;
            }

            var frame = LoadBitmap(safePath);
            if (!IsFrameSafe(frame))
            {
                continue;
            }

            totalPixels += (long)frame.PixelWidth * frame.PixelHeight;
            if (totalPixels > MaximumTotalPixels)
            {
                break;
            }

            frames.Add(frame);
            durations.Add(DefaultFrameDuration);
        }

        return frames.Count == 0
            ? CharacterAnimationClip.Empty
            : new CharacterAnimationClip(frames, durations);
    }

    private static CharacterAnimationClip LoadGif(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new GifBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frames = new List<BitmapSource>();
        var durations = new List<TimeSpan>();
        long totalPixels = 0;

        foreach (var sourceFrame in decoder.Frames.Take(MaximumFrameCount))
        {
            var frame = sourceFrame.Clone();
            frame.Freeze();
            if (!IsFrameSafe(frame))
            {
                continue;
            }

            totalPixels += (long)frame.PixelWidth * frame.PixelHeight;
            if (totalPixels > MaximumTotalPixels)
            {
                break;
            }

            frames.Add(frame);
            durations.Add(ReadGifFrameDuration(sourceFrame));
        }

        return frames.Count == 0
            ? CharacterAnimationClip.Empty
            : new CharacterAnimationClip(frames, durations);
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0].Clone();
        frame.Freeze();
        return frame;
    }

    private static TimeSpan ReadGifFrameDuration(BitmapFrame frame)
    {
        if (frame.Metadata is BitmapMetadata metadata &&
            metadata.GetQuery("/grctlext/Delay") is ushort delay)
        {
            return TimeSpan.FromMilliseconds(Math.Max(20, delay * 10));
        }

        return DefaultFrameDuration;
    }

    private static bool IsFrameSafe(BitmapSource frame) =>
        frame.PixelWidth is > 0 and <= 4096 &&
        frame.PixelHeight is > 0 and <= 4096;
}
