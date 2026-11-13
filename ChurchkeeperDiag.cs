using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Read-only runtime dump of the chapel maid: objects whose path contains
/// <c>Churchkeeper</c>, their PlayMaker FSMs with every transition, the sprite
/// animation clips, the interaction components and the intro flag. Nothing is
/// sent to any FSM and no state is changed.
/// </summary>
internal static class ChurchkeeperDiag
{
    /// <summary>Part of the object path that marks an object as the chapel maid tree.</summary>
    private const string NameFilter = "Churchkeeper";

    /// <summary>Logs the whole dump; never throws.</summary>
    internal static void Dump()
    {
        Plugin.Log.LogInfo("[NPC] === dump ===");
        try
        {
            DumpObjects();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[NPC] object dump failed: {ex}");
        }

        LogPlayerData();
        LogDialogueApi();
    }

    /// <summary>Dumps every scene object whose path contains <see cref="NameFilter"/>.</summary>
    private static void DumpObjects()
    {
        int count = 0;
        foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
        {
            Transform? node = candidate;
            if (node == null || !node.gameObject.scene.IsValid())
            {
                continue;
            }

            string path = Path(node);
            if (path.IndexOf(NameFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            count++;
            LogObject(node, path);
        }

        Plugin.Log.LogInfo(count == 0
            ? $"[NPC] no objects matching '{NameFilter}' in scene."
            : $"[NPC] objects matching '{NameFilter}': {count}");
    }

    /// <summary>Object header, then its FSMs, animators and interaction components.</summary>
    private static void LogObject(Transform transform, string path)
    {
        GameObject go = transform.gameObject;
        Plugin.Log.LogInfo(
            $"[NPC] '{path}' activeSelf={go.activeSelf} inHierarchy={go.activeInHierarchy}" +
            $" layer={go.layer} pos={Vec(transform.position)} comps=[{ComponentNames(go)}]");

        foreach (PlayMakerFSM fsm in go.GetComponents<PlayMakerFSM>())
        {
            LogFsm(fsm);
        }

        foreach (tk2dSpriteAnimator animator in go.GetComponents<tk2dSpriteAnimator>())
        {
            LogAnimator(animator);
        }

        LogComponents(go);
    }

    /// <summary>One FSM: name, active state, global and per-state transitions, event summary.</summary>
    private static void LogFsm(PlayMakerFSM fsm)
    {
        if (fsm == null)
        {
            return;
        }

        Fsm? data = fsm.Fsm;
        Plugin.Log.LogInfo(
            $"[NPC-FSM] fsmName='{SafeName(fsm)}' activeState='{fsm.ActiveStateName}'" +
            $" start='{(data != null ? data.StartState : "<no Fsm data>")}'");
        if (data == null)
        {
            return;
        }

        HashSet<string> events = new HashSet<string>();
        LogTransitions("GLOBAL", data.GlobalTransitions, events);

        FsmState[]? states = fsm.FsmStates;
        if (states == null)
        {
            return;
        }

        foreach (FsmState state in states)
        {
            if (state != null)
            {
                LogTransitions(state.Name, state.Transitions, events);
            }
        }

        Plugin.Log.LogInfo($"[NPC-FSM] events ({events.Count}): " + string.Join(", ", events.OrderBy(name => name)));
    }

    /// <summary>Logs transitions as <c>state --event--> to</c> and collects the event names.</summary>
    private static void LogTransitions(string from, FsmTransition[]? transitions, HashSet<string> events)
    {
        if (transitions == null)
        {
            return;
        }

        foreach (FsmTransition transition in transitions)
        {
            if (transition == null)
            {
                continue;
            }

            string eventName = transition.EventName ?? "<none>";
            events.Add(eventName);
            Plugin.Log.LogInfo($"[NPC-FSM]   {from} --{eventName}--> {transition.ToState ?? "<none>"}");
        }
    }

    /// <summary>Current clip and the clip names of the animator library.</summary>
    private static void LogAnimator(tk2dSpriteAnimator animator)
    {
        if (animator == null)
        {
            return;
        }

        tk2dSpriteAnimationClip? current = animator.CurrentClip;
        Plugin.Log.LogInfo(
            $"[NPC-ANIM] '{Path(animator.transform)}' current='{(current != null ? current.name : "<none>")}'" +
            $" playing={animator.Playing}");

        tk2dSpriteAnimation? library = animator.Library;
        if (library == null || library.clips == null)
        {
            Plugin.Log.LogInfo("[NPC-ANIM] library unavailable.");
            return;
        }

        string[] names = library.clips
            .Where(clip => clip != null)
            .Select(clip => clip.name)
            .ToArray();
        Plugin.Log.LogInfo($"[NPC-ANIM] clips ({names.Length}): " + string.Join(", ", names));
    }

    /// <summary>Colliders, trigger detectors and the interaction components of one object.</summary>
    private static void LogComponents(GameObject go)
    {
        foreach (Collider2D collider in go.GetComponents<Collider2D>())
        {
            if (collider != null)
            {
                Plugin.Log.LogInfo(
                    $"[NPC-TRIGGER] Collider2D type={collider.GetType().Name} isTrigger={collider.isTrigger}" +
                    $" enabled={collider.enabled}");
            }
        }

        foreach (TriggerEnterEvent trigger in go.GetComponents<TriggerEnterEvent>())
        {
            if (trigger == null)
            {
                continue;
            }

            Plugin.Log.LogInfo(
                $"[NPC-TRIGGER] TriggerEnterEvent checkLayer={Describe(PrivateField(trigger, "checkLayer"))}" +
                $" checkTags=[{Describe(PrivateField(trigger, "checkTags"))}]" +
                $" fsmEvent='{Describe(PrivateField(trigger, "fsmEvent"))}'" +
                $" fsmTarget='{Describe(PrivateField(trigger, "fsmTarget"))}'");
        }

        foreach (InteractableBase interactable in go.GetComponents<InteractableBase>())
        {
            if (interactable != null)
            {
                Plugin.Log.LogInfo($"[NPC-TRIGGER] InteractableBase type={interactable.GetType().Name}");
            }
        }

        foreach (NPCControlBase npc in go.GetComponents<NPCControlBase>())
        {
            if (npc == null)
            {
                continue;
            }

            string running = npc is PlayMakerNPC playMakerNpc
                ? $" runningDialogue={playMakerNpc.IsRunningDialogue}"
                : string.Empty;
            Plugin.Log.LogInfo($"[NPC-TRIGGER] NPCControlBase type={npc.GetType().Name}{running}");
        }
    }

    /// <summary>Intro flags of the maid plus the save slot.</summary>
    private static void LogPlayerData()
    {
        PlayerData? data = PlayerData.instance;
        if (data == null)
        {
            Plugin.Log.LogInfo("[NPC-PD] no PlayerData.instance.");
            return;
        }

        Plugin.Log.LogInfo(
            $"[NPC-PD] churchKeeperIntro={data.churchKeeperIntro}" +
            $" churchKeeperCursedConvo={data.churchKeeperCursedConvo}" +
            $" churchKeeperBonegraveConvo={data.churchKeeperBonegraveConvo}" +
            $" ChurchKeeperLeftBasement={data.ChurchKeeperLeftBasement}" +
            $" profileID={data.profileID}");
    }

    /// <summary>Public DialogueBox API in one line, to see how a dialogue can be started.</summary>
    private static void LogDialogueApi()
    {
        try
        {
            string methods = string.Join(", ", typeof(DialogueBox)
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(method => method.Name)
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal));
            Plugin.Log.LogInfo($"[NPC-PD] DialogueBox public methods: {methods}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[NPC-PD] DialogueBox reflection failed: {ex.Message}");
        }
    }

    private static object? PrivateField(object instance, string name)
    {
        FieldInfo? field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return field != null ? field.GetValue(instance) : null;
    }

    /// <summary>Short readable form of a reflected value.</summary>
    private static string Describe(object? value)
    {
        switch (value)
        {
            case null:
                return "<none>";
            case string[] tags:
                return string.Join("|", tags);
            case UnityEngine.Object unityObject:
                return unityObject == null ? "<none>" : unityObject.name;
            default:
                return value.ToString() ?? "<none>";
        }
    }

    private static string ComponentNames(GameObject go)
    {
        return string.Join(",", go.GetComponents<Component>()
            .Where(component => component != null)
            .Select(component => component.GetType().Name));
    }

    private static string SafeName(PlayMakerFSM fsm)
    {
        try
        {
            return fsm.FsmName ?? string.Empty;
        }
        catch (Exception)
        {
            return "<unavailable>";
        }
    }

    private static string Path(Transform transform)
    {
        StringBuilder builder = new StringBuilder(transform.name);
        Transform? current = transform.parent;
        while (current != null)
        {
            builder.Insert(0, current.name + "/");
            current = current.parent;
        }

        return builder.ToString();
    }

    private static string Vec(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }
}
