using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using TeamCherry.NestedFadeGroup;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Per-frame diagnostic of every <c>shop_control</c> FSM in the loaded scene (ours and
/// the vanilla ones), plus the clone's confirm group and schema container. Writes one
/// line per change and touches nothing in the game objects. Read-only, never throws.
/// </summary>
internal static class ShopFsmDiag
{
    /// <summary>Name of the FSM that drives the shop window.</summary>
    private const string ControlFsm = "shop_control";

    /// <summary>Group that fades the purchase confirmation in.</summary>
    private const string ConfirmGroupName = "Item Confirm Group";

    /// <summary>Item picture inside the confirmation group.</summary>
    private const string ItemSpriteName = "Item Sprite";

    /// <summary>States that mean the window just opened.</summary>
    private static readonly string[] OpenStates = { "Open Window", "Open" };

    /// <summary>Frames between full rescans; new windows may spawn at any time.</summary>
    private const int ScanInterval = 60;

    /// <summary>Known <c>shop_control</c> FSMs in the loaded scene.</summary>
    private static readonly List<PlayMakerFSM> _fsms = new();

    /// <summary>Last state per FSM instance id.</summary>
    private static readonly Dictionary<int, string> _lastStates = new();

    /// <summary>Frame of the last full rescan.</summary>
    private static int _scanFrame = -ScanInterval;

    /// <summary>Clone instance id the current FSM list was scanned for.</summary>
    private static int _scanWindowId;

    /// <summary>Instance id of the clone the last lines came from; 0 before the first.</summary>
    private static int _windowId;

    /// <summary>Last logged confirm group fields; null before the first sample of a clone.</summary>
    private static string? _lastConfirm;

    /// <summary>Last logged schema fields; null before the first sample of a clone.</summary>
    private static string? _lastSchema;

    /// <summary>Samples the shop FSMs and the clone; call every frame.</summary>
    internal static void Poll()
    {
        try
        {
            NoteTransitions();

            ShopMenuStock? window = OwnShopWindow.Current;
            if (window == null)
            {
                _windowId = 0;
                _lastConfirm = null;
                _lastSchema = null;
                return;
            }

            Transform root = window.transform;
            int id = root.GetInstanceID();
            if (id != _windowId)
            {
                _windowId = id;
                _lastConfirm = null;
                _lastSchema = null;
            }

            NoteConfirm(root);
            NoteSchema();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFSM] diag failed: {ex.Message}");
        }
    }

    /// <summary>Rescans the scene, then writes a line for every FSM whose state changed.</summary>
    private static void NoteTransitions()
    {
        int windowId = OwnShopWindow.Current != null ? OwnShopWindow.Current.GetInstanceID() : 0;
        if (_fsms.Count == 0 || windowId != _scanWindowId ||
            Time.frameCount - _scanFrame >= ScanInterval)
        {
            Rescan(windowId);
        }

        foreach (PlayMakerFSM fsm in _fsms)
        {
            if (fsm == null)
            {
                continue;
            }

            int id = fsm.GetInstanceID();
            string state = fsm.ActiveStateName ?? string.Empty;
            if (_lastStates.TryGetValue(id, out string? was) && was == state)
            {
                continue;
            }

            _lastStates[id] = state;
            bool ours = IsOurs(fsm);
            Plugin.Log.LogInfo($"[SHOPFSM] fsm path='{OwnShopWindow.PathOf(fsm.transform)}'" +
                $" state '{was ?? "<none>"}' -> '{state}' ours={(ours ? "true" : "false")}");

            if (Array.IndexOf(OpenStates, state) >= 0)
            {
                Plugin.Log.LogInfo($"[SHOPFSM] OPENED path='{OwnShopWindow.PathOf(fsm.transform)}'" +
                    $" event={EventName(fsm)} ourOpen={ShopCrestMenu.OurOpen}" +
                    $" ourClosing={ShopCrestMenu.OurClosing}");
            }
        }
    }

    /// <summary>Rebuilds the FSM list from the loaded scene; assets are ignored.</summary>
    private static void Rescan(int windowId)
    {
        _scanFrame = Time.frameCount;
        _scanWindowId = windowId;
        _fsms.Clear();
        foreach (PlayMakerFSM fsm in UnityEngine.Object.FindObjectsByType<PlayMakerFSM>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (fsm != null && fsm.FsmName == ControlFsm && fsm.gameObject.scene.IsValid())
            {
                _fsms.Add(fsm);
            }
        }
    }

    /// <summary>True when the FSM belongs to our clone.</summary>
    private static bool IsOurs(PlayMakerFSM fsm)
    {
        ShopMenuStock? window = OwnShopWindow.Current;
        return window != null && fsm.transform.IsChildOf(window.transform);
    }

    /// <summary>Transition event that led into the current state, quoted, or <c>&lt;none&gt;</c>.</summary>
    private static string EventName(PlayMakerFSM fsm)
    {
        try
        {
            FsmTransition? transition = fsm.Fsm?.LastTransition;
            return transition != null ? $"'{transition.EventName}'" : "<none>";
        }
        catch (Exception)
        {
            return "<none>";
        }
    }

    /// <summary>Writes the confirm group line when its alpha or item sprite changed.</summary>
    private static void NoteConfirm(Transform root)
    {
        Transform? group = FindChild(root, ConfirmGroupName);
        Transform? sprite = group != null ? FindChild(group, ItemSpriteName) : null;
        SpriteRenderer? renderer = sprite != null ? sprite.GetComponent<SpriteRenderer>() : null;
        string spriteName = renderer != null && renderer.sprite != null
            ? renderer.sprite.name
            : sprite != null ? sprite.name : "<none>";
        string line = $"alpha={Alpha(group):F2}" +
            $" spriteActive={sprite != null && sprite.gameObject.activeSelf}" +
            $" spriteName='{spriteName}'";
        if (line == _lastConfirm)
        {
            return;
        }

        _lastConfirm = line;
        Plugin.Log.LogInfo($"[SHOPFSM] confirm group {line}");
    }

    /// <summary>Writes the schema line when our container became active or changed alpha.</summary>
    private static void NoteSchema()
    {
        GameObject? container = ShopCrestSchema.DetailsContainer;
        string line = $"active={container != null && container.activeSelf}" +
            $" alpha={Alpha(container != null ? container.transform : null):F2}";
        if (line == _lastSchema)
        {
            return;
        }

        _lastSchema = line;
        Plugin.Log.LogInfo($"[SHOPFSM] schema {line}");
    }

    /// <summary>
    /// Fade alpha of an object: its fade group, the first one below it, or the first
    /// sprite alpha; <c>-1</c> when neither is present.
    /// </summary>
    private static float Alpha(Transform? node)
    {
        if (node == null)
        {
            return -1f;
        }

        NestedFadeGroupBase? group = node.GetComponent<NestedFadeGroupBase>()
            ?? node.GetComponentInChildren<NestedFadeGroupBase>(true);
        if (group != null)
        {
            return group.AlphaSelf;
        }

        SpriteRenderer? sprite = node.GetComponentInChildren<SpriteRenderer>(true);
        return sprite != null ? sprite.color.a : -1f;
    }

    /// <summary>First child with this name anywhere under the root, inactive ones included.</summary>
    private static Transform? FindChild(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == name)
            {
                return child;
            }
        }

        return null;
    }
}
