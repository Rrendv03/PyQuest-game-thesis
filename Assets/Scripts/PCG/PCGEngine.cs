using System.Collections;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class PCGEngine : MonoBehaviour
{
    public static PCGEngine Instance;

    /// <summary>
    /// True once puzzle_templates.json has finished loading (success or
    /// failure). Loading is now asynchronous on Android (UnityWebRequest
    /// is the only API that can read StreamingAssets out of a compressed
    /// APK), so any caller that needs allTemplates immediately after
    /// scene load must wait on this first, same as BKTEngine.IsLoaded.
    /// </summary>
    public bool IsLoaded { get; private set; } = false;

    private List<PuzzleTemplate> allTemplates = new List<PuzzleTemplate>();

    private Dictionary<string, Queue<string>> recentlyUsed = new Dictionary<string, Queue<string>>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            StartCoroutine(LoadTemplatesRoutine());
        }
        else Destroy(gameObject);
    }

    private IEnumerator LoadTemplatesRoutine()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "puzzle_templates.json");
        string json = "";

#if UNITY_ANDROID && !UNITY_EDITOR
        using (UnityWebRequest req = UnityWebRequest.Get(path))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                json = req.downloadHandler.text;
            }
            else
            {
                Debug.LogError("[PCG] Failed to load puzzle_templates.json: " + req.error);
            }
        }
#else
        if (File.Exists(path))
        {
            json = File.ReadAllText(path);
        }
        else
        {
            Debug.LogError("[PCG] puzzle_templates.json not found at: " + path);
        }
        yield return null;
#endif

        if (!string.IsNullOrEmpty(json))
        {
            PuzzleTemplateLibrary lib = JsonUtility.FromJson<PuzzleTemplateLibrary>(json);
            var authored = lib != null && lib.templates != null ? lib.templates : new List<PuzzleTemplate>();
            // Slot expansion: every template carrying {name}/{num}/{msg}/...
            // tokens becomes VariantsPerSkeleton concrete instances, and
            // evaluator-safe skeletons also derive Intermediate/Advanced
            // variants. Hand-authored templates pass through untouched, so
            // the shipped JSON stays small while each (KC, format, tier)
            // bucket gets a deep, non-repeating pool.
            allTemplates = PuzzleVariationEngine.ExpandAll(authored);
            Debug.Log($"[PCG] Loaded {authored.Count} authored templates -> " +
                      $"{allTemplates.Count} playable instances");
        }

        IsLoaded = true;
    }

    public PuzzleData GeneratePuzzle(string componentName, PuzzleType puzzleType)
    {
        float mastery = BKTEngine.Instance.GetMastery(componentName);
        DifficultyTier targetTier = GetTierForMastery(mastery);
        return GeneratePuzzle(componentName, puzzleType, targetTier);
    }

    public PuzzleData GeneratePuzzle(string componentName, PuzzleType puzzleType,
                                      DifficultyTier forcedTier)
    {
        if (!IsLoaded)
            Debug.LogWarning("[PCG] GeneratePuzzle called before puzzle_templates.json finished " +
                              "loading. allTemplates may still be empty; this call will likely " +
                              "return null. Wait on PCGEngine.Instance.IsLoaded before entering " +
                              "gameplay.");

        // Cross-format widening was removed entirely. It solved thin
        // buckets by borrowing a template tagged for a different
        // puzzleType, but when a (KC, difficulty) combination only had
        // ONE template total across every format, borrowing just forced
        // that one template into every request at that combination
        // instead of adding real variety, and its fields (correctAnswer,
        // distractors, bugLineIndex) don't necessarily mean the same
        // thing across formats, which produced the "predict the output
        // shows a blank" / "spot the bug shows no bug and no correct
        // option" class of bugs. Straight fallback chain now: exact
        // match, then same format at any difficulty, then same KC at any
        // format/difficulty as a last resort.
        List<PuzzleTemplate> candidates = allTemplates
            .Where(t => t.knowledgeComponent == componentName
                     && t.difficulty == forcedTier
                     && t.puzzleType == puzzleType)
            .ToList();

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

        IPuzzleFormat formatHandler = PuzzleFormatFactory.CreatePuzzleFormat(mutated);
        if (formatHandler == null)
        {
            Debug.LogError($"[PCG] Failed to create format handler for: {mutated.puzzleType}");
            return null;
        }

        Debug.Log($"[PCG] Puzzle generated | Component: {componentName} | Type: {puzzleType} | Tier: {forcedTier}");
        return new PuzzleData(mutated, formatHandler);
    }

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

        bool outputShouldBeTrue = Random.Range(0, 2) == 0;
        string finalCodeDisplay = string.Join("\n", mutatedTemplate.codeLines);

        if (!outputShouldBeTrue)
        {
            if (finalCodeDisplay.Contains("=="))
                finalCodeDisplay = ReplaceFirst(finalCodeDisplay, "==", "!=");
            else if (finalCodeDisplay.Contains(" + "))
                finalCodeDisplay = ReplaceFirst(finalCodeDisplay, " + ", " - ");
            else if (finalCodeDisplay.Contains(" < "))
                finalCodeDisplay = ReplaceFirst(finalCodeDisplay, " < ", " > ");
            else
                finalCodeDisplay += "\n# Bug injected: logic trace mismatch";
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

    public string GenerateGuaranteedWrongOption(List<string> codeLines, string correctAnswer)
    {
        List<string> candidates = codeLines
            .Where(l => l.Trim() != (correctAnswer ?? "").Trim())
            .ToList();

        if (candidates.Count > 0)
            return candidates[Random.Range(0, candidates.Count)];

        string[] genericFallbacks = { "pass", "break", "continue", "return None" };
        return genericFallbacks[Random.Range(0, genericFallbacks.Length)];
    }

    /// <summary>
    /// CONFIRMED ROOT CAUSE of the "every word gets mutated" bug in later
    /// sanctums: naive string.Replace(variableName, newName) treats the
    /// variable name as a raw substring, not a whole word. Elif
    /// Labyrinth/Input Mists content frequently uses single-letter
    /// variable names (variableName="i" is used in 4 of your real loop
    /// templates), and "i" is a substring of "in", "if", "print",
    /// "input", and "while" -- every one of those keywords got partially
    /// overwritten on every mutation. "for i in range(5):" naive-replaced
    /// "i"->"mana" becomes "for mana manan range(5):", a guaranteed syntax
    /// error, every single time that template mutates. \b keeps
    /// replacement scoped to whole tokens only; the same "i" that starts
    /// "in" no longer matches because it isn't followed by a word
    /// boundary. Also fixes the equivalent numeric case (replacing "10"
    /// must not also corrupt "100").
    /// </summary>
    private static string ReplaceWholeWord(string text, string oldWord, string newWord)
    {
        if (string.IsNullOrEmpty(oldWord)) return text;
        return Regex.Replace(text, @"\b" + Regex.Escape(oldWord) + @"\b", newWord);
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
            variableValue = original.variableValue,
            goalText = original.goalText,
            // Dormant unless a template actually populates it (none do
            // right now by design, per the decision to hold multi-variable
            // content back until the single-variable path is confirmed
            // stable). Safe to leave wired in: an empty list here is a
            // complete no-op in the loop below.
            additionalVariables = original.additionalVariables != null
                ? original.additionalVariables.Select(v => new VariablePair { name = v.name, value = v.value }).ToList()
                : new List<VariablePair>()
        };

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
            string newName = nameVariantPool[Random.Range(0, nameVariantPool.Length)];
            string newValue;
            int parsedInt;
            bool isNumeric = int.TryParse(original.variableValue, out parsedInt);
            if (isNumeric)
                newValue = intValuePool[Random.Range(0, intValuePool.Length)];
            else
                newValue = stringValuePool[Random.Range(0, stringValuePool.Length)].Replace("'", "");

            // MUTATION SAFETY GATE: control-flow snippets (for/while/if and
            // input()) have answers authored for their LITERAL loop bounds
            // and branch values. Re-shuffling numbers in them produced the
            // "range(3) became range(500) while correctAnswer stayed 0\n1\n2"
            // class of wrong answers, so they only ever get NAME mutations.
            bool controlFlow = PuzzleVariationEngine.ContainsControlFlow(m.codeLines);

            for (int i = 0; i < m.codeLines.Count; i++)
                m.codeLines[i] = ReplaceWholeWord(m.codeLines[i], original.variableName, newName);

            for (int i = 0; i < m.distractors.Count; i++)
                m.distractors[i] = ReplaceWholeWord(m.distractors[i], original.variableName, newName);

            // goalText is the player-facing contract ("Add the two amounts
            // together and store the result in mana"), so it must undergo the
            // SAME rename as the code it describes. It used to be copied over
            // verbatim, which produced the playtested bug where the goal
            // named one variable ("store the result in mana") while the code
            // and every option used the renamed one ("lives = defense +
            // shield") -- an undecidable puzzle. Mirrors the code rename
            // below, including the control-flow carve-out for values.
            m.goalText = ReplaceWholeWord(m.goalText ?? "", original.variableName, newName);

            // correctAnswer must undergo the SAME rename as the code it
            // belongs to. SpotTheBug templates that author their bug
            // directly in codeLines (bugLineIndex >= 0) store the clean
            // line in correctAnswer; renaming only codeLines would leave
            // the "correct fix" pointing at the old variable while the
            // served snippet uses the new one -- the same undecidable
            // mismatch class as the goalText bug fixed above. No-op for
            // every other format: PredictTheOutput/FillInTheBlank
            // overwrite correctAnswer after this point, PairACode ignores
            // it, and TrueOrFalse's true/false contains no identifiers.
            m.correctAnswer = ReplaceWholeWord(m.correctAnswer ?? "", original.variableName, newName);

            if (!controlFlow)
            {
                for (int i = 0; i < m.codeLines.Count; i++)
                    m.codeLines[i] = ReplaceWholeWord(m.codeLines[i], original.variableValue, newValue);

                for (int i = 0; i < m.distractors.Count; i++)
                    m.distractors[i] = ReplaceWholeWord(m.distractors[i], original.variableValue, newValue);

                // Keep the goal's numbers in lockstep with the code's (the
                // same contract argument as the rename above): bo_pac_002's
                // goal quotes the discount as a bare number, and that number
                // IS variableValue, so leaving it behind after a value
                // mutation re-creates the goal/code mismatch class.
                m.goalText = ReplaceWholeWord(m.goalText ?? "", original.variableValue, newValue);

                // Same contract for values quoted inside an authored
                // SpotTheBug fix line (e.g. cond_stb_001's "if x > N:").
                m.correctAnswer = ReplaceWholeWord(m.correctAnswer ?? "", original.variableValue, newValue);
            }

            m.variableName = newName;
            m.variableValue = controlFlow ? original.variableValue : newValue;

            // Multi-variable renaming: dormant for existing content since
            // additionalVariables is empty unless a template sets it, but
            // wired in now so it's ready when you decide to author
            // multi-variable templates later, without another PCGEngine
            // change at that point.
            HashSet<string> usedNewNames = new HashSet<string> { newName };
            foreach (VariablePair extra in m.additionalVariables)
            {
                string oldExtraName = extra.name;
                string oldExtraValue = extra.value;
                if (string.IsNullOrEmpty(oldExtraName)) continue;

                string extraNewName = newName;
                for (int attempt = 0; attempt < 20 && usedNewNames.Contains(extraNewName); attempt++)
                    extraNewName = nameVariantPool[Random.Range(0, nameVariantPool.Length)];
                usedNewNames.Add(extraNewName);

                bool extraIsNumeric = int.TryParse(oldExtraValue, out int _);
                string extraNewValue = extraIsNumeric
                    ? intValuePool[Random.Range(0, intValuePool.Length)]
                    : stringValuePool[Random.Range(0, stringValuePool.Length)].Replace("'", "");

                for (int i = 0; i < m.codeLines.Count; i++)
                    m.codeLines[i] = ReplaceWholeWord(
                        ReplaceWholeWord(m.codeLines[i], oldExtraName, extraNewName),
                        oldExtraValue, extraNewValue);

                for (int i = 0; i < m.distractors.Count; i++)
                    m.distractors[i] = ReplaceWholeWord(
                        ReplaceWholeWord(m.distractors[i], oldExtraName, extraNewName),
                        oldExtraValue, extraNewValue);

                // Same contract as the primary rename: any prose reference to
                // a renamed secondary variable/value moves with the code.
                m.goalText = ReplaceWholeWord(
                    ReplaceWholeWord(m.goalText ?? "", oldExtraName, extraNewName),
                    oldExtraValue, extraNewValue);

                // Same contract for secondary variables inside an authored
                // SpotTheBug fix line.
                m.correctAnswer = ReplaceWholeWord(
                    ReplaceWholeWord(m.correctAnswer ?? "", oldExtraName, extraNewName),
                    oldExtraValue, extraNewValue);

                extra.name = extraNewName;
                extra.value = extraNewValue;
            }

            string baselineOutput = null;
            bool baselineSimulated = !controlFlow
                && MiniPythonEvaluator.TrySimulate(m.codeLines, out baselineOutput);

            if (baselineSimulated)
            {
                // FIX: previously only trusted the evaluator if its output
                // happened to already match original.correctAnswer, which
                // meant a template with a WRONG authored correctAnswer
                // (content typo, not a code bug) would permanently keep
                // that wrong value forever, since the mismatch itself was
                // what blocked the evaluator-based path from ever running.
                // The evaluator is the one actually executing the logic,
                // so its output is ground truth; if it disagrees with what
                // was authored, that's a content mistake worth surfacing,
                // not a reason to keep serving the wrong answer.
                // ANSWER-AUTHORITY GATE: only PredictTheOutput's correct
                // answer IS the computed output. Clobbering other formats'
                // answers (e.g. TrueOrFalse's "true") with the printed text
                // corrupted them at generation time.
                if (original.puzzleType == PuzzleType.PredictTheOutput)
                {
                    if (baselineOutput != original.correctAnswer)
                        Debug.LogWarning($"[PCG] {original.id}: authored correctAnswer " +
                                          $"'{original.correctAnswer}' does not match what the " +
                                          $"code actually computes ('{baselineOutput}'). Using the " +
                                          $"computed value. Fix this in puzzle_templates.json.");

                    m.correctAnswer = baselineOutput;
                }

                List<string> candidateLines = new List<string>(m.codeLines);

                // FIX: this used line.Contains(num)/line.Replace(num, ...),
                // plain substring matching. "5" matches inside "25", "10"
                // matches inside "100"/"150"/etc, so an UNTRACKED second
                // variable's value (anything PCGEngine doesn't know about
                // via variableName/variableValue) could get silently
                // corrupted here even after Strategy 1's word-boundary fix,
                // since digits aren't word-bounded the same way identifiers
                // are unless checked explicitly. Now uses the same \b
                // word-boundary approach as ReplaceWholeWord.
                for (int i = 0; i < candidateLines.Count; i++)
                {
                    string line = candidateLines[i];
                    foreach (string num in new string[] { "80", "18", "5", "10", "100" })
                    {
                        bool wholeNumberPresent = Regex.IsMatch(line, @"\b" + Regex.Escape(num) + @"\b");
                        if (wholeNumberPresent && !line.Contains(newValue))
                        {
                            candidateLines[i] = ReplaceWholeWord(line, num,
                                intValuePool[Random.Range(0, intValuePool.Length)]);
                            break;
                        }
                    }
                }

                for (int i = 0; i < candidateLines.Count; i++)
                {
                    string line = candidateLines[i];
                    if (line.Contains(" + ") || line.Contains(" - ") || line.Contains(" * "))
                    {
                        string op = operatorPairs[Random.Range(0, operatorPairs.Length)];
                        candidateLines[i] = Regex.Replace(line, @" [\+\-\*] ", $" {op} ");
                        break;
                    }
                }

                // ANSWER-AUTHORITY GATE: this reshuffle exists to recompute
                // PredictTheOutput's answer after the number/operator
                // shuffle above. Serving the shuffled snippet to OTHER
                // formats (a) silently flipped basic operators (+ -> * or
                // -) inside SpotTheBug and TrueOrFalse snippets -- exactly
                // the nuance-gotcha mutation the design notes prohibit --
                // and (b) clobbered their authored correctAnswer with the
                // printed output, the same corruption class the PTO gate
                // above fixed. Non-PTO formats keep the renamed snippet
                // verbatim now.
                if (original.puzzleType == PuzzleType.PredictTheOutput
                    && MiniPythonEvaluator.TrySimulate(candidateLines, out string newOutput))
                {
                    m.codeLines = candidateLines;
                    m.correctAnswer = newOutput;
                }
            }
            else if (m.correctAnswer == original.variableValue)
            {
                m.correctAnswer = newValue;
            }
        }
        else
        {
            // MUTATION SAFETY GATE (no primary variable): string literal and
            // numeric shuffles only run for control-flow-FREE snippets, for
            // the same reason as the primary-variable path -- loop bounds
            // and branch values are load-bearing for authored answers.
            bool controlFlow = PuzzleVariationEngine.ContainsControlFlow(m.codeLines);
            bool mutated = false;
            for (int i = 0; i < m.codeLines.Count && !controlFlow; i++)
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
                else if (Regex.IsMatch(line, @"\b\d+\b"))
                {
                    m.codeLines[i] = Regex.Replace(
                        line, @"\b\d+\b",
                        match => intValuePool[Random.Range(0, intValuePool.Length)]);
                    mutated = true;
                }
            }

            // Same fix applied here: if the (possibly just-mutated) code is
            // simulatable, let the evaluator's output be the ground truth
            // for correctAnswer -- but only for PredictTheOutput, whose
            // correct answer IS the printed output (answer-authority gate).
            if (!controlFlow && MiniPythonEvaluator.TrySimulate(m.codeLines, out string noVarOutput)
                && original.puzzleType == PuzzleType.PredictTheOutput)
                m.correctAnswer = noVarOutput;

            if (!mutated && !controlFlow)
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
                        if (m.bugLineIndex >= i) m.bugLineIndex++;
                        for (int k = 0; k < m.correctOrder.Count; k++)
                            if (m.correctOrder[k] >= i) m.correctOrder[k]++;
                        break;
                    }
                }
            }
        }

        // FITB blank rotation: choose WHICH token is missing. The cursor
        // rotates per skeleton so consecutive encounters never blank the
        // same token -- the old behavior blanked print every single time
        // because the authored templates ship correctAnswer == "" and the
        // format file inferred one fixed blank. Runs AFTER renaming so the
        // candidates are scanned from the served snippet; ForgeDistractors
        // below then rebuilds category-matched options for the new answer.
        if (m.puzzleType == PuzzleType.FillInTheBlank)
            PuzzleVariationEngine.RotateFitbBlank(m);

        // Distractor forge: guarantee three options that are distinct from
        // the correct answer, same-statement-family, free of nuance gotchas
        // (==/quotes/case) and free of invented identifiers, and for
        // PredictTheOutput verifiably different from the real output. Runs
        // AFTER renaming so synthesized options use the served variables.
        PuzzleVariationEngine.ForgeDistractors(m);

        Debug.Log($"[PCG] Mutated: {m.id} | Type: {m.puzzleType} | Tier: {m.difficulty}");
        return m;
    }

    [System.Serializable]
    public class TrueFalseData
    {
        public string snippetText;
        public bool isSnippetTrue;
    }
}