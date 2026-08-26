using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD XP bar display. Shows current XP and the threshold for the
/// current sanctum's boss unlock. Place in HUDCanvas below QuestPanel.
///
/// Hierarchy:
/// XPPanel
/// ??? XPLabel (Text: "XP")
/// ??? XPBar (Image, Image Type: Filled, Fill Method: Horizontal)
/// ??? XPValueText (Text: "0 / 150")
/// </summary>
public class XPHUDDisplay : MonoBehaviour
{
    [Header("UI")]
    public Image xpBarFill;
    public Text xpValueText;
    public GameObject xpPanel;

    [Header("Current Sanctum")]
    // Set this in the Inspector per scene, or update via SetSanctum()
    public string currentSanctumID = "echoing_atrium";

    void Start()
    {
        if (XPManager.Instance != null)
        {
            XPManager.Instance.OnXPChanged += RefreshDisplay;
            RefreshDisplay(XPManager.Instance.CurrentXP);
        }
        else
        {
            // XPManager not ready yet, poll until it is
            StartCoroutine(WaitForXPManager());
        }
    }

    private System.Collections.IEnumerator WaitForXPManager()
    {
        while (XPManager.Instance == null)
            yield return null;

        XPManager.Instance.OnXPChanged += RefreshDisplay;
        RefreshDisplay(XPManager.Instance.CurrentXP);
    }

    void OnDestroy()
    {
        if (XPManager.Instance != null)
            XPManager.Instance.OnXPChanged -= RefreshDisplay;
    }

    public void SetSanctum(string sanctumID)
    {
        currentSanctumID = sanctumID;
        RefreshDisplay(XPManager.Instance != null ? XPManager.Instance.CurrentXP : 0);
    }

    private void RefreshDisplay(int currentXP)
    {
        if (XPManager.Instance == null) return;

        int threshold = XPManager.Instance.GetThreshold(currentSanctumID);

        if (xpValueText != null)
            xpValueText.text = $"{currentXP} / {threshold}";

        if (xpBarFill != null)
        {
            float fill = threshold > 0
                ? Mathf.Clamp01((float)currentXP / threshold)
                : 1f;
            xpBarFill.fillAmount = fill;
        }

        if (xpPanel != null)
            xpPanel.SetActive(true);
    }
}