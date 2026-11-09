using System;
using System.Collections.Generic;
using System.Reflection;
using CrestPicker.Pure;
using HarmonyLib;
using HutongGames.PlayMaker;
using TeamCherry.Localization;
using TeamCherry.NestedFadeGroup;
using TMProOld;
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

    /// <summary>Event the window FSM needs to fade its borders and prompts back in.</summary>
    private const string BordesUpEvent = "FLUERS UP";

    /// <summary>Event the window FSM needs to fade its borders and prompts out.</summary>
    private const string BordesDownEvent = "FLUERS DOWN";

    /// <summary>Idle state of <c>shop_control</c>; the window is closed there.</summary>
    private const string IdleState = "Idle";

    /// <summary>Resting open state of <c>shop_control</c>; the window is fully shown there.</summary>
    private const string OpenState = "Open";

    /// <summary>Prefix of the states that fade the window out.</summary>
    private const string CloseStatePrefix = "Close";

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

    /// <summary>True once reaching the resting open state was logged for this session.</summary>
    private static bool _openReachedLogged;

    /// <summary>True once this session started hiding the schema and the rows, whatever triggered it.</summary>
    private static bool _hideStarted;

    /// <summary>True once the root alpha reached 1 in this session, so a later drop means the close fade.</summary>
    private static bool _windowSeenOpen;

    /// <summary>True once the return to idle was logged for this session.</summary>
    private static bool _closeDetectedLogged;

    /// <summary>Vanilla active state of every object switched off for a close; restored on the next open.</summary>
    private static readonly Dictionary<GameObject, bool> LeftoverStates =
        new Dictionary<GameObject, bool>();

    /// <summary>True once the close switched the leftovers off, so a later restore does not repeat it.</summary>
    private static bool _leftoversHidden;

    /// <summary>Teardown targets and their vanilla state, captured when the window opens.</summary>
    private static NestedFadeGroupBase? _fade;
    private static PlayMakerFSM? _windowFsm;
    private static GameObject? _cancelPrompt;
    private static GameObject? _confirmPrompt;
    private static bool _teardownCaptured;

    /// <summary>Vanilla active state of the two action prompts, captured when the window opens.</summary>
    private static bool _cancelWasActive;
    private static bool _confirmWasActive;

    /// <summary>True after the emergency path hid the prompts, so <see cref="Restore"/> puts them back.</summary>
    private static bool _promptsHidden;

    /// <summary>True after the emergency path asked the window FSM to fade its borders out.</summary>
    private static bool _bordesDown;

    /// <summary>Frames the open window spent with an alpha near zero.</summary>
    private static int _lowAlphaFrames;

    /// <summary>True once the guard raised the alpha in this session.</summary>
    private static bool _guardRaised;

    /// <summary>Monotonic session counter; incremented by <see cref="TryOpen"/>.</summary>
    private static int _session;

    /// <summary>Current session number, captured by every live <see cref="ShopCrestSession"/>.</summary>
    internal static int Session => _session;

    /// <summary>Frame counter of the last diagnostic tick line; zero before the first session frame.</summary>
    private static int _diagFrames;

    /// <summary>Monotonic times of the open steps, for the one timing line.</summary>
    private static float _openStart;
    private static float _stockReady;
    private static float _shopUp;
    private static bool _timingLogged;

    /// <summary>Time of the crest purchase; zero while nothing was bought.</summary>
    private static float _purchaseAt;

    /// <summary>True once the fallback <c>CLOSE SHOP WINDOW</c> was sent after a purchase.</summary>
    private static bool _emergencyClose;

    /// <summary>True once the vanilla purchase message was seen after a purchase.</summary>
    private static bool _messageSeen;

    /// <summary>Seconds from the purchase to the moment the message appeared.</summary>
    private static float _messageShownAt;

    /// <summary>Frame and line counters of the post-purchase FSM trace.</summary>
    private static int _purchaseFrame;
    private static int _purchaseLines;

    /// <summary>True from the moment the close is sent until the full restore finished.</summary>
    internal static bool _closing;

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
        _session++;
        _openStart = Time.realtimeSinceStartup;
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
            _stockReady = Time.realtimeSinceStartup;
            if (stock.GetItemCount() == 0)
            {
                return Fallback("live shop built an empty item list");
            }

            // The open event runs FSM actions that read ShopObject, and that getter
            // re-applies the owner's vanilla stock. Arm the session first so the
            // owner guard holds; ReassertStock() catches a later overwrite anyway.
            IsOpen = true;
            CrestSelectMenu.IsOpen = true;
            ShopInputLock.Block("shop open");

            MoveToHero();
            ReviveTeardown();
            control.SendEvent(OpenEvent);
            _shopUp = Time.realtimeSinceStartup;
            RaiseRootAlpha();
            LogOpenDiag(owner, control);
            int count = stock.GetItemCount();

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

    /// <summary>Crest of a stock row, by that row's index in the available stock list.</summary>
    /// <param name="row">Row index the shop FSM asked about.</param>
    /// <returns>Offered crest, or null for a vanilla row or a bad index.</returns>
    internal static ToolCrest? CrestForRow(int row)
    {
        if (row < 0)
        {
            return null;
        }

        ShopItem? item = null;
        if (_stock != null && AvailableStockField?.GetValue(_stock) is List<ShopItemStats> list && row < list.Count)
        {
            item = list[row] != null ? list[row].Item : null;
        }

        if (item == null && row < _swapItems.Length)
        {
            item = _swapItems[row];
        }

        return CrestFor(item);
    }

    /// <summary>World Y of every available stock row in row-index order; NaN for an absent row.</summary>
    /// <returns>One value per row, empty when no stock is open.</returns>
    internal static float[] RowYs()
    {
        if (_stock == null || AvailableStockField?.GetValue(_stock) is not List<ShopItemStats> list)
        {
            return Array.Empty<float>();
        }

        float[] ys = new float[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            ShopItemStats? row = list[i];
            ys[i] = row != null ? row.transform.position.y : float.NaN;
        }

        return ys;
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
        SetLocalised(item, "displayName", crest.DisplayName);
        SetLocalised(item, "description", crest.Description);
    }

    /// <summary>
    /// Writes a <see cref="LocalisedString"/> into a private field of the same type. A
    /// text field is never written with a plain string: that cast fails, the panel falls
    /// back to the temp key, and the old catch hid it. A type mismatch is logged instead.
    /// </summary>
    /// <param name="item">Shop entry whose text field is written.</param>
    /// <param name="fieldName">Private field of <see cref="ShopItem"/> to write.</param>
    /// <param name="value">Crest text, sheet and key included, so it stays localised.</param>
    private static void SetLocalised(ShopItem item, string fieldName, LocalisedString value)
    {
        FieldInfo? field = typeof(ShopItem).GetField(fieldName, PrivateInstance);
        if (field == null || field.FieldType != typeof(LocalisedString))
        {
            Plugin.Log.LogWarning(
                $"[SHOP] crest text field '{fieldName}' is {field?.FieldType.FullName ?? "<missing>"}," +
                $" expected {typeof(LocalisedString).FullName}; value '{value.Key}' not written");
            return;
        }

        field.SetValue(item, value);
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
            ShopCrestSchema.ConfirmActive = true;
            CrestPickerService.ApplyCrest(crest);
            Plugin.OnMenuClosed(true, _fromDialogue);
            BeginPurchaseWait();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[SHOP] temp purchase handler failed: {ex}");
            Restore("purchase failed", true);
        }
    }

    /// <summary>
    /// Starts the post-purchase wait. The vanilla confirm FSM closes the window on its
    /// own; <see cref="TickPurchase"/> only watches, and steps in when it does not.
    /// </summary>
    private static void BeginPurchaseWait()
    {
        _purchaseAt = Time.realtimeSinceStartup;
        _purchaseFrame = 0;
        _purchaseLines = 0;
        _emergencyClose = false;
        _messageSeen = false;
        _messageShownAt = 0f;
        string state = _control != null ? ActiveState(_control) : "<none>";
        Plugin.Log.LogInfo($"[SHOP] purchase done, leaving the close to the vanilla FSM (state='{state}')");
    }

    /// <summary>
    /// Watches the window after a purchase: logs the FSM state, and closes the window
    /// 0.5 s after the vanilla purchase message appears (3 s fallback if it never does).
    /// </summary>
    /// <param name="frames">Frames the session has been running, for the trace spacing.</param>
    internal static void TickPurchase(int frames)
    {
        if (_purchaseAt <= 0f || _control == null)
        {
            return;
        }

        try
        {
            string state = ActiveState(_control);
            _purchaseFrame++;
            if (_purchaseFrame % 10 == 0 && _purchaseLines < 30)
            {
                _purchaseLines++;
                Plugin.Log.LogInfo($"[SHOP] after purchase state='{state}'");
            }

            float elapsed = Time.realtimeSinceStartup - _purchaseAt;

            if (!_messageSeen && ShopCosmetics.PurchaseMessageShown)
            {
                _messageSeen = true;
                _messageShownAt = elapsed;
                Plugin.Log.LogInfo($"[SHOP] purchase message shown at {elapsed:F2}s, closing in 0.5s");
            }

            if (state == IdleState && _emergencyClose)
            {
                Plugin.Log.LogInfo($"[SHOP] window closed {elapsed:F2}s after purchase");
                _purchaseAt = 0f;
                return;
            }

            switch (PurchaseClose.Decide(elapsed, _messageSeen, _messageShownAt, _emergencyClose))
            {
                case CloseDecision.CloseWindow when IsOpen && _control != null:
                    _emergencyClose = true;
                    _control.SendEvent(CloseEvent);
                    string why = _messageSeen ? "message + 0.5s" : "3s fallback";
                    Plugin.Log.LogInfo($"[SHOP] sent '{CloseEvent}' {elapsed:F2}s after purchase ({why})");
                    break;

                case CloseDecision.ForceIdle:
                    _purchaseAt = 0f;
                    ForceIdle($"'{state}' 5s after purchase");
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] purchase wait failed: {ex.Message}");
        }
    }

    /// <summary>Puts <c>shop_control</c> back to idle and restores the vanilla window.</summary>
    /// <param name="reason">What forced the state, written to the log.</param>
    private static void ForceIdle(string reason)
    {
        try
        {
            _control?.Fsm?.SetState(IdleState);
            ShopTrace.Begin("forced idle");
            Plugin.Log.LogWarning($"[SHOP] forced state '{IdleState}' ({reason})");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] forced idle failed: {ex.Message}");
        }

        Restore("stuck", true);
    }

    /// <summary>
    /// Remembers the fade group, the window FSM and the two action prompts with their
    /// vanilla state, so nothing of ours has to guess what the FSM wants. The root alpha
    /// is not touched: the vanilla open chain owns it.
    /// </summary>
    private static void BeginTeardown(Transform root)
    {
        RestoreLeftovers();
        _teardownCaptured = true;
        _fade = root.GetComponent<NestedFadeGroupBase>();
        Transform? window = root.Find("Window");
        _windowFsm = window != null ? window.GetComponent<PlayMakerFSM>() : null;
        _cancelPrompt = FindChild(root, "Action Cancel")?.gameObject;
        _confirmPrompt = FindChild(root, "Action Confirm")?.gameObject;
        _cancelWasActive = _cancelPrompt != null && _cancelPrompt.activeSelf;
        _confirmWasActive = _confirmPrompt != null && _confirmPrompt.activeSelf;
        _guardRaised = false;
        _lowAlphaFrames = 0;
        RestorePrompts();
    }

    /// <summary>Reads the root fade alpha, falling back to <paramref name="fallback"/>.</summary>
    internal static float SafeAlpha(float fallback)
    {
        try
        {
            return _fade != null ? _fade.AlphaSelf : fallback;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] alpha read failed: {ex.Message}");
            return fallback;
        }
    }

    /// <summary>
    /// Emergency only: puts the fade alpha back to the vanilla resting value and hides
    /// the two prompts, because the vanilla close chain never ran. The plane path
    /// touches nothing here - the FSM fades the window and its prompts itself.
    /// </summary>
    private static void TeardownShop(bool emergency)
    {
        if (!_teardownCaptured || !emergency)
        {
            if (_teardownCaptured)
            {
                Plugin.Log.LogInfo("[SHOP] teardown skipped: vanilla FSM owns the fade.");
            }

            return;
        }

        int prompts = 0;
        try
        {
            prompts = HidePrompts();
        }
        catch (Exception ex)
        {
            TeardownFailure("prompts", ex);
        }

        bool bordes = false;
        try
        {
            bordes = BordersDown();
        }
        catch (Exception ex)
        {
            TeardownFailure("bordes", ex);
        }

        float alpha = -1f;
        try
        {
            alpha = FadeOut();
        }
        catch (Exception ex)
        {
            TeardownFailure("alpha", ex);
            _fade = null;
        }

        Plugin.Log.LogInfo($"[SHOP] teardown done emergency prompts={prompts} bordes={bordes} alpha={alpha:F2}");
    }

    /// <summary>
    /// Writes nothing to the root alpha. A raise the guard made in this session is only
    /// written off on the next open, never pulled down to zero: a zero written into a
    /// window that is already being reopened becomes its new resting alpha.
    /// </summary>
    private static void RestoreAlpha()
    {
        if (!_teardownCaptured)
        {
            return;
        }

        if (!_guardRaised)
        {
            return;
        }

        _guardRaised = false;
        Plugin.Log.LogInfo("[SHOP] alpha was raised this session");
    }

    /// <summary>Switches both action prompts off; returns how many were touched.</summary>
    private static int HidePrompts()
    {
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

        _promptsHidden = prompts > 0;
        return prompts;
    }

    /// <summary>
    /// Puts the prompts back to their captured vanilla state only when our own teardown
    /// switched them off, i.e. on the emergency path. In a normal close the vanilla chain
    /// owns them and is left alone. A prompt already recorded by the close leftovers is
    /// skipped: it is restored on the next open instead.
    /// </summary>
    private static void RestorePrompts()
    {
        if (!_promptsHidden)
        {
            return;
        }

        try
        {
            if (_cancelPrompt != null && !LeftoverStates.ContainsKey(_cancelPrompt) &&
                _cancelPrompt.activeSelf != _cancelWasActive)
            {
                _cancelPrompt.SetActive(_cancelWasActive);
            }

            if (_confirmPrompt != null && !LeftoverStates.ContainsKey(_confirmPrompt) &&
                _confirmPrompt.activeSelf != _confirmWasActive)
            {
                _confirmPrompt.SetActive(_confirmWasActive);
            }

            _promptsHidden = false;
            bool leftovers = (_cancelPrompt != null && LeftoverStates.ContainsKey(_cancelPrompt)) ||
                (_confirmPrompt != null && LeftoverStates.ContainsKey(_confirmPrompt));
            Plugin.Log.LogInfo(leftovers
                ? "[SHOP] prompts stay hidden until the next open"
                : $"[SHOP] prompts restored cancel={_cancelWasActive} confirm={_confirmWasActive}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] prompt restore failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Switches off the two action prompts and the whole item list group once the close
    /// starts or the window begins to fade: the vanilla fade never reaches the selector,
    /// the rows or the vanilla prompt text, so they would outlive the window. Their vanilla
    /// active state is recorded and written back by <see cref="RestoreLeftovers"/> once the
    /// fade is done.
    /// </summary>
    /// <param name="duringFade">True when the close fade itself asked for the hide, false at the FSM close start.</param>
    private static void HideLeftovers(bool duringFade = false)
    {
        LogCloseStart();
        int prompts = 0;
        if (HideLeftover(_cancelPrompt))
        {
            prompts++;
        }

        if (HideLeftover(_confirmPrompt))
        {
            prompts++;
        }

        bool listGroup = HideLeftover(ListGroup()?.gameObject);
        _leftoversHidden = prompts > 0 || listGroup;
        Plugin.Log.LogInfo(duringFade
            ? $"[SHOP] leftovers hidden at fade listGroup={listGroup} prompts={prompts}"
            : $"[SHOP] leftovers hidden at close start listGroup={listGroup} prompts={prompts}");
    }

    /// <summary>Turns one object off and records its vanilla active state; false when absent.</summary>
    private static bool HideLeftover(GameObject? target)
    {
        if (target == null)
        {
            return false;
        }

        if (!LeftoverStates.ContainsKey(target))
        {
            LeftoverStates[target] = target.activeSelf;
        }

        target.SetActive(false);
        return true;
    }

    /// <summary>Container of the shop rows and the selector; null when the window has none.</summary>
    private static Transform? ListGroup()
    {
        if (_root == null)
        {
            return null;
        }

        return _root.Find("Item List Group") ?? FindChild(_root, "Item List Group");
    }

    /// <summary>Puts every object switched off for a close back to the state it had before it.</summary>
    private static void RestoreLeftovers()
    {
        LogRestoreDiag();
        foreach (KeyValuePair<GameObject, bool> pair in LeftoverStates)
        {
            if (pair.Key != null && pair.Key.activeSelf != pair.Value)
            {
                pair.Key.SetActive(pair.Value);
            }
        }

        LeftoverStates.Clear();
        _leftoversHidden = false;
    }

    /// <summary>
    /// Puts the nodes the close start switched off back as soon as the window is fully
    /// faded out. The vanilla shop reuses this same window, so leaving them off until the
    /// next open of ours left a vanilla trader with an empty frame.
    /// </summary>
    internal static void RestoreLeftoversAfterFade()
    {
        if (!_leftoversHidden)
        {
            return;
        }

        float alpha = SafeAlpha(1f);
        bool faded = alpha < 0.01f;
        bool idle = _control != null && ActiveState(_control) == IdleState && alpha < 0.05f;
        if (!faded && !idle)
        {
            return;
        }

        RestoreLeftovers();
        Plugin.Log.LogInfo($"[SHOP] leftovers restored after fade alpha={alpha:F2}");
    }

    /// <summary>Asks the window FSM to fade its borders out; false when it has no such event.</summary>
    private static bool BordersDown()
    {
        if (_windowFsm == null || !HasEvent(_windowFsm, BordesDownEvent))
        {
            return false;
        }

        _windowFsm.SendEvent(BordesDownEvent);
        _bordesDown = true;
        return true;
    }

    /// <summary>Sets the root fade alpha to zero, emergency path only; -1 when there is none.</summary>
    private static float FadeOut()
    {
        if (_fade == null)
        {
            return -1f;
        }

        _fade.AlphaSelf = 0f;
        return _fade.AlphaSelf;
    }

    private static void TeardownFailure(string step, Exception ex)
    {
        Plugin.Log.LogWarning($"[SHOP] teardown step '{step}' failed: {ex.GetType().Name}: {ex.Message}");
    }

    /// <summary>
    /// Puts the captions the vanilla chain may have left off back on, and only the borders
    /// if our emergency teardown sent them down. The root alpha is never written here: the
    /// vanilla open chain raises it on its own, and a leftover value would become the new
    /// resting alpha of the window.
    /// </summary>
    private static void ReviveTeardown()
    {
        if (!_teardownCaptured)
        {
            return;
        }

        float alpha = SafeAlpha(-1f);
        bool bordes = false;

        try
        {
            if (_bordesDown && _windowFsm != null && HasEvent(_windowFsm, BordesUpEvent))
            {
                _windowFsm.SendEvent(BordesUpEvent);
                _bordesDown = false;
                bordes = true;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] revive borders failed: {ex.Message}");
        }

        Plugin.Log.LogInfo($"[SHOP] revive: alpha={alpha:F2} bordes={bordes}");
    }

    /// <summary>
    /// Session safety net: the vanilla chain sometimes leaves a reused window faded out
    /// while the FSM rests in <see cref="OpenState"/>. After 60 such frames the root alpha
    /// is raised once, and only once per session, and only while the window is open, not
    /// closing and not already hiding for a close; <see cref="RestoreAlpha"/> clears the
    /// guard raise on close.
    /// </summary>
    internal static void GuardTeardown()
    {
        if (_closing || _hideStarted || !IsOpen || !_teardownCaptured || _fade == null)
        {
            return;
        }

        // IsOpen stays true through the close fade, so the state decides: only the resting
        // open state wants a visible window. A raised alpha during the fade brought the rows
        // and the prompts back right after they were hidden.
        if (_control == null || ActiveState(_control) != OpenState)
        {
            _lowAlphaFrames = 0;
            return;
        }

        float alpha = SafeAlpha(1f);
        if (alpha >= 0.01f)
        {
            _lowAlphaFrames = 0;
            return;
        }

        if (++_lowAlphaFrames < 60 || _guardRaised)
        {
            return;
        }

        try
        {
            if (_fade == null || _fade.gameObject == null)
            {
                return;
            }

            _fade.AlphaSelf = 1f;
            _guardRaised = true;
            Plugin.Log.LogInfo($"[SHOP] guard raised alpha (was {alpha:F2})");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] guard alpha raise failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Raises the root alpha once when the window opens. Every row, the selector and both
    /// prompts hang off this group, and a reused window can start the session at zero; the
    /// vanilla open chain keeps tweening the value afterwards.
    /// </summary>
    private static void RaiseRootAlpha()
    {
        try
        {
            if (_fade == null || _fade.gameObject == null)
            {
                return;
            }

            float before = SafeAlpha(0f);
            _fade.AlphaSelf = 1f;
            Plugin.Log.LogInfo($"[SHOP] root alpha raised at open was={before:F2}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] root alpha raise failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Logs the one open timing line once the root alpha reached 1, i.e. when the vanilla
    /// open chain finished fading the window in.
    /// </summary>
    /// <param name="frames">Frames the session has been running.</param>
    internal static void CheckOpenTiming(int frames)
    {
        if (_timingLogged || !_teardownCaptured)
        {
            return;
        }

        float alpha = SafeAlpha(0f);
        if (alpha < 0.99f)
        {
            return;
        }

        _timingLogged = true;
        float now = Time.realtimeSinceStartup;
        Plugin.Log.LogInfo($"[SHOP] timing: open->stock={( _stockReady - _openStart) * 1000f:F0}ms" +
            $" open->SHOPUP={(_shopUp - _openStart) * 1000f:F0}ms" +
            $" open->alpha1={(now - _openStart) * 1000f:F0}ms frames={frames}");
        ShopRenderDiag.CapturePostOpen();
    }

    /// <summary>
    /// Logs the root alpha and the FSM state for the first 60 frames after the open, one
    /// line every 10 frames: these lines say whether the vanilla tween raises the root.
    /// </summary>
    /// <param name="frames">Frames the session has been running.</param>
    internal static void TraceRootAlpha(int frames)
    {
        if (frames > 60 || frames % 10 != 0)
        {
            return;
        }

        try
        {
            string state = _control != null ? ActiveState(_control) : "<none>";
            Plugin.Log.LogInfo($"[SHOP] rootAlpha={SafeAlpha(-1f):F2} state='{state}' frames={frames}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] root alpha trace failed: {ex.Message}");
        }
    }

    /// <summary>One line per open: who owns the stock and what the reused window starts as.</summary>
    /// <param name="owner">Live owner whose window is being reused.</param>
    /// <param name="control">FSM that drives the window.</param>
    private static void LogOpenDiag(ShopOwnerBase owner, PlayMakerFSM control)
    {
        try
        {
            Transform? listGroup = ListGroup();
            Plugin.Log.LogInfo(
                $"[SHOPDIAG] open rootAlpha={AlphaOf(_root):F2} listGroupActive={ActiveOf(listGroup)}" +
                $" listGroupAlpha={AlphaOf(listGroup):F2} prompts={PromptFlags()}" +
                $" listChildren={ListChildren()} stockOwner='{owner.name}' state='{ActiveState(control)}'");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPDIAG] open diag failed: {ex.Message}");
        }
    }

    /// <summary>One line per hide: the flags that decide whether the restore ever runs.</summary>
    private static void LogCloseStart()
    {
        try
        {
            Transform? listGroup = ListGroup();
            Plugin.Log.LogInfo(
                $"[SHOPDIAG] closeStart flags(hide={_hideStarted} leftovers={_leftoversHidden}" +
                $" leftStates={LeftoverStates.Count} seenOpen={_windowSeenOpen})" +
                $" rootAlpha={AlphaOf(_root):F2} listGroupActive={ActiveOf(listGroup)}" +
                $" listGroupAlpha={AlphaOf(listGroup):F2} listChildren={ListChildren()}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPDIAG] close diag failed: {ex.Message}");
        }
    }

    /// <summary>One line per restore: what is put back and what the window looks like now.</summary>
    private static void LogRestoreDiag()
    {
        try
        {
            Transform? listGroup = ListGroup();
            Plugin.Log.LogInfo(
                $"[SHOPDIAG] restore flags(leftovers={_leftoversHidden}" +
                $" leftStates={LeftoverStates.Count}) listGroupActive={ActiveOf(listGroup)}" +
                $" listGroupAlpha={AlphaOf(listGroup):F2} prompts={PromptFlags()}" +
                $" rootAlpha={AlphaOf(_root):F2}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPDIAG] restore diag failed: {ex.Message}");
        }
    }

    /// <summary>One line per session reset: the flags that must not leak into the next session.</summary>
    private static void LogResetDiag()
    {
        try
        {
            Plugin.Log.LogInfo(
                $"[SHOPDIAG] resetSession hide={_hideStarted} leftovers={_leftoversHidden}" +
                $" seenOpen={_windowSeenOpen} teardown={_teardownCaptured}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPDIAG] reset diag failed: {ex.Message}");
        }
    }

    /// <summary>
    /// One tick line every 30 frames while the window is ours, plus the first frame and
    /// the close: it shows which code path writes the root and the list group alpha.
    /// </summary>
    /// <param name="frames">Frames the session has been running.</param>
    internal static void TraceDiagTick(int frames)
    {
        if (!IsOpen)
        {
            return;
        }

        _diagFrames = frames;
        if (frames != 1 && frames % 30 != 0)
        {
            return;
        }

        LogTickDiag(frames);
    }

    private static void LogTickDiag(int frames)
    {
        try
        {
            Transform? listGroup = ListGroup();
            string state = _control != null ? ActiveState(_control) : "<none>";
            Plugin.Log.LogInfo(
                $"[SHOPDIAG] tick frames={frames} rootAlpha={AlphaOf(_root):F2}" +
                $" listGroupAlpha={AlphaOf(listGroup):F2} listGroupActive={ActiveOf(listGroup)}" +
                $" state='{state}'");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOPDIAG] tick diag failed: {ex.Message}");
        }
    }

    /// <summary>Effective alpha of the fade group on a node; -1 when the node has none.</summary>
    private static float AlphaOf(Transform? node)
    {
        if (node == null)
        {
            return -1f;
        }

        NestedFadeGroupBase? group = node.GetComponent<NestedFadeGroupBase>();
        return group != null ? group.AlphaTotal : -1f;
    }

    /// <summary>True when the node exists and is switched on.</summary>
    private static bool ActiveOf(Transform? node)
    {
        return node != null && node.gameObject.activeSelf;
    }

    /// <summary>Active state of the action prompts as cancel/confirm.</summary>
    private static string PromptFlags()
    {
        bool cancel = _cancelPrompt != null && _cancelPrompt.activeSelf;
        bool confirm = _confirmPrompt != null && _confirmPrompt.activeSelf;
        return $"{cancel}/{confirm}";
    }

    /// <summary>Child count of the row container; -1 when it is absent.</summary>
    private static int ListChildren()
    {
        Transform? list = _root != null ? FindChild(_root, "Item List") : null;
        return list != null ? list.childCount : -1;
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

        string state = ActiveState(_control);
        if (state != IdleState)
        {
            _sawOpenState = true;
            if (state == OpenState && !_openReachedLogged)
            {
                _openReachedLogged = true;
                Plugin.Log.LogInfo($"[SHOP] schema visible through open (state='{OpenState}')");
            }

            return false;
        }

        if (_sawOpenState && !_closeDetectedLogged)
        {
            _closeDetectedLogged = true;
            Plugin.Log.LogInfo($"[SHOP] close detected state='{state}'");
        }

        return _sawOpenState;
    }

    /// <summary>
    /// Hides both schemas when the FSM reaches a terminal close state.
    /// The schema lives outside the fade hierarchy, so the vanilla close fade never
    /// reaches it; the fade alpha is the earlier trigger, see <see cref="HideSchemaOnFade"/>.
    /// The open chain passes through several states before it rests, so a state that
    /// merely differs from the state at the open event is not a close.
    /// </summary>
    internal static void HideSchemaAtCloseStart()
    {
        if (!IsOpen || !_sawOpenState || _control == null || _hideStarted)
        {
            return;
        }

        string state = ActiveState(_control);
        bool terminal = state == IdleState || state.StartsWith(CloseStatePrefix, StringComparison.Ordinal);
        if (state.Length == 0 || !terminal)
        {
            return;
        }

        _hideStarted = true;
        ShopCrestSchema.Hide();
        HideLeftovers();
        Plugin.Log.LogInfo($"[SHOP] schema hidden at close start state='{state}'");
    }

    /// <summary>
    /// Hides the schema as soon as the window starts fading, whatever the FSM says: the
    /// schema lives outside the fade hierarchy and the window is gone before the FSM reaches
    /// a terminal state. The rows follow for the same reason. Needs a fully shown window in
    /// this session first, so the fade-in itself is not taken for a close.
    /// </summary>
    internal static void HideSchemaOnFade()
    {
        if (_hideStarted || !IsOpen || !_teardownCaptured)
        {
            return;
        }

        float alpha = SafeAlpha(1f);
        if (alpha >= 0.99f)
        {
            _windowSeenOpen = true;
            return;
        }

        if (!_windowSeenOpen || alpha >= 0.5f)
        {
            return;
        }

        _hideStarted = true;
        ShopCrestSchema.Hide();
        HideLeftovers(true);
        Plugin.Log.LogInfo($"[SHOP] window fading, hiding schema and rows alpha={alpha:F2}");
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
    /// <param name="emergency">
    /// True when the vanilla close chain never ran, so the fade and the prompts must be
    /// torn down here. The normal path leaves both to the FSM.
    /// </param>
    /// <param name="session">
    /// Session number of the caller, or -1 to skip the check. A late call from an old
    /// session must not touch a window that was already reopened.
    /// </param>
    internal static void Restore(string reason = "closed", bool emergency = false, int session = -1)
    {
        // The confirmation lives no longer than the window: every restore ends it.
        ShopCrestSchema.ConfirmActive = false;

        // A newer session already owns the window: this call is a leftover. Same for a
        // call with no live session and nothing left to put back.
        bool foreign = IsOpen && session >= 0 && session != _session;
        bool idle = !IsOpen && _stock == null;
        if (foreign || idle)
        {
            if (!IsOpen)
            {
                ShopInputLock.Unblock(reason);
            }

            RestorePrompts();
            Plugin.Log.LogInfo($"[SHOP] restore skipped, no session of ours (reason={reason} ours={session} now={_session} open={IsOpen})");
            return;
        }

        _closing = true;
        LogTickDiag(_diagFrames);
        ShopCrestSchema.Hide();

        // The session is over: the window goes back to the vanilla shop, which needs the
        // nodes the close start switched off. Hiding them here again is what left the
        // vanilla trader with an empty frame until the next open of ours.
        Step("leftovers", RestoreLeftovers);
        ShopInputLock.Unblock(reason);
        if (_stock == null)
        {
            IsOpen = false;
            RestorePrompts();
            ResetSession();
            _closing = false;
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
            Step("teardown", () => TeardownShop(emergency));
            Step("alpha", RestoreAlpha);
            Step("prompts", RestorePrompts);
            Step("position", () =>
            {
                if (_root != null)
                {
                    ShiftRowBounds(-_boundShift);
                    _root.position = _origPosition;
                }
            });

            Plugin.Log.LogInfo($"[SHOP] live shop restored to vanilla stock and position (reason={reason}).");
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
            _closing = false;
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
        LogResetDiag();
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
        _openReachedLogged = false;
        _hideStarted = false;
        _windowSeenOpen = false;
        _closeDetectedLogged = false;
        SuppressDialogue = false;
        _teardownCaptured = false;
        _closing = false;
        _cancelWasActive = true;
        _confirmWasActive = true;
        _guardRaised = false;
        _promptsHidden = false;
        _lowAlphaFrames = 0;
        _timingLogged = false;
        _diagFrames = 0;
        _purchaseAt = 0f;
        _emergencyClose = false;
        _purchaseFrame = 0;
        _purchaseLines = 0;
        _fade = null;
        _windowFsm = null;
        _cancelPrompt = null;
        _confirmPrompt = null;
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
            for (int i = 0; i < rows.Length; i++)
            {
                ShopItemStats row = rows[i];
                TMP_Text? label = ShopCosmetics.RowLabel(row);
                if (label != null)
                {
                    label.ForceMeshUpdate();
                }

                Renderer? renderer = label != null ? label.GetComponent<Renderer>() : null;
                string text = label != null ? (label.text ?? string.Empty) : "<none>";
                string font = label != null && label.font != null ? label.font.name : "<none>";
                string size = renderer != null ? Vec(renderer.bounds.size) : "<none>";
                string labelOn = label != null ? label.enabled.ToString() : "<none>";
                string rendererOn = renderer != null ? renderer.enabled.ToString() : "<none>";
                string alpha = label != null ? label.color.a.ToString("F2") : "<none>";
                string fontSize = label != null ? label.fontSize.ToString("F2") : "<none>";
                Plugin.Log.LogInfo(
                    $"[SHOP]   row{i} y={row.transform.position.y:F2}" +
                    $" top={Float(TopYField, row):F2} bot={Float(BotYField, row):F2}" +
                    $" active={row.gameObject.activeSelf}" +
                    $" label='{text}' len={text.Length} font='{font}' size={size}" +
                    $" labelOn={labelOn} rendererOn={rendererOn}" +
                    $" alpha={alpha} fontSize={fontSize}" +
                    $" item='{(row.Item != null ? row.Item.name : "<none>")}'" +
                    $" cost={(row.Item != null ? row.GetCost().ToString() : "<none>")}");
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

    private int _frames;
    private int _session = -1;
    private float _deadline;
    private float _openedAt;
    private bool _renderCaptured;
    private bool _stockLogged;

    private void Start()
    {
        _deadline = Time.unscaledTime + TimeoutSeconds;
        _openedAt = Time.unscaledTime;
        _session = ShopCrestMenu.Session;
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
            ShopCrestMenu.HideSchemaOnFade();
            ShopCrestMenu.GuardTeardown();
            ShopCrestMenu.TraceRootAlpha(_frames);
            ShopCrestMenu.TraceDiagTick(_frames);
            ShopCrestMenu.CheckOpenTiming(_frames);
            ShopCrestMenu.TickPurchase(_frames);

            if (ShopCrestMenu.IsOpen)
            {
                ShopCrestMenu.HideSchemaAtCloseStart();
            }

            ShopCrestMenu.RestoreLeftoversAfterFade();

            if (ShopCrestMenu.ClosedByFsm())
            {
                Plugin.Log.LogInfo("[SHOP] shop FSM returned to idle, restoring vanilla shop.");
                ShopCrestMenu.Restore("fsm idle", false, _session);
                Destroy(this);
                return;
            }

            if (Time.unscaledTime > _deadline)
            {
                Plugin.Log.LogWarning("[SHOP] live shop session timed out, restoring vanilla shop.");
                ShopCrestMenu.Restore("timeout", false, _session);
                Destroy(this);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] live shop session failed: {ex.Message}");
            ShopCrestMenu.Restore("exception", false, _session);
            Destroy(this);
        }
    }

    private void LateUpdate()
    {
        ShopCosmetics.LateTick();
    }

    private void OnDestroy()
    {
        ShopCrestMenu.Restore("scene unload", false, _session);
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
