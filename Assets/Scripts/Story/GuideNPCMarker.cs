using UnityEngine;

/// <summary>
/// Issue 1 fix: floating "talk to me" indicator hovering above a guide NPC.
/// Attached at runtime by SanctumEntryLock to every guide NPC whose starting
/// (intro) dialogue has not been completed yet. Purely diegetic: a small
/// glowing golden gem that bobs and slowly spins above the NPC's head.
/// Removed by SanctumEntryLock once that NPC's intro dialogue completes
/// (or when the whole sanctum lock is lifted), so it can never linger over
/// an NPC the player has already talked to.
///
/// Anchor rules (matches the scene survey in the fix plan): markerSlot is
/// unassigned in all four sanctum scenes, so the marker parents itself to
/// the NPC's npcModel (falls back to the NPCController's own transform) and
/// derives its height from the live renderer-bounds top at attach time.
/// Parenting under npcModel means the marker follows the model's movement.
/// </summary>
public class GuideNPCMarker : MonoBehaviour
{
    private const float BaseOffset = 0.5f;    // metres above the model's top
    private const float BobAmplitude = 0.1f;  // hover bob range (metres)
    private const float BobSpeed = 2.2f;
    private const float SpinSpeed = 45f;      // degrees per second
    private const float MarkerRadius = 0.14f; // world-space sphere radius

    private NPCController owner;
    private float baseHeight;
    private float bobPhase;
    private bool initialized;

    public NPCController Owner => owner;

    /// <summary>
    /// Creates (or re-uses) the marker for a guide NPC. Idempotent: calling
    /// twice for the same NPC returns the existing marker instead of
    /// stacking a second gem on its head.
    /// </summary>
    public static GuideNPCMarker Attach(NPCController owner, Material markerMaterial = null)
    {
        if (owner == null) return null;

        // Idempotency guard: an old marker (possibly from a previous
        // initialization pass on this same scene) wins; never double-mark.
        // The marker parents under npcModel, which is a SIBLING of the
        // NPCController's GameObject in the scene hierarchy — look under the
        // model first, then the controller's own subtree.
        Transform anchorEarly = owner.npcModel != null ? owner.npcModel.transform : owner.transform;
        GuideNPCMarker existing = anchorEarly.GetComponentInChildren<GuideNPCMarker>(true);
        if (existing == null)
            existing = owner.GetComponentInChildren<GuideNPCMarker>(true);
        if (existing != null)
        {
            existing.gameObject.SetActive(true);
            return existing;
        }

        Transform anchor = owner.npcModel != null ? owner.npcModel.transform : owner.transform;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = $"GuideMarker_{Mathf.Max(0, owner.GetInstanceID())}";
        // The auto collider on a primitive must go: the marker floats at
        // player head height and would otherwise be a physical bump object
        // and a raycast target. This marker is visual-only.
        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null) Destroy(markerCollider);

        marker.transform.SetParent(anchor, false);
        Vector3 topLocal = ComputeModelTopLocal(anchor);
        marker.transform.localPosition = topLocal + Vector3.up * BaseOffset;
        marker.transform.localScale = new Vector3(MarkerRadius * 2f, MarkerRadius * 2.6f, MarkerRadius * 2f);

        Renderer markerRenderer = marker.GetComponent<Renderer>();
        if (markerRenderer != null)
        {
            markerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markerRenderer.receiveShadows = false;
            // Assigned material first (e.g. Assets/Prefabs/UI_assets/NPCmarker.mat
            // wired into SanctumManager.npcMarkerMaterial); generated glow only
            // when the slot is empty. Resources fallback covers the case where
            // the material was moved under a Resources folder instead.
            Material glow = markerMaterial;
            if (glow == null)
                glow = Resources.Load<Material>("Prefabs/UI_assets/NPCmarker");
            if (glow == null)
                glow = MakeGlowMaterial();
            if (glow != null) markerRenderer.material = glow;
        }

        GuideNPCMarker markerComp = marker.AddComponent<GuideNPCMarker>();
        markerComp.Init(owner, marker.transform.localPosition.y);
        return markerComp;
    }

    private void Init(NPCController guideOwner, float localBaseHeight)
    {
        owner = guideOwner;
        baseHeight = localBaseHeight;
        bobPhase = Random.Range(0f, Mathf.PI);
        initialized = true;
    }

    /// <summary>Removes this marker (Destroy is deferred to end of frame).</summary>
    public void Remove()
    {
        Destroy(gameObject);
    }

    private void Update()
    {
        if (!initialized || owner == null) return;

        // Local-space bob + slow spin so the marker survives any model-root
        // rotation/movement (parenting keeps it glued to the NPC).
        Vector3 local = transform.localPosition;
        local.y = baseHeight + Mathf.Sin(Time.time * BobSpeed + bobPhase) * BobAmplitude;
        transform.localPosition = local;
        transform.Rotate(0f, SpinSpeed * Time.deltaTime, 0f, Space.Self);
    }

    /// <summary>
    /// Local-space (relative to the anchor) position at the top of the
    /// anchor model's combined renderer bounds. Falls back to colliders
    /// when the model has no renderers, then to a flat 1m offset when it
    /// has neither — so a marker is always visible rather than silently
    /// buried inside the mesh.
    /// </summary>
    private static Vector3 ComputeModelTopLocal(Transform anchor)
    {
        Bounds bounds = new Bounds();
        bool anyBounds = false;

        Renderer[] renderers = anchor.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            if (!anyBounds) { bounds = r.bounds; anyBounds = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (!anyBounds)
        {
            Collider[] colliders = anchor.GetComponentsInChildren<Collider>();
            foreach (Collider c in colliders)
            {
                if (c == null) continue;
                if (!anyBounds) { bounds = c.bounds; anyBounds = true; }
                else bounds.Encapsulate(c.bounds);
            }
        }

        if (!anyBounds)
            return new Vector3(0f, 1f, 0f); // bare anchor, marker 1m above pivot

        float topY = bounds.center.y + bounds.extents.y;
        Vector3 worldTop = new Vector3(anchor.position.x, topY, anchor.position.z);
        return anchor.InverseTransformPoint(worldTop);
    }

    private static Material MakeGlowMaterial()
    {
        // Standard is present in editor/play mode; older/legacy fallbacks
        // keep the marker visible if the shader was stripped, and a null
        // shader falls back to Unity's default material.
        Shader shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");
        if (shader == null) return null;

        Material mat = new Material(shader);
        Color gold = new Color(1f, 0.78f, 0.25f);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", gold);
        if (mat.HasProperty("_EmissionColor"))
        {
            mat.SetColor("_EmissionColor", new Color(1f, 0.72f, 0.18f) * 1.4f);
            mat.EnableKeyword("_EMISSION");
        }
        return mat;
    }
}
