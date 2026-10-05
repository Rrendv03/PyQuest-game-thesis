// TEMPORARY smoke test V8.6 (last rev V8.3) — delete before Phase C lands.
// V8.6 = input() execution + isolation (no conditionals in Input Mists), if/elif/else + nested loops at tier 2, KD-isolated kind pools.
// V8.3–V8.5 = strict use-policy (print references defined var; every var reachable — reverse dataflow sweep), output budgets (1 line default; STB/LS B:1/I:1–3/A:2–5; prints B:1/I:2/A:1–4 args), zone STRICT-KC rule, boundary disjointness, advanced never one-liner.
// V8 = goalText machine-derived from executed trace (STAGE 12) + trace-match grading (STAGE 13); every trace logs under "[IronPython] final output:".
// if/for/while handled EXCLUSIVELY VIA REFLECTION (no CS0246/CS1061 drift). CreateEngine() here is EDITOR-ONLY; production must use the low-level parser (install doc Step 5).
using IronPython.Compiler;
using IronPython.Compiler.Ast;
using IronPython.Hosting;
using IronPython.Runtime;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting.Providers;
using Microsoft.Scripting.Runtime;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class IronPythonSmokeTest : MonoBehaviour
{
    // ——— charter pools ———
    static readonly string[] Names = { "Ula", "Bryn", "Oro", "Wren", "hp", "score", "gold", "mana" };
    static readonly string[] Strings = { "Hello", "Hi", "Greetings", "Welcome", "hero" };
    static readonly string[] Msgs = { "Found", "Gained", "Lost", "You have" };
    static readonly string[] InputPromptString = { "input your name: ", "type is your name: ", "input your favorite color: " };
    static readonly string[] InputPromptNumber = { "input your number here: ", "type your age: " };
    static readonly int[] Ints = { 1, 2, 3, 5, 7, 10, 12 };
    static readonly char[] Ops = { '+', '-', '*' };
    static readonly char[] CmpOps = { '>', '<' };

    const int MinLines = 1;
    const int MaxLines = 5;
    const int MaxLoopIters = 6;    // hard execution bound — endless-output guard (was 12)
    const int MaxTraceLines = 24;
    public bool logParseFailures = true; // set false in Inspector for release logging

    // ——— output-budget rule (ALL KCs; handoff contract) ———
    int tier = 0;               // 0 beginner, 1 intermediate, 2 advanced (set per trial)
    bool stbOrLs = false;       // set per trial; grants the tiered multi-line budget
    int allowedOutputLines = 1; // tier+format-derived, set per trial
    bool wantBig = false;       // RULE 2: advanced 70% >2-code-lines draw (set per trial)
    string kc = "print";        // RULE 5/STRICT-KC: the zone's assigned KC (set per trial):
                                // "print" | "variables" | "operations" | "input" | "conditionals" | "loops"

    // counters
    int plainTrials = 0, plainAligned = 0;
    int condTrials = 0, condAligned = 0;   // forced conditional/loop experiment trials
    int gradeTrials = 0, gradeOk = 0;
    int kcTrials = 0, kcOk = 0;            // STAGE 14: per-KC × per-tier matrix
    int inputTrials = 0, inputOk = 0;      // STAGE 15: input() presence + no-leak check
    string inputPreset = "Hi";             // preset input() value for Input-Mists puzzles (set at draw)
    int inputShape = 0;                    // 0 = string input, 1 = int(input(...)) numeric conversion

    void Start()
    {
        Debug.Log("[PCG-V8] stage 0: goalText (now with conditionals/loops) + trace-match grading probe alive");
        try { RunStages(); }
        catch (System.Exception ex)
        {
            Debug.LogError("[PCG-V8] FAILED at cycle: " + ex.GetType().Name + "\n" + ex);
        }
        Debug.Log($"[PCG-V8] SUMMARY: plainGoal={plainAligned}/{plainTrials} | condLoopGoal={condAligned}/{condTrials} | grading={gradeOk}/{gradeTrials} | kcMatrix={kcOk}/{kcTrials} | inputIsolation={inputOk}/{inputTrials}");
        bool ok = plainTrials > 0 && plainAligned == plainTrials
                  && condTrials > 0 && condAligned == condTrials
                  && gradeTrials > 0 && gradeOk == gradeTrials
                  && kcTrials > 0 && kcOk == kcTrials
                  && inputTrials > 0 && inputOk == inputTrials;
        Debug.Log(ok
            ? "[PCG-V8] ALL STAGES VALID — goalText (plain + conditional/loop) machine-derived and aligned; trace-match grading proven. Handoff foundation complete."
            : "[PCG-V8] some stages failed — inspect logs above.");
    }

    // ================== GENERATION ==================

    List<string> GenerateLines(bool withConstructs)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var lines = new List<string>();
            var intVars = new List<string>();   // defined int vars so far (generation-scope chain)
            var strVars = new List<string>();   // defined str vars so far
            if (kc == "input") inputShape = Random.Range(0, 2); // one input shape per attempt
            // RULE 2: at advanced tier, 70% of puzzles must exceed 2 code lines strictly —
            // and advanced NEVER generates a one-liner (absolute minimum of 2 lines)
            int minAtTier = tier == 2 ? 2 : MinLines;
            int lineCount = wantBig ? Random.Range(minAtTier, MaxLines + 1) : Random.Range(MinLines, MaxLines + 1);
            if (tier == 2 && lineCount < 2) lineCount = 2;
            // construct KCs draw a fixed shape: one scaffold assignment + the focal construct
            if (kc == "conditionals" || kc == "loops") lineCount = Mathf.Min(lineCount, tier == 2 ? 3 : 2);
            for (int j = 0; j < lineCount; j++)
                lines.Add(RandomStatementCtx(withConstructs, intVars, strVars, j == 0));
            Sanitize(lines);
            if (!CountSafe(lines)) continue;
            if (withConstructs && !ContainsConstruct(lines)) continue;
            // RULE 6 (STRICT KC): the zone's assigned KC must be present on EVERY puzzle —
            // append the KC line if the drawn program lacks it (and get its var, if any,
            // so a print can be bound to it and it survives the dead-var sweep)
            string kcVar = EnsureKcLine(lines, intVars, strVars);
            // STRICT use-policy (all tiers): the print must reference a defined variable
            // (input KC: bind to the input() var so pruning always keeps the input line)
            if (kc == "input") kcVar = kcVar ?? InputVarOf(lines);
            EnsurePrintConnects(lines, kcVar);
            // STRICT ?1 output line: append a top-level print only when NO print exists —
            // replacing would overwrite the zone's construct (the KC-killer behind 9/12), and
            // a top-level print would break the tier-0 1-line budget when the construct body already prints.
            if (!ContainsTopLevelPrint(lines) && !ContainsInlineBodyPrint(lines)) lines.Add(RandomPrint(intVars, strVars));
            if (kc == "input") EnsurePrintConnects(lines, InputVarOf(lines)); // re-bind: late-added print must reference the input var
            Sanitize(lines);
            // ADVANCED ELABORATION (tier 2): conditionals ? if/elif/else (high probability);
            // loops ? nested loops. Elaborated bodies are then swept under the SAME
            // ground rules (use-policy, disjointness, budgets) as every other line.
            ElaborateConditionals(lines, intVars, strVars);
            ElaborateLoops(lines, intVars, strVars);
            Sanitize(lines);
            PruneUnconnected(lines);      // drop assignments whose var is never used
            Sanitize(lines);
            // RULE 6 (STRICT KC, verified): the zone's KC must survive pruning — else re-draw
            if (!SatisfiesKc(lines)) continue;
            // RULE 2 (strict): advanced never one-liner + wantBig draws must survive pruning
            if (tier == 2 && lines.Count < 2) continue;
            if (wantBig && lines.Count <= 2) continue;
            // RULE 1 (strict): no printed element (string/var/int) may repeat across a
            // conditional/loop boundary — inside body vs outside prints must be disjoint
            if (!DisjointInsideOutside(lines)) continue;
            // final gate: line budget + ?1 print anywhere — a tier-0 construct puzzle's
            // inline body print counts (there is no top-level print on those, by design)
            if (!CountSafe(lines) || !(ContainsTopLevelPrint(lines) || ContainsInlineBodyPrint(lines))) continue;
            return lines;
        }
        return new List<string> { withConstructs
            ? "for " + Names[0] + " in range(" + Ints[0] + "): print(" + Names[0] + ")"
            : "print(\"Found\")" };
    }

    static bool ContainsConstruct(List<string> lines)
    {
        foreach (var l in lines)
        {
            var t = l.Trim();
            if (t.StartsWith("if ") || t.StartsWith("for ")) return true;
        }
        return false;
    }

    string GenerateProgramSanitized(bool withConstructs = false)
    {
        var lines = GenerateLines(withConstructs);
        return string.Join("\n", lines.ToArray());
    }

    // ——— tier-aware budget helpers ———

    int MaxPrintArgsFor(int t) { return t == 0 ? 1 : t == 1 ? 2 : 4; }

    // derive the trial's KC (zone rule), format, tier and the output-line allowance
    void SetKcForTrial(bool stb, int t, string newKc)
    {
        stbOrLs = stb; tier = t; kc = newKc;
        // RULE 2: 70% of advanced-tier puzzles must have MORE than 2 code lines, strictly
        wantBig = t == 2 && Random.value <= 0.7f;
        if (!stb || t == 0) { allowedOutputLines = 1; return; }      // default formats / beginner: 1 line
        if (t == 1) allowedOutputLines = Random.Range(1, 4);         // intermediate STB/LS: 1–3
        else allowedOutputLines = Random.Range(2, 6);                // advanced STB/LS: 2–5
    }

    // ——— RULE 1: inside/outside printed-element disjointness ———

    // elements printed by a print(): string contents, int literals, var names
    static HashSet<string> ElementKeysOf(string printArgs)
    {
        var keys = new HashSet<string>();
        foreach (var pp in printArgs.Split(','))
        {
            string a = pp.Trim();
            if (a.StartsWith("\"")) { string s = a.Trim('"'); if (s.Length > 0) keys.Add("s:" + s); }
            else if (int.TryParse(a, out int iv)) keys.Add("i:" + iv);
            else if (a.Length > 0) keys.Add("v:" + a);
        }
        return keys;
    }

    // STRICT: printed elements on the inside vs outside of constructs must be disjoint.
    static bool DisjointInsideOutside(List<string> lines)
    {
        var outside = new HashSet<string>();
        var inside = new HashSet<string>();
        foreach (var raw in lines)
        {
            string t = raw.Trim();
            bool indented = raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t');
            if (t.StartsWith("if ") || t.StartsWith("for "))
            {
                int colon = t.IndexOf(':');
                if (colon <= 0) return false;
                string rest = t.Substring(colon + 1).Trim();
                if (rest.Length > 0) // inline body — element set belongs to the inside
                    foreach (var k in ElementKeysOf(rest)) inside.Add(k);
            }
            else if (indented)
            {
                // multi-line construct body (if/elif/else, nested loops) — inside elements
                if (t.StartsWith("print("))
                    foreach (var k in ElementKeysOf(t.Substring(6, t.Length - 7))) inside.Add(k);
            }
            else if (t.StartsWith("print("))
            {
                foreach (var k in ElementKeysOf(t.Substring(6, t.Length - 7))) outside.Add(k);
            }
        }
        foreach (var k in inside) if (outside.Contains(k)) return false;
        return true;
    }

    static int TraceLineCount(string trace) { return string.IsNullOrEmpty(trace) ? 0 : trace.Split('\n').Length; }

    // STRICT output rule: at least ONE line, at most the trial's format/tier budget
    bool OutputLinesOk(string trace) { int n = TraceLineCount(trace); return n >= 1 && n <= allowedOutputLines; }

    // kind 5 = inline conditional, kind 6 = inline for-range loop (experiment kinds).
    // CONTEXT-AWARE GENERATION: keeps a running scope of defined int/str vars so later
    // statements can BUILD ON earlier ones (dataflow chains) instead of emitting loose,
    // unrelated facts. Beginner (tier 0) generates only simple, independent statements;
    // chained assignments appear from intermediate (tier >= 1) upward, per the ruleset.
    string RandomStatementCtx(bool withConstructs, List<string> intVars, List<string> strVars, bool firstLine)
    {
        // STRICT KC ISOLATION: kind pools scoped per zone KC so constructs never leak across sanctums.
        int kind;
        int[] pool;
        switch (kc)
        {
            case "print": pool = new[] { 3, 3, 3, 4 }; break;
            case "variables": pool = new[] { 0, 1, 0, 1 }; break;
            case "operations": pool = new[] { 2, 2, 2, 0, 1 }; break;
            case "input": pool = new[] { 7, 7, 7, 1, 3 }; break;
            case "conditionals": pool = firstLine ? new[] { 0, 1 } : new[] { 5 }; break; // scaffold first, then strictly the focal construct
            case "loops": pool = firstLine ? new[] { 0, 1 } : new[] { 6 }; break;        // scaffold first, then strictly the focal loop
            default: pool = new[] { 0, 1, 2, 3, 4 }; break;
        }
        kind = pool[Random.Range(0, pool.Length)];
        string name = Names[Random.Range(0, Names.Length)];

        // chained shape from a previously defined var — intermediate+ only
        if (!firstLine && tier >= 1 && (kind == 0 || kind == 1) && Random.Range(0, 2) == 1
            && (intVars.Count > 0 || strVars.Count > 0))
            return ChainedAssignment(name, intVars, strVars);

        switch (kind)
        {
            case 0:
                intVars.Add(name);
                return $"{name} = {Ints[Random.Range(0, Ints.Length)]}";
            case 1:
                strVars.Add(name);
                return $"{name} = \"{Strings[Random.Range(0, Strings.Length)]}\"";
            case 2: // binop — intermediate+ prefers building on a defined int var
                string src;
                if (tier >= 1 && intVars.Count > 0 && Random.Range(0, 2) == 1)
                    src = intVars[intVars.Count - 1];
                else src = Names[Random.Range(0, Names.Length)];
                intVars.Add(name);
                return $"{name} = {src} {Ops[Random.Range(0, Ops.Length)]} {Ints[Random.Range(0, Ints.Length)]}";
            case 3:
            case 4:
                return RandomPrint(intVars, strVars);
            case 5: // inline conditional — prefers a defined int var as the condition operand
                string varName = (tier >= 1 && intVars.Count > 0 && Random.Range(0, 2) == 1)
                    ? intVars[intVars.Count - 1] : Names[Random.Range(0, Names.Length)];
                string msg = Msgs[Random.Range(0, Msgs.Length)];
                return $"if {varName} > {Ints[Random.Range(0, Ints.Length)]}: print(\"{msg}\")";
            case 7: // input() — Input Mists KC only; shape = string or numeric conversion
                if (inputShape == 1)
                {
                    inputPreset = Ints[Random.Range(0, Ints.Length)].ToString(); // preset must be a number for int() to convert
                    intVars.Add(name);
                    return $"{name} = int(input(\"{InputPromptNumber[Random.Range(0, InputPromptNumber.Length)]}\"))";
                }
                inputPreset = Strings[Random.Range(0, Strings.Length)];
                strVars.Add(name);
                return $"{name} = input(\"{InputPromptString[Random.Range(0, InputPromptString.Length)]}\")";
            default: // inline for-range loop, single-line body
                string loopVar = Names[Random.Range(0, Names.Length)];
                int iter = Random.Range(1, tier == 0 ? 2 : (tier == 1 ? 3 : 4)); // tiered iters: 1 / 1–2 / 1–3
                return $"for {loopVar} in range({iter}): print({loopVar})";
        }
    }

    // ——— ADVANCED ELABORATION (tier 2 only) ———

    // tier-2 conditionals: high-probability if/elif/else elaboration, descending thresholds.
    void ElaborateConditionals(List<string> lines, List<string> intVars, List<string> strVars)
    {
        if (tier != 2 || kc != "conditionals" || Random.value > 0.75f) return;
        int victim = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i].Trim();
            if (t.StartsWith("if ") && t.Contains(": print(")) { victim = i; break; }
        }
        if (victim < 0) return;
        string t0 = lines[victim].Trim();
        int colon = t0.IndexOf(':');
        string head = t0.Substring(3, colon - 3).Trim();
        int gt = head.IndexOf('>');
        if (gt <= 0) return;
        string a = head.Substring(0, gt).Trim();
        string bStr = head.Substring(gt + 1).Trim();
        int b1;
        if (!int.TryParse(bStr, out b1)) return;
        // FATAL-LOOP GUARD: if no strictly-smaller threshold exists in the pool, skip
        if (b1 <= Ints[0]) return; // Ints[0] == 1 is the minimum — nothing can be smaller
        int b2 = b1;
        for (int guard = 0; guard < 8 && b2 >= b1; guard++) b2 = Ints[Random.Range(0, Ints.Length)];
        if (b2 >= b1) return;
        lines[victim] = $"if {a} > {b1}:";
        lines.InsertRange(victim + 1, new List<string>
        {
            "    " + RandomPrint(intVars, strVars),
            $"    elif {a} > {b2}:",
            "    " + RandomPrint(intVars, strVars),
            "    else:",
            "    " + RandomPrint(intVars, strVars)
        });
    }

    // tier-2 loops: always nested; a*b product bounded by the output budget.
    void ElaborateLoops(List<string> lines, List<string> intVars, List<string> strVars)
    {
        if (tier != 2 || kc != "loops") return;
        int victim = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            string t = lines[i].Trim();
            if (t.StartsWith("for ") && t.Contains(": print(")) { victim = i; break; }
        }
        if (victim < 0) return;
        string t0 = lines[victim].Trim();
        int colon = t0.IndexOf(':');
        if (colon <= 4) return;
        string head = t0.Substring(4, colon - 4).Trim();
        int rin = head.IndexOf(" in ");
        if (rin <= 0) return;
        string lv = head.Substring(0, rin).Trim();
        string lv2 = FreshName(intVars, strVars);
        intVars.Add(lv2);
        // bounded search: a,b ? {1,2}; initialized 1/1 (always valid), refined by the fit loop
        int a = 1, b = 1;
        bool fit = false;
        for (int guard = 0; guard < 8 && !fit; guard++)
        {
            a = Random.Range(1, 3); b = Random.Range(1, 3);
            fit = a * b <= 4 && a * b <= (allowedOutputLines < 1 ? 1 : allowedOutputLines);
        }
        if (!fit) return; // only reachable when the allowance is somehow < 1
        lines[victim] = $"for {lv} in range({a}):";
        lines.InsertRange(victim + 1, new List<string>
        {
            $"    for {lv2} in range({b}):",
            $"        print({lv2})"
        });
    }

    // DATAFLOW CHAINS (tier >= 1): new vars are computed FROM earlier vars (`var2 = var1 + 3`, `s2 = s1 + "lit"`).
    string ChainedAssignment(string name, List<string> intVars, List<string> strVars)
    {
        bool fromStr = strVars.Count > 0 && (intVars.Count == 0 || Random.Range(0, 2) == 1);
        if (fromStr)
        {
            string src = strVars[strVars.Count - 1]; // chain grows on the LAST defined string var
            string rhs = Random.Range(0, 3) == 0
                ? src                                              // echo: s2 = s1
                : src + " + \"" + Strings[Random.Range(0, Strings.Length)] + "\""; // s2 = s1 + "lit"
            strVars.Add(name);
            return $"{name} = {rhs}";
        }
        string srcI = intVars[intVars.Count - 1];
        int shape = Random.Range(0, 4);
        string rhsI = shape == 0 ? srcI
            : shape == 1 ? $"{srcI} + {Ints[Random.Range(0, Ints.Length)]}"
            : shape == 2 ? $"{srcI} - {Ints[Random.Range(0, Ints.Length)]}"
            : $"{srcI} * {Ints[Random.Range(0, Ints.Length)]}";
        intVars.Add(name);
        return $"{name} = {rhsI}";
    }

    static bool ContainsInlineBodyPrint(List<string> lines)
    {
        foreach (var l in lines)
        {
            string t = l.Trim();
            if ((t.StartsWith("if ") || t.StartsWith("for ")) && t.Contains(": print(")) return true;
        }
        return false;
    }

    static bool ContainsTopLevelPrint(List<string> lines)
    {
        foreach (var l in lines) if (l.TrimStart().StartsWith("print(")) return true;
        return false;
    }

    // ——— STRICT USE-POLICY (all KCs; handoff contract) ———

    // Binds a print to a defined var (`target` = appended KC var, if any) — in a top-level
    // print OR a construct's inline body print; one arg slot swapped, budget preserved.
    void EnsurePrintConnects(List<string> lines, string target)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            string t = lines[i].Trim();
            bool isTopPrint = t.StartsWith("print(");
            bool isInlineBody = (t.StartsWith("if ") || t.StartsWith("for ")) && t.Contains(": print(");
            if (!isTopPrint && !isInlineBody) continue;
            int cIdx = isInlineBody ? t.IndexOf(':') : 0;
            string prefix = isInlineBody ? t.Substring(0, cIdx + 1) + " " : "";
            string body = isInlineBody ? t.Substring(cIdx + 1).Trim() : t;
            string args = body.Substring(6, body.Length - 7);
            var before = new List<string>(); // vars defined BEFORE this construct/print line
            for (int j = 0; j < i; j++)
            {
                string tj = lines[j].Trim();
                int eqj = tj.IndexOf(" = ");
                if (eqj > 0)
                {
                    string nm = tj.Substring(0, eqj).Trim();
                    if (!before.Contains(nm) && !nm.Contains("\"")) before.Add(nm);
                }
            }
            if (before.Count == 0) continue;
            var parts = new List<string>(args.Split(','));
            bool satisfied = false; int swapIdx = parts.Count - 1;
            for (int pI = parts.Count - 1; pI >= 0; pI--)
            {
                string a = parts[pI].Trim();
                if (target != null && a == target) { satisfied = true; break; }   // bound to the KC var
                if (target == null && !a.Contains("\"") && !int.TryParse(a, out _) && before.Contains(a)) { satisfied = true; break; }
                if (a.Contains("\"") || int.TryParse(a, out _)) swapIdx = pI;    // rightmost literal slot
            }
            if (satisfied) return;
            parts[swapIdx] = target ?? before[Random.Range(0, before.Count)];
            lines[i] = prefix + "print(" + string.Join(", ", parts.ToArray()) + ")";
            return;
        }
    }

    // Reverse dataflow sweep: drops assignments whose var never feeds a print/condition/feed-chain.
    void PruneUnconnected(List<string> lines)
    {
        var needed = new HashSet<string>();
        var keep = new bool[lines.Count];
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            string t = lines[i].Trim();
            if (t.StartsWith("print("))
            {
                keep[i] = true;
                CollectNames(t.Substring(6, t.Length - 7), needed);
            }
            else if (t.StartsWith("if "))
            {
                int colon = t.IndexOf(':');
                if (colon <= 0) continue;
                keep[i] = true;
                CollectNames(t.Substring(3, colon - 3), needed);  // condition operands serve a purpose
                CollectNames(t.Substring(colon + 1), needed);    // ...and its body print args
            }
            else if (t.StartsWith("for "))
            {
                int colon = t.IndexOf(':');
                if (colon <= 0) continue;
                keep[i] = true;
                int rin = t.IndexOf(" in ", 4);
                if (rin > 0) CollectNames(t.Substring(4, rin - 4), needed); // loop var feeds body prints
                CollectNames(t.Substring(colon + 1), needed);
            }
            else if (t.StartsWith("elif ") || t == "else:")
            {
                // multi-line chain heads are structural — always kept
                keep[i] = true;
                CollectNames(t, needed); // elif operands serve a purpose too
            }
            else
            {
                int eq = t.IndexOf(" = ");
                if (eq > 0)
                {
                    string name = t.Substring(0, eq).Trim();
                    if (needed.Contains(name)) { keep[i] = true; CollectNames(t.Substring(eq + 3), needed); }
                    // else: this variable serves nothing downstream ? line dropped
                }
            }
        }
        for (int i = lines.Count - 1; i >= 0; i--) if (!keep[i]) lines.RemoveAt(i);
        lines.RemoveAll(l => string.IsNullOrWhiteSpace(l));
    }

    // names referenced in a fragment: skips literals, quoted strings, keywords
    static void CollectNames(string chunk, HashSet<string> into)
    {
        foreach (var tok in chunk.Split(new[] { ' ', ',', '(', ')', ':' }))
        {
            string a = tok.Trim();
            if (a.Length == 0 || a.Contains("\"") || int.TryParse(a, out _)) continue;
            if (a == "print" || a == "range" || a == "in" || a == "if" || a == "elif" || a == "else") continue;
            into.Add(a);
        }
    }

    // ——— STRICT KC PRESENCE (zone rule; handoff contract) ———

    // Every puzzle must present its zone's assigned KC, strictly, on every difficulty.
    bool SatisfiesKc(List<string> lines)
    {
        switch (kc)
        {
            case "print": return ContainsTopLevelPrint(lines);
            case "variables": return HasAssignment(lines);
            case "operations": return HasBinopAssignment(lines);
            case "input": return HasInput(lines); // STRICT: scaffolds no longer count — input() must literally appear
            case "conditionals": return HasIf(lines);
            case "loops": return HasFor(lines);
        }
        return true;
    }

    static bool HasAssignment(List<string> lines)
    {
        foreach (var raw in lines)
        {
            string t = raw.Trim();
            int eq = t.IndexOf(" = ");
            if (eq > 0 && !t.StartsWith("if ") && !t.StartsWith("for ")) return true;
        }
        return false;
    }

    static bool HasBinopAssignment(List<string> lines)
    {
        foreach (var raw in lines)
        {
            string t = raw.Trim();
            int eq = t.IndexOf(" = ");
            if (eq <= 0 || t.StartsWith("if ") || t.StartsWith("for ")) continue;
            var p = t.Substring(eq + 3).Trim().Split(' ');
            if (p.Length == 3 && (p[1] == "+" || p[1] == "-" || p[1] == "*")) return true;
        }
        return false;
    }

    static bool HasIf(List<string> lines) { foreach (var raw in lines) if (raw.Trim().StartsWith("if ")) return true; return false; }
    static bool HasInput(List<string> lines) { foreach (var raw in lines) if (raw.Trim().Contains("input(") && raw.Trim().Contains(" = ")) return true; return false; }
    static string InputVarOf(List<string> lines) { foreach (var raw in lines) { string t = raw.Trim(); int eq = t.IndexOf(" = "); if (eq > 0 && t.Substring(eq + 3).Trim().Contains("input(")) return t.Substring(0, eq).Trim(); } return null; }
    static bool HasFor(List<string> lines) { foreach (var raw in lines) if (raw.Trim().StartsWith("for ")) return true; return false; }

    static string FreshName(List<string> intVars, List<string> strVars)
    {
        foreach (var n in Names) if (!intVars.Contains(n) && !strVars.Contains(n)) return n;
        return Names[Random.Range(0, Names.Length)];
    }

    // Appends the zone's KC line if missing; returns its var so a print can bind to it.
    // Scaffold rule: conditionals/loops also keep a variable assignment from past sanctums.
    string EnsureKcLine(List<string> lines, List<string> intVars, List<string> strVars)
    {
        if (SatisfiesKc(lines)) return null;
        string name = FreshName(intVars, strVars);
        switch (kc)
        {
            case "variables":
                if (Random.Range(0, 2) == 0)
                {
                    intVars.Add(name);
                    lines.Add($"{name} = {Ints[Random.Range(0, Ints.Length)]}");
                }
                else
                {
                    strVars.Add(name);
                    lines.Add($"{name} = \"{Strings[Random.Range(0, Strings.Length)]}\"");
                }
                return name;
            case "operations":
                {
                    string rhs;
                    if (intVars.Count > 0)
                    {
                        string src = intVars[intVars.Count - 1];
                        rhs = $"{src} {Ops[Random.Range(0, Ops.Length)]} {Ints[Random.Range(0, Ints.Length)]}";
                    }
                    else if (strVars.Count > 0)
                    {
                        rhs = $"{strVars[strVars.Count - 1]} * {Ints[Random.Range(0, Ints.Length)]}"; // str * int counts as an operation
                    }
                    else
                    {
                        rhs = $"{Names[Random.Range(0, Names.Length)]} {Ops[Random.Range(0, Ops.Length)]} {Ints[Random.Range(0, Ints.Length)]}";
                    }
                    intVars.Add(name);
                    lines.Add($"{name} = {rhs}");
                    return name;
                }
            case "input": // real input() line with a charter prompt; shape follows the attempt's inputShape
                if (inputShape == 1)
                {
                    inputPreset = Ints[Random.Range(0, Ints.Length)].ToString();
                    intVars.Add(name);
                    lines.Insert(0, $"{name} = int(input(\"{InputPromptNumber[Random.Range(0, InputPromptNumber.Length)]}\"))"); // prepend so prints see it defined
                }
                else
                {
                    inputPreset = Strings[Random.Range(0, Strings.Length)];
                    strVars.Add(name);
                    lines.Insert(0, $"{name} = input(\"{InputPromptString[Random.Range(0, InputPromptString.Length)]}\")"); // prepend so prints see it defined
                }
                return name;
            case "conditionals":
                {
                    if (!HasAssignment(lines)) // scaffold: a past-sanctum variable feeds the condition
                    {
                        string v = FreshName(intVars, strVars);
                        intVars.Add(v);
                        lines.Insert(0, $"{v} = {Ints[Random.Range(0, Ints.Length)]}");
                    }
                    string operand = (tier >= 1 && intVars.Count > 0) ? intVars[intVars.Count - 1] : Ints[Random.Range(0, Ints.Length)].ToString();
                    lines.Add($"if {operand} > {Ints[Random.Range(0, Ints.Length)]}: print(\"{Msgs[Random.Range(0, Msgs.Length)]}\")");
                    return null;
                }
            case "loops":
                {
                    if (!HasAssignment(lines)) // scaffold: a past-sanctum variable in scope
                    {
                        string v = FreshName(intVars, strVars);
                        intVars.Add(v);
                        lines.Insert(0, $"{v} = {Ints[Random.Range(0, Ints.Length)]}");
                    }
                    intVars.Add(name);
                    int iter = Random.Range(1, tier == 0 ? 2 : (tier == 1 ? 3 : 4));
                    lines.Add($"for {name} in range({iter}): print({name})");
                    return name;
                }
            default: // print
                if (!ContainsTopLevelPrint(lines)) lines.Add(RandomPrint(intVars, strVars));
                return null;
        }
    }

    // tier-aware print builder: arg count obeys the tier's argument budget;
    // at intermediate+ it PULLS DEFINED VARS into the args so prints connect to the chain
    string RandomPrint(List<string> intVars = null, List<string> strVars = null)
    {
        int cap = MaxPrintArgsFor(tier);
        int n = tier == 0 ? 1 : Random.Range(1, cap + 1);
        var args = new List<string>();
        for (int i = 0; i < n; i++)
        {
            int pick = Random.Range(0, 3);
            if (pick == 0 && !(i == 0 && n > 1)) args.Add("\"" + Msgs[Random.Range(0, Msgs.Length)] + "\"");
            else if (pick == 1) args.Add(Ints[Random.Range(0, Ints.Length)].ToString());
            else
            {
                string withVar = null;
                if (tier >= 1 && intVars != null && intVars.Count > 0 && Random.Range(0, 3) > 0)
                    withVar = intVars[Random.Range(0, intVars.Count)];
                else if (tier >= 1 && strVars != null && strVars.Count > 0 && Random.Range(0, 4) == 0)
                    withVar = strVars[Random.Range(0, strVars.Count)];
                args.Add(withVar ?? Names[Random.Range(0, Names.Length)]);
            }
        }
        return "print(" + string.Join(", ", args.ToArray()) + ")";
    }

    // ================== SANITIZER / VERIFIER ==================

    bool Sanitize(List<string> lines)
    {
        var typeOf = new Dictionary<string, string>();
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            string t = line.Trim();
            string pad = line.Substring(0, line.Length - line.TrimStart().Length); // preserve indentation

            if (t.StartsWith("if "))
            {
                // shape: "if <A> <OP> <B>: <rest>"  — single > or < only (rest may be empty for multi-line blocks)
                int colon = t.IndexOf(':');
                if (colon <= 0) continue;
                string head = t.Substring(3, colon - 3).Trim();
                string rest = t.Substring(colon + 1).Trim();
                int pi = head.IndexOfAny(CmpOps);
                if (pi < 0) continue;
                string aName = head.Substring(0, pi).Trim();
                string opStr = head[pi] + (pi + 1 < head.Length && head[pi + 1] == '=' ? "=" : "");
                string bVal = head.Substring(pi + opStr.Length).Trim();
                // A must be a defined int variable or int literal — else replace with int literal
                if (!(int.TryParse(aName, out _)) && !(typeOf.TryGetValue(aName, out var ta) && ta == "int"))
                {
                    string aLit = Ints[Random.Range(0, Ints.Length)].ToString();
                    head = aLit + head.Substring(pi); // keep op + B
                    aName = aLit;
                }
                // B must be int literal or defined int variable
                if (!int.TryParse(bVal, out _) && !(typeOf.TryGetValue(bVal, out var tb) && tb == "int"))
                    bVal = Ints[Random.Range(0, Ints.Length)].ToString();
                lines[i] = pad + RebuildIf(head, FixInlineBody(rest, typeOf));
            }
            else if (t.StartsWith("for "))
            {
                // shape: "for <name> in range(<int>): print(...)"  — just register loop var as int
                int colon = t.IndexOf(':');
                if (colon > 0)
                {
                    string head = t.Substring(4, colon - 4).Trim();
                    int rin = head.IndexOf(" in ");
                    if (rin > 0)
                    {
                        string loopVar = head.Substring(0, rin).Trim();
                        typeOf[loopVar] = "int";
                    }
                }
            }
            else if (t.Contains(" = "))
            {
                string line2 = t;
                int eq = line2.IndexOf(" = ");
                string name = line2.Substring(0, eq).Trim();
                string rhs = line2.Substring(eq + 3).Trim();
                if (rhs.StartsWith("\"") || int.TryParse(rhs, out _)) { typeOf[name] = rhs.StartsWith("\"") ? "str" : "int"; }
                else
                {
                    var p = rhs.Split(' ');
                    if (p.Length == 1 && typeOf.ContainsKey(p[0]))
                    {
                        // echo chain (intermediate+): `s2 = s1` — copy the type, keep the link
                        typeOf[name] = typeOf[p[0]];
                    }
                    else if (p.Length == 3)
                    {
                        string x = p[0], op = p[1], y = p[2];
                        bool xLit = int.TryParse(x, out _);
                        bool yIsName = !int.TryParse(y, out _) && !y.StartsWith("\"");
                        if (!xLit && !typeOf.ContainsKey(x)) { x = Ints[Random.Range(0, Ints.Length)].ToString(); xLit = true; }
                        if (yIsName && !typeOf.ContainsKey(y)) { y = Ints[Random.Range(0, Ints.Length)].ToString(); yIsName = false; }
                        // var-to-var chains (intermediate+): `var2 = var1 + var0` etc.
                        string tx = xLit ? "int" : typeOf[x];
                        string ty = yIsName ? typeOf[y] : (y.StartsWith("\"") ? "str" : "int");
                        if (tx == ty) { rhs = $"{x} {op} {y}"; typeOf[name] = tx; }
                        else if (tx == "str" && ty == "int" && op == "*") { rhs = $"{x} {op} {y}"; typeOf[name] = "str"; }
                        else { rhs = Ints[Random.Range(0, Ints.Length)].ToString(); typeOf[name] = "int"; }
                    }
                    else if (p.Length == 1 && rhs == "input()")
                    {
                        typeOf[name] = "str"; // input() returns a string (string-only charter; int(input()) is banned)
                    }
                    else if (rhs.StartsWith("input(\"") || rhs.StartsWith("int(input(\""))
                    {
                        typeOf[name] = rhs.StartsWith("int(") ? "int" : "str"; // preserve prompted input() lines verbatim
                    }
                    else { rhs = Ints[Random.Range(0, Ints.Length)].ToString(); typeOf[name] = "int"; }
                }
                lines[i] = pad + $"{name} = {rhs}";
            }
            else if (t.StartsWith("print("))
            {
                string args = t.Substring(6, t.Length - 7);
                args = CapPrintArgs(args, typeOf);
                lines[i] = pad + $"print({args})";
            }
        }
        lines.RemoveAll(l => string.IsNullOrWhiteSpace(l));
        return lines.Count >= MinLines;
    }

    string RebuildIf(string head, string rest)
    {
        return $"if {head}: {rest}";
    }

    // shared print-arg repair — TIER-AWARE: caps the arg count at the tier budget
    // (drops extras) and repairs undefined names to literals. Used for top-level
    // prints AND inline if/for bodies via FixInlineBody below.
    string CapPrintArgs(string args, Dictionary<string, string> typeOf)
    {
        int cap = MaxPrintArgsFor(tier);
        var parts = new List<string>();
        foreach (var p in args.Split(',')) parts.Add(p.Trim());
        parts.RemoveAll(string.IsNullOrEmpty);
        while (parts.Count > cap) parts.RemoveAt(parts.Count - 1); // tier budget: extra args dropped
        if (parts.Count == 0) parts.Add("\"" + Msgs[Random.Range(0, Msgs.Length)] + "\"");
        for (int i = 0; i < parts.Count; i++)
        {
            string a = parts[i];
            if (a.StartsWith("\"") || int.TryParse(a, out _)) continue;
            if (!typeOf.ContainsKey(a)) parts[i] = "\"" + Msgs[Random.Range(0, Msgs.Length)] + "\"";
        }
        return string.Join(", ", parts.ToArray());
    }

    string FixInlineBody(string rest, Dictionary<string, string> typeOf)
    {
        var t = rest.Trim();
        if (t.StartsWith("print(") && t.EndsWith(")"))
        {
            string inner = t.Substring(6, t.Length - 7);
            return "print(" + CapPrintArgs(inner, typeOf) + ")";
        }
        return rest;
    }

    static bool CountSafe(List<string> lines)
    {
        if (lines.Count < MinLines || lines.Count > MaxLines) return false;
        foreach (var l in lines) if (string.IsNullOrWhiteSpace(l)) return false;
        return true;
    }

    bool DefinitionSafe(List<string> lines) // tier-aware (checks the print-arg budget too)
    {
        var typeOf = new Dictionary<string, string>();
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.StartsWith("for "))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) return false;
                string head = line.Substring(4, colon - 4).Trim();
                int rin = head.IndexOf(" in ");
                if (rin <= 0) return false;
                string loopVar = head.Substring(0, rin).Trim();
                if (!head.Contains("range(")) return false;
                typeOf[loopVar] = "int";
                continue;
            }
            if (line.StartsWith("if "))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) return false;
                string head = line.Substring(3, colon - 3).Trim();
                int pi = head.IndexOfAny(CmpOps);
                if (pi < 0) return false;
                string a = head.Substring(0, pi).Trim();
                string b = head.Substring(pi + 1).Trim();
                if (b.EndsWith(":")) b = b.Substring(0, b.Length - 1).Trim();
                bool aOk = int.TryParse(a, out _) || (typeOf.TryGetValue(a, out var ta) && ta == "int");
                bool bOk = int.TryParse(b, out _) || (typeOf.TryGetValue(b, out var tb2) && tb2 == "int");
                if (!aOk || !bOk) return false;
                continue;
            }
            int eq = line.IndexOf(" = ");
            if (eq > 0)
            {
                string name = line.Substring(0, eq).Trim();
                string rhs = line.Substring(eq + 3).Trim();
                if (rhs.StartsWith("\"")) { typeOf[name] = "str"; continue; }
                if (int.TryParse(rhs, out _)) { typeOf[name] = "int"; continue; }
                if (rhs == "input()" || rhs.StartsWith("input(\"") || rhs.StartsWith("int(input(")) { typeOf[name] = rhs.StartsWith("int(") ? "int" : "str"; continue; }
                var p = rhs.Split(' ');
                if (p.Length == 1 && typeOf.ContainsKey(p[0])) { typeOf[name] = typeOf[p[0]]; continue; } // echo chain
                if (p.Length != 3) return false;
                string px = p[0], py = p[2];
                bool pyName = !int.TryParse(py, out _) && !py.StartsWith("\"");
                if (!int.TryParse(px, out _) && !typeOf.ContainsKey(px)) return false;
                if (pyName && !typeOf.ContainsKey(py)) return false;
                string tx = int.TryParse(px, out _) ? "int" : typeOf[px];
                string ty = pyName ? typeOf[py] : (py.StartsWith("\"") ? "str" : "int");
                if (tx == ty) { typeOf[name] = tx; }
                else if (tx == "str" && ty == "int" && p[1] == "*") { typeOf[name] = "str"; }
                else return false;
            }
            else if (line.StartsWith("print("))
            {
                string args = line.Substring(6, line.Length - 7);
                if (args.Split(',').Length > MaxPrintArgsFor(tier)) return false; // print-arg budget
                foreach (var part in args.Split(','))
                {
                    string a = part.Trim();
                    if (int.TryParse(a, out _) || a.StartsWith("\"")) continue;
                    if (!typeOf.ContainsKey(a)) return false;
                }
            }
            else return false;
        }
        return true;
    }

    // ================== extended trace evaluator (reflection for if/for/while) ==================

    string ExecuteProgram(PythonAst ast, out int loopRuns, out int condRuns)
    {
        loopRuns = 0; condRuns = 0;
        var vars = new Dictionary<string, object>();
        var trace = new StringBuilder();
        if (!(ast.Body is SuiteStatement suite)) return null;
        if (!ExecSuite(suite.Statements, vars, trace, ref loopRuns, ref condRuns)) return null;
        return trace.ToString().TrimEnd('\n');
    }

    bool ExecSuite(System.Collections.IEnumerable stmts, Dictionary<string, object> vars, StringBuilder trace, ref int loopRuns, ref int condRuns)
    {
        if (stmts == null) return false;
        int count = 0;
        foreach (var st in stmts)
        {
            if (++count > 100) return false; // statement cap
            if (!ExecOne(st, vars, trace, ref loopRuns, ref condRuns)) return false;
        }
        return true;
    }

    bool ExecOne(object st, Dictionary<string, object> vars, StringBuilder trace, ref int loopRuns, ref int condRuns)
    {
        if (st == null) return false;
        string t = st.GetType().Name;

        if (t == "AssignmentStatement")
        {
            object rhsExpr = InvProp(st, "Right");
            object val;
            if (rhsExpr != null && rhsExpr.GetType().Name == "CallExpression")
            {
                // input()/int(input(...)) — the preset input value stands in for the typed input
                val = EvalCallValue(rhsExpr, vars);
                if (val == null) return false;
            }
            else
            {
                val = EvalObj(rhsExpr, vars);
                if (val == null) return false;
            }
            var left = InvProp(st, "Left");
            if (left is System.Collections.IEnumerable le)
                foreach (var tgt in le)
                {
                    if (!(tgt is NameExpression tn)) return false;
                    vars[tn.Name] = val;
                }
            else return false;
            return true;
        }

        if (t == "ExpressionStatement")
        {
            var ex = InvProp(st, "Expression") as CallExpression;
            var target = InvProp(ex, "Target") as NameExpression;
            if (target == null || target.Name != "print") return false;
            var parts = new List<string>();
            var args = InvProp(ex, "Args") as System.Collections.IEnumerable;
            if (args == null) return false;
            foreach (var arg in args)
            {
                object v = EvalObj(arg, vars);
                if (v == null) return false;
                parts.Add(v.ToString());
            }
            if (trace.ToString().Split('\n').Length > MaxTraceLines) return false;
            trace.AppendLine(string.Join(" ", parts.ToArray()));
            return true;
        }

        if (t == "IfStatement")
        {
            var tests = InvProp(st, "Tests") as System.Collections.IEnumerable;
            if (tests == null) return false;
            bool matched = false;
            foreach (var rec in tests)
            {
                object cond = EvalObj(InvProp(rec, "Test"), vars);
                if (!(cond is bool b)) return false; // only int comparisons — charter enforced
                var body = InvProp(rec, "Body"); // IronPython 3.4.2: IfStatementTest.Body (verified via metadata probe)
                if (!b) continue;
                if (!ExecSuite(ToSuiteStatements(body), vars, trace, ref loopRuns, ref condRuns)) return false;
                matched = true;
                break;
            }
            if (!matched)
            {
                var elseBody = ToSuiteStatements(InvProp(st, "ElseStatement")); // IronPython 3.4.2: IfStatement.ElseStatement
                if (elseBody != null && !ExecSuite(elseBody, vars, trace, ref loopRuns, ref condRuns)) return false;
            }
            condRuns++;
            return true;
        }

        if (t == "ForStatement")
        {
            var left = InvProp(st, "Left") as NameExpression;
            if (left == null) return false;
            var iterExpr = InvProp(st, "List") as CallExpression; // IronPython 3.4.2: ForStatement.List
            var rangeTarget = InvProp(iterExpr, "Target") as NameExpression;
            if (rangeTarget == null || rangeTarget.Name != "range") return false; // only range() loops
            var rangeArgs = InvProp(iterExpr, "Args") as System.Collections.IEnumerable;
            if (rangeArgs == null) return false;
            int n = 0; bool saw = false;
            foreach (var ra in rangeArgs) { object v = EvalObj(ra, vars); if (!(v is int)) return false; n = (int)v; saw = true; }
            if (!saw || n <= 0 || n > MaxLoopIters) return false; // bounded by generation
            var body = ToSuiteStatements(InvProp(st, "Body"));
            for (int i = 0; i < n; i++)
            {
                vars[left.Name] = i;
                if (!ExecSuite(body, vars, trace, ref loopRuns, ref condRuns)) return false;
                loopRuns++;
            }
            return true;
        }

        if (t == "WhileStatement")
        {
            var cond = InvProp(st, "Test"); // IronPython 3.4.2: WhileStatement.Test
            var body = ToSuiteStatements(InvProp(st, "Body"));
            for (int i = 0; i < MaxLoopIters; i++)
            {
                loopRuns++;
                object cval = EvalObj(cond, vars);
                if (!(cval is bool b)) break;
                if (!b) break;
                if (!ExecSuite(body, vars, trace, ref loopRuns, ref condRuns)) return false;
            }
            return true;
        }

        return false; // charter subset exceeded
    }

    // body may be a SuiteStatement (inline or indented) — normalize to statement enumerable
    static System.Collections.IEnumerable ToSuiteStatements(object body)
    {
        if (body is SuiteStatement s) return s.Statements;
        if (body is Statement single) return new List<object> { single };
        return null;
    }

    static object InvProp(object obj, string name)
    {
        if (obj == null) return null;
        try
        {
            var p = obj.GetType().GetProperty(name);
            if (p == null) return null;
            return p.GetValue(obj);
        }
        catch { return null; }
    }

    // evaluates input()/int(input(...)) call expressions: the preset value stands in;
    // int() conversion succeeds ONLY when the preset parses as a number (charter rule)
    object EvalCallValue(object call, Dictionary<string, object> vars)
    {
        if (call == null || call.GetType().Name != "CallExpression") return null;
        var callee = InvProp(InvProp(call, "Target"), "Name") as string;
        object first = null;
        if (InvProp(call, "Args") is System.Collections.IEnumerable ae)
            foreach (var a0 in ae) { first = a0; break; }
        if (callee == "input") return inputPreset; // prompt text is context, not data
        if (callee == "int")
        {
            object inner = first != null && first.GetType().Name == "CallExpression" ? EvalCallValue(first, vars) : EvalObj(first, vars);
            int parsed;
            return (inner is string sv && int.TryParse(sv, out parsed)) ? (object)parsed : null;
        }
        return null;
    }

    object EvalObj(object e, Dictionary<string, object> vars)
    {
        if (e == null) return null;
        string t = e.GetType().Name;
        if (t == "ConstantExpression")
        {
            object v = InvProp(e, "Value");
            return (v is int || v is string) ? v : null; // charter: int/str only
        }
        if (t == "NameExpression")
        {
            string n = InvProp(e, "Name") as string;
            if (n == null) return null;
            return vars.TryGetValue(n, out var val) ? val : null;
        }
        if (t == "BinaryExpression")
        {
            object l = EvalObj(InvProp(e, "Left"), vars), r = EvalObj(InvProp(e, "Right"), vars);
            if (l == null || r == null) return null;
            // Operator (or Op in other versions) — reflection, add-safe
            string op = null;
            { var opProp = e.GetType().GetProperty("Operator"); if (opProp == null) opProp = e.GetType().GetProperty("Op"); if (opProp != null) try { op = opProp.GetValue(e)?.ToString(); } catch { } }
            if (l is int li && r is int ri)
            {
                switch (op)
                {
                    case "Add": return li + ri;
                    case "Subtract": return li - ri;
                    case "Multiply": return li * ri;
                    case "GreaterThan": return li > ri;
                    case "LessThan": return li < ri;
                    case "Equal": return li == ri;
                    case "GreaterThanOrEqual": return li >= ri;
                    case "LessThanOrEqual": return li <= ri;
                    case "NotEqual": return li != ri;
                    default: return null;
                }
            }
            if (l is string ls)
            {
                if (op == "Add" && r is string rs) return ls + rs;
                if (op == "Multiply" && r is int ri2) return Repeat(ls, ri2);
                return null;
            }
            return null;
        }
        return null;
    }

    static string Repeat(string s, int n)
    {
        if (n <= 0) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.Append(s);
        return sb.ToString();
    }

    // ================== STAGE 12: goalText derivation + alignment ==================

    string DeriveGoalText(string source, out string statedOutput)
    {
        var ast = Parse(source, out _);
        if (ast == null) { statedOutput = null; return null; }
        string trace = ExecuteProgram(ast, out int loopRuns, out int condRuns);
        if (trace == null) { statedOutput = null; return null; }
        if (!OutputLinesOk(trace)) { statedOutput = null; return null; } // output-budget limiter
        statedOutput = trace.Length == 0 ? "(nothing — the program prints nothing)" : trace;

        int prints = 0, assigns = 0, ops = 0, ifs = 0, loops = 0;
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("print(")) prints++;
            else if (line.StartsWith("if ")) { if (line.Contains(": print(")) prints++; ifs++; }
            else if (line.StartsWith("for ")) { if (line.Contains(": print(")) prints++; loops++; }
            else if (line.Contains(" = ")) assigns++;
            if (line.Contains(" + ") || line.Contains(" - ") || line.Contains(" * ")) ops++;
        }

        var sb = new StringBuilder();
        if (source.Contains("input("))
        {
            sb.Append($"This is a goal about how input is handled. The answer input was {inputPreset}: the program runs as if the player had typed {inputPreset}.\n");
            if (source.Contains("int(input(")) sb.Append("The input was converted with int() — possible because the typed value is a number.\n");
        }
        sb.Append(prints > 0 ? "Run this program in your head:" : "Trace this program in your head:");
        sb.Append($" it has {assigns} assignment" + (assigns == 1 ? "" : "s"));
        if (ops > 0) sb.Append($" and {ops} operation" + (ops == 1 ? "" : "s"));
        if (ifs > 0) sb.Append($" and {ifs} conditional" + (ifs == 1 ? "" : "s"));
        if (loops > 0) sb.Append($" and {loops} loop" + (loops == 1 ? "" : "s"));

        bool constructs = ifs > 0 || loops > 0;
        sb.Append(prints > 0
            ? $". The goal is to predict what it prints."
            : $". The goal is to trace the values it stores.");
        if (constructs)
        {
            sb.Append($"\nThis should produce: {statedOutput}");
            if (loopRuns > 0) sb.Append($"\nThe loop should run {loopRuns} iteration" + (loopRuns == 1 ? "" : "s") + " in this run.");
        }
        else
        {
            sb.Append($"\nThis should output: {statedOutput}");
        }
        return sb.ToString();
    }

    // independent verification: re-parse, re-execute — goal must equal the fresh trace
    bool GoalTextAligned(string source, string goalText, string statedOutput)
    {
        var ast2 = Parse(source, out _);
        if (ast2 == null) return false;
        string actual = ExecuteProgram(ast2, out int _, out int _);
        if (actual == null) return false;
        string shown = actual.Length == 0 ? "(nothing — the program prints nothing)" : actual;
        if (shown != statedOutput) return false;
        return goalText.Contains(statedOutput);
    }

    // ================== STAGE 13: trace-match grading ==================

    bool GradeByTraceMatch(string submitted, string acceptedTrace)
    {
        var ast = Parse(submitted, out _);
        if (ast == null) return false;
        string trace = ExecuteProgram(ast, out int _, out int _);
        if (trace == null) return false;
        return trace == acceptedTrace;
    }

    // (a) equivalent alternative: rename one defined variable everywhere — via spans
    bool EquivalentByRename(string source, out string alt, out string detail)
    {
        alt = null; detail = null;
        var candidates = new List<string>();
        foreach (var raw in source.Split('\n'))
        {
            var t = raw.Trim();
            int eq = t.IndexOf(" = ");
            if (eq > 0) { var n = t.Substring(0, eq).Trim(); if (!candidates.Contains(n)) candidates.Add(n); }
        }
        if (candidates.Count == 0) { detail = "no assignments"; return false; }
        string chosen = candidates[Random.Range(0, candidates.Count)];
        string fresh = Names[Random.Range(0, Names.Length)];
        while (fresh == chosen) fresh = Names[Random.Range(0, Names.Length)];

        var ast = Parse(source, out _);
        if (ast == null) { detail = "unparseable"; return false; }
        var hits = new List<SourceSpan>();
        foreach (var n in Walk(ast, 32))
            if (n is NameExpression ne && ne.Name == chosen) hits.Add(ne.Span);
        if (hits.Count == 0) { detail = "no name occurrences"; return false; }

        hits.Sort((a, b) => TextIndex(source, b.Start.Line, b.Start.Column).CompareTo(TextIndex(source, a.Start.Line, a.Start.Column)));
        var sb = new StringBuilder(source);
        foreach (var h in hits)
        {
            int start = TextIndex(source, h.Start.Line, h.Start.Column);
            int end = TextIndex(source, h.End.Line, h.End.Column);
            if (start < 0 || end < 0) { detail = "span mapping failed"; return false; }
            sb.Remove(start, end - start);
            sb.Insert(start, fresh);
        }
        alt = sb.ToString();
        detail = $"{chosen}?{fresh} ({hits.Count} spans)";
        return true;
    }

    // (b) equivalent alternative: swap two ADJACENT INDEPENDENT top-level assignments
    // top-level only (column 0) — never touches if/for bodies or their boundaries
    bool EquivalentByIndependentSwap(string source, out string alt, out string detail)
    {
        alt = null; detail = null;
        var lines = new List<string>(source.Split('\n'));
        var idx = new List<int>();
        for (int i = 0; i + 1 < lines.Count; i++)
        {
            string a = lines[i], b = lines[i + 1];
            if (a.StartsWith(" ") || a.StartsWith("\t") || b.StartsWith(" ") || b.StartsWith("\t")) continue;
            var ta = a.Trim(); var tb = b.Trim();
            int ea = ta.IndexOf(" = "), eb = tb.IndexOf(" = ");
            if (ea <= 0 || eb <= 0) continue;
            if (ta.StartsWith("if ") || tb.StartsWith("if ") || ta.StartsWith("for ") || tb.StartsWith("for ")) continue;
            string na = ta.Substring(0, ea).Trim(), va = ta.Substring(ea + 3);
            string nb = tb.Substring(0, eb).Trim(), vb = tb.Substring(eb + 3);
            bool independent = na != nb && !vb.Contains(na) && !va.Contains(nb) && !vb.Contains(nb) && !va.Contains(na);
            if (independent) idx.Add(i);
        }
        if (idx.Count == 0) { detail = "no adjacent independent pair"; return false; }
        int pick = idx[Random.Range(0, idx.Count)];
        string tmp = lines[pick]; lines[pick] = lines[pick + 1]; lines[pick + 1] = tmp;
        alt = string.Join("\n", lines.ToArray());
        detail = $"swapped lines {pick + 1}/{pick + 2}";
        return true;
    }

    // (c) wrong submission: value substitution that changes semantics — via span
    bool WrongSubmission(string source, out string wrong, out string detail)
    {
        wrong = null; detail = null;
        var ast = Parse(source, out _);
        if (ast == null) return false;
        var consts = new List<ConstantExpression>();
        foreach (var n in Walk(ast, 32))
            if (n is ConstantExpression c && c.Value is int) consts.Add(c);
        if (consts.Count == 0) { detail = "no int constants"; return false; }

        var victim = consts[Random.Range(0, consts.Count)];
        int orig = (int)victim.Value;
        int fresh = orig;
        while (fresh == orig) fresh = Ints[Random.Range(0, Ints.Length)];

        var span = victim.Span;
        int start = TextIndex(source, span.Start.Line, span.Start.Column);
        int end = TextIndex(source, span.End.Line, span.End.Column);
        if (start < 0 || end < 0) { detail = "span mapping failed"; return false; }
        wrong = source.Substring(0, start) + fresh + source.Substring(end);
        detail = $"changed a value: {orig}?{fresh}";
        return true;
    }

    // ================== driver ==================

    // draw + derive + budget-check with retries; falls back to an always-in-budget seed
    bool TryGenerate(bool withConstructs, out string src, out string goal, out string statedOut)
    {
        for (int attempt = 0; attempt < 15; attempt++)
        {
            string cand = GenerateProgramSanitized(withConstructs);
            string g = DeriveGoalText(cand, out string so);
            // RULE 2 enforced on the FINAL program: advanced draws must stay >2 lines
            if (g != null && (!wantBig || cand.Split('\n').Length > 2)) { src = cand; goal = g; statedOut = so; return true; }
        }
        src = withConstructs ? "if 3 > 2: print(\"Found\")" : "print(\"Found\")";
        goal = DeriveGoalText(src, out statedOut);
        return goal != null;
    }

    void RunStages()
    {
        // ——— STAGE 12 block A: plain programs (mixed tiers + formats) ———
        Debug.Log("[PCG-V8] STAGE 12-A: machine-derived goalText on plain programs (tiered output budgets)");
        for (int i = 0; i < 6; i++)
        {
            try
            {
                plainTrials++;
                SetKcForTrial(Random.Range(0, 2) == 1, Random.Range(0, 3),
                    Random.Range(0, 4) == 0 ? "print" : Random.Range(0, 2) == 0 ? "variables" : "operations");
                if (!TryGenerate(false, out string src, out string stated, out string statedOutput))
                { Debug.LogError($"[PCG-V8] STAGE 12-A trial {i + 1}: no in-budget program after retries"); continue; }
                bool aligned = GoalTextAligned(src, stated, statedOutput);
                if (aligned) plainAligned++;
                else Debug.LogError($"[PCG-V8] STAGE 12-A trial {i + 1}: goalText MISALIGNED:\n{src}");
                Debug.Log($"[PCG-V8] STAGE 12-A program {i + 1} (tier={tier}, budget={allowedOutputLines} line{(allowedOutputLines == 1 ? "" : "s")}):\n{src}\n[PCG-V8] goalText ?\n{stated}\n[IronPython] final output:\n{statedOutput}");
            }
            catch (System.Exception ex) { Debug.LogError($"[PCG-V8] STAGE 12-A trial {i + 1} threw: {ex.GetType().Name}: {ex.Message} — continuing"); }
        }
        Debug.Log($"[PCG-V8] STAGE 12-A done: {plainAligned}/{plainTrials} plain goalTexts aligned");

        // ——— STAGE 12 block B: forced conditional / loop experiment ———
        Debug.Log("[PCG-V8] STAGE 12-B: conditional/loop construct experiment begins");
        for (int i = 0; i < 6; i++)
        {
            try
            {
                condTrials++;
                SetKcForTrial(true, Random.Range(0, 3), Random.Range(0, 2) == 0 ? "conditionals" : "loops"); // STB/LS budget
                if (!TryGenerate(true, out string src, out string stated, out string statedOutput))
                { Debug.LogError($"[PCG-V8] STAGE 12-B trial {i + 1}: no in-budget construct program after retries"); continue; }
                bool aligned = GoalTextAligned(src, stated, statedOutput);
                if (aligned) condAligned++;
                else Debug.LogError($"[PCG-V8] STAGE 12-B trial {i + 1}: goalText MISALIGNED on construct program:\n{src}");
                Debug.Log($"[PCG-V8] STAGE 12-B program {i + 1} (tier={tier}, budget={allowedOutputLines} line{(allowedOutputLines == 1 ? "" : "s")}):\n{src}\n[PCG-V8] goalText ?\n{stated}\n[IronPython] final output:\n{statedOutput}");
            }
            catch (System.Exception ex) { Debug.LogError($"[PCG-V8] STAGE 12-B trial {i + 1} threw: {ex.GetType().Name}: {ex.Message} — continuing"); }
        }
        Debug.Log($"[PCG-V8] STAGE 12-B done: {condAligned}/{condTrials} conditional/loop goalTexts aligned");

        // ——— STAGE 13: trace-match grading ———
        Debug.Log("[PCG-V8] STAGE 13: trace-match grading begins");
        for (int i = 0; i < 4; i++)
        {
            try
            {
                SetKcForTrial(Random.Range(0, 2) == 1, Random.Range(0, 3), Random.Range(0, 2) == 0 ? "variables" : "operations");
                if (!TryGenerate(false, out string seed, out string goal, out string so))
                { Debug.LogWarning($"[PCG-V8] STAGE 13 puzzle {i + 1}: no in-budget program — puzzle skipped"); continue; }
                var ast = Parse(seed, out _);
                if (ast == null) continue;
                string acceptedTrace = ExecuteProgram(ast, out int _, out int _) ?? "";
                Debug.Log($"[PCG-V8] STAGE 13 puzzle {i + 1} (tier={tier}, budget={allowedOutputLines}):\n{seed}\n[PCG-V8] goalText ?\n{goal}\n[IronPython] final output:\n{acceptedTrace}");

                if (EquivalentByRename(seed, out string altA, out string dA) && altA != seed)
                {
                    bool passA = GradeByTraceMatch(altA, acceptedTrace);
                    gradeTrials++; if (passA) gradeOk++;
                    Debug.Log($"[PCG-V8] submit (rename, {dA}):\n{altA}\n? {(passA ? "ACCEPTED ? equivalent code accepted" : "REJECTED ? BUG: equivalent code rejected")}");
                }

                if (EquivalentByIndependentSwap(seed, out string altB, out string dB) && altB != seed)
                {
                    bool passB = GradeByTraceMatch(altB, acceptedTrace);
                    gradeTrials++; if (passB) gradeOk++;
                    Debug.Log($"[PCG-V8] submit (swap, {dB}):\n{altB}\n? {(passB ? "ACCEPTED ? independent line order accepted" : "REJECTED ? BUG: equivalent code rejected")}");
                }

                bool doneC = false;
                for (int attemptC = 0; attemptC < 4 && !doneC; attemptC++)
                {
                    if (!WrongSubmission(seed, out string altC, out string dC)) break;
                    bool passC = GradeByTraceMatch(altC, acceptedTrace);
                    if (passC) continue; // substituted value invisible to output — re-draw
                    gradeTrials++; gradeOk++; doneC = true;
                    Debug.Log($"[PCG-V8] submit (wrong value, {dC}):\n{altC}\n? REJECTED ? wrong code rejected");
                }
            }
            catch (System.Exception ex) { Debug.LogError($"[PCG-V8] STAGE 13 puzzle {i + 1} threw: {ex.GetType().Name}: {ex.Message} — continuing"); }
        }
        Debug.Log($"[PCG-V8] STAGE 13 done: {gradeOk}/{gradeTrials} grading decisions correct");

        // ——— STAGE 14: per-KC × per-tier matrix (one puzzle per KC per difficulty) ———
        Debug.Log("[PCG-V8] STAGE 14: per-KC × per-tier matrix begins");
        string[] kcNames = { "Print Console", "Vars Vault", "Input Mists", "Elif Labyrinth" };
        for (int kcI = 0; kcI < 4; kcI++)
        {
            for (int tI = 0; tI < 3; tI++)
            {
                try
                {
                    kcTrials++;
                    string kcForThis = kcI == 0 ? "print"
                        : kcI == 1 ? (Random.Range(0, 2) == 0 ? "variables" : "operations")
                        : kcI == 2 ? "input"
                        : (Random.Range(0, 2) == 0 ? "conditionals" : "loops");
                    SetKcForTrial(kcI == 3, tI, kcForThis); // Elif Labyrinth runs under the STB/LS budget
                    // STRICT isolation: conditionals NEVER leak into Input Mists; Print Console
                    // stays print-scoped. Constructs only in Elif Labyrinth.
                    bool useConstructs = kcI == 3;
                    if (!TryGenerate(useConstructs, out string ksrc, out string kgoal, out string kout))
                    { Debug.LogError($"[PCG-V8] STAGE 14 [{kcNames[kcI]}]: no in-budget puzzle"); continue; }
                    var kast = Parse(ksrc, out string kerr);
                    if (kast == null) { Debug.LogError($"[PCG-V8] STAGE 14 [{kcNames[kcI]}]: parse FAILED: {kerr}\n{ksrc}"); continue; }
                    kcOk++;
                    string tierName = tI == 0 ? "Beginner" : tI == 1 ? "Intermediate" : "Advanced";
                    Debug.Log($"[PCG-V8] STAGE 14 [{kcNames[kcI]} / {tierName} / kc={kcForThis}] (codeLines={ksrc.Split('\n').Length}, budget={allowedOutputLines}):\n{ksrc}\n[PCG-V8] goalText ?\n{kgoal}\n[IronPython] final output:\n{kout}");
                }
                catch (System.Exception ex) { Debug.LogError($"[PCG-V8] STAGE 14 KC {kcI} tier {tI} threw: {ex.GetType().Name}: {ex.Message} — continuing"); }
            }
        }
        Debug.Log($"[PCG-V8] STAGE 14 done: {kcOk}/{kcTrials} KC×tier puzzles generated and validated");

        // ——— STAGE 15: input() really appears + Input-Mists isolation ———
        Debug.Log("[PCG-V8] STAGE 15: input() handling + isolation probe begins");
        for (int tI = 0; tI < 3; tI++)
        {
            try
            {
                inputTrials++;
                SetKcForTrial(false, tI, "input");
                if (!TryGenerate(false, out string isrc, out string igoal, out string iout))
                { Debug.LogError($"[PCG-V8] STAGE 15 tier {tI}: no in-budget input puzzle"); continue; }
                bool hasInput = isrc.Contains("input(");
                bool noLeaks = !isrc.Contains("if ") && !isrc.Contains("for ");
                var iast = Parse(isrc, out string ierr);
                if (iast != null && hasInput && noLeaks) inputOk++; // deliberate strict gate
                else Debug.LogError($"[PCG-V8] STAGE 15 tier {tI}: hasInput={hasInput} noLeaks={noLeaks} parseOk={(iast != null)}\n{isrc}");
                string tName = tI == 0 ? "Beginner" : tI == 1 ? "Intermediate" : "Advanced";
                Debug.Log($"[PCG-V8] STAGE 15 [Input Mists / {tName}] (codeLines={isrc.Split('\n').Length}, budget={allowedOutputLines}):\n{isrc}\n[PCG-V8] goalText ?\n{igoal}\n[IronPython] final output:\n{iout}");
            }
            catch (System.Exception ex) { Debug.LogError($"[PCG-V8] STAGE 15 tier {tI} threw: {ex.GetType().Name}: {ex.Message} — continuing"); }
        }
        Debug.Log($"[PCG-V8] STAGE 15 done: {inputOk}/{inputTrials} input puzzles valid (input() present, zero leaks)");
    }

    // ================== shared machinery ==================

    static IEnumerable<Node> Walk(Node root, int maxDepth)
    {
        var visited = new HashSet<Node>();
        var stack = new Stack<(Node, int)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            if (node == null || depth > maxDepth || !visited.Add(node)) continue;
            yield return node;
            foreach (var child in ChildrenOf(node)) stack.Push((child, depth + 1));
        }
    }

    static IEnumerable<Node> ChildrenOf(Node node)
    {
        var list = new List<Node>();
        try
        {
            if (node is PythonAst root) { list.Add(root.Body); return list; }
            if (node is SuiteStatement s) { foreach (var st in s.Statements) list.Add(st); return list; }
            if (node is AssignmentStatement a) { foreach (var t in a.Left) list.Add(t); list.Add(a.Right); return list; }
            if (node is ExpressionStatement e) { list.Add(e.Expression); return list; }
            if (node is CallExpression c) { list.Add(c.Target); foreach (var arg in c.Args) list.Add(arg); return list; }
            if (node is BinaryExpression b) { list.Add(b.Left); list.Add(b.Right); return list; }
        }
        catch { }
        try
        {
            if (!(node is NameExpression || node is ConstantExpression) && list.Count == 0)
            {
                foreach (var p in node.GetType().GetProperties())
                {
                    if (p.Name == "Parent" || p.Name == "Span" || !p.CanRead) continue;
                    object v = null; try { v = p.GetValue(node); } catch { }
                    if (v is Node c2) list.Add(c2);
                    else if (v is System.Collections.IEnumerable en && !(v is string))
                        foreach (var it in en) if (it is Node cn) list.Add(cn);
                }
            }
        }
        catch { }
        return list;
    }

    // cached hosting engine — creating one per parse was pathological (deep in the hang too)
    static Microsoft.Scripting.Hosting.ScriptEngine engineCache;
    Microsoft.Scripting.Hosting.ScriptEngine SharedEngine()
    {
        if (engineCache == null) engineCache = Python.CreateEngine();
        return engineCache;
    }

    PythonAst Parse(string code, out string error)
    {
        error = null;
        try
        {
            var engine = SharedEngine(); // cached — engine creation per parse was pathological
            var source = engine.CreateScriptSourceFromString(code, SourceCodeKind.Statements);
            var unit = HostingHelpers.GetSourceUnit(source);
            var ctx = new CompilerContext(unit, new PythonCompilerOptions(), ErrorSink.Default);
            var ast = Parser.CreateParser(ctx, new PythonOptions()).ParseFile(false);
            if (ast == null) error = "ParseFile returned null";
            if (error != null && logParseFailures) Debug.LogWarning($"[PCG-V8][diag] Parse failed ({error}) on:\n{code}");
            return ast;
        }
        catch (System.Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            if (logParseFailures) Debug.LogWarning($"[PCG-V8][diag] Parse threw {error} on:\n{code}");
            return null;
        }
    }

    static int TextIndex(string text, int line, int column)
    {
        int idx = 0, l = 1, c = 1;
        while (idx < text.Length)
        {
            if (l == line && c == column) return idx;
            if (text[idx] == '\n') { l++; c = 1; } else c++;
            idx++;
        }
        return (l == line && c == column) ? idx : -1;
    }
}