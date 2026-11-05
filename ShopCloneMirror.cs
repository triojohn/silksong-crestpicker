using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TeamCherry.NestedFadeGroup;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Copies the runtime look of a live shop window onto the clone and reports every
/// field that decides whether the world-space item rows are on screen. Never throws.
/// </summary>
internal static class ShopCloneMirror
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo? TopYField = typeof(ShopItemStats).GetField("topY", PrivateInstance);
    private static readonly FieldInfo? BotYField = typeof(ShopItemStats).GetField("botY", PrivateInstance);
    private static readonly FieldInfo? FadeField = typeof(ShopItemStats).GetField("fadeGroup", PrivateInstance);
    private static readonly FieldInfo? HiddenField = typeof(ShopItemStats).GetField("hidden", PrivateInstance);
    private static readonly FieldInfo? AvailableField =
        typeof(ShopMenuStock).GetField("availableStock", PrivateInstance);

    /// <summary>Copies layer, sorting order and enabled state from live onto the clone, level by level.</summary>
    /// <param name="live">Live shop window root.</param>
    /// <param name="clone">Clone root built from the same prefab.</param>
    internal static void Mirror(Transform live, Transform clone)
    {
        CopyNode(live, clone);

        int count = Math.Min(live.childCount, clone.childCount);
        for (int i = 0; i < count; i++)
        {
            Mirror(live.GetChild(i), clone.GetChild(i));
        }
    }

    /// <summary>Forces every fade group outside the item rows to full alpha.</summary>
    /// <param name="root">Clone root.</param>
    internal static void ForceFades(Transform root)
    {
        if (!IsRow(root) && root.GetComponent<NestedFadeGroupBase>() is NestedFadeGroupBase fade)
        {
            fade.AlphaSelf = 1f;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            ForceFades(root.GetChild(i));
        }
    }

    /// <summary>Activates the rows the stock counts as available and sets their alpha to one.</summary>
    /// <param name="stock">Clone's stock component.</param>
    internal static void ForceRows(ShopMenuStock stock)
    {
        if (!(AvailableField?.GetValue(stock) is List<ShopItemStats> rows))
        {
            return;
        }

        foreach (ShopItemStats row in rows)
        {
            if (row == null)
            {
                continue;
            }

            if (!row.gameObject.activeSelf)
            {
                row.gameObject.SetActive(true);
            }

            if (FadeField?.GetValue(row) is NestedFadeGroupBase fade)
            {
                fade.AlphaSelf = 1f;
            }
        }
    }

    /// <summary>Logs live and clone state side by side, so a mismatch is visible in the log.</summary>
    /// <param name="live">Live shop root, may be null.</param>
    /// <param name="clone">Clone root, may be null.</param>
    internal static void LogComparison(Transform? live, Transform? clone)
    {
        Plugin.Log.LogInfo("[SHOP] " + Describe("live", live));
        Plugin.Log.LogInfo("[SHOP] " + Describe("clone", clone));
    }

    /// <summary>One-line state of a shop window plus its first three item rows.</summary>
    /// <param name="tag">Label for the log line.</param>
    /// <param name="root">Window root, may be null.</param>
    internal static string Describe(string tag, Transform? root)
    {
        if (root == null)
        {
            return tag + ": <missing>";
        }

        GameObject go = root.gameObject;
        ShopItemStats[] rows = root.GetComponentsInChildren<ShopItemStats>(true);
        StringBuilder text = new StringBuilder();
        text.Append($"{tag}: activeSelf={go.activeSelf} inHierarchy={go.activeInHierarchy}")
            .Append($" layer={go.layer} {CameraText(go.layer)}")
            .Append($" pos={Vec(root.position)} scale={Vec(root.localScale)}")
            .Append($" canvas={root.GetComponentsInChildren<Canvas>(true).Length}")
            .Append($" canvasGroup={root.GetComponentsInChildren<CanvasGroup>(true).Length}")
            .Append($" fades={root.GetComponentsInChildren<NestedFadeGroupBase>(true).Length}")
            .Append($" rows={rows.Length} {SortingText(root)}");
        for (int i = 0; i < rows.Length && i < 3; i++)
        {
            text.Append($" || row{i}: {RowText(rows[i])}");
        }

        return text.ToString();
    }

    /// <summary>Row position against its own fade bounds plus its current fade state.</summary>
    private static string RowText(ShopItemStats row)
    {
        NestedFadeGroupBase? fade = FadeField?.GetValue(row) as NestedFadeGroupBase;
        string alpha = fade == null ? "<no fade>" : $"{fade.AlphaSelf:F2}/{fade.AlphaTotal:F2}";
        return $"y={row.transform.position.y:F2} top={Float(TopYField, row):F2} bot={Float(BotYField, row):F2}" +
            $" active={row.gameObject.activeSelf} alpha={alpha} hidden={HiddenField?.GetValue(row)}";
    }

    /// <summary>Recursive copy step for one node; enable state is only ever turned on.</summary>
    private static void CopyNode(Transform live, Transform clone)
    {
        clone.gameObject.layer = live.gameObject.layer;
        if (live.gameObject.activeSelf && !clone.gameObject.activeSelf)
        {
            clone.gameObject.SetActive(true);
        }

        if (live.GetComponent<Renderer>() is Renderer source && clone.GetComponent<Renderer>() is Renderer target)
        {
            target.sortingLayerID = source.sortingLayerID;
            target.sortingOrder = source.sortingOrder;
        }
    }

    /// <summary>True for the row root and anything under it.</summary>
    private static bool IsRow(Transform node)
    {
        return node.GetComponentInParent<ShopItemStats>() != null;
    }

    private static float Float(FieldInfo? field, object target)
    {
        return field?.GetValue(target) is float value ? value : float.NaN;
    }

    private static string SortingText(Transform root)
    {
        Renderer? renderer = root.GetComponentInChildren<Renderer>(true);
        return renderer == null
            ? "sorting=<none>"
            : $"sorting={renderer.sortingLayerName}:{renderer.sortingOrder}";
    }

    private static string CameraText(int layer)
    {
        Camera? camera = Camera.main;
        if (camera == null)
        {
            return "camera=<none>";
        }

        bool seen = (camera.cullingMask & (1 << layer)) != 0;
        return $"camera='{camera.name}' seesLayer={seen}";
    }

    private static string Vec(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }
}
