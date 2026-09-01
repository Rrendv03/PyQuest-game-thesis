using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Read-only static lesson reference tablet. Separate GameObject from
/// TabletMissionObject and RuneCrystal; shows a hardcoded definition,
/// uses, do's, and don'ts block for a given knowledge component.
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

    private string currentKC;

    private class LessonContent
    {
        public string title;
        public string definition;
        public string uses;
        public string dos;
        public string donts;
    }

    private static readonly Dictionary<string, LessonContent> Content = new Dictionary<string, LessonContent>
    {
        ["print_statements"] = new LessonContent
        {
            title = "Print Statements",
            definition = "A print statement is a command that displays text or values on the screen. In Python, it is written as print(\"your message\") or print(value).",
            uses = "Showing output to the user, checking what a variable currently holds, and confirming a program is running as expected.",
            dos = "Do use quotation marks around plain text. Do use a comma or a plus sign to combine multiple items inside one print statement.",
            donts = "Do not forget the closing parenthesis. Do not mix text and numbers with a plus sign without converting the number to text first."
        },
        ["variables"] = new LessonContent
        {
            title = "Variables",
            definition = "A variable is a named container that stores a value in memory so it can be reused later. In Python, a variable is created the moment a value is assigned to it, for example score = 10.",
            uses = "Storing player input, keeping track of a running total, and holding a value that changes as a program runs.",
            dos = "Do give variables clear, descriptive names. Do assign a value before using the variable anywhere else in the program.",
            donts = "Do not start a variable name with a number. Do not use spaces inside a variable name."
        },
        ["input_handling"] = new LessonContent
        {
            title = "Input Handling",
            definition = "Input handling is how a program receives information typed by the user while it is running. In Python, this is done with the input() function, for example name = input(\"Enter your name: \").",
            uses = "Asking the player for their name, collecting a number for a calculation, and pausing a program until the user responds.",
            dos = "Do store the result of input() in a variable so it can be used later. Do convert the input to a number with int() or float() before doing math with it.",
            donts = "Do not assume input() always returns a number. It always returns text, even if the user types digits."
        },
        ["conditionals"] = new LessonContent
        {
            title = "Conditionals",
            definition = "A conditional is a structure that runs different code depending on whether a condition is true or false, using if, elif, and else.",
            uses = "Checking whether a player's answer is correct, deciding which path a story takes, and validating input before using it.",
            dos = "Do use two equals signs (==) to compare values. Do indent the code inside each if, elif, or else block.",
            donts = "Do not use a single equals sign (=) when checking equality; that is assignment, not comparison. Do not forget the colon at the end of an if line."
        },
        ["loops"] = new LessonContent
        {
            title = "Loops",
            definition = "A loop repeats a block of code multiple times. Python has two main kinds: a for loop, which repeats a set number of times or over a collection, and a while loop, which repeats as long as a condition stays true.",
            uses = "Repeating an action for every item in a list, retrying a puzzle until it is solved correctly, and counting down or up automatically.",
            dos = "Do make sure a while loop's condition will eventually become false. Do use range() for a for loop that just needs to repeat a fixed number of times.",
            donts = "Do not forget to update the variable a while loop depends on, or the loop will never end. Do not confuse for and while when a fixed count is already known."
        },
        ["basic_operations"] = new LessonContent
        {
            title = "Basic Operations",
            definition = "Basic operations are the arithmetic symbols Python uses to calculate values: + for addition, - for subtraction, * for multiplication, / for division, and % for the remainder after division.",
            uses = "Calculating a score, computing a total from multiple values, and checking whether a number is even or odd using %.",
            dos = "Do use parentheses to control the order operations happen in. Do remember that / always returns a decimal value in Python.",
            donts = "Do not confuse = (assignment) with == (comparison). Do not divide by a variable without checking it cannot be zero."
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

        if (titleText != null) titleText.text = lesson.title;
        if (bodyText != null)
        {
            bodyText.text =
                $"<b>Definition</b>\n{lesson.definition}\n\n" +
                $"<b>Uses</b>\n{lesson.uses}\n\n" +
                $"<b>Do's</b>\n{lesson.dos}\n\n" +
                $"<b>Don'ts</b>\n{lesson.donts}";
        }

        if (panelRoot != null) panelRoot.SetActive(true);

        _pendingSanctumID = sanctumID;
    }

    private string _pendingSanctumID;

    public void CloseLesson()
    {
        if (panelRoot != null) panelRoot.SetActive(false);

        if (!string.IsNullOrEmpty(currentKC))
        {
            StudentLogManager.Instance?.LogLessonTabletViewed(_pendingSanctumID, currentKC);
        }

        currentKC = null;
        _pendingSanctumID = null;
    }
}