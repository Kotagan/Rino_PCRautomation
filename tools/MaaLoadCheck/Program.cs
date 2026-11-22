using MaaFramework.Binding;

var resourcePath = args.Length > 0
    ? args[0]
    : @"D:\workplace\bin\x64\Release\Resource\base";

Console.WriteLine($"cwd={Directory.GetCurrentDirectory()}");
Console.WriteLine($"Loading: {resourcePath}");
try
{
    using var res = new MaaResource(resourcePath);
    Console.WriteLine("PASS");
    Environment.ExitCode = 0;
}
catch (Exception ex)
{
    Console.WriteLine("FAIL");
    Console.WriteLine(ex.GetType().FullName);
    Console.WriteLine(ex.Message);
    if (ex.InnerException != null)
        Console.WriteLine(ex.InnerException.Message);
    Environment.ExitCode = 1;
}
