// PlayerAnimatorBuilder.cs
//
// PYQUEST SETUP TOOL — builds an Animator Controller pre-wired to what
// EncounterManager fires on the PLAYER, plus the free-roam walk/idle
// locomotion. This is the player-side mirror of EncounterAnimatorBuilder
// (the enemy tool), using the same recipe that already works there.
//
// WHAT ENCOLUNTERMANAGER FIRES ON THE PLAYER (verified from the script):
//   Attack     — the player throws the ice ball (throw wind-up). Fired on
//                origin.GetComponent<Animator>() in ThrowIceBall.
//   TakeDamage — the enemy's strike lands on the player (PerformedWalkAttack
//                passes PlayerAnimator() + "TakeDamage").
//   Victory    — (NEW, added by this build) the player WINS the encounter.
//   Die        — (NEW, added by this build) the player LOSES the encounter.
//   IsWalking  — NOT a trigger: a BOOL set every physics frame by
//                PlayerMovement (1 = moving, 0 = standing). The player walks
//                freely in the world, so a continuous bool beats the enemy's
//                Walk/Idle trigger pair here.
//
// TWO MENU TOOLS — pick based on what the pack gave you:
//
// A) Tools > PyQuest > Create Player Animator Controller
//    Builds a brand-new controller from the player FBX's clips.
//    1. Put this file in a folder named "Editor" inside Assets (create
//       Assets/Editor if you don't have one). Must live in an Editor folder.
//    2. In the Project window, SELECT your player skeleton model (the FBX),
//       or select some of its animation clips directly.
//    3. Run the menu command.
//    4. Clips are auto-matched to states by name (idle / walk-run-move /
//       attack-throw-cast / hurt-hit-damage / victory-win-cheer /
//       die-death-fall). Anything unmatched is left empty — open the
//       Animator window and drag the clip onto that state.
//    5. The controller is created at Assets/PlayerAnimatorController.controller
//       and pinged. Assign it to the Animator on your player prefab.
//
// B) Tools > PyQuest > Patch Player Triggers into Selected Animator Controller
//    Use when the player pack ALREADY ships a working controller — e.g. a
//    base .controller plus per-state variants like death.overrideController
//    (same layout as the enemy pack had). Select the override controller (or
//    the base .controller) and run this: it ADDS the player triggers, the
//    IsWalking bool, and the minimal transitions WITHOUT touching the pack's
//    existing states, clips or transitions.

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PlayerAnimatorBuilder
{
    // Must match EncounterManager's SetTrigger calls EXACTLY (case included).
    const string T_Attack = "Attack";
    const string T_TakeDamage = "TakeDamage";
    const string T_Victory = "Victory";
    const string T_Die = "Die";
    const string P_IsWalking = "IsWalking";
    const string P_IsRunning = "IsRunning";

    // Clip-name families/keywords for the directional locomotion matcher.
    static readonly string[] WalkFamily = { "walk", "step", "strafe" };
    static readonly string[] RunFamily = { "run", "sprint", "jog" };
    static readonly string[] DirForward = { "forward", "fwd", "front" };
    static readonly string[] DirBackward = { "backward", "back", "reverse" };
    static readonly string[] DirLeft = { "left" };
    static readonly string[] DirRight = { "right" };
    // Directional blend tree params, driven by PlayerMovement every physics
    // frame (camera-relative strafe input, -1..1). The Walk state holds a
    // 2D Freeform Directional blend tree over these.
    const string P_MoveX = "MoveX";
    const string P_MoveY = "MoveY";

    [MenuItem("Tools/PyQuest/Create Player Animator Controller")]
    public static void CreateController()
    {
        var clips = CollectSelectedClips();
        if (clips.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Create Player Animator Controller",
                "Select your player model (or its animation clips) in the Project window first,\n" +
                "then run Tools > PyQuest > Create Player Animator Controller again.", "OK");
            return;
        }

        // ---- Auto-match clips to the states the player needs ----
        var pool = new List<AnimationClip>(clips);
        AnimationClip dieClip = TakeMatch(pool, "die", "death", "dead", "fall");
        AnimationClip victoryClip = TakeMatch(pool, "victory", "win", "cheer", "celebrate", "excited");
        AnimationClip attackClip = TakeMatch(pool, "throw", "cast", "spell", "attack", "swing", "punch");
        AnimationClip hurtClip = TakeMatch(pool, "hurt", "gethit", "hit", "damage", "pain", "flinch", "wound");

        // ---- NEW: 4-direction walk + 4-direction run clips ----
        // Directional clips are matched FIRST so a clip literally named
        // "WalkForward" is not consumed by the generic walk fallback below.
        // A clip counts as directional when its name contains BOTH a family
        // key (walk/run) AND a direction key. Anything the pack doesn't
        // provide falls back to the generic walk clip (or the opposite
        // side's set), so the blend tree always exists and movement can
        // never be left without a clip to play.
        AnimationClip walkFwd = TakeMatchDir(pool, WalkFamily, DirForward);
        AnimationClip walkBack = TakeMatchDir(pool, WalkFamily, DirBackward);
        AnimationClip walkLeft = TakeMatchDir(pool, WalkFamily, DirLeft);
        AnimationClip walkRight = TakeMatchDir(pool, WalkFamily, DirRight);

        // Generic walk = whatever's left (shuffled AFTER the directionals).
        AnimationClip walkClip = TakeMatch(pool, "walk", "step", "locomotion", "move");

        // NOTE the order: directional run clips are matched FIRST, and only
        // whatever's LEFT in the pool becomes the generic run clip. (Earlier
        // attempt matched the generic 'run' clip first, which swallowed
        // 'RunForward' and left the forward-blend slot empty.) Fallbacks for a
        // missing direction: the generic run clip, then the matching walk
        // direction, then the generic walk clip — the blend tree always has
        // something playable in every quadrant.
        AnimationClip runFwd = TakeMatchDir(pool, RunFamily, DirForward);
        AnimationClip runBack = TakeMatchDir(pool, RunFamily, DirBackward);
        AnimationClip runLeft = TakeMatchDir(pool, RunFamily, DirLeft);
        AnimationClip runRight = TakeMatchDir(pool, RunFamily, DirRight);
        AnimationClip runClip = TakeMatch(pool, "run", "sprint", "jog");
        runFwd = runFwd ?? runClip ?? runFwdAnim(walkFwd, walkClip);
        runBack = runBack ?? runClip ?? runFwdAnim(walkBack, walkClip);
        runLeft = runLeft ?? runClip ?? runFwdAnim(walkLeft, walkClip);
        runRight = runRight ?? runClip ?? runFwdAnim(walkRight, walkClip);

        AnimationClip idleClip = TakeMatch(pool, "idle", "stand", "breath") ?? pool.FirstOrDefault();

        // ---- Build the controller asset ----
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/PlayerAnimatorController.controller");
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);

        controller.AddParameter(T_Attack, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(T_TakeDamage, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(T_Victory, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(T_Die, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(P_IsWalking, AnimatorControllerParameterType.Bool);
        // NEW: run flag driven by PlayerMovement using the SAME runThreshold
        // as the speed curve. true = run blend tree, false = walk blend tree.
        controller.AddParameter(P_IsRunning, AnimatorControllerParameterType.Bool);
        controller.AddParameter(P_MoveX, AnimatorControllerParameterType.Float);
        controller.AddParameter(P_MoveY, AnimatorControllerParameterType.Float);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        var idle = AddState(sm, "Idle", idleClip, new Vector2(0, 0));
        var walk = AddBlendState(sm, "Walk", MakeDirectionalTree(
                        "WalkDirections", walkClip,
                        walkFwd, walkBack, walkLeft, walkRight,
                        P_MoveX, P_MoveY, controller), new Vector2(0, 180));
        var run = AddBlendState(sm, "Run", MakeDirectionalTree(
                        "RunDirections", runClip,
                        runFwd, runBack, runLeft, runRight,
                        P_MoveX, P_MoveY, controller), new Vector2(0, 330));
        var attack = AddState(sm, "Attack", attackClip, new Vector2(320, -60));
        var hurt = AddState(sm, "TakeDamage", hurtClip, new Vector2(320, 60));
        var victory = AddState(sm, "Victory", victoryClip, new Vector2(320, 180));
        var die = AddState(sm, "Die", dieClip, new Vector2(580, -60));

        sm.defaultState = idle;

        // ---- Locomotion: Idle <-> Walk <-> Run ----
        // Idle -> Walk only when NOT running; Idle -> Run when running.
        AnimatorStateTransition idleToWalk = BoolAnim(idle, walk, AnimatorConditionMode.If, P_IsWalking);
        idleToWalk.AddCondition(AnimatorConditionMode.IfNot, 0f, P_IsRunning);
        AnimatorStateTransition idleToRun = BoolAnim(idle, run, AnimatorConditionMode.If, P_IsWalking);
        idleToRun.AddCondition(AnimatorConditionMode.If, 0f, P_IsRunning);
        BoolAnim(walk, idle, AnimatorConditionMode.IfNot, P_IsWalking);
        BoolAnim(run, idle, AnimatorConditionMode.IfNot, P_IsWalking);
        BoolAnim(walk, run, AnimatorConditionMode.If, P_IsRunning);
        BoolAnim(run, walk, AnimatorConditionMode.IfNot, P_IsRunning);

        // Attack interrupts locomotion from ANY locomotion state, one-shot back to Idle.
        Immediate(idle, attack, T_Attack);
        Immediate(walk, attack, T_Attack);
        Immediate(run, attack, T_Attack);
        AfterClip(attack, idle);

        // TakeDamage beats everything EXCEPT death, and returns to Idle when
        // the flinch clip ends. Any State with canTransitionToSelf=false is the
        // proven pattern from the enemy controller — a bare wrapper trigger
        // fires the moment EncounterManager SetTriggers it, even mid-throw.
        AnimatorStateTransition hurtIn = sm.AddAnyStateTransition(hurt);
        hurtIn.hasExitTime = false;
        hurtIn.duration = 0.05f;
        hurtIn.canTransitionToSelf = false; // REQUIRED, or Any State re-fires into itself forever
        hurtIn.AddCondition(AnimatorConditionMode.If, 0f, T_TakeDamage);
        AfterClip(hurt, idle);

        // Victory: one-shot celebration when a fight is won, back to Idle.
        AnimatorStateTransition win = sm.AddAnyStateTransition(victory);
        win.hasExitTime = false;
        win.duration = 0.05f;
        win.canTransitionToSelf = false;
        win.AddCondition(AnimatorConditionMode.If, 0f, T_Victory);
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
            "[PlayerAnimatorBuilder] Created " + path + "\n" +
            "Assign it to the Animator on your player prefab.\n" +
            Report(idleClip, attackClip, hurtClip, victoryClip, dieClip,
                   walkFwd, walkBack, walkLeft, walkRight,
                   runFwd, runBack, runLeft, runRight));
    }

    // ================================================================ PATCH MODE
    // For packs that ship their own working controller (+ override variants).
    // Additive only: adds the missing parameters and the minimal transitions.
    // Never deletes or rewrites the pack's states/transitions.

    [MenuItem("Tools/PyQuest/Patch Player Triggers into Selected Animator Controller")]
    public static void PatchSelectedController()
    {
        Object sel = Selection.activeObject;
        if (sel == null)
        {
            EditorUtility.DisplayDialog(
                "Patch Player Triggers",
                "Select the pack's player controller in the Project window first —\n" +
                "e.g. 'death.overrideController' or the base '.controller' asset —\n" +
                "then run this again.", "OK");
            return;
        }

        AnimatorController controller;
        var aoc = sel as AnimatorOverrideController;
        if (aoc != null)
        {
            controller = aoc.runtimeAnimatorController as AnimatorController;
            if (controller == null)
            {
                EditorUtility.DisplayDialog(
                    "Patch Player Triggers",
                    "'" + aoc.name + "' is an override controller, but its base controller\n" +
                    "could not be loaded. Select the base .controller file directly instead.", "OK");
                return;
            }
            Debug.Log("[PlayerAnimatorBuilder] '" + aoc.name + "' is an override of '" +
                      controller.name + "'. Patching the BASE controller so ALL of its " +
                      "override variants inherit the new logic.");
        }
        else
        {
            controller = sel as AnimatorController;
            if (controller == null)
            {
                EditorUtility.DisplayDialog(
                    "Patch Player Triggers",
                    "Selection is not an Animator Controller. Select either:\n" +
                    "  • an .overrideController (its base gets patched), or\n" +
                    "  • the pack's plain .controller asset.", "OK");
                return;
            }
        }

        Undo.RegisterCompleteObjectUndo(controller, "Patch Player Triggers");
        var log = new System.Text.StringBuilder("[PlayerAnimatorBuilder] Patched '" + controller.name + "':\n");

        // ---- 1. Parameters: only add the ones that don't exist yet ----
        foreach (var p in new[] {
            (T_Attack,     AnimatorControllerParameterType.Trigger),
            (T_TakeDamage, AnimatorControllerParameterType.Trigger),
            (T_Victory,    AnimatorControllerParameterType.Trigger),
            (T_Die,        AnimatorControllerParameterType.Trigger),
            (P_IsWalking,  AnimatorControllerParameterType.Bool),
            (P_IsRunning,  AnimatorControllerParameterType.Bool),
            // Directional blend params — declared even if the pack's state
            // doesn't use them yet (a float param no transition reads is
            // harmless, but PlayerMovement writes them every frame).
            (P_MoveX,      AnimatorControllerParameterType.Float),
            (P_MoveY,      AnimatorControllerParameterType.Float) })
            if (!HasParameter(controller, p.Item1))
            {
                controller.AddParameter(p.Item1, p.Item2);
                log.AppendLine("  + parameter '" + p.Item1 + "' (" + p.Item2 + ")");
            }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // ---- 2. Find the pack's states by name (same fuzzy matching as the enemy tool) ----
        AnimatorState idle = FindState(sm, "idle", "stand");
        AnimatorState walk = FindState(sm, "walk", "run", "locomotion");
        AnimatorState attack = FindState(sm, "attack", "throw", "cast", "swing", "punch", "spell");
        AnimatorState hurt = FindState(sm, "hurt", "gethit", "hit", "damage", "pain", "flinch");
        AnimatorState victory = FindState(sm, "victory", "win", "cheer", "celebrate");
        AnimatorState die = FindState(sm, "die", "death", "dead", "fall");

        // ---- 3. Minimal wiring. WireTrigger = Any State --trigger--> state
        //         plus a clip-end return to Idle if the state has no exit logic.
        WireTrigger(sm, attack, T_Attack, idle, log);
        WireTrigger(sm, hurt, T_TakeDamage, idle, log);
        WireTrigger(sm, victory, T_Victory, idle, log);

        if (die != null && !AnyStateHasCondition(sm, T_Die))
        {
            AnimatorStateTransition death = sm.AddAnyStateTransition(die);
            death.hasExitTime = false;
            death.duration = 0.05f;
            death.canTransitionToSelf = false;
            death.AddCondition(AnimatorConditionMode.If, 0f, T_Die);
            log.AppendLine("  + Any State --Die--> '" + die.name + "' (terminal, no return)");
        }

        // Locomotion: only wire if BOTH an idle-looking and a walk-looking
        // state exist and neither bool transition is present yet.
        if (idle != null && walk != null && idle != walk)
        {
            if (!idle.transitions.Any(t => destIs(t, walk) && hasBoolCond(t, P_IsWalking)))
            {
                Anim(idle, walk, AnimatorConditionMode.If);
                log.AppendLine("  + '" + idle.name + "' --IsWalking=true--> '" + walk.name + "'");
            }
            if (!walk.transitions.Any(t => destIs(t, idle) && hasBoolCond(t, P_IsWalking)))
            {
                Anim(walk, idle, AnimatorConditionMode.IfNot);
                log.AppendLine("  + '" + walk.name + "' --IsWalking=false--> '" + idle.name + "'");
            }
        }
        else if (idle == null || walk == null)
            log.AppendLine("  ! could not find both an Idle-looking and a Walk-looking state — IsWalking not wired. Wire them in the Animator window: Idle --IsWalking--> Walk and Walk --!IsWalking--> Idle.");

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
        AnimatorState state = sm.AddState(stateName, position);
        state.motion = motion;
        return state;
    }

    // Bool-condition transition: "If" reads as true, "IfNot" as false.
    // NOTE param: walk transitions condition on IsWalking, walk<->run
    // transitions condition on IsRunning - always pass it explicitly.
    static AnimatorStateTransition BoolAnim(AnimatorState from, AnimatorState to,
        AnimatorConditionMode mode, string param)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = 0.1f;
        t.AddCondition(mode, 0f, param);
        return t;
    }

    static void Anim(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
        => BoolAnim(from, to, mode, P_IsWalking);

    // ---- NEW: directional blend tree helpers (4-direction walk/run) ----

    // Takes the first pool clip whose name contains BOTH a family key
    // (walk/run) AND a direction key (forward/back/left/right).
    static AnimationClip TakeMatchDir(List<AnimationClip> pool, string[] family, string[] dirKeys)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (NameHas(pool[i], family) && NameHas(pool[i], dirKeys))
            {
                AnimationClip clip = pool[i];
                pool.RemoveAt(i);
                return clip;
            }
        }
        return null;
    }

    // 2D Freeform Directional blend tree over MoveX/MoveY. Child positions:
    // forward (0,1), right (1,0), back (0,-1), left (-1,0) - exactly the
    // camera-relative input space PlayerMovement feeds in. Any missing
    // direction falls back to the generic locomotion clip so the tree is
    // never empty (worst case: every direction plays the same clip, which
    // still animates - just not direction-specific).
    static BlendTree MakeDirectionalTree(string treeName, AnimationClip fallbackClip,
        AnimationClip fwd, AnimationClip back, AnimationClip left, AnimationClip right,
        string px, string py, AnimatorController controller)
    {
        BlendTree tree = new BlendTree();
        tree.name = treeName;
        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = px;
        tree.blendParameterY = py;
        tree.useAutomaticThresholds = false;

        var children = new List<ChildMotion>();
        void Add(AnimationClip clip, Vector2 pos)
        {
            AnimationClip use = clip != null ? clip : fallbackClip;
            if (use == null) return;
            children.Add(new ChildMotion { motion = use, position = pos });
        }
        Add(fwd, new Vector2(0f, 1f));
        Add(right, new Vector2(1f, 0f));
        Add(back, new Vector2(0f, -1f));
        Add(left, new Vector2(-1f, 0f));
        tree.children = children.ToArray();

        // Blend trees must be embedded in the controller asset to persist.
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.hideFlags = HideFlags.HideInHierarchy;
        return tree;
    }

    static AnimatorState AddBlendState(AnimatorStateMachine sm, string stateName,
                                       Motion motion, Vector2 position)
    {
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
        if (from == null || to == null || from == to) return;
        if (from.transitions.Any(t => t.destinationState == to)) return;
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = true;       // wait until the clip finishes
        t.exitTime = 1f;
        t.duration = 0.05f;
    }

    static bool destIs(AnimatorStateTransition t, AnimatorState s) => t.destinationState == s;
    static bool hasBoolCond(AnimatorStateTransition t, string p) =>
        t.conditions.Any(c => c.parameter == p);

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

    // Fallback chain for a missing directional run slot: matching walk
    // direction clip, else the generic walk clip. Keeps the blend tree playable.
    static AnimationClip runFwdAnim(AnimationClip preferred, AnimationClip fallback)
        => preferred != null ? preferred : fallback;

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

    static string Report(AnimationClip idle, AnimationClip attack, AnimationClip hurt,
                         AnimationClip victory, AnimationClip die,
                         AnimationClip wFwd, AnimationClip wBack, AnimationClip wLeft, AnimationClip wRight,
                         AnimationClip rFwd, AnimationClip rBack, AnimationClip rLeft, AnimationClip rRight)
    {
        var sb = new System.Text.StringBuilder("Clip assignment:\n");
        sb.AppendLine("  Idle              : " + (idle ? idle.name : "<EMPTY - drag a clip onto this state>"));
        sb.AppendLine("  Attack (throw)    : " + (attack ? attack.name : "<EMPTY - drag a clip onto this state>"));
        sb.AppendLine("  TakeDamage        : " + (hurt ? hurt.name : "<EMPTY - drag a clip onto this state>"));
        sb.AppendLine("  Victory           : " + (victory ? victory.name : "<none (state can be deleted)>"));
        sb.AppendLine("  Die               : " + (die ? die.name : "<EMPTY - drag a clip onto this state>"));
        sb.AppendLine("  Walk forward      : " + ClipOr(wFwd));
        sb.AppendLine("  Walk backward     : " + ClipOr(wBack));
        sb.AppendLine("  Walk left         : " + ClipOr(wLeft));
        sb.AppendLine("  Walk right        : " + ClipOr(wRight));
        sb.AppendLine("  Run forward       : " + ClipOr(rFwd));
        sb.AppendLine("  Run backward      : " + ClipOr(rBack));
        sb.AppendLine("  Run left          : " + ClipOr(rLeft));
        sb.AppendLine("  Run right         : " + ClipOr(rRight));
        sb.Append("Parameters created: Attack, TakeDamage, Victory, Die (triggers) + " +
                  "IsWalking / IsRunning (bools) + MoveX / MoveY (floats) - all driven by " +
                  "PlayerMovement every physics frame. Walk and Run are 2D directional " +
                  "blend trees; missing direction clips fall back to the generic walk clip.");
        return sb.ToString();
    }

    static string ClipOr(AnimationClip clip) => clip ? clip.name : "<fallback: generic walk clip>";
}
