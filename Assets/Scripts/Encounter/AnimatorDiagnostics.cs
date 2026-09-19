// AnimatorDiagnostics.cs — temporary debugging tool for the skeleton enemies.
// NOT an editor script: put it in Assets/Scripts (NOT Assets/Editor).
// Attach it to the enemy PREFAB (the same asset in EncounterManager's
// beginner/intermediate/advanced prefab slots). It reports on spawn and can
// manually fire triggers from the Inspector while the game is running.
using UnityEngine;

public class AnimatorDiagnostics : MonoBehaviour
{
    static readonly string[] Triggers = { "OnEncounterStart", "Attack", "TakeDamage", "Die", "Victory" };

    void Start() { Report(); }

    [ContextMenu("Re-run Diagnostics")]
    public void Report()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[AnimatorDiagnostics] Probing '{name}'");

        Animator[] animators = GetComponentsInChildren<Animator>(true);
        if (animators.Length == 0)
        {
            sb.AppendLine("  !! NO Animator on the root or any child.");
            sb.AppendLine("  => Your prefab is probably an empty wrapper with the FBX nested deeper, or the model has no Animator at all.");
            Debug.LogWarning(sb.ToString(), this);
            return;
        }

        foreach (Animator an in animators)
        {
            sb.AppendLine($"  Animator on '{Path(an.transform)}' enabled={an.enabled} speed={an.speed} culling={an.cullingMode} rootMotion={an.applyRootMotion}");

            RuntimeAnimatorController ctrl = an.runtimeAnimatorController;
            if (ctrl == null)
            {
                sb.AppendLine("    !! Controller slot is EMPTY -> nothing will ever animate.");
                sb.AppendLine("       Fix: assign the patched controller on the PREFAB's Animator component.");
                continue;
            }
            if (ctrl is AnimatorOverrideController aoc)
                sb.AppendLine($"    controller: '{aoc.name}' (OVERRIDE of '{(aoc.runtimeAnimatorController ? aoc.runtimeAnimatorController.name : "MISSING BASE!")}')");
            else
                sb.AppendLine($"    controller: '{ctrl.name}'");

            // Trigger parameters present on this controller?
            foreach (string t in Triggers)
            {
                bool found = false;
                foreach (AnimatorControllerParameter p in an.parameters)
                    if (p.name == t && p.type == AnimatorControllerParameterType.Trigger) { found = true; break; }
                if (!found) sb.AppendLine($"    !! trigger '{t}' does NOT exist on this controller -> that animation can never fire.");
            }

            // What is the machine actually doing right now?
            AnimatorClipInfo[] clip = an.GetCurrentAnimatorClipInfo(0);
            AnimatorStateInfo st = an.GetCurrentAnimatorStateInfo(0);
            if (clip.Length == 0)
                sb.AppendLine($"    !! current state (hash {st.shortNameHash}, normTime {st.normalizedTime:0.00}) has NO clip assigned -> empty state, model will freeze in bind pose.");
            else
                sb.AppendLine($"    current state plays clip '{clip[0].clip.name}' (len {clip[0].clip.length:0.00}s, loop={clip[0].clip.isLooping}, normTime {st.normalizedTime:0.00})");
        }

        Debug.Log(sb.ToString(), this);
    }

    // ---- Manual trigger tests: select the SPAWNED enemy in the hierarchy while
    // playing, right-click this component's header, fire one, watch the model. ----
    [ContextMenu("TEST: Fire OnEncounterStart")] void TestStart() { Fire("OnEncounterStart"); }
    [ContextMenu("TEST: Fire Attack")] void TestAttack() { Fire("Attack"); }
    [ContextMenu("TEST: Fire TakeDamage")] void TestHurt() { Fire("TakeDamage"); }
    [ContextMenu("TEST: Fire Die")] void TestDie() { Fire("Die"); }
    [ContextMenu("TEST: Fire Victory")] void TestVictory() { Fire("Victory"); }

    void Fire(string trigger)
    {
        foreach (Animator an in GetComponentsInChildren<Animator>(true))
        {
            Debug.Log($"[AnimatorDiagnostics] SetTrigger('{trigger}') -> '{Path(an.transform)}' (controller: {(an.runtimeAnimatorController ? an.runtimeAnimatorController.name : "NONE")})", an);
            an.SetTrigger(trigger);
        }
    }

    static string Path(Transform t)
    {
        string p = t.name;
        while (t.parent != null && t.parent != t.root) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
