using System.Diagnostics;

var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "";
var index = 1;
if (name.Contains('3')) index = 3;
else if (name.Contains('1')) index = 1;
else if (args.Length > 0 && int.TryParse(args[0], out var a)) index = a;

var mm = @"C:\Program Files\Netease\MuMu\nx_main\MuMuManager.exe";
Process.Start(new ProcessStartInfo
{
    FileName = mm,
    Arguments = $"control -v {index} launch",
    UseShellExecute = true,
    WorkingDirectory = Path.GetDirectoryName(mm)!,
});
