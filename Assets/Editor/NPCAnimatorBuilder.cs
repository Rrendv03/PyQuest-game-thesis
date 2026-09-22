// NPCAnimatorBuilder.cs
//
// PYQUEST SETUP TOOL — builds the two-state Animator Controller for NPC guides.
//
// Companion to EncounterAnimatorBuilder, but for the static NPC guides that
// only ever Idle and Talk (they never move from their position).
//
// ONE MENU TOOL:
//
//   Tools > PyQuest > Create NPC Guide Animator Controller
//
//   1. Put this file (and NPCAnimationController.cs) in Assets. This one must
//      live in a folder named "Editor" (create Assets/Editor if needed) or
//      Unity won't compile it.
//   2. In the Project window, SELECT your NPC guide model (the FBX), or select
//      its animation clips directly.
//   3. Run the menu command.
//   4. Clips are auto-matched by name:
//        Idle ? "idle", "stand", "breath" (falls back to the first unmatched clip)
//        Talk ? "talk", "gesture", "point", "wave", "greet", "explain"
//      Anything unmatched is left empty — open the Animator window and drag
//      the clip onto that state. Both states are set to LOOP, since guides
//      stand in place.
//   5. The controller is created at Assets/NPCGuideAnimatorController.controller
//      and pinged. Assign it to the Animator on the NPC MODEL child (the child
//      of the InteractTrigger object that holds NPCController).
//
// WHAT NPCAnimationController FIRES (verified from the script):
//   Talk — NPCController starts a dialogue sequence (PlayTalk)
//   Idle — the sequence ends, or the NPC departs (BackToIdle)
//
// STATE MACHINE (deliberately tiny):
//   Idle (default) --Talk trigger--> Talk
//   Talk           --Idle trigger--> Idle
//   No exit-time returns: Talk is a loop that plays until the dialogue
//   sequence completes, so only the Idle trigger ends it. If you'd rather
//   have Talk auto-return after N seconds, add an exit-time transition in
//   the Animator window — the script's triggers still work either way.

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class NPCAnimatorBuilder
{
    // These must match NPCAnimationController's constants EXACTLY (case included).
    const string T_Talk = "Talk";
    const string T_Idle = "Idle";

    [MenuItem("Tools/PyQuest/Create NPC Guide Animator Controller")]
    public static void CreateController()
    {
        var clips = CollectSelectedClips();
        if (clips.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Create NPC Guide Animator Controller",
                "Select your NPC guide model (or its animation clips) in the Project window first,\n" +
                "then run Tools > PyQuest > Create NPC Guide Animator Controller again.", "OK");
            return;
        }

        // ---- Auto-match clips to the two states NPCAnimationController drives ----
        var pool = new List<AnimationClip>(clips);
        AnimationClip talkClip = TakeMatch(pool, "talk", "gesture", "point", "wave", "greet", "explain", "converse");
        AnimationClip idleClip = TakeMatch(pool, "idle", "stand", "breath") ?? pool.FirstOrDefault();

        // ---- Build the controller asset ----
        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/NPCGuideAnimatorController.controller");
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);

        controller.AddParameter(T_Talk, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(T_Idle, AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        var idle = AddState(sm, "Idle", idleClip, new Vector2(0, 0));
        var talk = AddState(sm, "Talk", talkClip, new Vector2(320, 0));

        // Guides never leave their spot, so both states loop in place.
        // NOTE: looping is a CLIP setting (AnimationClip settings), not a state
        // setting — there is no 'loopTime' on AnimatorState. Try to force it on
        // the matched clips; for FBX sub-clips the importer owns the setting, so
        // we also nudge via the model importer where possible.
        MakeLoop(idleClip);
        MakeLoop(talkClip);

        sm.defaultState = idle;

        // Idle -> Talk the instant NPCAnimationController fires PlayTalk().
        Immediate(idle, talk, T_Talk);
        // Talk -> Idle when BackToIdle() fires (sequence complete / departure).
        Immediate(talk, idle, T_Idle);

        AssetDatabase.SaveAssets();
        Selection.activeObject = controller;
        EditorGUIUtility.PingObject(controller);

        Debug.Log(
            "[NPCAnimatorBuilder] Created " + path + "\n" +
            "Assign it to the Animator on the NPC MODEL child (the child of the\n" +
            "InteractTrigger object that holds NPCController + NPCAnimationController).\n" +
            Report(idleClip, talkClip));
    }

    // ================================================================ helpers
    // Same conventions as EncounterAnimatorBuilder.

    static List<AnimationClip> CollectSelectedClips()
    {
        var result = new List<AnimationClip>();

        foreach (Object sel in Selection.objects)
        {
            if (sel is AnimationClip clip)
            {
                result.Add(clip);
                continue;
            }

            // Model import: pull every clip out of the FBX's sub-assets.
            // (Selection.objects only ever contains GameObjects for a model root —
            // it never contains a GameObject[] — so no array case is needed.)
            if (sel is GameObject || sel.name.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
            {
                foreach (Object asset in new[] { (Object)sel })
                {
                    foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(asset)))
                        if (sub is AnimationClip c && !c.name.StartsWith("__", System.StringComparison.Ordinal))
                            result.Add(c);
                }
            }
        }

        return result.Distinct().ToList();
    }

    // Set Loop Time on a clip. Standalone .anim assets can be edited directly;
    // clips inside an FBX are owned by the ModelImporter, so flip it in the
    // importer's clip settings instead (and reimport).
    static void MakeLoop(AnimationClip clip)
    {
        if (clip == null) return;

        string assetPath = AssetDatabase.GetAssetPath(clip);
        ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;

        if (importer != null)
        {
            var settings = importer.clipAnimations.ToList();
            if (settings.Count == 0)
            {
                // Importer hasn't been customized yet — materialize from defaults.
                settings = importer.defaultClipAnimations.ToList();
            }

            bool changed = false;
            foreach (var c in settings)
            {
                if (c.name == clip.name && !c.loopTime)
                {
                    c.loopTime = true;
                    changed = true;
                }
            }

            if (changed)
            {
                importer.clipAnimations = settings.ToArray();
                importer.SaveAndReimport();
            }
        }
        else
        {
            AnimationClipSettings settings2 = AnimationUtility.GetAnimationClipSettings(clip);
            if (!settings2.loopTime)
            {
                settings2.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, settings2);
                EditorUtility.SetDirty(clip);
            }
        }
    }

    static AnimationClip TakeMatch(List<AnimationClip> pool, params string[] keywords)
    {
        for (int i = 0; i < pool.Count; i++)
        {
            string n = pool[i].name.ToLowerInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
            if (keywords.Any(k => n.Contains(k)))
            {
                AnimationClip match = pool[i];
                pool.RemoveAt(i);
                return match;
            }
        }
        return null;
    }

    static AnimatorState AddState(AnimatorStateMachine sm, string name, AnimationClip clip, Vector2 position)
    {
        AnimatorState state = sm.AddState(name, position);
        state.motion = clip;
        state.writeDefaultValues = true;
        return state;
    }

    static void Immediate(AnimatorState from, AnimatorState to, string trigger)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = 0.1f;
        t.hasFixedDuration = true;
        t.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

    static string Report(AnimationClip idleClip, AnimationClip talkClip)
    {
        return "Clip match report:\n" +
               "  Idle: " + (idleClip != null ? idleClip.name : "(MISSING — drag an idle clip onto the Idle state)") + "\n" +
               "  Talk: " + (talkClip != null ? talkClip.name : "(MISSING — drag a talk/gesture clip onto the Talk state)");
    }
}