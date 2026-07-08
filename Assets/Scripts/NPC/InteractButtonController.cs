using UnityEngine;

public class InteractButtonController : MonoBehaviour
{
    private NPCController nearestNPC;

    public void RegisterNPC(NPCController npc)
    {
        nearestNPC = npc;
    }

    public void ClearNPC(NPCController npc)
    {
        if (nearestNPC == npc) nearestNPC = null;
    }

    public void OnInteractButtonPressed()
    {
        Debug.Log("[InteractButtonController] Interact button pressed.");
        if (nearestNPC != null && nearestNPC.IsPlayerInRange())
            nearestNPC.TriggerInteraction();
    }
}