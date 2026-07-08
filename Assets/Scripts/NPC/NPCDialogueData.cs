using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class DialogueLine
{
    public string speakerName;
    [TextArea(2, 5)]
    public string dialogueText;
}

[System.Serializable]
public class DialogueSequence
{
    public string sequenceID;
    public List<DialogueLine> lines = new List<DialogueLine>();
}

[CreateAssetMenu(fileName = "NPCDialogueData", menuName = "PyQuest/NPC Dialogue Data")]
public class NPCDialogueData : ScriptableObject
{
    public string npcName;
    public List<DialogueSequence> sequences = new List<DialogueSequence>();

    public DialogueSequence GetSequence(string sequenceID)
    {
        foreach (var seq in sequences)
            if (seq.sequenceID == sequenceID)
                return seq;

        Debug.LogWarning($"[NPCDialogueData] Sequence '{sequenceID}' not found in {npcName}");
        return null;
    }
}