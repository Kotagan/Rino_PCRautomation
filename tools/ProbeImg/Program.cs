using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
var path = @"D:\workplace\bin\x64\Release\MaaFramework.Binding.dll";
var alc = new AssemblyLoadContext("probe2", true);
alc.Resolving += (c, n) => {
  var p = System.IO.Path.Combine(@"D:\workplace\bin\x64\Release", n.Name + ".dll");
  return System.IO.File.Exists(p) ? c.LoadFromAssemblyPath(p) : null;
};
var asm = alc.LoadFromAssemblyPath(path);
var t = asm.GetType("MaaFramework.Binding.Buffers.IMaaImageBuffer");
foreach (var m in t.GetMethods().Where(x => x.Name.Contains("Encoded") || x.Name is "get_Width" or "get_Height"))
{
  Console.WriteLine(m);
}
