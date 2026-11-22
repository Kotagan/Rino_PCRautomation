using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var dir = AppContext.BaseDirectory;
        var script = Path.Combine(dir, "adb_mumu.py");
        if (!File.Exists(script))
        {
            // when published, base dir is publish/; script sits next to project or copied
            var alt = Path.GetFullPath(Path.Combine(dir, "..", "adb_mumu.py"));
            if (File.Exists(alt)) script = alt;
        }
        var psi = new ProcessStartInfo
        {
            FileName = "python",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(script);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var tOut = p.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
        var tErr = p.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        await Task.WhenAll(tOut, tErr);
        await p.WaitForExitAsync();
        return p.ExitCode;
    }
}
