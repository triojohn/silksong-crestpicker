using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace CrestPicker;

/// <summary>
/// Tries to show the crest choice in the game's own shop window. Works on a
/// throwaway copy of the shop prefab taken from a live <see cref="ShopOwnerBase"/>,
/// so the player's real shop instance is never touched. Never throws.
/// </summary>
internal static class ShopCrestMenu
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    private static readonly FieldInfo? ShopPrefabField =
        typeof(ShopOwnerBase).GetField("shopPrefab", PrivateInstance);

    private static readonly FieldInfo? SpawnedShopField =
        typeof(ShopOwnerBase).GetField("_spawnedShop", PrivateStatic);

    /// <summary>True while a cloned shop window is on screen.</summary>
    internal static bool IsOpen { get; set; }

    /// <summary>
    /// Builds a cloned shop window for <paramref name="crests"/> and shows it.
    /// Falls back to the custom menu on any failure.
    /// </summary>
    /// <param name="crests">Crests to show; must not be empty.</param>
    /// <param name="fromDialogue">True when the choice came from the maid dialogue.</param>
    /// <returns>True when the cloned shop window was built and shown.</returns>
    internal static bool TryOpen(List<ToolCrest> crests, bool fromDialogue)
    {
        if (IsOpen || crests.Count == 0)
        {
            return false;
        }

        GameObject? clone = null;
        try
        {
            ShopOwnerBase? owner = FindLiveOwner();
            if (owner == null)
            {
                return Fallback("no live ShopOwnerBase in scene");
            }

            ShopMenuStock? prefab = ShopPrefabField?.GetValue(owner) as ShopMenuStock;
            if (prefab == null)
            {
                return Fallback($"ShopOwnerBase '{owner.name}' has no shopPrefab");
            }

            clone = UnityEngine.Object.Instantiate(prefab.gameObject);
            ShopMenuStock? stock = clone.GetComponent<ShopMenuStock>();
            if (stock == null)
            {
                return Fallback("shop prefab copy has no ShopMenuStock");
            }

            PlaceLikeLiveShop(clone.transform);
            stock.MasterList = null;
            stock.SetStock(BuildItems(crests, fromDialogue));
            stock.BuildItemList();
            if (stock.GetItemCount() == 0)
            {
                return Fallback("shop built an empty item list");
            }

            clone.SetActive(true);
            clone.AddComponent<ShopCrestSession>();
            UnityEngine.Object.Destroy(clone, ShopCrestSession.TimeoutSeconds);

            IsOpen = true;
            CrestSelectMenu.IsOpen = true;
            Plugin.Log.LogInfo(
                $"[SHOP] real shop menu opened: {stock.GetItemCount()} crest entries, " +
                $"owner='{owner.name}', prefab='{prefab.name}'.");
            return true;
        }
        catch (Exception ex)
        {
            return Fallback($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (!IsOpen && clone != null)
            {
                UnityEngine.Object.Destroy(clone);
            }
        }
    }

    /// <summary>Builds one free temp shop entry per crest and hooks its purchase.</summary>
    private static ShopItem[] BuildItems(List<ToolCrest> crests, bool fromDialogue)
    {
        ShopItem[] items = new ShopItem[crests.Count];
        for (int i = 0; i < crests.Count; i++)
        {
            ToolCrest crest = crests[i];
            ShopItem item = ShopItem.CreateTemp(crest.name);
            item.OverrideCostDelegate = ZeroCost;
            CopyCrestText(item, crest);
            SetField(item, "itemSprite", crest.CrestSprite);
            HookPurchase(item, crest, fromDialogue);
            items[i] = item;
        }

        return items;
    }

    private static int ZeroCost()
    {
        return 0;
    }

    /// <summary>Copies the crest's own localised name and description onto the item.</summary>
    private static void CopyCrestText(ShopItem item, ToolCrest crest)
    {
        try
        {
            SetField(item, "displayName", crest.DisplayName);
            SetField(item, "description", crest.Description);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] crest text copy failed for '{crest.name}': {ex.Message}");
        }
    }

    /// <summary>Applies the crest when the player buys this temp shop entry.</summary>
    private static void HookPurchase(ShopItem item, ToolCrest crest, bool fromDialogue)
    {
        try
        {
            if (item.GetType().GetField("onPurchase", PrivateInstance)?.GetValue(item) is not UnityEvent purchase)
            {
                Plugin.Log.LogWarning($"[SHOP] no purchase event on temp item '{crest.name}'.");
                return;
            }

            purchase.AddListener(() =>
            {
                try
                {
                    CrestPickerService.ApplyCrest(crest);
                    Plugin.OnMenuClosed(true, fromDialogue);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[SHOP] crest purchase handler failed: {ex}");
                }
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] purchase hook failed for '{crest.name}': {ex.Message}");
        }
    }

    /// <summary>Places the clone at the live shop instance so it appears where the shop is.</summary>
    private static void PlaceLikeLiveShop(Transform clone)
    {
        if (SpawnedShopField?.GetValue(null) is not ShopMenuStock live || !live.gameObject.activeInHierarchy)
        {
            Plugin.Log.LogInfo("[SHOP] no active live shop instance, clone keeps prefab transform.");
            return;
        }

        clone.SetParent(live.transform.parent, false);
        clone.localPosition = live.transform.localPosition;
        clone.localRotation = live.transform.localRotation;
        clone.localScale = live.transform.localScale;
        Plugin.Log.LogInfo("[SHOP] clone placed at the live shop transform.");
    }

    /// <summary>First active scene shop owner; asset copies are ignored.</summary>
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

    /// <summary>Sets a private field, ignoring a missing field or a rejected value.</summary>
    private static void SetField(object target, string fieldName, object? value)
    {
        try
        {
            if (value == null)
            {
                return;
            }

            target.GetType().GetField(fieldName, PrivateInstance)?.SetValue(target, value);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] setting '{fieldName}' failed: {ex.Message}");
        }
    }

    /// <summary>Logs why the real shop was not used and tells the caller to fall back.</summary>
    private static bool Fallback(string reason)
    {
        IsOpen = false;
        Plugin.Log.LogWarning($"[SHOP] fallback to custom menu: {reason}");
        return false;
    }
}

/// <summary>
/// Lives on the cloned shop window: tears it down on cancel and clears the
/// open flags. The clone is also self-destroyed by a timer as a safety net.
/// </summary>
internal sealed class ShopCrestSession : MonoBehaviour
{
    /// <summary>Upper bound on how long a forgotten clone can stay in the scene.</summary>
    internal const float TimeoutSeconds = 900f;

    private void Update()
    {
        try
        {
            HeroActions? actions = ManagerSingleton<InputHandler>.Instance?.inputActions;
            if (actions == null)
            {
                return;
            }

            Platform? platform = Platform.Current;
            bool cancel = platform != null
                ? platform.GetMenuAction(actions) == Platform.MenuActions.Cancel
                : actions.MenuCancel.WasPressed;
            if (cancel)
            {
                Destroy(gameObject);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] clone input check failed: {ex.Message}");
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        ShopCrestMenu.IsOpen = false;
        CrestSelectMenu.IsOpen = false;
    }
}
