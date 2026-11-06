using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using HutongGames.PlayMaker;
using TeamCherry.NestedFadeGroup;
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

    /// <summary>Vanilla close event, valid in the <c>Open</c> state; runs the visual teardown.</summary>
    private const string VanillaCloseEvent = "CLOSE";

    /// <summary>State <c>shop_control</c> holds while the window is fully open.</summary>
    private const string OpenState = "Open";

    /// <summary>Event the window FSM needs to fade its borders and prompts back in.</summary>
    private const string BordesUpEvent = "FLUERS UP";

    /// <summary>Event the window FSM needs to fade its borders and prompts out.</summary>
    private const string BordesDownEvent = "FLUERS DOWN";

    /// <summary>Idle state of <c>shop_control</c>; the window is closed there.</summary>
    private const string IdleState = "Idle";

    /// <summary>Panel centre is lifted this far above the hero so the window reads centred.</summary>
    private const float UiLift = 0.8f;

    private static readonly FieldInfo? SpawnedShopField =
        typeof(ShopOwnerBase).GetField("_spawnedShop", PrivateStatic);

    private static readonly FieldInfo? StockArrayField =
        typeof(ShopMenuStock).GetField("stock", PrivateInstance);

    private static readonly FieldInfo? AvailableStockField =
        typeof(ShopMenuStock).GetField("availableStock", PrivateInstance);

    private static readonly FieldInfo? TopYField = typeof(ShopItemStats).GetField("topY", PrivateInstance);
    private static readonly FieldInfo? BotYField = typeof(ShopItemStats).GetField("botY", PrivateInstance);

    /// <summary>Temp shop entries on screen mapped to their crest index, filled per session.</summary>
    private static readonly Dictionary<ShopItem, int> TempItems = new Dictionary<ShopItem, int>();

    private static List<ToolCrest> _crests = new List<ToolCrest>();
    private static List<ShopItem> _tempItems = new List<ShopItem>();
    private static ShopItem[] _swapItems = Array.Empty<ShopItem>();
    private static ShopMenuStock? _stock;
    private static Transform? _root;
    private static PlayMakerFSM? _control;

    /// <summary>Vanilla stock per swapped stock component, restored on close.</summary>
    private static readonly Dictionary<ShopMenuStock, ShopItem[]> Swapped =
        new Dictionary<ShopMenuStock, ShopItem[]>();
    private static Vector3 _origPosition;
    private static float _boundShift;
    private static bool _fromDialogue;
    private static bool _sawOpenState;

    /// <summary>Teardown targets and their vanilla state, captured when the window opens.</summary>
    private static NestedFadeGroupBase? _fade;
    private static PlayMakerFSM? _windowFsm;
    private static GameObject? _cancelPrompt;
    private static GameObject? _confirmPrompt;
    private static bool _teardownCaptured;
    private static bool _reviveLogged;

    /// <summary>True while the live shop window is showing our crests.</summary>
    internal static bool IsOpen { get; set; }

    /// <summary>
    /// True while the live shop window is ours: NPC dialogue must not pop up over it.
    /// Set before the open event is sent, cleared when the session is reset.
    /// </summary>
    internal static bool SuppressDialogue { get; private set; }

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
            BeginTeardown(root.transform);
            _origPosition = root.transform.position;
            _crests = new List<ToolCrest>(crests);
            _fromDialogue = fromDialogue;
            SuppressDialogue = true;

            SwapStock(BuildItems());
            BuildLists();
            if (stock.GetItemCount() == 0)
            {
                return Fallback("live shop built an empty item list");
            }

            // The open event runs FSM actions that read ShopObject, and that getter
            // re-applies the owner's vanilla stock. Arm the session first so the
            // owner guard holds, then put our stock back after the event anyway.
            IsOpen = true;
            CrestSelectMenu.IsOpen = true;
            ShopInputLock.Block("shop open");

            MoveToHero();
            ReviveTeardown();
            control.SendEvent(OpenEvent);
            SwapStock(_swapItems);
            BuildLists();
            int count = stock.GetItemCount();

            ShopTrace.Begin("SHOP UP");
            LogStock("after SHOP UP");
            ShopRenderDiag.Capture("after SHOP UP", root.transform);

            ShopCosmetics.Begin(root.transform);
            root.AddComponent<ShopCrestSession>();
            LogState($"opened with {count} crest entries");
            return true;
        }
        catch (Exception ex)
        {
            Restore("open exception");
            return Fallback($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (!IsOpen)
            {
                Restore("open failed");
            }
        }
    }

    /// <summary>Crest behind a temp shop entry; null for any other item.</summary>
    /// <param name="item">Entry shown in a shop row.</param>
    internal static ToolCrest? CrestFor(ShopItem? item)
    {
        if (item == null || !TempItems.TryGetValue(item, out int index))
        {
            return null;
        }

        return index >= 0 && index < _crests.Count ? _crests[index] : null;
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
    /// Puts <paramref name="items"/> on the root window and on every nested stock
    /// list, so no list keeps a vanilla array. Original arrays are kept for restore.
    /// </summary>
    private static void SwapStock(ShopItem[] items)
    {
        _swapItems = items;
        if (_root == null)
        {
            return;
        }

        foreach (ShopMenuStock stock in _root.GetComponentsInChildren<ShopMenuStock>(true))
        {
            if (!Swapped.ContainsKey(stock) && StockArrayField?.GetValue(stock) is ShopItem[] original)
            {
                Swapped[stock] = original;
            }

            stock.SetStock(items);
            stock.SetWasItemPurchased(false);
            Plugin.Log.LogInfo(
                $"{StockTrace.Tag} our swap: stock='{Path(stock.transform)}' array={items.Length}" +
                $" items=[{StockTrace.Names(items)}] field={StockTrace.Field(stock)}");
        }
    }

    /// <summary>
    /// Puts our stock back when the game overwrote the field after the window was
    /// opened. Called every frame of the session; a reference check keeps it cheap.
    /// </summary>
    internal static void ReassertStock()
    {
        if (!IsOpen || _root == null || _swapItems.Length == 0)
        {
            return;
        }

        bool lost = false;
        foreach (KeyValuePair<ShopMenuStock, ShopItem[]> pair in Swapped)
        {
            if (pair.Key != null && StockArrayField?.GetValue(pair.Key) != _swapItems)
            {
                lost = true;
                break;
            }
        }

        if (!lost)
        {
            return;
        }

        Plugin.Log.LogWarning($"{StockTrace.Tag} stock overwritten during session, re-applying [{StockTrace.Names(_swapItems)}]");
        SwapStock(_swapItems);
        BuildLists();
        ShopCosmetics.Tick();
        LogStock("reasserted");
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
            Restore("purchase failed");
        }
    }

    /// <summary>
    /// Closes the window the way the game does: <c>CLOSE</c> from <c>Open</c> runs the
    /// vanilla fade-out chain, so borders and prompts are torn down visually. Any other
    /// state falls back to the blunt <see cref="CloseEvent"/>. The restore is left to
    /// the session, which waits for the FSM to reach idle on a later update.
    /// </summary>
    private static void CloseShop()
    {
        if (_control == null)
        {
            Restore("purchase");
            return;
        }

        string state = ActiveState(_control);
        string eventName = state == OpenState && HasEvent(_control, VanillaCloseEvent)
            ? VanillaCloseEvent
            : CloseEvent;
        _sawOpenState = true;
        _control.SendEvent(eventName);
        Plugin.Log.LogInfo($"[SHOP] sent '{eventName}' to '{ControlFsmName}' (state '{state}');" +
            " restore waits for idle.");
    }

    /// <summary>
    /// Remembers the fade group, the window FSM and the two action prompts, then puts
    /// the window back to its visible state, because a previous session left it faded.
    /// </summary>
    private static void BeginTeardown(Transform root)
    {
        _teardownCaptured = true;
        _fade = root.GetComponent<NestedFadeGroupBase>();
        Transform? window = root.Find("Window");
        _windowFsm = window != null ? window.GetComponent<PlayMakerFSM>() : null;
        _cancelPrompt = FindChild(root, "Action Cancel")?.gameObject;
        _confirmPrompt = FindChild(root, "Action Confirm")?.gameObject;
        _reviveLogged = false;
        ReviveTeardown();
    }

    /// <summary>
    /// Chokes the window on the way out: alpha to zero, borders faded down through the
    /// window FSM, prompts hidden. The game's own close path does this; ours is a guard.
    /// </summary>
    private static void TeardownShop()
    {
        if (!_teardownCaptured)
        {
            return;
        }

        float alpha = -1f;
        if (_fade != null)
        {
            _fade.AlphaSelf = 0f;
            alpha = _fade.AlphaSelf;
        }

        bool bordes = false;
        if (_windowFsm != null && HasEvent(_windowFsm, BordesDownEvent))
        {
            _windowFsm.SendEvent(BordesDownEvent);
            bordes = true;
        }

        int prompts = 0;
        if (_cancelPrompt != null)
        {
            _cancelPrompt.SetActive(false);
            prompts++;
        }

        if (_confirmPrompt != null)
        {
            _confirmPrompt.SetActive(false);
            prompts++;
        }

        Plugin.Log.LogInfo($"[SHOP] teardown: alpha={alpha:F2} bordes={bordes} prompts={prompts}");
    }

    /// <summary>
    /// Puts the root fade alpha, the prompts and the window borders back on, so a
    /// window reused after our teardown is visible and interactive again. Only the
    /// root fade group is touched; the children keep their vanilla animation.
    /// </summary>
    private static void ReviveTeardown()
    {
        if (!_teardownCaptured)
        {
            return;
        }

        float alpha = -1f;
        int prompts = 0;
        bool bordes = false;
        try
        {
            if (_fade != null)
            {
                _fade.AlphaSelf = 1f;
                alpha = _fade.AlphaSelf;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] revive alpha failed: {ex.Message}");
        }

        try
        {
            if (_cancelPrompt != null)
            {
                _cancelPrompt.SetActive(true);
                prompts++;
            }

            if (_confirmPrompt != null)
            {
                _confirmPrompt.SetActive(true);
                prompts++;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] revive prompts failed: {ex.Message}");
        }

        try
        {
            if (_windowFsm != null && HasEvent(_windowFsm, BordesUpEvent))
            {
                _windowFsm.SendEvent(BordesUpEvent);
                bordes = true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] revive borders failed: {ex.Message}");
        }

        Plugin.Log.LogInfo($"[SHOP] revive: alpha={alpha:F2} prompts={prompts} bordes={bordes}");
    }

    /// <summary>
    /// Session safety net: the game may fade the reused window back to zero on its
    /// own, so a low root alpha or hidden prompt is revived. One log line per session.
    /// </summary>
    internal static void GuardTeardown()
    {
        if (!IsOpen || !_teardownCaptured)
        {
            return;
        }

        float alpha = _fade != null ? _fade.AlphaSelf : 1f;
        float cancel = PromptAlpha(_cancelPrompt);
        float confirm = PromptAlpha(_confirmPrompt);
        if (alpha > 0.01f && cancel > 0.01f && confirm > 0.01f)
        {
            return;
        }

        ReviveTeardown();
        if (_reviveLogged)
        {
            return;
        }

        _reviveLogged = true;
        Plugin.Log.LogInfo($"[SHOP] revive re-applied alpha={alpha:F2} prompts={PromptsAlive()}");
    }

    /// <summary>Fade alpha of a prompt or its nearest child group; 1 when it has none.</summary>
    private static float PromptAlpha(GameObject? prompt)
    {
        if (prompt == null)
        {
            return 1f;
        }

        NestedFadeGroupBase? group = prompt.GetComponent<NestedFadeGroupBase>() ??
            prompt.GetComponentInChildren<NestedFadeGroupBase>(true);
        return group != null ? group.AlphaSelf : (prompt.activeSelf ? 1f : 0f);
    }

    /// <summary>How many of the two prompts are switched on right now.</summary>
    private static int PromptsAlive()
    {
        int count = 0;
        if (_cancelPrompt != null && _cancelPrompt.activeSelf)
        {
            count++;
        }

        if (_confirmPrompt != null && _confirmPrompt.activeSelf)
        {
            count++;
        }

        return count;
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
        offset.z = 0f;
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

    /// <summary>
    /// Alternative placement: shifts the window so the panel centre lands in the middle
    /// of the HUD viewport, keeping the panel depth. Row fade bounds move with it.
    /// </summary>
    /// <returns>True when the window was moved.</returns>
    internal static bool RecenterIntoViewport()
    {
        try
        {
            if (_root == null)
            {
                return false;
            }

            GameCameras? cams = GameCameras.SilentInstance;
            Camera? cam = cams != null && cams.hudCamera != null ? cams.hudCamera : Camera.main;
            if (cam == null)
            {
                Plugin.Log.LogWarning("[RENDER] recentre skipped: no HUD camera and no Camera.main.");
                return false;
            }

            Transform reference = _root.Find("Window/Shop_Backboard") ?? _root.Find("Window") ?? _root;
            Vector3 panel = reference.position;
            Vector3 viewport = cam.WorldToViewportPoint(panel);
            Vector3 target = cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, viewport.z));
            Vector3 delta = target - panel;
            delta.z = 0f;
            Vector3 before = _root.position;
            _root.position = before + delta;
            _boundShift += delta.y;
            ShiftRowBounds(delta.y);
            Plugin.Log.LogInfo(
                $"[RENDER] recentre root {Vec(before)} -> {Vec(_root.position)}" +
                $" delta={Vec(delta)} panelVpBefore={Vec(viewport)} cam='{cam.name}'");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[RENDER] recentre failed: {ex.Message}");
            return false;
        }
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
    /// <param name="reason">Where the session ended; logged with the input release.</param>
    internal static void Restore(string reason = "closed")
    {
        ShopInputLock.Unblock(reason);
        if (_stock == null && !IsOpen)
        {
            ResetSession();
            return;
        }

        try
        {
            Step("cosmetics", ShopCosmetics.End);
            Step("stock", () =>
            {
                foreach (KeyValuePair<ShopMenuStock, ShopItem[]> pair in Swapped)
                {
                    if (pair.Key != null)
                    {
                        pair.Key.SetStock(pair.Value);
                        pair.Key.SetWasItemPurchased(false);
                    }
                }
            });
            Step("stock lists", () =>
            {
                Swapped.Clear();
                BuildLists();
            });
            Step("teardown", TeardownShop);
            Step("position", () =>
            {
                if (_root != null)
                {
                    ShiftRowBounds(-_boundShift);
                    _root.position = _origPosition;
                }
            });

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

    /// <summary>
    /// Runs one restore step, so a failure stops nothing else. The step name in the
    /// log is the point of this wrapper: a null reference used to abort the whole restore.
    /// </summary>
    private static void Step(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] restore step '{name}' failed: {ex}");
        }
    }

    /// <summary>Clears per-session state before a window is prepared and after it is restored.</summary>
    internal static void ResetSession()
    {
        TempItems.Clear();
        DestroyTempItems();
        Swapped.Clear();
        _swapItems = Array.Empty<ShopItem>();
        _crests = new List<ToolCrest>();
        _stock = null;
        _root = null;
        _control = null;
        _boundShift = 0f;
        _fromDialogue = false;
        _sawOpenState = false;
        SuppressDialogue = false;
        _teardownCaptured = false;
        _reviveLogged = false;
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

    /// <summary>
    /// Read-only dump of the live window stock: every stock component with its item
    /// count and list, then every row. Answers which object feeds the visible items.
    /// </summary>
    /// <param name="label">Moment the dump was taken, written into every line.</param>
    internal static void LogStock(string label)
    {
        try
        {
            if (_root == null)
            {
                Plugin.Log.LogInfo($"[STOCK] {label}: no shop root.");
                return;
            }

            foreach (ShopMenuStock stock in _root.GetComponentsInChildren<ShopMenuStock>(true))
            {
                LogStockComponent(label, stock);
            }

            ShopItemStats[] rows = _root.GetComponentsInChildren<ShopItemStats>(true);
            Plugin.Log.LogInfo($"[STOCK] {label}: rows={rows.Length}");
            foreach (ShopItemStats row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                ShopItem? item = row.Item;
                Plugin.Log.LogInfo(
                    $"[STOCK] {label}: row '{Path(row.transform)}' num={row.ItemNumber}" +
                    $" item='{(item != null ? item.name : "<none>")}' cost={(item != null ? item.Cost : -1)}" +
                    $" activeSelf={row.gameObject.activeSelf} inHierarchy={row.gameObject.activeInHierarchy}");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[STOCK] dump '{label}' failed: {ex.Message}");
        }
    }

    /// <summary>Logs one stock component: count, master list and each available entry.</summary>
    private static void LogStockComponent(string label, ShopMenuStock stock)
    {
        List<ShopItemStats>? list = AvailableStockField?.GetValue(stock) as List<ShopItemStats>;
        Plugin.Log.LogInfo(
            $"[STOCK] {label}: stock '{Path(stock.transform)}' count={stock.GetItemCount()}" +
            $" list={(list != null ? list.Count : -1)}" +
            $" master={(stock.MasterList != null ? Path(stock.MasterList.transform) : "<none>")}");
        if (list == null)
        {
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            ShopItemStats? entry = list[i];
            ShopItem? item = entry != null ? entry.Item : null;
            Plugin.Log.LogInfo(
                $"[STOCK] {label}:   [{i}] row='{(entry != null ? Path(entry.transform) : "<none>")}'" +
                $" name='{(item != null ? item.name : "<none>")}'" +
                $" display='{(item != null ? item.DisplayName : "<none>")}'" +
                $" cost={(item != null ? item.Cost : -1)}");
        }
    }

    /// <summary>Scene path of a transform, for telling the two stock lists apart.</summary>
    internal static string Path(Transform transform)
    {
        string path = transform.name;
        Transform? current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
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

    /// <summary>Frames between window visibility checks.</summary>
    private const int GuardInterval = 30;

    private int _frames;
    private float _deadline;
    private float _openedAt;
    private bool _renderCaptured;
    private bool _stockLogged;

    private void Start()
    {
        _deadline = Time.unscaledTime + TimeoutSeconds;
        _openedAt = Time.unscaledTime;
    }

    private void Update()
    {
        try
        {
            if (_frames++ == 1)
            {
                ShopCrestMenu.LogRows();
            }

            if (!_stockLogged && Time.unscaledTime - _openedAt >= 0.5f)
            {
                _stockLogged = true;
                ShopCrestMenu.LogStock("settled +0.5s");
            }

            if (!_renderCaptured && _frames > 40)
            {
                _renderCaptured = true;
                ShopRenderDiag.Capture("session settled", transform);
            }

            ShopCrestMenu.ReassertStock();
            ShopCosmetics.Tick();
            if (_frames % GuardInterval == 0)
            {
                ShopCrestMenu.GuardTeardown();
            }

            if (ShopCrestMenu.ClosedByFsm())
            {
                Plugin.Log.LogInfo("[SHOP] shop FSM returned to idle, restoring vanilla shop.");
                ShopCrestMenu.Restore("fsm idle");
                Destroy(this);
                return;
            }

            if (Time.unscaledTime > _deadline)
            {
                Plugin.Log.LogWarning("[SHOP] live shop session timed out, restoring vanilla shop.");
                ShopCrestMenu.Restore("timeout");
                Destroy(this);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] live shop session failed: {ex.Message}");
            ShopCrestMenu.Restore("exception");
            Destroy(this);
        }
    }

    private void OnDestroy()
    {
        ShopCrestMenu.Restore("scene unload");
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

/// <summary>
/// Keeps every NPC dialogue out of the way while our shop window is up. The
/// overload with the full argument list is the one the two shorter overloads
/// delegate to, so one prefix covers all three entry points.
/// </summary>
[HarmonyPatch(typeof(DialogueBox), "StartConversation",
    new[] { typeof(string), typeof(NPCControlBase), typeof(bool), typeof(DialogueBox.DisplayOptions), typeof(Action), typeof(Action) })]
internal static class DialogueSuppressPatch
{
    private static bool Prefix(string text, NPCControlBase instigator)
    {
        try
        {
            if (!ShopCrestMenu.SuppressDialogue)
            {
                return true;
            }

            string who = instigator != null ? instigator.name : "<none>";
            Plugin.Log.LogInfo($"[SHOP] dialogue suppressed: instigator='{who}' text='{Short(text)}'");
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] dialogue suppress check failed: {ex.Message}");
            return true;
        }
    }

    private static string Short(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text!.Length <= 60 ? text : text.Substring(0, 60) + "...";
    }
}

/// <summary>
/// Stops the owner from re-applying its vanilla stock over ours: touching
/// <c>ShopObject</c> calls this, and SetStock would replace our temp entries.
/// Skipped only while our live shop session is up; the window itself already exists.
/// </summary>
[HarmonyPatch(typeof(ShopOwnerBase), "SpawnUpdateShop")]
internal static class ShopOwnerStockResetPatch
{
    private static bool Prefix()
    {
        try
        {
            if (ShopCrestMenu.IsOpen)
            {
                Plugin.Log.LogInfo("[STOCK] owner stock re-apply blocked during crest session");
                return false;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[STOCK] owner stock guard failed: {ex.Message}");
        }

        return true;
    }
}
