// PyQuest — Phase A: SubsetInterpreter.cs
// Target: Assets/Scripts/PCG/Ast/SubsetInterpreter.cs
//
// Tree-walking executor over the IronPython AST (parse via PythonAstGateway).
// Covers the whole taught subset: long/str/bool values, `+ - * / // %`,
// precedence, comparisons (`== != < > <= >=`), `and or not`, int/str literals,
// assignment + augmented assignment (+= -= *= //= %= //=) — via PythonOperator,
// `print(...)` (single / comma / concat args), `if/elif/else`, `for x in range(a[,b])`,
// bounded `while`, `input("prompt")` via presets, `int(input(...))` (charter
// exception: intermediate+ numeric presets only — enforced by the grammar,
// re-asserted by SubsetChecker), optional `len()`.
//
// Everything is interpreted MANUALLY (no Python.CreateEngine execution, no
// reflection into IronPython runtime internals — only the parsed AST).
// Bounded by construction: iteration cap, trace-line cap, eval-node cap.
// UnityEngine-free.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using IronPython.Compiler;
using IronPython.Compiler.Ast;

namespace PyQuest.Pcg.Ast
{
    /// <summary>One line printed by print(). Inside = inside a construct body.</summary>
    public class TraceLine
    {
        public string Text;      // exact printed output of the line
        public int SourceLine;   // source line index (0-based) of the print
        public bool InsideConstruct; // inside if/for/while body
        public override string ToString() { return Text; }
    }

    /// <summary>Runtime failure with the taught-subset error kind.</summary>
    public class SubsetRuntimeError : Exception
    {
        public string Kind; // NameError | UndefinedPreset | LoopBound | TraceBudget | UnsupportedNode | ZeroDivision | TypeError
        public SubsetRuntimeError(string kind, string message) : base(message) { Kind = kind; }
    }

    public class ExecResult
    {
        public bool Success;
        public string FailureKind;
        public string FailureMessage;
        public List<TraceLine> Trace = new List<TraceLine>();
        /// <summary>code-line count (non-empty, non-blank)</summary>
        public int ExecutedLines;
        public Dictionary<string, object> FinalVars = new Dictionary<string, object>();
        /// <summary>Ordered prompts seen by input() calls (first-class D4 data)</summary>
        public List<string> InputPrompts = new List<string>();
        public override string ToString() { return Success ? string.Join("|", Trace) : "FAIL:" + FailureKind; }
    }

    public class SubsetInterpreter
    {
        // ——— execution bounds (endless-output guard, per probe lineage) ———
        public const int MaxLoopIters = 6;
        public const int MaxTraceLines = 24;
        public const int MaxEvalNodes = 10000;

        /// <summary>Preset input values, consumed in order (D4: per-line presets).</summary>
        readonly List<string> _inputs;
        int _inputIndex;

        readonly List<TraceLine> _trace = new List<TraceLine>();
        readonly Dictionary<string, object> _vars = new Dictionary<string, object>();
        readonly List<string> _prompts = new List<string>();
        int _evalNodes;
        int _loopsEntered;
        string _failureKind, _failureMessage;
        ExecResult _result;
        string _code;

        public SubsetInterpreter(List<string> inputPresets)
        {
            _inputs = inputPresets ?? new List<string>();
        }

        /// <summary>Shared ConstructSupport table — single source of truth for what the
        /// taught subset means; SubsetChecker and KcGrammar read this table.</summary>
        public static readonly string[] SupportedStatements =
        {
            "assign", "augmented_assign", "print_call", "if_elif_else", "for_range",
            "while_bounded", "input_call", "int_input_call", "str_literal", "long_literal",
            "bool_literal", "name_ref", "binary_op", "unary_not", "and_or", "conditional_expr",
            "len_call", "pass", "aug_ops_add_sub_mul_mod_div_fn"
        };

        public static bool IsSupportedBinaryOp(PythonOperator op)
        {
            switch (op)
            {
                case PythonOperator.Add: case PythonOperator.Subtract:
                case PythonOperator.Multiply: case PythonOperator.TrueDivide:
                case PythonOperator.FloorDivide: case PythonOperator.Mod:
                case PythonOperator.Equal: case PythonOperator.NotEqual:
                case PythonOperator.GreaterThan: case PythonOperator.LessThan:
                case PythonOperator.GreaterThanOrEqual: case PythonOperator.LessThanOrEqual:
                    return true;
                default: return false;
            }
        }

        // ============ entry ============

        public ExecResult Execute(PythonAst ast, string code, int maxTraceLines)
        {
            _code = code;
            _failureKind = null; _failureMessage = null;
            _evalNodes = 0; _inputIndex = 0; _loopsEntered = 0;
            _trace.Clear(); _vars.Clear(); _prompts.Clear();
            var bodySuite = ast.Body as SuiteStatement;
            List<Statement> stmts = bodySuite != null ? bodySuite.Statements.ToList() : new List<Statement> { ast.Body };
            try
            {
                ExecStmts(stmts, false);
                _result = new ExecResult
                {
                    Success = true,
                    Trace = _trace,
                    ExecutedLines = CountCodeLines(code),
                    FinalVars = new Dictionary<string, object>(_vars),
                    InputPrompts = _prompts
                };
                if (_trace.Count > Math.Max(1, maxTraceLines) && maxTraceLines >= 0)
                    _result.FailureKind = null; // budget not enforced here — SubsetChecker re-draws
            }
            catch (SubsetRuntimeError re)
            {
                _result = new ExecResult { Success = false, FailureKind = re.Kind, FailureMessage = re.Message, Trace = _trace };
            }
            catch (Exception ex)
            {
                _result = new ExecResult { Success = false, FailureKind = "Internal", FailureMessage = ex.GetType().Name + ": " + ex.Message, Trace = _trace };
            }
            return _result;
        }

        // ============ statement execution ============

        void ExecStmts(IList<Statement> stmts, bool inside)
        {
            foreach (var s in stmts) ExecStmt(s, inside);
        }

        void ExecStmt(Statement s, bool inside)
        {
            if (s is SuiteStatement suite) { ExecStmts(Body(suite.Statements), false); return; } // bodies re-flag below
            if (s is EmptyStatement) return; // pass
            if (s is ExpressionStatement es) { Expr(es.Expression, inside); return; }
            if (s is AssignmentStatement a)
            {
                var targets = a.Left;
                if (targets.Count != 1) Fail("TypeError", "only single-target assignment taught");
                var name = targets[0] as NameExpression;
                if (name == null) Fail("TypeError", "only simple name targets taught");
                var v = Expr(a.Right, inside);
                _vars[name.Name] = v;
                return;
            }
            if (s is AugmentedAssignStatement au)
            {
                var name = au.Left as NameExpression;
                if (name == null) Fail("TypeError", "augmented assign only on names");
                if (!_vars.ContainsKey(name.Name)) Fail("NameError", "augmented assign on undefined var " + name.Name);
                var cur = _vars[name.Name];
                var rhs = Expr(au.Right, inside);
                // au.Operator is the underlying binary op (Add for +=, etc.)
                _vars[name.Name] = Binary(cur, au.Operator, rhs, au);
                return;
            }
            if (s is IfStatement is_) { ExecIf(is_, inside); return; }
            if (s is ForStatement fs) { ExecFor(fs, inside); return; }
            if (s is WhileStatement ws) { ExecWhile(ws, inside); return; }
            if (s is ReturnStatement) Fail("UnsupportedNode", "return is charter-banned");
            Fail("UnsupportedNode", "statement kind " + s.GetType().Name + " is outside the taught subset");
        }

        void ExecIf(IfStatement is_, bool inside)
        {
            for (int i = 0; i < is_.Tests.Count; i++)
            {
                var t = is_.Tests[i];
                if (Truth(Expr(t.Test, true))) { ExecBody(t.Body, true); return; }
            }
            if (is_.ElseStatement != null) ExecBody(is_.ElseStatement, true);
        }

        void ExecFor(ForStatement fs, bool inside)
        {
            if (_loopsEntered >= 2) Fail("UnsupportedNode", "nested loops beyond charter (2 caps)");
            var loopName = fs.Left as NameExpression;
            if (loopName == null) Fail("TypeError", "for loop variable must be a simple name");
            // List must be a range(a[,b]) call
            var call = fs.List as CallExpression;
            if (call == null || !(call.Target is NameExpression ranged) || ranged.Name != "range")
                Fail("UnsupportedNode", "only range() loops taught");
            var a = call.Args;
            long start = 0, stop = 0;
            if (a.Count == 1) stop = RangeArg(a[0]);
            else if (a.Count == 2) { start = RangeArg(a[0]); stop = RangeArg(a[1]); }
            else Fail("UnsupportedNode", "range with 3 args unsupported");
            long iters = stop - start;
            if (iters < 0 || iters > MaxLoopIters) Fail("LoopBound", "range " + start + " to " + stop + " exceeds loop bound " + MaxLoopIters);
            _loopsEntered++;
            try
            {
                for (long i = start; i < stop; i++)
                {
                    _vars[loopName.Name] = i;
                    ExecBody(fs.Body, true);   // inside construct
                    if (_trace.Count > MaxTraceLines) Fail("TraceBudget", "printed lines exceed " + MaxTraceLines);
                }
            }
            finally { _loopsEntered--; }
        }

        void ExecWhile(WhileStatement ws, bool inside)
        {
            if (_loopsEntered >= 1) Fail("UnsupportedNode", "while may not nest (charter: loops are for-ranges; bounded while only)");
            try
            {
                _loopsEntered++;
                for (int iter = 0; iter < MaxLoopIters; iter++)
                {
                    if (!Truth(Expr(ws.Test, true))) return;
                    ExecBody(ws.Body, true);
                    if (_trace.Count > MaxTraceLines) Fail("TraceBudget", "printed lines exceed " + MaxTraceLines);
                }
            }
            finally { _loopsEntered--; }
            Fail("LoopBound", "while loop ran unbounded (" + MaxLoopIters + " iterations reached)");
        }

        void ExecBody(Statement body, bool inside)
        {
            if (body == null) return;
            if (body is SuiteStatement suite) { ExecStmts(suite.Statements, inside); return; }
            ExecStmt(body, inside);
        }

        

        // Body statements may be SuiteStatement chunks; flatten one level.
        static List<Statement> Body(IList<Statement> raw)
        {
            var flat = new List<Statement>();
            foreach (var s in raw)
            {
                if (s is SuiteStatement suite) flat.AddRange(suite.Statements);
                else flat.Add(s);
            }
            return flat;
        }

        // ============ expression evaluation ============

        object Expr(Node e, bool inside)
        {
            if (++_evalNodes > MaxEvalNodes) Fail("LoopBound", "program too large to evaluate (" + MaxEvalNodes + " node cap)");
            if (e is ConstantExpression ce) return CoerceConstant(ce);
            if (e is NameExpression ne)
            {
                if (_vars.TryGetValue(ne.Name, out var v)) return v;
                Fail("NameError", "undefined name " + ne.Name);
            }
            if (e is BinaryExpression be) return Binary(Expr(be.Left, true), be.Operator, Expr(be.Right, true), be);
            if (e is UnaryExpression ue && ue.Operator == PythonOperator.Not) return !Truth(Expr(ue.Expression, true));
            if (e is AndExpression an) return Truth(Expr(an.Left, true)) ? Truth(RhsBool(an.Right)) : false;
            if (e is OrExpression or) return Truth(Expr(or.Left, true)) ? true : Truth(RhsBool(or.Right));
            if (e is ConditionalExpression cx)
            {
                var t = Expr(cx.Test, true);
                return Truth(t) ? Expr(cx.TrueExpression, inside) : Expr(cx.FalseExpression, inside);
            }
            if (e is ParenthesisExpression pe) return Expr(pe.Expression, inside);
            if (e is TupleExpression) Fail("UnsupportedNode", "tuple targets unsupported");
            if (e is CallExpression ce2) return Call(ce2, inside);
            Fail("UnsupportedNode", "expression kind " + e.GetType().Name + " outside the taught subset");
            return null;
        }

        /// <summary>Bool RHS of and/or keeps Python truthiness (int truthiness: 0=false).</summary>
        bool RhsBool(Node n) { return Truth(Expr(n, true)); }

        object Call(CallExpression ce, bool inside)
        {
            var target = ce.Target as NameExpression;
            if (target == null) Fail("UnsupportedNode", "only calls on names taught (no methods)");
            string fn = target.Name;
            if (fn == "print")
            {
                // comma-args join with ", " (taught subset); duplicate args? treat as separate values
                var parts = new List<string>();
                foreach (var arg in ce.Args) parts.Add(Stringify(Expr(arg, inside)));
                string line = string.Join(" ", parts);
                Emit(line, inside, ce);
                return null;
            }
            if (fn == "input")
            {
                if (ce.Args.Count > 1) Fail("TypeError", "input() with more than one arg unsupported");
                if (ce.Args.Count == 1)
                {
                    // prompt may be a str literal or a variable holding one (taught subsets use literals)
                    var p = Expr(ce.Args[0], inside);
                    if (p is string ps) _prompts.Add(ps);
                    else if (p is long || p is bool) _prompts.Add(Stringify(p));
                    else Fail("TypeError", "input prompt must be a string");
                }
                else _prompts.Add("");
                if (_inputIndex >= _inputs.Count) Fail("UndefinedPreset", "no preset supplied for input() call #" + (_inputIndex + 1));
                return _inputs[_inputIndex++];
            }
            if (fn == "int")
            {
                if (ce.Args.Count != 1) Fail("TypeError", "int() takes one argument");
                var v = Expr(ce.Args[0], inside);
                if (v is string s)
                {
                    long n;
                    if (long.TryParse(s.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n)) return n;
                    Fail("TypeError", "int() got a non-numeric string preset: " + s);
                }
                if (v is long l) return l;
                if (v is bool b) return b ? 1L : 0L;
                Fail("TypeError", "int() unsupported value kind " + v?.GetType().Name);
            }
            if (fn == "str")
            {
                if (ce.Args.Count != 1) Fail("TypeError", "str() takes one argument");
                return Stringify(Expr(ce.Args[0], inside));
            }
            if (fn == "len")
            {
                if (ce.Args.Count != 1) Fail("TypeError", "len() takes one argument");
                var v = Expr(ce.Args[0], inside);
                if (v is string ls) return (long)ls.Length;
                Fail("TypeError", "len() of non-string unsupported");
            }
            Fail("UnsupportedNode", "function " + fn + "() is outside the taught subset");
            return null;
        }

        void Emit(string line, bool inside, Node ce)
        {
            _trace.Add(new TraceLine
            {
                Text = line,
                SourceLine = SourceLineIndex(ce),
                InsideConstruct = inside
            });
            if (_trace.Count > MaxTraceLines) Fail("TraceBudget", "printed lines exceed " + MaxTraceLines);
        }

        int SourceLineIndex(Node n)
        {
            if (_code == null) return 0;
            int upto = Math.Min(n.StartIndex, _code.Length);
            int line = 0;
            for (int i = 0; i < upto && i < _code.Length; i++) if (_code[i] == '\n') line++;
            return line;
        }

        static object CoerceConstant(ConstantExpression ce)
        {
            var v = ce.Value;
            if (v is int i) return (long)i;
            if (v is string || v is bool) return v;
            if (v is sbyte || v is short) return Convert.ToInt64(v);
            if (v is long) return v;
            if (v is uint || v is ushort) Fail("UnsupportedNode", "unsigned literals unsupported");
            // BigInteger (large ints): outside the taught subset
            Fail("UnsupportedNode", "literal kind " + v?.GetType().Name + " outside taught subset");
            return null;
        }

        // ============ value semantics ============

        static bool Truth(object v)
        {
            if (v is bool b) return b;
            if (v is long l) return l != 0;
            if (v is string s) return s.Length > 0;
            Fail("TypeError", "truthiness of " + v?.GetType().Name);
            return false;
        }

        static bool IsInt(object v) { return v is long || v is bool; }
        static long IntVal(object v) { return v is bool b ? (b ? 1L : 0L) : (long)v; }

        object Binary(object l, PythonOperator op, object r, Node where)
        {
            if (op == PythonOperator.Equal) return Eq(l, r);
            if (op == PythonOperator.NotEqual) return !Eq(l, r);
            if (op == PythonOperator.Add)
            {
                if (l is string || r is string)
                {
                    if (l is string && r is string) return (string)l + (string)r;
                    Fail("TypeError", "cannot mix str and int in +");
                }
                if (IsInt(l) && IsInt(r)) return checked(IntVal(l) + IntVal(r));
                Fail("TypeError", "untaught operands for +");
            }
            if (IsInt(l) && IsInt(r))
            {
                long a = IntVal(l), b = IntVal(r);
                switch (op)
                {
                    case PythonOperator.Subtract: return checked(a - b);
                    case PythonOperator.Multiply: return checked(a * b);
                    case PythonOperator.TrueDivide:
                        if (b == 0) Fail("ZeroDivision", "division by zero");
                        // Charter (§3.1 + 09 §6.3): "/ only when evenly divisible (else
                        // reject candidate — no float teaching)". A non-divisible draw is a
                        // failed candidate, never a served float.
                        if (a % b != 0) Fail("TypeError", "true division " + a + " / " + b + " is not even, so it is out of the taught subset");
                        // Python 3 always returns a float: 10/2 -> 5.0 (Stringify keeps .0)
                        return (double)(a / b);
                    case PythonOperator.FloorDivide:
                        if (b == 0) Fail("ZeroDivision", "division by zero");
                        long fdQ = a / b, fdR = a - fdQ * b;
                        if (fdR != 0 && ((fdR < 0) != (b < 0))) fdQ -= 1; // Python floors toward -inf
                        return fdQ;
                    case PythonOperator.Mod:
                        if (b == 0) Fail("ZeroDivision", "division by zero");
                        long mdQ = a / b, mdR = a - mdQ * b;
                        if (mdR != 0 && ((mdR < 0) != (b < 0))) mdQ -= 1; // floor toward -inf
                        return a - mdQ * b; // Python mod takes the divisor's sign
                    case PythonOperator.GreaterThan: return a > b;
                    case PythonOperator.LessThan: return a < b;
                    case PythonOperator.GreaterThanOrEqual: return a >= b;
                    case PythonOperator.LessThanOrEqual: return a <= b;
                    default:
                        Fail("UnsupportedNode", "operator " + op + " outside taught subset");
                        return null;
                }
            }
            // string comparisons (inputHandling: echo/compare string inputs)
            if (l is string && r is string)
            {
                string a = (string)l, b = (string)r;
                switch (op)
                {
                    case PythonOperator.Equal: return a == b;
                    case PythonOperator.NotEqual: return a != b;
                    case PythonOperator.GreaterThan: return string.CompareOrdinal(a, b) > 0;
                    case PythonOperator.LessThan: return string.CompareOrdinal(a, b) < 0;
                    case PythonOperator.GreaterThanOrEqual: return string.CompareOrdinal(a, b) >= 0;
                    case PythonOperator.LessThanOrEqual: return string.CompareOrdinal(a, b) <= 0;
                }
            }
            Fail("TypeError", "untaught operand mix for " + op + " (" + l?.GetType().Name + ", " + r?.GetType().Name + ")");
            return null;
        }

        static bool Eq(object a, object b)
        {
            if (IsInt(a) && IsInt(b)) return IntVal(a) == IntVal(b);
            if (a is string sa && b is string sb) return sa == sb;
            if (a is string && IsInt(b)) Fail("TypeError", "== between str and int (inputs are strings)");
            if (b is string && IsInt(a)) Fail("TypeError", "== between int and str (inputs are strings)");
            return object.Equals(a, b);
        }

        /// <summary>Exact Python str() / print formatting for the taught value kinds.</summary>
        public static string Stringify(object v)
        {
            if (v is string s) return s;
            if (v is bool b) return b ? "True" : "False";
            if (v is long l) return l.ToString(CultureInfo.InvariantCulture);
            if (v is double d)
            {
                // CPython float rendering: integral floats print with a trailing .0
                // (5 -> "5.0"), otherwise shortest round-trip repr.
                if (d == Math.Floor(d) && !double.IsInfinity(d))
                    return d.ToString("0.0", CultureInfo.InvariantCulture);
                return d.ToString("R", CultureInfo.InvariantCulture);
            }
            return v?.ToString() ?? "None";
        }

        long RangeArg(Node arg)
        {
            var v = Expr(arg, true);
            if (IsInt(v)) return IntVal(v);
            Fail("TypeError", "range() args must be int literals");
            return 0;
        }

        static void Fail(string kind, string message) { throw new SubsetRuntimeError(kind, message); }

        static int CountCodeLines(string code)
        {
            if (code == null) return 0;
            int n = 0;
            foreach (var l in code.Split('\n')) if (l.Trim().Length > 0) n++;
            return n;
        }
    }
}
