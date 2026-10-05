// Same pipeline as IronPythonSmokeTest V3/V5: parse + walk + spans.
using System;
using IronPython.Compiler;
using IronPython.Compiler.Ast;
using IronPython.Hosting;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting.Providers;
using Microsoft.Scripting.Runtime;

class ParseSmoke
{
    static int Main()
    {
        var engine = Python.CreateEngine();
        string code = "name = \"Ula\"\ntimes = 3\nif times > 2:\n    print(\"Hello\", name, times)";
        var source = engine.CreateScriptSourceFromString(code, SourceCodeKind.Statements);
        var unit = HostingHelpers.GetSourceUnit(source);
        var ctx = new CompilerContext(unit, new IronPython.Compiler.PythonCompilerOptions(), ErrorSink.Default);
        var ast = Parser.CreateParser(ctx, new IronPython.Runtime.PythonOptions()).ParseFile(false);
        var suite = ast.Body as SuiteStatement;
        if (suite == null) { Console.WriteLine("FAIL: Body not SuiteStatement"); return 1; }
        int i = 0;
        foreach (var st in suite.Statements)
        {
            i++;
            var s = st.Span;
            Console.WriteLine($"line {i}: {st.GetType().Name} span {s.Start.Line}:{s.Start.Column}-{s.End.Line}:{s.End.Column}");
        }
        Console.WriteLine(i >= 4 ? "PARSE OK — pipeline works with CodeDom stub" : "FAIL: wrong statement count");
        return i >= 4 ? 0 : 1;
    }
}
