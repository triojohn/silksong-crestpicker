using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GlobalEnums;
using HarmonyLib;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// BepInEx entry point for the CrestPicker mod: after the chapel maid dialogue at
/// Bonetown/Churchkeeper the player gets to pick a crest instead of the vanilla one.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("Hollow Knight Silksong.exe")]
public sealed class Plugin : BaseUnityPlugin
{
    /// <summary>Unique plugin identifier.</summary>
    public const string PluginGuid = "io.github.triojohn.crestpicker";

    /// <summary>Human-readable plugin name.</summary>
    public const string PluginName = "CrestPicker";

    /// <summary>Semantic version of the plugin.</summary>
    public const string PluginVersion = "1.0.0";

    /// <summary>Shared log source for static patch code.</summary>
    internal static ManualLogSource Log = null!;

    /// <summary>When true, the chapel maid dialogue offers the crest choice.</summary>
    internal static bool EnableSnailChoice;

    /// <summary>
    /// When true, the picker reuses the live shop window of the scene instead of
    /// the custom menu. Vanilla stock and position are restored afterwards.
    /// </summary>
    internal static bool UseLiveShop;

    /// <summary>Seconds to wait for a playable state before dropping an offer.</summary>
    private const float OfferGrace = 2f;

    private Harmony? _harmony;

    private ConfigEntry<bool> _grantAllCrests = null!;
    private ConfigEntry<CrestPoolMode> _crestPool = null!;
    private ConfigEntry<bool> _enableSnailChoice = null!;
    private ConfigEntry<bool> _useLiveShop = null!;
    private ConfigEntry<KeyboardShortcut> _testHotkey = null!;
    private ConfigEntry<KeyboardShortcut> _shopDumpHotkey = null!;
    private ConfigEntry<bool> _showCrestSchema = null!;
    private ConfigEntry<float> _crestSchemaScale = null!;
    private ConfigEntry<float> _crestDotSize = null!;
    private ConfigEntry<float> _confirmSchemaScale = null!;
    private ConfigEntry<string> _confirmText = null!;
    private ConfigEntry<string> _confirmButtonText = null!;
    private ConfigEntry<bool> _shrinkCrestName = null!;
    private ConfigEntry<string> _textCentreSource = null!;

    private bool _loggedAlive;

    private static float _offerDeadline;

    private void Awake()
    {
        Log = Logger;

        Probe.Enabled = Config.Bind("Probe", "Enabled", false,
            "Log dialogue FSMs of early scenes and the collectable/tool name lists.").Value;

        _grantAllCrests = Config.Bind("General", "GrantAllCrests", false,
            "Устаревший ключ: true трактуется как CrestPool=All");
        _crestPool = Config.Bind("General", "CrestPool", CrestPoolMode.All,
            "Пул знаков для выбора: All — все 7 нормальных, Unlocked — только открытые в сейве");
        _enableSnailChoice = Config.Bind("General", "EnableSnailChoice", true,
            "Предлагать выбор знака после диалога с прислужницей капеллы");
        _testHotkey = Config.Bind("General", "TestHotkey", new KeyboardShortcut(KeyCode.F9),
            "Клавиша тестового выбора знака");
        _shopDumpHotkey = Config.Bind("General", "ShopDumpHotkey", new KeyboardShortcut(KeyCode.F8),
            "Клавиша дампа живого магазина (объекты, FSM, строки)");
        _useLiveShop = Config.Bind("General", "UseLiveShop", true,
            "Показывать выбор знака в настоящем окне магазина (сток и позиция восстанавливаются)");
        _showCrestSchema = Config.Bind("General", "ShowCrestSchema", true,
            "Рисовать схему знака (силуэт и цветные точки слотов) в панели деталей магазина");
        _crestSchemaScale = Config.Bind("General", "CrestSchemaScale", 3f,
            "Множитель размера схемы знака в панели деталей (схема подтверждения вдвое больше)");
        _crestDotSize = Config.Bind("General", "CrestDotSize", 0.11f,
            "Диаметр точки слота как доля высоты силуэта знака");
        _confirmSchemaScale = Config.Bind("General", "ConfirmSchemaScale", 1f,
            "Размер схемы на экране подтверждения относительно правой панели");
        _confirmText = Config.Bind("General", "ConfirmText", "Вы уверены?",
            "Текст подтверждения покупки знака");
        _confirmButtonText = Config.Bind("General", "ConfirmButtonText", "Выбрать",
            "Текст кнопки подтверждения покупки знака");
        _shrinkCrestName = Config.Bind("General", "ShrinkCrestNameToFit", true,
            "Уменьшать шрифт названия знака, чтобы оно влезло в строку");
        _textCentreSource = Config.Bind("General", "TextCentreSource", "rowBars",
            "Источник цели для центрирования названия знака: rowBars (среднее X центров строк товаров, " +
            "та же система координат, что у текста), column (середина между левым краем " +
            "Item List и левым краем Item Details), columnCentre (между левым краем Item Details " +
            "и правым краем окна), rows (среднее X строк), " +
            "list (Item List), listGroup (Item List Group), backboard (Window/Shop_Backboard), " +
            "window (Window), original (не двигать текст). Читается каждый кадр.");

        CrestPickerService.PoolMode = _grantAllCrests.Value ? CrestPoolMode.All : _crestPool.Value;
        EnableSnailChoice = _enableSnailChoice.Value;
        UseLiveShop = _useLiveShop.Value;
        ShopCrestSchema.Enabled = _showCrestSchema.Value;
        ShopCrestSchema.Scale = _crestSchemaScale.Value;
        float dotSizeRaw = _crestDotSize.Value;
        ShopCrestSchema.DotSizeRaw = dotSizeRaw;
        ShopCrestSchema.DotSize = dotSizeRaw > 0.5f ? dotSizeRaw * 0.11f : dotSizeRaw;
        if (dotSizeRaw > 0.5f)
        {
            Logger.LogWarning($"[SCHEMA] CrestDotSize={dotSizeRaw:F3}" +
                $" looks like legacy multiplier, using {ShopCrestSchema.DotSize:F3}");
        }
        ShopCrestSchema.ConfirmScale = _confirmSchemaScale.Value;
        ShopCosmetics.ConfirmText = _confirmText.Value;
        ShopCosmetics.ConfirmButtonText = _confirmButtonText.Value;
        ShopCosmetics.ShrinkNameToFit = _shrinkCrestName.Value;
        ShopCosmetics.CentreSource = _textCentreSource;
        Logger.LogInfo($"[CREST] crest pool mode: {CrestPickerService.PoolMode}" +
            $" (GrantAllCrests={_grantAllCrests.Value}, CrestPool={_crestPool.Value}).");
        CrestSelectMenu.Closed += OnMenuClosed;
        CrestSelectMenu.RunSelfCheck();
        CrestOfferState.RunSelfCheck();

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void Update()
    {
        if (!_loggedAlive)
        {
            _loggedAlive = true;
            Log.LogInfo($"[CREST] Update alive (gameState={DescribeGameState()}).");
        }

        UpdatePendingOffer();

        if (_shopDumpHotkey.Value.IsDown())
        {
            Log.LogInfo($"[CREST] Hotkey {_shopDumpHotkey.Value} pressed: shop dump.");
            ShopDiag.DumpShopNpc();
            ShopDiag.DumpLiveShop();
            ShopTrace.Begin("hotkey");
            ShopRenderDiag.Capture("hotkey", null);
        }

        if (!IsInGame() || CrestSelectMenu.IsOpen)
        {
            return;
        }

        if (_testHotkey.Value.IsDown())
        {
            Log.LogInfo($"[CREST] Hotkey {_testHotkey.Value} pressed.");
            CrestSelectMenu.TryOpen();
        }
    }

    /// <summary>
    /// Called by the FSM action when the maid dialogue ends. Shows the picker once
    /// per save slot unless it was already used, cancelled offers retry later.
    /// </summary>
    internal static void RequestOffer()
    {
        try
        {
            if (!EnableSnailChoice)
            {
                return;
            }

            if (CrestSelectMenu.IsOpen)
            {
                Log.LogInfo("[CREST] Offer skipped: picker already open.");
                return;
            }

            int slot = CurrentSlotId();
            if (slot < 0)
            {
                Log.LogWarning("[CREST] Offer skipped: save slot id is unknown.");
                return;
            }

            if (CrestOfferState.IsOffered(slot))
            {
                Log.LogInfo($"[CREST] Offer skipped: already used in slot {slot}.");
                return;
            }

            _offerDeadline = Time.unscaledTime + OfferGrace;
            Log.LogInfo($"[CREST] Offer requested for slot {slot}.");
        }
        catch (Exception ex)
        {
            Log.LogError($"[CREST] RequestOffer failed: {ex}");
        }
    }

    /// <summary>Opens a pending offer once the game is playable; drops it after a grace period.</summary>
    private void UpdatePendingOffer()
    {
        if (_offerDeadline <= 0f || CrestSelectMenu.IsOpen)
        {
            return;
        }

        if (IsInGame() && CrestSelectMenu.TryOpen(fromDialogue: true))
        {
            _offerDeadline = 0f;
            return;
        }

        if (Time.unscaledTime > _offerDeadline)
        {
            _offerDeadline = 0f;
            Log.LogWarning($"[CREST] Offer dropped: game not ready (state={DescribeGameState()}).");
        }
    }

    /// <summary>Consumes the one-time flag only for a real selection made by the dialogue offer.</summary>
    internal static void OnMenuClosed(bool selected, bool fromDialogue)
    {
        if (!selected || !fromDialogue)
        {
            return;
        }

        int slot = CurrentSlotId();
        CrestOfferState.MarkOffered(slot);
        Log.LogInfo($"[CREST] Offer marked as used for slot {slot}.");
    }

    /// <summary>Game save slot index, taken from the fields the save system uses.</summary>
    private static int CurrentSlotId()
    {
        GameManager? manager = GameManager.instance;
        if (manager != null && manager.profileID >= 0)
        {
            return manager.profileID;
        }

        PlayerData? data = PlayerData.instance;
        return data != null ? data.profileID : -1;
    }

    /// <summary>Current game state for diagnostics, never throws.</summary>
    private static string DescribeGameState()
    {
        GameManager? manager = GameManager.instance;
        return manager == null ? "no GameManager" : manager.GameState.ToString();
    }

    /// <summary>True only while a loaded scene is being played.</summary>
    private static bool IsInGame()
    {
        GameManager? manager = GameManager.instance;
        return manager != null && manager.GameState == GameState.PLAYING;
    }

    private void OnDestroy()
    {
        CrestSelectMenu.Closed -= OnMenuClosed;
        _harmony?.UnpatchSelf();
    }
}
