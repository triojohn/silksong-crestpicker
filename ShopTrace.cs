using System;
using System.Text;
using HutongGames.PlayMaker;
using TeamCherry.NestedFadeGroup;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Samples the live shop window ten times after the open event, so it is visible in
/// the log whether the window, the item list and the row fades actually came up.
/// Read-only, never throws.
/// </summary>
internal static class ShopTrace
{
    /// <summary>How many samples are taken.</summary>
    private const int Samples = 10;

    /// <summary>Frames between two samples; ten frames is about 0.2 s.</summary>
    private const int FrameStep = 10;

    /// <summary>Starts a trace on the live shop window, if there is one.</summary>
    /// <param name="reason">What triggered the trace, for the log line.</param>
    internal static void Begin(string reason)
    {
        try
        {
            GameObject? root = LiveShopRoot();
            if (root == null)
            {
                Plugin.Log.LogInfo($"[SHOPTRACE] skipped ({reason}): no live shop window.");
                return;
            }

            ShopTraceRunner? runner = root.GetComponent<ShopTraceRunner>();
            if (runner == null)
            {
                root.AddComponent<ShopTraceRunner>();
            }
            else
            {
                runner.Restart();
            }

            Plugin.Log.LogInfo(
                $"[SHOPTRACE] start ({reason}) root='{root.name}'" +
                $" activeSelf={root.activeSelf} inHierarchy={root.activeInHierarchy}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPTRACE] start failed: {ex.Message}");
        }
    }

    /// <summary>Logs one sample of window state, alphas and positions.</summary>
    /// <param name="root">Shop window root, still alive.</param>
    /// <param name="index">Sample number, starting at zero.</param>
    internal static void Sample(Transform root, int index)
    {
        try
        {
            if (root == null)
            {
                return;
            }

            StringBuilder line = new StringBuilder("[SHOPTRACE] ");
            line.Append($"i={index} state='{State(root)}'");
            line.Append($" listGroup={Flag(root.Find("Item List Group"))}");
            line.Append($" list={Flag(root.Find("Item List Group/Item List"))}");
            line.Append($" window={Flag(root.Find("Window"))}");
            line.Append($" winAlpha={Alpha(root.Find("Window"))}");
            line.Append($" rows={Rows(root)}");
            line.Append($" root={Vec(root.position)}");
            line.Append($" hero={Vec(HeroPosition(root.position))}");
            line.Append($" cam={(Camera.main != null ? Vec(Camera.main.transform.position) : "<none>")}");
            Plugin.Log.LogInfo(line.ToString());
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPTRACE] sample {index} failed: {ex.Message}");
        }
    }

    /// <summary>Active state of <c>shop_control</c> on the window root, or a note.</summary>
    private static string State(Transform root)
    {
        foreach (PlayMakerFSM fsm in root.GetComponents<PlayMakerFSM>())
        {
            if (fsm != null && fsm.FsmName == "shop_control")
            {
                return fsm.ActiveStateName ?? string.Empty;
            }
        }

        return "<no shop_control>";
    }

    /// <summary><c>self/hier</c> flags of an object, <c>missing</c> when it is gone.</summary>
    private static string Flag(Transform? node)
    {
        if (node == null)
        {
            return "missing";
        }

        return $"{node.gameObject.activeSelf}/{node.gameObject.activeInHierarchy}";
    }

    /// <summary>Fade alphas of an object: its own and the total through parent groups.</summary>
    private static string Alpha(Transform? node)
    {
        NestedFadeGroupBase? group = FadeGroup(node);
        if (group == null)
        {
            return "<none>";
        }

        return $"{group.AlphaSelf:F2}/{group.AlphaTotal:F2}";
    }

    /// <summary>Alphas of the first three item rows.</summary>
    private static string Rows(Transform root)
    {
        ShopItemStats[] rows = root.GetComponentsInChildren<ShopItemStats>(true);
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < rows.Length && i < 3; i++)
        {
            NestedFadeGroupBase? group = FadeGroup(rows[i].transform);
            string alpha = group != null ? $"{group.AlphaSelf:F2}/{group.AlphaTotal:F2}" : "<none>";
            builder.Append($" row{i}=y{rows[i].transform.position.y:F2} a{alpha}");
        }

        return builder.Length == 0 ? "<none>" : builder.ToString();
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

    /// <summary>Hero world position, with a fallback so the line always has a value.</summary>
    private static Vector3 HeroPosition(Vector3 fallback)
    {
        HeroController? hero = HeroController.instance;
        return hero != null ? hero.transform.position : fallback;
    }

    private static string Vec(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }

    /// <summary>The spawned shop window of the scene, even while it is hidden.</summary>
    private static GameObject? LiveShopRoot()
    {
        try
        {
            foreach (ShopOwnerBase owner in Resources.FindObjectsOfTypeAll<ShopOwnerBase>())
            {
                if (owner != null && owner.gameObject.scene.IsValid() && owner.gameObject.activeInHierarchy)
                {
                    return owner.ShopObject;
                }
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }
}

/// <summary>
/// Ticks the shop trace: one sample every <see cref="ShopTrace.FrameStep"/> frames,
/// then removes itself.
/// </summary>
internal sealed class ShopTraceRunner : MonoBehaviour
{
    private int _frames;
    private int _samples;

    /// <summary>Restarts the ten samples without adding a second runner.</summary>
    internal void Restart()
    {
        _frames = 0;
        _samples = 0;
    }

    private void Update()
    {
        if (++_frames % 10 != 0)
        {
            return;
        }

        ShopTrace.Sample(transform, _samples++);
        if (_samples >= 10)
        {
            Destroy(this);
        }
    }
}
