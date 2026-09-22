using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Deliberately narrow simulator for the beginner Python subset PCGEngine's
/// mutations can produce: sequential "var = expr" assignments and
/// "print(expr)" calls, where expr is a flat (no parentheses) chain of
/// +, -, * over integer literals/variables, or + over string literals/
/// variables, or a comma-separated print() argument list.
///
/// Tracks whether each variable was assigned a STRING (quoted literal) or
/// NUMERIC value, not just its text. Without this, x = '5' (a string) and
/// x = 5 (an int) were indistinguishable once stored, so x + 2 would
/// silently compute a number even though real Python raises TypeError for
/// str + int. That's the difference between "safe to skip" and "silently
/// wrong": this evaluator now bails out (returns false) on exactly the
/// cases that would actually error in real Python, rather than guessing.
///
/// Anything outside this grammar (if/elif/else/for/while/def, input(),
/// comparisons, function calls other than print()) also makes it bail
/// out, so callers can fall back to trusting the authored correctAnswer
/// instead of trusting a wrong derived one.
///
/// CHANGES (variety engine): the execution loop now lives in TrySimulateCore,
/// which also exposes the per-variable string/numeric typing environment.
/// TrySimulate keeps its exact previous signature and behavior;
/// PuzzleVariationEngine uses TrySimulateDetailed to decide whether a
/// skeleton's last assignment is numerically extendable before deriving
/// higher-difficulty variants from it.
/// </summary>
public static class MiniPythonEvaluator
{
    private static readonly string[] unsupportedMarkers =
    {
        "if ", "elif ", "else", "for ", "while ", "def ", "input(",
        "==", "!=", ">=", "<=", " > ", " < ", "(", ")"
    };

    public static bool TrySimulate(List<string> codeLines, out string finalOutput)
    {
        List<string> printed;
        Dictionary<string, string> env;
        Dictionary<string, bool> envIsString;

        bool ok = TrySimulateCore(codeLines, true, out printed, out env, out envIsString);
        finalOutput = ok ? string.Join("\n", printed.ToArray()) : null;
        return ok;
    }

    /// <summary>
    /// Same simulation, but also reports the string/numeric typing of every
    /// variable after the run. Used by PuzzleVariationEngine.CanScale so
    /// difficulty scaling never appends arithmetic to a string-typed tail
    /// (greeting = 'Hello' + small would be a TypeError, not a variant).
    /// </summary>
    public static bool TrySimulateDetailed(List<string> codeLines,
        out string finalOutput, out Dictionary<string, bool> envIsString)
    {
        List<string> printed;
        Dictionary<string, string> env;
        bool ok = TrySimulateCore(codeLines, false, out printed, out env,
            out envIsString);
        finalOutput = ok ? string.Join("\n", printed.ToArray()) : null;
        return ok;
    }

    private static bool TrySimulateCore(List<string> codeLines, bool requirePrint,
        out List<string> printed, out Dictionary<string, string> env,
        out Dictionary<string, bool> envIsStringOut)
    {
        printed = new List<string>();
        env = new Dictionary<string, string>();
        envIsStringOut = new Dictionary<string, bool>();
        var envIsString = new Dictionary<string, bool>();

        foreach (string raw in codeLines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            Match printMatch = Regex.Match(line, @"^print\((.*)\)$");
            if (printMatch.Success)
            {
                List<string> args = SplitTopLevelCommas(printMatch.Groups[1].Value.Trim());
                List<string> evaluatedArgs = new List<string>();
                foreach (string arg in args)
                {
                    if (!TryEvaluateExpression(arg.Trim(), env, envIsString, out string argVal, out _))
                        return false;
                    evaluatedArgs.Add(argVal);
                }
                printed.Add(string.Join(" ", evaluatedArgs));
                continue;
            }

            foreach (string marker in unsupportedMarkers)
                if (line.Contains(marker)) return false;

            Match assignMatch = Regex.Match(line, @"^(\w+)\s*=(?!=)\s*(.+)$");
            if (!assignMatch.Success) return false;

            string varName = assignMatch.Groups[1].Value;
            string expr = assignMatch.Groups[2].Value.Trim();
            if (!TryEvaluateExpression(expr, env, envIsString, out string value, out bool isString))
                return false;
            env[varName] = value;
            envIsString[varName] = isString;
        }

        envIsStringOut = envIsString;
        // Assignment-only snippets are still fully traced (PairACode tails
        // need the typing environment for difficulty scaling), but callers
        // that need OUTPUT (TrySimulate) bail out on them, exactly like the
        // original implementation did.
        if (requirePrint && printed.Count == 0) return false;
        return true;
    }

    private static bool TryEvaluateExpression(string expr, Dictionary<string, string> env,
        Dictionary<string, bool> envIsString, out string result, out bool isStringResult)
    {
        result = null;
        isStringResult = false;
        expr = expr.Trim();

        if (expr.StartsWith("'") || expr.StartsWith("\"") || IsStringExpression(expr, env, envIsString))
        {
            List<string> parts = SplitTopLevel(expr, '+');
            var sb = new StringBuilder();
            foreach (string part in parts)
            {
                if (!TryResolveStringToken(part.Trim(), env, envIsString, out string val)) return false;
                sb.Append(val);
            }
            result = sb.ToString();
            isStringResult = true;
            return true;
        }

        if (!TryEvalArithmetic(expr, env, envIsString, out int intResult)) return false;
        result = intResult.ToString();
        isStringResult = false;
        return true;
    }

    // A single bare variable reference (e.g. print(name), no '+' involved)
    // is a string expression if that variable is KNOWN to be string-typed,
    // not merely if its stored text happens to parse as a number -- a
    // string variable holding "5" must NOT be treated as numeric just
    // because its digits look like an int.
    private static bool IsStringExpression(string expr, Dictionary<string, string> env,
        Dictionary<string, bool> envIsString)
    {
        List<string> parts = SplitTopLevel(expr, '+');
        string first = parts[0].Trim();
        if (first.StartsWith("'") || first.StartsWith("\"")) return true;
        if (envIsString.TryGetValue(first, out bool isStr)) return isStr;
        return false;
    }

    private static bool TryResolveStringToken(string token, Dictionary<string, string> env,
        Dictionary<string, bool> envIsString, out string val)
    {
        val = null;
        if ((token.StartsWith("'") && token.EndsWith("'") && token.Length >= 2)
         || (token.StartsWith("\"") && token.EndsWith("\"") && token.Length >= 2))
        {
            val = token.Substring(1, token.Length - 2);
            return true;
        }
        // A variable can only join a string concatenation if IT is also
        // string-typed. A numeric variable (or bare numeric literal, which
        // never reaches here since it's not a key in env) being pulled in
        // here is exactly the str + int TypeError case in real Python: it
        // must fail, not silently get stringified into the result.
        if (env.TryGetValue(token, out string stored) && envIsString.TryGetValue(token, out bool isStr) && isStr)
        {
            val = stored;
            return true;
        }
        return false;
    }

    private static bool TryEvalArithmetic(string expr, Dictionary<string, string> env,
        Dictionary<string, bool> envIsString, out int result)
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
                if (!TryResolveInt(f, env, envIsString, out int val)) return false;
                product *= val;
            }
            total += sign * product;
        }
        result = total;
        return true;
    }

    private static bool TryResolveInt(string token, Dictionary<string, string> env,
        Dictionary<string, bool> envIsString, out int val)
    {
        val = 0;
        token = token.Trim();

        // A variable KNOWN to be string-typed can never participate in
        // arithmetic, regardless of whether its stored text happens to
        // look numeric ('5' is not the same as 5). This is the fix for
        // the str + int TypeError case being silently mis-evaluated.
        if (envIsString.TryGetValue(token, out bool isStr) && isStr) return false;

        if (int.TryParse(token, out val)) return true;
        if (env.TryGetValue(token, out string stored) && int.TryParse(stored, out val)) return true;
        val = 0;
        return false;
    }

    private static List<string> SplitTopLevelCommas(string expr)
    {
        List<string> parts = new List<string>();
        bool inSingle = false, inDouble = false;
        int start = 0;
        for (int i = 0; i < expr.Length; i++)
        {
            char c = expr[i];
            if (c == '\'' && !inDouble) inSingle = !inSingle;
            else if (c == '"' && !inSingle) inDouble = !inDouble;
            else if (c == ',' && !inSingle && !inDouble)
            {
                parts.Add(expr.Substring(start, i - start));
                start = i + 1;
            }
        }
        parts.Add(expr.Substring(start));
        return parts;
    }

    private static List<string> SplitTopLevel(string expr, char delimiter)
    {
        return new List<string>(expr.Split(delimiter));
    }
}
