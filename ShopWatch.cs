using System;
using System.Text;
using CrestPicker.Pure;
using HutongGames.PlayMaker;
using TeamCherry.NestedFadeGroup;
using TMProOld;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Watches the shared shop window for the whole session: samples it twice a second and
/// logs a line only when one of the watched fields changes. Pins the fade alpha the vanilla
/// shop leaves at zero and writes back the panel snapshot the last close took: the close
/// timer drops the root alpha and the open chain raises only <c>Window</c>, never the root
/// or the nested panels. Never throws.
/// </summary>
internal sealed class ShopWatch : MonoBehaviour
{
    /// <summary>Seconds between two samples.</summary>
    private const float PollInterval = 0.5f;

    /// <summary>
    /// Seconds the window must stay in an open state before the panel snapshot is written
    /// back: the vanilla open chain raises the outer groups itself, and its writes must land
    /// first or they overwrite the snapshot values we just put back.
    /// </summary>
    private const float RestoreDelay = 0.5f;

    /// <summary>Name of the spawned shop window in the scene.</summary>
    private const string WindowName = "Shop Menu(Clone)";

    /// <summary>Name of the FSM that drives the window.</summary>
    private const string ControlFsm = "shop_control";

    /// <summary>Cached window; re-found whenever it is gone.</summary>
    private GameObject? _window;

    /// <summary>Instance id of the last window seen, to notice a recreated one.</summary>
    private int _windowId;

    /// <summary>Last logged field line; empty right after a window appears.</summary>
    private string _last = string.Empty;

    /// <summary>Last logged presence, null before the first sample.</summary>
    private bool? _present;

    /// <summary>Unscaled time of the next sample.</summary>
    private float _nextPoll;

    /// <summary>True once the root alpha was pinned in this window session.</summary>
    private bool _rootPinned;

    /// <summary>True once the panel snapshot was written back for the current open.</summary>
    private bool _panelsRestored;

    /// <summary>Unscaled time the state entered an open state; valid only while open.</summary>
    private float _openSince;

    /// <summary>True while the last sample saw an open state.</summary>
    private bool _sawOpenState;

    private void Update()
    {
        if (Time.unscaledTime < _nextPoll)
        {
            return;
        }

        _nextPoll = Time.unscaledTime + PollInterval;
        try
        {
            Sample();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[WATCH] sample failed: {ex.Message}");
        }
    }

    /// <summary>One sample: presence line on change, field line on any changed field.</summary>
    private void Sample()
    {
        if (_window == null)
        {
            GameObject? found = GameObject.Find(WindowName);
            if (found == null)
            {
                Note(false, "[WATCH] window absent");
                return;
            }

            _window = found;
            int id = found.GetInstanceID();
            if (_windowId != 0 && _windowId != id)
            {
                Plugin.Log.LogInfo("[WATCH] window recreated");
            }

            _windowId = id;
            _last = string.Empty;
            _rootPinned = false;
            _panelsRestored = false;
            _openSince = 0f;
            _sawOpenState = false;
            Note(true, "[WATCH] window found");
        }

        Apply(_window.transform);
        string line = Describe(_window.transform);
        if (line == _last)
        {
            return;
        }

        _last = line;
        Plugin.Log.LogInfo(line);
    }

    /// <summary>
    /// Writes back the alphas the vanilla shop leaves at zero: the root fade group is
    /// pinned while the window is open, and the panels hidden by the last close are
    /// restored once per open. Both key off the <c>shop_control</c> state, not
    /// <c>winAlpha</c>: the fade alpha of a closed window stays at one, so it cannot tell
    /// open from closed, and while the window fades out the watcher writes nothing, so the
    /// vanilla close timer can drive the fade to zero. The restore waits
    /// <see cref="RestoreDelay"/> so the vanilla open chain finishes its own writes first;
    /// raising the panels to one here would put the confirmation group the open chain just
    /// zeroed back on screen before anything was bought.
    /// </summary>
    private void Apply(Transform root)
    {
        string state = State(root);
        float alpha = Alpha(root);
        if (WindowPinPolicy.ShouldPinRoot(state) && alpha < 1f)
        {
            NestedFadeGroupBase? group = FadeGroup(root);
            if (group != null)
            {
                group.AlphaSelf = 1f;
                if (!_rootPinned)
                {
                    _rootPinned = true;
                    Plugin.Log.LogInfo($"[WATCH] root pinned to 1 state='{state}' was={alpha:F2}");
                }
            }
        }

        if (!WindowPinPolicy.IsOpenState(state))
        {
            _sawOpenState = false;
            _panelsRestored = false;
            return;
        }

        if (!_sawOpenState)
        {
            _sawOpenState = true;
            _openSince = Time.unscaledTime;
            return;
        }

        if (_panelsRestored || Time.unscaledTime - _openSince < RestoreDelay)
        {
            return;
        }

        _panelsRestored = true;
        ShopCrestMenu.RestorePanelSnapshot();
        Plugin.Log.LogInfo($"[WATCH] panel snapshot restore state='{state}'");
    }

    /// <summary>Logs a presence line only when the presence changed.</summary>
    private void Note(bool present, string text)
    {
        if (_present == present)
        {
            return;
        }

        _present = present;
        Plugin.Log.LogInfo(text);
    }

    /// <summary>All watched fields of the window as one log line.</summary>
    private static string Describe(Transform root)
    {
        Transform? window = root.Find("Window");
        Transform? listGroup = root.Find("Item List Group");
        Transform? details = FindChild(root, "Item Details");
        Transform? confirm = FindChild(root, "Item Confirm Group");
        Transform? cancel = FindChild(root, "Action Cancel");
        Transform? confirmAction = FindChild(root, "Action Confirm");
        StringBuilder line = new StringBuilder("[WATCH] ");
        line.Append($"alpha={Alpha(root):F2}");
        line.Append($" winAlpha={Alpha(window):F2}");
        line.Append($" owner='{OwnerName()}'");
        line.Append($" state='{State(root)}'");
        line.Append($" listAlpha={Alpha(listGroup):F2}");
        line.Append($" listActive={listGroup != null && listGroup.gameObject.activeSelf}");
        line.Append($" detailsAlpha={Alpha(details):F2}");
        line.Append($" confirmAlpha={Alpha(confirm):F2}");
        line.Append($" cancelAlpha={Alpha(cancel):F2}");
        line.Append($" confirmText='{ConfirmText(confirmAction)}'");
        line.Append($" rootPos={Vec(root.position)}");
        line.Append($" ourOpen={ShopCrestMenu.OurOpen}");
        line.Append($" ourClosing={ShopCrestMenu.OurClosing}");
        return line.ToString();
    }

    /// <summary>Active state of the window FSM, or a note when it has none.</summary>
    private static string State(Transform root)
    {
        foreach (PlayMakerFSM fsm in root.GetComponents<PlayMakerFSM>())
        {
            if (fsm != null && fsm.FsmName == ControlFsm)
            {
                return fsm.ActiveStateName ?? string.Empty;
            }
        }

        return "<no shop_control>";
    }

    /// <summary>Fade alpha of an object, <c>-1</c> when it has no fade group.</summary>
    private static float Alpha(Transform? node)
    {
        NestedFadeGroupBase? group = FadeGroup(node);
        return group != null ? group.AlphaSelf : -1f;
    }

    /// <summary>Fade group of an object, looking at children when the object has none.</summary>
    private static NestedFadeGroupBase? FadeGroup(Transform? node)
    {
        if (node == null)
        {
            return null;
        }

        NestedFadeGroupBase? group = node.GetComponent<NestedFadeGroupBase>();
        return group != null ? group : node.GetComponentInChildren<NestedFadeGroupBase>(true);
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

    /// <summary>
    /// First text of a node, cut to the first 24 characters with line breaks escaped,
    /// <c>none</c> when the node or its text is absent.
    /// </summary>
    private static string ConfirmText(Transform? node)
    {
        TMP_Text? text = node != null ? node.GetComponentInChildren<TMP_Text>(true) : null;
        if (text == null)
        {
            return "none";
        }

        string value = text.text ?? string.Empty;
        if (value.Length > 24)
        {
            value = value.Substring(0, 24);
        }

        return value.Replace("\r", string.Empty).Replace("\n", "\\n");
    }

    /// <summary>Name of the live shop owner, <c>none</c> when nothing owns the window.</summary>
    private static string OwnerName()
    {
        foreach (ShopOwnerBase owner in Resources.FindObjectsOfTypeAll<ShopOwnerBase>())
        {
            if (owner != null && owner.gameObject.scene.IsValid() && owner.gameObject.activeInHierarchy)
            {
                return owner.name;
            }
        }

        return "none";
    }

    private static string Vec(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }
}
