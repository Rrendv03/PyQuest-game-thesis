using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public class PCGEngine : MonoBehaviour
{
    public static PCGEngine Instance;

    private List<PuzzleTemplate> allTemplates = new List<PuzzleTemplate>();

    private string[] nameVariants = { "x", "score", "mana", "health", "level", "count", "total" };
    private string[] valueVariants = { "10", "25", "50", "7", "100", "42", "3" };

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
        
        // FIXED: Changed from 'if (File.Exists(path))' to '!File.Exists(path)'
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
    public PuzzleData GeneratePuzzle(string componentName, PuzzleType puzzleType)
    {
        Debug.Log($"[PCG] GeneratePuzzle called | Component: {componentName} | Type: {puzzleType} ({(int)puzzleType})");

        foreach (var t in allTemplates.Where(t => t.knowledgeComponent == componentName))
            Debug.Log($"[PCG] Candidate: {t.id} | puzzleType: {t.puzzleType} ({(int)t.puzzleType})");

        float mastery = BKTEngine.Instance.GetMastery(componentName);
        DifficultyTier targetTier = GetTierForMastery(mastery);

        // Filter by component + tier + puzzle type
        List<PuzzleTemplate> candidates = allTemplates
            .Where(t => t.knowledgeComponent == componentName
                     && t.difficulty == targetTier
                     && t.puzzleType == puzzleType)
            .ToList();

        // Fallback: any template matching component + puzzle type
        if (candidates.Count == 0)
            candidates = allTemplates
                .Where(t => t.knowledgeComponent == componentName
                         && t.puzzleType == puzzleType)
                .ToList();

        // Fallback: any template for this component
        if (candidates.Count == 0)
            candidates = allTemplates
                .Where(t => t.knowledgeComponent == componentName)
                .ToList();

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"[PCG] No templates for {componentName} | Type: {puzzleType}");
            return null;
        }

        PuzzleTemplate selected = candidates[Random.Range(0, candidates.Count)];
        PuzzleTemplate mutated = MutatePuzzlePublic(selected);

        IPuzzleFormat formatHandler = PuzzleFormatFactory.CreatePuzzleFormat(mutated);
        if (formatHandler == null)
        {
            Debug.LogError($"[PCG] Failed to create format handler for: {mutated.puzzleType}");
            return null;
        }

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

        // Pick candidates matching component + tier
        List<PuzzleTemplate> candidates = allTemplates
            .Where(t => t.knowledgeComponent == componentName && t.difficulty == targetTier)
            .ToList();

        // Fallback: any template for this component
        if (candidates.Count == 0)
            candidates = allTemplates.Where(t => t.knowledgeComponent == componentName).ToList();

        if (candidates.Count == 0) { Debug.LogWarning($"[PCG] No templates for {componentName}"); return null; }

        PuzzleTemplate selected = candidates[Random.Range(0, candidates.Count)];
        return MutatePuzzlePublic(selected);
    }

    // New generation endpoint tailored specifically for the True or False mechanic
    public TrueFalseData GenerateTrueFalsePuzzle(string componentName)
    {
        float mastery = BKTEngine.Instance.GetMastery(componentName);
        DifficultyTier targetTier = GetTierForMastery(mastery);

        // Filter templates matching both the active component and current mastery level
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

        // Select baseline template and run standard name/value structural mutations
        PuzzleTemplate baseTemplate = candidates[Random.Range(0, candidates.Count)];
        PuzzleTemplate mutatedTemplate = MutatePuzzlePublic(baseTemplate);

        // Core Procedural Mutation Rule for True/False Format:
        // System rolls a 50/50 chance to decide if the output code should be correct (True) or bugged (False)
        bool outputShouldBeTrue = Random.Range(0, 2) == 0;
        string finalCodeDisplay = string.Join("\n", mutatedTemplate.codeLines);

        if (!outputShouldBeTrue)
        {
            // Inject a semantic logical bug into the text stream to turn it False
            if (finalCodeDisplay.Contains("=="))
            {
                finalCodeDisplay = finalCodeDisplay.Replace("==", "!=");
            }
            else if (finalCodeDisplay.Contains("+"))
            {
                finalCodeDisplay = finalCodeDisplay.Replace("+", "-");
            }
            else if (finalCodeDisplay.Contains("<"))
            {
                finalCodeDisplay = finalCodeDisplay.Replace("<", ">");
            }
            else
            {
                // Fallback: append an invalid state mutation to the syntax representation
                finalCodeDisplay += "\n# Bug injected: logic trace mismatch";
            }
        }

        TrueFalseData puzzlePackage = new TrueFalseData();
        puzzlePackage.snippetText = finalCodeDisplay;
        puzzlePackage.isSnippetTrue = outputShouldBeTrue;

        return puzzlePackage;
    }

    DifficultyTier GetTierForMastery(float mastery)
    {
        if (mastery < 0.35f) return DifficultyTier.Remembering;
        if (mastery < 0.55f) return DifficultyTier.Understanding;
        if (mastery < 0.70f) return DifficultyTier.Applying;
        return DifficultyTier.Analyzing;
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

        // --- Mutation pools ---
        string[] nameVariantPool = new string[]
        {
        "mana", "health", "score", "level", "gold", "damage",
        "defense", "stamina", "magic", "runes", "power", "shield",
        "energy", "speed", "armor", "quest", "rank", "coins",
        "lives", "points", "strength", "agility", "wisdom", "luck"
        };

        string[] intValuePool = new string[]
        {
        "5", "10", "15", "20", "25", "30", "50", "75",
        "100", "150", "200", "250", "500", "7", "13", "99"
        };

        string[] stringValuePool = new string[]
        {
        "'Hero'", "'Wizard'", "'Archer'", "'Knight'", "'Mage'",
        "'Dragon'", "'Quest'", "'Rogue'", "'Paladin'", "'Hunter'",
        "'Warrior'", "'Sage'", "'Scout'", "'Ranger'", "'Monk'"
        };

        string[] greetingPool = new string[]
        {
        "'Hello'", "'Greetings'", "'Welcome'", "'Salutations'",
        "'Howdy'", "'Hey there'", "'Hi'", "'Good day'"
        };

        string[] messagePool = new string[]
        {
        "'Game Over'", "'Level Up'", "'You Win'", "'Try Again'",
        "'Quest Complete'", "'Victory'", "'Defeat'", "'Well Done'",
        "'Keep Going'", "'Almost There'"
        };

        string[] operatorPairs = new string[] { "+", "-", "*" };

        // --- Strategy 1: Variable name + value swap ---
        if (!string.IsNullOrEmpty(original.variableName))
        {
            string newName = nameVariantPool[Random.Range(0, nameVariantPool.Length)];

            // Detect if value is int or string and pick matching pool
            string newValue;
            int parsedInt;
            if (int.TryParse(original.variableValue, out parsedInt))
                newValue = intValuePool[Random.Range(0, intValuePool.Length)];
            else
                newValue = stringValuePool[Random.Range(0, stringValuePool.Length)]
                           .Replace("'", "");

            for (int i = 0; i < m.codeLines.Count; i++)
                m.codeLines[i] = m.codeLines[i]
                    .Replace(original.variableName, newName)
                    .Replace(original.variableValue, newValue);

            if (m.correctAnswer == original.variableValue)
                m.correctAnswer = newValue;

            m.variableName = newName;
            m.variableValue = newValue;

            // Strategy 2: Also randomize numeric literals inside lines
            for (int i = 0; i < m.codeLines.Count; i++)
            {
                string line = m.codeLines[i];
                foreach (string num in new string[] { "80", "18", "5", "10", "100" })
                {
                    if (line.Contains(num) && !line.Contains(newValue))
                    {
                        m.codeLines[i] = line.Replace(num,
                            intValuePool[Random.Range(0, intValuePool.Length)]);
                        break;
                    }
                }
            }

            // Strategy 3: Randomize arithmetic operators
            for (int i = 0; i < m.codeLines.Count; i++)
            {
                string line = m.codeLines[i];
                if (line.Contains(" + ") || line.Contains(" - ") || line.Contains(" * "))
                {
                    string op = operatorPairs[Random.Range(0, operatorPairs.Length)];
                    m.codeLines[i] = System.Text.RegularExpressions.Regex
                        .Replace(line, @" [\+\-\*] ", $" {op} ");
                    break;
                }
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