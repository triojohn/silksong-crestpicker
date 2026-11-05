using System;
using System.Collections.Generic;
using System.Linq;
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

    private static bool _logged;

    /// <summary>Logs shop object counts once per session for the target scene.</summary>
    /// <param name="scene">Scene that just loaded.</param>
    internal static void OnSceneLoaded(Scene scene)
    {
        if (!Plugin.UseShopMenu || _logged || scene.name != TargetScene)
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
