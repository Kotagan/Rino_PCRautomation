namespace MFAWPF.Services.RinoPackages;

internal sealed record RinoSidecarHandshakeResult(string RuntimeVersion, string RuntimeMode, int MaximumFrameBytes, int MinimumGraphSchemaVersion, int MaximumGraphSchemaVersion);
