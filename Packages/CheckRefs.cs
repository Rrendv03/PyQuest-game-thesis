// Simulates Unity's linker/AssemblyHelper walk: resolve every TypeRef/MemberRef across the set.
using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;

class CheckRefs
{
    static int Main(string[] args)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (var d in args) resolver.AddSearchDirectory(d);
        resolver.AddSearchDirectory("/usr/lib/mono/4.5");
        resolver.AddSearchDirectory("/usr/lib/mono/4.5/Facades");
        var rp = new ReaderParameters { AssemblyResolver = resolver };
        int missing = 0;
        foreach (var dir in args)
        {
            foreach (var f in System.IO.Directory.GetFiles(dir, "*.dll"))
            {
                Console.WriteLine("=== " + f);
                AssemblyDefinition mod;
                try { mod = AssemblyDefinition.ReadAssembly(f, rp); }
                catch (Exception e) { Console.WriteLine("  READ FAIL: " + e.Message); missing++; continue; }
                foreach (var r in mod.MainModule.AssemblyReferences)
                {
                    AssemblyDefinition ad;
                    try { ad = resolver.Resolve(r); }
                    catch { Console.WriteLine("  UNRESOLVABLE assembly ref: " + r.FullName); missing++; continue; }
                    Console.WriteLine("  ref ok: " + r.Name + " -> " + ad.MainModule.Assembly.Name);
                }
                int tyf = 0, mef = 0;
                foreach (var t in mod.MainModule.GetTypeReferences())
                {
                    try { if (t.Scope != null && t.Resolve() != null) tyf++;
                    } catch { try { if (t.Scope is ModuleDefinition || t.Name.Contains("<PrivateImplementationDetails>")) { tyf++; continue; } } catch {} Console.WriteLine("  UNRESOLVED typeref: " + t.FullName + "  scope=" + (t.Scope as AssemblyNameReference)?.Name); missing++; }
                }
                foreach (var t in mod.MainModule.GetMemberReferences())
                {
                    try { t.Resolve(); mef++; }
                    catch { Console.WriteLine("  UNRESOLVED memberref: " + t.FullName); missing++; }
                }
                Console.WriteLine("  typeref ok=" + tyf + " memberref ok=" + mef);
            }
        }
        Console.WriteLine(missing == 0 ? "LINKER AUDIT CLEAN — nothing missing" : ("AUDIT PROBLEMS: " + missing));
        return missing == 0 ? 0 : 1;
    }
}
