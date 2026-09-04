using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public class PCGEngine : MonoBehaviour
{
    public static PCGEngine Instance;

    private List<PuzzleTemplate> allTemplates = new List<PuzzleTemplate>();

    // Tracks recently-served template IDs per (component|type|tier) bucket so
    // the same 2-3 templates don't repeat back to back when a KC's pool is
    // small. Capped per-bucket in SelectWithHistory.
    private Dictionary<string, Queue<string>> recentlyUsed = new Dictionary<string, Queue<string>>();

    // A template tagged for one puzzleType is content, not a hard format
    // lock: SpotTheBug injects its own bug procedurally, LineScramble
    // derives its own dependency graph, PredictTheOutput can derive its
    // own answer via MiniPythonEvaluator, FillInTheBlank finds its own
    // blank target -- none of them actually NEED puzzleType-specific
    // hand-authored fields beyond codeLines (and optionally variableName).
    // This table says which formats a template's codeLines are
    // STRUCTURALLY usable for, regardless of what it was originally
    // tagged as, so GeneratePuzzle can widen a thin candidate pool with
    // real content instead of falling back to the wrong difficulty or
    // wrong puzzleType entirely (see GeneratePuzzle below).
    private static readonly Dictionary<PuzzleType, System.Func<PuzzleTemplate, bool>> formatEligibility =
        new Dictionary<PuzzleType, System.Func<PuzzleTemplate, bool>>
    {
        { PuzzleType.SpotTheBug, t => t.codeLines.Count >= 1 },
        { PuzzleType.TrueOrFalse, t => t.codeLines.Count >= 1 },
        { PuzzleType.PairACode, t => t.codeLines.Count >= 2 },
        { PuzzleType.LineScramble, t => t.codeLines.Count >= 3 },
        { PuzzleType.PredictTheOutput, t => t.codeLines.Exists(l => l.Contains("print(")) },
        { PuzzleType.FillInTheBlank, t => t.codeLines.Exists(l =>
            l.Contains("print") || l.Contains("if") || l.Contains("elif") || l.Contains("else")
            || l.Contains("for") || l.Contains("while") || l.Contains("input") || l.Contains("range"))
            || !string.IsNullOrEmpty(t.variableName) },
    };

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadTemplates();
        }
        else Destroy(gameObject);

    }

    void LoadTemplates()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "puzzle_templates.json");

        if (!File.Exists(path))
        {
            Debug.LogError("[PCG] puzzle_templates.json not found at: " + path);
            return;
        }

        string json = File.ReadAllText(path);
        PuzzleTemplateLibrary lib = JsonUtility.FromJson<PuzzleTemplateLibrary>(json);
        allTemplates = lib.templates;
        Debug.Log($"[PCG] Loaded {allTemplates.Count} puzzle templates");
    }

    /// <summary>
    /// Centralized puzzle generation method that works with all puzzle formats.
    /// This is the primary endpoint for generating puzzles.
    /// </summary>
    /// <param name="componentName">The knowledge component to generate a puzzle for</param>
    /// <returns>A fully initialized PuzzleData object with format handler, or null if generation fails</returns>
    // Two-parameter overload: exploration mode, uses live BKT mastery
    public PuzzleData GeneratePuzzle(string componentName, PuzzleType puzzleType)
    {
        float mastery = BKTEngine.Instance.GetMastery(componentName);
        DifficultyTier targetTier = GetTierForMastery(mastery);
        return GeneratePuzzle(componentName, puzzleType, targetTier);
    }

    // Three-parameter overload: encounter mode, uses locked tier
    public PuzzleData GeneratePuzzle(string componentName, PuzzleType puzzleType,
                                      DifficultyTier forcedTier)
    {
        List<PuzzleTemplate> candidates = allTemplates
            .Where(t => t.knowledgeComponent == componentName
                     && t.difficulty == forcedTier
                     && t.puzzleType == puzzleType)
            .ToList();

        // Cross-format widening: if the exact-tagged pool is thin, pull in
        // templates from OTHER puzzleTypes at the SAME (KC, difficulty)
        // that are structurally eligible for the requested format. This
        // keeps the requested difficulty intact (unlike the two fallbacks
        // below, which drop difficulty/puzzleType entirely), it just stops
        // requiring separately-authored content per format.
        bool crossFormatUsed = false;
        if (candidates.Count < 3 && formatEligibility.TryGetValue(puzzleType, out var isEligible))
        {
            List<PuzzleTemplate> crossFormat = allTemplates
                .Where(t => t.knowledgeComponent == componentName
                         && t.difficulty == forcedTier
                         && t.puzzleType != puzzleType
                         && isEligible(t))
                .ToList();
            if (crossFormat.Count > 0)
            {
                candidates.AddRange(crossFormat);
                crossFormatUsed = true;
            }
        }

        if (candidates.Count == 0)
            candidates = allTemplates
                .Where(t => t.knowledgeComponent == componentName
                         && t.puzzleType == puzzleType)
                .ToList();

        if (candidates.Count == 0)
            candidates = allTemplates
                .Where(t => t.knowledgeComponent == componentName)
                .ToList();

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"[PCG] No templates for {componentName} | Type: {puzzleType} | Tier: {forcedTier}");
            return null;
        }

        string bucketKey = $"{componentName}|{puzzleType}|{forcedTier}";
        PuzzleTemplate selected = SelectWithHistory(candidates, bucketKey);
        PuzzleTemplate mutated = MutatePuzzlePublic(selected);

        // If this came from the cross-format pool, its puzzleType still
        // says whatever it was originally authored/tagged as. Force it to
        // the REQUESTED type so PuzzleFormatFactory builds the right
        // handler; the format classes themselves derive their answer key
        // from codeLines procedurally, so this is safe.
        if (crossFormatUsed && mutated.puzzleType != puzzleType)
        {
            Debug.Log($"[PCG] Cross-format: template {selected.id} (authored as " +
                      $"{selected.puzzleType}) rendered as {puzzleType}");
            mutated.puzzleType = puzzleType;
        }

        IPuzzleFormat formatHandler = PuzzleFormatFactory.CreatePuzzleFormat(mutated);
        if (formatHandler == null)
        {
            Debug.LogError($"[PCG] Failed to create format handler for: {mutated.puzzleType}");
            return null;
        }

        Debug.Log($"[PCG] Puzzle generated | Component: {componentName} | Type: {puzzleType} | Tier: {forcedTier}");
        return new PuzzleData(mutated, formatHandler);
    }

    /// <summary>
    /// Legacy method for generating templates only (without format handling).
    /// Use GeneratePuzzle() instead for the new centralized system.
    /// </summary>
    public PuzzleTemplate GeneratePuzzleTemplate(string componentName)
    {
        float mastery = BKTEngine.Instance.GetMastery(componentName);
        DifficultyTier targetTier = GetTierForMastery(mastery);

        List<PuzzleTemplate> candidates = allTemplates
            .Where(t => t.knowledgeComponent == componentName && t.difficulty == targetTier)
            .ToList();

        if (candidates.Count == 0)
            candidates = allTemplates.Where(t => t.knowledgeComponent == componentName).ToList();

        if (candidates.Count == 0) { Debug.LogWarning($"[PCG] No templates for {componentName}"); return null; }

        string bucketKey = $"{componentName}|legacy|{targetTier}";
        PuzzleTemplate selected = SelectWithHistory(candidates, bucketKey);
        return MutatePuzzlePublic(selected);
    }

    // New generation endpoint tailored specifically for the True or False mechanic
    public TrueFalseData GenerateTrueFalsePuzzle(string componentName)
    {
        float mastery = BKTEngine.Instance.GetMastery(componentName);
        DifficultyTier targetTier = GetTierForMastery(mastery);

        List<PuzzleTemplate> candidates = allTemplates
            .Where(t => t.knowledgeComponent == componentName && t.difficulty == targetTier)
            .ToList();

        if (candidates.Count == 0)
            candidates = allTemplates.Where(t => t.knowledgeComponent == componentName).ToList();

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"[PCG] No templates found for component: {componentName}");
            return null;
        }

        string bucketKey = $"{componentName}|truefalse|{targetTier}";
        PuzzleTemplate baseTemplate = SelectWithHistory(candidates, bucketKey);
        PuzzleTemplate mutatedTemplate = MutatePuzzlePublic(baseTemplate);

        // Core Procedural Mutation Rule for True/False Format:
        // System rolls a 50/50 chance to decide if the output code should be correct (True) or bugged (False)
        bool outputShouldBeTrue = Random.Range(0, 2) == 0;
        string finalCodeDisplay = string.Join("\n", mutatedTemplate.codeLines);

        if (!outputShouldBeTrue)
        {
            // Inject a semantic logical bug into the text stream to turn it False.
            // Only the FIRST occurrence is flipped (not every "+" or "==" in the
            // whole snippet) so the bug stays a single, findable change rather
            // than corrupting every matching operator at once.
            if (finalCodeDisplay.Contains("=="))
            {
                finalCodeDisplay = ReplaceFirst(finalCodeDisplay, "==", "!=");
            }
            else if (finalCodeDisplay.Contains(" + "))
            {
                finalCodeDisplay = ReplaceFirst(finalCodeDisplay, " + ", " - ");
            }
            else if (finalCodeDisplay.Contains(" < "))
            {
                finalCodeDisplay = ReplaceFirst(finalCodeDisplay, " < ", " > ");
            }
            else
            {
                finalCodeDisplay += "\n# Bug injected: logic trace mismatch";
            }
        }

        TrueFalseData puzzlePackage = new TrueFalseData();
        puzzlePackage.snippetText = finalCodeDisplay;
        puzzlePackage.isSnippetTrue = outputShouldBeTrue;

        return puzzlePackage;
    }

    private static string ReplaceFirst(string source, string search, string replacement)
    {
        int idx = source.IndexOf(search);
        if (idx < 0) return source;
        return source.Substring(0, idx) + replacement + source.Substring(idx + search.Length);
    }

    DifficultyTier GetTierForMastery(float mastery)
    {
        if (mastery < 0.50f) return DifficultyTier.Beginner;
        if (mastery < 0.75f) return DifficultyTier.Intermediate;
        return DifficultyTier.Advanced;
    }

    public DifficultyTier GetTierForMasteryPublic(float mastery)
    {
        return GetTierForMastery(mastery);
    }

    /// <summary>
    /// Picks a template from candidates, avoiding IDs used recently in the
    /// same bucket when possible. Falls back to the full candidate pool if
    /// avoiding recent picks would leave nothing to choose from (e.g. a
    /// bucket with only 1-2 templates).
    /// </summary>
    private PuzzleTemplate SelectWithHistory(List<PuzzleTemplate> candidates, string bucketKey)
    {
        if (candidates.Count == 1) return candidates[0];

        if (!recentlyUsed.TryGetValue(bucketKey, out Queue<string> recent))
        {
            recent = new Queue<string>();
            recentlyUsed[bucketKey] = recent;
        }

        List<PuzzleTemplate> fresh = candidates.Where(t => !recent.Contains(t.id)).ToList();
        List<PuzzleTemplate> pool = fresh.Count > 0 ? fresh : candidates;

        PuzzleTemplate picked = pool[Random.Range(0, pool.Count)];

        int historyCap = Mathf.Max(1, Mathf.Min(candidates.Count - 1, 4));
        recent.Enqueue(picked.id);
        while (recent.Count > historyCap)
            recent.Dequeue();

        return picked;
    }

    /// <summary>
    /// Returns a distractor that is guaranteed wrong, not a nuanced
    /// near-miss, by pulling an actual line from elsewhere in the SAME
    /// snippet (thematically related, since it's real code from this exact
    /// puzzle) rather than generating a plausible-looking variant of the
    /// correct answer. Useful as one option among otherwise-nuanced
    /// distractors (numeric near-values, operator swaps) so at least one
    /// choice can never be argued as "almost right."
    /// </summary>
    public string GenerateGuaranteedWrongOption(List<string> codeLines, string correctAnswer)
    {
        List<string> candidates = codeLines
            .Where(l => l.Trim() != (correctAnswer ?? "").Trim())
            .ToList();

        if (candidates.Count > 0)
            return candidates[Random.Range(0, candidates.Count)];

        // Degenerate case: snippet is a single line and it IS the answer.
        // Fall back to generic statements that are valid Python but make no
        // sense as a match for a specific line in context.
        string[] genericFallbacks = { "pass", "break", "continue", "return None" };
        return genericFallbacks[Random.Range(0, genericFallbacks.Length)];
    }

    public PuzzleTemplate MutatePuzzlePublic(PuzzleTemplate original)
    {
        PuzzleTemplate m = new PuzzleTemplate
        {
            id = original.id + "_mut_" + Random.Range(0, 10000),
            knowledgeComponent = original.knowledgeComponent,
            puzzleType = original.puzzleType,
            difficulty = original.difficulty,
            codeLines = new List<string>(original.codeLines),
            correctAnswer = original.correctAnswer,
            bugLineIndex = original.bugLineIndex,
            correctOrder = new List<int>(original.correctOrder),
            distractors = new List<string>(original.distractors),
            variableName = original.variableName,
            variableValue = original.variableValue
        };

        // Pools expanded (roughly 1.7-2x each) so back-to-back mutations of
        // the same template have more room to land on genuinely different
        // values instead of cycling through a small set.
        string[] nameVariantPool = new string[]
        {
            "mana", "health", "score", "level", "gold", "damage",
            "defense", "stamina", "magic", "runes", "power", "shield",
            "energy", "speed", "armor", "quest", "rank", "coins",
            "lives", "points", "strength", "agility", "wisdom", "luck",
            "vigor", "guard", "focus", "morale", "essence", "charge",
            "rating", "tally", "streak", "combo", "supply", "reserve",
            "endurance", "fortune", "resolve", "insight"
        };

        string[] intValuePool = new string[]
        {
            "5", "10", "15", "20", "25", "30", "50", "75",
            "100", "150", "200", "250", "500", "7", "13", "99",
            "3", "8", "12", "17", "22", "40", "60", "80",
            "120", "175", "300", "400", "9", "11", "45", "65"
        };

        string[] stringValuePool = new string[]
        {
            "'Hero'", "'Wizard'", "'Archer'", "'Knight'", "'Mage'",
            "'Dragon'", "'Quest'", "'Rogue'", "'Paladin'", "'Hunter'",
            "'Warrior'", "'Sage'", "'Scout'", "'Ranger'", "'Monk'",
            "'Druid'", "'Bard'", "'Cleric'", "'Alchemist'", "'Nomad'",
            "'Guardian'", "'Sentinel'", "'Wanderer'", "'Champion'", "'Seer'"
        };

        string[] greetingPool = new string[]
        {
            "'Hello'", "'Greetings'", "'Welcome'", "'Salutations'",
            "'Howdy'", "'Hey there'", "'Hi'", "'Good day'",
            "'Well met'", "'Ahoy'", "'Cheers'", "'Hail'"
        };

        string[] messagePool = new string[]
        {
            "'Game Over'", "'Level Up'", "'You Win'", "'Try Again'",
            "'Quest Complete'", "'Victory'", "'Defeat'", "'Well Done'",
            "'Keep Going'", "'Almost There'",
            "'New Record'", "'Boss Defeated'", "'Path Unlocked'",
            "'Sanctum Cleared'", "'Not Yet'"
        };

        string[] operatorPairs = new string[] { "+", "-", "*" };

        if (!string.IsNullOrEmpty(original.variableName))
        {
            // --- Strategy 1: variable name + value swap (single pass; the
            // old code ran this exact block twice in a row, which meant the
            // second pass's Replace() calls almost always found nothing left
            // to replace, while it still unconditionally overwrote
            // m.variableName/m.variableValue with a second, different random
            // pick -- so the stored variable name/value no longer matched
            // what was actually in codeLines). ---
            string newName = nameVariantPool[Random.Range(0, nameVariantPool.Length)];
            string newValue;
            int parsedInt;
            bool isNumeric = int.TryParse(original.variableValue, out parsedInt);
            if (isNumeric)
                newValue = intValuePool[Random.Range(0, intValuePool.Length)];
            else
                newValue = stringValuePool[Random.Range(0, stringValuePool.Length)].Replace("'", "");

            for (int i = 0; i < m.codeLines.Count; i++)
                m.codeLines[i] = m.codeLines[i]
                    .Replace(original.variableName, newName)
                    .Replace(original.variableValue, newValue);

            // FIX: distractors reference the same variable name/value the
            // codeLines just got renamed to, but were never mutated here
            // before, so they showed stale/unmutated text every single
            // time a template was drawn ("same wrong answers" regardless
            // of how many times the puzzle regenerated). Mirror the exact
            // same substitution onto them.
            for (int i = 0; i < m.distractors.Count; i++)
                m.distractors[i] = m.distractors[i]
                    .Replace(original.variableName, newName)
                    .Replace(original.variableValue, newValue);

            m.variableName = newName;
            m.variableValue = newValue;

            // --- Strategy 2 + 3: only allowed once MiniPythonEvaluator can
            // simulate the snippet AND its computed output for the
            // unmutated codeLines matches the authored correctAnswer. If
            // either check fails (conditionals/loops/input in the snippet,
            // or correctAnswer represents something the evaluator doesn't
            // understand like a formatted string), these two strategies are
            // skipped entirely for this mutation rather than risking a
            // correctAnswer that no longer matches the mutated code. ---
            bool baselineSimulated = MiniPythonEvaluator.TrySimulate(m.codeLines, out string baselineOutput);
            bool baselineTrustworthy = baselineSimulated && baselineOutput == original.correctAnswer;

            if (baselineTrustworthy)
            {
                List<string> candidateLines = new List<string>(m.codeLines);

                // Strategy 2: randomize a numeric literal
                for (int i = 0; i < candidateLines.Count; i++)
                {
                    string line = candidateLines[i];
                    foreach (string num in new string[] { "80", "18", "5", "10", "100" })
                    {
                        if (line.Contains(num) && !line.Contains(newValue))
                        {
                            candidateLines[i] = line.Replace(num,
                                intValuePool[Random.Range(0, intValuePool.Length)]);
                            break;
                        }
                    }
                }

                // Strategy 3: randomize an arithmetic operator
                for (int i = 0; i < candidateLines.Count; i++)
                {
                    string line = candidateLines[i];
                    if (line.Contains(" + ") || line.Contains(" - ") || line.Contains(" * "))
                    {
                        string op = operatorPairs[Random.Range(0, operatorPairs.Length)];
                        candidateLines[i] = System.Text.RegularExpressions.Regex
                            .Replace(line, @" [\+\-\*] ", $" {op} ");
                        break;
                    }
                }

                if (MiniPythonEvaluator.TrySimulate(candidateLines, out string newOutput))
                {
                    m.codeLines = candidateLines;
                    m.correctAnswer = newOutput;
                }
                // If re-simulation somehow fails after 2/3 (shouldn't happen
                // since only literals/operators the evaluator already
                // understood were touched), m.codeLines/correctAnswer stay
                // at the Strategy-1-only result computed above.
            }
            else if (m.correctAnswer == original.variableValue)
            {
                // Can't simulate (conditional/loop/input in the snippet, or
                // correctAnswer isn't derived from plain simulation). Keep
                // the narrow rename-only sync so the common case -- where
                // correctAnswer literally equals the mutated variable's
                // value -- still works.
                m.correctAnswer = newValue;
            }
        }
        else
        {
            // --- Strategy 4: String literal replacement ---
            bool mutated = false;
            for (int i = 0; i < m.codeLines.Count; i++)
            {
                string line = m.codeLines[i];

                if (line.Contains("'Hello'") || line.Contains("'World'"))
                {
                    m.codeLines[i] = line
                        .Replace("'Hello'", greetingPool[Random.Range(0, greetingPool.Length)])
                        .Replace("'World'", greetingPool[Random.Range(0, greetingPool.Length)]);
                    mutated = true;
                }
                else if (line.Contains("'Pass'") || line.Contains("'Fail'")
                      || line.Contains("'Yes'") || line.Contains("'No'"))
                {
                    m.codeLines[i] = line
                        .Replace("'Pass'", messagePool[Random.Range(0, messagePool.Length)])
                        .Replace("'Fail'", messagePool[Random.Range(0, messagePool.Length)])
                        .Replace("'Yes'", messagePool[Random.Range(0, messagePool.Length)])
                        .Replace("'No'", messagePool[Random.Range(0, messagePool.Length)]);
                    mutated = true;
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\b\d+\b"))
                {
                    // Strategy 5: Replace standalone numbers
                    m.codeLines[i] = System.Text.RegularExpressions.Regex.Replace(
                        line, @"\b\d+\b",
                        match => intValuePool[Random.Range(0, intValuePool.Length)]);
                    mutated = true;
                }
            }

            // Strategy 6: Inject a variable line before print if nothing mutated
            if (!mutated)
            {
                string[] injections = new string[]
                {
                    nameVariantPool[Random.Range(0, nameVariantPool.Length)]
                        + " = " + intValuePool[Random.Range(0, intValuePool.Length)],
                    nameVariantPool[Random.Range(0, nameVariantPool.Length)]
                        + " = " + stringValuePool[Random.Range(0, stringValuePool.Length)],
                };

                string injection = injections[Random.Range(0, injections.Length)];

                for (int i = 0; i < m.codeLines.Count; i++)
                {
                    if (m.codeLines[i].Contains("print("))
                    {
                        m.codeLines.Insert(i, injection);
                        // FIX: inserting a line shifts every line at or after
                        // index i down by one. bugLineIndex and correctOrder
                        // are indices INTO codeLines, so they need the same
                        // shift or they silently point at the wrong lines
                        // once something downstream actually reads them.
                        if (m.bugLineIndex >= i) m.bugLineIndex++;
                        for (int k = 0; k < m.correctOrder.Count; k++)
                            if (m.correctOrder[k] >= i) m.correctOrder[k]++;
                        break;
                    }
                }
            }
        }

        Debug.Log($"[PCG] Mutated: {m.id} | Type: {m.puzzleType} | Tier: {m.difficulty}");
        return m;
    }

    // Data carrier block declared outside the class boundary to safely pass package information to the UI canvas
    [System.Serializable]
    public class TrueFalseData
    {
        public string snippetText;
        public bool isSnippetTrue;
    }


}