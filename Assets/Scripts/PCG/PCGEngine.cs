using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

public class PCGEngine : MonoBehaviour
{
    public static PCGEngine Instance;
    private List<PuzzleTemplate> allTemplates = new List<PuzzleTemplate>();

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

    // Two-parameter: uses live BKT mastery
    public PuzzleData GeneratePuzzle(string componentName, PuzzleType puzzleType)
    {
        float mastery = BKTEngine.Instance.GetMastery(componentName);
        DifficultyTier targetTier = GetTierForMastery(mastery);
        return GeneratePuzzle(componentName, puzzleType, targetTier);
    }

    // Three-parameter: uses locked tier from encounter
    public PuzzleData GeneratePuzzle(string componentName, PuzzleType puzzleType, DifficultyTier forcedTier)
    {
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
            Debug.LogWarning($"[PCG] No templates for {componentName} | {puzzleType} | {forcedTier}");
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

        Debug.Log($"[PCG] Generated | {componentName} | {puzzleType} | {forcedTier}");
        Debug.Log($"[PCG] After mutation: {string.Join(" | ", mutated.codeLines)}");
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
            candidates = allTemplates
                .Where(t => t.knowledgeComponent == componentName)
                .ToList();

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"[PCG] No templates for {componentName}");
            return null;
        }

        return MutatePuzzlePublic(candidates[Random.Range(0, candidates.Count)]);
    }

    public TrueFalseData GenerateTrueFalsePuzzle(string componentName)
    {
        float mastery = BKTEngine.Instance.GetMastery(componentName);
        DifficultyTier targetTier = GetTierForMastery(mastery);

        List<PuzzleTemplate> candidates = allTemplates
            .Where(t => t.knowledgeComponent == componentName && t.difficulty == targetTier)
            .ToList();

        if (candidates.Count == 0)
            candidates = allTemplates
                .Where(t => t.knowledgeComponent == componentName)
                .ToList();

        if (candidates.Count == 0)
        {
            Debug.LogWarning($"[PCG] No templates for: {componentName}");
            return null;
        }

        PuzzleTemplate baseTemplate = candidates[Random.Range(0, candidates.Count)];
        PuzzleTemplate mutatedTemplate = MutatePuzzlePublic(baseTemplate);

        bool outputShouldBeTrue = Random.Range(0, 2) == 0;
        string finalCodeDisplay = string.Join("\n", mutatedTemplate.codeLines);

        if (!outputShouldBeTrue)
        {
            if (finalCodeDisplay.Contains("=="))
                finalCodeDisplay = finalCodeDisplay.Replace("==", "!=");
            else if (finalCodeDisplay.Contains("+"))
                finalCodeDisplay = finalCodeDisplay.Replace("+", "-");
            else if (finalCodeDisplay.Contains("<"))
                finalCodeDisplay = finalCodeDisplay.Replace("<", ">");
            else
                finalCodeDisplay += "\n# Bug injected: logic trace mismatch";
        }

        return new TrueFalseData
        {
            snippetText = finalCodeDisplay,
            isSnippetTrue = outputShouldBeTrue
        };
    }

    public DifficultyTier GetTierForMasteryPublic(float mastery)
        => GetTierForMastery(mastery);

    private DifficultyTier GetTierForMastery(float mastery)
    {
        if (mastery < 0.50f) return DifficultyTier.Beginner;
        if (mastery < 0.75f) return DifficultyTier.Intermediate;
        return DifficultyTier.Advanced;
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

        string[] namePool = new string[]
        {
            "mana", "health", "score", "level", "gold", "damage",
            "defense", "stamina", "magic", "runes", "power", "shield",
            "energy", "speed", "armor", "quest", "rank", "coins",
            "lives", "points", "strength", "agility", "wisdom", "luck"
        };
        string[] intPool = new string[]
        {
            "5", "10", "15", "20", "25", "30", "50", "75",
            "100", "150", "200", "250", "500", "7", "13", "99"
        };
        string[] stringPool = new string[]
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

        if (!string.IsNullOrEmpty(original.variableName))
        {
            // --- Strategy 1: Variable name + value swap (runs ONCE) ---
            string newName = namePool[Random.Range(0, namePool.Length)];
            string newValue;
            if (int.TryParse(original.variableValue, out _))
                newValue = intPool[Random.Range(0, intPool.Length)];
            else
                newValue = stringPool[Random.Range(0, stringPool.Length)].Replace("'", "");

            for (int i = 0; i < m.codeLines.Count; i++)
                m.codeLines[i] = m.codeLines[i]
                    .Replace(original.variableName, newName)
                    .Replace(original.variableValue, newValue);

            // Update correctAnswer if it matched the original value
            if (m.correctAnswer == original.variableValue)
                m.correctAnswer = newValue;

            m.variableName = newName;
            m.variableValue = newValue;

            // --- Strategy 2: Randomize numeric literals ---
            for (int i = 0; i < m.codeLines.Count; i++)
            {
                string line = m.codeLines[i];
                foreach (string num in new string[] { "80", "18", "5", "10", "100" })
                {
                    if (line.Contains(num) && !line.Contains(newValue))
                    {
                        m.codeLines[i] = line.Replace(num,
                            intPool[Random.Range(0, intPool.Length)]);
                        break;
                    }
                }
            }

            // --- Strategy 3: Randomize arithmetic operators ---
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
                    m.codeLines[i] = System.Text.RegularExpressions.Regex.Replace(
                        line, @"\b\d+\b",
                        match => intPool[Random.Range(0, intPool.Length)]);
                    mutated = true;
                }
            }

            // --- Strategy 5: Inject variable line if nothing mutated ---
            if (!mutated)
            {
                string[] injections = new string[]
                {
                    namePool[Random.Range(0, namePool.Length)]
                        + " = " + intPool[Random.Range(0, intPool.Length)],
                    namePool[Random.Range(0, namePool.Length)]
                        + " = " + stringPool[Random.Range(0, stringPool.Length)]
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

        Debug.Log($"[PCG] Mutated: {m.id} | {m.puzzleType} | {m.difficulty}");
        return m;
    }

    [System.Serializable]
    public class TrueFalseData
    {
        public string snippetText;
        public bool isSnippetTrue;
    }
}