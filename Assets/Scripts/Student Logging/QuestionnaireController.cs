using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Post-game questionnaire, shown after the epilogue end screen.
///
/// Renders the Instrument Validation Form as a real uGUI TABLE (bordered
/// Word-style rows, NOT ascii art): a non-uniform 6-column grid where the
/// question column stretches, the four score columns are small fixed-width
/// toggle cells, and the remarks column is a fixed-width input cell. All
/// rows live inside the ScrollRect Content, so the whole form scrolls.
///
/// Flow: end screen appears after the epilogue -> player presses the
/// questionnaire button -> EpilogueEndScreenController hides the end screen
/// and calls Show() -> player fills Name / Email / DPA consent, then the
/// scored table -> Submit validates everything, writes BOTH
/// questionnaire_responses.csv (one row per submission, spreadsheet-ready)
/// and questionnaire_response_<timestamp>.json (full per-student record)
/// into Application.persistentDataPath -> confirmation panel -> its
/// Continue returns to the end screen, which now shows only its Continue
/// button (the send-CSV and questionnaire buttons stay hidden for the data
/// gathering session).
///
/// ============================================================================
/// SCENE SETUP (one-time, in MainMap, next to the end screen panel)
/// ============================================================================
///
/// A) QuestionnairePanel (root, under the same Canvas as the end screen)
///    GameObject + full-stretch RectTransform + Image (dark backdrop).
///    Put QuestionnaireController on this GO. Leave the panel INACTIVE.
///
///    ├─ ScrollRect GO  ("Form Scroll")
///    │    Components: ScrollRect (Vertical, Movement=Clamped),
///    │                Image (optional, transparent), RectMask2D.
///    │    Anchors: stretch; leave ~110px bottom margin for the Submit bar.
///    │    │
///    │    └─ Viewport (RectMask2D, stretch full)
///    │         └─ Content ("Form Content")
///    │              Vertical Layout Group (spacing 12, width expand ON,
///    │              height expand OFF) + Content Size Fitter
///    │              (Vertical Fit = Preferred Size). This pair is what
///    │              makes the form scrollable.
///    │              Leave Content EMPTY: every visible row is generated
///    │              at runtime from the templates below, in this order:
///    │                title, working title, instructions, Likert
///    │                explanation, respondent fields (name/email), DPA
///    │                consent, table header rows, prior-experience row,
///    │                then per section: section header + item rows,
///    │                then "Nothing follows".
///    │
///    ├─ SubmitBar (bottom of the panel, OUTSIDE the ScrollRect)
///    │    └─ SubmitButton (Button + Image + TMP label)
///    │
///    └─ ConfirmationPanel ("Done Panel", full stretch, INACTIVE)
///         ├─ Image (backdrop)
///         ├─ ThanksText (TextMeshProUGUI)
///         └─ ContinueButton (Button + Image + TMP label)
///
/// B) Templates (OPTIONAL — the controller generates ALL content in code,
///    so templates can no longer corrupt the layout; they exist only to
///    let you style rows). Put six simple INACTIVE GameObjects anywhere
///    off-screen (e.g. "Questionnaire Templates" under the panel):
///
///    TextBlock / SectionHeader / ConsentRow / FieldRow / HeaderRow /
///    ItemRow — each is just a styled panel: an optional Image background
///    and/or padding. NO children, NO TextMeshProUGUI, NO toggles, NO
///    input fields are required inside them; anything you put inside a
///    template is DELETED at spawn time. If you leave a template slot
///    empty the controller builds a clean unstyled row instead, so even
///    a fully unwired setup produces a correct questionnaire.
///
///    Column sizing is code-driven per the spec: number/checkbox cells
///    are small (scoreColumnWidth), remarks stays wide (remarksColumnWidth),
///    and every row uses the same widths so columns align down the table.
///
/// C) Wire the controller (nothing else in the scene):
///    All the UI references below, and check the config lists: sections +
///    items are pre-filled with the exact wording from the validation doc.
///
/// Nothing throws if a reference is missing; that part is skipped.
/// </summary>
public class QuestionnaireController : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Root panel of the whole questionnaire, inactive by default.")]
    public GameObject questionnairePanel;
    [Tooltip("The ScrollRect that contains the form content.")]
    public ScrollRect formScrollRect;
    [Tooltip("Empty child of the ScrollRect Content; every generated row is spawned here.")]
    public GameObject formHook;
    [Tooltip("Inactive template: root + TMP + ContentSizeFitter(V=Preferred). For title, instructions, Likert explanation, Nothing follows.")]
    public GameObject textBlockTemplate;
    [Tooltip("Inactive template: same as TextBlock but bold font). For section divider rows.")]
    public GameObject sectionHeaderTemplate;
    [Tooltip("Inactive template: full-width row with one Toggle + TMP label. For the DPA consent checkbox.")]
    public GameObject consentRowTemplate;
    [Tooltip("Inactive template: row with 'FieldLabel' TMP + 'FieldInput' TMP_InputField. For Name / Email.")]
    public GameObject fieldRowTemplate;
    [Tooltip("Inactive template: 6-column row of TMP texts (QuestionText, Score4..Score1, RemarksText). Used for both header rows.")]
    public GameObject headerRowTemplate;
    [Tooltip("Inactive template: 6-column row — 'QuestionText' TMP, 'Score4'..'Score1' Toggles, 'RemarksInput' TMP_InputField.")]
    public GameObject itemRowTemplate;
    [Tooltip("YOUR OWN text component anywhere in the scene. All validation errors are written here — you position and style it, the script only sets its text and turns it red/black.")]
    public TextMeshProUGUI validationText;
    [Tooltip("Submit button (pinned bar, outside the ScrollRect).")]
    public Button submitButton;
    [Tooltip("Shown after a successful save, replacing the form panel.")]
    public GameObject confirmationPanel;
    [Tooltip("Text inside confirmationPanel.")]
    public TextMeshProUGUI thanksText;
    [Tooltip("Button on the confirmation panel; returns to the end screen.")]
    public Button continueButton;

    [Header("Text")]
    [Tooltip("Base font size for every generated text element.")]
    public float baseFontSize = 12f;

    [Header("Spacing")]
    [Tooltip("Left/right margin (px) between the panel edge and the form content.")]
    public float sidePadding = 12f;

    [Header("Column Sizing")]
    [Tooltip("Width of each of the four score columns (number + checkbox only).")]
    public float scoreColumnWidth = 36f;
    [Tooltip("Width of the remarks input column (only used when Remarks are enabled).")]
    public float remarksColumnWidth = 180f;
    [Tooltip("Enable the Remarks column. Disabled by default: no remarks cells are built and no scene reconfiguration is needed.")]
    public bool showRemarks = false;

    [Header("Header Block Texts")]
    public string titleText = "INSTRUMENT VALIDATION FORM";
    [TextArea]
    public string workingTitle =
        "Working Title: PyQuest: An Adaptive 3D Fantasy RPG for Novice Python Programmers using Machine Learning via Bayesian Knowledge Tracing (BKT)";
    [TextArea]
    public string instructions =
        "Instructions: Please rate the level of acceptability and appropriateness of the following statements by putting a check (✔) in the score of your choice, with 4 as the highest score and 1 as the lowest. In rating, kindly consider each statement's appropriateness to the system.";
    [TextArea(4, 12)]
    public string likertExplanation =
        "How to interpret the scores:\n" +
        "4 — Strongly Agree: the system fully meets the statement.\n" +
        "3 — Agree: the system meets the statement with minor reservations.\n" +
        "2 — Disagree: the system does not adequately meet the statement.\n" +
        "1 — Strongly Disagree: the system does not meet the statement at all.\n\n" +
        "(The first table row, Prior Programming Experience, uses its own scale: 1 = none, 2 = novice, 3 = intermediate, 4 = advanced.)";

    [Header("Respondent Fields (shown before the table)")]
    public List<QuestionnaireTextField> textFields = new List<QuestionnaireTextField>
    {
        new QuestionnaireTextField { label = "Name", required = true },
        new QuestionnaireTextField { label = "Email", required = false, isEmail = true },
    };
    [TextArea(2, 6)]
    public string consentText =
        "I have read and understood the Data Privacy Act of 2012 (RA 10173). I voluntarily consent to the collection, processing, and storage of my responses and personal information for the purposes of this study's evaluation.";

    [Header("The Table")]
    [Tooltip("Special first row of the table; its own scale (none/novice/intermediate/advanced).")]
    public string priorExperiencePrompt = "Prior Programming Experience (before this course)\n1 (none), 2 (novice), 3 (intermediate), 4 (advanced)";
    [Tooltip("Section divider rows and their item statements, verbatim from the validation doc.")]
    public List<QuestionnaireSection> sections = new List<QuestionnaireSection>
    {
        new QuestionnaireSection
        {
            sectionName = "Functionality Suitability",
            items = new List<string>
            {
                "I find that all necessary features and functions are present in the system.",
                "I receive accurate and precise results when performing functions in the system.",
                "I can easily accomplish my intended tasks using the system's functions.",
                "I experience smooth and seamless operation without missing essential tools or procedures.",
            },
        },
        new QuestionnaireSection
        {
            sectionName = "Performance Efficiency",
            items = new List<string>
            {
                "I experience fast response and processing times when using the system.",
                "I notice that the system operates efficiently without consuming excessive device memory or processing resources.",
                "I find that the system can handle large amounts of data and user activities without slowing down.",
                "I observe consistent performance speed even when performing complex operations within the system.",
            },
        },
        new QuestionnaireSection
        {
            sectionName = "Compatibility",
            items = new List<string>
            {
                "I can use the system alongside other applications without experiencing conflict or system crashes.",
                "I observe that running this system does not negatively affect the performance of other programs on my device.",
                "I can smoothly exchange or share data between this system and other related systems or applications.",
                "I find that the system accurately interprets and uses information received from external software or services.",
            },
        },
        new QuestionnaireSection
        {
            sectionName = "Interaction Capability",
            items = new List<string>
            {
                "I can easily tell whether the system fits my needs upon looking at its main features.",
                "I can learn how to use the system's functions quickly and without difficulty.",
                "I find the system easy to operate, control, and navigate during tasks.",
                "I am protected from making mistakes, as the system provides clear warnings and confirmation prompts.",
            },
        },
        new QuestionnaireSection
        {
            sectionName = "Reliability",
            items = new List<string>
            {
                "I experience stable and reliable performance from the system during normal operation.",
                "I can access and use the system whenever I need it without unexpected downtime.",
                "I find that the system continues to function properly even when minor system or network errors occur.",
                "I observe that the system can quickly recover data and restore its normal state after an unexpected interruption or system breakdown.",
            },
        },
        new QuestionnaireSection
        {
            sectionName = "Security",
            items = new List<string>
            {
                "I am confident that my personal information and data are kept private and accessible only to authorized users.",
                "I trust that the system protects data from unauthorized modifications, tampering, or deletion.",
                "I observe that system activities and transactions are properly recorded and traceable to specific users.",
                "I can verify my identity securely through the system's authentication process.",
            },
        },
        new QuestionnaireSection
        {
            sectionName = "Maintainability",
            items = new List<string>
            {
                "I observe that changes or updates to one feature do not break or affect other components of the system.",
                "I can easily identify or diagnose the cause of errors and system issues when they occur.",
                "I find that system updates, bug fixes, or enhancements can be applied smoothly without degrading overall system quality.",
                "I can easily test and verify system features to confirm that they are functioning correctly.",
            },
        },
        new QuestionnaireSection
        {
            sectionName = "Flexibility",
            items = new List<string>
            {
                "I can use the system across different hardware, software, or operating environments without issues.",
                "I observe that the system easily adjusts to increasing or changing workloads without losing performance.",
                "I find that installing or uninstalling the system in my environment is quick and hassle-free.",
                "I can conveniently replace previous software or alternative tools with this system to perform the same functions.",
            },
        },
    };
    public string nothingFollowsText = "Nothing follows";

    [Header("Confirmation")]
    [Tooltip("Shown on the confirmation panel after saving.")]
    public string thanksMessage = "Thank you! Your response has been saved.";

    [Serializable]
    public class QuestionnaireTextField
    {
        [Tooltip("Label shown left of the input.")]
        public string label;
        [Tooltip("Optional placeholder inside the empty input.")]
        public string placeholder;
        [Tooltip("Whether Submit is blocked while this field is empty.")]
        public bool required = true;
        [Tooltip("Require a basic name@domain format when non-empty.")]
        public bool isEmail;
    }

    [Serializable]
    public class QuestionnaireSection
    {
        [Tooltip("Bold divider row text, e.g. 'Functionality Suitability'.")]
        public string sectionName;
        [Tooltip("Item statements under this section, in order.")]
        public List<string> items = new List<string>();
    }

    // One scored row of the table (prior-experience row + every item).
    private class ItemRowState
    {
        public string section;
        public string prompt;
        public readonly Toggle[] toggles = new Toggle[4];
        public TMP_InputField remarks;
    }

    private class FieldRowState
    {
        public QuestionnaireTextField def;
        public TMP_InputField input;
    }

    /// <summary>Scene singleton so EpilogueEndScreenController can find the
    /// questionnaire without a serialized reference. Null-safe at call sites.</summary>
    public static QuestionnaireController Instance { get; private set; }

    private bool formBuilt;
    private readonly List<ItemRowState> itemRows = new List<ItemRowState>();
    private readonly List<FieldRowState> fieldRows = new List<FieldRowState>();
    private Toggle consentToggle;
    private readonly List<ToggleGroup> toggleGroups = new List<ToggleGroup>();
    private bool submitted;

    private bool listenersAttached;

    private void Awake()
    {
        Debug.Log("[Questionnaire] Awake");
        Instance = this;
        ResolveReferences();

        // Force the intended respondent-field policy at runtime; the serialized
        // Inspector list may still hold stale entries (required Email, 'Anything else').
        textFields.RemoveAll(f => f != null && f.label != null && f.label.StartsWith("Anything else"));
        foreach (var f in textFields)
            if (f != null && f.label == "Email") f.required = false;

        if (questionnairePanel != null) questionnairePanel.SetActive(false);
        if (confirmationPanel != null) confirmationPanel.SetActive(false);

        AttachListeners();
    }

    private void AttachListeners()
    {
        if (listenersAttached) return;
        if (submitButton == null)
        {
            Transform root = questionnairePanel != null ? questionnairePanel.transform : transform;
            foreach (var b in root.GetComponentsInChildren<Button>(true))
                if (b.name.ToLower().Contains("submit")) { submitButton = b; break; }
        }
        if (submitButton != null)
        {
            submitButton.onClick.RemoveListener(OnSubmitClicked);
            submitButton.onClick.AddListener(OnSubmitClicked);
            listenersAttached = true;
            Debug.Log($"[Questionnaire] Submit listener attached to '{submitButton.name}'");
        }
        else Debug.LogWarning("[Questionnaire] No submit button found!");
        if (continueButton != null)
        {
            continueButton.onClick.RemoveListener(OnConfirmationContinueClicked);
            continueButton.onClick.AddListener(OnConfirmationContinueClicked);
        }
    }

    /// <summary>
    /// Safety net for "Missing" inspector refs: inactive objects DO keep
    /// their references, but a half-wired scene (e.g. ThanksText or
    /// ContinueButton left unassigned or on a disabled object that was
    /// duplicated) breaks Submit silently. Re-find them by name among ALL
    /// children including inactive ones, then fall back to a code-built
    /// confirmation overlay so Submit can never be a dead button.
    /// </summary>
    private void ResolveReferences()
    {
        Transform root = questionnairePanel != null ? questionnairePanel.transform : transform;
        var allTmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        var allButtons = root.GetComponentsInChildren<Button>(true);
        var allImages = root.GetComponentsInChildren<Image>(true);

        if (thanksText == null)
            foreach (var t in allTmps)
                if (t.name.Contains("Thank") || t.name.Contains("Thanks")) { thanksText = t; break; }
        if (continueButton == null)
            foreach (var b in allButtons)
                if (b.name.Contains("Continue") || b.name.ToLower().Contains("confirm")) { continueButton = b; break; }
        if (confirmationPanel == null)
        {
            foreach (var img in allImages)
                if (img.name.Contains("Confirm") || img.name.Contains("Done"))
                { confirmationPanel = img.gameObject; break; }
        }
        if (validationText == null)
            foreach (var t in allTmps)
                if (t.name.Contains("Validation") || t.name.Contains("Error")) { validationText = t; break; }

        // Last resort: build a guaranteed-working confirmation overlay.
        if (confirmationPanel == null || thanksText == null || continueButton == null)
            BuildConfirmationOverlay();
    }

    /// <summary>Code-built confirmation panel (backdrop + thanks text +
    /// continue button) used only when the scene wiring is incomplete.</summary>
    private void BuildConfirmationOverlay()
    {
        Transform root = questionnairePanel != null ? questionnairePanel.transform : transform;
        confirmationPanel = new GameObject("Confirmation Panel (auto)", typeof(RectTransform));
        var rect = (RectTransform)confirmationPanel.transform;
        rect.SetParent(root, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        var bg = confirmationPanel.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = new Color(1f, 1f, 1f, 0.97f);

        GameObject textGo = new GameObject("ThanksText (auto)", typeof(RectTransform));
        var tRect = (RectTransform)textGo.transform;
        tRect.SetParent(rect, false);
        tRect.anchorMin = new Vector2(0.1f, 0.45f); tRect.anchorMax = new Vector2(0.9f, 0.75f);
        tRect.offsetMin = Vector2.zero; tRect.offsetMax = Vector2.zero;
        thanksText = textGo.AddComponent<TextMeshProUGUI>();
        thanksText.font = SharedFont();
        thanksText.fontSize = 16f;
        thanksText.color = Color.black;
        thanksText.alignment = TextAlignmentOptions.Midline;
        thanksText.text = thanksMessage;

        GameObject btnGo = new GameObject("ContinueButton (auto)", typeof(RectTransform));
        var bRect = (RectTransform)btnGo.transform;
        bRect.SetParent(rect, false);
        bRect.anchorMin = new Vector2(0.35f, 0.2f); bRect.anchorMax = new Vector2(0.65f, 0.32f);
        bRect.offsetMin = Vector2.zero; bRect.offsetMax = Vector2.zero;
        var btnImg = btnGo.AddComponent<Image>();
        btnImg.sprite = WhiteSprite();
        btnImg.color = new Color32(70, 130, 200, 255);
        continueButton = btnGo.AddComponent<Button>();
        var btnLabel = AddTmpChild(bRect, "Label", FontStyles.Bold);
        btnLabel.color = Color.white;
        btnLabel.alignment = TextAlignmentOptions.Midline;
        btnLabel.text = "Continue";
    }

    /// <summary>Fresh start every time the questionnaire opens: everything
    /// unchecked, every input empty, so the player cannot submit blanks
    /// that come pre-checked.</summary>
    private void ResetFormState()
    {
        UncheckAllScores();
        if (consentToggle != null) consentToggle.SetIsOnWithoutNotify(false);
        foreach (var row in itemRows)
        {
            foreach (var t in row.toggles)
                if (t != null) t.SetIsOnWithoutNotify(false);
            if (row.remarks != null) row.remarks.text = "";
        }
        foreach (var f in fieldRows)
            if (f.input != null) f.input.text = "";
    }

    /// <summary>Called by EpilogueEndScreenController when the end screen redirects here.</summary>
    public void Show()
    {
        // Lock the player and hide the HUD again, mirroring the end screen,
        // in case anything restored them in between.
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            PlayerMovement movement = player.GetComponent<PlayerMovement>();
            if (movement != null) movement.enabled = false;
        }
        HUDController.Instance?.SetVisible(false);
        SaveRestrictionEnforcer.Instance?.AddBlocker("questionnaire");

        submitted = false;
        BuildForm(); // no-op after the first time
        AttachListeners(); // safety net if refs resolved late
        ResetFormState();
        ClearValidation();
        if (questionnairePanel != null)
        {
            questionnairePanel.SetActive(true);
            // ToggleGroup force-checks its first toggle whenever the group
            // (re)enables — which happens on this SetActive. Uncheck again
            // one frame later, after the group's OnEnable has run.
            StartCoroutine(UncheckAfterGroupEnable());
        }
        if (formScrollRect != null) formScrollRect.verticalNormalizedPosition = 1f;
        Debug.Log($"[Questionnaire] Shown. itemRows={itemRows.Count} fields={fieldRows.Count} " +
                  $"consent={(consentToggle != null ? "ok" : "MISSING")} " +
                  $"submit={(submitButton != null ? submitButton.name : "MISSING")} " +
                  $"interactable={(submitButton != null ? submitButton.interactable.ToString() : "-")} " +
                  $"groups={toggleGroups.Count}");
    }

    private System.Collections.IEnumerator UncheckAfterGroupEnable()
    {
        yield return null;          // let ToggleGroup.OnEnable run first
        yield return null;          // and any layout-driven pass
        UncheckAllScores();
        Debug.Log("[Questionnaire] Post-enable uncheck done");
    }

    private void UncheckAllScores()
    {
        foreach (var g in toggleGroups)
        {
            if (g == null) continue;
            g.allowSwitchOff = true;
            foreach (var t in g.GetComponentsInChildren<Toggle>(true))
                t.SetIsOnWithoutNotify(false);
            g.allowSwitchOff = false;
        }
    }

    // ------------------------------------------------------------------
    // Form construction (table renderer)
    // ------------------------------------------------------------------

    private void BuildForm()
    {
        if (formBuilt || formHook == null) return;
        // Templates are optional styling sources; every cell is generated
        // in code, so an unwired template list can no longer break the form.

        formBuilt = true;
        EnsureHookLayout();
        itemRows.Clear();
        fieldRows.Clear();
        consentToggle = null;
        for (int i = formHook.transform.childCount - 1; i >= 0; i--)
            Destroy(formHook.transform.GetChild(i).gameObject);

        // --- Header block ---
        AddText(textBlockTemplate, titleText);
        AddText(textBlockTemplate, workingTitle);
        AddText(textBlockTemplate, instructions);

        // --- Likert interpretation, before the table ---
        AddText(textBlockTemplate, likertExplanation);

        // --- Respondent information ---
        AddText(sectionHeaderTemplate, "Respondent Information");
        foreach (var field in textFields)
        {
            if (field == null || string.IsNullOrWhiteSpace(field.label)) continue;
            GameObject row = SpawnRow(fieldRowTemplate, "Field Row");
            var fhlg = row.GetComponent<HorizontalLayoutGroup>() ?? row.AddComponent<HorizontalLayoutGroup>();
            ConfigureRowLayout(fhlg);
            EnsureContentSizeFitter(row);

            var label = MakeCellText(row.transform, "FieldLabel", TextAlignmentOptions.MidlineLeft);
            var lle = label.gameObject.AddComponent<LayoutElement>();
            lle.preferredWidth = 140f;
            lle.minHeight = 55f;
            label.text = field.label;

            var inputCell = MakeCell(row.transform, "FieldInput", 0f);
            var ile = inputCell.GetComponent<LayoutElement>();
            ile.preferredWidth = -1f;
            ile.flexibleWidth = 1f;
            ile.minWidth = 300f;
            var input = BuildInputField(inputCell);
            input.lineType = TMP_InputField.LineType.SingleLine;
            if (input.placeholder is TextMeshProUGUI ph && !string.IsNullOrEmpty(field.placeholder))
                ph.text = field.placeholder;
            input.text = "";
            fieldRows.Add(new FieldRowState { def = field, input = input });
        }

        // --- DPA consent checkbox ---
        var consent = SpawnRow(consentRowTemplate, "Consent Row");
        var chlg = consent.GetComponent<HorizontalLayoutGroup>() ?? consent.AddComponent<HorizontalLayoutGroup>();
        ConfigureRowLayout(chlg);
        EnsureContentSizeFitter(consent);

        var toggleCell = new GameObject("ConsentToggleCell", typeof(RectTransform));
        var tRect = (RectTransform)toggleCell.transform;
        tRect.SetParent(consent.transform, false);
        tRect.localScale = Vector3.one;
        var tle = toggleCell.AddComponent<LayoutElement>();
        tle.preferredWidth = 40f;
        tle.minHeight = 34f;
        consentToggle = BuildToggle(toggleCell);

        var consentLabel = MakeCellText(consent.transform, "ConsentLabel", TextAlignmentOptions.TopLeft);
        var cle = consentLabel.gameObject.AddComponent<LayoutElement>();
        cle.flexibleWidth = 1f;
        cle.minHeight = 34f;
        consentLabel.text = consentText;

        // --- Table header band (two rows, as in the doc) ---
        var header1 = SpawnRow(headerRowTemplate, "Header Row 1");
        BuildHeaderRow(header1, "Proposed Question", "SCORE", "", "", "", "Remarks", true);

        var header2 = SpawnRow(headerRowTemplate, "Header Row 2");
        BuildHeaderRow(header2, "", "4", "3", "2", "1", "", false);

        // --- Row 0: prior programming experience (its own scale) ---
        AddItemRow("", priorExperiencePrompt);

        // --- Sections + items ---
        foreach (var section in sections)
        {
            if (section == null) continue;
            if (!string.IsNullOrWhiteSpace(section.sectionName))
                AddText(sectionHeaderTemplate, section.sectionName);

            if (section.items == null) continue;
            foreach (string item in section.items)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                AddItemRow(section.sectionName, item);
            }
        }

        AddText(textBlockTemplate, nothingFollowsText);

        // Two-pass rebuild: wrapped question text needs column widths before
        // its preferred height is correct, so rebuild after a canvas pass.
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(formHook.transform as RectTransform);
        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(formHook.transform as RectTransform);
    }

    /// <summary>One table row: statement | 4 toggles | remarks input.
    /// Every cell is generated in code; the template only supplies styling.</summary>
    private void AddItemRow(string section, string prompt)
    {
        GameObject row = SpawnRow(itemRowTemplate, "Item Row");
        var hlg = row.GetComponent<HorizontalLayoutGroup>() ?? row.AddComponent<HorizontalLayoutGroup>();
        ConfigureRowLayout(hlg);
        EnsureContentSizeFitter(row);

        var question = MakeCellText(row.transform, "QuestionText", TextAlignmentOptions.TopLeft);
        var qle = question.gameObject.AddComponent<LayoutElement>();
        qle.flexibleWidth = 1f;
        qle.minHeight = 42f;
        question.fontSize = baseFontSize;
        question.text = prompt;

        var state = new ItemRowState { section = section, prompt = prompt };
        var group = row.GetComponent<ToggleGroup>() ?? row.AddComponent<ToggleGroup>();
        // NOTE: allowSwitchOff must stay TRUE while the toggles register.
        // With it false, Unity's ToggleGroup force-checks its first toggle
        // (Score 4) when none is on — the "pre-checked" bug. We flip it to
        // false only AFTER everything is built and unchecked.
        group.allowSwitchOff = true;

        for (int i = 0; i < 4; i++)
        {
            var cell = MakeCell(row.transform, "Score" + (4 - i), scoreColumnWidth);
            var toggle = BuildToggle(cell);
            toggle.isOn = false;
            toggle.group = group;
            state.toggles[i] = toggle;
        }
        if (!toggleGroups.Contains(group)) toggleGroups.Add(group);

        if (showRemarks)
        {
            var remarksCell = MakeCell(row.transform, "RemarksInput", remarksColumnWidth);
            var remarks = BuildInputField(remarksCell);
            remarks.lineType = TMP_InputField.LineType.SingleLine;
            remarks.text = "";
            state.remarks = remarks;
        }

        itemRows.Add(state);
    }

    /// <summary>One table header row (question | score labels | remarks).</summary>
    private void BuildHeaderRow(GameObject row, string question, string s4, string s3, string s2, string s1, string remarks, bool scoreSpans)
    {
        var hlg = row.GetComponent<HorizontalLayoutGroup>() ?? row.AddComponent<HorizontalLayoutGroup>();
        ConfigureRowLayout(hlg);
        EnsureContentSizeFitter(row);

        var q = MakeCellText(row.transform, "QuestionText", TextAlignmentOptions.MidlineLeft);
        q.fontStyle = FontStyles.Bold;
        var qle = q.gameObject.AddComponent<LayoutElement>();
        qle.flexibleWidth = 1f;
        qle.minHeight = 36f;
        q.text = question;

        string[] labels = { s4, s3, s2, s1 };
        if (scoreSpans)
        {
            // Row 1: "SCORE" is centered over all four score columns,
            // matching the checkbox columns of the item rows below.
            var score = MakeCellText(row.transform, "ScoreSpan", TextAlignmentOptions.Midline);
            score.fontStyle = FontStyles.Bold;
            score.text = s4;
            var sle = score.gameObject.AddComponent<LayoutElement>();
            sle.preferredWidth = scoreColumnWidth * 4f + ConfigureRowSpacing * 3f;
            sle.minWidth = sle.preferredWidth;
            sle.minHeight = 36f;
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                var t = MakeCellText(row.transform, "Score" + (4 - i), TextAlignmentOptions.Midline);
                t.fontStyle = FontStyles.Bold;
                t.text = labels[i];
                var le = t.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = scoreColumnWidth;
                le.minWidth = scoreColumnWidth;
                le.minHeight = 36f;
            }
        }

        if (showRemarks)
        {
            var r = MakeCellText(row.transform, "RemarksText", TextAlignmentOptions.Midline);
            r.fontStyle = FontStyles.Bold;
            r.text = remarks;
            var rle = r.gameObject.AddComponent<LayoutElement>();
            rle.preferredWidth = remarksColumnWidth;
            rle.minWidth = remarksColumnWidth;
            rle.minHeight = 36f;
        }
    }

    /// <summary>
    /// Linescramble-style spawning: every cloned row is wrapped in a
    /// full-width ROW CONTAINER that owns the layout. The clone never keeps
    /// its off-screen template rect (that was the bug where all rows piled
    /// up on top of each other); it is re-anchored to stretch inside the
    /// container, and the container is stacked by formHook's Vertical
    /// Layout Group.
    /// </summary>
    private GameObject SpawnRow(GameObject template, string rowName)
    {
        // 1) Container: full width, top-stacked, height driven by its child.
        GameObject container = new GameObject(rowName + " Container", typeof(RectTransform));
        RectTransform containerRect = (RectTransform)container.transform;
        containerRect.SetParent(formHook.transform, false);
        StretchRect(containerRect);

        var containerLayout = container.AddComponent<VerticalLayoutGroup>();
        containerLayout.childControlWidth = true;
        containerLayout.childControlHeight = true;
        containerLayout.childForceExpandWidth = true;
        containerLayout.childForceExpandHeight = false;
        containerLayout.childAlignment = TextAnchor.UpperLeft;
        containerLayout.spacing = 0f;

        // 2) The row itself. If a template is wired, clone it for its styling
        //    but strip ALL children: the row's content is generated in code,
        //    so a miswired template can never inject "New Text" or bad rects.
        GameObject row;
        if (template != null)
        {
            row = Instantiate(template, containerRect);
            row.name = rowName; // drop "(Clone)"
            StretchRect(row.GetComponent<RectTransform>());
            for (int i = row.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = row.transform.GetChild(i);
                child.gameObject.SetActive(false); // layout ignores it immediately
                Destroy(child.gameObject);
            }
        }
        else
        {
            row = new GameObject(rowName, typeof(RectTransform));
            RectTransform rect = (RectTransform)row.transform;
            rect.SetParent(containerRect, false);
            StretchRect(rect);
        }
        row.SetActive(true);
        return row;
    }

    /// <summary>Force a RectTransform to stretch-fill its parent and sit at
    /// the top, discarding whatever off-screen rect the template had.</summary>
    private static void StretchRect(RectTransform rect)
    {
        if (rect == null) return;
        rect.localScale = Vector3.one;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Defensive: make sure formHook itself is a proper stacking root even
    /// if the scene setup missed a component. Requires the hook's parent to
    /// be the ScrollRect Content (per the scene-setup header).
    /// </summary>
    private void EnsureHookLayout()
    {
        RectTransform hookRect = formHook.transform as RectTransform;
        StretchRect(hookRect);

        var vlg = formHook.GetComponent<VerticalLayoutGroup>() ?? formHook.AddComponent<VerticalLayoutGroup>();
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.spacing = 8f;

        var fitter = formHook.GetComponent<ContentSizeFitter>() ?? formHook.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // Content must stretch the hook horizontally and follow its height.
        RectTransform content = hookRect.parent as RectTransform;
        if (content != null)
        {
            StretchRect(content);
            var contentVlg = content.GetComponent<VerticalLayoutGroup>();
            if (contentVlg != null)
            {
                contentVlg.childControlWidth = true;
                contentVlg.childControlHeight = true;
                contentVlg.childForceExpandWidth = true;
                contentVlg.childForceExpandHeight = false;
                contentVlg.padding = new RectOffset(
                    Mathf.RoundToInt(sidePadding), Mathf.RoundToInt(sidePadding), 4, 4);
            }
            var contentFitter = content.GetComponent<ContentSizeFitter>();
            if (contentFitter != null) contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }

    /// <summary>A plain text row (title, instructions, section divider,
    /// "Nothing follows"). The template only styles the row background.</summary>
    private void AddText(GameObject template, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        GameObject row = SpawnRow(template, "Text Block");
        EnsureContentSizeFitter(row);
        var tmp = row.GetComponent<TextMeshProUGUI>() ?? row.AddComponent<TextMeshProUGUI>();
        tmp.font = SharedFont();
        tmp.fontSize = baseFontSize;
        tmp.color = Color.black;
        tmp.enableWordWrapping = true;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        tmp.text = value;
    }

    // ------------------------------------------------------------------
    // Code-generated cells (templates contribute styling only)
    // ------------------------------------------------------------------

    /// <summary>Column gap used by every row so header columns line up
    /// exactly with the item-row checkbox columns.</summary>
    private const float ConfigureRowSpacing = 6f;

    private static void ConfigureRowLayout(HorizontalLayoutGroup hlg)
    {
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = true;
        hlg.childAlignment = TextAnchor.UpperLeft;
        hlg.spacing = ConfigureRowSpacing;
        hlg.padding = new RectOffset(8, 8, 6, 6);
    }

    private static void EnsureContentSizeFitter(GameObject row)
    {
        var fitter = row.GetComponent<ContentSizeFitter>() ?? row.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    /// <summary>Fixed-width cell for a score number, toggle, or remarks input.</summary>
    private static GameObject MakeCell(Transform parent, string name, float width)
    {
        GameObject cell = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)cell.transform;
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        var le = cell.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        le.minWidth = width > 0f ? width : -1f; // fixed cells can never shrink
        le.minHeight = 42f;
        return cell;
    }

    private TextMeshProUGUI MakeCellText(Transform parent, string name, TextAlignmentOptions alignment)
    {
        GameObject cell = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)cell.transform;
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        var tmp = cell.AddComponent<TextMeshProUGUI>();
        tmp.font = SharedFont();
        tmp.fontSize = baseFontSize;
        tmp.color = Color.black;
        tmp.alignment = alignment;
        tmp.enableWordWrapping = true;
        tmp.raycastTarget = false;
        return tmp;
    }

    private TextMeshProUGUI AddTmpChild(RectTransform parent, string name, FontStyles style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = SharedFont();
        tmp.fontStyle = style;
        tmp.fontSize = baseFontSize;
        tmp.color = Color.black;
        tmp.enableWordWrapping = true;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        return tmp;
    }

    /// <summary>A small check box: cell > Box (background) > Checkmark.</summary>
    private Toggle BuildToggle(GameObject cell)
    {
        var rect = (RectTransform)cell.transform;
        var toggle = cell.AddComponent<Toggle>();

        GameObject box = new GameObject("Box", typeof(RectTransform));
        var boxRect = (RectTransform)box.transform;
        boxRect.SetParent(rect, false);
        boxRect.localScale = Vector3.one;
        boxRect.anchorMin = boxRect.anchorMax = boxRect.pivot = new Vector2(0.5f, 0.5f);
        boxRect.sizeDelta = new Vector2(26f, 26f);
        var bg = box.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = new Color(1f, 1f, 1f, 1f);

        GameObject check = new GameObject("Checkmark", typeof(RectTransform));
        var checkRect = (RectTransform)check.transform;
        checkRect.SetParent(boxRect, false);
        checkRect.localScale = Vector3.one;
        checkRect.anchorMin = new Vector2(0.15f, 0.15f);
        checkRect.anchorMax = new Vector2(0.85f, 0.85f);
        checkRect.offsetMin = Vector2.zero;
        checkRect.offsetMax = Vector2.zero;
        var checkImg = check.AddComponent<Image>();
        checkImg.sprite = WhiteSprite();
        checkImg.color = new Color32(80, 200, 100, 255);

        toggle.targetGraphic = bg;
        toggle.graphic = checkImg;
        toggle.isOn = false;
        return toggle;
    }

    /// <summary>A single-line TMP input field built entirely in code:
    /// cell > Image background + Text Area (RectMask2D) > Placeholder + Text.</summary>
    private TMP_InputField BuildInputField(GameObject cell)
    {
        var rect = (RectTransform)cell.transform;
        var bg = cell.AddComponent<Image>();
        bg.sprite = WhiteSprite();
        bg.color = new Color(0.95f, 0.95f, 0.95f, 1f);

        GameObject area = new GameObject("Text Area", typeof(RectTransform));
        var areaRect = (RectTransform)area.transform;
        areaRect.SetParent(rect, false);
        areaRect.localScale = Vector3.one;
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(8f, 4f);
        areaRect.offsetMax = new Vector2(-8f, -4f);
        area.AddComponent<RectMask2D>();

        var placeholder = AddTmpChild(areaRect, "Placeholder", FontStyles.Italic);
        placeholder.color = new Color(0f, 0f, 0f, 0.35f);
        placeholder.text = "...";

        var text = AddTmpChild(areaRect, "Text", FontStyles.Normal);
        text.color = Color.black; // input text is always black, never white
        text.text = "";

        var input = cell.AddComponent<TMP_InputField>();
        input.textComponent = text;
        input.placeholder = placeholder;
        input.textViewport = areaRect;
        input.targetGraphic = bg;
        input.text = "";
        return input;
    }

    private Sprite whiteSprite;
    private Sprite WhiteSprite()
    {
        if (whiteSprite == null)
        {
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
            whiteSprite.name = "QuestionnaireWhite";
        }
        return whiteSprite;
    }

    private TMP_FontAsset sharedFont;
    /// <summary>Reuses a font already in the scene (validation/thanks text)
    /// so generated rows match the rest of the UI; falls back to the TMP
    /// default asset.</summary>
    private TMP_FontAsset SharedFont()
    {
        if (sharedFont == null)
        {
            if (validationText != null) sharedFont = validationText.font;
            if (sharedFont == null && thanksText != null) sharedFont = thanksText.font;
            if (sharedFont == null) sharedFont = TMP_Settings.defaultFontAsset;
        }
        return sharedFont;
    }

    // ------------------------------------------------------------------
    // Validation + saving
    // ------------------------------------------------------------------

    private void OnSubmitClicked()
    {
        Debug.Log("[Questionnaire] Submit clicked");
        if (submitted) { Debug.Log("[Questionnaire] Ignored: already submitted"); return; }
        ClearErrorText();

        Debug.Log($"[Questionnaire] State: fields={fieldRows.Count} rows={itemRows.Count} " +
                  $"consent={(consentToggle != null ? consentToggle.isOn.ToString() : "null")} " +
                  $"panel={(questionnairePanel != null ? "ok" : "NULL")} " +
                  $"confirm={(confirmationPanel != null ? confirmationPanel.name : "NULL")} " +
                  $"thanks={(thanksText != null ? thanksText.name : "NULL")}");

        string error;
        try { error = Validate(); }
        catch (Exception e)
        {
            Debug.LogError($"[QuestionnaireController] Validation crashed: {e}");
            error = "Something went wrong while checking your answers. Please tell your evaluator.";
        }
        Debug.Log($"[Questionnaire] Validate() returned: '{error}'");

        // Scroll back to the top so the player sees the error banner
        // even if they are scrolled deep inside the table.
        if (formScrollRect != null) formScrollRect.verticalNormalizedPosition = 1f;

        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogWarning($"[Questionnaire] Submit blocked: {error}");
            // The error MUST be visible inside the active questionnaire panel.
            // ThanksText lives in the (still inactive) confirmation panel, so
            // writing the error there made Submit look like a dead button.
            var target = EnsureVisibleErrorText();
            if (target != null)
            {
                target.color = new Color(0.75f, 0.1f, 0.1f);
                target.text = error;
                var tr = target.transform as RectTransform;
                Debug.Log($"[Questionnaire] Error display '{target.name}': parent={target.transform.parent.name} active={target.gameObject.activeInHierarchy} size={(tr != null ? tr.rect.ToString() : "none")}");
                if (formHook != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(formHook.transform as RectTransform);
                if (tr != null && target == errorBanner)
                    Debug.Log($"[Questionnaire] After rebuild: size={tr.rect} pos={tr.anchoredPosition}");
            }
            else Debug.LogError("[Questionnaire] NO error display target available!");
            Debug.LogWarning($"[QuestionnaireController] Submit blocked: {error}");
            return;
        }

        string csvErrorOrPath = SaveResponse();
        Debug.Log($"[Questionnaire] Saved. csvPath='{csvErrorOrPath}' -> showing confirmation");

        submitted = true;
        if (errorText != null) errorText.text = "";
        if (validationText != null) validationText.text = "";
        SwapFormForConfirmation(true);
        if (thanksText != null)
        {
            thanksText.color = Color.black;
            thanksText.text = string.IsNullOrEmpty(csvErrorOrPath)
                ? "Thank you! (Saving the file failed, please tell your evaluator.)"
                : thanksMessage;
        }
    }

    /// <summary>Show the confirmation (hide the form) or bring the form
    /// back. If the confirmation panel is a CHILD of the questionnaire
    /// panel we must not deactivate the whole parent, because that would
    /// hide the confirmation too; instead we only hide the form pieces
    /// (scroll view + submit button) behind it.</summary>
    private void SwapFormForConfirmation(bool toConfirmation)
    {
        bool confirmIsChild = confirmationPanel != null && questionnairePanel != null &&
                              confirmationPanel.transform.IsChildOf(questionnairePanel.transform);
        Debug.Log($"[Questionnaire] SwapFormForConfirmation({toConfirmation}) confirmIsChild={confirmIsChild}");

        if (toConfirmation)
        {
            if (confirmIsChild)
            {
                if (formScrollRect != null) formScrollRect.gameObject.SetActive(false);
                if (submitButton != null) submitButton.gameObject.SetActive(false);
                questionnairePanel.SetActive(true);
            }
            else if (questionnairePanel != null)
                questionnairePanel.SetActive(false);
            if (confirmationPanel != null) confirmationPanel.SetActive(true);
        }
        else
        {
            if (confirmationPanel != null) confirmationPanel.SetActive(false);
            if (confirmIsChild)
            {
                if (formScrollRect != null) formScrollRect.gameObject.SetActive(true);
                if (submitButton != null) submitButton.gameObject.SetActive(true);
            }
            else if (questionnairePanel != null)
                questionnairePanel.SetActive(false);
        }
    }

    /// <summary>An error line the player can always see. A code-built
    /// label pinned to the panel root, OUTSIDE the ScrollRect and the
    /// Content's layout groups, so no layout quirk can hide or collapse
    /// it. Scene ValidationText is deliberately NOT used: it lives in the
    /// scroll Content where the Vertical Layout Group squeezes it to
    /// near-zero height, making the error invisible.</summary>
    private TextMeshProUGUI EnsureVisibleErrorText()
    {
        // The user's own wired text component takes priority: they position
        // and style it, we only write into it. The auto label is a fallback;
        // it must NOT be used if the wired text exists but is hidden -
        // hiding should not fall back to a duplicate message.
        if (validationText != null) return validationText;
        if (errorText == null)
        {
            Transform root = questionnairePanel != null ? questionnairePanel.transform : transform;
            GameObject go = new GameObject("ValidationText (auto)", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(root, false);
            rect.anchorMin = new Vector2(0.02f, 0.88f);
            rect.anchorMax = new Vector2(0.98f, 0.97f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var bg = go.GetComponent<Image>();
            bg.color = new Color(1f, 0.92f, 0.92f); // pale red pill
            errorText = go.GetComponentInChildren<TextMeshProUGUI>();
            if (errorText == null)
            {
                GameObject child = new GameObject("Text", typeof(RectTransform));
                var crect = (RectTransform)child.transform;
                crect.SetParent(go.transform, false);
                crect.anchorMin = Vector2.zero; crect.anchorMax = Vector2.one;
                crect.offsetMin = new Vector2(6f, 2f); crect.offsetMax = new Vector2(-6f, -2f);
                errorText = child.AddComponent<TextMeshProUGUI>();
                errorText.enableWordWrapping = true;
                errorText.alignment = TextAlignmentOptions.Midline;
            }
            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            errorText.font = SharedFont();
            errorText.fontSize = baseFontSize + 1f;
            errorText.color = new Color(0.75f, 0.1f, 0.1f);
            errorText.raycastTarget = false;
        }
        Debug.Log($"[Questionnaire] Using pinned ValidationText (auto)");
        return errorText;
    }

    private TextMeshProUGUI errorText;
    private TextMeshProUGUI errorBanner;

    private void ClearErrorText()
    {
        if (errorBanner != null) errorBanner.text = "";
        if (errorText != null) errorText.text = "";
    }

    private string Validate()
    {
        Debug.Log($"[Questionnaire] Validate start: fieldRows={fieldRows.Count} itemRows={itemRows.Count}");
        foreach (var row in fieldRows)
        {
            string value = row.input != null ? row.input.text.Trim() : "";
            if (row.def.required && string.IsNullOrEmpty(value))
                return "Please fill in: " + row.def.label;
            if (!string.IsNullOrEmpty(value) && row.def.isEmail && !LooksLikeEmail(value))
                return "The email address does not look right. Please check it.";
        }
        Debug.Log("[Questionnaire] Validate: fields ok");

        if (consentToggle != null && !consentToggle.isOn)
            return "Please tick the Data Privacy Act (RA 10173) consent checkbox.";
        Debug.Log("[Questionnaire] Validate: consent ok");

        for (int i = 0; i < itemRows.Count; i++)
        {
            var row = itemRows[i];
            if (row == null) continue;
            bool answered = false;
            foreach (var t in row.toggles)
            {
                if (t != null && t.isOn) { answered = true; break; }
            }
            if (!answered)
            {
                string label = row.prompt.Contains("\n") ? row.prompt.Split('\n')[0] : row.prompt;
                return $"Please answer row {i}: {label}";
            }
        }
        Debug.Log("[Questionnaire] Validate: all rows ok");
        return "";
    }

    private void ClearValidation()
    {
        if (validationText != null) validationText.text = "";
    }

    private static bool LooksLikeEmail(string email)
    {
        int at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1 && !email.Contains(" ");
    }

    /// <summary>
    /// Appends one CSV row to questionnaire_responses.csv (header written on
    /// file creation) and writes the full submission as a timestamped JSON
    /// file, both under Application.persistentDataPath. Returns the CSV path,
    /// or "" on failure (never throws).
    /// </summary>
    private string SaveResponse()
    {
        Debug.Log($"[Questionnaire] SaveResponse entered, dir={Application.persistentDataPath}");
        try
        {
            string dir = Application.persistentDataPath;
            string studentId = ResolveStudentId();

            var record = new QuestionnaireSubmission
            {
                submittedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                gameCompleted = GameCompletionState.HasCompletedGame(),
            };
            var csv = new List<string> { SchemaVersion, studentId, record.submittedUtc };

            foreach (var row in fieldRows)
            {
                string value = row.input != null ? row.input.text.Trim() : "";
                record.fields.Add(new FieldEntry { label = row.def.label, answer = value });
                csv.Add(value);
            }

            csv.Add(consentToggle != null && consentToggle.isOn ? "Yes" : "No");

            for (int i = 0; i < itemRows.Count; i++)
            {
                var row = itemRows[i];
                int index = -1;
                for (int s = 0; s < row.toggles.Length; s++)
                {
                    if (row.toggles[s] != null && row.toggles[s].isOn) { index = s; break; }
                }
                string scoreValue = index >= 0 ? (4 - index).ToString(CultureInfo.InvariantCulture) : "";
                string remarks = row.remarks != null ? row.remarks.text.Trim() : "";

                record.answers.Add(new AnswerEntry
                {
                    questionNumber = i,
                    section = row.section,
                    prompt = row.prompt,
                    score = scoreValue,
                    remarks = remarks,
                });
                csv.Add(scoreValue);
                if (showRemarks) csv.Add(remarks);
            }

            record.studentId = studentId;

            string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string jsonPath = Path.Combine(dir, $"questionnaire_response_{stamp}.json");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(record, true), new UTF8Encoding(false));

            // Canonical per-student questionnaire CSV, same convention as the
            // log data (pyquest_log_<studentID>.csv): one fixed filename per
            // student, fully overwritten each submission, so a student can
            // never accumulate duplicate rows and the ID is always visible.
            string safeId = SanitizeFieldName(studentId);
            string perStudentPath = Path.Combine(dir, $"pyquest_log_{safeId}_questionnaire.csv");
            File.WriteAllText(perStudentPath, CsvLine(HeaderCells()) + "\n" + CsvLine(csv) + "\n", new UTF8Encoding(false));

            // Shared aggregate. Appends only when the on-disk header matches
            // the current build's schema, so a stale build can never silently
            // shift columns and corrupt big merges.
            string aggregatePath = Path.Combine(dir, "questionnaire_responses.csv");
            string expectedHeader = CsvLine(HeaderCells());
            if (!File.Exists(aggregatePath))
            {
                File.AppendAllText(aggregatePath, expectedHeader + "\n", new UTF8Encoding(false));
            }
            else
            {
                string existingHeader = ReadFirstLine(aggregatePath);
                if (existingHeader != expectedHeader)
                {
                    Debug.LogError($"[QuestionnaireController] Aggregate CSV header mismatch (starts '{existingHeader.Substring(0, Math.Min(60, existingHeader.Length))}...'). Row NOT appended to {aggregatePath}; it is saved in {perStudentPath} instead. Delete the old aggregate CSV before collecting with this build.");
                    Debug.Log($"[QuestionnaireController] Saved response to {perStudentPath} and {jsonPath} (aggregate skipped)");
                    return perStudentPath;
                }
            }
            File.AppendAllText(aggregatePath, CsvLine(csv) + "\n", new UTF8Encoding(false));

            Debug.Log($"[QuestionnaireController] Saved response to {aggregatePath}, {perStudentPath} and {jsonPath}");
            return aggregatePath;
        }
        catch (Exception e)
        {
            Debug.LogError($"[QuestionnaireController] Failed to save response: {e}");
            return "";
        }
    }

    private const string SchemaVersion = "1";

    /// Builds the header row matching exactly what SaveResponse appends.
    private List<string> HeaderCells()
    {
        var header = new List<string> { "SchemaVersion", "StudentId", "SubmittedUtc" };
        foreach (var row in fieldRows) header.Add(row.def.label);
        header.Add("DataPrivacyConsent");
        for (int i = 0; i < itemRows.Count; i++)
        {
            header.Add($"Q{i + 1}_Score");
            if (showRemarks) header.Add($"Q{i + 1}_Remarks");
        }
        return header;
    }

    /// <summary>
    /// The respondent's ID as set by the name-entry welcome screen, shared
    /// with the log data (StudentLogManager / PlayerPrefs). Questionnaire
    /// rows and log rows can therefore be joined on StudentId when the
    /// files are collected.
    /// </summary>
    private string ResolveStudentId()
    {
        if (StudentLogManager.Instance != null && !string.IsNullOrWhiteSpace(StudentLogManager.Instance.studentID))
            return StudentLogManager.Instance.studentID.Trim();
        string prefs = PlayerPrefs.GetString("PyQuest_StudentID", "");
        return string.IsNullOrWhiteSpace(prefs) ? "unnamed_student" : prefs.Trim();
    }

    private static string ReadFirstLine(string path)
    {
        using (var reader = new StreamReader(path))
        {
            return reader.ReadLine() ?? "";
        }
    }

    private static string SanitizeFieldName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "unnamed";
        var sb = new System.Text.StringBuilder();
        foreach (char c in s.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        string result = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(result) ? "unnamed" : result;
    }

    private static string CsvLine(List<string> cells)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cells.Count; i++)
        {
            if (i > 0) sb.Append(',');
            string v = cells[i] ?? "";
            if (v.Contains(",") || v.Contains("\"") || v.Contains("\n") || v.Contains("\r"))
                sb.Append('"').Append(v.Replace("\"", "\"\"")).Append('"');
            else
                sb.Append(v);
        }
        return sb.ToString();
    }

    [Serializable]
    private class QuestionnaireSubmission
    {
        public string studentId;
        public string submittedUtc;
        public bool gameCompleted;
        public List<FieldEntry> fields = new List<FieldEntry>();
        public List<AnswerEntry> answers = new List<AnswerEntry>();
    }

    [Serializable]
    private class FieldEntry
    {
        public string label;
        public string answer;
    }

    [Serializable]
    private class AnswerEntry
    {
        public int questionNumber;
        public string section;
        public string prompt;
        public string score;
        public string remarks;
    }

    private void OnConfirmationContinueClicked()
    {
        // The questionnaire is done: go straight back to gameplay (NOT to
        // the epilogue end screen). Restore the same things the end screen's
        // Continue previously restored.
        SaveRestrictionEnforcer.Instance?.RemoveBlocker("questionnaire");
        SaveRestrictionEnforcer.Instance?.RemoveBlocker("epilogue_end_screen");

        if (questionnairePanel != null) questionnairePanel.SetActive(false);
        if (confirmationPanel != null) confirmationPanel.SetActive(false);

        // Swap epilogue music for the restored scene music.
        WorldRestorationController.Instance?.RequestRestoredMusic();

        // Re-enable the player and the HUD.
        var endScreen = EpilogueEndScreenController.Instance;
        if (endScreen != null) endScreen.HideEndScreenPermanently();
        var player = endScreen != null ? endScreen.GetPlayer() : GameObject.FindGameObjectWithTag("Player");
        var pm = player != null ? player.GetComponent<PlayerMovement>() : null;
        var pmDisabled = player != null ? player.GetComponentInChildren<PlayerMovement>(true) : null;
        var movement = pm != null ? pm : pmDisabled;
        if (movement != null) movement.enabled = true;

        HUDController.Instance?.SetVisible(true);
        Time.timeScale = 1f;
    }
}
