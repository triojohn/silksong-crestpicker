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
/// logs a line only when one of the watched fields changes. This is what shows what
/// happens to the window between our picker sessions and when the vanilla shopkeeper
/// opens it. Also glides the root fade alpha in the settled states only: toward zero
/// while idle, toward full while open.
/// Never throws.
/// </summary>
internal sealed class ShopWatch : MonoBehaviour
{
    /// <summary>Seconds between two samples.</summary>
    private const float PollInterval = 0.5f;

    /// <summary>Seconds between two root glide steps while the alpha is still moving.</summary>
    private const float GlideInterval = 0.02f;

    /// <summary>Seconds a full <c>0..1</c> root glide takes.</summary>
    private const float GlideDuration = 0.2f;

    /// <summary>Smallest alpha change worth writing.</summary>
    private const float GlideEpsilon = 0.001f;

    /// <summary>Distance to the target below which the glide stops.</summary>
    private const float GlideSettle = 0.005f;

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

    /// <summary>Unscaled time of the next glide step.</summary>
    private float _nextGlide;

    /// <summary>True while the root alpha is still moving toward its target.</summary>
    private bool _gliding;

    /// <summary>Direction of the last glide step, <c>0</c> when the target is reached.</summary>
    private int _glideDir;

    private void Update()
    {
        float now = Time.unscaledTime;

        if (_gliding && _window != null && now >= _nextGlide)
        {
            _nextGlide = now + GlideInterval;
            try
            {
                StepRoot(_window.transform, GlideInterval);
                return;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[WATCH] glide failed: {ex.Message}");
            }
        }

        if (now < _nextPoll)
        {
            return;
        }

        _nextPoll = now + PollInterval;
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
                StopGlide();
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

        StepRoot(_window.transform, 0f);
        string line = Describe(_window.transform);
        if (line == _last)
        {
            return;
        }

        _last = line;
        Plugin.Log.LogInfo(line);
    }

    /// <summary>
    /// Moves the root fade alpha one step toward the settled target: <c>0</c> when idle,
    /// <c>1</c> when open. Transitional states and a missing window are left to vanilla's
    /// own fades. With a non-positive <paramref name="deltaTime"/> nothing is written: the
    /// sample only checks whether a glide is needed and arms the fast tick. The child
    /// panels are left alone.
    /// </summary>
    private void StepRoot(Transform root, float deltaTime)
    {
        string state = State(root);
        float? target = WindowPinPolicy.RootTarget(state);
        NestedFadeGroupBase? group = target != null ? FadeGroup(root) : null;
        if (target == null || group == null)
        {
            StopGlide();
            return;
        }

        float alpha = group.AlphaSelf;
        float diff = target.Value - alpha;
        if (Mathf.Abs(diff) <= GlideSettle)
        {
            if (Mathf.Abs(diff) > GlideEpsilon)
            {
                group.AlphaSelf = target.Value;
                Plugin.Log.LogInfo($"[WATCH] root glide -> {target.Value:F2} now={target.Value:F2} state='{state}'");
            }

            StopGlide();
            return;
        }

        if (deltaTime <= 0f)
        {
            _gliding = true;
            _nextGlide = 0f;
            return;
        }

        float next = WindowPinPolicy.StepTowards(alpha, target.Value, deltaTime, GlideDuration);
        if (Mathf.Abs(next - alpha) <= GlideEpsilon)
        {
            return;
        }

        bool reached = Mathf.Abs(next - target.Value) <= GlideEpsilon;
        int dir = Math.Sign(diff);
        group.AlphaSelf = next;
        if (reached || dir != _glideDir)
        {
            Plugin.Log.LogInfo($"[WATCH] root glide -> {target.Value:F2} now={next:F2} state='{state}'");
        }

        _glideDir = reached ? 0 : dir;
        _gliding = !reached;
    }

    /// <summary>Stops the fast glide tick until a sample asks for it again.</summary>
    private void StopGlide()
    {
        _gliding = false;
        _glideDir = 0;
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
