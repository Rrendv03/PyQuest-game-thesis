// PyQuest — Phase A: PythonAstGateway.cs
// Target: Assets/Scripts/PCG/Ast/PythonAstGateway.cs
//
// Role: the ONLY script allowed to reference the IronPython DLLs. Parse-only.
// Uses the verified low-level pipeline from 10_ironpython_install.md Step 5:
//   engine.CreateScriptSourceFromString -> HostingHelpers.GetSourceUnit ->
//   CompilerContext(unit, PythonCompilerOptions, ErrorSink.Default) ->
//   Parser.CreateParser(ctx, PythonOptions).ParseFile(false)
// The parser infrastructure is created ONCE and reused for every request
// (production rule from install doc: "create the engine/infrastructure once").
// UnityEngine-free; safe for any assembly.
using System;
using IronPython.Compiler;
using IronPython.Compiler.Ast;
using IronPython.Hosting;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Hosting.Providers;
using Microsoft.Scripting.Runtime;

namespace PyQuest.Pcg.Ast
{
    public static class PythonAstGateway
    {
        static ScriptEngine _engine;
        static bool _initializationFailed;
        static string _initError;

        /// <summary>Parse-generated (not synthetic) failure message from the last Parse call.</summary>
        public static string LastError { get; private set; }

        /// <summary>
        /// True when the IronPython plugin loaded and can be used for parsing.
        /// When false, AstPuzzleService must fall back to the legacy template pool.
        /// </summary>
        public static bool Available
        {
            get
            {
                EnsureEngine();
                return _engine != null;
            }
        }

        /// <summary>Why the plugin is unavailable (null when Available).</summary>
        public static string UnavailableReason
        {
            get { EnsureEngine(); return _engine == null ? _initError : null; }
        }

        static void EnsureEngine()
        {
            if (_engine != null || _initializationFailed) return;
            try
            {
                // Created once per process — never per request.
                _engine = Python.CreateEngine();
            }
            catch (Exception ex)
            {
                _initializationFailed = true;
                _initError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>
        /// Parse the given Python 3.4-syntax source. Returns a STANDALONE AST.
        /// On any failure, returns null and sets LastError (never throws).
        /// </summary>
        public static PythonAst Parse(string code)
        {
            EnsureEngine();
            if (_engine == null) return null;
            try
            {
                var source = _engine.CreateScriptSourceFromString(code, SourceCodeKind.Statements);
                var unit = HostingHelpers.GetSourceUnit(source);
                var ctx = new CompilerContext(unit, new PythonCompilerOptions(), ErrorSink.Default);
                var parser = Parser.CreateParser(ctx, new IronPython.Runtime.PythonOptions());
                var ast = parser.ParseFile(false);
                LastError = ast == null ? "ParseFile returned null" : null;
                return ast;
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        /// <summary>Convenience: does this source parse cleanly (no syntax errors)?
        /// Uses a dedicated error collector so genuinely malformed code is caught.</summary>
        public static bool TryParse(string code, out PythonAst ast, out string error)
        {
            error = null; ast = null;
            EnsureEngine();
            if (_engine == null) { error = _initError ?? "gateway unavailable"; return false; }
            try
            {
                var source = _engine.CreateScriptSourceFromString(code, SourceCodeKind.Statements);
                var unit = HostingHelpers.GetSourceUnit(source);
                var sink = new CollectingErrorSink();
                var ctx = new CompilerContext(unit, new PythonCompilerOptions(), sink);
                var parser = Parser.CreateParser(ctx, new IronPython.Runtime.PythonOptions());
                ast = parser.ParseFile(false);
                if (ast == null) { error = "ParseFile returned null"; return false; }
                if (sink.ErrorCount > 0) { ast = null; error = sink.UselessMessage; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>Substring of <paramref name="code"/> for a parsed node's source span
        /// (used by AstMutators for splice surgery).</summary>
        public static string SpanSource(string code, Node node)
        {
            if (node == null) return null;
            int start = node.StartIndex, end = node.EndIndex;
            if (start < 0 || end > code.Length || start > end) return null;
            return code.Substring(start, end - start);
        }

        /// <summary>AST of a single expression (for inspecting a print's argument lists etc.).</summary>
        public static Node ParseExpression(string expr)
        {
            EnsureEngine();
            if (_engine == null) return null;
            try
            {
                var source = _engine.CreateScriptSourceFromString(expr, SourceCodeKind.Expression);
                var unit = HostingHelpers.GetSourceUnit(source);
                var ctx = new CompilerContext(unit, new PythonCompilerOptions(), ErrorSink.Default);
                var parser = Parser.CreateParser(ctx, new IronPython.Runtime.PythonOptions());
                return parser.ParseTopExpression(); // this IronPython build has no ParseSingleExpression
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>ErrorSink that counts syntax errors and keeps the first message.</summary>
    public class CollectingErrorSink : ErrorSink
    {
        public int ErrorCount { get; private set; }
        public string UselessMessage { get; private set; }
        void Add(SourceSpan span, string message, Severity type, int errorCode)
        {
            if (type == Severity.Error || type == Severity.FatalError)
            {
                ErrorCount++;
                if (UselessMessage == null) UselessMessage = message;
            }
        }
    }
}