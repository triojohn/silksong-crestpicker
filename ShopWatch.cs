using System;
using System.Text;
using HutongGames.PlayMaker;
using TeamCherry.NestedFadeGroup;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Watches the shared shop window for the whole session: samples it twice a second and
/// logs a line only when one of the watched fields changes. This is what shows what
/// happens to the window between our picker sessions and when the vanilla shopkeeper
/// opens it. Read-only, never throws.
/// </summary>
internal sealed class ShopWatch : MonoBehaviour
{
    /// <summary>Seconds between two samples.</summary>
    private const float PollInterval = 0.5f;

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
            Note(true, "[WATCH] window found");
        }

        string line = Describe(_window.transform);
        if (line == _last)
        {
            return;
        }

        _last = line;
        Plugin.Log.LogInfo(line);
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
        StringBuilder line = new StringBuilder("[WATCH] ");
        line.Append($"alpha={Alpha(root):F2}");
        line.Append($" winAlpha={Alpha(window):F2}");
        line.Append($" owner='{OwnerName()}'");
        line.Append($" state='{State(root)}'");
        line.Append($" listAlpha={Alpha(listGroup):F2}");
        line.Append($" listActive={listGroup != null && listGroup.gameObject.activeSelf}");
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
