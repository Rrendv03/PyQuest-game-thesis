// Assembly-ref-only audit: the exact thing Unity's linker failed on (IL1005).
using System;
using System.Linq;
using Mono.Cecil;
class CheckAsmRefs
{
    static int Main(string[] args)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (var d in args) resolver.AddSearchDirectory(d);
        resolver.AddSearchDirectory("/usr/lib/mono/4.5");
        int missing = 0;
        foreach (var dir in args)
            foreach (var f in System.IO.Directory.GetFiles(dir, "*.dll").Where(f => !f.EndsWith(".codeless.dll")))
            {
                try {
                    var ad = AssemblyDefinition.ReadAssembly(f, new ReaderParameters { AssemblyResolver = resolver });
                    foreach (var r in ad.MainModule.AssemblyReferences)
                    {
                        try { resolver.Resolve(r); }
                        catch { Console.WriteLine("UNRESOLVABLE: " + System.IO.Path.GetFileName(f) + " -> " + r.FullName); missing++; }
                    }
                    Console.WriteLine("audited: " + System.IO.Path.GetFileName(f) + " (refs=" + ad.MainModule.AssemblyReferences.Count + ")");
                } catch (Exception e) { Console.WriteLine("READ FAIL " + f + ": " + e.Message); missing++; }
            }
        Console.WriteLine(missing == 0 ? "ASSEMBLY GRAPH CLEAN — every ref resolves" : "MISSING: " + missing);
        return missing;
    }
}
