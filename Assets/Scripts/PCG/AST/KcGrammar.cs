// PyQuest — Phase A: KcGrammar.cs
// Target: Assets/Scripts/PCG/Ast/KcGrammar.cs
//
// Per-KC generative grammar. THE D1 FIX: construct-KC programs are generated in
// a FIXED structured order —
//     [scaffold assignment the construct provably references] ->
//     [the focal construct] ->
//     [top-level print, only when the construct does not already print]
// — so every rule is then applied as a VERIFIER (named RULE violations in the
// console via SubsetChecker), and a trial is aborted only on a verifier failure,
// never on shape. No draw-and-reject line soup.
//
// Pools are the field-verified pools from dropin/IronPythonSmokeTestV9.cs
// (probe parity, per the handoff contract). UnityEngine-free; fully seeded.
using System;
using System.Collections.Generic;
using System.Linq;

namespace PyQuest.Pcg.Ast
{
    public class PuzzleRequest
    {
        public string Kc;      // print | variables | operations | input | conditionals | loops
        public int Tier;       // 0/1/2
        public string Format;  // PTO | TF | FITB | PAC | STB | LS
        public int Seed;
        public List<string> InputPresets; // optional: caller-forced presets (D4)
    }

    public class ProgramDraft
    {
        public string Code;
        public string Kc;
        public int Tier;
        public string Format;
        public List<string> Inputs = new List<string>();   // presets consumed in order (D4)
        public List<string> Prompts = new List<string>();  // prompts recorded at draw time
        public int InputShape = -1;                        // 0 string input, 1 int(input(...))
        public string RuleLog;                             // named verifier diagnostic trail, null when clean
    }

    public static class KcGrammar
    {
        // ——— charter pools (V9 probe parity) ———
        public static readonly string[] Names = { "Ula", "Bryn", "Oro", "Wren", "Echo", "Fang", "Nova", "hp", "score", "gold", "mana",
            "xp", "lives", "coins", "power", "ammo", "energy", "water", "wood", "iron", "stone", "gems", "keys", "bonus", "tally", "food", "pace" };
        // lowercase taught var-name pool (2026-10-01): builders draw each program's
        // variable names from here (DrawName) so puzzles no longer show hp/gold every
        // time. TryRename also sources real-word replacements from this pool.
        public static readonly string[] VarNames = { "hp", "score", "gold", "mana", "xp", "lives", "coins", "power", "ammo", "energy",
            "water", "wood", "iron", "stone", "gems", "keys", "bonus", "tally", "food", "pace" };
        // lowercase taught STRING-variable names (assigned a quoted string value).
        public static readonly string[] StringVars = { "name", "hero", "ally", "enemy", "pet", "title", "guild", "banner" };
        public static readonly string[] Strings = { "Hello", "Hi", "Greetings", "Welcome", "hero",
            "Hero", "Ranger", "Mage", "Rogue", "Cleric", "Paladin", "Druid", "Bard", "Smith",
            "Archer", "Scout", "Healer", "Knight", "Tamer", "Sailor", "Hunter", "Scribe", "Merchant" };
        public static readonly string[] Msgs = { "Found", "Gained", "Lost", "You have",
            "Quest opened", "Chest unlocked", "Trap dodged", "Scroll found", "Potion brewed",
            "Spell learned", "Door opened", "Key acquired", "Loot collected", "Level up",
            "Star earned", "Wood gathered", "Ore mined", "Fish caught", "Bread baked",
            "Fire lit", "Moon high", "Dragon seen", "Rune carved", "Camp set",
            "Path clear", "Bridge fixed", "Storm passed", "Shield raised", "Arrow strung" };
        public static readonly string[] InputPromptString = { "input your name: ", "type is your name: ", "input your favorite color: ",
            "type your class: ", "name your companion: ", "input a creature: ", "type your guild: ", "name your pet: ",
            "input your banner word: ", "type a password word: " };
        public static readonly string[] InputPromptNumber = { "input your number here: ", "type your age: ",
            "input your level: ", "type your gold count: ", "input your potion count: ", "type your star count: " };
        // 2026-10-01: capped to 1..10 (user: big-value chains produced 100k+ outputs —
        // more math than code). Worst multiply chain is now 10*10*10 = 1000.
        public static readonly long[] Ints = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        public static readonly char[] Ops = { '+', '-', '*' };

        static readonly string[] TrueWords = { "Rich", "Big", "High", "Pass", "Win", "Strike", "Swift", "Ready", "Ahead", "Alive", "Boost", "Charged" };
        static readonly string[] FalseWords = { "Poor", "Small", "Low", "Fail", "Miss", "Dip", "Slow", "Empty", "Behind", "Down", "Drain", "Blocked" };
        static readonly string[] LoopVarNames = { "i", "n", "t" };

        public static string Pick<T>(Random rng, IList<T> pool) { return pool[rng.Next(pool.Count)].ToString(); }
        public static T PickV<T>(Random rng, IList<T> pool) { return pool[rng.Next(pool.Count)]; }

        /// <summary>Draw a never-used variable name from the taught pool and record it in
        /// <paramref name="used"/>. Pool is 20+ wide, programs use at most 3 names, so the
        /// guard is a formality; a same-name draw is impossible unless the pool exhausts.</summary>
        public static string DrawName(Random rng, HashSet<string> used, string[] pool)
        {
            string s;
            int guard = 0;
            do { s = pool[rng.Next(pool.Length)]; } while (used.Contains(s) && guard++ < 64);
            used.Add(s);
            return s;
        }
        public static int MaxPrintArgsFor(int tier) { return tier <= 0 ? 1 : tier == 1 ? 2 : 4; }

        // ——— generation entry: fixed-order construction (D1) ———
        public static ProgramDraft BuildProgram(PuzzleRequest req)
        {
            var rng = new Random(req.Seed);
            var draft = new ProgramDraft
            {
                Kc = req.Kc,
                Tier = req.Tier,
                Format = req.Format,
                Inputs = req.InputPresets != null ? new List<string>(req.InputPresets) : new List<string>()
            };
            var lines = new List<string>();
            string shapeKind = null;

            switch (req.Tier)
            {
                case 0: shapeKind = "beginner"; break;
                case 1: shapeKind = "intermediate"; break;
                default: shapeKind = "advanced"; break;
            }

            try
            {
                switch (req.Kc)
                {
                    case "print": BuildPrint(req, rng, lines, draft); break;
                    case "variables": BuildVariables(req, rng, lines, draft); break;
                    case "operations": BuildOperations(req, rng, lines, draft); break;
                    case "input": BuildInput(req, rng, lines, draft); break;
                    case "conditionals": BuildConditionals(req, rng, lines, draft); break;
                    case "loops": BuildLoops(req, rng, lines, draft); break;
                    default: throw new NotSupportedException("unknown kc " + req.Kc);
                }
            }
            catch (Exception e)
            {
                draft.RuleLog = "GRAMMAR: " + shapeKind + " kc=" + req.Kc + " failed: " + e.Message;
                return draft;
            }
            draft.Code = string.Join("\n", lines);
            return draft;
        }

        // ============ BUILDERS (fixed order per D1) ============

        // ---- print — prints only, no binops on defined vars ----
        // 2026-10-01 variety pass: each tier draws among MULTIPLE taught-shape variants
        // (fixed shapes, never draw-and-reject). All variants stay inside the rules:
        // beginner = flat independent statements (no vars, 1 arg max), intermediate+ =
        // dataflow chain then print(s) referencing EVERY defined var (use-policy),
        // comma-args within MaxPrintArgsFor(tier).
        static void BuildPrint(PuzzleRequest req, Random rng, List<string> lines, ProgramDraft draft)
        {
            if (req.Tier <= 0)
            {
                // beginner: three flat-shape variants (independent statements, no vars)
                int shape = rng.Next(3);
                string m1 = Pick(rng, Msgs);
                if (shape == 0)
                {
                    lines.Add("print(\"" + m1 + "\")");
                    return;
                }
                // two independent prints — always distinct flat strings
                string m2 = PickDistinct(rng, Msgs, m1);
                if (shape == 1)
                {
                    lines.Add("print(\"" + m1 + "\")");
                    lines.Add("print(\"" + m2 + "\")");
                }
                else
                {
                    // labeled + bare flat string from the taught Strings pool
                    lines.Add("print(\"" + m1 + "\")");
                    lines.Add("print(\"" + Pick(rng, Strings) + "\")");
                }
                return;
            }
            // intermediate+: dataflow chain then print referencing the defined var.
            // Var names are DRAWN per program from the taught VarNames pool (diversity).
            var used = new HashSet<string>();
            string var = DrawName(rng, used, VarNames);
            lines.Add(var + " = " + PickV(rng, Ints));
            if (req.Tier >= 2)
            {
                // both defined vars are printed (use-policy); advanced draws among
                // one combined label print or two per-var label prints (taught comma-args)
                string var2 = DrawName(rng, used, VarNames);
                lines.Add(var2 + " = " + PickV(rng, Ints));
                if (rng.Next(2) == 0)
                {
                    lines.Add("print(\"" + Pick(rng, Msgs) + "\", " + var + ", " + var2 + ")");
                }
                else
                {
                    string m1 = Pick(rng, Msgs);
                    string m2 = PickDistinct(rng, Msgs, m1);
                    lines.Add("print(\"" + m1 + "\", " + var + ")");
                    lines.Add("print(\"" + m2 + "\", " + var2 + ")");
                }
            }
            else
            {
                // intermediate: labeled print, bare print, OR a two-var bare pair print
                // (2 args = MaxPrintArgsFor(1)); keeps the same chain-then-print policy.
                int shape = rng.Next(3);
                if (shape == 0)
                    lines.Add("print(\"" + Pick(rng, Msgs) + "\", " + var + ")");
                else if (shape == 1)
                    lines.Add("print(" + var + ")");
                else
                {
                    string var2 = DrawName(rng, used, VarNames);
                    lines.Add(var2 + " = " + PickV(rng, Ints));
                    lines.Add("print(" + var + ", " + var2 + ")");
                }
            }
        }

        /// <summary>Drawn pool item guaranteed different from <paramref name="not"/>.
        /// Used so multi-print shapes never repeat the same phrase back-to-back.</summary>
        static string PickDistinct(Random rng, string[] pool, string not)
        {
            string s = Pick(rng, pool);
            while (s == not) s = Pick(rng, pool);
            return s;
        }

        // ---- variables — assignment/reassignment only; NO binops (Vars Vault isolation) ----
        static void BuildVariables(PuzzleRequest req, Random rng, List<string> lines, ProgramDraft draft)
        {
            var used = new HashSet<string>();
            string var = DrawName(rng, used, VarNames);
            lines.Add(var + " = " + PickV(rng, Ints));
            if (req.Tier >= 1)
            {
                // reassignment (focal construct at intermediate+): a different literal
                lines.Add(var + " = " + PickV(rng, Ints));
            }
            if (req.Tier >= 2)
            {
                // advanced: draw between the int+string pair print and an int pair with
                // reassignments on both (still assignment/reassignment ONLY, no binops)
                if (rng.Next(2) == 0)
                {
                    string svar2 = DrawName(rng, used, StringVars);
                    lines.Add(svar2 + " = \"" + Pick(rng, Strings) + "\"");
                    lines.Add("print(" + var + ", " + svar2 + ")");
                }
                else
                {
                    // int pair, both reassigned once (var already got its reassign from
                    // the tier>=1 block), then printed as a bare pair
                    string ivar2 = DrawName(rng, used, VarNames);
                    lines.Add(ivar2 + " = " + PickV(rng, Ints));
                    lines.Add("print(" + var + ", " + ivar2 + ")");
                }
                return;
            }
            lines.Add("print(" + var + ")");
        }

        // ---- operations — binop focal (on literals beginner; on vars I+) ----
        static void BuildOperations(PuzzleRequest req, Random rng, List<string> lines, ProgramDraft draft)
        {
            char op = PickV(rng, Ops);
            long a = PickV(rng, Ints), b = PickV(rng, Ints);
            if (op == '-' && b > a) { long t = a; a = b; b = t; } // stay teachable: non-negative ints
            if (req.Tier <= 0)
            {
                // beginner: binop on literals
                lines.Add("print(" + a + " " + op + " " + b + ")");
                return;
            }
            // var names raised from hardcoded total/score/rank to drawn distinct names
            var passed = new HashSet<string>();
            string n1 = DrawName(rng, passed, VarNames);
            string n2 = DrawName(rng, passed, VarNames);
            string n3 = DrawName(rng, passed, VarNames);
            lines.Add(n1 + " = " + a + " " + op + " " + b);
            if (req.Tier >= 1)
            {
                char op2 = PickV(rng, Ops);
                long c = PickV(rng, Ints);
                lines.Add(n2 + " = " + n1 + " " + op2 + " " + c);
            }
            if (req.Tier >= 2)
            {
                // advanced: parentheses-precedence chain -> chained reassignment -> label print.
                // THREE statements by construction, so the advanced tier floor (never a
                // one-liner, 70% >= 3 lines) holds deterministically instead of on a coin flip.
                lines[0] = n1 + " = (" + a + " " + op + " " + b + ") " + PickV(rng, Ops) + " " + PickV(rng, Ints);
                lines.Add(n3 + " = " + n2 + " + 1");
                lines.Add("print(\"" + Pick(rng, Msgs) + "\", " + n2 + ", " + n3 + ")");
                return;
            }
            // intermediate: labeled or bare final print (2-arg label = teachable comma-args)
            if (rng.Next(3) != 0)
                lines.Add("print(" + n2 + ")");
            else
                lines.Add("print(\"" + Pick(rng, Msgs) + "\", " + n2 + ")");
        }

        // ---- input — input() handling D4 + no conditionals ever ----
        static void BuildInput(PuzzleRequest req, Random rng, List<string> lines, ProgramDraft draft)
        {
            // D4/§3.1: int(input(...)) is an INTERMEDIATE+ exception with numeric presets only.
            // The shape is decided ONCE here so prompt family, preset family and the
            // int(...) wrapper are drawn together and code/presets/goalText agree.
            draft.InputShape = (req.Tier >= 1 && (req.InputPresets == null || req.InputPresets.All(IsNumericString)) && rng.NextDouble() < 0.5) ? 1 : 0;
            if (draft.InputShape == 1)
            {
                // int(input(...)) — intermediate+ only, numeric preset required (§3.1 exception)
                draft.InputShape = 1;
                string prompt = Pick(rng, InputPromptNumber);
                string var = DrawName(rng, new HashSet<string>(), VarNames);
                lines.Add(var + " = int(input(\"" + prompt.Trim() + "\"))");
                draft.Prompts.Add(prompt);
                draft.Inputs.Add(PickV(rng, Ints).ToString()); // numeric preset by construction
                if (req.Tier >= 2)
                {
                    // advanced: echo + chain arithmetic (still no conditionals)
                    string nameVar = DrawName(rng, new HashSet<string> { var }, StringVars);
                    lines.Add(nameVar + " = \"" + Pick(rng, Strings) + "\"");
                    lines.Add("print(\"Player\", " + nameVar + ", " + var + ")");
                }
                else if (rng.Next(2) == 0) // intermediate: labeled or bare echo
                    lines.Add("print(\"" + Pick(rng, Msgs) + "\", " + var + ")");
                else
                    lines.Add("print(" + var + ")");
                return;
            }
            string sprompt = Pick(rng, InputPromptString);
            draft.InputShape = 0;
            string svar = DrawName(rng, new HashSet<string>(), StringVars);
            lines.Add(svar + " = input(\"" + sprompt.Trim() + "\")");
            draft.Prompts.Add(sprompt);
            draft.Inputs.Add(Pick(rng, Strings));
            if (req.Tier >= 1)
            {
                // intermediate+: string comparison/echo chain — conditionals stay banned
                lines.Add("print(\"Player\", " + svar + ")");
            }
            else lines.Add("print(" + svar + ")");
        }

        static bool IsNumericString(string s) { long _v; return long.TryParse(s, out _v); }

        /// <summary>D4: caller-forced presets must satisfy the same numeric-only rule the
        /// grammar draws by construction (§3.1: int(input()) presets are always numbers).</summary>
        public static bool PresetsNumeric(IList<string> presets, bool requireNumeric)
        {
            if (presets == null) return !requireNumeric;
            if (requireNumeric && presets.Count == 0) return false;
            return presets.All(p => !requireNumeric || IsNumericString(p));
        }

        // ---- conditionals — scaffold -> focal if/elif/else -> (nothing more) ----
        static void BuildConditionals(PuzzleRequest req, Random rng, List<string> lines, ProgramDraft draft)
        {
            string var = DrawName(rng, new HashSet<string>(), VarNames);
            long target = PickV(rng, Ints);
            lines.Add(var + " = " + target); // 1. scaffold the construct provably references

            if (req.Tier <= 0)
            {
                // beginner: inline if (print lives inside the construct — trace ≥1 by the body)
                lines.Add("if " + var + " > " + (target - 1) + ": print(\"" + Pick(rng, TrueWords) + "\")");
                return;
            }
            if (req.Tier == 1)
            {
                // intermediate: if/else with disjoint body strings; body draw between bare
                // word prints and 2-arg labeled echoes (print(word, var)) for shape variety
                bool labeled = rng.Next(2) == 0;
                string tw = Pick(rng, TrueWords), fw = PickDistinct(rng, FalseWords, tw);
                lines.Add("if " + var + " > " + target + ":");
                lines.Add("    print(" + (labeled ? "\"" + tw + "\", " + var : "\"" + tw + "\"") + ")");
                lines.Add("else:");
                lines.Add("    print(" + (labeled ? "\"" + fw + "\", " + var : "\"" + fw + "\"") + ")");
                return;
            }
            // advanced: if/elif/else chain, thresholds DESCENDING by construction (D2 coherence)
            string a = Pick(rng, TrueWords), b = Pick(rng, FalseWords);
            string c = a == b ? "Done" : (Pick(rng, TrueWords) == a ? "Done" : Pick(rng, Msgs));
            if (c == a || c == b) c = "Done";
            long t1 = PickV(rng, Ints);
            long t2 = t1 >= 3 ? t1 - 2 : 0;
            lines.Add("if " + var + " > " + t1 + ":");
            lines.Add("    print(\"" + a + "\")");
            lines.Add("elif " + var + " > " + t2 + ":");
            lines.Add("    print(\"" + b + "\")");
            lines.Add("else:");
            lines.Add("    print(\"" + c + "\")");
        }

        // ---- loops — scaffold -> focal loop (bounded trace ≤ budget) ----
        static void BuildLoops(PuzzleRequest req, Random rng, List<string> lines, ProgramDraft draft)
        {
            string loopVar = Pick(rng, LoopVarNames);
            if (req.Tier <= 0)
            {
                // beginner budget = 1 trace line: bounded while (prints once when done)
                lines.Add("k = 0");
                lines.Add("while k < 2:");
                lines.Add("    k = k + 1");
                lines.Add("print(k)");
                return;
            }
            if (req.Tier == 1)
            {
                // intermediate: accumulation loop, 1 trace line by construction
                lines.Add("total = 0");
                lines.Add("for " + loopVar + " in range(3):");
                lines.Add("    total = total + " + loopVar);
                lines.Add("print(total)");
                return;
            }
            // advanced: ALWAYS nested (tier-2 floor) while staying in budget — inner accumulation
            lines.Add("total = 0");
            lines.Add("for " + loopVar + " in range(2):");
            lines.Add("    for j in range(3):");
            lines.Add("        total = total + j");
            lines.Add("print(total)");
        }
    }
}