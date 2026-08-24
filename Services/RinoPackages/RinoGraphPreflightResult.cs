using System.Text.Json;

namespace MFAWPF.Services.RinoPackages;

internal sealed record RinoGraphPreflightResult(bool Executable, JsonElement Report);
