// PyQuest — Phase C/D glue: PuzzleContextBridge.cs + DistractorExplanation (09 §8)
// Target: Assets/Scripts/PCG/Ast/PuzzleContextBridge.cs
//
// Two tiny static holders that complete the Phase C → D wiring without touching
// any serialized surface:
//
//   1. PuzzleTemplate.context  (added to PuzzleTemplate.cs as [NonSerialized])
//      is THE transport: PCGEngine attaches the serve's PuzzleContextMetadata
//      before PuzzleFormatFactory.CreatePuzzleFormat runs, so every format
//      handler reads machine-derived facts straight from the template it was
//      Initialized with. Null on the legacy path → formats keep today's
//      behavior verbatim.
//
//   2. DistractorExplanation is the serve→encounter why-wrong pipe (09 §8):
//      format classes call ReportWrong(kind, context) on a wrong pick;
//      EncounterManager RoundResolution's wrong branch reads Take() into its
//      dedicated errorText element. Cleared at round start / on a correct
//      answer. UnityEngine-free; EncounterManager/null-checks live game-side.
//
// All sentences below are misconception-kind restatements of the machine
// verified difference — never authored per-puzzle prose (anti-misinformation
// rule, 09 §2.5).
using System;
using System.Collections.Generic;
using System.Linq;

namespace PyQuest.Pcg.Ast
{
    /// <summary>Serve→encounter feedback pipe + controlled static state.</summary>
    public static class PuzzleFeedback
    {
        /// <summary>The kind string of the LAST wrong pick (misconception kind from
        /// the context's Options), or null when unknown/legacy. Consumed by
        /// EncounterManager through DistractorExplanation.FreshSentence().</summary>
        public static string PickedWrongKind;

        /// <summary>The PuzzleContextMetadata of the puzzle whose wrong answer is
        /// being reported (09 §8): formats stamp it from template.context right
        /// alongside ReportWrongOption; EncounterManager reads it back through
        /// DistractorExplanation.FreshSentence. Null on the legacy path.</summary>
        public static PuzzleContextMetadata ServedContext;

        /// <summary>Called by format classes on a wrong answer — one line each.</summary>
        public static void ReportWrongOption(string pickedKind)
        {
            PickedWrongKind = pickedKind;
        }

        public static void Clear()
        {
            PickedWrongKind = null;
            ServedContext = null;
        }
    }

    public static class DistractorExplanation
    {
        /// <summary>Misconception-kind → why-wrong sentence (09 §8 sample set).
        /// One place per controller rule: reviewable const text only.</summary>
        static readonly Dictionary<string, string> Sentences = new Dictionary<string, string>
        {
            { "PtoDistractorOffset",   "Off by one on the numbers — recompute the final value from the top." },
            { "PtoDistractorOffBy",    "That value never reaches the print — trace how each line changes it." },
            { "PtoDistractorQuoteToggle", "Quoting makes it text: x is the value, 'x' is literally x." },
            { "PtoDistractorNameEcho", "Printing the variable's name prints the name, not what's inside it." },
            { "TfVerdict",             "Compare the two runs line by line — would any output actually change?" },
            { "StbFixWrong",           "That fix still changes the output — the correct fix restores the exact original behavior." },
            { "PacWrongLine",          "That line makes the program print something different — trace what it would produce." },
            { "FitbWrongToken",        "That token makes the code fail to parse or change the output." },
            { "swap-args",             "Right pieces in the wrong order — print shows arguments left to right." },
            { "off-by-one",            "Off by one: check where the range / comparison actually stops." },
            { "wrong-variable",        "That line uses a different variable — check which name feeds the answer." },
            { "op-misuse",             "The operator changed the math — recompute with each operator in play." },
            { "value-drift",           "The literal drifted — reassign from the original value and re-run." },
            { "correct",               "Trace each line's effect on the output, then compare it to your choice." },
        };

        /// <summary>The sentence for the picked kind; falls back to the context's
        /// expected-output fact, then to the generic defensive line (legacy
        /// instances without metadata land here too).</summary>
        public static string FreshSentence(PuzzleContextMetadata context)
        {
            string kind = PuzzleFeedback.PickedWrongKind;
            string sentence;
            if (kind != null && Sentences.TryGetValue(kind, out sentence))
                return sentence;

            if (context != null && context.ErrorFacts.Count > 0)
                return "Try again: " + string.Join(", ", context.ErrorFacts.ToArray());

            return Sentences["correct"];
        }

        // ——— 12: goal-display block (player-facing, debug-fact-free) ———
        // The old debug line read "kc=for_loop | tier=2 | input=...". The
        // player-facing block instead is:
        // Compact layout (2026-10 directive):
        //     #[Goal: "<const sentence>"]
        //     #[Inputted code was: "<preset>"]  <- only when the puzzle has an input
        //     <code>                            <- raw, no `__` fence, no blank lines
        //     #[Output: "<lines>"]             <- only when the serve has the fact
        // Facts like kc=/tier=/format=/var= NEVER leak into prose; only the
        // input preset is shown verbatim, because it IS player-relevant.

        /// <summary>The input preset from an "input=Prompt|Preset[int|str]" goal
        /// fact, or null when the puzzle has no simulated input.</summary>
        public static string InputValueFor(PuzzleContextMetadata ctx)
        {
            if (ctx == null || ctx.GoalFacts == null) return null;
            foreach (string f in ctx.GoalFacts)
            {
                if (f == null || !f.StartsWith("input=")) continue;
                string body = f.Substring(6);
                // fact anatomy: input=Prompt|Preset[int|str] — display the
                // VALUE (Preset); the prompt text stays engine-side.
                string[] parts = body.Split('|');
                return parts.Length >= 2 ? parts[1] : parts[0];
            }
            return null;
        }

        /// <summary>Player-facing goal block: the [Goal: "..."] line plus the
        /// #[Inputted code was: "..."] line only when the puzzle carries an
        /// input fact. goalSentence is a const, format-side wording.</summary>
        public static string GoalBlock(PuzzleContextMetadata ctx, string goalSentence)
        {
            if (string.IsNullOrEmpty(goalSentence)) return null;
            string block = "#[Goal: \"" + goalSentence + "\"]";
            string input = InputValueFor(ctx);
            if (input != null)
                block += "\n#[Inputted code was: \"" + input + "\"]";
            return block;
        }


        /// <summary>Full player-facing display: goal block header + one blank
        /// line + the RAW code (2026-10 compact: no blank lines, no `__`
        /// fence), then a footer fact line such as #[Output: "..."] when
        /// footer is not null. Header null (legacy path) returns null so
        /// legacy callers keep their original text verbatim — the assembly
        /// only ever decorates the goal-block era.</summary>
        public static string AssembleGoalDisplay(string header, string code)
        {
            return AssembleGoalDisplay(header, code, null);
        }

        public static string AssembleGoalDisplay(string header, string code, string footer)
        {
            if (string.IsNullOrEmpty(header)) return null;
            if (string.IsNullOrEmpty(code))
                return string.IsNullOrEmpty(footer) ? header : header + "\n" + footer;
            string body = header + "\n" + code;
            return string.IsNullOrEmpty(footer) ? body : body + "\n" + footer;
        }

        // ——— 13: output fact line ([Output: "..."]) ———
        // One fact from the serve enters the header: the trace-verified
        // output key (GoalFacts "expectedOutput=<a|b|c>"). The | separators
        // are trace joins; on display they become the printed lines.

        /// <summary>The raw expected-output key from the goal facts, or null.</summary>
        public static string OutputFactFor(PuzzleContextMetadata ctx)
        {
            if (ctx == null || ctx.GoalFacts == null) return null;
            foreach (string f in ctx.GoalFacts)
                if (f != null && f.StartsWith("expectedOutput="))
                    return f.Substring(15);
            return null;
        }

        /// <summary>The output key as printed lines (| join → newline).</summary>
        public static string OutputDisplay(string outputKey)
        {
            return string.IsNullOrEmpty(outputKey) ? "" : outputKey.Replace("|", "\n");
        }

        /// <summary>A [Output: "..."] footer line, or null when the puzzle
        /// carries no expected-output fact (legacy path stays bare).</summary>
        public static string OutputLine(PuzzleContextMetadata ctx, string label)
        {
            string key = OutputFactFor(ctx);
            return key == null ? null : "#[" + label + ": \"" + OutputDisplay(key) + "\"]";
        }

        /// <summary>13: parse + execute a snippet with the taught-subset
        /// interpreter and return its printed-output key ("a|b|..."), "" when
        /// it ran but printed nothing, null when parse or run failed. The
        /// format-side equivalence grader compares two of these keys; never
        /// throws (Unity's format files never touch IronPython types).</summary>
        public static string RunTraceKey(string code, List<string> inputs)
        {
            if (string.IsNullOrEmpty(code)) return null;
            try
            {
                IronPython.Compiler.Ast.PythonAst ast; string err;
                if (!PythonAstGateway.TryParse(code, out ast, out err)) return null;
                var run = new SubsetInterpreter(inputs)
                    .Execute(ast, code, SubsetInterpreter.MaxTraceLines);
                if (!run.Success) return null;
                return string.Join("|", run.Trace.Select(t => t.Text).ToArray());
            }
            catch (Exception) { return null; }
        }

        /// <summary>Header WITHOUT any code display and WITHOUT the input
        /// line: #[Goal: "..."] plus — when an expected-output fact exists —
        /// a #[OutputLabel: "..."] line directly under it (13: SpotTheBug's
        /// two-line instruction contract; 2026-10 compact prefixes).</summary>
        public static string HeaderWithOutput(PuzzleContextMetadata ctx, string goalSentence, string outputLabel)
        {
            if (string.IsNullOrEmpty(goalSentence)) return null;
            string block = "#[Goal: \"" + goalSentence + "\"]";
            string footer = OutputLine(ctx, outputLabel);
            return footer == null ? block : block + "\n" + footer;
        }

        /// <summary>The shared `__`-fenced code panel so all six formats frame
        /// snippets identically (12: goal block, blank line, then this).</summary>
        public static string CodeFrame(string code)
        {
            if (string.IsNullOrEmpty(code)) return "__\n__";
            return "__\n" + code + "\n__";
        }

        /// <summary>Builds a short machine-fact header line for context-UI rendering
        /// (09 §7: UI renders sentences ONLY from these facts).</summary>
        public static string GoalHeader(PuzzleContextMetadata context, string template)
        {
            if (context == null || context.GoalFacts.Count == 0) return null;
            return string.Format(template,
                string.Join(", ", context.GoalFacts.Take(4).ToArray()));
        }
    }
}
