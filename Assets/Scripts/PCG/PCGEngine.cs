// PyQuest PCG engine — all invariants/documented behavior live in PCG_MIGRATION_PLAN.md (do not re-document inline).
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

    public bool IsLoaded { get; private set; } = false;

    private List<PuzzleTemplate> allTemplates = new List<PuzzleTemplate>();

    private List<PuzzleTemplate> allSkeletons = new List<PuzzleTemplate>();

    private const bool useRequestTimeExpansion = true;

    private const int MaxGenerationDraws = 3;

    private readonly Dictionary<string, int> skeletonDrawCounter = new Dictionary<string, int>();

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

            allTemplates = PuzzleVariationEngine.ExpandAll(authored);
            allSkeletons = authored;
            Debug.Log($"[PCG] Loaded {authored.Count} authored templates -> " +
                      $"{allTemplates.Count} playable instances (fallback pool), " +
                      $"{allSkeletons.Count} skeletons for request-time generation");
        }

        IsLoaded = true;
    }

    public PuzzleData GeneratePuzzle(string componentName, PuzzleType puzzleType)
    {
        float mastery = BKTEngine.Instance.GetMastery(componentName);
        DifficultyTier targetTier = GetTierForMastery(mastery);
        return GeneratePuzzle(componentName, puzzleType, targetTier);
    }

    private static bool SkeletonServesTier(PuzzleTemplate s, int tier)
    {
        return (int)s.difficulty == tier
            || ((tier == 1 || tier == 2) && PuzzleVariationEngine.CanScale(s));
    }

    private string StableVariantId(string skeletonId)
    {
        int k;
        skeletonDrawCounter.TryGetValue(skeletonId, out k);
        skeletonDrawCounter[skeletonId] = k + 1;
        return skeletonId + "_v" + (k % PuzzleVariationEngine.VariantsPerSkeleton);
    }

    private PuzzleTemplate DrawFromSkeletons(List<PuzzleTemplate> skeletons,
                                             string bucketKey, int serveTier)
    {
        // Cover the whole bucket, not just 3 random draws: one bad skeleton
        // must not exhaust the budget when siblings are healthy.
        int draws = Mathf.Max(MaxGenerationDraws, skeletons.Count);
        for (int draw = 0; draw < draws; draw++)
        {
            PuzzleTemplate skeleton = SelectWithHistory(skeletons, bucketKey);
            if (skeleton == null) return null;

            int targetTier = serveTier >= 0 ? serveTier : (int)skeleton.difficulty;
            PuzzleTemplate instance = PuzzleVariationEngine.ExpandSkeleton(skeleton, targetTier);
            if (instance != null)
                instance.id = StableVariantId(skeleton.id);

            string failure = null;
            if (instance == null
                || !PuzzleVariationEngine.ValidateInstance(instance, skeleton, out failure))
            {
                Debug.LogWarning($"[PCG] Request-time draw {draw + 1}/{draws} rejected " +
                                 $"(skeleton: {(skeleton != null ? skeleton.id : "null")}): {failure}");
                continue;
            }

            Debug.Log($"[PCG] Request-time generation | skeleton: {skeleton.id} | " +
                      $"draw: {draw + 1}/{draws} | validation: {failure}");
            return ServeMutated(instance, skeleton);
        }

        Debug.LogWarning($"[PCG] Request-time generation exhausted {draws} draws for " +
                         $"'{bucketKey}'; serving from the load-time pool instead.");
        return null;
    }

    // Post-mutation gate: MutatePuzzlePublic runs AFTER validation and can
    // invalidate its invariants (e.g. an op-flip making a distractor
    // semantically equal to the answer). Re-validate; on failure serve the
    // already-validated pre-mutation instance - mutations are cosmetic.
    private PuzzleTemplate ServeMutated(PuzzleTemplate validated,
                                        PuzzleTemplate skeleton)
    {
        PuzzleTemplate mutated = MutatePuzzlePublic(validated);
        string failure;
        if (PuzzleVariationEngine.ValidateInstance(mutated, skeleton, out failure))
            return mutated;
        Debug.LogWarning($"[PCG] Post-mutation gate rejected ({failure}); " +
                         $"serving pre-mutation instance.");
        return validated;
    }

    private PuzzleTemplate SelectValidatedFromPool(List<PuzzleTemplate> poolInstances,
                                                   string bucketKey)
    {
        for (int attempt = 0; attempt < poolInstances.Count; attempt++)
        {
            PuzzleTemplate selected = SelectWithHistory(poolInstances, bucketKey);
            if (selected == null) break;
            string failure;
            if (PuzzleVariationEngine.ValidateInstance(selected, FindAuthoredSkeleton(selected.id), out failure))
                return selected;
            Debug.LogWarning($"[PCG] Pool candidate rejected ({failure}): {selected.id}");
        }
        Debug.LogWarning($"[PCG] Pool validation exhausted for '{bucketKey}'; widening (validated instances only).");
        foreach (List<PuzzleTemplate> widened in WidenedPools(poolInstances))
        {
            for (int attempt = 0; attempt < Mathf.Min(widened.Count, 12); attempt++)
            {
                PuzzleTemplate selected = SelectWithHistory(widened, bucketKey + "|widen");
                if (selected == null) break;
                string failure;
                if (PuzzleVariationEngine.ValidateInstance(selected, FindAuthoredSkeleton(selected.id), out failure))
                {
                    Debug.LogWarning($"[PCG] Widened pool serving validated instance: {selected.id}");
                    return selected;
                }
            }
        }
        PuzzleTemplate emergency = poolInstances[0];
        Debug.LogError($"[PCG] No validated instance in the entire pool; serving {emergency.id}. Corpus bug - do not ship.");
        return emergency;
    }

    private IEnumerable<List<PuzzleTemplate>> WidenedPools(List<PuzzleTemplate> bucket)
    {
        if (bucket == null || bucket.Count == 0) yield break;
        // Widening stays INSIDE the knowledge component: a sanctum teaches its
        // own KCs, so a variables bucket must never leak conditional templates.
        string kc = bucket[0].knowledgeComponent;
        PuzzleType ptype = bucket[0].puzzleType;
        yield return allTemplates.Where(t => t.knowledgeComponent == kc
                                          && t.puzzleType == ptype).ToList();
        yield return allTemplates.Where(t => t.knowledgeComponent == kc).ToList();
    }

    private PuzzleTemplate FindAuthoredSkeleton(string instanceId)
    {
        if (allSkeletons == null) return null;
        foreach (PuzzleTemplate s in allSkeletons)
            if (instanceId == s.id || instanceId.StartsWith(s.id + "_v")
                || instanceId.StartsWith(s.id + "_s"))
                return s;
        return null;
    }

    public PuzzleData GeneratePuzzle(string componentName, PuzzleType puzzleType,
                                      DifficultyTier forcedTier)
    {
        if (!IsLoaded)
            Debug.LogWarning("[PCG] GeneratePuzzle called before puzzle_templates.json finished " +
                              "loading. allTemplates may still be empty; this call will likely " +
                              "return null. Wait on PCGEngine.Instance.IsLoaded before entering " +
                              "gameplay.");

        if (useRequestTimeExpansion)
        {
            List<PuzzleTemplate> skeletons = allSkeletons
                .Where(s => s.knowledgeComponent == componentName
                         && s.puzzleType == puzzleType
                         && SkeletonServesTier(s, (int)forcedTier))
                .ToList();
            bool exactTier = true;
            if (skeletons.Count == 0)
            {
                exactTier = false;
                skeletons = allSkeletons
                    .Where(s => s.knowledgeComponent == componentName
                             && s.puzzleType == puzzleType)
                    .ToList();
            }
            if (skeletons.Count == 0)
                skeletons = allSkeletons
                    .Where(s => s.knowledgeComponent == componentName)
                    .ToList();

            if (skeletons.Count > 0)
            {
                PuzzleTemplate generated = DrawFromSkeletons(skeletons,
                    $"rt|{componentName}|{puzzleType}|{forcedTier}",
                    exactTier ? (int)forcedTier : -1);
                if (generated != null)
                {
                    IPuzzleFormat rtFormatHandler = PuzzleFormatFactory.CreatePuzzleFormat(generated);
                    if (rtFormatHandler == null)
                    {
                        Debug.LogError($"[PCG] Failed to create format handler for: {generated.puzzleType}");
                        return null;
                    }
                    return new PuzzleData(generated, rtFormatHandler);
                }
            }
        }

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
        PuzzleTemplate selected = SelectValidatedFromPool(candidates, bucketKey);
        PuzzleTemplate mutated = ServeMutated(selected, FindAuthoredSkeleton(selected.id));

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

        if (useRequestTimeExpansion)
        {
            List<PuzzleTemplate> skeletons = allSkeletons
                .Where(s => s.knowledgeComponent == componentName
                         && SkeletonServesTier(s, (int)targetTier))
                .ToList();
            bool exactTier = true;
            if (skeletons.Count == 0)
            {
                exactTier = false;
                skeletons = allSkeletons
                    .Where(s => s.knowledgeComponent == componentName)
                    .ToList();
            }
            if (skeletons.Count > 0)
            {
                PuzzleTemplate generated = DrawFromSkeletons(skeletons,
                    $"rt|{componentName}|legacy|{targetTier}",
                    exactTier ? (int)targetTier : -1);
                if (generated != null) return generated;
            }
        }

        string bucketKey = $"{componentName}|legacy|{targetTier}";
        PuzzleTemplate selected = SelectValidatedFromPool(candidates, bucketKey);
        return ServeMutated(selected, FindAuthoredSkeleton(selected.id));
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

        if (useRequestTimeExpansion)
        {
            List<PuzzleTemplate> skeletons = allSkeletons
                .Where(s => s.knowledgeComponent == componentName
                         && SkeletonServesTier(s, (int)targetTier))
                .ToList();
            bool exactTier = true;
            if (skeletons.Count == 0)
            {
                exactTier = false;
                skeletons = allSkeletons
                    .Where(s => s.knowledgeComponent == componentName)
                    .ToList();
            }
            if (skeletons.Count > 0)
            {
                PuzzleTemplate generated = DrawFromSkeletons(skeletons,
                    $"rt|{componentName}|truefalse|{targetTier}",
                    exactTier ? (int)targetTier : -1);
                if (generated != null)
                    return BuildTrueFalseData(generated);
            }
        }

        string bucketKey = $"{componentName}|truefalse|{targetTier}";
        PuzzleTemplate baseTemplate = SelectValidatedFromPool(candidates, bucketKey);
        PuzzleTemplate mutatedTemplate = ServeMutated(baseTemplate,
            FindAuthoredSkeleton(baseTemplate.id));

        return BuildTrueFalseData(mutatedTemplate);
    }

    private TrueFalseData BuildTrueFalseData(PuzzleTemplate mutatedTemplate)
    {
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

        if (!outputShouldBeTrue && !finalCodeDisplay.Contains("# Bug injected"))
        {
            string baseOutput = null;
            string postOutput = null;
            List<string> postLines = finalCodeDisplay.Split('\n').ToList();
            if (MiniPythonEvaluator.TrySimulate(mutatedTemplate.codeLines, out baseOutput)
                && MiniPythonEvaluator.TrySimulate(postLines, out postOutput)
                && baseOutput == postOutput)
            {
                finalCodeDisplay = string.Join("\n", mutatedTemplate.codeLines)
                                 + "\n# Bug injected: logic trace mismatch";
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
            acceptedOrders = original.acceptedOrders != null
                ? new List<string>(original.acceptedOrders) : null,
            distractors = new List<string>(original.distractors),
            variableName = original.variableName,
            variableValue = original.variableValue,
            goalText = original.goalText,

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
            "endurance", "fortune", "resolve", "insight",
            "arcana", "valor", "spirit", "glyph", "ember", "thunder",
            "cinder", "radiance", "zenith", "apex", "stride", "pulse",
            "cadence", "lore", "sigil", "crest", "momentum", "gravity",
            "harvest", "beacon", "quiver", "talent", "bounty", "tempo"
        };

        string[] intValuePool = new string[]
        {
            "5", "10", "15", "20", "25", "30", "50", "75",
            "100", "150", "200", "250", "500", "7", "13", "99",
            "3", "8", "12", "17", "22", "40", "60", "80",
            "120", "175", "300", "400", "9", "11", "45", "65",
            "4", "6", "14", "18", "28", "35", "55", "70",
            "85", "95", "110", "130"
        };

        string[] stringValuePool = new string[]
        {
            "'Hero'", "'Wizard'", "'Archer'", "'Knight'", "'Mage'",
            "'Dragon'", "'Quest'", "'Rogue'", "'Paladin'", "'Hunter'",
            "'Warrior'", "'Sage'", "'Scout'", "'Ranger'", "'Monk'",
            "'Druid'", "'Bard'", "'Cleric'", "'Alchemist'", "'Nomad'",
            "'Guardian'", "'Sentinel'", "'Wanderer'", "'Champion'", "'Seer'",
            "'Oracle'", "'Voyager'", "'Crusader'", "'Mystic'", "'Pilgrim'",
            "'Vanguard'", "'Warlord'", "'Enigma'"
        };

        string[] greetingPool = new string[]
        {
            "'Hello'", "'Greetings'", "'Welcome'", "'Salutations'",
            "'Howdy'", "'Hey there'", "'Hi'", "'Good day'",
            "'Well met'", "'Ahoy'", "'Cheers'", "'Hail'",
            "'Good morning'", "'Good evening'", "'Blessings'",
            "'Onward'", "'Salute'", "'Hello there'"
        };

        string[] messagePool = new string[]
        {
            "'Game Over'", "'Level Up'", "'You Win'", "'Try Again'",
            "'Quest Complete'", "'Victory'", "'Defeat'", "'Well Done'",
            "'Keep Going'", "'Almost There'",
            "'New Record'", "'Boss Defeated'", "'Path Unlocked'",
            "'Sanctum Cleared'", "'Not Yet'",
            "'Final Blow'", "'Rune Found'", "'Gate Open'",
            "'Tower Cleared'", "'Perfect Run'", "'Slow Down'",
            "'Next Round'", "'Combo Broken'", "'Skill Up'"
        };

        string[] operatorPairs = new string[] { "+", "-", "*" };

        if (!string.IsNullOrEmpty(original.variableName))
        {
            var reserved = new HashSet<string>();
            foreach (string blob in m.codeLines.Concat(m.distractors)
                         .Append(m.correctAnswer ?? "").Append(m.goalText ?? ""))
                foreach (Match w in Regex.Matches(blob ?? "", @"[A-Za-z_]\w*"))
                    reserved.Add(w.Value);
            reserved.Remove(original.variableName);
            string newName = nameVariantPool[Random.Range(0, nameVariantPool.Length)];
            for (int attempt = 0; attempt < 40 && reserved.Contains(newName); attempt++)
                newName = nameVariantPool[Random.Range(0, nameVariantPool.Length)];
            string newValue;
            int parsedInt;
            bool isNumeric = int.TryParse(original.variableValue, out parsedInt);
            if (isNumeric)
                newValue = intValuePool[Random.Range(0, intValuePool.Length)];
            else
                newValue = stringValuePool[Random.Range(0, stringValuePool.Length)].Replace("'", "");

            bool controlFlow = PuzzleVariationEngine.ContainsControlFlow(m.codeLines);

            for (int i = 0; i < m.codeLines.Count; i++)
                m.codeLines[i] = ReplaceWholeWord(m.codeLines[i], original.variableName, newName);

            for (int i = 0; i < m.distractors.Count; i++)
                m.distractors[i] = ReplaceWholeWord(m.distractors[i], original.variableName, newName);

            m.goalText = ReplaceWholeWord(m.goalText ?? "", original.variableName, newName);

            m.correctAnswer = ReplaceWholeWord(m.correctAnswer ?? "", original.variableName, newName);

            if (!controlFlow)
            {
                for (int i = 0; i < m.codeLines.Count; i++)
                    m.codeLines[i] = ReplaceWholeWord(m.codeLines[i], original.variableValue, newValue);

                for (int i = 0; i < m.distractors.Count; i++)
                    m.distractors[i] = ReplaceWholeWord(m.distractors[i], original.variableValue, newValue);

                m.goalText = ReplaceWholeWord(m.goalText ?? "", original.variableValue, newValue);

                m.correctAnswer = ReplaceWholeWord(m.correctAnswer ?? "", original.variableValue, newValue);
            }

            m.variableName = newName;
            m.variableValue = controlFlow ? original.variableValue : newValue;

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

                m.goalText = ReplaceWholeWord(
                    ReplaceWholeWord(m.goalText ?? "", oldExtraName, extraNewName),
                    oldExtraValue, extraNewValue);

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

            if (!controlFlow && MiniPythonEvaluator.TrySimulate(m.codeLines, out string noVarOutput)
                && original.puzzleType == PuzzleType.PredictTheOutput)
                m.correctAnswer = noVarOutput;

            if (!mutated && !controlFlow && m.correctOrder.Count == 0)
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

        if (m.puzzleType == PuzzleType.FillInTheBlank)
            PuzzleVariationEngine.RotateFitbBlank(m);

        PuzzleVariationEngine.ForgeDistractors(m);

        if (m.puzzleType == PuzzleType.LineScramble)
            m.acceptedOrders = PuzzleVariationEngine.ComputeAcceptedOrders(m);

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
