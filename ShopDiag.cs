using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrestPicker;

/// <summary>
/// One-shot diagnostic of the shop objects present in Bonetown, plus an on-demand
/// F8 dump of the live shop window: object tree, PlayMaker FSMs with transitions
/// and the item rows. Read-only, never throws.
/// </summary>
internal static class ShopDiag
{
    /// <summary>Scene the counts are taken from.</summary>
    private const string TargetScene = "Bonetown";

    /// <summary>How deep the object tree dump goes below the shop root.</summary>
    private const int MaxDepth = 4;

    /// <summary>How deep the shopkeeper object tree dump goes.</summary>
    private const int NpcDepth = 3;

    /// <summary>Upper bound on FSMs dumped for one shopkeeper, so the log stays readable.</summary>
    private const int MaxNpcFsms = 12;

    /// <summary>Longest field value printed per action field.</summary>
    private const int ValueLimit = 48;

    /// <summary>Action-name keywords that make a line worth reading first.</summary>
    private static readonly string[] InterestingKeywords =
    {
        "Send", "Event", "Shop", "Camera", "Hud", "Fade", "Relinquish", "Control", "Position", "Tween",
    };

    private static bool _logged;

    /// <summary>Logs shop object counts once per session for the target scene.</summary>
    /// <param name="scene">Scene that just loaded.</param>
    internal static void OnSceneLoaded(Scene scene)
    {
        if (!Plugin.UseLiveShop || _logged || scene.name != TargetScene)
        {
            return;
        }

        _logged = true;
        try
        {
            Plugin.Log.LogInfo(
                $"[SHOP] scene={scene.name} counts: ShopOwner={Count<ShopOwner>()}, " +
                $"ShopOwnerBase={Count<ShopOwnerBase>()}, ShopMenuStock={Count<ShopMenuStock>()}, " +
                $"SimpleShopMenu={Count<SimpleShopMenu>()}, " +
                $"SimpleShopMenuOwner={Count<SimpleShopMenuOwner>()}, ShopItem={Count<ShopItem>()}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] scene scan failed: {ex}");
        }
    }

    /// <summary>
    /// Dumps the live shop window on demand: owner, shop object tree, every
    /// PlayMaker FSM on it with states/transitions/events, and the item rows.
    /// </summary>
    internal static void DumpLiveShop()
    {
        try
        {
            ShopOwnerBase? owner = FindLiveOwner();
            if (owner == null)
            {
                Plugin.Log.LogInfo("[SHOPFSM] no active ShopOwnerBase in scene.");
                return;
            }

            GameObject? shopObject = owner.ShopObject;
            if (shopObject == null)
            {
                Plugin.Log.LogInfo($"[SHOPFSM] owner='{owner.name}' has no ShopObject.");
                return;
            }

            ShopMenuStock? stock = shopObject.GetComponent<ShopMenuStock>()
                ?? shopObject.GetComponentInChildren<ShopMenuStock>(true);
            Plugin.Log.LogInfo(
                $"[SHOPFSM] owner='{owner.name}' shopObject='{shopObject.name}'" +
                $" stock={(stock != null ? stock.name : "<none>")}" +
                $" stockActive={stock != null && stock.gameObject.activeSelf}");

            LogTree(shopObject.transform, 0);

            foreach (PlayMakerFSM fsm in shopObject.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                LogFsm(fsm);
            }

            LogRows(shopObject.transform);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFSM] dump failed: {ex}");
        }
    }

    /// <summary>First scene shop owner that is active; asset copies are ignored.</summary>
    private static ShopOwnerBase? FindLiveOwner()
    {
        foreach (ShopOwnerBase owner in Resources.FindObjectsOfTypeAll<ShopOwnerBase>())
        {
            if (owner != null && owner.gameObject.scene.IsValid() && owner.gameObject.activeInHierarchy)
            {
                return owner;
            }
        }

        return null;
    }

    /// <summary>Logs one line per object down to <see cref="MaxDepth"/>.</summary>
    private static void LogTree(Transform node, int depth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        GameObject go = node.gameObject;
        string components = string.Join(",", go.GetComponents<Component>()
            .Where(component => component != null)
            .Select(component => component.GetType().Name));
        string indent = new string(' ', depth * 2);
        Plugin.Log.LogInfo(
            $"[SHOPFSM] {indent}{Path(node)} activeSelf={go.activeSelf}" +
            $" inHierarchy={go.activeInHierarchy} layer={go.layer}" +
            $" pos={Vec(node.position)} comps=[{components}]");

        for (int i = 0; i < node.childCount; i++)
        {
            LogTree(node.GetChild(i), depth + 1);
        }
    }

    /// <summary>Logs one FSM: name, start state, state transitions, global transitions, events.</summary>
    private static void LogFsm(PlayMakerFSM fsm)
    {
        if (fsm == null || fsm.gameObject == null)
        {
            return;
        }

        Fsm? fsmData = fsm.Fsm;
        string header = $"[SHOPFSM] FSM '{Path(fsm.transform)}' fsmName='{SafeName(fsm)}'";
        if (fsmData == null)
        {
            Plugin.Log.LogInfo(header + " <no Fsm data>");
            return;
        }

        Plugin.Log.LogInfo(
            header + $" start='{fsmData.StartState}' active='{fsm.ActiveStateName}'" +
            $" activeFlag={fsmData.Active}");

        HashSet<string> events = new HashSet<string>();
        LogTransitions("GLOBAL", fsmData.GlobalTransitions, events);

        FsmState[]? states = fsm.FsmStates;
        if (states != null)
        {
            foreach (FsmState state in states)
            {
                if (state == null)
                {
                    continue;
                }

                LogTransitions(state.Name, state.Transitions, events);
            }
        }

        Plugin.Log.LogInfo($"[SHOPFSM] FSM '{SafeName(fsm)}' events ({events.Count}): " +
            string.Join(", ", events.OrderBy(name => name)));
    }

    /// <summary>Logs transitions as <c>from -> event -> to</c> and collects event names.</summary>
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
            string target = transition.ToState ?? "<none>";
            events.Add(eventName);
            Plugin.Log.LogInfo($"[SHOPFSM]   {from} -> {eventName} -> {target}");
        }
    }

    /// <summary>Logs every item row of the shop: active state, item name, number, cost.</summary>
    private static void LogRows(Transform shopRoot)
    {
        ShopItemStats[] rows = shopRoot.GetComponentsInChildren<ShopItemStats>(true);
        Plugin.Log.LogInfo($"[SHOPFSM] item rows: {rows.Length}");
        foreach (ShopItemStats row in rows)
        {
            try
            {
                if (row == null)
                {
                    continue;
                }

                ShopItem? item = row.Item;
                Plugin.Log.LogInfo(
                    $"[SHOPFSM]   row '{Path(row.transform)}' activeSelf={row.gameObject.activeSelf}" +
                    $" inHierarchy={row.gameObject.activeInHierarchy}" +
                    $" num={row.ItemNumber} item='{(item != null ? item.name : "<none>")}'" +
                    $" cost={row.GetCost()}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SHOPFSM] row read failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Dumps the shopkeeper object itself: object tree, every FSM on it with the
    /// actions of every state, and a summary of the event-sending actions. This is
    /// what vanilla does before and after the shop window event.
    /// </summary>
    internal static void DumpShopNpc()
    {
        try
        {
            ShopOwnerBase? owner = FindLiveOwner();
            if (owner == null)
            {
                Plugin.Log.LogInfo("[SHOPNPC] no active ShopOwnerBase in scene.");
                return;
            }

            GameObject go = owner.gameObject;
            Plugin.Log.LogInfo(
                $"[SHOPNPC] owner='{owner.name}' type={owner.GetType().Name} go='{go.name}'" +
                $" path='{Path(go.transform)}' activeSelf={go.activeSelf} inHierarchy={go.activeInHierarchy}" +
                $" pos={Vec(go.transform.position)}");
            LogNpcTree(go.transform, 0);

            PlayMakerFSM[] fsms = go.GetComponentsInChildren<PlayMakerFSM>(true);
            Plugin.Log.LogInfo($"[SHOPNPC] fsm count on owner tree: {fsms.Length}");
            for (int i = 0; i < fsms.Length && i < MaxNpcFsms; i++)
            {
                LogFsmActions(fsms[i]);
            }

            if (fsms.Length > MaxNpcFsms)
            {
                Plugin.Log.LogInfo($"[SHOPNPC] {fsms.Length - MaxNpcFsms} more FSMs not dumped.");
            }

            LogSendEventActions(fsms);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPNPC] dump failed: {ex}");
        }
    }

    /// <summary>Object tree of the shopkeeper down to <see cref="NpcDepth"/> levels.</summary>
    private static void LogNpcTree(Transform node, int depth)
    {
        if (depth > NpcDepth)
        {
            return;
        }

        GameObject go = node.gameObject;
        string components = string.Join(",", go.GetComponents<Component>()
            .Where(component => component != null)
            .Select(component => component.GetType().Name));
        Plugin.Log.LogInfo(
            $"[SHOPNPC] {new string(' ', depth * 2)}{node.name} activeSelf={go.activeSelf}" +
            $" inHierarchy={go.activeInHierarchy} layer={go.layer} pos={Vec(node.position)} comps=[{components}]");

        for (int i = 0; i < node.childCount; i++)
        {
            LogNpcTree(node.GetChild(i), depth + 1);
        }
    }

    /// <summary>Logs one FSM of the shopkeeper: states, transitions and the actions of each state.</summary>
    private static void LogFsmActions(PlayMakerFSM fsm)
    {
        if (fsm == null || fsm.gameObject == null)
        {
            return;
        }

        Fsm? data = fsm.Fsm;
        if (data == null)
        {
            Plugin.Log.LogInfo($"[SHOPNPC] FSM '{Path(fsm.transform)}' <no Fsm data>");
            return;
        }

        Plugin.Log.LogInfo(
            $"[SHOPNPC] FSM '{Path(fsm.transform)}' name='{SafeName(fsm)}' start='{data.StartState}'" +
            $" active='{fsm.ActiveStateName}' activeFlag={data.Active}");

        LogTransitions("GLOBAL", data.GlobalTransitions, new HashSet<string>());
        FsmState[]? states = fsm.FsmStates;
        if (states == null)
        {
            return;
        }

        foreach (FsmState state in states)
        {
            LogState(state);
        }
    }

    /// <summary>Logs transitions then every action of a single state.</summary>
    private static void LogState(FsmState? state)
    {
        if (state == null)
        {
            return;
        }

        LogTransitions(state.Name, state.Transitions, new HashSet<string>());
        FsmStateAction[]? actions = state.Actions;
        int count = actions?.Length ?? 0;
        Plugin.Log.LogInfo($"[SHOPNPC]   state '{state.Name}' actions={count}");
        if (actions == null)
        {
            return;
        }

        foreach (FsmStateAction action in actions)
        {
            if (action == null)
            {
                continue;
            }

            string typeName = action.GetType().Name;
            string marker = IsInteresting(typeName) ? "*" : " ";
            Plugin.Log.LogInfo($"[SHOPNPC]    {marker} {typeName} {DescribeFields(action, 3)}");
        }
    }

    /// <summary>Summary of every action that sends or registers an event, with its state.</summary>
    private static void LogSendEventActions(PlayMakerFSM[] fsms)
    {
        foreach (PlayMakerFSM fsm in fsms)
        {
            if (fsm == null)
            {
                continue;
            }

            FsmState[]? states = fsm.FsmStates;
            if (states == null)
            {
                continue;
            }

            foreach (FsmState state in states)
            {
                FsmStateAction[]? actions = state != null ? state.Actions : null;
                if (actions == null)
                {
                    continue;
                }

                foreach (FsmStateAction action in actions)
                {
                    if (action == null || action.GetType().Name.IndexOf("Send", StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    Plugin.Log.LogInfo(
                        $"[SHOPNPC] SEND fsm='{SafeName(fsm)}' state='{state!.Name}'" +
                        $" action='{action.GetType().Name}' {DescribeFields(action, 4)}");
                }
            }
        }
    }

    /// <summary>Public fields of an action as <c>name=value</c>, at most <paramref name="max"/> of them.</summary>
    private static string DescribeFields(FsmStateAction action, int max)
    {
        List<string> parts = new List<string>();
        FieldInfo[] fields = action.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (FieldInfo field in fields)
        {
            if (parts.Count >= max || SkipField(field))
            {
                continue;
            }

            string value;
            try
            {
                value = DescribeValue(field.GetValue(action));
            }
            catch (Exception)
            {
                value = "<unreadable>";
            }

            parts.Add($"{field.Name}={value}");
        }

        return parts.Count == 0 ? string.Empty : string.Join(" ", parts);
    }

    /// <summary>Fields that carry no state and would only add noise.</summary>
    private static bool SkipField(FieldInfo field)
    {
        return field.IsStatic
            || typeof(FsmStateAction).IsAssignableFrom(field.FieldType)
            || field.FieldType == typeof(Fsm) || field.FieldType == typeof(FsmState);
    }

    /// <summary>Event target as target kind and FSM name, so the receiving FSM is visible.</summary>
    private static string DescribeTarget(FsmEventTarget? target)
    {
        if (target == null)
        {
            return "<null>";
        }

        try
        {
            string? fsmName = target.fsmName != null ? target.fsmName.Value : null;
            return $"{target.target}/fsm='{fsmName}'";
        }
        catch (Exception)
        {
            return "<unreadable>";
        }
    }

    /// <summary>Short readable form of a field value, never longer than <see cref="ValueLimit"/>.</summary>
    private static string DescribeValue(object? value)
    {
        switch (value)
        {
            case null:
                return "<null>";
            case string text:
                return "\"" + Truncate(text) + "\"";
            case FsmEvent fsmEvent:
                return "\"" + (fsmEvent.Name ?? "?") + "\"";
            case FsmEventTarget target:
                return DescribeTarget(target);
            case UnityEngine.Object unityObject:
                return unityObject == null ? "<null>" : "'" + Truncate(unityObject.name) + "'";
            default:
                return Truncate(value.ToString());
        }
    }

    private static string Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text!.Length <= ValueLimit ? text : text.Substring(0, ValueLimit) + "...";
    }

    /// <summary>Action names worth a second look when reading the log.</summary>
    private static bool IsInteresting(string typeName)
    {
        foreach (string keyword in InterestingKeywords)
        {
            if (typeName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
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
        Transform current = transform.parent;
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

    /// <summary>Number of objects of a type, including inactive and asset copies.</summary>
    private static int Count<T>() where T : UnityEngine.Object
    {
        try
        {
            return Resources.FindObjectsOfTypeAll<T>().Length;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] count of {typeof(T).Name} failed: {ex.Message}");
            return -1;
        }
    }
}

/// <summary>Fires the shop diagnostics after every scene load.</summary>
[HarmonyPatch(typeof(SceneManager), "Internal_SceneLoaded")]
internal static class ShopDiagScenePatch
{
    private static void Postfix(Scene __0)
    {
        ShopDiag.OnSceneLoaded(__0);
    }
}
