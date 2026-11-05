using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrestPicker;

/// <summary>
/// Diagnostic mode: dumps dialogue-ish FSMs of early scenes and the full
/// collectable/tool name lists into the BepInEx log. Read-only, no game logic.
/// </summary>
internal static class Probe
{
    /// <summary>Set from the BepInEx config in <see cref="Plugin.Awake"/>.</summary>
    internal static bool Enabled;

    /// <summary>
    /// Early scenes worth logging. Extend the list when probing further areas;
    /// the whole scene FSM scan is limited to these to keep the log small.
    /// </summary>
    private static readonly string[] LoggedScenes =
    {
        "Tut_01", "Tut_01b", "Tut_02", "Tut_03", "Tut_04", "Tut_05",
        "Bonetown", "Mosstown_01", "Mosstown_02",
    };

    private static readonly Regex DialogueStatePattern =
        new Regex("Dlg|Dialog|Convo|Talk|Speak", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Scene where full FSM transition graphs are dumped.</summary>
    private const string EdgesScene = "Bonetown";

    /// <summary>FSM names worth dumping transitions for; others are skipped to keep the log small.</summary>
    private static readonly Regex EdgesFsmPattern =
        new Regex("^(Conversation|Dialogue|Control)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool _itemsLogged;
    private static bool _toolsLogged;

    /// <summary>Scene-load entry point for all probe output.</summary>
    internal static void OnSceneLoaded(Scene scene)
    {
        if (!Enabled)
        {
            return;
        }

        if (IsLoggedScene(scene.name))
        {
            LogSceneFsms(scene);
        }

        if (scene.name == EdgesScene)
        {
            LogSceneEdges(scene);
        }

        if (!_itemsLogged)
        {
            _itemsLogged = LogItems();
        }

        if (!_toolsLogged)
        {
            _toolsLogged = LogTools();
        }
    }

    private static bool IsLoggedScene(string sceneName)
    {
        return Array.IndexOf(LoggedScenes, sceneName) >= 0;
    }

    private static void LogSceneFsms(Scene scene)
    {
        try
        {
            foreach (PlayMakerFSM fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
            {
                if (fsm == null || fsm.gameObject == null || fsm.gameObject.scene != scene)
                {
                    continue;
                }

                List<string> states = GetDialogueStates(fsm);
                if (states.Count == 0)
                {
                    continue;
                }

                Plugin.Log.LogInfo(
                    $"[PROBE-FSM] {scene.name} | {fsm.gameObject.name} | path={BuildPath(fsm.transform)} | " +
                    $"fsm={fsm.FsmName} | states={string.Join(",", states)}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PROBE-FSM] failed in scene {scene.name}: {ex}");
        }
    }

    private static List<string> GetDialogueStates(PlayMakerFSM fsm)
    {
        List<string> matches = new List<string>();
        FsmState[] states = fsm.FsmStates;
        if (states == null)
        {
            return matches;
        }

        foreach (FsmState state in states)
        {
            if (state != null && state.Name != null && DialogueStatePattern.IsMatch(state.Name))
            {
                matches.Add(state.Name);
            }
        }

        return matches;
    }

    private static string BuildPath(Transform transform)
    {
        StringBuilder builder = new StringBuilder(transform.name);
        Transform current = transform.parent;
        while (current != null)
        {
            builder.Insert(0, current.name + "/");
            current = current.parent;
        }

        return builder.ToString();
    }

    /// <summary>Dumps FSM transition graphs for dialogue FSMs of <see cref="EdgesScene"/>.</summary>
    private static void LogSceneEdges(Scene scene)
    {
        try
        {
            foreach (PlayMakerFSM fsm in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
            {
                if (fsm == null || fsm.gameObject == null || fsm.gameObject.scene != scene)
                {
                    continue;
                }

                if (fsm.FsmName == null || !EdgesFsmPattern.IsMatch(fsm.FsmName))
                {
                    continue;
                }

                LogFsmEdges(scene.name, fsm);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PROBE-EDGES] failed in scene {scene.name}: {ex}");
        }
    }

    private static void LogFsmEdges(string sceneName, PlayMakerFSM fsm)
    {
        string prefix = $"[PROBE-EDGES] {sceneName} | {fsm.gameObject.name} | fsm={fsm.FsmName} | ";
        LogTransitions(prefix + "GLOBAL", fsm.Fsm == null ? null : fsm.Fsm.GlobalTransitions);

        FsmState[]? states = fsm.FsmStates;
        if (states == null)
        {
            return;
        }

        foreach (FsmState state in states)
        {
            if (state == null || state.Transitions == null)
            {
                continue;
            }

            LogTransitions(prefix + state.Name, state.Transitions);
        }
    }

    private static void LogTransitions(string label, FsmTransition[]? transitions)
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
            string target = transition.ToState ?? "<none>";
            Plugin.Log.LogInfo($"{label} -> event={eventName} -> {target}");
        }
    }

    /// <summary>Dumps every collectable item once per session.</summary>
    /// <returns>True when the manager is available and the list was logged.</returns>
    private static bool LogItems()
    {
        try
        {
            CollectableItemManager manager = ManagerSingleton<CollectableItemManager>.Instance;
            if (manager == null)
            {
                return false;
            }

            foreach (CollectableItem item in manager.GetAllCollectables())
            {
                if (item == null)
                {
                    continue;
                }

                string display;
                try
                {
                    display = item.GetDisplayName(CollectableItem.ReadSource.Inventory);
                }
                catch (Exception ex)
                {
                    display = $"<error: {ex.GetType().Name}>";
                }

                Plugin.Log.LogInfo($"[PROBE-ITEMS] name={item.name} display={display}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PROBE-ITEMS] failed: {ex}");
            return false;
        }
    }

    /// <summary>Dumps every tool item once per session.</summary>
    /// <returns>True when the manager is available and the list was logged.</returns>
    private static bool LogTools()
    {
        try
        {
            ToolItemManager manager = ManagerSingleton<ToolItemManager>.Instance;
            if (manager == null)
            {
                return false;
            }

            foreach (ToolItem tool in ToolItemManager.GetAllTools())
            {
                if (tool == null)
                {
                    continue;
                }

                string display;
                try
                {
                    display = tool.GetPopupName();
                }
                catch (Exception ex)
                {
                    display = $"<error: {ex.GetType().Name}>";
                }

                Plugin.Log.LogInfo($"[PROBE-TOOLS] name={tool.name} display={display}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[PROBE-TOOLS] failed: {ex}");
            return false;
        }
    }
}

/// <summary>Fires the probe after every scene load.</summary>
[HarmonyPatch(typeof(SceneManager), "Internal_SceneLoaded")]
internal static class ProbeScenePatch
{
    private static void Postfix(Scene __0)
    {
        Probe.OnSceneLoaded(__0);
    }
}
