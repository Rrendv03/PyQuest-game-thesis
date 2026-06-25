using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ZoneTrigger : MonoBehaviour
{
    public string zoneName;
    public PuzzleType forcedPuzzleType = PuzzleType.PairACode;
    public bool randomizePuzzleType = false;

    private bool triggered = false;

    void OnTriggerEnter(Collider other)
    {
        if (triggered) return;
        if (other.CompareTag("Player"))
        {
            Debug.Log($"Player entered {zoneName}");
            triggered = true;

            PuzzleType selectedType = randomizePuzzleType
                ? GetRandomPuzzleType()
                : forcedPuzzleType;

            PuzzleManager.Instance.OnZoneEntered(zoneName, selectedType);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Debug.Log($"Player exited {zoneName}");
            triggered = false;
        }
    }

    private PuzzleType GetRandomPuzzleType()
    {
        PuzzleType[] available = new PuzzleType[]
        {
            PuzzleType.TrueOrFalse,
            PuzzleType.PairACode,
            PuzzleType.FillInTheBlank,
            PuzzleType.PredictTheOutput
        };
        return available[Random.Range(0, available.Length)];
    }
}