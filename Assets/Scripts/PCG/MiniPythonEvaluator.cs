using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Deliberately narrow simulator for the beginner Python subset PCGEngine's
/// mutations can produce: sequential "var = expr" assignments and
/// "print(expr)" calls, where expr is a flat (no parentheses) chain of
/// +, -, * over integer literals/variables, or + over string literals/
/// variables.
///
/// Anything outside that (if/elif/else/for/while/def, input(), comparisons,
/// function calls other than print()) makes it bail out with false rather
/// than guess, so PCGEngine can fall back to its old narrow correctAnswer
/// sync instead of trusting a wrong derived value.
///
/// Algorithm verified against ~15 hand-built cases (independent inits,
/// self-referencing updates, string concatenation, operator precedence,
/// multi-print sequences) via a Python port before being written here.
/// Still needs a real Unity/C# compile-and-play check on actual templates;
/// this file has not been run inside Unity.
/// </summary>
public static class MiniPythonEvaluator
{
    private static readonly string[] unsupportedMarkers =
    {
        "if ", "elif ", "else", "for ", "while ", "def ", "input(",
        "==", "!=", ">=", "<=", " > ", " < ", "(", ")"
    };
    // Note: "(" and ")" are excluded wholesale except for print(...), which
    // is special-cased below. That means any function call other than
    // print() (len(), int(), str(), range() used outside a for-header, etc.)
    // makes this bail out too. That's intentional: better to defer to the
    // old correctAnswer sync than silently mis-evaluate.

    public static bool TrySimulate(List<string> codeLines, out string finalOutput)
    {
        finalOutput = null;
        var env = new Dictionary<string, string>();
        var printed = new List<string>();

        foreach (string raw in codeLines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            Match printMatch = Regex.Match(line, @"^print\((.*)\)$");
            if (printMatch.Success)
            {
                if (!TryEvaluateExpression(printMatch.Groups[1].Value.Trim(), env, out string val))
                    return false;
                printed.Add(val);
                continue;
            }

            foreach (string marker in unsupportedMarkers)
                if (line.Contains(marker)) return false;

            Match assignMatch = Regex.Match(line, @"^(\w+)\s*=(?!=)\s*(.+)$");
            if (!assignMatch.Success) return false;

            string varName = assignMatch.Groups[1].Value;
            string expr = assignMatch.Groups[2].Value.Trim();
            if (!TryEvaluateExpression(expr, env, out string value)) return false;
            env[varName] = value;
        }

        if (printed.Count == 0) return false;
        finalOutput = string.Join("\n", printed);
        return true;
    }

    private static bool TryEvaluateExpression(string expr, Dictionary<string, string> env, out string result)
    {
        result = null;
        expr = expr.Trim();

        if (expr.StartsWith("'") || expr.StartsWith("\"") || IsStringExpression(expr, env))
        {
            List<string> parts = SplitTopLevel(expr, '+');
            var sb = new StringBuilder();
            foreach (string part in parts)
            {
                if (!TryResolveStringToken(part.Trim(), env, out string val)) return false;
                sb.Append(val);
            }
            result = sb.ToString();
            return true;
        }

        if (!TryEvalArithmetic(expr, env, out int intResult)) return false;
        result = intResult.ToString();
        return true;
    }

    // IMPORTANT: this checks the FIRST token only, with no minimum part count.
    // A single bare variable reference (e.g. print(name), no '+' involved)
    // must still be recognized as a string expression when that variable
    // holds a non-numeric value -- an earlier draft required at least 2
    // '+'-split parts here and silently failed on exactly this case.
    private static bool IsStringExpression(string expr, Dictionary<string, string> env)
    {
        List<string> parts = SplitTopLevel(expr, '+');
        string first = parts[0].Trim();
        if (first.StartsWith("'") || first.StartsWith("\"")) return true;
        if (env.TryGetValue(first, out string val) && !int.TryParse(val, out _)) return true;
        return false;
    }

    private static bool TryResolveStringToken(string token, Dictionary<string, string> env, out string val)
    {
        if ((token.StartsWith("'") && token.EndsWith("'") && token.Length >= 2)
         || (token.StartsWith("\"") && token.EndsWith("\"") && token.Length >= 2))
        {
            val = token.Substring(1, token.Length - 2);
            return true;
        }
        return env.TryGetValue(token, out val);
    }

    // Splits on '+' / '-' at the top level (no parens in this grammar), then
    // splits each term on '*', so standard precedence (* before +/-) holds
    // for flat, unparenthesized expressions.
    private static bool TryEvalArithmetic(string expr, Dictionary<string, string> env, out int result)
    {
        result = 0;
        string compact = expr.Replace(" ", "");
        if (compact.Length == 0) return false;

        MatchCollection terms = Regex.Matches(compact, @"[+-]?[^+-]+");
        if (terms.Count == 0) return false;

        int total = 0;
        foreach (Match term in terms)
        {
            string t = term.Value;
            int sign = 1;
            if (t.StartsWith("-")) { sign = -1; t = t.Substring(1); }
            else if (t.StartsWith("+")) { t = t.Substring(1); }
            if (t.Length == 0) return false;

            string[] factors = t.Split('*');
            int product = 1;
            foreach (string f in factors)
            {
                if (!TryResolveInt(f, env, out int val)) return false;
                product *= val;
            }
            total += sign * product;
        }
        result = total;
        return true;
    }

    private static bool TryResolveInt(string token, Dictionary<string, string> env, out int val)
    {
        token = token.Trim();
        if (int.TryParse(token, out val)) return true;
        if (env.TryGetValue(token, out string stored) && int.TryParse(stored, out val)) return true;
        val = 0;
        return false;
    }

    private static List<string> SplitTopLevel(string expr, char delimiter)
    {
        // No parentheses in this restricted grammar, and quoted content in
        // this dataset never contains the delimiter, so a plain split is
        // safe here.
        return new List<string>(expr.Split(delimiter));
    }
}