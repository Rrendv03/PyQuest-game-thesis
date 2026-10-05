// PyQuest — Phase A: SubsetChecker.cs
// Target: Assets/Scripts/PCG/Ast/SubsetChecker.cs
//
// Audits a PARSED program against the curriculum charter (09 §3.1 + §6.5):
// charter bans, strict KC presence/isolation, output budgets on the EXECUTED
// trace, tier floors, strict use-policy (reverse dataflow sweep), and boundary
// disjointness. Every violation is a NAMED string — production verifiers fail
// fast with `RULE<n>: kc=<kc> <reason>` (the D1 fix), never silent re-draws.
// Reads SubsetInterpreter.SupportedStatements (single ConstructSupport table).
// UnityEngine-free.
using System;
using System.Collections.Generic;
using System.Linq;
using IronPython.Compiler.Ast;

namespace PyQuest.Pcg.Ast
{
    public class CheckerRequest
    {
        public int Seed;
        public string Kc;          // print | variables | operations | input | conditionals | loops
        public int Tier;           // 0 beginner, 1 intermediate, 2 advanced
        public string Format;      // PTO | TF | FITB | PAC | STB | LS
        public int MaxTraceLines;  // format budget (AstPuzzleService computes: PTO/TF/FITB/PAC=1; STB/LS B:1·I:1–3·A:2–5)
        public List<string> Inputs; // presets consumed by input()
    }

    public class CheckerResult
    {
        public bool Pass;
        public List<string> Violations = new List<string>();
        public int CodeLines;
        public override string ToString() { return Pass ? "PASS" : string.Join("; ", Violations.ToArray()); }
    }

    public static class SubsetChecker
    {
        // ——— charter table (mirrors §3.1; single shared support table) ———
        // kc -> constructs the puzzle MUST feature (focal) ; scaffold = previous KCs.
        public static readonly Dictionary<string, string[]> FocalTokens = new Dictionary<string, string[]>
        {
            { "print",        new[] { "print" } },
            { "variables",    new[] { "assign" } },
            { "operations",   new[] { "binop" } },
            { "input",        new[] { "input" } },
            { "conditionals", new[] { "if" } },
            { "loops",        new[] { "for", "while" } },
        };

        // banned constructs, per charter (generation grammar never emits these;
        // the checker is the second line of defense against mutator drift)
        public static readonly string[] BannedStatementNames =
        {
            "ReturnStatement", "FunctionDefinition", "ClassDefinition", "LambdaExpression",
            "TryStatement", "RaiseStatement", "ImportStatement", "FromImportStatement",
            "GlobalStatement", "DelStatement", "ListExpression", "IndexExpression",
            "SliceExpression", "JoinedStringExpression", "FormattedValueExpression",
            "GeneratorExpression", "ListComprehension", "DictionaryExpression",
            "SetExpression", "StarredExpression"
        };

        public static int MaxPrintArgsFor(Tier t) { return t == Tier.Beginner ? 1 : t == Tier.Intermediate ? 2 : 4; }
        public enum Tier { Beginner = 0, Intermediate = 1, Advanced = 2 }
        public static Tier ToTier(int v) { return v <= 0 ? Tier.Beginner : v == 1 ? Tier.Intermediate : Tier.Advanced; }

        // ——— run the executed-trace-producing audit ———
        public static CheckerResult Check(string code, PythonAst ast, CheckerRequest req, List<TraceLine> trace)
        {
            var r = new CheckerResult();
            bool shouldRefDefinedVar = req.Kc == "print" || req.Kc == "variables" || req.Kc == "operations" || req.Kc == "input";
            var tier = ToTier(req.Tier);
            r.CodeLines = CountCodeLines(code);

            // RULE B — charter bans (AST-level)
            string banned = FirstBanned(ast);
            if (banned != null) { r.Violations.Add("RULEB: " + banned + " construct is charter-banned"); }

            // RULE 0 — in-curriculum audit against the SHARED ConstructSupport table
            // (SubsetChecker and SubsetInterpreter are two views of the same table)
            SupportViolations(ast, r.Violations);

            // RULE 1 — strict KC presence: the zone's focal construct must be present
            if (!KcPresent(ast, req.Kc))
                r.Violations.Add("RULE1: kc=" + req.Kc + " focal construct missing");

            // RULE 2 — strict KC isolation
            IsolationViolations(ast, req.Kc, req.Tier, r.Violations);

            // RULE 3 — output budget on the executed trace
            if (trace.Count < 1)
                r.Violations.Add("RULE3: trace has 0 printed lines");
            if (req.MaxTraceLines >= 0 && trace.Count > req.MaxTraceLines)
                r.Violations.Add("RULE3: trace=" + trace.Count + " exceeds budget=" + req.MaxTraceLines);

            // RULE 4 — tier floors
            TierFloorViolations(code, req, tier, r.Violations);

            // RULE 5 — strict use-policy / reverse dataflow sweep
            UsePolicyViolations(ast, trace, req.Kc, shouldRefDefinedVar, r.Violations);

            // RULE 6 — boundary disjointness (inside-construct prints vs outside prints)
            DisjointViolations(trace, r.Violations);

            // RULE 7 — int(input()) only at intermediate+ with numeric presets
            IntInputViolations(ast, req, r.Violations);

            r.Pass = r.Violations.Count == 0;
            return r;
        }

        public static int CountCodeLines(string code)
        {
            if (code == null) return 0;
            int n = 0;
            foreach (var l in code.Split('\n')) if (l.Trim().Length > 0) n++;
            return n;
        }

        // ——— RULE B ———
        public static string FirstBanned(Node node)
        {
            if (node == null) return null;
            string t = node.GetType().Name;
            foreach (var b in BannedStatementNames) if (t == b) return b;
            foreach (var c in NodeWalk.DirectChildren(node))
            {
                var f = FirstBanned(c);
                if (f != null) return f;
            }
            return null;
        }

        // ——— RULE 0 (ConstructSupport parity) ———
        /// <summary>Node kind -> ConstructSupport token. Every value MUST exist in
        /// SubsetInterpreter.SupportedStatements; SupportTableParity() asserts this so
        /// checker and interpreter can never drift apart (§6.2 single source of truth).</summary>
        public static readonly Dictionary<string, string> NodeToSupport = new Dictionary<string, string>
        {
            { "AssignmentStatement", "assign" },
            { "AugmentedAssignStatement", "augmented_assign" },
            { "IfStatement", "if_elif_else" },
            { "ForStatement", "for_range" },
            { "WhileStatement", "while_bounded" },
            { "BinaryExpression", "binary_op" },
            { "UnaryExpression", "unary_not" },
            { "AndExpression", "and_or" },
            { "OrExpression", "and_or" },
            { "ConditionalExpression", "conditional_expr" },
            { "ConstantExpression", "long_literal" },   // refined per literal below
            { "NameExpression", "name_ref" }
            // CallExpression / literals are function- and value-dependent (see tokens below)
        };

        /// <summary>Wrap-only node kinds: structural, carry no taught semantics.</summary>
        public static readonly string[] StructuralNodes =
        {
            "PythonAst", "SuiteStatement", "ExpressionStatement", "ParenthesisExpression",
            "IfStatementTest", "EmptyStatement"
        };

        /// <summary>true when every support token the checker relies on exists in the
        /// interpreter's SupportedStatements (the shared ConstructSupport table).</summary>
        public static bool SupportTableParity(out string missing)
        {
            var support = new HashSet<string>(SubsetInterpreter.SupportedStatements);
            var used = new HashSet<string>(NodeToSupport.Values);
            used.Add("print_call"); used.Add("input_call"); used.Add("int_input_call");
            used.Add("len_call"); used.Add("str_literal"); used.Add("bool_literal");
            foreach (var t in used)
                if (!support.Contains(t)) { missing = t; return false; }
            missing = null;
            return true;
        }

        /// <summary>RULE 0: any node outside the taught subset fails the audit by name.
        /// Step 5d hardening: bounded depth + visited set + try/continue.</summary>
        public static void SupportViolations(PythonAst ast, List<string> outV)
        {
            string missing;
            if (!SupportTableParity(out missing))
            {
                outV.Add("RULE0: ConstructSupport table out of sync (interpreter lacks " + missing + ")");
                return;
            }
            var support = new HashSet<string>(SubsetInterpreter.SupportedStatements);
            var unknown = new HashSet<string>();
            var visited = new HashSet<Node>();
            try { CollectUnsupported(ast, support, unknown, visited, 0); }
            catch { }
            foreach (var u in unknown)
                outV.Add("RULE0: " + u + " is outside the taught ConstructSupport subset");
        }

        const int SupportMaxDepth = 64;
        static void CollectUnsupported(Node node, HashSet<string> support, HashSet<string> unknown, HashSet<Node> visited, int depth)
        {
            if (node == null || depth > SupportMaxDepth || !visited.Add(node)) return;
            string t = node.GetType().Name;
            bool structural = Array.IndexOf(StructuralNodes, t) >= 0;
            if (!structural)
            {
                string token = null;
                if (node is CallExpression ce && ce.Target is NameExpression ne)
                {
                    if (ne.Name == "print") token = "print_call";
                    else if (ne.Name == "input") token = "input_call";
                    else if (ne.Name == "int") token = "int_input_call";
                    else if (ne.Name == "len") token = "len_call";
                    else if (ne.Name == "range") token = "for_range"; // loop head, owned by for_range
                }
                else if (node is ConstantExpression cx)
                {
                    if (cx.Value is string) token = "str_literal";
                    else if (cx.Value is bool) token = "bool_literal";
                    else if (cx.Value is int || cx.Value is long) token = "long_literal";
                }
                else if (node is UnaryExpression ue && IsUnaryNot(ue)) token = "unary_not";
                else NodeToSupport.TryGetValue(t, out token);

                if (token == null || !support.Contains(token))
                    unknown.Add(t + (token != null ? "(" + token + ")" : ""));
            }
            foreach (var c in NodeWalk.DirectChildren(node)) CollectUnsupported(c, support, unknown, visited, depth + 1);
        }

        // This IronPython build names the accessor `Operator` (not `Op`); compare the
        // operator name textually so no extra using/enum reference is needed.
        static bool IsUnaryNot(UnaryExpression ue)
        {
            object op = ue.Operator;
            return op != null && string.Equals(op.ToString(), "Not", StringComparison.Ordinal);
        }

        // ——— RULE 1 ———
        public static bool KcPresent(PythonAst ast, string kc)
        {
            var found = new List<string>();
            CollectConstructs(ast, found);
            string[] focal = FocalTokens.ContainsKey(kc) ? FocalTokens[kc] : new[] { "print" };
            foreach (var f in focal) if (found.Contains(f)) return true;
            return false;
        }

        public static void CollectConstructs(Node node, List<string> acc)
        {
            if (node == null) return;
            if (node is AssignmentStatement) { acc.Add("assign"); if (RightHasBinop(node)) acc.Add("binop"); }
            if (node is AugmentedAssignStatement) { acc.Add("assign"); acc.Add("binop"); }
            if (node is BinaryExpression) acc.Add("binop");
            if (node is IfStatement) acc.Add("if");
            if (node is ForStatement) acc.Add("for");
            if (node is WhileStatement) acc.Add("while");
            if (node is ConditionalExpression) acc.Add("condexpr");
            if (node is CallExpression ce && ce.Target is NameExpression n)
            {
                if (n.Name == "print") acc.Add("print");
                else if (n.Name == "input") acc.Add("input");
            }
            foreach (var c in NodeWalk.DirectChildren(node)) CollectConstructs(c, acc);
        }

        static bool RightHasBinop(Node assign)
        {
            foreach (var c in NodeWalk.DirectChildren(assign)) if (c is BinaryExpression) return true;
            return false;
        }

        // ——— RULE 2 (isolation) ———
        public static void IsolationViolations(PythonAst ast, string kc, int tier, List<string> outV)
        {
            if (kc == "print")
            {
                // Print Console: no binops on defined variables (constants fine inside prints)
                if (HasVarNameBinop(ast)) outV.Add("RULE2: kc=print contains binop on defined var");
            }
            if (kc == "input")
            {
                if (HasConstructNode(ast))
                    outV.Add("RULE2: kc=input contains a conditional/loop construct (Input Mists isolation)");
            }
            if (kc == "variables")
            {
                // Vars Vault emits binops only when zone KC is operations
                if (RightHasBinop(ast)) outV.Add("RULE2: kc=variables contains binop");
            }
            if (kc == "conditionals")
            {
                if (HasForOrWhile(ast)) outV.Add("RULE2: kc=conditionals contains a loop");
                if (HasNestedIf(ast)) outV.Add("RULE2: kc=conditionals contains a nested if (banned at tier<=2)");
            }
            if (kc == "loops")
            {
                if (HasWhileNested(ast) || (HasBothForWhile(ast)))
                    outV.Add("RULE2: kc=loops mixes while with for/nested loop beyond charter");
            }
        }

        public static bool HasVarNameBinop(Node node) { return HasBinopOnNonLiteral(node); }
        public static bool HasConstructNode(Node node)
        {
            if (node is IfStatement || node is ForStatement || node is WhileStatement) return true;
            foreach (var c in NodeWalk.DirectChildren(node)) if (HasConstructNode(c)) return true;
            return false;
        }
        public static bool RightHasFor(Statement s) { return HasForOrWhile(s); }

        static bool HasForOrWhile(Node node)
        {
            if (node is ForStatement || node is WhileStatement) return true;
            foreach (var c in NodeWalk.DirectChildren(node)) if (HasForOrWhile(c)) return true;
            return false;
        }
        static bool HasBothForWhile(Node node) { return HasNodeKind(node, "ForStatement") && HasNodeKind(node, "WhileStatement"); }
        static bool HasWhileNested(Node node) { return HasBothForWhile(node); }
        static bool HasNestedIf(Node node)
        {
            if (node is IfStatement outer)
            {
                if (ContainsIf(outer.Tests)) return true;
            }
            foreach (var c in NodeWalk.DirectChildren(node)) if (HasNestedIf(c)) return true;
            return false;
        }
        static bool ContainsIf(IList<IfStatementTest> tests)
        {
            foreach (var t in tests) if (ContainsIf(t.Body)) return true;
            return false;
        }
        static bool ContainsIf(Statement s)
        {
            if (s == null) return false;
            if (s is IfStatement) return true;
            if (s is SuiteStatement suite) { foreach (var t in suite.Statements) if (ContainsIf(t)) return true; }
            return false;
        }
        static bool HasNodeKind(Node node, string name)
        {
            if (node.GetType().Name == name) return true;
            foreach (var c in NodeWalk.DirectChildren(node)) if (HasNodeKind(c, name)) return true;
            return false;
        }

        static bool HasBinopOnNonLiteral(Node node)
        {
            if (node is BinaryExpression be)
                if (be.Left is NameExpression || be.Right is NameExpression) return true;
            foreach (var c in NodeWalk.DirectChildren(node)) if (HasBinopOnNonLiteral(c)) return true;
            return false;
        }

        // ——— RULE 4 (tier floors) ———
        public static void TierFloorViolations(string code, CheckerRequest req, Tier tier, List<string> outV)
        {
            int lines = CountCodeLines(code);
            if (tier == Tier.Advanced)
            {
                if (lines < 2) outV.Add("RULE4: advanced tier forbids one-liners (lines=" + lines + ")");
            }
        }

        // ——— RULE 5 (use-policy) ———
        public static void UsePolicyViolations(PythonAst ast, List<TraceLine> trace, string kc, bool mustRefVar, List<string> outV)
        {
            // collect names defined by assignments (in source order)
            var defined = new HashSet<string>();
            var read = new HashSet<string>();
            CollectAssignRead(ast, defined, read);
            var printed = new HashSet<string>();
            foreach (var t in trace) printed.Add("s:" + t.Text);

            // 5a: every print references a defined variable when one exists
            bool anyDefined = mustRefVar && defined.Count > 0;
            bool printRefsVar = PrintRefsVar(ast, defined);
            if (anyDefined && !printRefsVar)
                outV.Add("RULE5: defined variables exist but no print references one");

            // 5b: every defined var is reachable (printed, compared, or feeds chain to one that is)
            var reachableNames = CollectedReachableNames(ast);
            foreach (var d in defined)
                if (!reachableNames.Contains(d))
                    outV.Add("RULE5: dead variable " + d + " (never used by print/condition/chain)");
        }

        public static void CollectAssignRead(Node node, HashSet<string> defined, HashSet<string> read)
        {
            if (node is AssignmentStatement a)
            {
                foreach (var l in a.Left) if (l is NameExpression ne) defined.Add(ne.Name);
                CollectNames(a.Right, read);
            }
            if (node is AugmentedAssignStatement aa)
            {
                if (aa.Left is NameExpression an) defined.Add(an.Name);
                CollectNames(aa.Right, read);
            }
            foreach (var c in NodeWalk.DirectChildren(node)) CollectAssignRead(c, defined, read);
        }

        static void CollectNames(Node n, HashSet<string> read)
        {
            if (n is NameExpression ne) read.Add(ne.Name);
            foreach (var c in NodeWalk.DirectChildren(n)) CollectNames(c, read);
        }

        static bool PrintRefsVar(Node node, HashSet<string> defined)
        {
            if (node is CallExpression ce && ce.Target is NameExpression t && t.Name == "print")
            {
                var names = new HashSet<string>();
                foreach (var arg in ce.Args) CollectNames(arg, names);
                if (names.Overlaps(defined)) return true;
            }
            foreach (var c in NodeWalk.DirectChildren(node)) if (PrintRefsVar(c, defined)) return true;
            return false;
        }

        /// <summary>Reverse dataflow: names used by any print/condition/assignment-RHS,
        /// transitively (a var feeds a chain var that IS reachable).</summary>
        public static HashSet<string> CollectedReachableNames(PythonAst ast)
        {
            var usedDirectly = new HashSet<string>();
            var assignedMaps = new Dictionary<string, Node>(); // var -> RHS node
            CollectUsages(ast, usedDirectly, assignedMaps);
            var reachable = new HashSet<string>(usedDirectly.Where(IsVarName));
            // forward closure: any var that a REACHABLE var's RHS reads is itself reachable
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var kv in assignedMaps)
                {
                    if (!reachable.Contains(kv.Key)) continue;
                    var names = new HashSet<string>();
                    CollectNames(kv.Value, names);
                    foreach (var nm in names) { if (IsVarName(nm) && reachable.Add(nm)) grew = true; }
                }
            }
            return reachable;
        }

        static bool IsVarName(string n) { return char.IsLetter(n, 0); }

        static void CollectUsages(Node node, HashSet<string> direct, Dictionary<string, Node> rhs)
        {
            if (node is AssignmentStatement asg)
            {
                var tgt = asg.Left.Count == 1 ? asg.Left[0] as NameExpression : null;
                if (tgt != null) rhs[tgt.Name] = asg.Right;
            }
            if (node is AugmentedAssignStatement aa)
            {
                var t2 = aa.Left as NameExpression;
                if (t2 != null) rhs[t2.Name] = node; // augmented assign: reads itself too
                CollectNames(aa.Right, direct);
            }
            if (node is CallExpression ce && ce.Target is NameExpression t && (t.Name == "print" || t.Name == "int" || t.Name == "str" || t.Name == "len"))
                foreach (var arg in ce.Args) CollectNames(arg, direct);
            var conditions = node as IfStatement;
            if (conditions != null)
                foreach (var tt in conditions.Tests) CollectNames(tt.Test, direct);
            if (node is WhileStatement ws) CollectNames(ws.Test, direct);
            foreach (var c in NodeWalk.DirectChildren(node)) CollectUsages(c, direct, rhs);
        }

        // ——— RULE 6 (disjointness) ———
        public static void DisjointViolations(List<TraceLine> trace, List<string> outV)
        {
            var inside = new HashSet<string>();
            var outside = new HashSet<string>();
            foreach (var t in trace)
                (t.InsideConstruct ? inside : outside).Add(t.Text);
            foreach (var i in inside)
                if (outside.Contains(i)) outV.Add("RULE6: printed element \"" + i + "\" repeats across the construct boundary");
        }

        // ——— RULE 7 ———
        public static void IntInputViolations(PythonAst ast, CheckerRequest req, List<string> outV)
        {
            if (HasIntInputCall(ast) && req.Tier < 1)
                outV.Add("RULE7: int(input()) used below intermediate tier");
        }

        public static bool HasIntInputCall(Node node)
        {
            if (node is CallExpression outer && outer.Target is NameExpression n && n.Name == "int")
            {
                foreach (var arg in outer.Args)
                    if (arg is CallExpression ce && ce.Target is NameExpression t && t.Name == "input") return true;
            }
            foreach (var c in NodeWalk.DirectChildren(node)) if (HasIntInputCall(c)) return true;
            return false;
        }
    }
}
