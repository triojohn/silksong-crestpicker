using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
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

    /// <summary>Group that holds the list selector and the two scroll arrows.</summary>
    private const string ArrowGroupName = "Item List Group";

    /// <summary>Selector and arrows traced under the list group.</summary>
    private static readonly string[] ArrowNames = { "selector", "Arrow U", "Arrow D" };

    /// <summary>States that mean the window just opened.</summary>
    private static readonly string[] OpenStates = { "Open Window", "Open" };

    /// <summary>Frames between full rescans; new windows may spawn at any time.</summary>
    private const int ScanInterval = 60;

    /// <summary>Known <c>shop_control</c> FSMs in the loaded scene.</summary>
    private static readonly List<PlayMakerFSM> _fsms = new();

    /// <summary>Last state per FSM instance id.</summary>
    private static readonly Dictionary<int, string> _lastStates = new();

    /// <summary>Identity keys already written for the scanned clone.</summary>
    private static readonly HashSet<string> _loggedIdentity = new();

    /// <summary>Frame of the last full rescan.</summary>
    private static int _scanFrame = -ScanInterval;

    /// <summary><c>Fsm.Id</c> as a property or field; null when the game build has neither.</summary>
    private static readonly PropertyInfo? _fsmIdProperty = typeof(Fsm).GetProperty("Id");
    private static readonly FieldInfo? _fsmIdField = typeof(Fsm).GetField("Id");

    /// <summary>Clone instance id the current FSM list was scanned for.</summary>
    private static int _scanWindowId;

    /// <summary>Instance id of the clone the last lines came from; 0 before the first.</summary>
    private static int _windowId;

    /// <summary>Last logged confirm group fields; null before the first sample of a clone.</summary>
    private static string? _lastConfirm;

    /// <summary>Last logged schema fields; null before the first sample of a clone.</summary>
    private static string? _lastSchema;

    /// <summary>Last logged state line per selector/arrow name.</summary>
    private static readonly Dictionary<string, string> _lastArrows = new();

    /// <summary>Last logged FSM state per selector/arrow name.</summary>
    private static readonly Dictionary<string, string> _lastArrowFsms = new();

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
                _lastArrows.Clear();
                _lastArrowFsms.Clear();
                return;
            }

            Transform root = window.transform;
            int id = root.GetInstanceID();
            if (id != _windowId)
            {
                _windowId = id;
                _lastConfirm = null;
                _lastSchema = null;
                _lastArrows.Clear();
                _lastArrowFsms.Clear();
            }

            NoteConfirm(root);
            NoteSchema();
            NoteArrows(root);
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
        if (windowId != _scanWindowId)
        {
            _loggedIdentity.Clear();
        }

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

        NoteIdentity();
    }

    /// <summary>
    /// Writes one identity line per FSM and state: the shared <c>Fsm</c> object shows up
    /// as the same <c>fsmHash</c> under two paths. A key already written for the scanned
    /// clone is skipped, so the line appears once per state change, not every rescan.
    /// </summary>
    private static void NoteIdentity()
    {
        foreach (PlayMakerFSM fsm in _fsms)
        {
            if (fsm == null)
            {
                continue;
            }

            string path = OwnShopWindow.PathOf(fsm.transform);
            int hash = fsm.Fsm != null ? RuntimeHelpers.GetHashCode(fsm.Fsm) : 0;
            string state = fsm.ActiveStateName ?? string.Empty;
            if (!_loggedIdentity.Add($"{path}|{hash}|{state}"))
            {
                continue;
            }

            bool ours = IsOurs(fsm);
            string template = fsm.FsmTemplate != null ? fsm.FsmTemplate.name : "-";
            Plugin.Log.LogInfo($"[SHOPFSM] identity path='{path}' ours={(ours ? "true" : "false")}" +
                $" fsmHash={hash} template='{template}' fsmId={FsmId(fsm)} state='{state}'");
        }
    }

    /// <summary><c>Fsm.Id</c> value, or <c>-</c> when the game build has no such member.</summary>
    private static string FsmId(PlayMakerFSM fsm)
    {
        try
        {
            Fsm? fsmObject = fsm.Fsm;
            object? value = fsmObject == null
                ? null
                : _fsmIdProperty != null
                    ? _fsmIdProperty.GetValue(fsmObject)
                    : _fsmIdField?.GetValue(fsmObject);
            return value?.ToString() ?? "-";
        }
        catch (Exception)
        {
            return "-";
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
    /// Writes one <c>[ARROWS]</c> line per selector/arrow when its active flags, alpha or
    /// renderer changed, and one line per FSM state change on those objects. Read-only.
    /// </summary>
    private static void NoteArrows(Transform root)
    {
        Transform? group = root.Find(ArrowGroupName) ?? FindChild(root, ArrowGroupName);
        foreach (string name in ArrowNames)
        {
            Transform? node = group != null ? group.Find(name) ?? FindChild(group, name) : null;
            NoteArrow(name, node);
        }
    }

    /// <summary>Writes the state line and the FSM line of one selector/arrow on change.</summary>
    private static void NoteArrow(string name, Transform? node)
    {
        GameObject? go = node != null ? node.gameObject : null;
        Renderer? renderer = node != null
            ? node.GetComponent<Renderer>() ?? node.GetComponentInChildren<Renderer>(true)
            : null;
        NestedFadeGroupBase? fade = node != null ? node.GetComponent<NestedFadeGroupBase>() : null;
        string rendererFlag = renderer != null
            ? renderer.enabled.ToString()
            : fade != null ? fade.enabled.ToString() : "-";
        string line = $"active={(go != null && go.activeSelf ? "true" : "false")}" +
            $" inHierarchy={(go != null && go.activeInHierarchy ? "true" : "false")}" +
            $" alpha={ArrowAlpha(node):F2} renderer={rendererFlag}";
        if (!_lastArrows.TryGetValue(name, out string? wasLine) || wasLine != line)
        {
            _lastArrows[name] = line;
            Plugin.Log.LogInfo($"[ARROWS] {name} {line}");
        }

        NoteArrowFsm(name, node);
    }

    /// <summary>Writes the FSM state of a selector/arrow when it changed.</summary>
    private static void NoteArrowFsm(string name, Transform? node)
    {
        if (node == null)
        {
            return;
        }

        PlayMakerFSM? fsm = node.GetComponent<PlayMakerFSM>()
            ?? node.GetComponentInChildren<PlayMakerFSM>(true);
        if (fsm == null)
        {
            return;
        }

        string state = fsm.ActiveStateName ?? string.Empty;
        if (_lastArrowFsms.TryGetValue(name, out string? was) && was == state)
        {
            return;
        }

        _lastArrowFsms[name] = state;
        Plugin.Log.LogInfo($"[ARROWS] {name} fsm '{was ?? "<none>"}' -> '{state}'");
    }

    /// <summary>
    /// Fade alpha of a selector/arrow: its own fade group, else the first sprite alpha
    /// below it; <c>-1</c> when neither is present.
    /// </summary>
    private static float ArrowAlpha(Transform? node)
    {
        if (node == null)
        {
            return -1f;
        }

        NestedFadeGroupBase? group = node.GetComponent<NestedFadeGroupBase>();
        if (group != null)
        {
            return group.AlphaSelf;
        }

        SpriteRenderer? sprite = node.GetComponent<SpriteRenderer>()
            ?? node.GetComponentInChildren<SpriteRenderer>(true);
        return sprite != null ? sprite.color.a : -1f;
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
