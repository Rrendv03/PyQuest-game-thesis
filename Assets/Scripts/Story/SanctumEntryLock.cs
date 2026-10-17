using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Issue 1 fix — runtime sanctum entry/exit lock. Created automatically by
/// SanctumManager at scene start; exactly one per sanctum scene. Layered:
///
///  1. COLLIDER LOCKS: an invisible, airtight wall enclosure is built around
///     every exit portal so the player physically cannot reach the portal
///     trigger while locked. The enclosure is small (portal bounds + margin,
///     capped) so it never walls off a corridor the player legitimately walks.
///  2. EXIT LOCK: the portal's own trigger colliders are ALSO disabled while
///     locked (belt-and-braces — even a teleport or physics glitch cannot
///     fire SceneTransition), and SanctumManager.ExitSanctum() hard-gates on
///     this lock's verdict.
///  3. MARKER MESHES: every guide NPC (root + child NPCs of the optional
///     Inspector-assigned root) gets a bobbing glowing marker above its head
///     until ITS intro dialogue completes.
///
/// Exit verdict (user decision 2026-10-17, mirrors Issue 6): the exit stays
/// sealed until the sanctum's boss is defeated — entering a sanctum means you
/// finish it before you can leave. Intro-dialogue state only drives the
/// guide markers and the block reason text.
///
/// Intro-completion is deliberately resolved LAZILY every 0.25s and from
/// multiple sources, because dialogue loads asynchronously and saves restore
/// late:
///   - runtime: DialogueManager.OnSequenceComplete matched against the guide
///     (I subscribe at scene start, so skipped dialogue still counts);
///   - proxies: guide's quest completed (questIDToCompleteOnInteract), guide's
///     currentSequenceID already advanced past startingSequenceID (save
///     restore / progression advance), boss already defeated (re-entered
///     cleared sanctum).
/// This survives save/load without any new save data.
/// </summary>
public class SanctumEntryLock : MonoBehaviour
{
    /// <summary>Scene-local singleton (mirrors SanctumManager's lifetime).</summary>
    public static SanctumEntryLock Instance { get; private set; }

    /// <summary>
    /// Fired once when the final barrier is lifted (boss defeated). Later
    /// issues (3: tablet auto-open, 6: exit-portal minimap blip) subscribe
    /// to this instead of re-deriving state.
    /// </summary>
    public event Action OnLockRemoved;

    [Header("Barrier Shape (safe defaults; no scene edits required)")]
    [Tooltip("Clearance around the exit portal's own colliders.")]
    public float enclosureMargin = 1.0f;
    [Tooltip("Cap on the enclosure half-extent per horizontal axis, so the barrier never swallows a corridor or spawn point.")]
    public float enclosureHalfExtentCap = 2.5f;
    [Tooltip("Wall height in metres (walls extend 2m below the portal's lowest collider to hug the floor).")]
    public float wallHeight = 6f;
    [Tooltip("Also disable the exit portals' trigger colliders while locked (recommended; this is the airtight layer).")]
    public bool disableExitCollidersWhileLocked = true;

    private SanctumManager manager;
    private readonly List<NPCController> guides = new List<NPCController>();
    private readonly HashSet<NPCController> introSeenRuntime = new HashSet<NPCController>();
    private readonly List<Collider> sealedExitColliders = new List<Collider>();
    private readonly List<Transform> exitAnchors = new List<Transform>();
    private readonly List<GameObject> barrierWalls = new List<GameObject>();
    private readonly Dictionary<NPCController, GuideNPCMarker> markers = new Dictionary<NPCController, GuideNPCMarker>();

    private Material markerMaterial;
    private readonly List<GameObject> guideRoomTrapWalls = new List<GameObject>();
    private bool trapsLifted = false;
    private bool sealedOnce = false;
    private float recheckTimer;
    private const float RecheckInterval = 0.25f;

    #region Initialization
    /// <summary>Called by SanctumManager right after it finishes its own Start work.</summary>
    public void Initialize(SanctumManager sanctumManager, GameObject guideNpcRoot,
                           Material markerMaterial = null,
                           Transform[] guideRoomTrapPoints = null,
                           Vector3 guideRoomTrapWallSize = default)
    {
        manager = sanctumManager;
        Instance = this;
        this.markerMaterial = markerMaterial;

        BuildGuideRoomTraps(guideRoomTrapPoints,
            guideRoomTrapWallSize == default ? new Vector3(6f, 6f, 0.5f) : guideRoomTrapWallSize);
        CollectGuides(guideNpcRoot);
        CollectExitAnchors();
        StartCoroutine(SubscribeDialogueWhenReady());

        recheckTimer = RecheckInterval;
        EvaluateNow();
        Debug.Log($"[SanctumEntryLock] Initialized in {manager.sanctumID}: " +
                  $"{guides.Count} guide(s), {exitAnchors.Count} exit anchor(s), locked={IsExitLocked()}.");
    }

    private void CollectGuides(GameObject guideNpcRoot)
    {
        guides.Clear();
        if (guideNpcRoot != null)
        {
            // Root itself plus every child NPC controller, deduped (the root
            // may itself carry an NPCController in some scene layouts).
            HashSet<NPCController> seen = new HashSet<NPCController>();
            NPCController rootNpc = guideNpcRoot.GetComponent<NPCController>();
            if (rootNpc != null) seen.Add(rootNpc);
            foreach (NPCController child in guideNpcRoot.GetComponentsInChildren<NPCController>(true))
                seen.Add(child);
            guides.AddRange(seen);
        }

        if (guides.Count == 0)
        {
            // Fallback (scene survey: every sanctum has exactly one guide NPC
            // root, no other NPCControllers) — the lock works even when the
            // Inspector field is never assigned.
            Debug.LogWarning("[SanctumEntryLock] guideNpcRoot not assigned/empty — " +
                             "falling back to every NPCController in the scene.");
            guides.AddRange(FindObjectsOfType<NPCController>(true));
        }
    }

    private void CollectExitAnchors()
    {
        exitAnchors.Clear();
        SceneTransition[] transitions = FindObjectsOfType<SceneTransition>(true);
        foreach (SceneTransition t in transitions)
        {
            if (t == null) continue;
            exitAnchors.Add(t.transform);
            Debug.Log($"[SanctumEntryLock] Exit anchor: '{t.gameObject.name}' -> '{t.sceneToLoad}'");
        }
        if (exitAnchors.Count == 0 && manager != null && manager.exitPortalPoint != null)
        {
            // Defensive: a scene without a SceneTransition still gets a wall
            // seal around exitPortalPoint; the ExitSanctum hard gate remains.
            exitAnchors.Add(manager.exitPortalPoint);
            Debug.LogWarning("[SanctumEntryLock] No SceneTransition found — sealing around exitPortalPoint instead.");
        }
    }

    private IEnumerator SubscribeDialogueWhenReady()
    {
        float waited = 0f;
        while (DialogueManager.Instance == null && waited < 10f)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (DialogueManager.Instance == null)
        {
            Debug.LogWarning("[SanctumEntryLock] No DialogueManager in scene; " +
                             "intro completion will only be detected via quest/sequence proxies.");
            yield break;
        }

        // Guard against double subscription if Initialize ever runs twice.
        DialogueManager.Instance.OnSequenceComplete -= HandleSequenceComplete;
        DialogueManager.Instance.OnSequenceComplete += HandleSequenceComplete;
    }

    private void OnDestroy()
    {
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.OnSequenceComplete -= HandleSequenceComplete;
        if (Instance == this)
            Instance = null;
    }
    #endregion

    #region Main Loop
    private void Update()
    {
        // Intro lesson HUD hold: while the auto-opened lesson tablet is up,
        // keep the gameplay HUD suppressed and restore it when it closes.
        // (Manual tablet use is never touched — this only runs when the lock
        // itself opened the tablet.)
        if (introLessonActive)
        {
            bool tabletOpen = LessonTabletUI.Instance != null &&
                              LessonTabletUI.Instance.panelRoot != null &&
                              LessonTabletUI.Instance.panelRoot.activeSelf;
            if (tabletOpen)
            {
                if (HUDController.Instance != null && HUDController.Instance.IsVisible)
                    HUDController.Instance.SetVisible(false);
            }
            else
            {
                // Chained follow-up support (two-KC sanctums): when the player
                // closes the current auto-opened tablet, open the next pending
                // follow-up lesson immediately instead of returning control —
                // the HUD stays hidden through the whole chain and only comes
                // back after the last tablet closes.
                string followupKC;
                if (TryGetPendingFollowup(out followupKC) && LessonTabletUI.Instance != null)
                {
                    Debug.Log($"[SanctumEntryLock] Intro lesson closed — opening follow-up lesson '{followupKC}'.");
                    LessonTabletUI.Instance.ShowLesson(followupKC, manager.sanctumID);
                    if (HUDController.Instance != null)
                        HUDController.Instance.SetVisible(false); // keep suppressed for the next tablet
                }
                else
                {
                    followupLessonQueue.Clear();
                    introLessonActive = false;
                    if (HUDController.Instance != null)
                        HUDController.Instance.SetVisible(true);
                    Debug.Log("[SanctumEntryLock] Intro lesson closed — gameplay HUD restored.");
                }
            }
        }

        recheckTimer -= Time.unscaledDeltaTime;
        if (recheckTimer > 0f) return;
        recheckTimer = RecheckInterval;
        EvaluateNow();
    }

    private void EvaluateNow()
    {
        if (manager == null) return;

        // ISSUE 3: the moment the guide's intro dialogue completes, auto-open
        // the sanctum's lesson tablet so the player is taught the concept
        // (print statements by default) straight away, in every sanctum.
        if (!introLessonShown && AllGuidesIntroComplete())
        {
            introLessonShown = true;
            StartCoroutine(ShowIntroLessonAfterDialogue());
        }

        bool shouldLock = IsExitLocked();
        if (shouldLock && !sealedOnce) SealBarriers();
        if (!shouldLock && sealedOnce) LiftBarriers();

        // Guide-room trap walls release as soon as the guide's intro dialogue
        // is complete (NOT gated on the boss) — the trap is only about making
        // sure the player meets the guide first.
        if (!trapsLifted && AllGuidesIntroComplete()) LiftGuideRoomTraps();

        RefreshGuides();
    }

    #region Intro Lesson Auto-Open (Issue 3)
    /// <summary>
    /// Knowledge component id (LessonTabletUI.Content key) auto-opened when the
    /// guide's intro dialogue completes. Set from SanctumManager's Inspector
    /// field (default print_statements); null/empty disables the feature.
    /// </summary>
    public string IntroLessonKnowledgeComponentID { get; set; } = "print_statements";

    /// <summary>
    /// Optional questID auto-completed when the auto-opened lesson tablet is
    /// shown (the "read the lesson tablet" objective). Set from SanctumManager;
    /// null/empty disables it.
    /// </summary>
    public string IntroLessonQuestToAutoComplete { get; set; } = "";

    /// <summary>
    /// Optional chained follow-up lessons (LessonTabletUI.Content keys) shown
    /// one after another each time the player closes the current auto-opened
    /// tablet. Supports sanctums with two knowledge components (e.g. the
    /// conditionals sanctum: intro conditionals, then loops). A follow-up whose
    /// knowledge component already has a completed tablet mission in this
    /// sanctum is skipped so re-entries don't re-teach finished material.
    /// </summary>
    private readonly List<string> followupLessonQueue = new List<string>();

    /// <summary>Parses the Inspector's comma-separated follow-up KC field. Call any time; re-calling replaces the queue.</summary>
    public void SetFollowupLessons(string commaSeparatedKCIDs)
    {
        followupLessonQueue.Clear();
        if (!string.IsNullOrWhiteSpace(commaSeparatedKCIDs))
        {
            foreach (string raw in commaSeparatedKCIDs.Split(','))
            {
                string id = raw.Trim();
                if (!string.IsNullOrEmpty(id)) followupLessonQueue.Add(id);
            }
        }
        if (followupLessonQueue.Count > 0)
            Debug.Log($"[SanctumEntryLock] Follow-up lesson chain configured: {string.Join(" -> ", followupLessonQueue)}.");
    }

    /// <summary>
    /// TRUE when a follow-up should open right after the current tablet closes:
    /// there is a pending KC and the player has NOT yet completed any tablet
    /// mission for it in this sanctum (completed => already taught => skip).
    /// </summary>
    private bool TryGetPendingFollowup(out string kcID)
    {
        kcID = null;
        while (followupLessonQueue.Count > 0)
        {
            string candidate = followupLessonQueue[0];
            followupLessonQueue.RemoveAt(0);
            if (IsKnowledgeComponentTaughtInSanctum(candidate))
            {
                Debug.Log($"[SanctumEntryLock] Skipping follow-up lesson '{candidate}' — its knowledge component already has a completed mission in this sanctum.");
                continue; // already taught; try the next one
            }
            kcID = candidate;
            return true;
        }
        return false;
    }

    /// <summary>
    /// A knowledge component counts as taught when at least one completed
    /// tablet mission in this sanctum declares it as its knowledge component.
    /// Uses MissionTabletManager's completion state so save/load restores are
    /// respected.
    /// </summary>
    private bool IsKnowledgeComponentTaughtInSanctum(string kcID)
    {
        MissionTabletManager missions = MissionTabletManager.Instance;
        if (missions == null || manager == null || string.IsNullOrEmpty(kcID)) return false;
        foreach (MissionTabletData mission in missions.GetMissionsForSanctum(manager.sanctumID))
        {
            if (mission != null &&
                string.Equals(mission.knowledgeComponent, kcID, StringComparison.OrdinalIgnoreCase) &&
                missions.IsMissionComplete(mission.missionID))
                return true;
        }
        return false;
    }

    private bool introLessonShown;

    /// <summary>True while the auto-opened intro lesson tablet is on screen and we are holding the HUD hidden for it.</summary>
    private bool introLessonActive;

    /// <summary>
    /// Fires only AFTER the guide's dialogue sequence has fully ended (natural
    /// finish or skip). Never opens while a dialogue sequence is on screen —
    /// including the save-restore proxy path, which waits out any active
    /// sequence first.
    /// </summary>
    private IEnumerator ShowIntroLessonAfterDialogue()
    {
        // Wait until DialogueManager reports no active sequence. This covers the
        // runtime path (event fires as the panel closes) AND the proxy paths
        // (save restore, quest completion) that could land mid-conversation.
        float safetyTimeout = 10f;
        while (DialogueManager.Instance != null && DialogueManager.Instance.IsSequenceActive && safetyTimeout > 0f)
        {
            safetyTimeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        // Small grace period so the dialogue panel's own closing/restore work
        // (HUD re-enable, fade, save unblock) finishes before we take over.
        yield return new WaitForSeconds(0.5f);

        // Re-verify completion after the wait (a save/load or scene teardown
        // could have raced us); LessonTabletUI only shows known content, so an
        // unknown/typo'd id just logs a warning and shows nothing — safe.
        if (manager == null || !AllGuidesIntroComplete()) yield break;
        if (string.IsNullOrEmpty(IntroLessonKnowledgeComponentID)) yield break;
        if (LessonTabletUI.Instance == null)
        {
            Debug.LogWarning("[SanctumEntryLock] Intro dialogue complete but no LessonTabletUI in scene — skipping auto-lesson.");
            yield break;
        }

        Debug.Log($"[SanctumEntryLock] Intro complete — auto-opening lesson '{IntroLessonKnowledgeComponentID}' in {manager.sanctumID}.");

        // Hide the gameplay HUD for the duration of the auto-opened tablet.
        // DialogueManager re-enables it when dialogue ends (before this runs),
        // so we hide it again here and restore it only when the player closes
        // the tablet (see Update).
        if (HUDController.Instance != null)
            HUDController.Instance.SetVisible(false);

        introLessonActive = true;
        LessonTabletUI.Instance.ShowLesson(IntroLessonKnowledgeComponentID, manager.sanctumID);

        // Bonus: the tablet was shown automatically, so auto-check the quest
        // objective tied to reading it (only if a questID was configured).
        if (!string.IsNullOrEmpty(IntroLessonQuestToAutoComplete))
        {
            if (StoryProgressionManager.Instance != null &&
                !StoryProgressionManager.Instance.IsQuestComplete(IntroLessonQuestToAutoComplete))
            {
                StoryProgressionManager.Instance.CompleteQuest(IntroLessonQuestToAutoComplete);
                Debug.Log($"[SanctumEntryLock] Auto-completed quest '{IntroLessonQuestToAutoComplete}' (intro lesson shown).");
            }
        }
    }

    #endregion

    #region Guide Room Traps
    /// <summary>
    /// ISSUE-1 (user request 2026-10-17): invisible walls at Inspector-assigned
    /// transforms that trap the player in the guide's room until the guide's
    /// intro dialogue completes. Each wall uses the point transform's position
    /// AND rotation (box extends along local Z = thickness), so you place them
    /// like door blockers in the editor. No points assigned = no trap (feature off).
    /// </summary>
    private void BuildGuideRoomTraps(Transform[] points, Vector3 size)
    {
        if (points == null || points.Length == 0) return;

        foreach (Transform point in points)
        {
            if (point == null) continue;
            GameObject wall = new GameObject($"GuideRoomTrap_{point.name}");
            wall.transform.SetPositionAndRotation(point.position, point.rotation);

            BoxCollider box = wall.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.size = size;

            guideRoomTrapWalls.Add(wall);
        }

        if (guideRoomTrapWalls.Count > 0)
            Debug.Log($"[SanctumEntryLock] Built {guideRoomTrapWalls.Count} guide-room trap wall(s). They lift when the guide's intro dialogue completes.");
    }

    private void LiftGuideRoomTraps()
    {
        trapsLifted = true;
        foreach (GameObject wall in guideRoomTrapWalls)
        {
            if (wall == null) continue;
            Destroy(wall);
        }
        guideRoomTrapWalls.Clear();
        Debug.Log("[SanctumEntryLock] Guide-room traps lifted — intro dialogue complete.");
    }
    #endregion
    #endregion

    #region Verdict
    /// <summary>
    /// TRUE while the sanctum exit must stay sealed. Only the boss verdict
    /// matters for the gate (Issue 6 hard rule); StoryProgressionManager is
    /// consulted as a backup because manager.restore can lag a save load.
    /// </summary>
    public bool IsExitLocked()
    {
        if (manager == null) return false;
        if (manager.IsBossDefeated() || manager.IsSanctumCleared()) return false;
        if (StoryProgressionManager.Instance != null &&
            !string.IsNullOrEmpty(manager.sanctumID) &&
            StoryProgressionManager.Instance.HasDefeatedBoss(manager.sanctumID))
        {
            return false;
        }
        return true;
    }

    /// <summary>Human-readable reason shown when an exit attempt is refused.</summary>
    public string GetExitBlockReason()
    {
        if (AnyGuideIntroPending())
            return "Speak with the sanctum's guide first.";
        return "Defeat this sanctum's guardian to unlock the exit.";
    }

    /// <summary>
    /// FALSE once EVERY guide's intro dialogue is accounted complete
    /// (runtime or proxy). Consumed by later issues (auto-tablet, markers).
    /// </summary>
    public bool AllGuidesIntroComplete()
    {
        foreach (NPCController guide in guides)
        {
            if (guide == null) continue; // departed/destroyed guide = nothing to gate
            if (!IsIntroComplete(guide)) return false;
        }
        return true;
    }

    public bool AnyGuideIntroPending()
    {
        foreach (NPCController guide in guides)
        {
            if (guide == null || guide.HasDeparted()) continue;
            if (!IsIntroComplete(guide)) return true;
        }
        return false;
    }

    /// <summary>
    /// Intro-complete resolution for one guide. Order matters: a live
    /// runtime observation wins, then persisted/session proxies. A guide
    /// that departed (or was removed) counts as complete — there is no
    /// dialogue left to gate on.
    /// </summary>
    public bool IsIntroComplete(NPCController guide)
    {
        if (guide == null) return true;
        if (guide.HasDeparted()) return true;
        if (introSeenRuntime.Contains(guide)) return true;

        // Proxy 1: the guide's own quest (completed on first interact) is
        // recorded as done.
        if (!string.IsNullOrEmpty(guide.questIDToCompleteOnInteract) &&
            StoryProgressionManager.Instance != null &&
            StoryProgressionManager.Instance.IsQuestComplete(guide.questIDToCompleteOnInteract))
        {
            return true;
        }

        // Proxy 2: save-restore / progression already advanced the guide
        // past its starting sequence (save recorded post-intro state).
        // IMPORTANT: an EMPTY currentSequenceID (guide's own Start() has not
        // run yet — script execution order is undefined) must NOT count as
        // "advanced", otherwise a fresh sanctum entry would look intro-complete
        // for the first frames. Empty = pending until lifecycle init populates it.
        if (!string.IsNullOrEmpty(guide.startingSequenceID) &&
            !string.IsNullOrEmpty(guide.GetCurrentSequenceID()) &&
            guide.GetCurrentSequenceID() != guide.startingSequenceID)
        {
            return true;
        }

        // Proxy 3: boss already defeated (re-entered cleared sanctum — the
        // exit verdict is open and markers make no sense).
        if (!IsExitLocked()) return true;

        return false;
    }
    #endregion

    #region Dialogue Completion Tracking
    /// <summary>
    /// Called by DialogueManager for every completed sequence (read OR
    /// skipped — skip uses the same completion path per DialogueManager).
    /// Attribution: subscribers run in subscription order, and SanctumEntryLock
    /// subscribes at scene start while NPCController subscribes when the
    /// player triggers dialogue — so this runs BEFORE the NPC advances its
    /// own currentSequenceID and the finished id still matches.
    /// </summary>
    private void HandleSequenceComplete(DialogueSequence finished)
    {
        if (finished == null || string.IsNullOrEmpty(finished.sequenceID)) return;

        foreach (NPCController guide in guides)
        {
            if (guide == null) continue;
            string playing = guide.GetCurrentSequenceID();
            bool attributed = !string.IsNullOrEmpty(playing) && playing == finished.sequenceID;
            // Secondary catch: a sequence id equal to this guide's declared
            // starting sequence completes that guide's intro even if the
            // live attribution somehow missed (e.g. mid-chain re-entry).
            if (!attributed && !string.IsNullOrEmpty(guide.startingSequenceID))
                attributed = finished.sequenceID == guide.startingSequenceID;

            if (attributed && !introSeenRuntime.Contains(guide))
            {
                introSeenRuntime.Add(guide);
                Debug.Log($"[SanctumEntryLock] Guide '{guide.npcID}' intro dialogue complete ('{finished.sequenceID}').");
            }
        }
    }
    #endregion

    #region Barriers
    private void SealBarriers()
    {
        sealedOnce = true;

        if (disableExitCollidersWhileLocked)
        {
            foreach (Transform anchor in exitAnchors)
            {
                if (anchor == null) continue;
                Collider[] colliders = anchor.GetComponentsInChildren<Collider>(true);
                foreach (Collider col in colliders)
                {
                    if (col == null || !col.enabled) continue;
                    bool sameObject = col.transform == anchor;
                    // Child colliders: only triggers gate the exit and only
                    // triggers get sealed. Colliders sitting directly on the
                    // SceneTransition's own GameObject are part of the exit
                    // mechanism and are sealed unconditionally.
                    if (!sameObject && !col.isTrigger) continue;
                    col.enabled = false;
                    sealedExitColliders.Add(col);
                }
            }
            Debug.Log($"[SanctumEntryLock] Sealed {sealedExitColliders.Count} exit trigger collider(s).");
        }

        BuildBarrierWalls();
    }

    private void LiftBarriers()
    {
        sealedOnce = false;

        foreach (Collider col in sealedExitColliders)
        {
            if (col == null) continue;
            col.enabled = true; // re-fires OnTriggerEnter if the player overlaps (post-boss, both are legitimate)
        }
        sealedExitColliders.Clear();

        foreach (GameObject wall in barrierWalls)
        {
            if (wall == null) continue;
            Destroy(wall);
        }
        barrierWalls.Clear();

        RemoveAllGuides();

        OnLockRemoved?.Invoke();
        Debug.Log("[SanctumEntryLock] Barriers lifted — exit unlocked.");
    }

    private void BuildBarrierWalls()
    {
        List<Vector3> keepOutsidePoints = CollectKeepOutsidePoints();

        foreach (Transform anchor in exitAnchors)
        {
            if (anchor == null) continue;

            Bounds portalBounds = ComputeAnchorBounds(anchor);
            Vector3 center = portalBounds.center;
            Vector3 half = portalBounds.extents;
            half.x = Mathf.Min(half.x + enclosureMargin, enclosureHalfExtentCap);
            half.z = Mathf.Min(half.z + enclosureMargin, enclosureHalfExtentCap);
            // Vertical: hug the floor (extend 2m below the lowest collider)
            // and rise well above it so nothing can hop over.
            float baseY = Mathf.Min(portalBounds.min.y - 2f, center.y - 2f);

            float spanX = half.x * 2f;
            float spanZ = half.z * 2f;
            float thickness = 0.3f;
            float height = Mathf.Max(wallHeight, portalBounds.size.y + 2f);

            // Four axis-aligned walls, one per side. Axis-aligned on purpose:
            // correct regardless of the portal's own rotation and airtight
            // against every approach direction.
            // (fully positional: C# rejects positional args after named ones)
            BuildWall(anchor, "South", center, keepOutsidePoints, 2, -1, half, baseY, spanX, height, thickness);
            BuildWall(anchor, "North", center, keepOutsidePoints, 2, +1, half, baseY, spanX, height, thickness);
            BuildWall(anchor, "West", center, keepOutsidePoints, 0, -1, half, baseY, spanZ, height, thickness);
            BuildWall(anchor, "East", center, keepOutsidePoints, 0, +1, half, baseY, spanZ, height, thickness);
        }

        Debug.Log($"[SanctumEntryLock] Built {barrierWalls.Count} barrier wall(s).");
    }

    /// <summary>
    /// Points that must never end up INSIDE a sealed enclosure: the player's
    /// spawn point and every live guide NPC. If an enclosure would contain
    /// one, that wall is pushed outward past the point instead of being
    /// skipped, so the seal stays airtight without jailing anyone.
    /// </summary>
    private List<Vector3> CollectKeepOutsidePoints()
    {
        List<Vector3> points = new List<Vector3>();
        if (manager != null && manager.playerSpawnPoint != null)
            points.Add(manager.playerSpawnPoint.position);
        foreach (NPCController guide in guides)
        {
            if (guide != null && !guide.HasDeparted())
                points.Add(guide.transform.position);
        }
        return points;
    }

    private void BuildWall(Transform anchor, string side, Vector3 portalCenter,
                           List<Vector3> keepOutsidePoints, int axis, int sign,
                           Vector3 half, float baseY, float spanLength, float height, float thickness)
    {
        // Wall plane on the requested side of the enclosure, pushed outward
        // past any keep-outside point that sits on this side of the portal.
        float offset = (axis == 2 ? half.z : half.x);
        foreach (Vector3 p in keepOutsidePoints)
        {
            float axisCenter = axis == 2 ? portalCenter.z : portalCenter.x;
            float along = (axis == 2 ? p.z : p.x) - axisCenter;
            // Push this wall outward past any point that would end up inside
            // the enclosure along this axis (either side, including points
            // sitting dead-center on the portal). The other axis's walls are
            // handled by their own pass, so the enclosure always grows
            // symmetrically around the point instead of skipping it.
            if (Mathf.Abs(along) > offset + thickness) continue;
            offset = Mathf.Max(offset, Mathf.Abs(along) + 1.2f);
        }

        Vector3 wallCenter = portalCenter;
        if (axis == 2) wallCenter.z += offset * sign;
        else wallCenter.x += offset * sign;
        wallCenter.y = baseY + height * 0.5f;

        GameObject wall = new GameObject($"SanctumEntryBarrier_{anchor.name}_{side}");
        wall.transform.SetPositionAndRotation(wallCenter, Quaternion.identity);

        BoxCollider box = wall.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.size = axis == 2
            ? new Vector3(spanLength, height, thickness)
            : new Vector3(thickness, height, spanLength);

        barrierWalls.Add(wall);
    }

    /// <summary>
    /// World bounds of the anchor and its children (portal mesh + trigger
    /// volumes). Falls back to a 2m cube around the transform when the
    /// anchor has no colliders/renderers at all.
    /// </summary>
    private static Bounds ComputeAnchorBounds(Transform anchor)
    {
        Bounds bounds = new Bounds();
        bool any = false;

        Collider[] colliders = anchor.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in colliders)
        {
            if (c == null) continue;
            if (!any) { bounds = c.bounds; any = true; }
            else bounds.Encapsulate(c.bounds);
        }

        Renderer[] renderers = anchor.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (!any)
            bounds = new Bounds(anchor.position, new Vector3(2f, 2f, 2f));
        return bounds;
    }
    #endregion

    #region Guide Markers
    private void RefreshGuides()
    {
        // Remove markers for departed guides first, then attach/remove the rest.
        List<NPCController> departedKeys = null;
        foreach (KeyValuePair<NPCController, GuideNPCMarker> kv in markers)
        {
            NPCController guide = kv.Key;
            bool dead = guide == null || guide.HasDeparted();
            if (!dead) continue;
            if (kv.Value != null) kv.Value.Remove();
            (departedKeys ?? (departedKeys = new List<NPCController>())).Add(guide);
        }
        if (departedKeys != null)
        {
            foreach (NPCController dead in departedKeys)
                markers.Remove(dead);
        }

        foreach (NPCController guide in guides)
        {
            if (guide == null) continue;
            bool pending = !IsIntroComplete(guide) && !guide.HasDeparted();

            if (pending && !markers.ContainsKey(guide))
            {
                GuideNPCMarker marker = GuideNPCMarker.Attach(guide, markerMaterial);
                if (marker != null) markers.Add(guide, marker);
            }
            else if (!pending && markers.TryGetValue(guide, out GuideNPCMarker done))
            {
                done.Remove();
                markers.Remove(guide);
            }
        }
    }

    private void RemoveAllGuides()
    {
        foreach (KeyValuePair<NPCController, GuideNPCMarker> kv in markers)
        {
            if (kv.Value != null) kv.Value.Remove();
        }
        markers.Clear();
    }
    #endregion
}
