using System;
using System.Collections.Generic;
using System.IO;

namespace MFAWPF.Services.RinoPackages;

internal sealed record RinoSidecarLaunchOptions(string ExecutablePath, IReadOnlyList<string> Arguments)
{
	public static RinoSidecarLaunchOptions CreateDefault(RinoPackageStoragePaths storagePaths)
	{
		string text = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		string fullPath = Path.GetFullPath(Path.Combine(text, "rino-runtime.exe"));
		if (!Path.GetDirectoryName(fullPath).Equals(text, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("The Rino runtime path escaped the application directory.");
		}
		string text2 = Path.Combine(storagePaths.RootDirectory, "runtime-preview");
		return new RinoSidecarLaunchOptions(fullPath, new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "--preview-cache-directory", text2 }));
	}
}
