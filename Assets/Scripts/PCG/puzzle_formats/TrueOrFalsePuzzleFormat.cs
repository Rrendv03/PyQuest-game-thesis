using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// True/False puzzle format implementation.
/// Presents a code snippet and asks the player to determine if it's correct (True) or contains a bug (False).
/// </summary>
public class TrueOrFalsePuzzleFormat : IPuzzleFormat
{
    public void RenderPuzzle(FillInTheBlankUIController uiController) { }

    public void RenderPuzzle(PredictTheOutputUIController uiController) { }

    public PuzzleType FormatType => PuzzleType.TrueOrFalse;

    private PuzzleTemplate template;
    private string displayedCode;
    private bool correctAnswer;

    public void Initialize(PuzzleTemplate template)
    {
        this.template = template;
        GenerateCodeSnippet();
    }

    public void RenderPuzzle(Text displayField)
    {
        if (displayField == null)
        {
            Debug.LogError("[TrueOrFalsePuzzleFormat] Display field is null");
            return;
        }

        // Format the code snippet for display with a question prompt
        string prompt = "Is the following code snippet correct?\n\n";
        string codeBlock = string.Join("\n", template.codeLines);

        displayField.text = prompt + "```\n" + codeBlock + "\n```\n\n[True] [False]";
    }

    public bool EvaluateAnswer(object playerAnswer)
    {
        if (playerAnswer is bool boolAnswer)
        {
            // True = correct code, False = buggy code
            return boolAnswer == correctAnswer;
        }

        Debug.LogError("[TrueOrFalsePuzzleFormat] Invalid answer type. Expected bool.");
        return false;
    }

    public object GetCorrectAnswer()
    {
        return correctAnswer;
    }

    /// <summary>
    /// Generates the code snippet by determining if it should be correct or contain a bug.
    /// This method mutates the template and injects bugs if needed.
    /// </summary>
    private void GenerateCodeSnippet()
    {
        // Always mutate the template first (swap variable names/values)
        template = PCGEngine.Instance.MutatePuzzlePublic(template);

        // 50/50 chance: code is correct (True) or contains a bug (False)
        correctAnswer = Random.Range(0, 2) == 0;

        if (!correctAnswer)
        {
            // Inject a semantic logical bug into the code
            MutateCodeWithBug();
        }

        Debug.Log($"[TrueOrFalsePuzzleFormat] Puzzle correctAnswer set to: {correctAnswer}, Code:\n{string.Join("\n", template.codeLines)}");
    }

    /// <summary>
    /// Mutates the code snippet to introduce a logical bug.
    /// </summary>
    private void MutateCodeWithBug()
    {
        // Strategy 1: Break variable references (only if variable appears in multiple places)
        if (!string.IsNullOrEmpty(template.variableName))
        {
            int varCount = 0;
            for (int i = 0; i < template.codeLines.Count; i++)
            {
                varCount += template.codeLines[i].Split(new string[] { template.variableName }, System.StringSplitOptions.None).Length - 1;
            }

            // Only misspell if variable appears 2+ times (assignment + usage)
            if (varCount >= 2)
            {
                for (int i = 0; i < template.codeLines.Count; i++)
                {
                    if (template.codeLines[i].Contains(template.variableName) && !template.codeLines[i].TrimStart().StartsWith(template.variableName + " ="))
                    {
                        // Only misspell the usage, not the assignment
                        string wrongName = template.variableName + "a";
                        template.codeLines[i] = template.codeLines[i].Replace(template.variableName, wrongName);
                        Debug.Log($"[TrueOrFalsePuzzleFormat] Injected bug: misspelled variable {template.variableName} -> {wrongName}");
                        return;
                    }
                }
            }
        }

        // Strategy 2: Flip comparison operators
        for (int i = 0; i < template.codeLines.Count; i++)
        {
            string line = template.codeLines[i];

            if (line.Contains("=="))
            {
                template.codeLines[i] = line.Replace("==", "!=");
                Debug.Log($"[TrueOrFalsePuzzleFormat] Injected bug: == to !=");
                return;
            }
            if (line.Contains(">="))
            {
                template.codeLines[i] = line.Replace(">=", "<");
                Debug.Log($"[TrueOrFalsePuzzleFormat] Injected bug: >= to <");
                return;
            }
            if (line.Contains("<="))
            {
                template.codeLines[i] = line.Replace("<=", ">");
                Debug.Log($"[TrueOrFalsePuzzleFormat] Injected bug: <= to >");
                return;
            }
            if (line.Contains(" > ") && !line.Contains(">="))
            {
                template.codeLines[i] = line.Replace(" > ", " < ");
                Debug.Log($"[TrueOrFalsePuzzleFormat] Injected bug: > to <");
                return;
            }
            if (line.Contains(" < ") && !line.Contains("<="))
            {
                template.codeLines[i] = line.Replace(" < ", " > ");
                Debug.Log($"[TrueOrFalsePuzzleFormat] Injected bug: < to >");
                return;
            }
        }

        // Strategy 3: Break arithmetic operators
        for (int i = 0; i < template.codeLines.Count; i++)
        {
            string line = template.codeLines[i];

            if (line.Contains("+"))
            {
                template.codeLines[i] = line.Replace("+", "-");
                Debug.Log($"[TrueOrFalsePuzzleFormat] Injected bug: + to -");
                return;
            }
            if (line.Contains("-") && !line.Contains("->") && !line.Contains("!="))
            {
                template.codeLines[i] = line.Replace("-", "+");
                Debug.Log($"[TrueOrFalsePuzzleFormat] Injected bug: - to +");
                return;
            }
            if (line.Contains("*"))
            {
                template.codeLines[i] = line.Replace("*", "/");
                Debug.Log($"[TrueOrFalsePuzzleFormat] Injected bug: * to /");
                return;
            }
        }

        // Strategy 4: Introduce syntax error in print statements
        for (int i = 0; i < template.codeLines.Count; i++)
        {
            if (template.codeLines[i].Contains("print("))
            {
                template.codeLines[i] = template.codeLines[i].Replace(")", "");
                Debug.Log($"[TrueOrFalsePuzzleFormat] Injected bug: removed closing paren from print");
                return;
            }
        }

        // Strategy 5: Change string literals to break output
        for (int i = 0; i < template.codeLines.Count; i++)
        {
            if (template.codeLines[i].Contains("'") || template.codeLines[i].Contains("\""))
            {
                template.codeLines[i] = template.codeLines[i].Replace("'Hello'", "'Goodbye'")
                                                            .Replace("'World'", "'Universe'")
                                                            .Replace("\"Hello\"", "\"Goodbye\"")
                                                            .Replace("\"World\"", "\"Universe\"");
                Debug.Log($"[TrueOrFalsePuzzleFormat] Injected bug: changed string literal");
                return;
            }
        }

        // Fallback: comment out a key line
        for (int i = template.codeLines.Count - 1; i >= 0; i--)
        {
            if (!template.codeLines[i].TrimStart().StartsWith("#"))
            {
                template.codeLines[i] = "# " + template.codeLines[i];
                Debug.Log($"[TrueOrFalsePuzzleFormat] Injected bug: commented out line {i}");
                return;
            }
        }
    }

    public void RenderPuzzle(PairACodeUIController uiController) { }
}