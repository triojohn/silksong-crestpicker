using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

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

    /// <summary>Temp shop entries on screen mapped to their crest index, filled per session.</summary>
    private static readonly Dictionary<ShopItem, int> TempItems = new Dictionary<ShopItem, int>();

    private static List<ToolCrest> _crests = new List<ToolCrest>();
    private static ShopMenuStock? _stock;
    private static GameObject? _clone;
    private static Transform? _liveRoot;
    private static bool _fromDialogue;

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

        ResetSession();
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

            Transform? live = LiveShop()?.transform;
            _liveRoot = live;
            PlaceLikeLiveShop(clone.transform, live);
            stock.MasterList = null;
            _crests = new List<ToolCrest>(crests);
            _stock = stock;
            _clone = clone;
            _fromDialogue = fromDialogue;
            stock.SetStock(BuildItems());
            stock.BuildItemList();
            stock.SetWasItemPurchased(false);
            if (stock.GetItemCount() == 0)
            {
                return Fallback("shop built an empty item list");
            }

            clone.SetActive(true);
            if (live != null)
            {
                ShopCloneMirror.Mirror(live, clone.transform);
            }

            ShopCloneMirror.ForceFades(clone.transform);
            ShopCloneMirror.ForceRows(stock);
            clone.AddComponent<ShopCrestSession>();
            UnityEngine.Object.Destroy(clone, ShopCrestSession.TimeoutSeconds);

            IsOpen = true;
            CrestSelectMenu.IsOpen = true;
            Plugin.Log.LogInfo(
                $"[SHOP] real shop menu opened: {stock.GetItemCount()} crest entries, " +
                $"owner='{owner.name}', prefab='{prefab.name}'.");
            ShopCloneMirror.LogComparison(live, clone.transform);
            return true;
        }
        catch (Exception ex)
        {
            return Fallback($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (!IsOpen)
            {
                ResetSession();
                if (clone != null)
                {
                    UnityEngine.Object.Destroy(clone);
                }
            }
        }
    }

    /// <summary>Builds one free temp shop entry per offered crest and records its index.</summary>
    private static ShopItem[] BuildItems()
    {
        TempItems.Clear();
        ShopItem[] items = new ShopItem[_crests.Count];
        for (int i = 0; i < _crests.Count; i++)
        {
            ToolCrest crest = _crests[i];
            ShopItem item = ShopItem.CreateTemp(crest.name);
            item.OverrideCostDelegate = ZeroCost;
            CopyCrestText(item, crest);
            SetField(item, "itemSprite", crest.CrestSprite);
            TempItems[item] = i;
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

    /// <summary>
    /// Harmony postfix target: applies the crest when the game buys one of the temp
    /// entries. Vanilla shop items are ignored because they are not in the map.
    /// </summary>
    /// <param name="stats">Item view whose purchase just completed; may be null.</param>
    internal static void HandleTempPurchase(ShopItemStats? stats)
    {
        try
        {
            ShopItem? item = stats != null ? stats.Item : null;
            if (item == null || !TempItems.TryGetValue(item, out int index))
            {
                return;
            }

            TempItems.Remove(item);
            if (index < 0 || index >= _crests.Count)
            {
                return;
            }

            ToolCrest crest = _crests[index];
            Plugin.Log.LogInfo($"[SHOP] temp position purchased: {crest.name} (index={index})");
            CrestPickerService.ApplyCrest(crest);

            if (_fromDialogue)
            {
                Plugin.OnMenuClosed(true, true);
                CloseClone();
                return;
            }

            _crests.RemoveAt(index);
            RebuildStock();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[SHOP] temp purchase handler failed: {ex}");
        }
    }

    /// <summary>Drops the bought crest from the list and closes the menu when nothing is left.</summary>
    private static void RebuildStock()
    {
        if (_stock == null)
        {
            CloseClone();
            return;
        }

        ShopItem[] items = BuildItems();
        if (items.Length == 0)
        {
            CloseClone();
            return;
        }

        _stock.SetStock(items);
        _stock.BuildItemList();
        ShopCloneMirror.ForceRows(_stock);
    }

    /// <summary>Tears down the cloned window; its session resets the shared state.</summary>
    private static void CloseClone()
    {
        if (_clone != null)
        {
            UnityEngine.Object.Destroy(_clone);
        }
    }

    /// <summary>Clears per-session state before a menu is built and after it is torn down.</summary>
    internal static void ResetSession()
    {
        TempItems.Clear();
        _crests = new List<ToolCrest>();
        _stock = null;
        _clone = null;
        _liveRoot = null;
        _fromDialogue = false;
    }

    /// <summary>Places the clone where the live shop instance sits and copies its layer.</summary>
    private static void PlaceLikeLiveShop(Transform clone, Transform? live)
    {
        if (live == null)
        {
            Plugin.Log.LogInfo("[SHOP] no live shop instance, clone keeps prefab transform.");
            return;
        }

        clone.SetParent(live.parent, false);
        clone.localPosition = live.localPosition;
        clone.localRotation = live.localRotation;
        clone.localScale = live.localScale;
        clone.gameObject.layer = live.gameObject.layer;
        Plugin.Log.LogInfo("[SHOP] clone placed at the live shop transform.");
    }

    /// <summary>The spawned shop window of the scene, even while it is hidden.</summary>
    private static ShopMenuStock? LiveShop()
    {
        return SpawnedShopField?.GetValue(null) as ShopMenuStock;
    }

    /// <summary>Logs live vs clone state once the clone has been through a frame.</summary>
    internal static void LogCloneState()
    {
        try
        {
            ShopCloneMirror.LogComparison(_liveRoot, _clone?.transform);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] clone state log failed: {ex.Message}");
        }
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

    private int _frames;

    private void Update()
    {
        try
        {
            if (_frames++ == 1)
            {
                ShopCrestMenu.LogCloneState();
            }

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
        ShopCrestMenu.ResetSession();
    }
}

/// <summary>
/// Applies the crest after a successful purchase of a temp shop entry. Postfix so
/// the vanilla charge, onComplete callback and FSM transitions still run first.
/// </summary>
[HarmonyPatch(typeof(ShopItemStats), "SetPurchased")]
internal static class ShopTempPurchasePatch
{
    private static void Postfix(ShopItemStats __instance)
    {
        ShopCrestMenu.HandleTempPurchase(__instance);
    }
}
