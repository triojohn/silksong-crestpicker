using System;
using HutongGames.PlayMaker;
using TeamCherry.NestedFadeGroup;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Per-frame diagnostic of our own shop window clone: the <c>shop_control</c> state,
/// the confirm group fade and its item sprite, and our schema container. Writes one
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

    /// <summary>Instance id of the clone the last lines came from; 0 before the first.</summary>
    private static int _windowId;

    /// <summary>Last logged FSM state; null before the first sample of a clone.</summary>
    private static string? _lastState;

    /// <summary>Last logged confirm group fields; null before the first sample of a clone.</summary>
    private static string? _lastConfirm;

    /// <summary>Last logged schema fields; null before the first sample of a clone.</summary>
    private static string? _lastSchema;

    /// <summary>Samples the clone; call every frame.</summary>
    internal static void Poll()
    {
        ShopMenuStock? window = OwnShopWindow.Current;
        if (window == null)
        {
            _windowId = 0;
            _lastState = null;
            _lastConfirm = null;
            _lastSchema = null;
            return;
        }

        try
        {
            Transform root = window.transform;
            int id = root.GetInstanceID();
            if (id != _windowId)
            {
                _windowId = id;
                _lastState = null;
                _lastConfirm = null;
                _lastSchema = null;
            }

            NoteState(root);
            NoteConfirm(root);
            NoteSchema();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPFSM] diag failed: {ex.Message}");
        }
    }

    /// <summary>Writes the state line when the <c>shop_control</c> state changed.</summary>
    private static void NoteState(Transform root)
    {
        string state = State(root);
        if (state == _lastState)
        {
            return;
        }

        string was = _lastState ?? "<none>";
        _lastState = state;
        Plugin.Log.LogInfo($"[SHOPFSM] state '{was}' -> '{state}'" +
            $" ourOpen={ShopCrestMenu.OurOpen} closing={ShopCrestMenu.OurClosing}");
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

    /// <summary>Active state of the window FSM, or a note when it has none.</summary>
    private static string State(Transform root)
    {
        foreach (PlayMakerFSM fsm in root.GetComponentsInChildren<PlayMakerFSM>(true))
        {
            if (fsm != null && fsm.FsmName == ControlFsm)
            {
                return fsm.ActiveStateName ?? string.Empty;
            }
        }

        return "<no shop_control>";
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
