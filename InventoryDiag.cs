using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Read-only diagnostic of the live inventory crest widgets: sizes of the crest card,
/// its sprite children, its slots and its parent chain. One block per crest name per
/// session, so the log stays readable. Never throws, never changes anything.
/// </summary>
internal static class InventoryDiag
{
    /// <summary>Minimum seconds between scene scans.</summary>
    private const float ScanInterval = 0.5f;

    /// <summary>How many ancestors of the crest card are reported.</summary>
    private const int ParentLevels = 3;

    private static readonly HashSet<string> Logged = new HashSet<string>();

    private static float _nextScan;

    /// <summary>Scans for active crest widgets and logs every name not seen yet.</summary>
    internal static void Poll()
    {
        if (Time.unscaledTime < _nextScan)
        {
            return;
        }

        _nextScan = Time.unscaledTime + ScanInterval;

        try
        {
            foreach (InventoryToolCrest crest in Resources.FindObjectsOfTypeAll<InventoryToolCrest>())
            {
                if (crest == null)
                {
                    continue;
                }

                GameObject go = crest.gameObject;
                if (!go.activeInHierarchy || go.hideFlags != HideFlags.None || !go.scene.isLoaded)
                {
                    continue;
                }

                if (!Logged.Add(go.name))
                {
                    continue;
                }

                LogCrest(crest);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[INV] crest poll failed: {ex.Message}");
        }
    }

    private static void LogCrest(InventoryToolCrest crest)
    {
        try
        {
            Transform root = crest.transform;
            Vector2 rect = Rect(root);
            Plugin.Log.LogInfo(
                $"[INV] crest '{crest.gameObject.name}'" +
                $" localScale={Vec(root.localScale)} lossyScale={Vec(root.lossyScale)}" +
                $" rect=({rect.x:F2},{rect.y:F2})");

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is SpriteRenderer sprite)
                {
                    LogChild(sprite);
                }
            }

            foreach (InventoryToolCrestSlot slot in root.GetComponentsInChildren<InventoryToolCrestSlot>(true))
            {
                if (slot != null)
                {
                    LogSlot(slot);
                }
            }

            Transform? current = root.parent;
            for (int level = 0; level < ParentLevels && current != null; level++)
            {
                Plugin.Log.LogInfo($"[INV] parent chain '{Path(current)}' scale={Vec(current.lossyScale)}");
                current = current.parent;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[INV] crest block failed: {ex.Message}");
        }
    }

    private static void LogChild(SpriteRenderer renderer)
    {
        Sprite? sprite = renderer.sprite;
        Vector2 spriteRect = sprite != null ? sprite.rect.size : Vector2.zero;
        float ppu = sprite != null ? sprite.pixelsPerUnit : 0f;
        Vector3 bounds = renderer.bounds.size;
        Vector2 world = ppu > 0f ? spriteRect / ppu : Vector2.zero;

        Plugin.Log.LogInfo(
            $"[INV]  child '{Path(renderer.transform)}'" +
            $" sprite='{(sprite != null ? sprite.name : "<null>")}'" +
            $" rect=({spriteRect.x:F2},{spriteRect.y:F2}) ppu={ppu:F2}" +
            $" bounds=({bounds.x:F3},{bounds.y:F3}) world=({world.x:F3},{world.y:F3})" +
            $" scale={Vec(renderer.transform.localScale)}");
    }

    private static void LogSlot(InventoryToolCrestSlot slot)
    {
        SpriteRenderer? renderer = slot.GetComponentInChildren<SpriteRenderer>(true);
        Sprite? sprite = renderer != null ? renderer.sprite : null;
        Vector3 bounds = renderer != null ? renderer.bounds.size : Vector3.zero;
        Vector3 local = slot.transform.localPosition;

        Plugin.Log.LogInfo(
            $"[INV]  slot '{Path(slot.transform)}'" +
            $" pos=({local.x:F3},{local.y:F3}) localScale={Vec(slot.transform.localScale)}" +
            $" sprite='{(sprite != null ? sprite.name : "<null>")}'" +
            $" bounds=({bounds.x:F3},{bounds.y:F3})");
    }

    /// <summary>Size of the transform's rect, or of the first sprite when it has no RectTransform.</summary>
    private static Vector2 Rect(Transform transform)
    {
        if (transform is RectTransform rect)
        {
            return rect.rect.size;
        }

        SpriteRenderer? sprite = transform.GetComponentInChildren<SpriteRenderer>(true);
        return sprite != null ? (Vector2)sprite.bounds.size : Vector2.zero;
    }

    private static string Path(Transform transform)
    {
        StringBuilder builder = new StringBuilder(transform.name);
        Transform current = transform.parent;
        while (current != null && builder.Length < 160)
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
}
