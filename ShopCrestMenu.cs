using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Shows the crest choice in the game's own live shop window: the shop object the
/// scene's <see cref="ShopOwnerBase"/> already spawned. The window is moved to the
/// hero, its stock is swapped for free temp entries, and the shop FSM is asked to
/// open. Vanilla stock and position are restored when the FSM returns to idle.
/// Never throws.
/// </summary>
internal static class ShopCrestMenu
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    /// <summary>FSM on the shop root that drives the window.</summary>
    private const string ControlFsmName = "shop_control";

    /// <summary>Event that takes <c>shop_control</c> from idle to the open chain.</summary>
    private const string OpenEvent = "SHOP UP";

    /// <summary>Event that closes the window again.</summary>
    private const string CloseEvent = "CLOSE SHOP WINDOW";

    /// <summary>Idle state of <c>shop_control</c>; the window is closed there.</summary>
    private const string IdleState = "Idle";

    /// <summary>Panel centre is lifted this far above the hero so the window reads centred.</summary>
    private const float UiLift = 0.8f;

    private static readonly FieldInfo? SpawnedShopField =
        typeof(ShopOwnerBase).GetField("_spawnedShop", PrivateStatic);

    private static readonly FieldInfo? StockArrayField =
        typeof(ShopMenuStock).GetField("stock", PrivateInstance);

    private static readonly FieldInfo? TopYField = typeof(ShopItemStats).GetField("topY", PrivateInstance);
    private static readonly FieldInfo? BotYField = typeof(ShopItemStats).GetField("botY", PrivateInstance);

    /// <summary>Temp shop entries on screen mapped to their crest index, filled per session.</summary>
    private static readonly Dictionary<ShopItem, int> TempItems = new Dictionary<ShopItem, int>();

    private static List<ToolCrest> _crests = new List<ToolCrest>();
    private static List<ShopItem> _tempItems = new List<ShopItem>();
    private static ShopMenuStock? _stock;
    private static Transform? _root;
    private static PlayMakerFSM? _control;
    private static ShopItem[]? _origStock;
    private static Vector3 _origPosition;
    private static float _boundShift;
    private static bool _fromDialogue;
    private static bool _sawOpenState;

    /// <summary>True while the live shop window is showing our crests.</summary>
    internal static bool IsOpen { get; set; }

    /// <summary>
    /// Swaps the live shop stock for <paramref name="crests"/> and opens the window.
    /// Falls back to the custom menu on any failure.
    /// </summary>
    /// <param name="crests">Crests to show; must not be empty.</param>
    /// <param name="fromDialogue">True when the choice came from the maid dialogue.</param>
    /// <returns>True when the live window was opened.</returns>
    internal static bool TryOpen(List<ToolCrest> crests, bool fromDialogue)
    {
        if (IsOpen || crests.Count == 0)
        {
            return false;
        }

        ResetSession();
        try
        {
            ShopOwnerBase? owner = FindLiveOwner();
            if (owner == null)
            {
                return Fallback("no live ShopOwnerBase in scene");
            }

            ShopMenuStock? stock = SpawnedStock() ?? FallbackStock(owner);
            if (stock == null)
            {
                return Fallback($"'{owner.name}' has no spawned shop window");
            }

            GameObject root = stock.gameObject;
            if (!root.activeInHierarchy)
            {
                return Fallback($"shop window '{root.name}' is inactive");
            }

            PlayMakerFSM? control = ControlFsm(root);
            if (control == null)
            {
                return Fallback($"'{root.name}' has no PlayMakerFSM '{ControlFsmName}'");
            }

            string state = ActiveState(control);
            if (state != IdleState)
            {
                return Fallback($"'{ControlFsmName}' is in state '{state}', not '{IdleState}'");
            }

            if (!HasEvent(control, OpenEvent))
            {
                return Fallback($"'{ControlFsmName}' has no '{OpenEvent}' transition");
            }

            ShopItem[]? origStock = StockArrayField?.GetValue(stock) as ShopItem[];
            if (origStock == null || origStock.Length == 0)
            {
                return Fallback("live shop has no stock array");
            }

            _stock = stock;
            _root = root.transform;
            _control = control;
            _origStock = origStock;
            _origPosition = root.transform.position;
            _crests = new List<ToolCrest>(crests);
            _fromDialogue = fromDialogue;

            stock.SetStock(BuildItems());
            BuildLists();
            stock.SetWasItemPurchased(false);
            int count = stock.GetItemCount();
            if (count == 0)
            {
                return Fallback("live shop built an empty item list");
            }

            MoveToHero();
            control.SendEvent(OpenEvent);

            IsOpen = true;
            CrestSelectMenu.IsOpen = true;
            root.AddComponent<ShopCrestSession>();
            LogState($"opened with {count} crest entries");
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
                Restore();
            }
        }
    }

    /// <summary>Builds one free temp shop entry per offered crest and records its index.</summary>
    private static ShopItem[] BuildItems()
    {
        TempItems.Clear();
        DestroyTempItems();
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
            _tempItems.Add(item);
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
    /// Rebuilds the row list on every stock component under the window: the master
    /// fills the rows, the input component keeps its own count for the FSM.
    /// </summary>
    private static void BuildLists()
    {
        if (_root == null)
        {
            return;
        }

        foreach (ShopMenuStock stock in _root.GetComponentsInChildren<ShopMenuStock>(true))
        {
            stock.BuildItemList();
        }
    }

    /// <summary>
    /// Harmony postfix target: applies the crest when the game buys one of the temp
    /// entries, then closes the window. Vanilla items are ignored.
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
            Plugin.Log.LogInfo($"[SHOP] crest purchased from live shop: {crest.name} (index={index})");
            CrestPickerService.ApplyCrest(crest);
            Plugin.OnMenuClosed(true, _fromDialogue);
            CloseShop();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[SHOP] temp purchase handler failed: {ex}");
        }
    }

    /// <summary>Sends the close event; restores right away if the FSM was already idle.</summary>
    private static void CloseShop()
    {
        if (_control == null)
        {
            Restore();
            return;
        }

        _control.SendEvent(CloseEvent);
        Plugin.Log.LogInfo($"[SHOP] sent '{CloseEvent}' to '{ControlFsmName}'.");
        if (ActiveState(_control) == IdleState)
        {
            Restore();
        }
    }

    /// <summary>True once the window has been open and the FSM is back in idle.</summary>
    internal static bool ClosedByFsm()
    {
        if (!IsOpen || _control == null)
        {
            return false;
        }

        if (ActiveState(_control) != IdleState)
        {
            _sawOpenState = true;
            return false;
        }

        return _sawOpenState;
    }

    /// <summary>
    /// Moves the window so the panel centre sits over the hero, and shifts the
    /// world-space row bounds by the same delta, or every row stays faded out.
    /// </summary>
    private static void MoveToHero()
    {
        if (_root == null)
        {
            return;
        }

        Transform reference = _root.Find("Window/Shop_Backboard") ?? _root.Find("Window") ?? _root;
        Vector3 panelCentre = reference.position;
        Vector3 offset = _origPosition - panelCentre;
        Vector3 hero = HeroPosition();
        Vector3 target = hero;
        target.y += UiLift;
        target.z = _origPosition.z;

        Vector3 next = target + offset;
        _root.position = next;
        _boundShift = next.y - _origPosition.y;
        ShiftRowBounds(_boundShift);
        LogState($"placed at hero={Vec(hero)} panel={Vec(panelCentre)}");
    }

    /// <summary>Adds <paramref name="dy"/> to every row's world-space fade bounds.</summary>
    private static void ShiftRowBounds(float dy)
    {
        if (_root == null || dy == 0f)
        {
            return;
        }

        foreach (ShopItemStats row in _root.GetComponentsInChildren<ShopItemStats>(true))
        {
            if (TopYField?.GetValue(row) is float top)
            {
                TopYField.SetValue(row, top + dy);
            }

            if (BotYField?.GetValue(row) is float bot)
            {
                BotYField.SetValue(row, bot + dy);
            }
        }
    }

    /// <summary>Puts the vanilla stock and the original window position back.</summary>
    internal static void Restore()
    {
        if (_stock == null && !IsOpen)
        {
            ResetSession();
            return;
        }

        try
        {
            if (_stock != null && _origStock != null)
            {
                _stock.SetStock(_origStock);
                BuildLists();
                _stock.SetWasItemPurchased(false);
            }

            if (_root != null)
            {
                ShiftRowBounds(-_boundShift);
                _root.position = _origPosition;
            }

            Plugin.Log.LogInfo("[SHOP] live shop restored to vanilla stock and position.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] live shop restore failed: {ex.Message}");
        }
        finally
        {
            IsOpen = false;
            CrestSelectMenu.IsOpen = false;
            ResetSession();
        }
    }

    /// <summary>Clears per-session state before a window is prepared and after it is restored.</summary>
    internal static void ResetSession()
    {
        TempItems.Clear();
        DestroyTempItems();
        _crests = new List<ToolCrest>();
        _stock = null;
        _root = null;
        _control = null;
        _origStock = null;
        _boundShift = 0f;
        _fromDialogue = false;
        _sawOpenState = false;
    }

    /// <summary>Destroys the temp scriptable objects so they do not accumulate.</summary>
    private static void DestroyTempItems()
    {
        foreach (ShopItem item in _tempItems)
        {
            if (item != null)
            {
                UnityEngine.Object.Destroy(item);
            }
        }

        _tempItems.Clear();
    }

    /// <summary>The spawned shop window of the scene, even while it is hidden.</summary>
    private static ShopMenuStock? SpawnedStock()
    {
        try
        {
            return SpawnedShopField?.GetValue(null) as ShopMenuStock;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Owner property fallback; it re-applies the owner stock before we read it.</summary>
    private static ShopMenuStock? FallbackStock(ShopOwnerBase owner)
    {
        try
        {
            GameObject? shopObject = owner.ShopObject;
            return shopObject != null ? shopObject.GetComponent<ShopMenuStock>() : null;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] owner shop lookup failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>The <c>shop_control</c> FSM on the window root.</summary>
    private static PlayMakerFSM? ControlFsm(GameObject root)
    {
        try
        {
            foreach (PlayMakerFSM fsm in root.GetComponents<PlayMakerFSM>())
            {
                if (fsm != null && fsm.FsmName == ControlFsmName)
                {
                    return fsm;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] FSM lookup failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>Active state name of an FSM, empty when it cannot be read.</summary>
    private static string ActiveState(PlayMakerFSM fsm)
    {
        try
        {
            return fsm.ActiveStateName ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>True when the FSM has a transition on <paramref name="eventName"/> anywhere.</summary>
    private static bool HasEvent(PlayMakerFSM fsm, string eventName)
    {
        try
        {
            Fsm? data = fsm.Fsm;
            if (data == null)
            {
                return false;
            }

            if (HasEvent(data.GlobalTransitions, eventName))
            {
                return true;
            }

            FsmState[]? states = fsm.FsmStates;
            if (states == null)
            {
                return false;
            }

            foreach (FsmState state in states)
            {
                if (state != null && HasEvent(state.Transitions, eventName))
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] FSM transition scan failed: {ex.Message}");
        }

        return false;
    }

    private static bool HasEvent(FsmTransition[]? transitions, string eventName)
    {
        if (transitions == null)
        {
            return false;
        }

        foreach (FsmTransition transition in transitions)
        {
            if (transition != null && transition.EventName == eventName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Hero world position, camera position as a fallback.</summary>
    private static Vector3 HeroPosition()
    {
        try
        {
            HeroController? hero = HeroController.instance;
            if (hero != null)
            {
                return hero.transform.position;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] hero position failed: {ex.Message}");
        }

        Camera? camera = Camera.main;
        return camera != null ? camera.transform.position : _origPosition;
    }

    /// <summary>Logs where the window and the hero are, so a wrong placement is visible.</summary>
    private static void LogState(string message)
    {
        Vector3 hero = HeroPosition();
        Vector3 camera = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        Vector3 root = _root != null ? _root.position : Vector3.zero;
        string state = _control != null ? ActiveState(_control) : "<none>";
        Plugin.Log.LogInfo(
            $"[SHOP] live shop {message}: state='{state}' root={Vec(root)}" +
            $" hero={Vec(hero)} camera={Vec(camera)} orig={Vec(_origPosition)}");
    }

    /// <summary>Logs the row fade bounds and item count of the live window.</summary>
    internal static void LogRows()
    {
        try
        {
            if (_root == null || _stock == null)
            {
                return;
            }

            ShopItemStats[] rows = _root.GetComponentsInChildren<ShopItemStats>(true);
            Plugin.Log.LogInfo(
                $"[SHOP] live rows={rows.Length} stock={_stock.GetItemCount()}" +
                $" counts={Counts()}");
            for (int i = 0; i < rows.Length && i < 3; i++)
            {
                ShopItemStats row = rows[i];
                Plugin.Log.LogInfo(
                    $"[SHOP]   row{i} y={row.transform.position.y:F2}" +
                    $" top={Float(TopYField, row):F2} bot={Float(BotYField, row):F2}" +
                    $" active={row.gameObject.activeSelf}" +
                    $" item='{(row.Item != null ? row.Item.name : "<none>")}' cost={row.GetCost()}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] live row log failed: {ex.Message}");
        }
    }

    /// <summary>Item count of every stock component under the window.</summary>
    private static string Counts()
    {
        if (_root == null)
        {
            return "<none>";
        }

        List<string> parts = new List<string>();
        foreach (ShopMenuStock stock in _root.GetComponentsInChildren<ShopMenuStock>(true))
        {
            parts.Add($"{stock.name}={stock.GetItemCount()}");
        }

        return string.Join(",", parts);
    }

    private static float Float(FieldInfo? field, object target)
    {
        return field?.GetValue(target) is float value ? value : float.NaN;
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

    /// <summary>Logs why the live shop was not used and tells the caller to fall back.</summary>
    private static bool Fallback(string reason)
    {
        IsOpen = false;
        Plugin.Log.LogWarning($"[SHOP] fallback to custom menu: {reason}");
        return false;
    }

    private static string Vec(Vector3 value)
    {
        return $"({value.x:F2},{value.y:F2},{value.z:F2})";
    }
}

/// <summary>
/// Lives on the live shop root while our stock is shown: restores the vanilla stock
/// and position as soon as <c>shop_control</c> returns to idle. A timeout restores
/// it too, so a stuck window cannot keep foreign stock forever.
/// </summary>
internal sealed class ShopCrestSession : MonoBehaviour
{
    /// <summary>Upper bound on how long our stock may stay in the window.</summary>
    private const float TimeoutSeconds = 900f;

    private int _frames;
    private float _deadline;

    private void Start()
    {
        _deadline = Time.unscaledTime + TimeoutSeconds;
    }

    private void Update()
    {
        try
        {
            if (_frames++ == 1)
            {
                ShopCrestMenu.LogRows();
            }

            if (ShopCrestMenu.ClosedByFsm())
            {
                Plugin.Log.LogInfo("[SHOP] shop FSM returned to idle, restoring vanilla shop.");
                ShopCrestMenu.Restore();
                Destroy(this);
                return;
            }

            if (Time.unscaledTime > _deadline)
            {
                Plugin.Log.LogWarning("[SHOP] live shop session timed out, restoring vanilla shop.");
                ShopCrestMenu.Restore();
                Destroy(this);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] live shop session failed: {ex.Message}");
            ShopCrestMenu.Restore();
            Destroy(this);
        }
    }

    private void OnDestroy()
    {
        ShopCrestMenu.Restore();
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
