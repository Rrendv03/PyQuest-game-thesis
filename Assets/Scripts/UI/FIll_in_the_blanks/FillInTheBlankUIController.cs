using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Bare `Random` means UnityEngine.Random (Random.Range API) -- keeps CS0104
// away if `using System;` is ever added to this file. Do not remove.
using Random = UnityEngine.Random;

public class FillInTheBlankUIController : MonoBehaviour
{
    [Header("Left - Code Display")]
    public Text codeDisplayText;

    [Header("Right - Token Buttons")]
    public List<GameObject> tokenObjects;

    [Header("Audio (optional)")]
    [Tooltip("Played once every time a token box is clicked (select or deselect). Leave empty for silence.")]
    public AudioClip clickSound;

    private FillInTheBlankToken selectedToken = null;
    private AudioSource clickAudio;

    void Awake()
    {
        // The AudioSource is created at runtime on this controller's object,
        // so no prefab or scene edit is needed. If you prefer a configured
        // source, put an AudioSource on this GameObject and it will be used.
        clickAudio = GetComponent<AudioSource>();
        if (clickAudio == null)
            clickAudio = gameObject.AddComponent<AudioSource>();
        clickAudio.playOnAwake = false;
    }

    /// <summary>Plays the click sound if one is assigned. Safe to call
    /// when the field is empty -- it just stays silent.</summary>
    public void PlayClickSound()
    {
        if (clickSound != null && clickAudio != null)
            clickAudio.PlayOneShot(clickSound);
    }

    public void PopulateUI(string codeSnippet, List<string> tokens)
    {
        // Display code snippet with blank
        if (codeDisplayText != null)
            codeDisplayText.text = codeSnippet;

        // Reset all tokens
        foreach (var obj in tokenObjects)
            obj.SetActive(false);

        // Shuffle tokens
        List<string> shuffled = new List<string>(tokens);
        ShuffleList(shuffled);

        // Populate token buttons
        for (int i = 0; i < shuffled.Count && i < tokenObjects.Count; i++)
        {
            tokenObjects[i].SetActive(true);

            FillInTheBlankToken token = tokenObjects[i].GetComponent<FillInTheBlankToken>();
            if (token != null)
                token.Setup(shuffled[i], this);
        }

        selectedToken = null;
        Debug.Log("[FillInTheBlankUIController] UI populated");
    }

    public void OnTokenSelected(FillInTheBlankToken token)
    {
        PlayClickSound();

        if (selectedToken != null)
            selectedToken.SetState_Default();

        selectedToken = token;
        token.SetState_Selected();

        if (codeDisplayText != null)
            codeDisplayText.text = codeDisplayText.text.Replace("____", token.tokenText);

        Debug.Log($"[FillInTheBlankUIController] Token selected: {token.tokenText}");

        PuzzleManager.Instance.UserSubmission(token.tokenText);
    }

    public void OnTokenDeselected(FillInTheBlankToken token)
    {
        PlayClickSound();

        if (selectedToken == token)
        {
            selectedToken = null;
            token.SetState_Default();

            if (codeDisplayText != null)
                codeDisplayText.text = codeDisplayText.text.Replace(token.tokenText, "____");

            Debug.Log($"[FillInTheBlankUIController] Token deselected: {token.tokenText}");
        }
    }

    private void ShuffleList(List<string> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            string temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
}
