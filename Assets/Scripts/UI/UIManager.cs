using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Global UI coordinator for non-HUD UI: screen fades and transient
/// notification toasts. HUDController remains separately responsible
/// for the mobile movement/pause HUD.
///
/// DontDestroyOnLoad singleton. Requires a Canvas with:
/// - fadeImage: a full-screen Image, alpha driven at runtime
/// - notificationRoot: a GameObject holding notificationText
/// - notificationText: a Text component for toast messages
/// </summary>
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Fade Overlay")]
    public Image fadeImage;

    [Header("Notification Toast")]
    public GameObject notificationRoot;
    public Text notificationText;

    private Coroutine notificationRoutine;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (notificationRoot != null)
            notificationRoot.SetActive(false);
    }

    #region Fade

    public IEnumerator FadeToBlack(float duration)
    {
        yield return StartCoroutine(Fade(0f, 1f, duration));
    }

    public IEnumerator FadeFromBlack(float duration)
    {
        yield return StartCoroutine(Fade(1f, 0f, duration));
    }

    private IEnumerator Fade(float fromAlpha, float toAlpha, float duration)
    {
        if (fadeImage == null)
        {
            yield return new WaitForSeconds(duration);
            yield break;
        }

        fadeImage.gameObject.SetActive(true);
        float elapsed = 0f;
        Color c = fadeImage.color;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            c.a = Mathf.Lerp(fromAlpha, toAlpha, t);
            fadeImage.color = c;
            yield return null;
        }

        c.a = toAlpha;
        fadeImage.color = c;

        if (Mathf.Approximately(toAlpha, 0f))
            fadeImage.gameObject.SetActive(false);
    }

    #endregion

    #region Notifications

    public void ShowNotification(string message, float duration = 3f)
    {
        if (notificationRoot == null || notificationText == null)
        {
            Debug.Log("[UIManager] Notification: " + message);
            return;
        }

        if (notificationRoutine != null)
            StopCoroutine(notificationRoutine);

        notificationRoutine = StartCoroutine(NotificationRoutine(message, duration));
    }

    private IEnumerator NotificationRoutine(string message, float duration)
    {
        notificationText.text = message;
        notificationRoot.SetActive(true);
        yield return new WaitForSeconds(duration);
        notificationRoot.SetActive(false);
        notificationRoutine = null;
    }

    #endregion
}