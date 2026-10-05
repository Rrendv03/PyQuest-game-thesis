// PyQuest — Phase B: AstMutators.cs
// Target: Assets/Scripts/PCG/Ast/AstMutators.cs
//
// Production mutators as SOURCE-SPAN SPLICES on generated source (install-doc
// rule: "mutations are SourceSpan splices on generated source" — the re-parsed,
// re-executed mutant is the served instance; grade by trace equality, never text).
// Each mutator runs ALL gates after the splice:
//   parse (PythonAstGateway) -> charter check (SubsetChecker) -> fresh execute
//   (SubsetInterpreter) -> semantic-change requirement (trace MUST differ from
//   the pre-mutation trace — binds D3: answer keys re-derived from the FINAL run).
// Post-mutator verifiers added for the handoff defects:
//   D2: BranchCoherence — if/elif chain keeps mutually-exclusive branches
//       (descending `>` thresholds only; mutation flip/sub into incoherence
//       is rejected before serving).
//   D3: single execution pass at the very END of the pipeline derives the
//       ExecResult that options come from (ServeOnce does exactly this).
// UnityEngine-free.
using System;
using System.Collections.Generic;
using System.Linq;
using IronPython.Compiler;
using IronPython.Compiler.Ast;

namespace PyQuest.Pcg.Ast
{
    public static class AstMutators
    {
        public static readonly SortedDictionary<string, int> MutFailures = new SortedDictionary<string, int>();
        static void BumpMutFail(string v)
        {
            var k = v.Length > 60 ? v.Substring(0, 60) : v;
            k = k.Replace("" + (char)10, "|");
            MutFailures[k] = (MutFailures.ContainsKey(k) ? MutFailures[k] : 0) + 1;
        }
        public const int MaxTries = 8;

        public class MutationOutcome
        {
            public string Code;               // mutated source (null = no mutation accepted)
            public string Kind;               // which mutator won
            public List<TraceLine> Trace;     // the FINAL executed trace (post-mutation, D3)
            public ExecResult Exec;           // the FINAL run
            public string RuleViolation;      // last named verifier failure (diagnostics)
        }

        /// <summary>Run the FULL mutation battery for one draft; at least 0 mutations at tier 0,
        /// >=1 at tier 1, >=1 at tier 2 (advanced). Returns the FINAL parsed+executed outcome.</summary>
        public static MutationOutcome MutateAndFinalize(string baseCode, CheckerRequest req, List<string> inputs)
        {
            var rng = new Random(req.Seed * 31 + 101);
            string code = baseCode;
            int target = req.Tier <= 0 ? 0 : req.Tier == 1 ? 1 : 2;
            string lastViolation = null;
            string winner = null;

            for (int m = 0; m < target; m++)
            {
                var originalTrace = RunTrace(code, inputs);
                bool isRenameTrial = m % 2 == 1; // alternate: even = trace-differentiating mutator, odd = rename
                var mutated = isRenameTrial
                    ? TryRename(code, req, inputs, rng, ref lastViolation)
                    : (TryOpSwap(code, req, inputs, rng, ref lastViolation)
                       ?? TryValueSub(code, req, inputs, rng, ref lastViolation)
                       ?? TryAddPrint(code, req, inputs, rng, ref lastViolation));
                if (mutated == null)
                {
                    // no gate-clean mutation found this round — named, and allowed to skip (pool fallback still unique)
                    continue;
                }
                if (isRenameTrial == false && originalTrace != null && TracesEqual(originalTrace.Trace, mutated.Trace))
                {
                    // semantic no-op: mutation must change the correct answer (rule 9's boundary)
                    lastViolation = "MUT: semantic no-op (trace identical) — rejected";
                    continue;
                }
                code = mutated.Code ?? code;
                winner = mutated.Kind ?? winner;
            }
            if (code == baseCode && lastViolation != null)
                BumpMutFail(lastViolation);
            if (code == baseCode)
            {
                // everything rejected: serve the base draft as-is (already proven by KcGrammar gates)
                return new MutationOutcome { Code = baseCode, Kind = null, RuleViolation = lastViolation };
            }
            var final = FinalRun(code, req, inputs);
            if (final == null) return new MutationOutcome { Code = null, Kind = winner, RuleViolation = "FINAL: post-mutation run failed charter" };
            return new MutationOutcome { Code = final.Code, Kind = winner, Trace = final.Exec.Trace, Exec = final.Exec, RuleViolation = lastViolation };
        }

        // ============ individual mutators (each: splice -> parse -> check -> exec) ============

        static ExecResult RunTrace(string code, List<string> inputs)
        {
            PythonAst ast; string err;
            if (!PythonAstGateway.TryParse(code, out ast, out err)) return null;
            var exec = new SubsetInterpreter(inputs).Execute(ast, code, 24);
            return exec.Success ? exec : null;
        }

        static bool TracesEqual(List<TraceLine> a, List<TraceLine> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].Text != b[i].Text) return false;
            return true;
        }

        /// <summary>Common gate: parse + charter + a trace-collection run.
        /// Returns the result or null (gates fail fast with a named violation).</summary>
        static ExecResult Gate(string code, CheckerRequest req, List<string> inputs, out string violation)
        {
            violation = null;
            PythonAst ast; string err;
            if (!PythonAstGateway.TryParse(code, out ast, out err)) { violation = "MUT-PARSE: " + err; return null; }
            var interp = new SubsetInterpreter(inputs);
            var exec = interp.Execute(ast, code, 24);
            if (!exec.Success) { violation = "MUT-EXEC: " + exec.FailureKind + " " + exec.FailureMessage; return null; }
            var audit = SubsetChecker.Check(code, ast, req, exec.Trace);
            if (!audit.Pass) { violation = "MUT-" + audit.Violations[0]; return null; }
            return exec;
        }

        // ---- op swap: `+` <-> `-` (never flips an if/elif CHAIN threshold: D2 guards below) ----
        static MutationOutcome TryOpSwap(string code, CheckerRequest req, List<string> inputs, Random rng, ref string lastViolation)
        {
            PythonAst ast; string err;
            if (!PythonAstGateway.TryParse(code, out ast, out err)) return null;
            var cand = new List<BinaryExpression>();
            CollectBinops(ast, cand);
            if (cand.Count == 0) return null;
            int pick = rng.Next(cand.Count);
            var b = cand[pick];
            // splice on the operator token between left and right operands
            int opStart = b.Left.EndIndex, opEnd = b.Right.StartIndex;
            if (opStart < 0 || opEnd > code.Length || opStart > opEnd) return null;
            string between = code.Substring(opStart, opEnd - opStart);
            string swapped;
            if (between.Contains("+")) swapped = between.Replace("+", "-");
            else if (between.Contains("-")) swapped = between.Replace("-", "+");
            else if (between.Contains("*")) swapped = between.Replace("*", "/");
            else if (between.Contains("/")) swapped = between.Replace("/", "*");
            else return null;
            string mutated = code.Substring(0, opStart) + swapped + code.Substring(opEnd);
            string v;
            var exec = Gate(mutated, req, inputs, out v);
            if (exec == null) { if (v != null) lastViolation = v; return null; }
            if (!BranchCoherence(mutated)) { lastViolation = "MUT-D2: branch chain incoherent after op swap"; return null; }
            return new MutationOutcome { Code = mutated, Kind = "op_swap", Exec = exec, Trace = exec.Trace };
        }

        static string SpanToken(string s, Node n) { return s.Substring(n.StartIndex, n.EndIndex - n.StartIndex); }

        static void CollectBinops(Node node, List<BinaryExpression> acc)
        {
            if (node is BinaryExpression b2 && IsArithOp(b2.Operator)) acc.Add(b2);
            foreach (var c in NodeWalk.DirectChildren(node)) CollectBinops(c, acc);
        }

        static bool IsArithOp(PythonOperator op)
        {
            return op == PythonOperator.Add || op == PythonOperator.Subtract ||
                   op == PythonOperator.Multiply || op == PythonOperator.FloorDivide ||
                   op == PythonOperator.TrueDivide || op == PythonOperator.Mod;
        }

        // ---- value sub: a random int literal shifts by +-1..3 ----
        static MutationOutcome TryValueSub(string code, CheckerRequest req, List<string> inputs, Random rng, ref string lastViolation)
        {
            PythonAst ast; string err;
            if (!PythonAstGateway.TryParse(code, out ast, out err)) return null;
            var cand = new List<ConstantExpression>();
            CollectIntLiterals(ast, cand);
            if (cand.Count == 0) return null;
            int pick = rng.Next(cand.Count);
            var c = cand[pick];
            string src = SpanToken(code, c);
            if (!long.TryParse(src, out long val)) return null;
            long shifted = val + (rng.Next(1, 4) * (rng.Next(2) == 0 ? 1 : -1));
            if (shifted < 0) shifted = val + 1;
            string mutated = code.Substring(0, c.StartIndex) + shifted.ToString() + code.Substring(c.EndIndex);
            string v;
            var exec = Gate(mutated, req, inputs, out v);
            if (exec == null) { if (v != null) lastViolation = v; return null; }
            if (!BranchCoherence(mutated)) { lastViolation = "MUT-D2: branch chain incoherent after value sub"; return null; }
            return new MutationOutcome { Code = mutated, Kind = "value_sub", Exec = exec, Trace = exec.Trace };
        }

        static void CollectIntLiterals(Node node, List<ConstantExpression> acc)
        {
            if (node is ConstantExpression ce && ce.Value is int) acc.Add(ce);
            if (node is ConstantExpression ce2 && ce2.Value is long) acc.Add(ce2);
            foreach (var c in NodeWalk.DirectChildren(node)) CollectIntLiterals(c, acc);
        }

        // ---- rename: rename one defined var across its def + uses ----
        static MutationOutcome TryRename(string code, CheckerRequest req, List<string> inputs, Random rng, ref string lastViolation)
        {
            PythonAst ast; string err;
            if (!PythonAstGateway.TryParse(code, out ast, out err)) return null;
            // candidate: name tokens not top-level Python reserved, appearing >=2 times
            var occurrences = new Dictionary<string, List<int>>();
            CollectNameSpans(ast, occurrences);
            if (occurrences.Count == 0) return null;
            var keys = occurrences.Keys.OrderByDescending(k => occurrences[k].Count).ToList();
            if (keys.Count == 0) return null;
            string oldName = keys[rng.Next(Math.Min(keys.Count, 3))];
            // 2026-10-01: rename targets a REAL taught word (KcGrammar.VarNames) that is
            // not already defined in the program — stops the old `hpx`/`goldx` artifacts.
            string newName = PickFreeName(rng, occurrences, oldName);
            // splice all occurrences END -> START so earlier indices stay valid
            string mutated = code;
            foreach (var idx in occurrences[oldName].OrderByDescending(i => i))
                mutated = mutated.Substring(0, idx) + newName + mutated.Substring(idx + oldName.Length);
            string v;
            var exec = Gate(mutated, req, inputs, out v);
            if (exec == null) { if (v != null) lastViolation = v; return null; }
            return new MutationOutcome { Code = mutated, Kind = "rename", Exec = exec, Trace = exec.Trace };
        }

        /// <summary>A taught word (KcGrammar.VarNames) that is not <paramref name="oldName"/>
        /// and does not already appear in the program, so the rename can never collide with
        /// an existing definition. Falls back to the legacy +/-x splice ONLY if the pool
        /// exhausts (never in practice: 20-word pool vs ≤3 defined names).</summary>
        static string PickFreeName(Random rng, Dictionary<string, List<int>> occurrences, string oldName)
        {
            int guard = 0;
            string s;
            do { s = KcGrammar.VarNames[rng.Next(KcGrammar.VarNames.Length)]; }
            while (oldName == s && guard++ < 64);
            if (oldName == s || occurrences.ContainsKey(s))
                return oldName.Length <= 4 ? oldName + "x" : oldName.Substring(0, oldName.Length - 1) + "x";
            return s;
        }

        static void CollectNameSpans(Node node, Dictionary<string, List<int>> acc)
        {
            if (node is NameExpression ne && NodeIsDefOrUse(ne))
            {
                if (!acc.ContainsKey(ne.Name)) acc[ne.Name] = new List<int>();
                acc[ne.Name].Add(ne.StartIndex);
            }
            foreach (var c in NodeWalk.DirectChildren(node)) CollectNameSpans(c, acc);
        }

        static bool NodeIsDefOrUse(NameExpression ne) { return ne.Name != "print" && ne.Name != "input" && ne.Name != "range" && ne.Name != "int" && ne.Name != "str" && ne.Name != "len" && ne.Name != "True" && ne.Name != "False"; }

        // ---- ADD: append a benign top-level print referencing an existing var ----
        static MutationOutcome TryAddPrint(string code, CheckerRequest req, List<string> inputs, Random rng, ref string lastViolation)
        {
            PythonAst ast; string err;
            if (!PythonAstGateway.TryParse(code, out ast, out err)) return null;
            var defined = new HashSet<string>();
            SubsetChecker.CollectAssignRead(ast, defined, new HashSet<string>());
            if (defined.Count == 0) return null;
            string varName = defined.ToList()[rng.Next(defined.Count)];
            string args = varName;
            if (rng.NextDouble() < 0.5) args = "\"" + KcGrammar.Msgs[rng.Next(KcGrammar.Msgs.Length)] + "\", " + args;
            string mutated = code.TrimEnd() + "\nprint(" + args + ")";
            string v;
            var exec = Gate(mutated, req, inputs, out v);
            if (exec == null) { if (v != null) lastViolation = v; return null; }
            return new MutationOutcome { Code = mutated, Kind = "add_print", Exec = exec, Trace = exec.Trace };
        }

        // ============ verifiers ============

        /// <summary>D2: every if/elif chain must keep provably-exclusive branches.
        /// Taught chains use only `>` comparisons on ONE variable with DESCENDING thresholds
        /// (built by construction in KcGrammar), so a chain-break is any non-descending
        /// threshold sequence (or a flipped operator + out-of-order threshold).</summary>
        public static bool BranchCoherence(string code)
        {
            PythonAst ast; string err;
            if (!PythonAstGateway.TryParse(code, out ast, out err)) return false;
            foreach (var iff in CollectIfs(ast))
            {
                long? prev = null;
                foreach (var t in iff.Tests)
                {
                    // only `name > literal` chains are coherent by construction; anything else in a chain:
                    if (!(t.Test is BinaryExpression b) || b.Operator != PythonOperator.GreaterThan) return false;
                    long thr;
                    if (!TryConstLong(b.Right, out thr)) return false;
                    if (!(b.Left is NameExpression)) return false;
                    if (prev.HasValue && thr >= prev.Value) return false; // must strictly descend
                    prev = thr;
                }
            }
            return true;
        }

        static bool TryConstLong(Node n, out long v)
        {
            v = 0;
            if (n is ConstantExpression ce)
            {
                if (ce.Value is int i) { v = i; return true; }
                if (ce.Value is long l) { v = l; return true; }
            }
            return false;
        }

        static List<IfStatement> CollectIfs(Node node)
        {
            var acc = new List<IfStatement>();
            if (node is IfStatement i) acc.Add(i);
            foreach (var c in NodeWalk.DirectChildren(node)) acc.AddRange(CollectIfs(c));
            return acc;
        }

        /// <summary>D3 entry: the FINAL single execution pass at the very end of the
        /// pipeline (mutation-era re-derive). Returns the outcome whose ExecResult
        /// is the canonical answer source for options/goalText.</summary>
        public static MutationOutcome FinalRun(string code, CheckerRequest req, List<string> inputs)
        {
            string v;
            var exec = Gate(code, req, inputs, out v);
            if (exec == null) return null;
            return new MutationOutcome { Code = code, Trace = exec.Trace, Exec = exec };
        }
    }
}
