using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class EncounterAnimatorBuilder
{
    // These must match EncounterManager's SetTrigger calls EXACTLY (case included).
    const string T_EncounterStart = "OnEncounterStart";
    const string T_Attack = "Attack";
    const string T_TakeDamage = "TakeDamage";
    const string T_Die = "Die";
    const string T_Victory = "Victory";
    // NEW: walk-to-attack triggers (names must match EncounterManager's
    // enemyWalkTrigger / enemyWalkEndTrigger Inspector fields, which
    // default to "Walk" and "Idle").
    const string T_Walk = "Walk";
    const string T_Idle = "Idle";

    [MenuItem("Tools/PyQuest/Create Encounter Animator Controller")]
    public static void CreateController()
    {
        var clips = CollectSelectedClips();
        if (clips.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Create Encounter Animator Controller",
                "Select your skeleton model (or its animation clips) in the Project window first,\n" +
                "then run Tools > PyQuest > Create Encounter Animator Controller again.", "OK");
            return;
        }

        // ---- Auto-match clips to the states EncounterManager drives ----
        var pool = new List<AnimationClip>(clips);
        AnimationClip dieClip = TakeMatch(pool, "die", "death", "dead");
        AnimationClip victoryClip = TakeMatch(pool, "victory", "win", "cheer", "celebrate", "excited");
        AnimationClip attackClip = TakeMatch(pool, "attack", "swing", "punch", "slash", "stab", "kick", "combo");
        AnimationClip hurtClip = TakeMatch(pool, "hurt", "gethit", "hit", "damage", "pain", "flinch", "wound");
        AnimationClip walkClip = TakeMatch(pool, "walk", "run", "move", "step", "locomotion");
        AnimationClip startClip = TakeMatch(pool, "roar", "alert", "spawn", "battlecry", "battlecry", "taunt", "intro");
        AnimationClip idleClip = TakeMatch(pool, "idle", "stand", "breath") ?? pool.FirstOrDefault();

        // ---- Build the controller asset ----
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/EncounterAnimatorController.controller");
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);

        controller.AddParameter(T_EncounterStart, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(T_Attack, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(T_TakeDamage, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(T_Die, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(T_Victory, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(T_Walk, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(T_Idle, AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        var idle = AddState(sm, "Idle", idleClip, new Vector2(0, 0));
        var start = AddState(sm, "EncounterStart", startClip, new Vector2(320, -180));
        var attack = AddState(sm, "Attack", attackClip, new Vector2(320, -60));
        var hurt = AddState(sm, "TakeDamage", hurtClip, new Vector2(320, 60));
        var victory = AddState(sm, "Victory", victoryClip, new Vector2(320, 180));
        var die = AddState(sm, "Die", dieClip, new Vector2(580, -60));
        var walk = AddState(sm, "Walk", walkClip, new Vector2(0, 180));

        sm.defaultState = idle;

        // Triggered interrupts: Idle -> X the instant EncounterManager fires the trigger.
        Immediate(idle, start, T_EncounterStart);
        Immediate(idle, attack, T_Attack);
        Immediate(idle, hurt, T_TakeDamage);
        Immediate(idle, victory, T_Victory);

        // NEW: walk-to-attack wiring. EncounterManager fires Walk when the
        // enemy starts moving (toward the player, and again for the walk
        // home) and Idle when it is back at its spot. While walking, the
        // enemy can still be hit (TakeDamage) or reach the player and
        // strike (Attack), so those triggers must also exit the Walk state.
        Immediate(idle, walk, T_Walk);
        Immediate(walk, attack, T_Attack);
        Immediate(walk, hurt, T_TakeDamage);
        AfterClip(walk, idle);

        // One-shot animations return to Idle when they finish.
        AfterClip(start, idle);
        AfterClip(attack, idle);
        AfterClip(hurt, idle);
        AfterClip(victory, idle);

        // Death beats everything, from anywhere, and never comes back.
        AnimatorStateTransition death = sm.AddAnyStateTransition(die);
        death.hasExitTime = false;
        death.duration = 0.05f;
        death.canTransitionToSelf = false; // REQUIRED, or Any State re-fires into itself forever
        death.AddCondition(AnimatorConditionMode.If, 0f, T_Die);

        AssetDatabase.SaveAssets();
        Selection.activeObject = controller;
        EditorGUIUtility.PingObject(controller);

        Debug.Log(
            "[EncounterAnimatorBuilder] Created " + path + "\n" +
            "Assign it to the Animator on your enemy prefab.\n" + Report(idleClip, startClip, attackClip, hurtClip, victoryClip, dieClip, walkClip));
    }

    // ================================================================ PATCH MODE
    // For packs that ship their own working controller (+ override variants).
    // Additive only: adds the five triggers, Any State entries, and clip-end
    // returns. Never deletes or rewrites the pack's states/transitions.

    [MenuItem("Tools/PyQuest/Patch Triggers into Selected Animator Controller")]
    public static void PatchSelectedController()
    {
        Object sel = Selection.activeObject;
        if (sel == null)
        {
            EditorUtility.DisplayDialog(
                "Patch PyQuest Triggers",
                "Select the pack's controller in the Project window first —\n" +
                "e.g. 'death.overrideController' or the base '.controller' asset —\n" +
                "then run Tools > PyQuest > Patch Triggers again.", "OK");
            return;
        }

        // An .overrideController is just a clip-swap layer over a real controller.
        // Patch the BASE so every override variant inherits the new logic.
        AnimatorController controller;
        var aoc = sel as AnimatorOverrideController;
        if (aoc != null)
        {
            controller = aoc.runtimeAnimatorController as AnimatorController;
            if (controller == null)
            {
                EditorUtility.DisplayDialog(
                    "Patch PyQuest Triggers",
                    "'" + aoc.name + "' is an override controller, but its base controller\n" +
                    "could not be loaded (missing/reimported asset?). Select the base\n" +
                    ".controller file directly instead — with the override selected, look\n" +
                    "at the Controller slot in the Inspector to find its name.", "OK");
                return;
            }
            Debug.Log("[EncounterAnimatorBuilder] '" + aoc.name + "' is an override of '" +
                      controller.name + "'. Patching the BASE controller so ALL of its " +
                      "override variants (idle, slash, fall, death, ...) inherit the triggers.");
        }
        else
        {
            controller = sel as AnimatorController;
            if (controller == null)
            {
                EditorUtility.DisplayDialog(
                    "Patch PyQuest Triggers",
                    "Selection is not an Animator Controller. Select either:\n" +
                    "  • an .overrideController (its base gets patched), or\n" +
                    "  • the pack's plain .controller asset.", "OK");
                return;
            }
        }

        Undo.RegisterCompleteObjectUndo(controller, "Patch PyQuest Triggers");
        var log = new System.Text.StringBuilder("[EncounterAnimatorBuilder] Patched '" + controller.name + "':\n");

        // ---- 1. Parameters: only add the ones that don't exist yet ----
        foreach (string trig in new[] { T_EncounterStart, T_Attack, T_TakeDamage, T_Die, T_Victory, T_Walk, T_Idle })
            if (!HasParameter(controller, trig))
            {
                controller.AddParameter(trig, AnimatorControllerParameterType.Trigger);
                log.AppendLine("  + trigger parameter : " + trig);
            }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // ---- 2. Find the pack's existing states by name ----
        AnimatorState idle = FindState(sm, "idle", "stand", "breath");
        AnimatorState attack = FindState(sm, "attack", "slash", "swing", "punch", "stab", "kick", "combo", "strike");
        AnimatorState hurt = FindState(sm, "hurt", "gethit", "hit", "damage", "pain", "flinch", "wound", "takehit");
        AnimatorState die = FindState(sm, "die", "death", "dead");
        AnimatorState victory = FindState(sm, "victory", "win", "cheer", "celebrate", "excited");
        AnimatorState start = FindState(sm, "roar", "alert", "spawn", "battlecry", "taunt", "intro");
        AnimatorState walk = FindState(sm, "walk", "run", "move", "step", "locomotion");

        if (idle == null)
            log.AppendLine("  ! no Idle-looking state found — clip-end returns skipped (tell me your state names and I'll match them).");

        // ---- 3. Wire EncounterManager's triggers in ----
        WireTrigger(sm, start, T_EncounterStart, idle, log);
        WireTrigger(sm, attack, T_Attack, idle, log);
        WireTrigger(sm, hurt, T_TakeDamage, idle, log);
        WireTrigger(sm, victory, T_Victory, idle, log);

        // NEW: walk-to-attack wiring. Walk is entered from Idle and returns
        // to Idle when its clip ends; Attack/TakeDamage must also be
        // reachable from Walk because the enemy can be hit or strike while
        // walking. The Idle trigger simply kicks the state back to Idle
        // (EncounterManager fires it when the enemy is home again).
        if (idle != null && walk != null && idle != walk)
        {
            if (!HasTransition(idle, walk, T_Walk))
            {
                Immediate(idle, walk, T_Walk);
                log.AppendLine("  + Idle --Walk--> '" + walk.name + "'");
            }
            if (walk.transitions.All(t => !t.conditions.Any(c => c.parameter == T_Attack)))
            {
                if (attack != null && attack != walk) { Immediate(walk, attack, T_Attack); log.AppendLine("  + '" + walk.name + "' --Attack--> '" + attack.name + "'"); }
            }
            if (hurt != null && hurt != walk && walk.transitions.All(t => !t.conditions.Any(c => c.parameter == T_TakeDamage)))
            {
                Immediate(walk, hurt, T_TakeDamage);
                log.AppendLine("  + '" + walk.name + "' --TakeDamage--> '" + hurt.name + "'");
            }
            if (walk.transitions.Length == 0 || walk.transitions.All(t => t.destinationState != idle))
            {
                AfterClip(walk, idle);
                log.AppendLine("      + '" + walk.name + "' --clip end--> Idle");
            }
        }
        else if (walk == null)
            log.AppendLine("  ! no Walk-looking state found (walk/run/move/step/locomotion) — walk animation not wired; the enemy will still move, just without a walk clip.");

        if (die != null)
        {
            if (!AnyStateHasCondition(sm, T_Die))
            {
                AnimatorStateTransition death = sm.AddAnyStateTransition(die);
                death.hasExitTime = false;
                death.duration = 0.05f;
                death.canTransitionToSelf = false; // REQUIRED, or Any State re-fires into itself forever
                death.AddCondition(AnimatorConditionMode.If, 0f, T_Die);
                log.AppendLine("  + Any State --Die--> '" + die.name + "' (terminal, no return)");
            }
        }
        else log.AppendLine("  ! no Death-looking state found — 'Die' trigger not wired.");

        // ---- 4. Dump what the pack controller already uses, for reference ----
        log.AppendLine("  Existing parameters: " +
            (controller.parameters.Length == 0 ? "(none)" :
             string.Join(", ", controller.parameters.Select(p => p.name + " (" + p.type + ")"))));

        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(controller);
        Debug.Log(log.ToString());
    }

    // ------------------------------------------------------------------ helpers

    static bool HasParameter(AnimatorController controller, string name) =>
        controller.parameters.Any(p => p.name == name);

    static AnimatorState FindState(AnimatorStateMachine sm, params string[] keys)
    {
        foreach (ChildAnimatorState entry in sm.states)
            if (StateNameHas(entry.state.name, keys)) return entry.state;
        return null;
    }

    static bool StateNameHas(string stateName, string[] keys)
    {
        string n = stateName.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
        return keys.Any(k => n.Contains(k));
    }

    // NEW: does 'from' already have a transition to 'to' gated on 'trigger'?
    static bool HasTransition(AnimatorState from, AnimatorState to, string trigger) =>
        from != null && to != null &&
        from.transitions.Any(t => t.destinationState == to &&
                                  t.conditions.Any(c => c.parameter == trigger));

    static bool AnyStateHasCondition(AnimatorStateMachine sm, string trigger) =>
        sm.anyStateTransitions.Any(t => t.conditions.Any(c => c.parameter == trigger));

    // Any State --trigger--> state; then the state returns to Idle when its clip
    // ends — but only if the pack left the state with no exit logic of its own.
    static void WireTrigger(AnimatorStateMachine sm, AnimatorState state, string trigger,
                            AnimatorState idle, System.Text.StringBuilder log)
    {
        if (state == null)
        {
            log.AppendLine("  ! no state matched for trigger '" + trigger + "' — skipped.");
            return;
        }
        if (AnyStateHasCondition(sm, trigger)) return; // already wired

        AnimatorStateTransition t = sm.AddAnyStateTransition(state);
        t.hasExitTime = false;
        t.duration = 0.05f;
        t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        log.AppendLine("  + Any State --" + trigger + "--> '" + state.name + "'");

        if (idle != null && idle != state && state.transitions.Length == 0)
        {
            AfterClip(state, idle);
            log.AppendLine("      + '" + state.name + "' --clip end--> Idle");
        }
    }

    static AnimatorState AddState(AnimatorStateMachine sm, string stateName, AnimationClip motion, Vector2 position)
    {
        // AnimatorState has no settable 'position'; the layout is set through the
        // state machine's AddState(string, Vector2) overload instead.
        AnimatorState state = sm.AddState(stateName, position);
        state.motion = motion;
        return state;
    }

    static void Immediate(AnimatorState from, AnimatorState to, string trigger)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = false;      // fire immediately when the trigger is set
        t.duration = 0.05f;
        t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

    static void AfterClip(AnimatorState from, AnimatorState to)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = true;       // wait until the clip finishes
        t.exitTime = 1f;
        t.duration = 0.05f;
    }

    static AnimationClip TakeMatch(List<AnimationClip> pool, params string[] keys)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (NameHas(pool[i], keys))
            {
                AnimationClip clip = pool[i];
                pool.RemoveAt(i);
                return clip;
            }
        }
        return null;
    }

    static bool NameHas(AnimationClip clip, params string[] keys)
    {
        string n = clip.name.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
        return keys.Any(k => n.Contains(k));
    }

    static List<AnimationClip> CollectSelectedClips()
    {
        var clips = new List<AnimationClip>();
        foreach (Object obj in Selection.objects)
        {
            if (obj == null) continue;

            if (obj is AnimationClip clip)
            {
                if (!clip.name.StartsWith("__")) clips.Add(clip); // skip Unity's internal preview clips
                continue;
            }

            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) continue;

            foreach (AnimationClip c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                if (!c.name.StartsWith("__"))
                    clips.Add(c);
        }
        return clips.Distinct().ToList();
    }

    static string Report(AnimationClip idle, AnimationClip start, AnimationClip attack,
                         AnimationClip hurt, AnimationClip victory, AnimationClip die,
                         AnimationClip walk = null)
    {
        var sb = new System.Text.StringBuilder("Clip assignment:\n");
        sb.AppendLine("  Idle           : " + (idle ? idle.name : "<EMPTY - drag a clip onto this state>"));
        sb.AppendLine("  EncounterStart : " + (start ? start.name : "<none (state can be deleted)>"));
        sb.AppendLine("  Attack         : " + (attack ? attack.name : "<EMPTY - drag a clip onto this state>"));
        sb.AppendLine("  TakeDamage     : " + (hurt ? hurt.name : "<EMPTY - drag a clip onto this state>"));
        sb.AppendLine("  Victory        : " + (victory ? victory.name : "<EMPTY - drag a clip onto this state>"));
        sb.AppendLine("  Die            : " + (die ? die.name : "<EMPTY - drag a clip onto this state>"));
        sb.AppendLine("  Walk           : " + (walk ? walk.name : "<EMPTY - walk animation missing; enemy will still move but slide>"));
        sb.Append("Triggers created: OnEncounterStart, Attack, TakeDamage, Die, Victory, Walk, Idle");
        return sb.ToString();
    }
}
