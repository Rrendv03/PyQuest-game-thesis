using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PyQuest.UI
{
    /// <summary>
    /// How To Play screen controller — structured exactly like the Settings
    /// panel (SettingsPanelController): a standalone component on the panel
    /// ROOT whose Back button and return targets are fillable Inspector
    /// fields, while MainMenuController only holds the panel GameObject
    /// (just like settingsPanel / saveLoadPanel).
    ///
    /// SETUP (~1 minute, mirrors the main-menu settings panel):
    ///   1. Build the How To Play panel (full-screen, starts hidden) with:
    ///      - one Image for the page picture
    ///      - optional Texts for page title / description / page counter
    ///      - Buttons: Previous, Next, Back, Skip Tutorial
    ///   2. Add this component to the panel ROOT object.
    ///   3. Drag the Back button into "Back Button". (Optional — if the panel
    ///      contains exactly one child Button named Back / BackButton /
    ///      BtnBack it is auto-detected and hooked, same as Settings.)
    ///   4. Drag the main-menu buttons panel into "Panel To Show On Close",
    ///      plus the menu background into "Extra Objects To Show On Close" —
    ///      identical to the main-menu copy of SettingsPanelController.
    ///   5. In MainMenuController, drag the PANEL GameObject into
    ///      "How To Play Panel" (next to Settings Panel / Save Load Panel).
    ///
    /// Pages are fillable in the Inspector: each page = picture (a Sprite
    /// OR a plain PNG texture — either field works) + optional title +
    /// optional description. No code or scene wiring for the content itself.
    ///
    /// Two open modes (called by MainMenuController):
    ///  - Open(): view-only, from the "How To Play" menu button. Back is
    ///    enabled and returns to the main menu; Skip Tutorial is hidden.
    ///  - OpenAsTutorial(callback): the New Game exception. Back is DISABLED,
    ///    Skip Tutorial is ENABLED, and pressing Skip invokes the callback so
    ///    the normal new-game flow (scene load) continues.
    ///
    /// Click sounds are handled globally by UISoundManager — nothing to wire.
    /// </summary>
    public class HowToPlayScreen : MonoBehaviour
    {
        [Serializable]
        public class HowToPlayPage
        {
            [Tooltip("Screenshot / diagram shown on this page (Sprite slot: PNGs with Texture Type = 'Sprite (2D and UI)'). Optional - if both image fields are empty the page Image is hidden (useful for text-only pages).")]
            public Sprite image;

            [Tooltip("Same picture as a plain PNG texture (Texture Type = 'Default') - drop the PNG file straight into this slot. Used when 'Image' (Sprite) above is empty.")]
            public Texture2D imageTexture;

            [Tooltip("Optional heading for this page, e.g. 'Mission Tablets'.")]
            public string title;

            [TextArea(3, 10)]
            [Tooltip("What this page explains, e.g. how mission tablets work.")]
            public string description;
        }

        [Header("Back Button")]
        [Tooltip("The panel's Back button. Hooked automatically on Awake. If " +
                 "left empty, a single child Button named like 'Back' is " +
                 "auto-detected (same behaviour as SettingsPanelController).")]
        public Button backButton;

        [Header("Return Target")]
        [Tooltip("Panel shown again when Back is pressed. Main-menu copy: the " +
                 "main menu buttons panel.")]
        public GameObject panelToShowOnClose;

        [Tooltip("Extra objects re-shown on Back, e.g. the main-menu background " +
                 "image. Leave empty if the background stays visible.")]
        public GameObject[] extraObjectsToShowOnClose;

        [Header("Page Content (fillable in Inspector)")]
        [Tooltip("Every 'how it works' page: picture + optional title + description. Configure entirely in the Inspector.")]
        [SerializeField] private List<HowToPlayPage> pages = new List<HowToPlayPage>();

        [Tooltip("Image element that displays the current page's picture.")]
        [SerializeField] private Image pageImage;

        [Tooltip("Optional heading Text for the current page.")]
        [SerializeField] private Text pageTitleText;

        [Tooltip("Optional description Text for the current page.")]
        [SerializeField] private Text pageDescriptionText;

        [Tooltip("Optional '1 / 5' counter Text.")]
        [SerializeField] private Text pageCounterText;

        [Header("Navigation Buttons")]
        [Tooltip("Shows the previous page (wraps if enabled).")]
        [SerializeField] private Button previousButton;

        [Tooltip("Shows the next page (wraps if enabled).")]
        [SerializeField] private Button nextButton;

        [Header("Tutorial Exception (New Game)")]
        [Tooltip("Skip Tutorial button — hidden in normal view mode, enabled only when the screen opens as the pre-game tutorial from New Game.")]
        [SerializeField] private Button skipTutorialButton;

        [Tooltip("If true, Next on the last page wraps to the first page (and Previous on the first page wraps to the last).")]
        [SerializeField] private bool wrapAroundPages = true;

        [Tooltip("Format for the page counter Text. {0} = current page, {1} = total pages.")]
        [SerializeField] private string pageCounterFormat = "Page {0} / {1}";

        [Tooltip("Text shown on the Skip button in tutorial mode. Fillable without touching code.")]
        [SerializeField] private string skipButtonText = "Skip Tutorial";

        [Tooltip("Optional extra Text explaining that this is shown before the first game (tutorial mode only). Leave empty to hide.")]
        [SerializeField] private Text tutorialBanner;

        [Tooltip("Message shown in the banner while in tutorial mode.")]
        [SerializeField] private string tutorialBannerMessage = "New here? Take a quick look at how the game works - or skip ahead and start playing!";

        private bool inTutorialMode;
        private int currentPage;
        private Action tutorialCompleteCallback;
        private Text skipButtonTextTarget;
        private bool backHooked;

        /// <summary>Sprites created at runtime from PNG (Texture2D) page content, so each texture is wrapped exactly once.</summary>
        private readonly Dictionary<Texture2D, Sprite> textureSpriteCache = new Dictionary<Texture2D, Sprite>();

        /// <summary>True while the screen is open as a pre-game tutorial (Back disabled, Skip enabled).</summary>
        public bool IsInTutorialMode => inTutorialMode;

        void Awake()
        {
            HookBackButton();

            if (skipTutorialButton != null)
            {
                skipButtonTextTarget = skipTutorialButton.GetComponentInChildren<Text>(true);
                skipTutorialButton.gameObject.SetActive(false); // view mode default; enabled only in tutorial mode
                skipTutorialButton.onClick.AddListener(HandleSkipTutorialClicked);
            }

            if (previousButton != null) previousButton.onClick.AddListener(HandlePreviousClicked);
            else Debug.LogWarning("[HowToPlayScreen] No Previous button assigned on '" + name + "' — page back-navigation will not work.");

            if (nextButton != null) nextButton.onClick.AddListener(HandleNextClicked);
            else Debug.LogWarning("[HowToPlayScreen] No Next button assigned on '" + name + "' — page forward-navigation will not work.");
        }

        private void OnDestroy()
        {
            foreach (Sprite created in textureSpriteCache.Values)
                if (created != null) Destroy(created);
            textureSpriteCache.Clear();

            if (previousButton != null) previousButton.onClick.RemoveListener(HandlePreviousClicked);
            if (nextButton != null) nextButton.onClick.RemoveListener(HandleNextClicked);
            if (backButton != null) backButton.onClick.RemoveListener(HandleBackClicked);
            if (skipTutorialButton != null) skipTutorialButton.onClick.RemoveListener(HandleSkipTutorialClicked);
        }

        // ------------------------------------------------------------------
        // PUBLIC API — MainMenuController wires these (Settings-style)
        // ------------------------------------------------------------------

        /// <summary>
        /// Opens the screen in normal view mode from the "How To Play" menu
        /// button. Hides the panel/background it replaces (same behaviour as
        /// SettingsPanelController.Open); Back restores them via Close().
        /// </summary>
        public void Open()
        {
            OpenInternal(tutorialMode: false, onTutorialComplete: null);
        }

        /// <summary>
        /// Opens the screen as the pre-game tutorial (New Game pressed):
        /// Back is disabled, Skip Tutorial is enabled, and pressing Skip
        /// invokes <paramref name="onTutorialComplete"/> so the normal
        /// new-game flow continues.
        /// </summary>
        public void OpenAsTutorial(Action onTutorialComplete)
        {
            OpenInternal(tutorialMode: true, onTutorialComplete: onTutorialComplete);
        }

        /// <summary>
        /// Back: hides this panel and re-shows whatever it was opened over.
        /// Safe no-op for anything left unassigned. Blocked while in tutorial
        /// mode (the Back button is also non-interactable there).
        /// </summary>
        public void Close()
        {
            if (inTutorialMode) return; // safety: Back never leaves the tutorial

            inTutorialMode = false;
            tutorialCompleteCallback = null;
            gameObject.SetActive(false);

            SetTarget(panelToShowOnClose, true);
            foreach (GameObject go in extraObjectsToShowOnClose)
                SetTarget(go, true);
        }

        // ------------------------------------------------------------------
        // OPEN / CLOSE internals
        // ------------------------------------------------------------------

        private void OpenInternal(bool tutorialMode, Action onTutorialComplete)
        {
            inTutorialMode = tutorialMode;
            tutorialCompleteCallback = onTutorialComplete;
            currentPage = 0;

            gameObject.SetActive(true);

            // Hide what we came from — but never an ancestor of this panel,
            // because hiding that would hide this panel with it (the same
            // ancestor safety SettingsPanelController uses).
            SetTarget(panelToShowOnClose, false);
            foreach (GameObject go in extraObjectsToShowOnClose)
                SetTarget(go, false);

            ApplyTutorialModeToUI();
            UpdatePage();
        }

        private void ApplyTutorialModeToUI()
        {
            if (backButton != null) backButton.interactable = !inTutorialMode;

            if (skipTutorialButton != null)
            {
                skipTutorialButton.gameObject.SetActive(inTutorialMode);
                if (skipButtonTextTarget != null) skipButtonTextTarget.text = skipButtonText;
            }
            else if (inTutorialMode)
            {
                Debug.LogWarning("[HowToPlayScreen] Tutorial mode needs a Skip Tutorial button — assign it in the Inspector, or the player can only leave via Back.");
            }

            if (tutorialBanner != null)
            {
                tutorialBanner.gameObject.SetActive(inTutorialMode);
                tutorialBanner.text = tutorialBannerMessage;
            }
        }

        // ------------------------------------------------------------------
        // NAVIGATION
        // ------------------------------------------------------------------

        private void HandlePreviousClicked()
        {
            if (pages == null || pages.Count == 0) return;

            if (currentPage > 0) currentPage--;
            else if (wrapAroundPages) currentPage = pages.Count - 1;
            else return;

            UpdatePage();
        }

        private void HandleNextClicked()
        {
            if (pages == null || pages.Count == 0) return;

            if (currentPage < pages.Count - 1) currentPage++;
            else if (wrapAroundPages) currentPage = 0;
            else
            {
                // Last page and no wrap: in tutorial mode the last Next finishes the tutorial.
                if (inTutorialMode) CompleteTutorial();
                return;
            }

            UpdatePage();
        }

        private void HandleBackClicked()
        {
            Close();
        }

        private void HandleSkipTutorialClicked()
        {
            if (!inTutorialMode) return;
            CompleteTutorial();
        }

        private void CompleteTutorial()
        {
            Action callback = tutorialCompleteCallback;
            tutorialCompleteCallback = null;
            Close();
            callback?.Invoke();
        }

        // ------------------------------------------------------------------
        // HELPERS — same contract as SettingsPanelController
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
                Debug.LogWarning("[HowToPlayScreen] No back button on '" + name +
                                 "' — drag it into 'Back Button', or wire the " +
                                 "button's onClick -> HowToPlayScreen.Close.");
                return;
            }

            backButton.onClick.AddListener(HandleBackClicked);
            backHooked = true;
            Debug.Log("[HowToPlayScreen] Back button hooked: " + backButton.name);
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

        // ------------------------------------------------------------------
        // RENDERING
        // ------------------------------------------------------------------

        /// <summary>
        /// Sprite for a page: the Sprite field first, else the PNG/Texture2D
        /// field wrapped once via Sprite.Create (cached, so flipping pages
        /// never re-creates it). Null when the page has no picture at all.
        /// </summary>
        private Sprite ResolvePageSprite(HowToPlayPage page)
        {
            if (page == null) return null;
            if (page.image != null) return page.image;
            if (page.imageTexture == null) return null;

            if (textureSpriteCache.TryGetValue(page.imageTexture, out Sprite cached) && cached != null)
                return cached;

            Sprite created = Sprite.Create(
                page.imageTexture,
                new Rect(0f, 0f, page.imageTexture.width, page.imageTexture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            textureSpriteCache[page.imageTexture] = created;
            return created;
        }

        private void UpdatePage()
        {
            int count = pages != null ? pages.Count : 0;
            if (count == 0)
            {
                Debug.LogWarning("[HowToPlayScreen] No pages configured. Add pages in the Inspector.");
                if (pageCounterText != null) pageCounterText.text = string.Empty;
                return;
            }

            currentPage = Mathf.Clamp(currentPage, 0, count - 1);
            HowToPlayPage page = pages[currentPage];

            if (pageImage != null)
            {
                Sprite sprite = ResolvePageSprite(page);
                if (sprite != null) { pageImage.sprite = sprite; pageImage.enabled = true; }
                else pageImage.enabled = false;
            }

            if (pageTitleText != null)
            {
                pageTitleText.gameObject.SetActive(!string.IsNullOrEmpty(page.title));
                pageTitleText.text = page.title;
            }

            if (pageDescriptionText != null)
            {
                pageDescriptionText.gameObject.SetActive(!string.IsNullOrEmpty(page.description));
                pageDescriptionText.text = page.description;
            }

            if (pageCounterText != null)
                pageCounterText.text = string.Format(pageCounterFormat, currentPage + 1, count);

            if (!wrapAroundPages)
            {
                if (previousButton != null) previousButton.interactable = currentPage > 0;
                if (nextButton != null) nextButton.interactable = inTutorialMode || currentPage < count - 1;
            }
        }

        // ------------------------------------------------------------------
        // EDITOR HELPERS
        // ------------------------------------------------------------------

#if UNITY_EDITOR
        // Convenience so designers can verify wiring from the Inspector.
        [ContextMenu("Preview First Page")]
        private void DebugPreviewFirstPage() { currentPage = 0; UpdatePage(); }

        [ContextMenu("Preview Next Page")]
        private void DebugPreviewNextPage() { HandleNextClicked(); }
#endif
    }
}