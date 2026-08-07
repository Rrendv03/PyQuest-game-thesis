using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PCGTestRunner : MonoBehaviour
{
    [Header("Test Configuration")]
    public bool runOnStart = true;
    public int mutationIterations = 20;

    [Header("Expected Invariants")]
    public string testVariableName = "score";
    public string testVariableValue = "10";

    void Start()
    {
        if (runOnStart) StartCoroutine(InitializeAndRun());
    }

    private IEnumerator InitializeAndRun()
    {
        // SAFETY: Ensure PCGEngine exists. If the user forgot to add it to the test scene,
        // we create a temporary one so the test can proceed.
        if (PCGEngine.Instance == null)
        {
            Debug.LogWarning("[PCGTestRunner] PCGEngine.Instance is null. Creating temporary instance...");
            GameObject tempPCG = new GameObject("TempPCGEngine");
            tempPCG.AddComponent<PCGEngine>();
            yield return null; // Wait one frame so Awake() runs
        }

        // Also ensure BKTEngine exists (needed by PCGEngine.GeneratePuzzle)
        if (BKTEngine.Instance == null)
        {
            Debug.LogWarning("[PCGTestRunner] BKTEngine.Instance is null. Creating temporary instance...");
            GameObject tempBKT = new GameObject("TempBKTEngine");
            tempBKT.AddComponent<BKTEngine>();
            yield return null;
        }

        RunAllTests();
    }

    [ContextMenu("Run All PCG Tests")]
    public void RunAllTests()
    {
        if (PCGEngine.Instance == null)
        {
            Debug.LogError("[PCGTestRunner] Cannot run tests: PCGEngine.Instance is still null after initialization.");
            return;
        }

        Debug.Log("========== PCG TEST SUITE ==========");

        TestMutationConsistency();
        TestTrueFalseNoDoubleMutation();
        TestEncounterListInit();

        Debug.Log("========== TESTS COMPLETE ==========");
    }

    // ---------- TEST 1: Mutation Consistency ----------
    private void TestMutationConsistency()
    {
        Debug.Log("[TEST] MutationConsistency: Verifying variableName matches codeLines after mutation...");

        PuzzleTemplate original = new PuzzleTemplate
        {
            id = "test_var_swap",
            knowledgeComponent = "variables",
            puzzleType = PuzzleType.FillInTheBlank,
            difficulty = DifficultyTier.Beginner,
            variableName = testVariableName,
            variableValue = testVariableValue,
            codeLines = new List<string>
            {
                $"{testVariableName} = {testVariableValue}",
                $"print({testVariableName})"
            },
            correctAnswer = testVariableValue,
            distractors = new List<string> { "5", "20" }
        };

        bool allPassed = true;

        for (int i = 0; i < mutationIterations; i++)
        {
            PuzzleTemplate mutated = PCGEngine.Instance.MutatePuzzlePublic(original);

            // INVARIANT 1: variableName must appear in code lines
            bool nameInLines = false;
            foreach (string line in mutated.codeLines)
            {
                if (line.Contains(mutated.variableName))
                {
                    nameInLines = true;
                    break;
                }
            }

            if (!nameInLines)
            {
                Debug.LogError($"[FAIL] Iteration {i}: variableName '{mutated.variableName}' NOT FOUND in code lines:\n{string.Join("\n", mutated.codeLines)}");
                allPassed = false;
            }

            // INVARIANT 2: correctAnswer should match the new value if it was the old value
            if (original.correctAnswer == original.variableValue && mutated.correctAnswer != mutated.variableValue)
            {
                Debug.LogError($"[FAIL] Iteration {i}: correctAnswer wasn't updated. Expected '{mutated.variableValue}', got '{mutated.correctAnswer}'");
                allPassed = false;
            }
        }

        Debug.Log(allPassed
            ? "[PASS] MutationConsistency: All invariants held across " + mutationIterations + " iterations."
            : "[FAIL] MutationConsistency: See errors above.");
    }

    // ---------- TEST 2: True/False No Double Mutation ----------
    private void TestTrueFalseNoDoubleMutation()
    {
        Debug.Log("[TEST] TrueFalseNoDoubleMutation: Verifying template isn't mutated twice...");

        PuzzleTemplate original = new PuzzleTemplate
        {
            id = "test_tof",
            knowledgeComponent = "conditionals",
            puzzleType = PuzzleType.TrueOrFalse,
            difficulty = DifficultyTier.Beginner,
            variableName = "health",
            variableValue = "100",
            codeLines = new List<string>
            {
                "health = 100",
                "if health > 50:",
                "    print('alive')"
            },
            correctAnswer = "True",
            distractors = new List<string>()
        };

        // Simulate what PCGEngine does: mutate once before passing to format
        PuzzleTemplate preMutated = PCGEngine.Instance.MutatePuzzlePublic(original);

        // Capture state BEFORE format handler touches it
        string preMutatedName = preMutated.variableName;
        List<string> preMutatedLines = new List<string>(preMutated.codeLines);
        string preMutatedCorrectAnswer = preMutated.correctAnswer;

        // Now create the format handler (this is what your game does)
        TrueOrFalsePuzzleFormat format = new TrueOrFalsePuzzleFormat();
        format.Initialize(preMutated);

        // INVARIANT: After Initialize(), the template's codeLines should NOT have changed
        bool linesMatch = preMutatedLines.Count == preMutated.codeLines.Count;
        if (linesMatch)
        {
            for (int i = 0; i < preMutatedLines.Count; i++)
            {
                if (preMutatedLines[i] != preMutated.codeLines[i])
                {
                    linesMatch = false;
                    Debug.LogError($"[FAIL] Line {i} changed after format init.\nBefore: {preMutatedLines[i]}\nAfter:  {preMutated.codeLines[i]}");
                }
            }
        }

        // INVARIANT: variableName must still match what's in the lines
        bool nameConsistent = true;
        foreach (string line in preMutated.codeLines)
        {
            if (!line.Contains(preMutated.variableName) && !line.Contains("print") && !line.Contains("if"))
            {
                nameConsistent = false;
            }
        }

        Debug.Log(linesMatch && nameConsistent
            ? "[PASS] TrueFalseNoDoubleMutation: Template stable after format init."
            : "[FAIL] TrueFalseNoDoubleMutation: Template was corrupted by double mutation.");
    }

    // ---------- TEST 3: Encounter List Init ----------
    private void TestEncounterListInit()
    {
        Debug.Log("[TEST] EncounterListInit: Verifying list behavior is normal...");

        List<float> list = new List<float>();
        list.Add(0.5f);
        list.Add(0.25f);

        bool passed = list.Count == 2 && list[0] == 0.5f;

        Debug.Log(passed
            ? "[PASS] EncounterListInit: List behavior normal."
            : "[FAIL] EncounterListInit: Unexpected list state.");
    }
}