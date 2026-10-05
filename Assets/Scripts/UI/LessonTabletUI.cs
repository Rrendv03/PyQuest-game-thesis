using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Read-only static lesson reference tablet. Separate GameObject from
/// TabletMissionObject and RuneCrystal; shows a compact cheat sheet for a
/// knowledge component in a two-column layout: the left column teaches the
/// concept (definition, SYNTAX, WHAT IT DOES) and the right column shows the
/// pitfalls (COMMON MISTAKES, CORRECT FORM, REMEMBER, Next pointer). The
/// right column is created at runtime by cloning bodyText, so no prefab
/// wiring is needed; set useTwoColumns = false to fall back to one column.
/// Deliberately teaches the concept itself — it never spells out which
/// puzzle formats or errors the player is about to face.
/// No puzzle, no XP, no progression side effects. Purely informational.
/// </summary>
public class LessonTabletUI : MonoBehaviour
{
    public static LessonTabletUI Instance { get; private set; }

    [Header("Panel")]
    public GameObject panelRoot;
    public TMP_Text titleText;
    public TMP_Text bodyText;
    public Button closeButton;

    [Header("Two-column body layout")]
    [Tooltip("Split the lesson body into two side-by-side columns. The right column is created at runtime by cloning bodyText; no prefab changes are needed. Once created, this only controls whether the right column is filled — set it before entering Play mode to get a single-column layout.")]
    public bool useTwoColumns = true;
    [Tooltip("Horizontal gap between the two columns, in canvas units.")]
    public float columnGap = 24f;

    private TMP_Text _bodyRight;
    private bool _columnsReady;
    private string currentKC;

    [Header("HUD to hide while tablet is open")]
    public GameObject hudCanvas;

    private class LessonContent
    {
        public string title;
        public string definition;
        public string syntax;
        public string whatItDoes;
        public string mistakes;
        public string correctForm;
        public string remember;
        public string nextUp;   // optional "? Next:" pointer line
    }

    private static readonly Dictionary<string, LessonContent> Content = new Dictionary<string, LessonContent>
    {
        ["print_statements"] = new LessonContent
        {
            title = "Print Statements",
            definition = "Displays output to the screen using print().",
            syntax =
                "  print(\"Hello\")\n" +
                "  print(variable)\n" +
                "  print(\"Label:\", value)",
            whatItDoes = "  Sends text or a value to the console; runs once per call.",
            mistakes =
                "  ? print \"Hello\" ? missing parentheses (Python 2 style)\n" +
                "  ? print(name + age) ? cannot add string and int directly",
            correctForm = "  ? print(\"Age:\", age)",
            remember = "  If nothing appears, check that print() is not inside an unexecuted block (wrong indentation or failed condition).",
            nextUp = "? Next: Variables let you store values to print later."
        },
        ["variables"] = new LessonContent
        {
            title = "Variables",
            definition = "A named container that stores a value for later use.",
            syntax =
                "  score = 100\n" +
                "  name = \"Hero\"\n" +
                "  is_ready = True",
            whatItDoes = "  Reserves memory and labels it; the value can change.",
            mistakes =
                "  ? 1score = 100 ? cannot start with a number\n" +
                "  ? my score = 100 ? spaces are not allowed in names",
            correctForm = "  ? my_score = 100",
            remember = "  Use the variable before you try to print or calculate with it, or you will get a NameError.",
            nextUp = "? Next: Input lets the user set the variable's value."
        },
        ["input_handling"] = new LessonContent
        {
            title = "Input Handling",
            definition = "Reads text typed by the user while the program runs.",
            syntax =
                "  name = input(\"Enter your name: \")\n" +
                "  age  = int(input(\"Enter your age: \"))",
            whatItDoes = "  Pauses execution, waits for the user, returns a string.",
            mistakes =
                "  ? age = input() ? result is text, not a number\n" +
                "  ? total = age + 5 ? TypeError: str + int",
            correctForm = "  ? age = int(input(\"Age: \"))",
            remember = "  input() always returns a string. Convert with int() or float() before doing any arithmetic.\n" +
                "  In PyQuest, input() always gives you text — we print, join, and compare it. Converting it to a number is not covered.",
            nextUp = "? Next: Conditionals let you branch based on that input."
        },
        ["conditionals"] = new LessonContent
        {
            title = "Conditionals",
            definition = "Runs different code depending on whether a condition is true.",
            syntax =
                "  if score > 80:\n" +
                "      print(\"Pass\")\n" +
                "  elif score > 50:\n" +
                "      print(\"Close\")\n" +
                "  else:\n" +
                "      print(\"Fail\")",
            whatItDoes = "  Evaluates the condition; executes only the matching block.",
            mistakes =
                "  ? if score = 80: ? assignment, not comparison\n" +
                "  ? if score > 80 ? missing colon",
            correctForm = "  ? if score == 80:",
            remember = "  Every if, elif, and else line ends with a colon. The body must be indented by exactly 4 spaces.",
            nextUp = "? Next: Loops let you repeat that check automatically."
        },
        ["loops"] = new LessonContent
        {
            title = "Loops",
            definition = "Repeats a block of code multiple times automatically.",
            syntax =
                "  for i in range(5):   # repeats 5 times (0 to 4)\n" +
                "      print(i)\n" +
                "\n" +
                "  count = 0\n" +
                "  while count < 5:\n" +
                "      count += 1",
            whatItDoes =
                "  for: iterates a fixed number of times or over a sequence.\n" +
                "  while: repeats as long as a condition stays True.",
            mistakes =
                "  ? while True: ? infinite loop if nothing changes inside\n" +
                "  ? for i in 5: ? must use range(5), not a bare number",
            correctForm = "  ? for i in range(5):",
            remember = "  A while loop must change the variable it checks, or it never stops. Use for when the count is already known.",
            nextUp = "? Next: Basic operations compute the values you count and branch on."
        },
        ["basic_operations"] = new LessonContent
        {
            title = "Basic Operations",
            definition = "Arithmetic symbols Python uses to compute values.",
            syntax =
                "  result = 10 + 3   # 13  addition\n" +
                "  result = 10 - 3   # 7   subtraction\n" +
                "  result = 10 * 3   # 30  multiplication\n" +
                "  result = 10 / 3   # 3.33 division (always float)\n" +
                "  result = 10 % 3   # 1   remainder",
            whatItDoes = "  Calculates a new value; follow standard math precedence.",
            mistakes =
                "  ? \"5\" + 2 ? TypeError: str and int\n" +
                "  ? score/0 ? ZeroDivisionError",
            correctForm = "  ? int(\"5\") + 2 ? 7",
            remember = "  Use parentheses to force order. Check for zero before dividing if the divisor comes from a variable.",
            nextUp = ""
        }
    };

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (panelRoot != null) panelRoot.SetActive(false);
        if (closeButton != null) closeButton.onClick.AddListener(CloseLesson);
    }

    public void ShowLesson(string knowledgeComponentID, string sanctumID)
    {
        if (string.IsNullOrEmpty(knowledgeComponentID) || !Content.TryGetValue(knowledgeComponentID, out LessonContent lesson))
        {
            Debug.LogWarning($"[LessonTabletUI] No content found for knowledge component '{knowledgeComponentID}'.");
            return;
        }

        currentKC = knowledgeComponentID;
        StudentLogManager.Instance?.StartLessonTabletTracking(knowledgeComponentID);

        if (!string.IsNullOrEmpty(sanctumID))
            TabletReadTracker.MarkLessonRead(sanctumID);

        // Activate first so the panel's layout is computed before the columns
        // capture the body's real width on the very first open.
        if (panelRoot != null) panelRoot.SetActive(true);
        EnsureColumns();

        if (titleText != null) titleText.text = lesson.title;
        if (bodyText != null)
        {
            // Left column teaches the concept; right column shows the pitfalls.
            string leftColumn =
                lesson.definition + "\n\n" +
                "<b>SYNTAX</b>\n<noparse>" + lesson.syntax + "</noparse>\n\n" +
                "<b>WHAT IT DOES</b>\n" + lesson.whatItDoes;

            string rightColumn =
                "<b>COMMON MISTAKES</b>\n<noparse>" + lesson.mistakes + "</noparse>\n\n" +
                "<b>CORRECT FORM</b>\n<noparse>" + lesson.correctForm + "</noparse>\n\n" +
                "<b>REMEMBER</b>\n" + lesson.remember;
            if (!string.IsNullOrEmpty(lesson.nextUp))
                rightColumn += "\n\n" + lesson.nextUp;

            if (useTwoColumns && _bodyRight != null)
            {
                // <noparse> keeps TMP from eating '<' in code lines like "while count < 5:".
                bodyText.text = leftColumn;
                _bodyRight.text = rightColumn;
            }
            else
            {
                bodyText.text = leftColumn + "\n\n" + rightColumn;
            }
        }

        if (HUDController.Instance != null)
            HUDController.Instance.SetVisible(false);

        _pendingSanctumID = sanctumID;
    }

    /// <summary>
    /// One-time setup: clones bodyText into a second column and re-geometries
    /// both texts so they split the body's original rect into left/right
    /// halves separated by columnGap. Works regardless of the body's anchor
    /// setup; both texts are also taken out of any parent layout group's
    /// control so the manual column geometry survives layout rebuilds.
    /// </summary>
    private void EnsureColumns()
    {
        if (!useTwoColumns || bodyText == null || _columnsReady) return;

        // Force a layout pass now that the panel is active, then read the
        // body's real width. If the rect is not laid out yet, retry on the
        // next ShowLesson call (the single-column path fills bodyText meanwhile).
        if (panelRoot != null && panelRoot.transform is RectTransform panelRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        Canvas.ForceUpdateCanvases();

        RectTransform rt = bodyText.rectTransform;
        float fullWidth = rt.rect.width;
        if (fullWidth <= 1f) return;

        float halfWidth = fullWidth * 0.5f - columnGap * 0.5f;
        Vector2 originalPos = rt.anchoredPosition;
        float pivotX = rt.pivot.x;

        // Detach from layout control before cloning so the clone inherits it,
        // and drop any horizontal auto-fit that would fight the column width.
        LayoutElement layoutElement = bodyText.GetComponent<LayoutElement>();
        if (layoutElement == null) layoutElement = bodyText.gameObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;
        ContentSizeFitter fitter = bodyText.GetComponent<ContentSizeFitter>();
        if (fitter != null && fitter.horizontalFit != ContentSizeFitter.FitMode.Unconstrained)
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        GameObject rightGo = Instantiate(bodyText.gameObject, rt.parent);
        rightGo.name = "BodyRight";
        _bodyRight = rightGo.GetComponent<TMP_Text>();
        LayoutElement rightLayoutElement = rightGo.GetComponent<LayoutElement>();
        if (rightLayoutElement == null) rightLayoutElement = rightGo.AddComponent<LayoutElement>();
        rightLayoutElement.ignoreLayout = true;

        // Both columns keep the body's left/right edges: the left column
        // keeps the original left edge, the right column starts at the
        // original center + half the gap. The formulas hold for point and
        // stretched anchors alike because both work in anchored space.
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, halfWidth);
        _bodyRight.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, halfWidth);

        rt.anchoredPosition = new Vector2(originalPos.x - pivotX * (fullWidth - halfWidth), originalPos.y);
        _bodyRight.rectTransform.anchoredPosition =
            new Vector2(originalPos.x + (1f - pivotX) * (fullWidth * 0.5f + columnGap * 0.5f), originalPos.y);

        _columnsReady = true;
    }

    private string _pendingSanctumID;

    public void CloseLesson()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (HUDController.Instance != null)
            HUDController.Instance.SetVisible(true);

        if (!string.IsNullOrEmpty(currentKC))
        {
            StudentLogManager.Instance?.LogLessonTabletViewed(_pendingSanctumID, currentKC);
        }

        currentKC = null;
        _pendingSanctumID = null;
    }
}