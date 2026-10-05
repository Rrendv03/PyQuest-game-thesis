// PyQuest — Phase B/C dataclass: PuzzleContextMetadata.cs
// Target: Assets/Scripts/PCG/Ast/PuzzleContextMetadata.cs
//
// Machine-derived puzzle context — the contract between the generator and the
// puzzle-format UI controllers (anti-misinformation rule: UI renders facts,
// never authored/generator-written sentences; goalText is assembled by UI code
// from these facts).
// UnityEngine-free.
using System;
using System.Collections.Generic;

namespace PyQuest.Pcg.Ast
{
    public class PuzzleContextMetadata
    {
        // ——— the served program + execution ———
        public string Code;
        public string Kc;            // print | variables | operations | input | conditionals | loops
        public int Tier;             // 0/1/2
        public string Format;        // PTO | TF | FITB | PAC | STB | LS
        public List<TraceFact> Trace = new List<TraceFact>();     // the executed output (canonical answer source)
        public Dictionary<string, string> FinalVars = new Dictionary<string, string>();

        // ——— D4: input() is first-class ———
        public List<InputLine> Inputs = new List<InputLine>();    // ordered: one per input() call
        public class InputLine { public string Prompt; public string Preset; public bool Numeric; }

        // ——— goal facts (UI renders goalText from these) ———
        public List<string> GoalFacts = new List<string>();       // deterministic fact strings, e.g. "var defines: hp = 5"
        // ——— error facts (rendered into errorText on wrong answer) ———
        public List<string> ErrorFacts = new List<string>();

        // ——— options / answer handling ———
        public string CorrectAnswer;                // the single correct answer (trace-derived)
        public int CodeChangedLine = -1;            // STB/FITB/PAC: 0-based index of the line the puzzle touches
        public string BugKind;                      // STB: proven bug classification (TextualBugKind)
        public List<OptionFact> Options = new List<OptionFact>(); // 3–4 forged options incl. the correct one
        public List<string> AcceptedOrders = new List<string>();  // LS/STB: accepted permutations (trace-graded)

        public class TraceFact { public string Text; public int SourceLine; public bool InsideConstruct; }
        public class OptionFact { public string Text; public string MisconceptionKind; public bool Correct; }

        /// <summary>Trace-equality grading predicate (never text-vs-text).</summary>
        public bool GradesCorrect(string submitted)
        {
            var canon = CorrectAnswer;
            if (string.Equals(submitted, canon, System.StringComparison.Ordinal)) return true;
            // accepted-order variants (whitespace-normalized equality)
            foreach (var a in AcceptedOrders)
                if (Normalize(submitted) == Normalize(a)) return true;
            return false;
        }

        static string Normalize(string s)
        {
            var pts = s.Split(' ');
            var nonEmpty = new List<string>();
            foreach (var p in pts) if (p.Length > 0) nonEmpty.Add(p);
            nonEmpty.Sort(System.StringComparer.Ordinal);
            return string.Join(" ", nonEmpty.ToArray());
        }
    }
}