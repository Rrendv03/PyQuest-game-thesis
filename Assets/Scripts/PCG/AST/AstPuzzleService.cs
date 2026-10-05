// PyQuest — Phase C: AstPuzzleService.cs
// Target: Assets/Scripts/PCG/Ast/AstPuzzleService.cs
//
// Request pipeline (09 §4 "PCGEngine.GeneratePuzzle -> if (useGenerativePath)"):
//   ServeRequest(zone / knowledgeComponent, tier, type, seed) ->
//   1 generate      KcGrammar, FIXED structured order (defect D1: scaffold -> focal
//                   construct -> print-when-needed, then rules run as VERIFIERS)
//   2 parse         PythonAstGateway (IronPython = parser only, never an engine on device)
//   3 audit         SubsetChecker — named RULE<n> violations, fail-fast, never a shape
//                   re-draw; every rejection is logged + counted (defect D5 metrics)
//   4 mutate        AstMutators span splices, with the D2 branch-coherence verifier and
//                   the D3 "re-derive from the FINAL run" contract
//   5 execute ONCE  the post-mutation execution below is the CANONICAL answer source
//                   (defect D3: answer keys are never derived before the final run)
//   6 derive        per-format answer key + proven distractors (09 §6.6 AnswerDeriver)
//   7 metadata      PuzzleContextMetadata (trace facts, D4 Inputs, goal/error facts)
//   8 legacy dataclass  LegacyPuzzleTemplate (UnityEngine-free mirror of PuzzleTemplate;
//                   PCGEngine adapts it and owns the `useGenerativePath` rollback switch)
//
// Rollback invariant: after 15 failed attempts the service returns null and the caller
// serves from the pre-expanded legacy pool — a failed request is never a failed serve.
// UnityEngine-free (log pipes are injected by PCGEngine). Wire:
//     AstPuzzleService.LogWarning = m => Debug.LogWarning(m);
//     AstPuzzleService.Log        = m => Debug.Log(m);
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using IronPython.Compiler;
using IronPython.Compiler.Ast;

namespace PyQuest.Pcg.Ast
{
    public static class AstPuzzleService
    {
        public const int MaxAttempts = 15; // 09 §9 rounds; beyond it the legacy pool serves

        // ——— zone-string -> KC map (ZoneTrigger / knowledgeComponent strings) ———
        public static string KcFromZone(string zone)
        {
            if (string.IsNullOrEmpty(zone)) return "print";
            var z = zone.ToLowerInvariant();
            // construct zones first (substring order matters for names like
            // "Print Console" / "Vars Vault" / "Input Mists" / "Elif Labyrinth")
            if (z.Contains("condition") || z.Contains("elif") || z.Contains("if ")) return "conditionals";
            if (z.Contains("loop") || z.Contains("range") || z.Contains("iter")) return "loops";
            if (z.Contains("input") || z.Contains("mists")) return "input";
            if (z.Contains("operation") || z.Contains("arithmet") || z.Contains("math")) return "operations";
            if (z.Contains("variable") || z.Contains("vars") || z.Contains("vault")) return "variables";
            if (z.Contains("print") || z.Contains("console")) return "print";
            return "print";
        }

        public static Action<string> LogWarning { get; set; } = delegate { };
        public static Action<string> Log { get; set; } = delegate { };

        /// <summary>D5 service-side conformance counters: named rejection class -> count.
        /// Every failed attempt is attributed to ONE named rule (never silently dropped).</summary>
        public static readonly SortedDictionary<string, int> ServeStats = new SortedDictionary<string, int>();
        static void Bump(string key) { ServeStats[key] = (ServeStats.ContainsKey(key) ? ServeStats[key] : 0) + 1; }
        public static void ResetStats() { ServeStats.Clear(); }

        /// <summary>§6.5 rule 3: output budget on the EXECUTED trace.
        /// PTO/TF/FITB/PAC = exactly 1 line; STB/LS = B:1 · I:1–3 · A:2–5.</summary>
        public static int TraceBudgetForFormat(string format, int tier)
        {
            if (format == "STB" || format == "LS")
            {
                if (tier <= 0) return 1;
                if (tier == 1) return 3;
                return 5;
            }
            return 1;
        }

        public static string PuzzleTypeToFormat(PuzzleTypeCode t)
        {
            switch (t)
            {
                case PuzzleTypeCode.TrueOrFalse: return "TF";
                case PuzzleTypeCode.FillInTheBlank: return "FITB";
                case PuzzleTypeCode.SpotTheBug: return "STB";
                case PuzzleTypeCode.LineScramble: return "LS";
                case PuzzleTypeCode.PairACode: return "PAC";
                default: return "PTO";
            }
        }

        /// <summary>Full serve. Null ⇒ generative path exhausted ⇒ legacy pool takes over.</summary>
        public static PuzzleResult ServeRequest(string zoneKnowledgeComponent, int tier, PuzzleTypeCode type, int seed)
        {
            string kc = KcFromZone(zoneKnowledgeComponent);
            string format = PuzzleTypeToFormat(type);
            int budget = TraceBudgetForFormat(format, tier);

            if (!PythonAstGateway.Available)
            {
                Bump("GATEWAY: " + (PythonAstGateway.UnavailableReason ?? "unavailable"));
                return null;
            }

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var req = new PuzzleRequest
                {
                    Kc = kc,
                    Tier = tier,
                    Format = format,
                    Seed = unchecked(seed * 104729 + attempt * 7919 + 13)
                };
                try
                {
                    var built = BuildAttempt(req, kc, tier, format, type, budget, seed);
                    if (built != null) return built;
                }
                catch (Exception ex)
                {
                    Bump("EXCEPTION: " + ex.GetType().Name);
                    LogWarning("[PCG][gen] attempt " + (attempt + 1) + "/" + MaxAttempts
                               + " threw " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            Bump("EXHAUSTED " + kc + "/" + tier + "/" + format);
            return null;
        }

        // ============ one generation attempt ============

        /// <summary>Grammar-layer enrichment so construct-deriving formats are
        /// derivable: SpotTheBug/LineScramble mechanics need >= 2 code lines,
        /// and a beginner print draw is a single statement. A charter-legal
        /// string-constant scaffold line keeps the 1-trace budget intact.</summary>
        static void EnsureDerivable(ProgramDraft draft, string format)
        {
            if (draft.Code == null) return;
            if (format != "STB" && format != "LS") return;
            if (draft.Code.Split('\n').Count(l => l.Trim().Length > 0) >= 2) return;
            // a SCAFFOLD line ABOVE the print (09 §3.1 print_statements/
            // variables scaffolds: a string constant) keeps the 1-trace budget:
            // a bare constant expression executes to zero output. The print
            // stays at index >= 1 where the STB/LS mechanics pick a bug line.
            string scaffold = null;
            foreach (var s in KcGrammar.Strings)
                if (!draft.Code.Contains("\"" + s + "\"")) { scaffold = "\"" + s + "\""; break; }
            if (scaffold == null) return; // pool exhausted on this draw
            draft.Code = scaffold + "\n" + draft.Code;
        }


        static PuzzleResult BuildAttempt(PuzzleRequest req, string kc, int tier, string format,
                                         PuzzleTypeCode type, int budget, int seed)
        {
            // —— 1. generate (D1: shape is fixed by the grammar; rules only ever REJECT) ——
            var draft = KcGrammar.BuildProgram(req);
            EnsureDerivable(draft, format);   // STB/LS need >= 2 code lines
            if (draft.Code == null || draft.Code.Trim().Length == 0)
            {
                Bump("GRAMMAR: " + (draft.RuleLog ?? "empty draft"));
                return null;
            }

            // —— 2. parse ——
            PythonAst ast; string perr;
            if (!PythonAstGateway.TryParse(draft.Code, out ast, out perr))
            {
                Bump("PARSE: " + Short(perr));
                LogWarning("[PCG][gen][parse] " + perr + " | " + OneLine(draft.Code));
                return null;
            }

            // —— 3. execute the draft + named-verifier audit ——
            var auditReq = new CheckerRequest
            {
                Seed = seed,
                Kc = kc,
                Tier = tier,
                Format = format,
                MaxTraceLines = budget,
                Inputs = draft.Inputs
            };
            var draftExec = new SubsetInterpreter(draft.Inputs).Execute(ast, draft.Code, SubsetInterpreter.MaxTraceLines);
            if (!draftExec.Success)
            {
                Bump("EXEC: " + draftExec.FailureKind);
                LogWarning("[PCG][gen][exec] " + draftExec.FailureKind + ": " + draftExec.FailureMessage
                           + " | " + OneLine(draft.Code));
                return null;
            }
            var audit = SubsetChecker.Check(draft.Code, ast, auditReq, draftExec.Trace);
            if (!audit.Pass)
            {
                foreach (var v in audit.Violations)
                {
                    Bump(RuleKey(v));
                    LogWarning("[PCG][gen][" + RuleKey(v) + "] " + v + " | " + OneLine(draft.Code));
                }
                return null;
            }

            // —— 4. mutate per tier (D2 coherence + D3 re-derive live inside the gates) ——
            var mo = AstMutators.MutateAndFinalize(draft.Code, auditReq, draft.Inputs);
            if (mo == null || mo.Code == null)
            {
                Bump("MUTATOR: " + (mo == null ? "null" : Short(mo.RuleViolation)));
                return null;
            }
            string cleaned = mo.Code;

            // —— 5. THE single canonical post-mutation execution (defect D3) ——
            PythonAst finalAst; string ferr;
            if (!PythonAstGateway.TryParse(cleaned, out finalAst, out ferr))
            {
                Bump("FINAL-PARSE: " + Short(ferr));
                return null;
            }
            var finalExec = new SubsetInterpreter(draft.Inputs).Execute(finalAst, cleaned, SubsetInterpreter.MaxTraceLines);
            if (!finalExec.Success)
            {
                Bump("FINAL-EXEC: " + finalExec.FailureKind);
                return null;
            }
            if (finalExec.Trace.Count < 1 || finalExec.Trace.Count > budget)
            {
                Bump("RULE3: final trace=" + finalExec.Trace.Count + " outside budget=1.." + budget);
                return null;
            }
            var finalAudit = SubsetChecker.Check(cleaned, finalAst, auditReq, finalExec.Trace);
            if (!finalAudit.Pass)
            {
                foreach (var v in finalAudit.Violations) Bump("POSTMUT " + RuleKey(v));
                LogWarning("[PCG][gen][postmut] " + finalAudit.Violations[0] + " | " + OneLine(cleaned));
                return null;
            }
            // D2 stands on its own: a mutated if/elif chain must keep exclusive branches.
            if (!AstMutators.BranchCoherence(cleaned))
            {
                Bump("D2: branch-chain incoherence after mutation");
                LogWarning("[PCG][gen][D2] branch chain lost strict-descending thresholds | " + OneLine(cleaned));
                return null;
            }

            // —— 6./7. derivation + metadata from the FINAL run ——
            var ctx = BuildMetadata(cleaned, kc, tier, format, type, draft, mo, finalExec, draftExec, budget);
            if (ctx == null) return null;

            var tpl = BuildTemplate(ctx, type, draft);
            if (tpl == null) return null;

            return new PuzzleResult { Metadata = ctx, Template = tpl };
        }

        // ============ metadata builder (trace-derived facts only — never authored prose) ============

        static PuzzleContextMetadata BuildMetadata(string code, string kc, int tier, string format,
                                                    PuzzleTypeCode type, ProgramDraft draft,
                                                    AstMutators.MutationOutcome mo,
                                                    ExecResult finalExec, ExecResult cleanExec,
                                                    int budget)
        {
            var ctx = new PuzzleContextMetadata
            {
                Kc = kc,
                Tier = tier,
                Format = format,
                Code = code
            };

            foreach (var t in finalExec.Trace)
                ctx.Trace.Add(new PuzzleContextMetadata.TraceFact
                {
                    Text = t.Text,
                    SourceLine = t.SourceLine,
                    InsideConstruct = t.InsideConstruct
                });
            foreach (var kv in finalExec.FinalVars)
                ctx.FinalVars[kv.Key] = SubsetInterpreter.Stringify(kv.Value);

            // —— D4: one InputLine per input() call, prompt + preset + numeric flag ——
            for (int i = 0; i < Math.Min(finalExec.InputPrompts.Count, draft.Inputs.Count); i++)
            {
                ctx.Inputs.Add(new PuzzleContextMetadata.InputLine
                {
                    Prompt = finalExec.InputPrompts[i],
                    Preset = draft.Inputs[i],
                    Numeric = InputLineIsNumeric(code, i)
                });
            }
            if (draft.Inputs.Count > ctx.Inputs.Count)
            {
                // preset without a prompt (EOL guard) — still first-class data
                for (int i = ctx.Inputs.Count; i < draft.Inputs.Count; i++)
                    ctx.Inputs.Add(new PuzzleContextMetadata.InputLine
                    {
                        Prompt = "",
                        Preset = draft.Inputs[i],
                        Numeric = InputLineIsNumeric(code, i)
                    });
            }

            // —— answer + options, per format (09 §6.6) ——
            if (!DeriveAnswers(ctx, type, draft, mo, finalExec, cleanExec))
            {
                Bump("DERIVE: " + type + " could not prove a unique answer");
                return null;
            }

            // —— goal facts (UI renders the header; the engine never writes sentences) ——
            ctx.GoalFacts.Add("kc=" + kc);
            ctx.GoalFacts.Add("tier=" + tier);
            ctx.GoalFacts.Add("format=" + format);
            ctx.GoalFacts.Add("expectedOutput=" + TraceKey(finalExec));
            foreach (var kv in ctx.FinalVars.OrderBy(p => p.Key, StringComparer.Ordinal))
                ctx.GoalFacts.Add("var=" + kv.Key + "=" + kv.Value);
            foreach (var il in ctx.Inputs)
                ctx.GoalFacts.Add("input=" + il.Prompt + "|" + il.Preset + (il.Numeric ? "|int" : "|str"));

            // —— error facts (rendered into the dedicated errorText element on a wrong answer) ——
            ctx.ErrorFacts.Add("expectedOutput=" + TraceKey(finalExec));
            if (ctx.Inputs.Count > 0)
                ctx.ErrorFacts.Add("inputPreset=" + string.Join(",", ctx.Inputs.Select(i => i.Preset).ToArray()));
            foreach (var kv in ctx.FinalVars.OrderBy(p => p.Key, StringComparer.Ordinal))
                ctx.ErrorFacts.Add("afterRun=" + kv.Key + "=" + kv.Value);
            if (mo != null && mo.Kind != null)
                ctx.ErrorFacts.Add("mutation=" + mo.Kind);

            return ctx;
        }

        /// <summary>D4 helper: does the i-th input() call convert with int()? Answered from
        /// the SERVED source (mechanically tied to code — never inferred prose).</summary>
        static bool InputLineIsNumeric(string code, int inputIndex)
        {
            if (code == null) return false;
            int seen = 0;
            foreach (var line in code.Split('\n'))
            {
                int at = line.IndexOf("input(", StringComparison.Ordinal);
                if (at < 0) continue;
                if (seen == inputIndex)
                    return line.Substring(0, at).Contains("int(");
                seen++;
            }
            return false;
        }

        // ============ 09 §6.6 answer derivation (with execution proofs) ============

        static bool DeriveAnswers(PuzzleContextMetadata ctx, PuzzleTypeCode type,
                                  ProgramDraft draft, AstMutators.MutationOutcome mo,
                                  ExecResult finalExec, ExecResult cleanExec)
        {
            string correctKey = TraceKey(finalExec);
            switch (type)
            {
                case PuzzleTypeCode.PredictTheOutput:
                    ctx.CorrectAnswer = correctKey;
                    return ForgeAnswerOptions(ctx, draft, correctKey);

                case PuzzleTypeCode.TrueOrFalse:
                    // §6.5 TF: base executes to O1, mutated to O2; verdict = (O1 == O2),
                    // proved by executing BOTH (rename-only mutations ground a "True").
                    bool same = TraceKey(cleanExec) == correctKey;
                    ctx.CorrectAnswer = same ? "True" : "False";
                    ctx.Options.Add(NewOption(ctx.CorrectAnswer, "TfVerdict", true));
                    ctx.Options.Add(NewOption(same ? "False" : "True", "TfVerdict", false));
                    return true;

                case PuzzleTypeCode.SpotTheBug:
                    return ForgeBugLine(ctx, draft, finalExec);

                case PuzzleTypeCode.PairACode:
                    return ForgePairLine(ctx, draft, finalExec);

                case PuzzleTypeCode.FillInTheBlank:
                    return ForgeBlank(ctx, draft, finalExec);

                case PuzzleTypeCode.LineScramble:
                default:
                    // canonical order is always a valid member of acceptedOrders; the
                    // format's dependency validator keeps loosening it format-side.
                    ctx.CorrectAnswer = string.Join(",", Enumerable.Range(0, LineCount(ctx.Code)).ToArray());
                    ctx.AcceptedOrders.Add(ctx.CorrectAnswer);
                    return ctx.AcceptedOrders.Count > 0;
            }
        }

        static PuzzleContextMetadata.OptionFact NewOption(string text, string kind, bool correct)
        {
            return new PuzzleContextMetadata.OptionFact { Text = text, MisconceptionKind = kind, Correct = correct };
        }

        /// <summary>PTO: 3 proven-wrong answers + the executed truth. Answer-level forging
        /// (offset / other-var / name-echo / quote-toggle) — each distractor is computed
        /// from the FINAL run or the code, never authored.</summary>
        static bool ForgeAnswerOptions(PuzzleContextMetadata ctx, ProgramDraft draft, string correct)
        {
            var wrongs = new List<string>();

            // (a) arithmetic offset: first integer in the answer shifted ±1
            var m = Regex.Match(correct, @"\d+");
            if (m.Success)
            {
                long v; if (long.TryParse(m.Value, out v))
                {
                    if (v + 1 >= 0) wrongs.Add(correct.Substring(0, m.Index) + (v + 1) + correct.Substring(m.Index + m.Length));
                    if (v - 1 >= 0) wrongs.Add(correct.Substring(0, m.Index) + (v - 1) + correct.Substring(m.Index + m.Length));
                }
            }
            // (b) other-variable: another final variable's value that is not the answer
            foreach (var kv in ctx.FinalVars.OrderBy(p => p.Key, StringComparer.Ordinal))
                if (wrongs.Count < 3 && kv.Value != correct) wrongs.Add(kv.Value);
            // (c) name-echo: a defined variable's NAME (print(x) vs x confusion)
            foreach (var kv in ctx.FinalVars.OrderBy(p => p.Key, StringComparer.Ordinal))
                if (wrongs.Count < 3 && kv.Key != correct && !wrongs.Contains(kv.Key)) wrongs.Add(kv.Key);
            // (d) quote toggle: strip/quote wrapper
            string toggled = correct.StartsWith("\"") ? correct.Substring(1)
                                                   : "\"" + correct;
            if (toggled != correct && wrongs.Count < 3) wrongs.Add(toggled);
            // (e) themed fallbacks (machine-legal Python values, never real outputs)
            foreach (var f in new[] { "None", "True", "False", "0" })
                if (wrongs.Count < 3 && f != correct) wrongs.Add(f);

            wrongs = wrongs.Where(w => w != correct).Distinct().ToList();
            // the only proof available for answer-level options: each claim differs from
            // the executed truth (uniqueness by construction) and from its siblings.
            if (wrongs.Count < 3) return false;

            foreach (var w in wrongs) ctx.Options.Add(NewOption(w, OptionsKind(w, correct), false));
            ctx.Options.Insert(0, NewOption(correct, "correct", true));
            return true;
        }

        static string OptionsKind(string w, string correct)
        {
            var mw = Regex.Match(w, @"\d+"); var mc = Regex.Match(correct, @"\d+");
            if (mw.Success && mc.Success && mw.Value != mc.Value) return "PtoDistractorOffset";
            if (w.StartsWith("\"")) return "PtoDistractorQuoteToggle";
            if (w == w.Trim()) return "PtoDistractorNameEcho";
            return "PtoDistractorOffBy";
        }

        /// <summary>STB: inject a PROVEN bug (execution differs) at one line, serve the
        /// dirty line as displayed code and key the answer to the clean line. Options the
        /// format builds type-side; metadata carries the proven fix + 3 proven-wrong fixes.</summary>
        static bool ForgeBugLine(PuzzleContextMetadata ctx, ProgramDraft draft, ExecResult clean)
        {
            string cleanCode = ctx.Code;
            string[] lines = cleanCode.Split('\n');
            if (lines.Length < 2) { Bump("DERIVE-STB: advanced/min-line floor (STB needs >= 2 lines)"); return false; }
            string cleanKey = TraceKey(clean);
            var defined = DefinedNames(cleanCode);
            Random rng = new Random(cleanCode.Length * 31 + lines.Length);

            for (int tries = 0; tries < 12; tries++)
            {
                int idx = rng.Next(1, lines.Length); // line 0 is usually the scaffold
                var variants = LineVariants(lines[idx], defined).ToList();
                foreach (var nv in variants)
                {
                    string dirty = lines[idx];
                    lines[idx] = nv;
                    string displayCode = string.Join("\n", lines);
                    var dirtyExec = ExecuteQuiet(displayCode, draft.Inputs);
                    // Phase C fix: a bugged line that BREAKS execution (NameError
                    // family) is a legitimate proven bug — the displayed program
                    // misbehaves and the answer is still the clean line; only a
                    // no-op (identical trace) is rejected.
                    if (dirtyExec != null && TraceKey(dirtyExec) == cleanKey) { continue; }
                    // proven bug found: `displayCode` shows nv, and theanswered key stays clean
                    string displayed = nv;
                    ctx.CodeChangedLine = idx;
                    ctx.BugKind = TextualBugKind(dirty, nv);
                    ctx.Code = displayCode;
                    lines[idx] = dirty; // restore, the option loop rebuilds from cleanCode

                    // fix options: correct = clean line, plus proven-wrong alternatives
                    ctx.CorrectAnswer = dirty; // the clean line is the correct fix (matches PAC, line 472)
                    ctx.Options.Add(NewOption(dirty, "correct-fix", true));
                    int wrongs = 0;
                    foreach (var alt in LineVariants(displayed, defined).Take(6))
                    {
                        if (alt == dirty) continue;
                        var altLines = cleanCode.Split('\n'); altLines[idx] = alt;
                        var altExec = ExecuteQuiet(string.Join("\n", altLines), draft.Inputs);
                        // A wrong FIX is one whose execution does NOT restore the
                        // clean trace (an erroring fix counts too). An alt that
                        // restores the clean trace is behaviorally the correct fix
                        // (trace grading) — ambiguous duplicate, never an option.
                        if (altExec == null || TraceKey(altExec) != cleanKey)
                        {
                            ctx.Options.Add(NewOption(alt, "StbFixWrong", false)); wrongs++;
                        }
                        if (wrongs >= 3) break;
                    }
                    if (wrongs < 2)
                    { // not enough proven-wrong fixes: reset and retry
                        ctx.Options.Clear();
                        ctx.Code = cleanCode;
                        lines = cleanCode.Split('\n');
                        break;
                    }
                    return true;
                }
                lines = cleanCode.Split('\n');
            }
            Bump("DERIVE-STB: no provable bug in 12 line draws");
            return false;
        }

        /// <summary>PAC: blank the LAST line; 3 forged candidate lines, each executed in
        /// place of the blank and proven to change the trace (or fail to parse).</summary>
        static bool ForgePairLine(PuzzleContextMetadata ctx, ProgramDraft draft, ExecResult clean)
        {
            string cleanCode = ctx.Code;
            string[] lines = cleanCode.Split('\n');
            // Phase C fix (conditionals): blank the LAST line that actually
            // executed. On an untaken branch line NO candidate variant can
            // change the trace (all prove no-op), so PAC/FITB cells exhausted
            // there. Headers (if/elif/else/for/while) are never blank candidates.
            int blank = LastExecutedDerivableLine(lines, TraceLineSet(clean));
            string correctLine = lines[blank];
            string cleanKey = TraceKey(clean);
            var defined = DefinedNames(cleanCode);

            ctx.CorrectAnswer = correctLine;
            ctx.Options.Add(NewOption(correctLine, "correct", true));
            int wrongs = 0;
            foreach (var alt in LineVariants(correctLine, defined).Take(8))
            {
                if (alt == correctLine) continue;
                var altLines = (string[])lines.Clone(); altLines[blank] = alt;
                var altExec = ExecuteQuiet(string.Join("\n", altLines), draft.Inputs);
                if (altExec == null || TraceKey(altExec) != cleanKey)
                {
                    ctx.Options.Add(NewOption(alt, "PacWrongLine", false));
                    wrongs++;
                    if (wrongs >= 3) break;
                }
            }
            if (wrongs < 3) { ctx.Options.Clear(); Bump("DERIVE-PAC: only " + wrongs + " proven-wrong lines"); return false; }
            return true;
        }

        /// <summary>FITB: pick a literal token, key it as the blank, and prove 3 wrong
        /// tokens (substituted token must fail to parse or change the trace).</summary>
        static bool ForgeBlank(PuzzleContextMetadata ctx, ProgramDraft draft, ExecResult clean)
        {
            string cleanCode = ctx.Code;
            string cleanKey = TraceKey(clean);
            // choose a printable literal on a line that is not an input prompt
            // (Phase C fix: line-level filtering — the char-level exclusion used
            // previously also dropped every literal inside print("..."), leaving
            // all beginner-output FITB cells with no token to blank)
            var literals = BlankTokens(cleanCode);
            if (literals.Count == 0) literals = BlankTokensNames(cleanCode);
            if (literals.Count == 0) { Bump("DERIVE-FITB: no token to blank"); return false; }
            // blank tokens must live on an EXECUTED line — untaken-branch
            // tokens prove no-op under every variant (phase C fix)
            var tracedLines0 = TraceLineSet(clean);
            if (tracedLines0.Count > 0)
            {
                var onTrace0 = literals.Where(tk => tracedLines0.Contains(LineIndexOf(cleanCode, tk.Index))).ToList();
                if (onTrace0.Count > 0) literals = onTrace0;
            }

            var rng = new Random(cleanKey.Length + literals.Count);
            // 2026-10-01 fix: trace lines are PRINTED OUTPUT ONLY, so a literal on a
            // pure assignment line can be exec/redrawn with no observable trace change
            // (0 provable wrongs → 15 exhausted attempts). Blank tokens must be
            // PROVABLY trace-discriminating: one substitute changes the trace.
            TokenRef chosen = null;
            foreach (var tk in literals.OrderBy(x => rng.Next()).ToList())
            {
                string firstAlt = TokenVariants(tk.Value).FirstOrDefault(a => a != tk.Value);
                if (firstAlt == null) continue;
                var probe = ExecuteQuiet(cleanCode.Substring(0, tk.Index) + firstAlt + cleanCode.Substring(tk.Index + tk.Value.Length), draft.Inputs);
                if (probe != null && TraceKey(probe) != cleanKey) { chosen = tk; break; }
            }
            if (chosen == null) { Bump("DERIVE-FITB: no trace-discriminating blank"); return false; }
            ctx.CorrectAnswer = chosen.Value;
            ctx.CodeChangedLine = LineIndexOf(cleanCode, chosen.Index);
            ctx.Options.Add(NewOption(chosen.Value, "correct", true));

            int wrongs = 0;
            foreach (var alt in TokenVariants(chosen.Value).Take(8))
            {
                if (alt == chosen.Value) continue;
                var altExec = ExecuteQuiet(cleanCode.Substring(0, chosen.Index) + alt + cleanCode.Substring(chosen.Index + chosen.Value.Length), draft.Inputs);
                if (altExec == null || TraceKey(altExec) != cleanKey)
                {
                    ctx.Options.Add(NewOption(alt, "FitbWrongToken", false));
                    wrongs++;
                    if (wrongs >= 3) break;
                }
            }
            if (wrongs < 3) { ctx.Options.Clear(); Bump("DERIVE-FITB: only " + wrongs + " proven-wrong tokens chosen=" + chosen.Value + " @" + cleanCode.Substring(Math.Max(0, chosen.Index - 12), Math.Min(24, cleanCode.Length - Math.Max(0, chosen.Index - 12)))); return false; }
            return true;
        }

        /// <summary>Absolute-index blank-token reference (replaces the raw Match
        /// once token candidates are collected per line).</summary>
        class TokenRef { public int Index; public string Value; }

        static List<TokenRef> BlankTokens(string cleanCode)
        {
            var tokens = new List<TokenRef>();
            int offset = 0;
            foreach (var ln in cleanCode.Split('\n'))
            {
                if (ln.Contains("input(") || ln.Contains("# Bug")) { offset += ln.Length + 1; continue; }
                foreach (Match nm in Regex.Matches(ln, @"\b\d+\b"))
                    tokens.Add(new TokenRef { Index = nm.Index + offset, Value = nm.Value });
                foreach (Match sm in Regex.Matches(ln, "\"[^\"]*\""))
                    tokens.Add(new TokenRef { Index = sm.Index + offset, Value = sm.Value });
                offset += ln.Length + 1;
            }
            return tokens;
        }

        /// <summary>Fallback blank family for programs with NO flat literal
        // (beginner input echo: `name = input(...)\nprint(name)`): a printed
        /// variable identifier. Each wrong variant is still execution-proved by
        /// ForgeBlank's substitution loop.</summary>
        static List<TokenRef> BlankTokensNames(string cleanCode)
        {
            var tokens = new List<TokenRef>();
            var banned = new HashSet<string> { "print", "input", "range", "int", "str", "len", "if", "elif", "else", "for", "while", "in", "and", "or", "not" };
            int offset = 0;
            foreach (var ln in cleanCode.Split('\n'))
            {
                if (ln.Contains("input(") || ln.Contains("# Bug")) { offset += ln.Length + 1; continue; }
                foreach (Match nm in Regex.Matches(ln, @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
                    if (!banned.Contains(nm.Value))
                        tokens.Add(new TokenRef { Index = nm.Index + offset, Value = nm.Value });
                offset += ln.Length + 1;
            }
            return tokens;
        }

        /// <summary>Last frame-anchored, bug-blankable code line: members of the
        /// executed trace that are NOT construct headers. Falls back to the
        /// last code line when the trace carries no line numbers (legacy
        /// behavior preserved).</summary>
        static HashSet<int> TraceLineSet(ExecResult run)
        {
            var set = new HashSet<int>();
            if (run != null && run.Trace != null)
                foreach (var t in run.Trace) if (t.SourceLine >= 0) set.Add(t.SourceLine);
            return set;
        }

        static int LastExecutedDerivableLine(string[] lines, HashSet<int> traced)
        {
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string t = lines[i].Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                if (t.StartsWith("if ") || t.StartsWith("elif ") || t.StartsWith("else")
                    || t.StartsWith("for ") || t.StartsWith("while ")) continue;
                if (traced.Count == 0 || traced.Contains(i)) return i;
            }
            return lines.Length - 1;
        }

        // ——— forging primitives ———

        /// <summary>Names a program defines or reads (the variable-swap family draws
        /// from this set: a swap only counts as a bug if the target name exists).</summary>
        static HashSet<string> DefinedNames(string code)
        {
            var defined = new HashSet<string>();
            var read = new HashSet<string>();
            PythonAst ast; string err;
            if (PythonAstGateway.TryParse(code, out ast, out err))
                SubsetChecker.CollectAssignRead(ast, defined, read);
            var set = new HashSet<string>(defined);
            // call names (input/print/int/...) are NOT variables — a swap that
            // substitutes them produces a nonsense variant family. Filtered here
            // before members of the pool are used as swap candidates.
            foreach (var u in read) set.Add(u);
            foreach (var w in KcGrammar.Names) set.Add(w);   // charter name pool
            foreach (var bad in new[] { "print", "input", "range", "int", "str", "len", "if", "elif", "else", "for", "while", "in", "and", "or", "not", "True", "False", "None" }) set.Remove(bad);
            return set;
        }

        /// <summary>Textual candidate fixes/bugs for one source line: operator swap,
        /// off-by-one on a literal, variable swap. Order is stable (deterministic seeds).</summary>
        static IEnumerable<string> LineVariants(string line, HashSet<string> names)
        {
            var seen = new HashSet<string>();
            string trimmed = line;
            if (trimmed.Trim().Length == 0) yield break;

            // operator swaps (only on spacing-delimited operators, never on '#{...}')
            foreach (var op in new[] { " + ", " - ", " * " })
            {
                int at = trimmed.IndexOf(op, StringComparison.Ordinal);
                if (at < 0) continue;
                string repl = op == " + " ? " - " : op == " - " ? " + " : " / ";
                string cand = trimmed.Substring(0, at) + repl + trimmed.Substring(at + op.Length);
                if (seen.Add(cand)) yield return cand;
            }

            // off-by-one (+1/-1, then ±2) on each integer literal
            foreach (Match m in Regex.Matches(trimmed, @"\b\d+\b"))
            {
                long v; if (!long.TryParse(m.Value, out v)) continue;
                foreach (var delta in new[] { 1L, -1L, 2L, -2L })
                {
                    if (v + delta < 0) continue;
                    string cand = trimmed.Substring(0, m.Index) + (v + delta) + trimmed.Substring(m.Index + m.Length);
                    if (seen.Add(cand)) yield return cand;
                }
            }

            // string-literal swap (skipping input-prompt lines): substituting a
            // charter string from KcGrammar.Strings/Msgs verifies as a real bug
            if (!trimmed.Contains("input("))
                foreach (Match sm in Regex.Matches(trimmed, "\"[^\"]*\""))
                {
                    foreach (var pool in new[] { KcGrammar.Strings, KcGrammar.Msgs })
                        foreach (var s in pool)
                        {
                            string cand = trimmed.Replace(sm.Value, "\"" + s + "\"");
                            if (cand != trimmed && seen.Add(cand)) yield return cand;
                        }
                }

            // variable swap: every name occurrence -> every other defined name
            foreach (Match nm in Regex.Matches(trimmed, @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
            {
                string w = nm.Value;
                if (w == "print" || w == "input" || w == "range" || w == "int" || w == "len"
                    || w == "if" || w == "else" || w == "for" || w == "in" || w == "while" || w == "not")
                    continue;
                foreach (var other in names)
                {
                    if (other == w) continue;
                    string cand = Regex.Replace(trimmed, @"\b" + w + @"\b", other);
                    if (cand != trimmed && seen.Add(cand)) yield return cand;
                }
            }
            yield break;
        }

        static IEnumerable<string> TokenVariants(string token)
        {
            long v;
            if (long.TryParse(token, out v))
            {
                foreach (var delta in new[] { 1L, -1L, 2L, -2L }) if (v + delta >= 0) yield return (v + delta).ToString();
                foreach (var p in KcGrammar.Ints) yield return p.ToString();
                yield break;
            }
            bool quoted = token.StartsWith("\"");
            string bare = quoted ? token.Trim('"') : token;
            foreach (var s in KcGrammar.Strings) yield return "\"" + s + "\"";
            foreach (var s in KcGrammar.Msgs) yield return "\"" + s + "\"";
            foreach (var n in KcGrammar.Names) if (n != bare) yield return n;
            if (quoted) yield return bare; else yield return "\"" + bare + "\"";
            yield break;
        }

        static string TextualBugKind(string a, string b)
        {
            if (Regex.IsMatch(a, @"\b\d+\b") && Regex.IsMatch(b, @"\b\d+\b")) return "off-by-one";
            var ops = new[] { "+", "-", "*" };
            foreach (var o in ops) if (Count(a, o) != Count(b, o)) return "op-misuse";
            foreach (var n in KcGrammar.Names) if (a != b && a.Contains(n) != b.Contains(n)) return "wrong-variable";
            return "value-drift";
        }

        static int Count(string s, string sub)
        {
            int c = 0, i = 0;
            while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { c++; i += sub.Length; }
            return c;
        }

        static int LineIndexOf(string code, int charIndex)
        {
            int line = 0;
            for (int i = 0; i < charIndex && i < code.Length; i++) if (code[i] == '\n') line++;
            return line;
        }

        static ExecResult ExecuteQuiet(string code, List<string> inputs)
        {
            PythonAst ast; string err;
            if (!PythonAstGateway.TryParse(code, out ast, out err)) return null;
            var run = new SubsetInterpreter(inputs).Execute(ast, code, SubsetInterpreter.MaxTraceLines);
            return run.Success ? run : null;
        }

        static string TraceKey(ExecResult r) { return r == null ? "" : string.Join("|", r.Trace.Select(t => t.Text).ToArray()); }
        static int LineCount(string code) { return code == null ? 0 : code.Split('\n').Count(l => l.Trim().Length > 0); }
        static string OneLine(string s) { return (s ?? "").Replace("\n", " ;; "); }
        static string Short(string s) { if (string.IsNullOrEmpty(s)) return ""; return s.Length > 60 ? s.Substring(0, 60) : s; }
        static string RuleKey(string violation)
        {
            var parts = (violation ?? "").Split(':');
            if (parts.Length >= 2) return parts[0] + ": " + parts[1].Trim().Split(' ').First();
            return parts[0];
        }

        // ============ legacy dataclass (mirror of PuzzleTemplate, UnityEngine-free) ============

        public class PuzzleResult
        {
            public PuzzleContextMetadata Metadata;
            public LegacyPuzzleTemplate Template;
        }

        /// <summary>Fields the 6 format handlers read from PuzzleTemplate. PCGEngine copies
        /// these verbatim into a real PuzzleTemplate behind `useGenerativePath`.</summary>
        static LegacyPuzzleTemplate BuildTemplate(PuzzleContextMetadata meta, PuzzleTypeCode type, ProgramDraft draft)
        {
            if (meta == null || meta.Trace.Count == 0) return null;
            var tpl = new LegacyPuzzleTemplate
            {
                id = "gen_" + meta.Kc + "_t" + meta.Tier + "_" + Guid.NewGuid().ToString("N").Substring(0, 8),
                knowledgeComponent = meta.Kc,
                difficulty = meta.Tier,
                codeLines = meta.Code.Split('\n').ToList(),
                correctAnswer = meta.CorrectAnswer,
                bugLineIndex = meta.CodeChangedLine >= 0 ? meta.CodeChangedLine : -1,
                goalText = string.Join(" | ", meta.GoalFacts.ToArray()),
                acceptedOrders = meta.AcceptedOrders.Count > 0 ? meta.AcceptedOrders : null
            };

            // distractors: the proven-wrong option texts (PAC consumes them verbatim;
            // PTO/TF/FITB/STB re-forge type-side from the same fields we key here)
            tpl.distractors = meta.Options.Where(o => !o.Correct).Select(o => o.Text).ToList();

            // variableName/variableValue feed PTO's name-echo family (must differ from the answer)
            string vn = meta.FinalVars.Keys.OrderBy(k => k, StringComparer.Ordinal)
                          .FirstOrDefault(k => meta.FinalVars[k] != meta.CorrectAnswer);
            if (vn != null)
            {
                tpl.variableName = vn;
                tpl.variableValue = meta.FinalVars[vn];
            }
            return tpl;
        }
    }

    // ——— puzzle-type codes mirrored from the Unity enum (UnityEngine-free) ———
    public enum PuzzleTypeCode
    {
        FillInTheBlank = 0,
        SpotTheBug = 1,
        LineScramble = 2,
        TrueOrFalse = 3,
        PredictTheOutput = 4,
        PairACode = 5
    }

    /// <summary>UnityEngine-free mirror of PuzzleTemplate; AstPuzzleService builds this and
    /// PCGEngine copies its fields into a real PuzzleTemplate (one-const rollback site).</summary>
    [Serializable]
    public class LegacyPuzzleTemplate
    {
        public string id;
        public string knowledgeComponent;
        public int difficulty;
        public List<string> codeLines;
        public string correctAnswer;
        public int bugLineIndex = -1;
        public List<int> correctOrder;
        public List<string> distractors;
        public string variableName;
        public string variableValue;
        public string goalText;
        public List<string> acceptedOrders;
    }
}