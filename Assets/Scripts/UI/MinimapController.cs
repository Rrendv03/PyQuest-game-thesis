using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// PyQuest minimap HUD + expandable scene map. One drop-in component per scene
/// (all 4 sanctums and MainMap). It builds its own canvas, its own top-down
/// camera and every pixel of its UI at runtime: no prefab, no sprite assets, no
/// manual wiring beyond an optional sanctum id.
///
/// HUD MODE
///   Circular disc pinned to the top-right, showing the ground around the player
///   live from a dedicated orthographic top-down camera. Cyan arrow = the player,
///   rotated to their facing. Other markers clamp onto the rim when they fall
///   outside the disc, so something you still have to find can never quietly drop
///   off the edge of the map.
///
/// FULLSCREEN MODE (press the disc, or the toggle key)
///   The whole map of the current scene, north-up, across the screen. Drag to pan
///   in any 2D direction; zoom with the + / - buttons, the mouse wheel or a
///   two-finger pinch; always clamped to the scene bounds so the camera cannot be
///   dragged off into the void. Back button in the top-left corner, current quest
///   name + description in a panel down the left edge, legend bottom-right.
///
/// FULLSCREEN SKIN (user-supplied art)
///   Back button uses the project sprite "Exit_Bright" at 80x80 in pure white with no
///   label; the quest panel uses the project sprite "9-Sliced" tinted #202020; the
///   zoom and recentre buttons are tinted #666666; every fullscreen text is set in
///   Jost-Regular. These are looked up by name at runtime - AssetDatabase in the
///   editor, Resources.Load (any folder called Resources) in builds - and the map
///   falls back to the generated round-rect / built-in font with a warning if absent.
///   The legend's geometric glyph characters (Jost has none) were replaced by the
///   real marker sprites at 20px, so the legend keeps its shapes in every font.
///
/// MARKERS (one code path for both modes)
///   tablet   gold diamond   one per unsolved TabletMissionObject, sanctums only
///   boss     red ring       only once the sanctum's real unlock condition holds -
///                           XPManager's threshold AND every mission tablet, which
///                           is exactly what the boss zone requires, so the map can
///                           never advertise a boss that will not start
///   sanctum  circle / star  MainMap only: every sanctum entrance, the one to visit
///                           next pulsing gold
///   player   cyan arrow     live position and facing
///
/// Those rules live in MinimapLogic and the words in QuestText, both of which read
/// the managers and touch no GameObjects, so the whole marker set can be derived
/// and unit-tested outside a scene.
///
/// SAVE / LOAD BEHAVIOUR
///   The map never calls SaveLoadManager and does not need to. It watches the state
///   the save system writes: MinimapLogic.BuildStateSignature() collapses scene +
///   active quest + XP + per-tablet completion + per-sanctum boss defeat into one
///   string, compared every statePollInterval. Loading a slot runs
///   MissionTabletManager.ImportCompletedMissions and
///   StoryProgressionManager.ImportDefeatedBosses, which replace those sets in
///   place WITHOUT firing an event; the signature poll catches that, and any future
///   load path this component has never heard of, with no new hooks. Event
///   subscriptions (XP changed, missions loaded, boss unlocked/defeated, sanctum
///   cleared, quest changed) only make the same refresh immediate instead of a
///   quarter-second late.
///
///   Opening the fullscreen map takes a keyed "minimap" blocker on
///   SaveRestrictionEnforcer so an autosave cannot fire mid-view. Keyed rather than
///   the shared SaveLoadManager.IsSafeToSave bool, deliberately: writing false here
///   would be safe, but writing true on close would silently release a blocker
///   another system still owns. The blocker, the movement lock and the hidden HUD
///   are released on collapse AND on destroy, so a map that dies while open (scene
///   unload, a removed GameObject) cannot strand the save system or the controls.
///
///   A scene load while expanded collapses first: a full-screen map of the scene
///   you just left means nothing, and leaving it up would keep the player locked out
///   of movement in the new one.
///
/// GAMEPLAY HUD VISIBILITY PARITY
///   Whatever hides the gameplay HUD (an enemy encounter, a cutscene) hides the
///   minimap too: the controller polls HUDController's SetVisible state every
///   frame. While the HUD is gone the map goes fully inert - no disc, no raycasts,
///   no toggle key - and if the fullscreen map was open when that happened it
///   collapses WITHOUT touching the encounter's hide state. The old failure (open the map
///   during an encounter, exit, and the map resurrects the HUD under the
///   encounter's feet) is exactly that restore-on-top-of-another-owner, which is
///   why the map inspects the same flag the encounter wrote instead of relying on
///   scene parenting: reparenting would deadlock the fullscreen close, because the
///   map itself hides that canvas while it is open.
///
/// PAUSE PARITY
///   The pause menu (PauseMenuManager) freezes the game with Time.timeScale = 0
///   behind its own overlay and never hides the gameplay HUD, so HUD-following
///   cannot see it. The controller polls PauseMenuManager.Instance.IsPaused every
///   frame (Update still runs while time is stopped) and while paused the map is
///   inert exactly as during an encounter: no disc, no raycasts, no toggle key,
///   and an open fullscreen map collapses without releasing pause's ownership of
///   the screen. The moment Resume() runs, the minimap comes back as it was.
///
/// CANVAS SCALING (fullscreen revert)
///   The fullscreen layout is authored in canvas units of the minimap's own
///   reference resolution (1920x1080 by default) and only reflows correctly on
///   that scaler: copying the gameplay HUD's scaling (its fixed pixel scale in
///   particular) misplaces every fullscreen element, so that attempt was
///   reverted and the canvas stays on its own CanvasScaler. hudCorner /
///   hudEdgeMargin / hudScale reposition and resize the disc for layout tests
///   through the Inspector instead.
///
/// PARTICLES AND WALLS
///   Particle renderers are switched off around the map camera's own render and
///   restored immediately afterwards, so the map stays clean while the gameplay
///   view keeps every effect. Wall silhouettes are collider footprints (Box,
///   Capsule, Mesh) flattened into tinted quads in one combined mesh whose
///   renderer only exists for the same single render: nothing appears in the
///   world, and drawWalls/wallColor/wallMinHeight tune the look.
///
/// UNITY SETUP
///   1. In each sanctum and in MainMap: create an empty GameObject, name it
///      "Minimap", Add Component > MinimapController. That is the whole setup.
///   2. MainMap only, and only if that scene has no MainMapSanctumCompass: fill
///      mainMapEntrances (sanctum id + label + entrance transform). If the compass
///      is there its entrance list is reused - one source of truth, nothing to keep
///      in sync between two components.
///   3. Needs an EventSystem, which every gameplay scene already has for the HUD
///      buttons. Without one this logs one warning and the map still renders, it is
///      just not pressable.
///   4. Tag any geography that should not define the map's extent with
///      "MiniMapIgnore" (a distant skybox shell, a floating VFX plane).
/// </summary>
[AddComponentMenu("PyQuest/Minimap Controller")]
public class MinimapController : MonoBehaviour
{
    /// <summary>Scene-local, not DontDestroyOnLoad: every scene builds its own map.</summary>
    public static MinimapController Instance { get; private set; }

    // === Inspector: identity ==============================================================

    [Header("Sanctum")]
    [Tooltip("Leave empty to auto-resolve from the scene name (then the scene's SanctumManager). Only set it in a scene that is neither.")]
    public string sanctumID = "";

    // === Inspector: HUD minimap ===========================================================

    [Header("HUD Minimap")]
    [Tooltip("Screen corner the disc is pinned to, so it can sit next to your other HUD controls.")]
    public HudCorner hudCorner = HudCorner.TopRight;
    [Tooltip("Disc diameter in reference pixels (the reference-resolution canvas).")]
    public float hudDiameter = 196f;
    public Vector2 hudEdgeMargin = new Vector2(26f, 26f);
    [Tooltip("Uniform scale on the disc and caption, for testing HUD layouts without touching code.")]
    [Range(0.5f, 2f)] public float hudScale = 1f;
    [Tooltip("Atlas file the Exit_Bright sub-sprite lives in. Sub-sprites inside a multi-sprite atlas are invisible to a name search, so the atlas path is given directly and the sprite is looked up inside it.")]
    public string backButtonAtlasPath = "Assets/Modern GDR - Free icons pack/00_Atlas/BrightIcons.png";
    [Tooltip("World half-height shown inside the disc. Raise it to see more of the sanctum.")]
    public float hudZoom = 18f;
    [Tooltip("Caption under the disc. Empty hides it.")]
    public string hudLabel = "MAP";
    [Tooltip("Seconds between disc re-renders while collapsed. The disc only changes when the player moves; 0.05 is ~20fps and skips a full extra scene render on most frames.")]
    [Range(0f, 0.5f)] public float hudRenderInterval = 0.05f;

    // === Inspector: fullscreen map ========================================================

    [Header("Fullscreen Map")]
    [Tooltip("Map title. Empty = the sanctum's display name, or 'The Overworld' on MainMap.")]
    public string mapTitleOverride = "";
    [Tooltip("World half-height at maximum zoom-IN (the smallest area shown).")]
    public float zoomMin = 9f;
    [Tooltip("Multiplier on the fit-the-whole-scene half-height at maximum zoom-OUT.")]
    public float zoomOutPadding = 1.12f;
    [Tooltip("World half-height changed per + / - press.")]
    public float zoomStep = 5f;
    public float wheelZoomPerNotch = 4f;
    [Tooltip("Exponential gain on a pinch: how strongly a finger spread maps to zoom.")]
    public float pinchZoomGain = 1.5f;
    [Tooltip("How fast eased zoom and pan settle, in lerps per second.")]
    public float cameraEase = 14f;
    [Tooltip("0 = open on the whole scene, 1 = open centred on the player at minimap zoom.")]
    [Range(0f, 1f)] public float openOnPlayerBlend = 0f;

    // === Inspector: markers ===============================================================

    [Header("Markers")]
    public bool showPlayerMarker = true;
    public bool showTabletMarkers = true;
    public bool showBossMarker = true;
    [Tooltip("Ghost the boss marker before its requirements are met? Off by default: the map should not point at a door the player cannot open.")]
    public bool showBossMarkerWhileLocked = false;
    [Tooltip("Keep a dim marker where the defeated guardian fell.")]
    public bool showBossMarkerAfterDefeat = true;
    public bool showSanctumMarkersOnMainMap = true;
    [Tooltip("Labels under the markers in fullscreen mode.")]
    public bool showMarkerLabels = true;
    [Tooltip("Seconds between re-scans for tablets, the boss zone and the entrances, none of which are guaranteed to exist yet at Start.")]
    [Min(0.25f)] public float markerRescanInterval = 2f;

    // === Inspector: save / load reactivity ================================================

    [Header("Save / Load Reactivity")]
    [Tooltip("Seconds between progress-state signature checks. This is what notices a save being loaded.")]
    [Min(0.05f)] public float statePollInterval = 0.25f;
    [Tooltip("Desktop test key that toggles the fullscreen map. None disables it.")]
    public KeyCode toggleKey = KeyCode.M;

    // === Inspector: map camera ===========================================================

    [Header("Map Camera")]
    [Tooltip("Resolution of the top-down map texture in pixels. 1024 is sharp on a phone; drop toward 512 if a sanctum becomes GPU-bound.")]
    public int mapTextureResolution = 1024;
    public float mapCameraHeight = 240f;
    public Color mapBackgroundColor = new Color(0.043f, 0.063f, 0.09f, 1f);
    [Tooltip("Layers the map camera must not draw: its own UI, and any VFX layer that would blot out the view.")]
    public string[] ignoredLayerNames = new string[] { "UI", "Water" };
    [Tooltip("Objects with this tag are left out of the map's computed extent.")]
    public string ignoreBoundsTag = "MiniMapIgnore";

    [System.NonSerialized] bool ignoreTagChecked;
    [System.NonSerialized] bool ignoreTagUsable;

    [Header("Manual Scene Bounds (optional)")]
    [Tooltip("Use these instead of measuring the scene, if a scene's geometry gives a bad fit.")]
    public bool useManualBounds = false;
    public Vector3 manualBoundsCenter = Vector3.zero;
    public Vector3 manualBoundsSize = new Vector3(200f, 10f, 200f);

    // === Inspector: scene references (all optional) ======================================

    [Header("MainMap Entrances (optional)")]
    [Tooltip("Only needed if this MainMap has no MainMapSanctumCompass to read. Fill in progression order: Print Console, Vars Vault, Input Mists, Elif Labyrinth.")]
    public MainMapEntrance[] mainMapEntrances = new MainMapEntrance[0];

    [Header("Boss Zone (sanctum scenes, optional)")]
    [Tooltip("Empty = auto-find the ZoneTrigger with isBossZone, the same lookup QuestHUDDisplay uses.")]
    public Transform bossZoneOverride;

    [Header("Map Canvas Scaling (advanced)")]
    [Tooltip("Reference resolution the minimap canvas is authored against. The fullscreen layout is laid out in these canvas units; resize the disc with hudScale instead of this.")]
    public Vector2 fallbackReferenceResolution = new Vector2(1920f, 1080f);
    [Tooltip("Width/height match when the screen aspect differs from the reference.")]
    [Range(0f, 1f)] public float fallbackMatchWidthOrHeight = 0.5f;

    [Header("Gameplay HUD Visibility (optional)")]
    [Tooltip("When something else hides the gameplay HUD (enemy encounter, cutscene), the minimap hides itself, closes any open fullscreen map and refuses to open until the HUD returns. Off for debugging only.")]
    public bool followHudVisibility = true;

    [Tooltip("While the pause menu (PauseMenuManager) is open the minimap is inert: no disc, no raycasts, no toggle key, and an open fullscreen map closes itself. Off only for debugging.")]
    public bool hideWhenPaused = true;

    [Header("Map Clutter (optional)")]
    [Tooltip("Exclude particle systems from the map render only. Their renderers are switched off for the map camera's own render and restored at once, so the gameplay view keeps every effect.")]
    public bool hideParticleSystems = true;

    [Header("Wall Silhouettes (optional)")]
    [Tooltip("Draw wall collider footprints as flat tinted shapes on the map, above the ground but below everything else. Uses the Box / Capsule / Mesh colliders already in the scene; no extra geometry is created in the world.")]
    public bool drawWalls = true;
    [Tooltip("Tint of the wall silhouettes on the map. Slightly lighter than mapBackgroundColor reads best.")]
    public Color wallColor = new Color(0.13f, 0.21f, 0.30f, 0.92f);
    [Tooltip("Colliders shorter than this (world units) count as ground, not walls, and are skipped - otherwise low ground colliders would blanket the whole map in tint.")]
    public float wallMinHeight = 1f;

    [System.Serializable]
    public class MainMapEntrance
    {
        [Tooltip("Must match SanctumManager.sanctumID exactly.")]
        public string sanctumID;
        [Tooltip("Text under the marker, e.g. 'Vars Vault'.")]
        public string displayLabel;
        [Tooltip("The entrance GameObject in MainMap.")]
        public Transform entranceTransform;
    }

    /// <summary>Which screen corner the HUD disc is pinned to.</summary>
    public enum HudCorner { TopRight, TopLeft, BottomRight, BottomLeft }

    // === Colours ========================================================================

    static readonly Color TabletColor = new Color(1f, 0.82f, 0.25f, 1f);
    // Issue 1/6 flow: green guide marker shown while the player still has to
    // speak with the sanctum's NPC guide (before tablets are revealed).
    static readonly Color GuideColor = new Color(0.45f, 1f, 0.55f, 1f);
    static readonly Color BossColor = new Color(1f, 0.3f, 0.3f, 1f);
    static readonly Color BossDefeatedColor = new Color(0.45f, 0.47f, 0.52f, 1f);
    static readonly Color BossLockedColor = new Color(0.5f, 0.28f, 0.28f, 1f);
    static readonly Color PlayerColor = new Color(0.3f, 0.92f, 1f, 1f);
    static readonly Color NextSanctumColor = new Color(1f, 0.85f, 0.3f, 1f);
    static readonly Color SanctumColor = new Color(0.7f, 0.76f, 0.86f, 1f);
    static readonly Color SanctumClearedColor = new Color(0.35f, 0.62f, 0.4f, 1f);
    static readonly Color DiscClipColor = new Color(0.09f, 0.12f, 0.16f, 1f);
    static readonly Color PanelBackground = new Color(0.035f, 0.055f, 0.085f, 0.9f);
    static readonly Color ButtonBackground = new Color(0.12f, 0.17f, 0.24f, 0.95f);
    // Fullscreen skin colours requested for the expandable map (HUD disc is untouched).
    static readonly Color QuestPanelSkinColor = new Color32(0x20, 0x20, 0x20, 0xFF); // #202020
    static readonly Color FullscreenButtonColor = new Color32(0x66, 0x66, 0x66, 0xFF); // #666666
    static readonly Color PanelText = new Color(0.92f, 0.94f, 0.98f, 1f);
    static readonly Color DimBackground = new Color(0.01f, 0.015f, 0.03f, 0.94f);

    /// <summary>Above the gameplay HUD, below the pause menu.</summary>
    const int CanvasSortingOrder = 450;
    const string SaveBlockerKey = "minimap";

    // === Runtime state ==================================================================

    enum MapMode { Hud, Fullscreen }

    Camera mapCamera;
    RenderTexture mapTexture;
    int textureWidth, textureHeight;

    Canvas canvas;
    RectTransform canvasRect;

    GameObject hudRoot;
    RectTransform hudClipRect;
    RectTransform hudMarkerParent;

    GameObject fullRoot;
    RectTransform viewportRect;
    RectTransform fullMarkerParent;
    Text mapTitleText;
    Text questNameText;
    Text questBodyText;

    readonly Dictionary<string, MarkerView> markers = new Dictionary<string, MarkerView>();
    readonly List<MainMapEntrance> entranceCache = new List<MainMapEntrance>();

    string resolvedSanctumID = "";
    bool isMainMapScene;

    MapMode mode = MapMode.Hud;
    bool building = true;
    bool destroyed;

    /// <summary>World-space centre of the top-down camera. Its y is unused.</summary>
    Vector3 mapCentre;
    float currentZoom = 18f;
    float targetZoom = 18f;

    Transform playerTransform;
    PlayerMovement playerMovement;
    /// <summary>PlayerMovement.enabled as it was BEFORE the map locked it.</summary>
    bool playerMovementPriorState = true;
    bool playerMovementLocked;
    bool saveBlockerHeld;
    bool hudHiddenByMap;

    string lastSignature = "";
    string lastScanKey = "";
    float nextPollTime;
    float nextRescanTime;
    float nextHudRenderTime;
    int playerInstanceID = -1;

    Bounds sceneBounds;
    bool hasSceneBounds;

    CanvasScaler canvasScaler;

    /// <summary>The gameplay HUD was hidden by something else: the minimap is inert.</summary>
    bool hudSuppressed;
    /// <summary>The collapse right now was caused by that hide, so the HUD must not be re-shown.</summary>
    bool suppressNextHudRestore;
    /// <summary>The pause menu is up: the minimap is inert, exactly like hudSuppressed.</summary>
    bool pauseSuppressed;

    readonly List<ParticleSystemRenderer> particleRenderers = new List<ParticleSystemRenderer>();
    readonly List<bool> particleRendererEnabled = new List<bool>();
    bool particlesSuppressedForRender;

    GameObject wallSilhouetteGo;
    MeshFilter wallSilhouetteFilter;
    MeshRenderer wallSilhouetteRenderer;
    Material wallSilhouetteMaterial;
    Mesh wallSilhouetteMesh;
    bool lastDrawWallsSetting;

    float pinchStartDistance;
    float pinchStartZoom;
    bool warnedMissingEventSystem;

    // === Lifecycle ======================================================================

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Minimap] A second MinimapController is in this scene; destroying '" +
                             gameObject.name + "'.");
            Destroy(gameObject);
            return;
        }
        Instance = this;

        MinimapSprites.Build();
        BuildCanvas();
        BuildMapCamera();
        BuildHudMinimap();
        BuildFullscreenMap();

        // Nothing can be positioned yet: the canvas scaler has not resolved and the
        // managers may not have their data, so the first real pass happens in Start.
        building = true;
    }

    void Start()
    {
        ResolveSceneContext();
        SubscribeToProgressEvents();

        // The scene is settled by now: the wall colliders and particle systems
        // can be cached for the map render.
        RebuildWallSilhouette();
        RefreshParticleCache();

        ComputeSceneBounds();
        CentreOnPlayerForHud();
        RebuildMarkers();
        RefreshQuestPanel();

        lastSignature = MinimapLogic.BuildStateSignature(resolvedSanctumID);

        // MissionTabletQuests.json is fetched asynchronously on Android, and the
        // DontDestroyOnLoad managers may not exist in the first frame of a scene
        // launched straight from the editor, so keep re-checking for a moment
        // instead of trusting that everything was ready at Start.
        nextRescanTime = Time.unscaledTime + 1f;
        nextPollTime = Time.unscaledTime + statePollInterval;
        building = false;
    }

    void OnEnable()
    {
        // Re-enabled after being switched off (a pause menu, a scene toggle): the
        // signature is no longer trustworthy, so force a re-check.
        lastSignature = "";
        lastScanKey = "";
        nextPollTime = 0f;
        nextRescanTime = 0f;
    }

    void OnDestroy()
    {
        destroyed = true;
        UnsubscribeFromProgressEvents();

        // Order matters: release the save blocker and the movement lock before the
        // objects that hold them go away, so a scene unload with the map still open
        // cannot strand either one.
        ReleaseSaveBlocker();
        RestorePlayerMovement();
        RestoreGameplayHud();
        // onDestroy of a destroyed-while-suppressed map, also drop the map-owned
        // hide flag: whoever owns the flag next should start clean.
        if (HUDController.Instance != null) HUDController.Instance.SetHiddenByMap(false);

        if (mapTexture != null)
        {
            mapTexture.Release();
            Destroy(mapTexture);
            mapTexture = null;
        }
        if (mapCamera != null)
        {
            mapCamera.targetTexture = null;
            if (mapCamera.gameObject != null) Destroy(mapCamera.gameObject);
            mapCamera = null;
        }

        DestroyWallSilhouetteMesh();
        if (wallSilhouetteGo != null)
        {
            Destroy(wallSilhouetteGo);
            wallSilhouetteGo = null;
        }
        if (wallSilhouetteMaterial != null)
        {
            Destroy(wallSilhouetteMaterial);
            wallSilhouetteMaterial = null;
        }

        markers.Clear();
        entranceCache.Clear();
        MinimapSprites.DisposeIfUnowned(Instance == this);
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (building || destroyed) return;

        PollHudVisibility();
        PollPauseVisibility();
        // An encounter, a cutscene or the pause menu has the minimap suppressed:
        // stay fully inert, no render, no raycasts, no toggle key, until it lifts.
        if (hudSuppressed || pauseSuppressed) return;

        if (toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
            SetExpanded(mode != MapMode.Fullscreen);

        if (drawWalls != lastDrawWallsSetting) RebuildWallSilhouette(); // live inspector tinkering

        PollProgressState();
        RescanMarkersIfDue();

        if (mode == MapMode.Hud)
        {
            CentreOnPlayerForHud();
            RenderHudIfDue();
            LayoutMarkers(hudMarkerParent, hudClipRect, true);
        }
        else
        {
            HandleFullscreenInput();
            EaseCamera();
            RenderNow();
            LayoutMarkers(fullMarkerParent, viewportRect, false);
        }
    }

    // === Public API =====================================================================

    public bool IsExpanded { get { return mode == MapMode.Fullscreen; } }

    /// <summary>The top-down map camera, for the drag handler's pixel conversions.</summary>
    public Camera MapCamera { get { return mapCamera; } }

    /// <summary>The fullscreen map surface, for the drag handler's pixel conversions.</summary>
    public RectTransform ViewportRect { get { return viewportRect; } }

    public void ShowMap() { if (!Suppressed()) SetExpanded(true); }

    public void HideMap() { SetExpanded(false); }

    public void ToggleMap() { if (!Suppressed()) SetExpanded(mode != MapMode.Fullscreen); }

    /// <summary>True while the map is forced inert: gameplay HUD hidden or paused.</summary>
    bool Suppressed() { return hudSuppressed || pauseSuppressed; }

    /// <summary>
    /// Force a complete re-derivation from current progress. Public so a cutscene,
    /// a debug jumper or anything else that just mutated progress can make the map
    /// correct now rather than at the next poll.
    /// </summary>
    public void RefreshNow()
    {
        if (destroyed) return;
        ResolveSceneContext();
        ComputeSceneBounds();
        RebuildMarkers();
        RefreshQuestPanel();
        lastSignature = MinimapLogic.BuildStateSignature(resolvedSanctumID);
        lastScanKey = BuildScanKey();
    }

    // === Save / load reactivity =========================================================

    /// <summary>
    /// The load-detection heartbeat: compares the discrete state the markers come
    /// from and re-renders on any difference. This is what makes a loaded save
    /// correct without the minimap knowing anything about SaveLoadManager.
    /// </summary>
    void PollProgressState()
    {
        if (Time.unscaledTime < nextPollTime) return;
        nextPollTime = Time.unscaledTime + statePollInterval;

        string signature = MinimapLogic.BuildStateSignature(resolvedSanctumID);
        Transform livePlayer = FindPlayerTransform();
        bool playerReplaced = livePlayer != null && livePlayer.GetInstanceID() != playerInstanceID;

        if (signature == lastSignature && !playerReplaced) return;

        bool progressChanged = signature != lastSignature && !string.IsNullOrEmpty(lastSignature);
        lastSignature = signature;

        if (playerReplaced)
        {
            // A save load or sanctum respawn can hand back a different Player
            // object; without this the arrow would keep tracking the dead one.
            playerTransform = livePlayer;
            playerInstanceID = livePlayer.GetInstanceID();
            playerMovement = livePlayer.GetComponent<PlayerMovement>();
            if (playerMovementLocked && playerMovement != null) playerMovement.enabled = false;
        }

        if (!progressChanged) return;

        Debug.Log("[Minimap] Progress changed (save load, quest step, tablet or XP) - re-deriving markers.");
        ComputeSceneBounds();
        RebuildMarkers();
        RefreshQuestPanel();
    }

    /// <summary>
    /// World objects are not guaranteed to exist at Start: tablets stream in with
    /// the scene, the boss zone may sit under a parent that is still activating.
    /// Re-scan until the set that was found stops changing.
    /// </summary>
    void RescanMarkersIfDue()
    {
        if (Time.unscaledTime < nextRescanTime) return;
        nextRescanTime = Time.unscaledTime + markerRescanInterval;
        RefreshParticleCache(); // fresh bursts and new systems join the exclusion

        string scanKey = BuildScanKey();
        if (scanKey == lastScanKey) return;
        lastScanKey = scanKey;

        ComputeSceneBounds();
        RebuildMarkers();
        RefreshQuestPanel();
    }

    /// <summary>
    /// Fingerprint of what is in the scene (tablet count, boss zone present,
    /// entrances resolved, measured extent). Only changes matter: a scene whose
    /// object set is stable does not need its markers rebuilt every interval.
    /// </summary>
    string BuildScanKey()
    {
        int tablets = 0;
        if (showTabletMarkers && !isMainMapScene && !string.IsNullOrEmpty(resolvedSanctumID))
        {
            TabletMissionObject[] found = FindObjectsOfType<TabletMissionObject>();
            tablets = found != null ? found.Length : 0;
        }

        bool boss = showBossMarker && FindBossZone() != null;
        // Intro-pending state flips the guide/tablet marker set, so it must be
        // part of the fingerprint — the 2s rescan rebuilds when dialogue ends.
        bool introPending = showTabletMarkers && !isMainMapScene && GuideIntroPending();
        int entrances = isMainMapScene ? ResolveEntrances().Count : 0;
        string extent = hasSceneBounds
            ? Mathf.RoundToInt(sceneBounds.size.x) + "x" + Mathf.RoundToInt(sceneBounds.size.z)
            : "unbound";

        return tablets + ":" + (boss ? "b" : "-") + (introPending ? ":g" : "") + ":" + entrances + ":" + extent;
    }

    void ResolveSceneContext()
    {
        resolvedSanctumID = MinimapLogic.ResolveSanctumID(sanctumID);
        isMainMapScene = MinimapLogic.IsMainMapScene(MinimapLogic.CurrentSceneName());

        Debug.Log("[Minimap] Scene '" + MinimapLogic.CurrentSceneName() + "' -> " +
                  (isMainMapScene ? "overworld (MainMap)"
                   : (!string.IsNullOrEmpty(resolvedSanctumID)
                        ? "sanctum '" + resolvedSanctumID + "'"
                        : "no sanctum content (position map only)")));
    }

    // === Event bindings ================================================================

    XPManager boundXP;
    QuestManager boundQuest;
    MissionTabletManager boundTablets;

    void SubscribeToProgressEvents()
    {
        // These managers are DontDestroyOnLoad singletons that can be replaced (a
        // new session, a reload), so remember exactly which instances were hooked:
        // the unsubscribe must hit those same objects, not whatever is current.
        if (XPManager.Instance != null && !ReferenceEquals(boundXP, XPManager.Instance))
        {
            if (boundXP != null) boundXP.OnXPChanged -= HandleProgressEvent;
            boundXP = XPManager.Instance;
            boundXP.OnXPChanged += HandleProgressEvent;
        }

        if (QuestManager.Instance != null && !ReferenceEquals(boundQuest, QuestManager.Instance))
        {
            if (boundQuest != null) boundQuest.OnActiveQuestChanged -= HandleQuestEvent;
            boundQuest = QuestManager.Instance;
            boundQuest.OnActiveQuestChanged += HandleQuestEvent;
        }

        if (MissionTabletManager.Instance != null && !ReferenceEquals(boundTablets, MissionTabletManager.Instance))
        {
            if (boundTablets != null) boundTablets.MissionsLoaded -= HandleProgressEvent;
            boundTablets = MissionTabletManager.Instance;
            boundTablets.MissionsLoaded += HandleProgressEvent;
        }

        // SanctumManager is scene-local but announces through static events, so
        // these are how a boss unlock or defeat becomes visible on the map instantly.
        SanctumManager.OnBossUnlockedEvent -= HandleSanctumEvent;
        SanctumManager.OnBossUnlockedEvent += HandleSanctumEvent;
        SanctumManager.OnBossDefeatedEvent -= HandleSanctumEvent;
        SanctumManager.OnBossDefeatedEvent += HandleSanctumEvent;
        SanctumManager.OnSanctumCleared -= HandleSanctumEvent;
        SanctumManager.OnSanctumCleared += HandleSanctumEvent;

        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    void UnsubscribeFromProgressEvents()
    {
        if (boundXP != null) boundXP.OnXPChanged -= HandleProgressEvent;
        if (boundQuest != null) boundQuest.OnActiveQuestChanged -= HandleQuestEvent;
        if (boundTablets != null) boundTablets.MissionsLoaded -= HandleProgressEvent;

        SanctumManager.OnBossUnlockedEvent -= HandleSanctumEvent;
        SanctumManager.OnBossDefeatedEvent -= HandleSanctumEvent;
        SanctumManager.OnSanctumCleared -= HandleSanctumEvent;

        SceneManager.sceneLoaded -= HandleSceneLoaded;

        boundXP = null;
        boundQuest = null;
        boundTablets = null;
    }

    /// <summary>
    /// Every progress notification does the same thing: ask for the next signature
    /// check now. The poll already distinguishes a real change from noise, so an
    /// event can only remove latency, never cause a double render.
    /// </summary>
    void HandleProgressEvent() { nextPollTime = 0f; }

    void HandleProgressEvent(int xpAmount) { nextPollTime = 0f; }

    void HandleQuestEvent(QuestManager.QuestEntry entry) { nextPollTime = 0f; }

    void HandleSanctumEvent(string sanctum) { nextPollTime = 0f; }

    void HandleSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        if (destroyed) return;

        // Normally this component is destroyed with its scene and the next scene
        // builds its own. Additive loads and same-scene reloads - which is what a
        // save load does - are the cases that would otherwise keep showing a map of
        // a scene the player has left, with movement still locked.
        if (mode == MapMode.Fullscreen) SetExpanded(false);

        ResolveSceneContext();
        RebuildWallSilhouette();
        RefreshParticleCache();
        lastScanKey = "";
        nextRescanTime = Time.unscaledTime + 0.5f;

        // The managers have just been re-hydrated from the save: make the map
        // correct on the next tick rather than after the poll interval.
        nextPollTime = 0f;
    }

    // === Mode side effects (the project's MissionTabletUI pattern) =====================

    void SetExpanded(bool expanded)
    {
        if (building || destroyed || fullRoot == null || hudRoot == null) return;
        if (expanded == (mode == MapMode.Fullscreen)) return;
        if (expanded && hudSuppressed) return; // the gameplay HUD is hidden: the map stays closed

        if (expanded)
        {
            mode = MapMode.Fullscreen;
            fullRoot.SetActive(true);
            hudRoot.SetActive(false);

            HideGameplayHud();
            LockPlayerMovement();
            HoldSaveBlocker();

            ComputeSceneBounds();

            float fitZoom = FitZoomForScene();
            targetZoom = Mathf.Lerp(fitZoom, Mathf.Max(zoomMin, hudZoom), openOnPlayerBlend);
            currentZoom = targetZoom;
            mapCentre = Vector3.Lerp(FitCentre(), PlayerPositionXZ(), openOnPlayerBlend);

            ResizeTextureForMode();
            ClampMapCentre();
            ApplyCamera();
            RenderNow();
            RebuildMarkers();
            RefreshQuestPanel();

            Debug.Log("[Minimap] Expanded to the full map of '" +
                      (isMainMapScene ? MinimapLogic.CurrentSceneName() : resolvedSanctumID) + "'.");
        }
        else
        {
            mode = MapMode.Hud;
            fullRoot.SetActive(false);
            hudRoot.SetActive(true);

            ReleaseSaveBlocker();
            RestorePlayerMovement();
            RestoreGameplayHud();

            ResizeTextureForMode();
            CentreOnPlayerForHud();
            RebuildMarkers();
        }
    }

    void HideGameplayHud()
    {
        if (HUDController.Instance == null) return;
        // Map-owned hide state, separate from the gameplay code's SetVisible flag:
        // the two cannot overwrite each other's bookkeeping.
        HUDController.Instance.SetHiddenByMap(true);
        hudHiddenByMap = true;
    }

    void RestoreGameplayHud()
    {
        // A suppression-driven collapse is NOT the map hiding the HUD: the gameplay
        // code (an enemy encounter) already took it away, and re-showing it here
        // would break that encounter on exit. Leave their state exactly as it was.
        if (suppressNextHudRestore)
        {
            suppressNextHudRestore = false;
            hudHiddenByMap = false;
            return;
        }
        if (!hudHiddenByMap) return;
        hudHiddenByMap = false;
        if (HUDController.Instance != null) HUDController.Instance.SetHiddenByMap(false);
    }

    void LockPlayerMovement()
    {
        PlayerMovement pm = ResolvePlayerMovement();
        if (pm == null || playerMovementLocked) return;

        playerMovement = pm;
        // Remember, never assume: a dialogue or an encounter may already have
        // locked movement, and closing the map must not unlock the player in the
        // middle of either.
        playerMovementPriorState = pm.enabled;
        pm.enabled = false;
        playerMovementLocked = true;
    }

    void RestorePlayerMovement()
    {
        if (!playerMovementLocked) return;
        playerMovementLocked = false;
        if (playerMovement != null) playerMovement.enabled = playerMovementPriorState;
    }

    void HoldSaveBlocker()
    {
        if (saveBlockerHeld) return;
        if (SaveRestrictionEnforcer.Instance == null)
        {
            Debug.LogWarning("[Minimap] No SaveRestrictionEnforcer in this build, so an autosave " +
                             "could fire while the map is open. The view changes no progress, so " +
                             "that is untidy rather than unsafe.");
            return;
        }
        SaveRestrictionEnforcer.Instance.AddBlocker(SaveBlockerKey);
        saveBlockerHeld = true;
    }

    void ReleaseSaveBlocker()
    {
        if (!saveBlockerHeld) return;
        saveBlockerHeld = false;
        if (SaveRestrictionEnforcer.Instance != null)
            SaveRestrictionEnforcer.Instance.RemoveBlocker(SaveBlockerKey);
    }

    // === Gameplay HUD visibility follow (encounter / cutscene parity) ===================

    /// <summary>
    /// True unless a NON-map owner of the gameplay HUD hid it. This reads the
    /// ownership flag HUDController.SetVisible writes (not the activeSelf bit), so
    /// an encounter hiding the HUD is still detected even while this map has its
    /// own hide flag down at the same time. A missing HUD controller means there is
    /// nothing to follow, and this simply never reports hidden.
    /// </summary>
    bool GameplayHudVisible()
    {
        HUDController hud = HUDController.Instance;
        if (hud == null) return true;
        return !hud.GameplayWantsHudHidden;
    }

    /// <summary>
    /// Runs every Update, before anything else. While the gameplay HUD is hidden by
    /// anything that is not this map, the minimap goes fully inert: its canvas is
    /// disabled (no disc, no raycasts, and the toggle key is ignored in Update).
    /// When the HUD returns, the minimap comes back exactly as it was.
    /// </summary>
    void PollHudVisibility()
    {
        if (canvas == null) return;
        if (!followHudVisibility) return;

        // GameplayHudVisible reads the gameplay-owned flag only, so this is safe to
        // watch while the map itself has the other hide flag down (fullscreen): if
        // an encounter hides the HUD under an open map, the map collapses WITHOUT
        // touching the encounter's state.
        bool hudVisible = GameplayHudVisible();
        if (hudVisible)
        {
            if (hudSuppressed)
            {
                hudSuppressed = false;
                // The pause menu may still be up; it owns the canvas off switch too,
                // so only the flag clears here until its own watcher lifts as well.
                if (!pauseSuppressed)
                {
                    if (canvas != null) canvas.enabled = true;
                    nextRescanTime = 0f;      // the world may have changed while inert
                    if (mode == MapMode.Hud) CentreOnPlayerForHud(); // the player may have moved; cheap
                }
            }
            return;
        }

        // The gameplay HUD was hidden by something else: do not outlive it.
        if (mode == MapMode.Fullscreen)
        {
            suppressNextHudRestore = true;   // this collapse must not re-show the HUD
            SetExpanded(false);              // releases movement lock + save blocker, HUD untouched
        }
        hudSuppressed = true;
        if (canvas != null) canvas.enabled = false;
    }

    /// <summary>
    /// Runs every Update right after PollHudVisibility. The pause menu pauses by
    /// Time.timeScale = 0 behind its own overlay and never hides the gameplay HUD,
    /// so HUD-following alone cannot see it. While PauseMenuManager reports paused
    /// the minimap is inert exactly as when an encounter hides the HUD: its canvas
    /// is disabled (no disc, no raycasts) and an open fullscreen map collapses
    /// without releasing pause's ownership of the screen. Update still runs while
    /// the clock is stopped, so the map returns the instant Resume() runs.
    /// </summary>
    void PollPauseVisibility()
    {
        if (canvas == null) return;
        if (!hideWhenPaused) return;

        PauseMenuManager pause = PauseMenuManager.Instance;
        bool paused = pause != null && pause.IsPaused;
        if (!paused)
        {
            if (pauseSuppressed)
            {
                pauseSuppressed = false;
                // An encounter may still have the HUD hidden; then ITS watcher owns
                // bringing the canvas back, not this one.
                if (!hudSuppressed)
                {
                    if (canvas != null) canvas.enabled = true;
                    nextRescanTime = 0f;      // the world may have changed while inert
                    if (mode == MapMode.Hud) CentreOnPlayerForHud(); // re-centre after being away
                }
            }
            return;
        }

        if (pauseSuppressed) return;
        pauseSuppressed = true;
        if (mode == MapMode.Fullscreen)
        {
            suppressNextHudRestore = true;   // this collapse must not re-show the HUD; pause owns the screen
            SetExpanded(false);              // releases movement lock + save blocker, HUD untouched
        }
        canvas.enabled = false;
    }

    // === Particles: kept out of the map render ===========================================

    /// <summary>
    /// The map camera draws the scene, so whatever the world shows the map shows -
    /// including particle clutter. The fix is scoped to one render: the cached
    /// particle renderers are switched off just before mapCamera.Render() and
    /// restored immediately afterwards, so the gameplay view never notices.
    /// </summary>
    void RefreshParticleCache()
    {
        particleRenderers.Clear();
        if (!hideParticleSystems) return;
        ParticleSystemRenderer[] found = FindObjectsOfType<ParticleSystemRenderer>();
        if (found == null) return;
        for (int i = 0; i < found.Length; i++)
            if (found[i] != null) particleRenderers.Add(found[i]);
    }

    void SuppressParticlesForMapRender()
    {
        if (!hideParticleSystems || particlesSuppressedForRender) return;
        particlesSuppressedForRender = true;
        particleRendererEnabled.Clear();
        for (int i = 0; i < particleRenderers.Count; i++)
        {
            ParticleSystemRenderer particleRenderer = particleRenderers[i];
            if (particleRenderer == null) { particleRendererEnabled.Add(false); continue; }
            particleRendererEnabled.Add(particleRenderer.enabled);
            if (particleRenderer.enabled) particleRenderer.enabled = false;
        }
    }

    void RestoreParticlesAfterMapRender()
    {
        if (!particlesSuppressedForRender) return;
        particlesSuppressedForRender = false;
        for (int i = 0; i < particleRenderers.Count && i < particleRendererEnabled.Count; i++)
        {
            ParticleSystemRenderer particleRenderer = particleRenderers[i];
            // Only restore what was on before: anything someone else disabled in
            // between stays disabled.
            if (particleRenderer != null && particleRendererEnabled[i] && !particleRenderer.enabled)
                particleRenderer.enabled = true;
        }
    }

    // === Wall silhouettes: collider footprints drawn onto the map ========================

    const float WallLift = 0.15f;        // above a collider's floor: no z-fight with the ground
    const int WallDiscSegments = 20;
    const int WallVertexBudget = 300000; // sanity cap for pathological MeshColliders

    /// <summary>
    /// Rebuilds this scene's wall shapes into one combined mesh in world space.
    /// Its renderer is normally DISABLED; RenderNow switches it on for the map
    /// camera's render alone, so the game camera never sees it and nothing appears
    /// in the world. Works from the Box / Capsule / Mesh colliders already in the
    /// scene; triggers and MiniMapIgnore tag-gets are skipped.
    /// </summary>
    void RebuildWallSilhouette()
    {
        lastDrawWallsSetting = drawWalls;
        DestroyWallSilhouetteMesh();
        if (!drawWalls) return;

        if (wallSilhouetteGo == null)
        {
            // Deliberately unparented: baked-in world coordinates must not inherit
            // any non-identity scale from this component's transform.
            wallSilhouetteGo = new GameObject("MinimapWallSilhouette");
            wallSilhouetteGo.layer = gameObject.layer;
            wallSilhouetteFilter = wallSilhouetteGo.AddComponent<MeshFilter>();
            wallSilhouetteRenderer = wallSilhouetteGo.AddComponent<MeshRenderer>();
            wallSilhouetteRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wallSilhouetteRenderer.receiveShadows = false;
            wallSilhouetteRenderer.enabled = false;
        }

        List<Vector3> vertices = new List<Vector3>(4096);
        List<int> triangles = new List<int>(4096);

        BoxCollider[] boxes = FindObjectsOfType<BoxCollider>();
        for (int i = 0; i < boxes.Length; i++) AddBoxFootprint(boxes[i], vertices, triangles);

        CapsuleCollider[] capsules = FindObjectsOfType<CapsuleCollider>();
        for (int i = 0; i < capsules.Length; i++) AddCapsuleFootprint(capsules[i], vertices, triangles);

        MeshCollider[] meshColliders = FindObjectsOfType<MeshCollider>();
        for (int i = 0; i < meshColliders.Length; i++) AddMeshFootprint(meshColliders[i], vertices, triangles);

        if (vertices.Count == 0 || vertices.Count >= WallVertexBudget) return; // no walls, or too many

        wallSilhouetteMesh = new Mesh();
        wallSilhouetteMesh.name = "MinimapWalls";
        wallSilhouetteMesh.indexFormat = vertices.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        wallSilhouetteMesh.SetVertices(vertices);
        wallSilhouetteMesh.SetTriangles(triangles, 0, false);
        wallSilhouetteMesh.RecalculateBounds();
        wallSilhouetteFilter.sharedMesh = wallSilhouetteMesh;

        if (wallSilhouetteMaterial == null)
        {
            wallSilhouetteMaterial = CreateWallMaterial();
            if (wallSilhouetteMaterial == null)
            {
                Debug.LogWarning("[Minimap] No tintable unlit shader is compiled into the build, " +
                                 "so wall silhouettes are skipped. drawWalls wants an ordinary " +
                                 "Sprites/Default or Unlit/Color shader referenced somewhere.");
                return;
            }
        }
        wallSilhouetteRenderer.sharedMaterial = wallSilhouetteMaterial;
        wallSilhouetteRenderer.enabled = false;

        if (IsIgnoredLayer(wallSilhouetteGo))
            Debug.LogWarning("[Minimap] The wall silhouette sits on layer '" +
                             LayerMask.LayerToName(wallSilhouetteGo.layer) + "', which the map " +
                             "camera culls; move the Minimap object off the ignored layers if " +
                             "you want walls drawn.");
    }

    void DestroyWallSilhouetteMesh()
    {
        if (wallSilhouetteMesh != null)
        {
            Destroy(wallSilhouetteMesh);
            wallSilhouetteMesh = null;
        }
        if (wallSilhouetteFilter != null) wallSilhouetteFilter.sharedMesh = null;
    }

    Material CreateWallMaterial()
    {
        string[] candidates = new string[] { "Sprites/Default", "Unlit/Color" };
        for (int i = 0; i < candidates.Length; i++)
        {
            Shader shader = Shader.Find(candidates[i]);
            if (shader == null) continue; // not compiled into this build
            Material material = new Material(shader);
            material.name = "MinimapWallMaterial";
            return material;
        }
        return null;
    }

    /// <summary>Culls helper for walls, matching the scene-bounds rule.</summary>
    bool TaggedIgnoreBounds(GameObject go)
    {
        return !string.IsNullOrEmpty(ignoreBoundsTag) && go.CompareTag(ignoreBoundsTag);
    }

    void AddBoxFootprint(BoxCollider box, List<Vector3> vertices, List<int> triangles)
    {
        if (box == null || !box.enabled || box.isTrigger) return;
        if (!box.gameObject.activeInHierarchy) return;
        if (IsIgnoredLayer(box.gameObject) || TaggedIgnoreBounds(box.gameObject)) return;
        if (box.bounds.size.y < wallMinHeight) return;

        Vector3 half = box.size * 0.5f;
        Transform t = box.transform;
        Vector3 c0 = t.TransformPoint(new Vector3(box.center.x - half.x, box.center.y - half.y, box.center.z - half.z));
        Vector3 c1 = t.TransformPoint(new Vector3(box.center.x + half.x, box.center.y - half.y, box.center.z - half.z));
        Vector3 c2 = t.TransformPoint(new Vector3(box.center.x + half.x, box.center.y - half.y, box.center.z + half.z));
        Vector3 c3 = t.TransformPoint(new Vector3(box.center.x - half.x, box.center.y - half.y, box.center.z + half.z));

        // The bottom face's ring, projected to a single horizontal plane: a flat
        // footprint exactly like the wall reads from directly above.
        float y = Mathf.Min(Mathf.Min(c0.y, c1.y), Mathf.Min(c2.y, c3.y)) + WallLift;
        EmitFlatQuad(vertices, triangles,
                     new Vector2(c0.x, c0.z), new Vector2(c1.x, c1.z),
                     new Vector2(c2.x, c2.z), new Vector2(c3.x, c3.z), y);
    }

    void AddCapsuleFootprint(CapsuleCollider capsule, List<Vector3> vertices, List<int> triangles)
    {
        if (capsule == null || !capsule.enabled || capsule.isTrigger) return;
        if (!capsule.gameObject.activeInHierarchy) return;
        if (IsIgnoredLayer(capsule.gameObject) || TaggedIgnoreBounds(capsule.gameObject)) return;
        if (capsule.bounds.size.y < wallMinHeight) return;

        Vector3 scale = capsule.transform.lossyScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        Vector3 centre = capsule.transform.TransformPoint(capsule.center);
        float y = capsule.bounds.min.y + WallLift;

        float radiusX, radiusZ;
        if (capsule.direction == 1)
        {
            // Standing up: the footprint is the pair of caps seen from above.
            radiusX = capsule.radius * Mathf.Max(scale.x, scale.z);
            radiusZ = radiusX;
        }
        else
        {
            // Lying down: an approximate elongated footprint along the capsule axis.
            float halfLength = Mathf.Max(capsule.radius, capsule.height * 0.5f);
            radiusX = (capsule.direction == 0 ? halfLength : capsule.radius) * Mathf.Max(scale.x, 0.0001f);
            radiusZ = (capsule.direction == 2 ? halfLength : capsule.radius) * Mathf.Max(scale.z, 0.0001f);
        }
        EmitFlatDisc(vertices, triangles, new Vector2(centre.x, centre.z), radiusX, radiusZ, y);
    }

    void AddMeshFootprint(MeshCollider collider, List<Vector3> vertices, List<int> triangles)
    {
        if (collider == null || collider.sharedMesh == null || !collider.enabled || collider.isTrigger) return;
        if (!collider.gameObject.activeInHierarchy) return;
        if (IsIgnoredLayer(collider.gameObject) || TaggedIgnoreBounds(collider.gameObject)) return;
        if (collider.bounds.size.y < wallMinHeight) return;
        if (vertices.Count >= WallVertexBudget) return;

        Vector3[] meshVertices = collider.sharedMesh.vertices;
        int[] meshTriangles = collider.sharedMesh.triangles;
        if (meshVertices == null || meshTriangles == null) return;

        Matrix4x4 localToWorld = collider.transform.localToWorldMatrix;
        float y = collider.bounds.min.y + WallLift;
        // Every mesh triangle projected to the same plane: flat, both windings.
        for (int i = 0; i + 2 < meshTriangles.Length && vertices.Count < WallVertexBudget; i += 3)
        {
            Vector3 a = localToWorld.MultiplyPoint3x4(meshVertices[meshTriangles[i]]); a.y = y;
            Vector3 b = localToWorld.MultiplyPoint3x4(meshVertices[meshTriangles[i + 1]]); b.y = y;
            Vector3 c = localToWorld.MultiplyPoint3x4(meshVertices[meshTriangles[i + 2]]); c.y = y;
            int start = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
        }
    }

    /// <summary>One quad from a ring of corners, both windings, at a fixed height.</summary>
    void EmitFlatQuad(List<Vector3> vertices, List<int> triangles, Vector2 a, Vector2 b,
                      Vector2 c, Vector2 d, float y)
    {
        int start = vertices.Count;
        vertices.Add(new Vector3(a.x, y, a.y));
        vertices.Add(new Vector3(b.x, y, b.y));
        vertices.Add(new Vector3(c.x, y, c.y));
        vertices.Add(new Vector3(d.x, y, d.y));
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
        triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
    }

    void EmitFlatDisc(List<Vector3> vertices, List<int> triangles, Vector2 centre,
                      float radiusX, float radiusZ, float y)
    {
        for (int i = 0; i < WallDiscSegments; i++)
        {
            float a0 = (i / (float)WallDiscSegments) * 2f * Mathf.PI;
            float a1 = ((i + 1) / (float)WallDiscSegments) * 2f * Mathf.PI;
            int start = vertices.Count;
            vertices.Add(new Vector3(centre.x, y, centre.y));
            vertices.Add(new Vector3(centre.x + Mathf.Cos(a0) * radiusX, y, centre.y + Mathf.Sin(a0) * radiusZ));
            vertices.Add(new Vector3(centre.x + Mathf.Cos(a1) * radiusX, y, centre.y + Mathf.Sin(a1) * radiusZ));
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
        }
    }

    // === Map camera =====================================================================

    void BuildMapCamera()
    {
        GameObject camGo = new GameObject("MinimapCamera");
        camGo.transform.SetParent(transform, false);
        mapCamera = camGo.AddComponent<Camera>();

        mapCamera.orthographic = true;
        mapCamera.orthographicSize = Mathf.Max(1f, hudZoom);
        mapCamera.nearClipPlane = 0.5f;
        mapCamera.farClipPlane = mapCameraHeight * 4f;
        // Straight down, north-up: screen +X is world +X and screen up is world +Z.
        mapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        mapCamera.clearFlags = CameraClearFlags.SolidColor;
        mapCamera.backgroundColor = mapBackgroundColor;
        mapCamera.allowMSAA = false;
        mapCamera.allowHDR = false;
        // Rendering is manual: the disc at ~20fps, the map at display rate, instead
        // of a second full scene render on top of the game camera every frame.
        mapCamera.enabled = false;
        mapCamera.cullingMask = BuildCullingMask();

        textureWidth = Mathf.Max(128, mapTextureResolution);
        textureHeight = textureWidth;
        mapTexture = new RenderTexture(textureWidth, textureHeight, 16, RenderTextureFormat.Default);
        mapTexture.name = "MinimapTexture";
        mapTexture.filterMode = FilterMode.Bilinear;
        mapTexture.Create();
        mapCamera.targetTexture = mapTexture;
    }

    int BuildCullingMask()
    {
        int mask = ~0;
        if (ignoredLayerNames == null) return mask;
        for (int i = 0; i < ignoredLayerNames.Length; i++)
        {
            int layer = LayerMask.NameToLayer(ignoredLayerNames[i] ?? "");
            if (layer >= 0) mask &= ~(1 << layer);
        }
        return mask;
    }

    bool IsIgnoredLayer(GameObject obj)
    {
        if (obj == null || ignoredLayerNames == null) return false;
        for (int i = 0; i < ignoredLayerNames.Length; i++)
        {
            int layer = LayerMask.NameToLayer(ignoredLayerNames[i] ?? "");
            if (layer >= 0 && obj.layer == layer) return true;
        }
        return false;
    }

    void RenderNow()
    {
        if (mapCamera == null || mapTexture == null) return;

        // One scoped render: particles hidden and walls showing, then restored.
        bool wallsShown = false;
        SuppressParticlesForMapRender();
        try
        {
            if (drawWalls && lastDrawWallsSetting && wallSilhouetteMesh != null &&
                wallSilhouetteRenderer != null && wallSilhouetteMaterial != null &&
                !IsIgnoredLayer(wallSilhouetteGo))
            {
                wallSilhouetteMaterial.color = wallColor;
                wallSilhouetteRenderer.enabled = true;
                wallsShown = true;
            }
            mapCamera.Render();
        }
        finally
        {
            // try/finally: even an exception mid-render cannot leave wall
            // silhouettes or hidden particles leaking into the gameplay view.
            if (wallsShown && wallSilhouetteRenderer != null) wallSilhouetteRenderer.enabled = false;
            RestoreParticlesAfterMapRender();
        }
    }

    void RenderHudIfDue()
    {
        if (hudRenderInterval <= 0f) { RenderNow(); return; }
        if (Time.unscaledTime < nextHudRenderTime) return;
        nextHudRenderTime = Time.unscaledTime + hudRenderInterval;
        RenderNow();
    }

    /// <summary>
    /// A square texture for the circular disc and an aspect-matched one for the
    /// wide map: sampling square pixels through a RawImage with a different aspect
    /// is how a map ends up stretched, so the texture follows the mode.
    /// </summary>
    void ResizeTextureForMode()
    {
        if (mapCamera == null) return;

        int width = Mathf.Max(128, mapTextureResolution);
        int height = width;

        if (mode == MapMode.Fullscreen && viewportRect != null && viewportRect.rect.height > 2f)
        {
            float aspect = viewportRect.rect.width / viewportRect.rect.height;
            if (aspect < 1f) height = Mathf.Max(128, Mathf.RoundToInt(width / Mathf.Max(0.05f, aspect)));
        }

        if (width == textureWidth && height == textureHeight && mapTexture != null) return;

        RenderTexture previous = mapTexture;
        textureWidth = width;
        textureHeight = height;

        mapTexture = new RenderTexture(width, height, 16, RenderTextureFormat.Default);
        mapTexture.name = "MinimapTexture";
        mapTexture.filterMode = FilterMode.Bilinear;
        mapTexture.Create();
        mapCamera.targetTexture = mapTexture;

        RawImage[] rawImages = GetComponentsInChildren<RawImage>(true);
        if (rawImages != null)
        {
            for (int i = 0; i < rawImages.Length; i++)
                if (rawImages[i] != null) rawImages[i].texture = mapTexture;
        }

        if (previous != null)
        {
            previous.Release();
            Destroy(previous);
        }
    }

    void CentreOnPlayerForHud()
    {
        currentZoom = hudZoom;
        targetZoom = hudZoom;
        mapCentre = PlayerPositionXZ();
        ApplyCamera();
    }

    void EaseCamera()
    {
        float t = Mathf.Clamp01(cameraEase * Time.unscaledDeltaTime);
        currentZoom = Mathf.Lerp(currentZoom, targetZoom, t);
        ClampMapCentre();
        ApplyCamera();
    }

    void ApplyCamera()
    {
        if (mapCamera == null) return;

        mapCamera.transform.position = new Vector3(mapCentre.x, mapCameraHeight, mapCentre.z);
        mapCamera.orthographicSize = Mathf.Max(0.5f, currentZoom);

        RectTransform surface = mode == MapMode.Fullscreen ? viewportRect : hudClipRect;
        if (surface != null && surface.rect.width > 1f && surface.rect.height > 1f)
            mapCamera.aspect = surface.rect.width / surface.rect.height;
    }

    /// <summary>Half-height that shows the whole measured scene in the current surface.</summary>
    float FitZoomForScene()
    {
        if (!hasSceneBounds) return Mathf.Max(zoomMin, hudZoom * 2f);

        float aspect = 1f;
        RectTransform surface = mode == MapMode.Fullscreen ? viewportRect : hudClipRect;
        if (surface != null && surface.rect.height > 1f)
            aspect = surface.rect.width / surface.rect.height;

        float halfZ = sceneBounds.extents.z;
        float halfX = sceneBounds.extents.x / Mathf.Max(0.25f, aspect);
        return Mathf.Max(zoomMin, Mathf.Max(halfX, halfZ) * zoomOutPadding);
    }

    Vector3 FitCentre()
    {
        if (!hasSceneBounds) return PlayerPositionXZ();
        return new Vector3(sceneBounds.center.x, 0f, sceneBounds.center.z);
    }

    /// <summary>
    /// Keep the view inside the scene. While the view is smaller than the world the
    /// centre is clamped to the bounds; once zoomed out past the world the centre
    /// snaps back to the scene centre, so panning can never lose the map.
    /// </summary>
    void ClampMapCentre()
    {
        if (!hasSceneBounds) return;

        float halfHeight = currentZoom;
        float halfWidth = currentZoom * (mapCamera != null ? mapCamera.aspect : 1f);

        float centreX = sceneBounds.center.x;
        float centreZ = sceneBounds.center.z;
        float extentX = sceneBounds.extents.x;
        float extentZ = sceneBounds.extents.z;

        mapCentre.x = halfWidth >= extentX
            ? centreX
            : Mathf.Clamp(mapCentre.x, centreX - extentX + halfWidth, centreX + extentX - halfWidth);

        mapCentre.z = halfHeight >= extentZ
            ? centreZ
            : Mathf.Clamp(mapCentre.z, centreZ - extentZ + halfHeight, centreZ + extentZ - halfHeight);
    }

    /// <summary>
    /// Scene extent from what the scene contains: every non-ignored, non-huge
    /// collider, which covers terrain (its collider is its footprint), buildings,
    /// the boss arena and the sanctum entrances. Runs at Start, on mode change and
    /// on re-scan - never per frame.
    /// </summary>
    void ComputeSceneBounds()
    {
        if (useManualBounds)
        {
            sceneBounds = new Bounds(manualBoundsCenter, manualBoundsSize);
            hasSceneBounds = manualBoundsSize.x > 0f && manualBoundsSize.z > 0f;
            return;
        }

        Bounds accumulated = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;

        Collider[] colliders = FindObjectsOfType<Collider>();
        if (colliders != null)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                if (IsIgnoredLayer(collider.gameObject)) continue;
                if (IsIgnoreBoundsTagged(collider.gameObject)) continue;

                Bounds cb = collider.bounds;
                if (cb.size.x <= 0f || cb.size.z <= 0f) continue;
                // A volume as tall as the sky is a gameplay trigger, not geography.
                if (cb.size.y > 400f) continue;

                if (!any) { accumulated = cb; any = true; }
                else accumulated.Encapsulate(cb);
            }
        }

        if (!any)
        {
            hasSceneBounds = false;
            sceneBounds = new Bounds(Vector3.zero, Vector3.zero);
            Debug.LogWarning("[Minimap] No usable colliders found to size the map. The view follows " +
                             "the player with no bounds clamp; set useManualBounds if this scene " +
                             "really has no geometry.");
            return;
        }

        accumulated.Expand(new Vector3(accumulated.size.x * 0.05f, 1f, accumulated.size.z * 0.05f));
        sceneBounds = accumulated;
        hasSceneBounds = true;
    }

    /// <summary>
    /// Cached guard so an unregistered tag in Project Settings only logs once per
    /// play session (CompareTag logs an error every call when the tag is missing).
    /// </summary>
    bool IsIgnoreBoundsTagged(GameObject go)
    {
        if (string.IsNullOrEmpty(ignoreBoundsTag)) return false;

        if (!ignoreTagChecked)
        {
            ignoreTagChecked = true;
#if UNITY_EDITOR
            ignoreTagUsable = TagListContains(ignoreBoundsTag);
#else
            // Player builds can't query the tag list; MinimapTagBootstrap keeps
            // the tag registered in Project Settings for both editor and build.
            ignoreTagUsable = true;
#endif
            if (!ignoreTagUsable)
                Debug.LogWarning("[Minimap] Tag '" + ignoreBoundsTag +
                                 "' is not defined in Project Settings; ignoring the " +
                                 "MiniMapIgnore exclusions for walkable bounds this session.");
        }

        return ignoreTagUsable && go.CompareTag(ignoreBoundsTag);
    }

#if UNITY_EDITOR
    // No System.Linq dependency: plain loop over the editor's registered tag list.
    static bool TagListContains(string tag)
    {
        foreach (string registered in UnityEditorInternal.InternalEditorUtility.tags)
            if (registered == tag) return true;
        return false;
    }
#endif

    // === Player =========================================================================

    Vector3 PlayerPositionXZ()
    {
        Transform player = ResolvePlayerTransform();
        if (player == null)
            return hasSceneBounds ? new Vector3(sceneBounds.center.x, 0f, sceneBounds.center.z) : Vector3.zero;

        Vector3 position = player.position;
        position.y = 0f;
        return position;
    }

    Transform ResolvePlayerTransform()
    {
        if (playerTransform != null) return playerTransform;

        playerTransform = FindPlayerTransform();
        if (playerTransform != null) playerInstanceID = playerTransform.GetInstanceID();
        return playerTransform;
    }

    Transform FindPlayerTransform()
    {
        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null) return tagged.transform;

        PlayerMovement movement = FindObjectOfType<PlayerMovement>();
        return movement != null ? movement.transform : null;
    }

    PlayerMovement ResolvePlayerMovement()
    {
        if (playerMovement != null && playerMovement.transform == playerTransform) return playerMovement;

        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null) return tagged.GetComponent<PlayerMovement>();
        return FindObjectOfType<PlayerMovement>();
    }

    /// <summary>
    /// Player yaw as a UI rotation. The map is north-up (screen up is world +Z) and
    /// the arrow graphic points up, so screen rotation is the negative world yaw -
    /// which also means no map rotation is ever needed.
    /// </summary>
    float ResolvePlayerFacingDegrees()
    {
        Transform player = ResolvePlayerTransform();
        if (player == null) return 0f;

        Vector3 forward = player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) return 0f;

        return -Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    }

    // === Marker construction ===========================================================

    void RebuildMarkers()
    {
        if (building || destroyed) return;

        ClearMarkers();
        if (mapCamera == null) return;

        if (showPlayerMarker)
            AddMarker("player", MarkerShape.Arrow, PlayerColor, 30f, Vector3.zero, true, null);

        bool introPending = GuideIntroPending();

        // Tablet markers stay hidden until the guide's intro dialogue has been
        // finished — the quest flow is: find the guide, talk, THEN the tablets
        // light up on the map.
        if (showTabletMarkers && !introPending && !isMainMapScene && !string.IsNullOrEmpty(resolvedSanctumID))
            AddTabletMarkers();

        if (showBossMarker && !isMainMapScene && !string.IsNullOrEmpty(resolvedSanctumID))
            AddBossMarker();

        // While the guide still has to be spoken to, point the player at them.
        if (!isMainMapScene && introPending)
            AddGuideMarker();

        if (showSanctumMarkersOnMainMap && isMainMapScene)
            AddSanctumMarkers();
    }

    void AddTabletMarkers()
    {
        if (GuideIntroPending()) return; // defence in depth: tablets post-dialogue only

        TabletMissionObject[] tablets = FindObjectsOfType<TabletMissionObject>();
        if (tablets == null) return;

        for (int i = 0; i < tablets.Length; i++)
        {
            TabletMissionObject tablet = tablets[i];
            if (tablet == null || string.IsNullOrEmpty(tablet.missionID)) continue;

            string key = "tablet_" + tablet.missionID;
            if (markers.ContainsKey(key)) continue;

            // The rule that has to survive a save load: an already-solved tablet
            // gets no marker, whether it was solved this session or restored from
            // slot 2. MinimapLogic reads MissionTabletManager rather than the
            // tablet's own GameObject, so a load that completes the mission hides
            // the marker even if the world model has not caught up.
            if (!MinimapLogic.IsTabletMarkerVisible(tablet.missionID)) continue;

            MissionTabletData data = MissionTabletManager.Instance != null
                ? MissionTabletManager.Instance.GetMissionByID(tablet.missionID)
                : null;

            string label = data != null && !string.IsNullOrEmpty(data.displayName)
                ? data.displayName
                : tablet.missionID;

            AddMarker(key, MarkerShape.Diamond, TabletColor, 22f, tablet.transform.position, false, label);
        }
    }

    /// <summary>
    /// True while this sanctum's NPC guide still has to be spoken to. Uses the
    /// same completion signal as the Issue 1 entry lock (dialogue-complete
    /// event, quest proxy, or sequence-advanced proxy), so map, world marker
    /// and exit seal always agree. No SanctumManager in the scene (menus,
    /// MainMap) means nothing is pending.
    /// </summary>
    bool GuideIntroPending()
    {
        if (isMainMapScene) return false;
        SanctumManager manager = FindObjectOfType<SanctumManager>();
        if (manager == null) return false;
        return !manager.IntroDialogueComplete();
    }

    /// <summary>
    /// Single green star at the guide NPC's position while their intro dialogue
    /// is pending. Uses SanctumManager.guideNpcRoot when assigned (matches the
    /// Issue 1 lock's guide list), falling back to the scene's NPCController —
    /// the scene survey showed every sanctum has exactly one guide.
    /// </summary>
    void AddGuideMarker()
    {
        SanctumManager manager = FindObjectOfType<SanctumManager>();
        Transform guide = null;
        if (manager != null && manager.guideNpcRoot != null)
            guide = manager.guideNpcRoot.transform;
        if (guide == null)
        {
            NPCController npc = FindObjectOfType<NPCController>();
            if (npc != null) guide = npc.transform;
        }
        if (guide == null) return;

        AddMarker("guide", MarkerShape.Star, GuideColor, 30f, guide.position, false, "Speak to the guide");
    }

    void AddBossMarker()
    {
        MinimapLogic.BossMarkerState state =
            MinimapLogic.GetBossMarkerState(resolvedSanctumID, showBossMarkerWhileLocked, showBossMarkerAfterDefeat);
        if (state == MinimapLogic.BossMarkerState.Hidden) return;

        Transform bossZone = FindBossZone();
        if (bossZone == null) return;

        Color color = state == MinimapLogic.BossMarkerState.Defeated ? BossDefeatedColor
                    : state == MinimapLogic.BossMarkerState.Locked ? BossLockedColor
                    : BossColor;

        string label = state == MinimapLogic.BossMarkerState.Defeated ? "Guardian defeated" : "Guardian";

        AddMarker("boss", MarkerShape.Ring, color, 34f, bossZone.position, false, label);
    }

    /// <summary>
    /// The guardian's arena: the inspector override, else the scene's ZoneTrigger
    /// flagged isBossZone - the same lookup QuestHUDDisplay uses to point its
    /// compass, so the map and the compass always agree on where the boss is.
    /// </summary>
    Transform FindBossZone()
    {
        if (bossZoneOverride != null) return bossZoneOverride;

        ZoneTrigger[] zones = FindObjectsOfType<ZoneTrigger>();
        if (zones == null) return null;

        for (int i = 0; i < zones.Length; i++)
            if (zones[i] != null && zones[i].isBossZone) return zones[i].transform;

        return null;
    }

    void AddSanctumMarkers()
    {
        List<MainMapEntrance> entrances = ResolveEntrances();
        if (entrances.Count == 0) return;

        string nextSanctum = MinimapLogic.GetNextSanctumID();

        for (int i = 0; i < entrances.Count; i++)
        {
            MainMapEntrance entrance = entrances[i];
            if (entrance == null || entrance.entranceTransform == null) continue;

            bool isNext = !string.IsNullOrEmpty(nextSanctum) && entrance.sanctumID == nextSanctum;
            bool cleared = MinimapLogic.IsBossDefeated(entrance.sanctumID);

            Color color = isNext ? NextSanctumColor : cleared ? SanctumClearedColor : SanctumColor;
            float size = isNext ? 34f : 24f;
            string key = isNext ? "next_sanctum" : "sanctum_" + entrance.sanctumID;

            string label = !string.IsNullOrEmpty(entrance.displayLabel)
                ? entrance.displayLabel
                : QuestText.ResolveSanctumTitle(entrance.sanctumID);
            if (cleared && !isNext) label += "  (cleared)";

            AddMarker(key, isNext ? MarkerShape.Star : MarkerShape.Circle,
                      color, size, entrance.entranceTransform.position, false, label);
        }
    }

    /// <summary>
    /// Where each sanctum's door is on MainMap. The inspector list wins; otherwise
    /// the scene's MainMapSanctumCompass is reused, since it already carries
    /// id + label + entrance in progression order for the HUD compass. Two lists
    /// that have to agree is a future bug; one list is not.
    /// </summary>
    List<MainMapEntrance> ResolveEntrances()
    {
        if (entranceCache.Count > 0) return entranceCache;

        if (mainMapEntrances != null && mainMapEntrances.Length > 0)
        {
            for (int i = 0; i < mainMapEntrances.Length; i++)
                if (mainMapEntrances[i] != null) entranceCache.Add(mainMapEntrances[i]);
            return entranceCache;
        }

        MainMapSanctumCompass compass = FindObjectOfType<MainMapSanctumCompass>();
        if (compass != null && compass.sanctumEntrances != null)
        {
            for (int i = 0; i < compass.sanctumEntrances.Length; i++)
            {
                MainMapSanctumCompass.SanctumEntranceEntry source = compass.sanctumEntrances[i];
                if (source == null || source.entranceTransform == null) continue;

                MainMapEntrance entrance = new MainMapEntrance();
                entrance.sanctumID = source.sanctumID;
                entrance.displayLabel = source.displayLabel;
                entrance.entranceTransform = source.entranceTransform;
                entranceCache.Add(entrance);
            }

            if (entranceCache.Count > 0)
                Debug.Log("[Minimap] Reusing MainMapSanctumCompass's " + entranceCache.Count +
                          " entrance(s) for the map markers.");
        }

        if (entranceCache.Count == 0)
            Debug.LogWarning("[Minimap] MainMap has no sanctum entrances: fill mainMapEntrances on this " +
                             "component or add a MainMapSanctumCompass, or the next-sanctum marker " +
                             "cannot be drawn.");

        return entranceCache;
    }

    enum MarkerShape { Circle, Diamond, Ring, Star, Arrow }

    void AddMarker(string key, MarkerShape shape, Color color, float size, Vector3 worldPosition,
                   bool tracksPlayer, string label)
    {
        if (markers.ContainsKey(key)) return;

        MarkerView view = new MarkerView();
        view.worldPosition = worldPosition;
        view.tracksPlayer = tracksPlayer;
        view.baseSize = size;

        // One record, two graphics: the disc copy and the map copy are the same
        // marker at different scales, which keeps the placement code below on a
        // single path instead of half-duplicated per mode.
        view.hudRect = CreateMarkerGraphic(hudMarkerParent, shape, color, size * 0.62f, key + "_hud");
        view.mapRect = CreateMarkerGraphic(fullMarkerParent, shape, color, size, key + "_map");

        if (showMarkerLabels && !string.IsNullOrEmpty(label) && !tracksPlayer)
            view.labelRect = CreateMarkerLabel(fullMarkerParent, label, key + "_label");

        markers[key] = view;
    }

    void ClearMarkers()
    {
        foreach (KeyValuePair<string, MarkerView> pair in markers)
        {
            MarkerView view = pair.Value;
            if (view == null) continue;
            if (view.hudRect != null) Destroy(view.hudRect.gameObject);
            if (view.mapRect != null) Destroy(view.mapRect.gameObject);
            if (view.labelRect != null) Destroy(view.labelRect.gameObject);
        }
        markers.Clear();
    }

    // === Per-frame marker placement =====================================================

    /// <summary>
    /// World -> surface mapping for the top-down camera, shared by both modes, and
    /// where the pulsing next-sanctum marker is animated.
    ///
    /// The camera looks straight down with screen up = world +Z and screen right =
    /// world +X, orthographic with half-height = orthographicSize, so a world
    /// point's offset from the camera centre maps linearly onto the image rect.
    /// Two subtractions and two divides instead of WorldToViewportPoint per marker
    /// per frame is what makes a dozen markers cost nothing on a phone.
    /// </summary>
    void LayoutMarkers(RectTransform markerParent, RectTransform surface, bool clampedToDisc)
    {
        if (markerParent == null || surface == null || mapCamera == null) return;
        if (surface.rect.width < 2f || surface.rect.height < 2f) return;

        float halfHeight = mapCamera.orthographicSize;
        float halfWidth = halfHeight * mapCamera.aspect;
        Vector3 centre = mapCamera.transform.position;

        float rectWidth = surface.rect.width;
        float rectHeight = surface.rect.height;
        float rimRadius = Mathf.Min(rectWidth, rectHeight) * 0.5f - 14f;

        float pulse = 1f + 0.16f * Mathf.Sin(Time.unscaledTime * 3.2f);

        foreach (KeyValuePair<string, MarkerView> pair in markers)
        {
            MarkerView view = pair.Value;
            if (view == null) continue;

            RectTransform rect = clampedToDisc ? view.hudRect : view.mapRect;
            if (rect == null) continue;

            Vector3 world = view.tracksPlayer ? PlayerPositionXZ() : view.worldPosition;

            float pointX = (world.x - centre.x) / (2f * halfWidth) * rectWidth;
            float pointY = (world.z - centre.z) / (2f * halfHeight) * rectHeight;

            bool onSurface = Mathf.Abs(pointX) < rectWidth * 0.5f - 6f &&
                             Mathf.Abs(pointY) < rectHeight * 0.5f - 6f;

            if (clampedToDisc)
            {
                // Off-view targets ride the rim instead of vanishing: a tablet the
                // player has not found yet must never simply be gone from the disc.
                Vector2 point = new Vector2(pointX, pointY);
                float magnitude = point.magnitude;
                if (magnitude > rimRadius && magnitude > 0.001f)
                    point = point * (rimRadius / magnitude);
                rect.anchoredPosition = point;
            }
            else
            {
                rect.anchoredPosition = new Vector2(pointX, pointY);
                if (rect.gameObject.activeSelf != onSurface) rect.gameObject.SetActive(onSurface);
            }

            if (view.tracksPlayer)
            {
                float facing = ResolvePlayerFacingDegrees();
                if (view.hudRect != null) view.hudRect.localRotation = Quaternion.Euler(0f, 0f, facing);
                if (view.mapRect != null) view.mapRect.localRotation = Quaternion.Euler(0f, 0f, facing);
            }
            else if (pair.Key == "next_sanctum")
            {
                // The one animated marker: the door to go through next has to
                // visibly differ from the cleared and not-yet-started ones.
                if (view.hudRect != null) view.hudRect.localScale = Vector3.one * pulse;
                if (view.mapRect != null) view.mapRect.localScale = Vector3.one * pulse;
            }

            // Labels are a fullscreen-only affordance: the disc is far too small.
            if (view.labelRect != null)
            {
                bool showLabel = !clampedToDisc && onSurface;
                if (showLabel)
                    view.labelRect.anchoredPosition = new Vector2(pointX, pointY - view.baseSize * 0.5f - 16f);
                if (view.labelRect.gameObject.activeSelf != showLabel)
                    view.labelRect.gameObject.SetActive(showLabel);
            }
        }
    }

    // === Fullscreen input ==============================================================

    void HandleFullscreenInput()
    {
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (!Mathf.Approximately(wheel, 0f)) ZoomBy(-wheel * wheelZoomPerNotch * 6f);

        // Pinch: two fingers. Anchoring the change to the finger spread ratio makes
        // it feel like the map is being pushed apart rather than a slider moving.
        if (Input.touchCount >= 2)
        {
            Touch first = Input.GetTouch(0);
            Touch second = Input.GetTouch(1);
            float distance = Vector2.Distance(first.position, second.position);

            if (pinchStartDistance <= 0f)
            {
                pinchStartDistance = distance;
                pinchStartZoom = targetZoom;
            }
            else if (distance > 1f && pinchStartDistance > 1f)
            {
                float ratio = pinchStartDistance / distance;
                targetZoom = Mathf.Clamp(pinchStartZoom * Mathf.Pow(ratio, pinchZoomGain), zoomMin, FitZoomForScene());
            }
        }
        else
        {
            pinchStartDistance = 0f;
        }
    }

    /// <summary>Change zoom by a world half-height delta, clamped to the scene.</summary>
    public void ZoomBy(float deltaHalfHeight)
    {
        float maxZoom = FitZoomForScene();
        targetZoom = Mathf.Clamp(targetZoom + deltaHalfHeight, zoomMin, Mathf.Max(zoomMin, maxZoom));
    }

    /// <summary>
    /// Move the map by a world-space delta (x = east, y = north). Called by the
    /// drag handler with the finger's movement already converted to world units,
    /// and usable by anything else that wants to nudge the view.
    /// </summary>
    public void PanByWorldUnits(Vector2 delta)
    {
        mapCentre.x += delta.x;
        mapCentre.z += delta.y;
        ClampMapCentre();
        ApplyCamera();
    }

    /// <summary>Recentre on the player, keeping the current zoom.</summary>
    public void RecentreOnPlayer()
    {
        mapCentre = PlayerPositionXZ();
        ClampMapCentre();
        ApplyCamera();
    }

    // === Runtime UI build ==============================================================

    void BuildCanvas()
    {
        GameObject canvasGo = new GameObject("MinimapCanvas");
        canvasGo.transform.SetParent(transform, false);
        canvasGo.layer = gameObject.layer;

        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;

        canvasScaler = canvasGo.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = fallbackReferenceResolution;
        // 0.5 splits the scale between width and height, so the disc stays usable
        // in both phone orientations instead of overflowing one.
        canvasScaler.matchWidthOrHeight = fallbackMatchWidthOrHeight;

        canvasGo.AddComponent<GraphicRaycaster>();
        canvasRect = canvasGo.GetComponent<RectTransform>();

        if (EventSystem.current == null && !warnedMissingEventSystem)
        {
            warnedMissingEventSystem = true;
            Debug.LogWarning("[Minimap] No EventSystem in this scene, so the map cannot be pressed. " +
                             "Gameplay scenes already have one for the HUD buttons; this only shows " +
                             "up when the minimap runs alone in a test scene.");
        }
    }

    void BuildHudMinimap()
    {
        hudRoot = new GameObject("MinimapHud");
        hudRoot.transform.SetParent(canvasRect, false);
        RectTransform hudRect = hudRoot.AddComponent<RectTransform>();

        bool bottomCorner = hudCorner == HudCorner.BottomLeft || hudCorner == HudCorner.BottomRight;
        bool leftCorner = hudCorner == HudCorner.TopLeft || hudCorner == HudCorner.BottomLeft;
        Vector2 cornerAnchor = new Vector2(leftCorner ? 0f : 1f, bottomCorner ? 0f : 1f);
        // The disc itself always hangs inward from whichever corner is chosen.
        Vector2 discAnchor = new Vector2(0.5f, bottomCorner ? 0f : 1f);
        float discCenterY = bottomCorner ? hudDiameter * 0.5f : -hudDiameter * 0.5f;

        hudRect.anchorMin = cornerAnchor;
        hudRect.anchorMax = cornerAnchor;
        hudRect.pivot = cornerAnchor;
        hudRect.anchoredPosition = new Vector2(leftCorner ? hudEdgeMargin.x : -hudEdgeMargin.x,
                                               bottomCorner ? hudEdgeMargin.y : -hudEdgeMargin.y);
        hudRect.sizeDelta = new Vector2(hudDiameter, hudDiameter + 30f);
        hudRect.localScale = Vector3.one * Mathf.Max(0.25f, hudScale);

        // Circular clip. A Mask needs an Image and the map needs a RawImage, and
        // Unity only allows one Graphic per GameObject, so the clip is the parent
        // and the map image its child.
        GameObject clipGo = new GameObject("MapDisc");
        clipGo.transform.SetParent(hudRect, false);
        RectTransform clipRect = clipGo.AddComponent<RectTransform>();
        SetRect(clipRect, discAnchor, discAnchor,
                new Vector2(0f, discCenterY), new Vector2(hudDiameter - 8f, hudDiameter - 8f));

        Image clipImage = clipGo.AddComponent<Image>();
        // High-res circle: the Mask cuts at the sprite's 50% alpha contour, and at
        // 128px that contour quantised into visible steps across the disc.
        clipImage.sprite = MinimapSprites.DiscClip;
        clipImage.color = DiscClipColor;
        clipImage.raycastTarget = false;
        Mask clipMask = clipGo.AddComponent<Mask>();
        clipMask.showMaskGraphic = false;
        hudClipRect = clipRect;

        GameObject hudImageGo = new GameObject("MapImage");
        hudImageGo.transform.SetParent(clipRect, false);
        RectTransform hudImageRect = hudImageGo.AddComponent<RectTransform>();
        Stretch(hudImageRect);
        RawImage hudRaw = hudImageGo.AddComponent<RawImage>();
        hudRaw.texture = mapTexture;
        hudRaw.raycastTarget = false;

        GameObject hudMarkerGo = new GameObject("Markers");
        hudMarkerGo.transform.SetParent(clipRect, false);
        hudMarkerParent = hudMarkerGo.AddComponent<RectTransform>();
        Stretch(hudMarkerParent);

        // Rim band: the Mask clips through the stencil buffer, a hard 1-bit edge
        // that can never anti-alias. This inverse annulus is deliberately a SIBLING
        // above the clip disc, not a child - children are stencil-clipped too, so
        // the previous feather was itself cut away exactly where it needed to cover
        // the edge. Slightly oversized (x1.08) so its own smooth outer fade becomes
        // the disc's visible, anti-aliased silhouette.
        GameObject bandGo = new GameObject("EdgeBand");
        bandGo.transform.SetParent(hudRect, false);
        RectTransform bandRect = bandGo.AddComponent<RectTransform>();
        SetRect(bandRect, discAnchor, discAnchor,
                new Vector2(0f, discCenterY),
                new Vector2((hudDiameter - 8f) * 1.08f, (hudDiameter - 8f) * 1.08f));
        Image edgeBand = bandGo.AddComponent<Image>();
        edgeBand.sprite = MinimapSprites.EdgeBand;
        edgeBand.color = DiscClipColor;
        edgeBand.raycastTarget = false;

        // Press surface: its own overlay, covering exactly the disc, so the open
        // hit area cannot be stolen by the RawImage under it.
        Button openButton = CreateButton(hudRect, "OpenMapButton", string.Empty, discAnchor,
                                         new Vector2(0f, discCenterY),
                                         new Vector2(hudDiameter, hudDiameter),
                                         new Color(1f, 1f, 1f, 0f), null, 0,
                                         () => SetExpanded(true));
        // The overlay's pivot is corrected to the disc centre exactly as the clip
        // disc's below, so the press surface and the disc land on the same circle.
        if (openButton != null)
        {
            RectTransform buttonRect = openButton.transform as RectTransform;
            if (buttonRect != null)
            {
                buttonRect.anchorMin = discAnchor;
                buttonRect.anchorMax = discAnchor;
                buttonRect.pivot = new Vector2(0.5f, 0.5f);
                buttonRect.anchoredPosition = new Vector2(0f, discCenterY);
                buttonRect.sizeDelta = new Vector2(hudDiameter, hudDiameter);
            }
        }

        if (!string.IsNullOrEmpty(hudLabel))
        {
            // Top corners: caption under the disc. Bottom corners: caption just
            // above it, so it never leaves the screen.
            Text caption = CreateText(hudRect, "MinimapCaption", hudLabel, discAnchor,
                                      bottomCorner ? new Vector2(0f, hudDiameter + 8f)
                                                   : new Vector2(0f, -hudDiameter - 4f),
                                      new Vector2(hudDiameter, 24f),
                                      17, TextAnchor.MiddleCenter, PlayerColor);
            caption.rectTransform.pivot = bottomCorner ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 1f);
        }
    }

    void BuildFullscreenMap()
    {
        fullRoot = new GameObject("FullscreenMap");
        fullRoot.transform.SetParent(canvasRect, false);
        RectTransform rootRect = fullRoot.AddComponent<RectTransform>();
        Stretch(rootRect);

        Image dim = fullRoot.AddComponent<Image>();
        dim.color = DimBackground;
        dim.raycastTarget = true;

        float panelWidth = Mathf.Clamp(CanvasWidth() * 0.3f, 280f, 470f);

        // --- Map surface: pan and pinch are received here ------------------------------

        GameObject viewportGo = new GameObject("MapViewport");
        viewportGo.transform.SetParent(rootRect, false);
        viewportRect = viewportGo.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(panelWidth + 40f, 32f);
        viewportRect.offsetMax = new Vector2(-32f, -124f);

        Image surface = viewportGo.AddComponent<Image>();
        surface.sprite = MinimapSprites.RoundRect;
        surface.color = new Color(0.09f, 0.13f, 0.19f, 1f);
        surface.raycastTarget = true;

        GameObject mapImageGo = new GameObject("MapImage");
        mapImageGo.transform.SetParent(viewportRect, false);
        RectTransform mapImageRect = mapImageGo.AddComponent<RectTransform>();
        Stretch(mapImageRect);
        RawImage fullRaw = mapImageGo.AddComponent<RawImage>();
        fullRaw.texture = mapTexture;
        fullRaw.raycastTarget = false;

        GameObject markerGo = new GameObject("Markers");
        markerGo.transform.SetParent(viewportRect, false);
        fullMarkerParent = markerGo.AddComponent<RectTransform>();
        Stretch(fullMarkerParent);

        viewportGo.AddComponent<MinimapMapDragHandler>().owner = this;

        // --- Back button: top-left ------------------------------------------------------

        // Exit_Bright already reads as a back affordance: sprite only, no text label.
        CreateButton(rootRect, "BackButton", string.Empty, new Vector2(0f, 1f), new Vector2(28f, -28f),
                     new Vector2(80f, 80f), Color.white, LoadNamedSprite("Exit_Bright", backButtonAtlasPath), 0,
                     () => SetExpanded(false));

        // --- Quest name + description: down the left edge --------------------------------

        GameObject panel = new GameObject("QuestPanel");
        panel.transform.SetParent(rootRect, false);
        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.offsetMin = new Vector2(24f, 28f);
        panelRect.offsetMax = new Vector2(24f + panelWidth, -124f);

        Image panelImage = panel.AddComponent<Image>();
        Sprite panelSkin = LoadNamedSprite("9-Sliced");
        panelImage.sprite = panelSkin != null ? panelSkin : MinimapSprites.RoundRect;
        if (panelSkin != null && panelSkin.border != Vector4.zero) panelImage.type = Image.Type.Sliced;
        panelImage.color = QuestPanelSkinColor;
        panelImage.raycastTarget = false;

        Text heading = CreateText(panelRect, "PanelHeading", "CURRENT QUEST", new Vector2(0f, 1f),
                                  new Vector2(20f, -18f), new Vector2(panelWidth - 40f, 26f),
                                  18, TextAnchor.UpperLeft, new Color(0.55f, 0.6f, 0.68f, 1f), FullscreenUiFont());
        heading.rectTransform.pivot = new Vector2(0f, 1f);

        questNameText = CreateText(panelRect, "QuestName", string.Empty, new Vector2(0f, 1f),
                                   new Vector2(20f, -48f), new Vector2(panelWidth - 40f, 74f),
                                   27, TextAnchor.UpperLeft, NextSanctumColor, FullscreenUiFont());
        questNameText.rectTransform.pivot = new Vector2(0f, 1f);
        questNameText.horizontalOverflow = HorizontalWrapMode.Wrap;

        questBodyText = CreateText(panelRect, "QuestDescription", string.Empty, new Vector2(0f, 1f),
                                   new Vector2(20f, -128f), new Vector2(panelWidth - 40f, 2000f),
                                   20, TextAnchor.UpperLeft, PanelText, FullscreenUiFont());
        questBodyText.rectTransform.pivot = new Vector2(0f, 1f);
        questBodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        questBodyText.verticalOverflow = VerticalWrapMode.Truncate;

        // --- Title, top-centre ----------------------------------------------------------

        mapTitleText = CreateText(rootRect, "MapTitle", string.Empty, new Vector2(0.5f, 1f),
                                  new Vector2(0f, -34f), new Vector2(620f, 52f),
                                  34, TextAnchor.MiddleCenter, Color.white, FullscreenUiFont());
        mapTitleText.rectTransform.pivot = new Vector2(0.5f, 1f);

        // --- Zoom controls, right edge --------------------------------------------------

        Font skinFont = FullscreenUiFont();
        CreateButton(rootRect, "ZoomInButton", "+", new Vector2(1f, 1f), new Vector2(-40f, -152f),
                     new Vector2(80f, 80f), FullscreenButtonColor, MinimapSprites.RoundRect, 34,
                     () => ZoomBy(zoomStep), skinFont);
        CreateButton(rootRect, "ZoomOutButton", "-", new Vector2(1f, 1f), new Vector2(-40f, -240f),
                     new Vector2(80f, 80f), FullscreenButtonColor, MinimapSprites.RoundRect, 34,
                     () => ZoomBy(-zoomStep), skinFont);
        // Jost has no \u25C8 glyph, so recentre shows the cyan player arrow instead.
        Button recentre = CreateButton(rootRect, "RecentreButton", string.Empty, new Vector2(1f, 1f), new Vector2(-40f, -328f),
                                       new Vector2(80f, 80f), FullscreenButtonColor, MinimapSprites.RoundRect, 0,
                                       () => RecentreOnPlayer(), skinFont);
        Image recentreIcon = CreateImage((RectTransform)recentre.transform, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero,
                                         new Vector2(36f, 36f), PlayerColor, SpriteForShape(MarkerShape.Arrow));

        BuildLegend(rootRect);
        fullRoot.SetActive(false);
    }

    void BuildLegend(RectTransform parent)
    {
        // Row layout instead of one rich-text string: Jost renders no geometric glyph
        // characters, so the legend uses the actual marker sprites at 20px per row.
        var entries = new (MarkerShape shape, Color color, string label)[]
        {
            (MarkerShape.Arrow, PlayerColor, "You are here"),
            (MarkerShape.Diamond, TabletColor, "Mission tablet"),
            (MarkerShape.Ring, BossColor, "Sanctum guardian"),
            (MarkerShape.Star, NextSanctumColor, "Next sanctum"),
        };

        Font font = FullscreenUiFont();
        RectTransform legendRoot = new GameObject("MapLegend").AddComponent<RectTransform>();
        legendRoot.SetParent(parent, false);
        SetRect(legendRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(320f, 136f));
        legendRoot.pivot = new Vector2(1f, 0f);

        for (int i = 0; i < entries.Length; i++)
        {
            float y = -10f - i * 32f;
            CreateImage(legendRoot, "LegendShape" + i, new Vector2(1f, 1f), new Vector2(-282f, y),
                        new Vector2(20f, 20f), entries[i].color, SpriteForShape(entries[i].shape));
            Text row = CreateText(legendRoot, "LegendLabel" + i, entries[i].label, new Vector2(1f, 1f),
                                  new Vector2(-260f, y), new Vector2(200f, 26f),
                                  18, TextAnchor.MiddleLeft, PanelText, font);
            row.rectTransform.pivot = new Vector2(0f, 0.5f);
        }
    }

    static string Glyph(MarkerShape shape)
    {
        switch (shape)
        {
            case MarkerShape.Arrow: return "\u25B2";
            case MarkerShape.Diamond: return "\u25C6";
            case MarkerShape.Ring: return "\u25CF";
            case MarkerShape.Star: return "\u2605";
            default: return "\u25CB";
        }
    }

    float CanvasWidth()
    {
        if (canvasRect != null && canvasRect.rect.width > 1f) return canvasRect.rect.width;
        return Screen.width > 0 ? Screen.width : 1920f;
    }

    // === Quest panel ====================================================================

    void RefreshQuestPanel()
    {
        if (questNameText == null || destroyed) return;

        if (mapTitleText != null)
        {
            mapTitleText.text = !string.IsNullOrEmpty(mapTitleOverride)
                ? mapTitleOverride
                : (isMainMapScene ? "The Overworld" : QuestText.ResolveSanctumTitle(resolvedSanctumID));
        }

        questNameText.text = QuestText.BuildQuestName(resolvedSanctumID);
        questBodyText.text = BuildMapQuestDescription();
    }

    /// <summary>
    /// The description the map shows. QuestText.BuildObjective is the same call the
    /// HUD panel makes, so the two can never tell the player different things about
    /// one quest; the map adds the tablet list, because that is what its markers
    /// are pointing at.
    /// </summary>
    string BuildMapQuestDescription()
    {
        QuestManager qm = QuestManager.Instance;
        if (qm == null) return "Loading quest data...";

        QuestManager.QuestEntry entry = qm.GetActiveQuestEntry();

        if (entry == null)
        {
            return "Every sanctum is clear. The Aethelscript is restored.";
        }

        if (isMainMapScene) return BuildOverworldDescription();

        string objective = QuestText.BuildObjective(entry, null, false);
        if (string.IsNullOrEmpty(objective)) return entry.displayName;

        string phase = QuestText.GetQuestPhase(entry);
        if (phase == "find" || phase == "speak")
            return objective + "\n\n<b>Mission tablets here</b>\n" + TabletRows();

        return objective;
    }

    /// <summary>
    /// The sanctum's tablets in text. The markers already show where each one is;
    /// this is the same set in words, so a player who cannot read a dot's colour
    /// still knows what is left to do.
    /// </summary>
    string TabletRows()
    {
        List<MissionTabletData> missions = MinimapLogic.GetSanctumMissions(resolvedSanctumID);
        if (missions.Count == 0) return "";

        var sb = new StringBuilder();
        for (int i = 0; i < missions.Count; i++)
        {
            MissionTabletData mission = missions[i];
            bool done = !MinimapLogic.IsTabletMarkerVisible(mission.missionID);
            string text = !string.IsNullOrEmpty(mission.description) ? mission.description : mission.missionID;
            sb.Append(done ? QuestText.CheckDone : QuestText.CheckPending).Append(' ').AppendLine(text);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// MainMap panel: the quest chain points at a sanctum the player is not in, so
    /// the panel's job is "where do I go next", matching the map's own markers.
    /// </summary>
    string BuildOverworldDescription()
    {
        string next = MinimapLogic.GetNextSanctumID();
        if (string.IsNullOrEmpty(next))
            return "Every sanctum is clear. The Aethelscript is restored.";

        QuestManager qm = QuestManager.Instance;
        QuestManager.QuestEntry entry = qm != null ? qm.GetActiveQuestEntry() : null;

        var sb = new StringBuilder();
        if (entry != null && !string.IsNullOrEmpty(entry.displayName))
            sb.AppendLine(entry.displayName);

        sb.AppendLine("<b>Travel to " + QuestText.ResolveSanctumTitle(next) + ".</b>");
        sb.AppendLine("Its marker pulses gold on the map.");
        sb.AppendLine();

        for (int i = 0; i < MinimapLogic.ProgressionOrder.Length; i++)
        {
            string sanctum = MinimapLogic.ProgressionOrder[i];
            string state;
            if (MinimapLogic.IsBossDefeated(sanctum)) state = "cleared";
            else if (sanctum == next) state = "next";
            else state = "not yet started";

            sb.Append(sanctum == next ? QuestText.CheckDone : QuestText.CheckPending)
              .Append(' ')
              .Append(QuestText.ResolveSanctumTitle(sanctum))
              .Append("  (")
              .Append(state)
              .AppendLine(")");
        }
        return sb.ToString().TrimEnd();
    }

    // === UI factories (every sprite is generated: no art dependencies) ================

    RectTransform CreateMarkerGraphic(RectTransform parent, MarkerShape shape, Color color,
                                      float size, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);

        Image image = go.AddComponent<Image>();
        image.sprite = SpriteForShape(shape);
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    RectTransform CreateMarkerLabel(RectTransform parent, string content, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(240f, 30f);

        Text label = go.AddComponent<Text>();
        label.font = DefaultUiFont();
        label.text = content;
        label.fontSize = 17;
        label.alignment = TextAnchor.UpperCenter;
        label.color = new Color(0.95f, 0.97f, 1f, 0.92f);
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        return rect;
    }

    Sprite SpriteForShape(MarkerShape shape)
    {
        switch (shape)
        {
            case MarkerShape.Diamond: return MinimapSprites.Diamond;
            case MarkerShape.Ring: return MinimapSprites.Ring;
            case MarkerShape.Star: return MinimapSprites.Star;
            case MarkerShape.Arrow: return MinimapSprites.Arrow;
            default: return MinimapSprites.Circle;
        }
    }

    Image CreateImage(RectTransform parent, string name, Vector2 anchor, Vector2 position,
                      Vector2 size, Color color, Sprite sprite)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        SetRect(rect, anchor, anchor, position, size);

        Image image = go.AddComponent<Image>();
        image.color = color;
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    Text CreateText(RectTransform parent, string name, string content, Vector2 anchor, Vector2 position,
                    Vector2 size, int fontSize, TextAnchor alignment, Color color, Font font = null)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Text text = go.AddComponent<Text>();
        text.font = font != null ? font : DefaultUiFont();
        text.text = content;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.supportRichText = true;
        return text;
    }

    Button CreateButton(RectTransform parent, string name, string label, Vector2 anchor, Vector2 position,
                        Vector2 size, Color background, Sprite sprite, int fontSize, System.Action onClick,
                        Font font = null)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Image image = go.GetComponent<Image>();
        image.color = background;
        image.sprite = sprite;
        image.raycastTarget = true;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() =>
        {
            if (onClick != null) onClick();
        });

        if (!string.IsNullOrEmpty(label))
        {
            Text text = CreateText(rect, "Label", label, new Vector2(0.5f, 0.5f), Vector2.zero, size,
                                   fontSize > 0 ? fontSize : 22, TextAnchor.MiddleCenter, Color.white, font);
            Stretch(text.rectTransform);
        }
        return button;
    }

    void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    Font cachedFont;

    Font DefaultUiFont()
    {
        if (cachedFont != null) return cachedFont;
        // The built-in font was renamed in Unity 2022.2; try both, as Dialoguemanager does.
        try { cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); if (cachedFont != null) return cachedFont; } catch { }
        try { cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); if (cachedFont != null) return cachedFont; } catch { }
        Debug.LogWarning("[Minimap] No built-in UI font found; map and quest text will render empty.");
        return null;
    }

    static readonly Dictionary<string, Sprite> namedSpriteCache = new Dictionary<string, Sprite>();

    /// <summary>Finds a user-supplied sprite by asset name: AssetDatabase in the editor,
    /// Resources.Load in builds. atlasPath (optional) names the multi-sprite atlas the
    /// sub-sprite lives in, which a plain name search cannot see. Cache once per play
    /// session; null results warn once.</summary>
    Sprite LoadNamedSprite(string spriteName, string atlasPath = null)
    {
        if (string.IsNullOrEmpty(spriteName)) return null;
        if (namedSpriteCache.TryGetValue(spriteName, out Sprite cached))
        {
            return cached;
        }

        Sprite sprite = FindSpriteInAtlas(spriteName, atlasPath);
        if (sprite == null) sprite = FindSpriteByName(spriteName);
        namedSpriteCache[spriteName] = sprite;
        if (sprite == null)
        {
            Debug.LogWarning("[Minimap] Sprite '" + spriteName + "' not found; the fullscreen map keeps its generated look. " +
                             "In builds the asset must live under a folder named Resources.");
        }
        return sprite;
    }

    /// <summary>Loads a named sub-sprite straight out of a known atlas file. Sub-sprite
    /// names are not the main asset name, so FindAssets by name can miss the atlas;
    /// loading by path sidesteps that completely.</summary>
    static Sprite FindSpriteInAtlas(string spriteName, string atlasPath)
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(atlasPath) || !System.IO.File.Exists(atlasPath)) return null;
        foreach (Object asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(atlasPath))
        {
            if (asset is Sprite sub && sub.name == spriteName) return sub;
        }
#endif
        return null;
    }

    static Sprite FindSpriteByName(string spriteName)
    {
#if UNITY_EDITOR
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Sprite " + spriteName))
        {
            Sprite candidate = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (candidate != null && candidate.name == spriteName) return candidate;
        }
        // Sprite sub-assets whose main object is the texture: scan LoadAllAssetsAtPath.
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Texture2D " + spriteName))
        {
            Object[] assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            foreach (Object asset in assets)
            {
                if (asset is Sprite sub && sub.name == spriteName) return sub;
            }
        }
#endif
        foreach (string resourcePath in new[] { spriteName, "Sprites/" + spriteName, "UI/" + spriteName })
        {
            Sprite loaded = Resources.Load<Sprite>(resourcePath);
            if (loaded != null) return loaded;
        }
        return null;
    }

    Font cachedFullscreenFont;
    bool fullscreenFontResolved;

    /// <summary>Jost-Regular for every fullscreen text, resolved once: project asset
    /// in the editor, Resources.Load in builds, built-in font as the last resort.</summary>
    Font FullscreenUiFont()
    {
        if (fullscreenFontResolved) return cachedFullscreenFont;
        fullscreenFontResolved = true;
        cachedFullscreenFont = FindFont("Jost-Regular");
        if (cachedFullscreenFont == null) cachedFullscreenFont = FindFont("Jost");
        if (cachedFullscreenFont == null)
        {
            cachedFullscreenFont = DefaultUiFont();
            Debug.LogWarning("[Minimap] Jost-Regular.ttf not found; the fullscreen map fell back to the built-in font. " +
                             "In builds the file must live under a folder named Resources.");
        }
        return cachedFullscreenFont;
    }

    static Font FindFont(string fontName)
    {
#if UNITY_EDITOR
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Font " + fontName))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            Font found = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>(path);
            if (found != null && (found.name == fontName || found.name.StartsWith(fontName))) return found;
        }
#endif
        Font font = Resources.Load<Font>(fontName);
        if (font == null) font = Resources.Load<Font>("Fonts/" + fontName);
        return font;
    }

    class MarkerView
    {
        public RectTransform hudRect;
        public RectTransform mapRect;
        public RectTransform labelRect;
        public Vector3 worldPosition;
        public bool tracksPlayer;
        public float baseSize;
    }
}

/// <summary>
/// Drag-to-pan handler for the fullscreen map surface, in its own class because
/// Unity's drag interfaces must sit on the Graphic that receives them.
///
/// Pointer movement arrives in screen pixels and is divided by the number of
/// pixels representing one world unit, so the map tracks the finger exactly at any
/// zoom instead of sliding at a fixed speed that feels wrong the moment the player
/// zooms out.
/// </summary>
public class MinimapMapDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [HideInInspector] public MinimapController owner;

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (owner == null || !owner.IsExpanded) return;

        float pixelsPerWorldUnit = PixelsPerWorldUnit();
        if (pixelsPerWorldUnit <= 0.0001f) return;

        // The canvas may be scaled up or down from its 1920x1080 reference, so undo
        // that scale before converting to world units - otherwise pan speed changes
        // with the device instead of staying one finger to one world unit.
        float canvasScale = CanvasScaleFactor(eventData);
        if (canvasScale <= 0.0001f) canvasScale = 1f;

        float worldPerPointerPixel = 1f / (pixelsPerWorldUnit * canvasScale);
        Vector2 worldDelta = new Vector2(eventData.delta.x * worldPerPointerPixel,
                                         eventData.delta.y * worldPerPointerPixel);

        // Dragging the map right means the camera moves left: the content follows
        // the finger, so the pan is negated.
        owner.PanByWorldUnits(-worldDelta);
    }

    public void OnEndDrag(PointerEventData eventData) { }

    /// <summary>Pixels of the map surface that represent one world unit.</summary>
    float PixelsPerWorldUnit()
    {
        if (owner == null) return 0f;

        Camera mapCamera = owner.MapCamera;
        RectTransform surface = owner.ViewportRect;
        if (mapCamera == null || surface == null || surface.rect.height < 2f) return 0f;

        return surface.rect.height / (2f * Mathf.Max(0.01f, mapCamera.orthographicSize));
    }

    float CanvasScaleFactor(PointerEventData eventData)
    {
        GameObject target = eventData.pointerCurrentRaycast.gameObject;
        if (target == null) return 1f;

        Canvas canvas = target.GetComponentInParent<Canvas>();
        if (canvas == null) return 1f;
        return canvas.rootCanvas != null ? canvas.rootCanvas.scaleFactor : canvas.scaleFactor;
    }
}
