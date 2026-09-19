using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Standalone controller for the Settings panel so its Back button works
/// everywhere the panel is used — the pause menu (HUD) AND the main menu —
/// without depending on MainMenuController (which only exists in the menu
/// scene, which is exactly why the ripped-out copy's back button was dead).
///
/// SETUP per settings-panel copy (~30 seconds, no hierarchy changes):
///   1. Select the settings panel root object.
///   2. Add Component -> SettingsPanelController.
///   3. Drag the panel's Back button into "Back Button".
///      (Optional — if the button is named Back / BackButton / BtnBack it is
///      auto-detected and hooked for you.)
///   4. Drag the panel that Back should return to into "Panel To Show On Close":
///        - pause-menu copy -> the pause menu root (the panel holding
///          Resume / Save-Load / Exit / Quit).
///        - main-menu copy  -> the main menu buttons panel, plus the menu
///          background into "Extra Objects To Show On Close".
///
/// The "Settings" button that opens this panel keeps working however you have
/// it today; if you want it handled here too, wire its onClick to
/// SettingsPanelController.Open, or call from code:
///     settingsPanel.GetComponent<SettingsPanelController>().Open();
///
/// Click sounds are handled globally by UISoundManager — nothing to wire.
///
/// NOTE: if this panel's Back button still carries the old persistent
/// onClick -> MainMenuController.OnBackToMainClicked entry, that entry is a
/// no-op in gameplay scenes (the script isn't there) but in the MAIN MENU it
/// would fire together with this component. Remove the stale entry there so
/// Back doesn't run twice.
/// </summary>
public class SettingsPanelController : MonoBehaviour
{
    [Header("Back Button")]
    [Tooltip("The panel's Back button. Hooked automatically on Awake. If " +
             "left empty, a single child Button named like 'Back' is " +
             "auto-detected.")]
    public Button backButton;

    [Header("Return Target")]
    [Tooltip("Panel shown again when Back is pressed. Pause-menu copy: the " +
             "pause menu root. Main-menu copy: the main menu buttons panel.")]
    public GameObject panelToShowOnClose;

    [Tooltip("Extra objects re-shown on Back, e.g. the main-menu background " +
             "image. Leave empty on the pause-menu copy.")]
    public GameObject[] extraObjectsToShowOnClose;

    private bool backHooked;

    void Awake()
    {
        HookBackButton();
    }

    // ------------------------------------------------------------------
    // PUBLIC API — wire buttons / PauseMenuManager to these
    // ------------------------------------------------------------------

    /// <summary>
    /// Opens the settings panel and hides the panel it replaces.
    /// </summary>
    public void Open()
    {
        gameObject.SetActive(true);

        // Hide what we came from — but never an ancestor of this panel,
        // because hiding that would hide the settings panel with it
        // (the settings panel typically lives inside the pause menu root).
        SetTarget(panelToShowOnClose, false);
        foreach (GameObject go in extraObjectsToShowOnClose)
            SetTarget(go, false);
    }

    /// <summary>
    /// Back: hides the settings panel and re-shows whatever it was opened
    /// over. Safe no-op for anything left unassigned.
    /// </summary>
    public void Close()
    {
        gameObject.SetActive(false);

        SetTarget(panelToShowOnClose, true);
        foreach (GameObject go in extraObjectsToShowOnClose)
            SetTarget(go, true);
    }

    // ------------------------------------------------------------------
    // HELPERS
    // ------------------------------------------------------------------

    private void SetTarget(GameObject go, bool active)
    {
        // Skip null, self, and any ancestor: an ancestor must stay active
        // for this panel to be visible at all.
        if (go == null || transform.IsChildOf(go.transform))
            return;

        go.SetActive(active);
    }

    private void HookBackButton()
    {
        if (backHooked)
            return;

        if (backButton == null)
            backButton = FindBackButtonFallback();

        if (backButton == null)
        {
            Debug.LogWarning("[SettingsPanelController] No back button on '" + name +
                             "' — drag it into 'Back Button', or wire the " +
                             "button's onClick -> SettingsPanelController.Close.");
            return;
        }

        backButton.onClick.AddListener(Close);
        backHooked = true;
        Debug.Log("[SettingsPanelController] Back button hooked: " + backButton.name);
    }

    /// <summary>
    /// Zero-setup fallback: if exactly one child Button is named like a back
    /// button (Back / BackButton / BtnBack ...), use it automatically.
    /// Zero or multiple matches -> do nothing, stay manual.
    /// </summary>
    private Button FindBackButtonFallback()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        Button match = null;
        int matches = 0;

        foreach (Button b in buttons)
        {
            if (IsBackLikeName(b.gameObject.name))
            {
                match = b;
                matches++;
            }
        }

        return matches == 1 ? match : null;
    }

    private static bool IsBackLikeName(string n)
    {
        if (string.IsNullOrEmpty(n))
            return false;

        string s = n.ToLowerInvariant().Replace("_", "").Replace(" ", "");
        return s == "back" || s == "backbutton" || s == "btnback" || s == "backbtn";
    }
}
