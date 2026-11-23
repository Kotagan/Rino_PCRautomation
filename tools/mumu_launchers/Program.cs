using System.Diagnostics;

// 独立启动器：文件名含 1/3 决定多开号；也可传参。
// 复制为 RinoDevice1.exe / RinoDevice3.exe 后，快捷方式各指向不同 exe，任务栏不会并成一个。
var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "";
var index = 1;
if (name.Contains('3')) index = 3;
else if (name.Contains('1')) index = 1;
else if (args.Length > 0 && int.TryParse(args[0], out var a)) index = a;

var mm = @"C:\Program Files\Netease\MuMu\nx_main\MuMuManager.exe";
if (File.Exists(mm))
{
    Process.Start(new ProcessStartInfo
    {
        FileName = mm,
        Arguments = $"control -v {index} launch",
        UseShellExecute = true,
        WorkingDirectory = Path.GetDirectoryName(mm)!,
    });
}

var rinoDir = AppContext.BaseDirectory;
var rinoExe = Path.Combine(rinoDir, "Rino_PCRautomation.exe");
if (!File.Exists(rinoExe))
{
    // 启动器若放在 tools 发布目录，回退到 Release
    rinoExe = @"D:\workplace\bin\x64\Release\Rino_PCRautomation.exe";
    rinoDir = Path.GetDirectoryName(rinoExe)!;
}

if (!File.Exists(rinoExe))
{
    Console.Error.WriteLine($"Rino not found: {rinoExe}");
    return 1;
}

Process.Start(new ProcessStartInfo
{
    FileName = rinoExe,
    Arguments = $"--instance {index}",
    UseShellExecute = true,
    WorkingDirectory = rinoDir,
});

return 0;
