using UnityEngine;
using UnityEngine.UI;

public class InteractButtonController : MonoBehaviour
{
    [Header("Prompt UI")]
    public GameObject interactPromptUI;
    public Text interactButtonText;

    private NPCController nearestNPC;
    private InteractableObject nearestInteractable;

    void Start()
    {
        if (interactPromptUI != null)
            interactPromptUI.SetActive(false);
    }

    // ?? NPC Registration ??????????????????????????????????????????????????????
    public void RegisterNPC(NPCController npc)
    {
        nearestNPC = npc;
        ShowPrompt($"Talk to {npc.npcDisplayName}");
    }

    public void ClearNPC(NPCController npc)
    {
        if (nearestNPC != npc) return;
        nearestNPC = null;

        // If an interactable is still in range, show its prompt instead
        if (nearestInteractable != null)
            ShowPrompt(nearestInteractable.promptText);
        else
            HidePrompt();
    }

    // ?? Interactable Registration ?????????????????????????????????????????????
    public void RegisterInteractable(InteractableObject obj)
    {
        nearestInteractable = obj;

        // NPC takes priority if both are somehow in range simultaneously
        if (nearestNPC == null)
            ShowPrompt(obj.promptText);
    }

    public void ClearInteractable(InteractableObject obj)
    {
        if (nearestInteractable != obj) return;
        nearestInteractable = null;

        if (nearestNPC != null)
            ShowPrompt($"Talk to {nearestNPC.npcDisplayName}");
        else
            HidePrompt();
    }

    // ?? Force Hide ????????????????????????????????????????????????????????????
    // Called by EncounterManager when an encounter starts.
    // Clears stale NPC/interactable references so the prompt does not
    // reappear after the player is teleported back from combat.
    public void ForceHide()
    {
        nearestNPC = null;
        nearestInteractable = null;
        HidePrompt();
    }

    // ?? Interact Button ???????????????????????????????????????????????????????
    public void OnInteractButtonPressed()
    {
        Debug.Log("[InteractButtonController] Interact button pressed.");

        if (nearestNPC != null && nearestNPC.IsPlayerInRange())
        {
            nearestNPC.TriggerInteraction();
            return;
        }

        if (nearestInteractable != null && nearestInteractable.IsPlayerInRange())
            nearestInteractable.TriggerInteraction();
    }

    // ?? Prompt Helpers ????????????????????????????????????????????????????????
    private void ShowPrompt(string text)
    {
        if (interactPromptUI != null) interactPromptUI.SetActive(true);
        if (interactButtonText != null) interactButtonText.text = text;
    }

    private void HidePrompt()
    {
        if (interactPromptUI != null) interactPromptUI.SetActive(false);
        if (interactButtonText != null) interactButtonText.text = "";
    }
}