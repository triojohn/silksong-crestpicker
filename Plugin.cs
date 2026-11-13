using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CrestPicker.Pure;
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

    /// <summary>When true, the maid chain (wave and call) may run; the save-crest gate still applies.</summary>
    internal static ConfigEntry<bool> GreetEnabled = null!;

    /// <summary>Normal crests the mod found in the save slots; written by the mod once per session.</summary>
    internal static ConfigEntry<int> SaveCrestCount = null!;

    /// <summary>Manual value that replaces <see cref="SaveCrestCount"/> when it is 0 or more.</summary>
    internal static ConfigEntry<int> SaveCrestCountOverride = null!;

    /// <summary>Distance at which the maid greets the hero, in world units.</summary>
    internal static ConfigEntry<float> GreetRadius = null!;

    /// <summary>Distance the hero must leave by before the maid may greet again, in world units.</summary>
    internal static ConfigEntry<float> GreetExitRadius = null!;

    /// <summary>Sprite animation clip played as the wave.</summary>
    internal static ConfigEntry<string> GreetClip = null!;

    /// <summary>Kept for old configs; the prayer loop no longer plays a second clip.</summary>
    internal static ConfigEntry<string> GreetClipEnd = null!;

    /// <summary>Every Nth repeat of the prayer clip the maid sings a line.</summary>
    internal static ConfigEntry<int> SingEveryLoops = null!;

    /// <summary>First line the maid says before the crest offer; empty skips it.</summary>
    internal static ConfigEntry<string> OfferLine1 = null!;

    /// <summary>Second line the maid says before the crest offer; empty skips it.</summary>
    internal static ConfigEntry<string> OfferLine2 = null!;

    /// <summary>Third line the maid says before the crest offer; empty skips it.</summary>
    internal static ConfigEntry<string> OfferLine3 = null!;

    /// <summary>When true, the picker offers every normal crest regardless of save state.</summary>
    internal static ConfigEntry<bool> IgnoreUnlockState = null!;

    /// <summary>When true, the currently equipped crest stays in the picker list.</summary>
    internal static ConfigEntry<bool> IncludeEquippedCrest = null!;

    /// <summary>
    /// When true, the chain runs even if the offer was already used in this save slot;
    /// the flag from <c>state.json</c> is ignored. For testing.
    /// </summary>
    internal static ConfigEntry<bool> IgnoreAlreadyOffered = null!;

    /// <summary>Seconds to wait for a playable state before dropping an offer.</summary>
    private const float OfferGrace = 2f;

    private Harmony? _harmony;

    private ConfigEntry<bool> _grantAllCrests = null!;
    private ConfigEntry<CrestPoolMode> _crestPool = null!;
    private ConfigEntry<bool> _enableSnailChoice = null!;
    private ConfigEntry<bool> _useLiveShop = null!;
    private ConfigEntry<KeyboardShortcut> _testHotkey = null!;
    private ConfigEntry<KeyboardShortcut> _shopDumpHotkey = null!;
    private ConfigEntry<KeyboardShortcut> _spriteDumpHotkey = null!;
    private ConfigEntry<KeyboardShortcut> _ownWindowHotkey = null!;
    private ConfigEntry<KeyboardShortcut> _npcDumpHotkey = null!;
    private ConfigEntry<bool> _showCrestSchema = null!;
    private ConfigEntry<SchemaRenderMode> _schemaRender = null!;
    private ConfigEntry<float> _crestSchemaScale = null!;
    private ConfigEntry<float> _crestSchemaOffsetY = null!;
    private ConfigEntry<float> _crestDotSize = null!;
    private ConfigEntry<float> _crestLockedDotSize = null!;
    private ConfigEntry<float> _crestDotOffsetY = null!;
    private ConfigEntry<string> _confirmText = null!;
    private ConfigEntry<string> _confirmButtonText = null!;
    private ConfigEntry<string> _purchaseText = null!;
    private ConfigEntry<bool> _shrinkCrestName = null!;
    private ConfigEntry<string> _textCentreSource = null!;
    private ConfigEntry<bool> _rowLabelDebugAscii = null!;
    private ConfigEntry<float> _rowLabelOffsetX = null!;
    private ConfigEntry<float> _rowLabelOffsetY = null!;

    private bool _loggedAlive;

    private static float _offerDeadline;
    private static bool _offerFromDialogue;

    private void Awake()
    {
        Log = Logger;

        Probe.Enabled = Config.Bind("Probe", "Enabled", false,
            "Log dialogue FSMs of early scenes and the collectable/tool name lists.").Value;

        _grantAllCrests = Config.Bind("General", "GrantAllCrests", false,
            "Устаревший ключ: true трактуется как CrestPool=All");
        _crestPool = Config.Bind("General", "CrestPool", CrestPoolMode.FromSaves,
            "Пул знаков для выбора: FromSaves — открытые в любом слоте сейва минус открытые " +
            "в текущем прохождении, All — все нормальные, Unlocked — только открытые в текущем сейве");
        IgnoreUnlockState = Config.Bind("General", "IgnoreUnlockState", false,
            "Устаревший ключ: true — показывать все нормальные знаки независимо от сейва, " +
            "false — фильтровать по CrestPool. При CrestPool=FromSaves игнорируется. Читается на лету.");
        IncludeEquippedCrest = Config.Bind("General", "IncludeEquippedCrest", true,
            "Оставлять надетый знак в списке выбора. true — знак не вырезается, " +
            "false — прежнее поведение (надетый знак исключён). Читается на лету.");
        IgnoreAlreadyOffered = Config.Bind("General", "IgnoreAlreadyOffered", true,
            "Игнорировать отметку «уже предлагали в этом слоте» из state.json: true — " +
            "цепочка срабатывает при каждом подходе (для тестов), false — строгое поведение. " +
            "Читается на лету.");
        _enableSnailChoice = Config.Bind("General", "EnableSnailChoice", true,
            "Предлагать выбор знака после диалога с прислужницей капеллы");
        _testHotkey = Config.Bind("General", "TestHotkey", new KeyboardShortcut(KeyCode.F9),
            "Клавиша тестового выбора знака");
        _shopDumpHotkey = Config.Bind("General", "ShopDumpHotkey", new KeyboardShortcut(KeyCode.F8),
            "Клавиша дампа живого магазина (объекты, FSM, строки)");
        _spriteDumpHotkey = Config.Bind("General", "SpriteDumpHotkey", new KeyboardShortcut(KeyCode.F10),
            "Клавиша дампа спрайтов гребней в PNG (C:\\Users\\trioj\\Downloads\\silksong_sprites)");
        _ownWindowHotkey = Config.Bind("General", "OwnWindowHotkey", new KeyboardShortcut(KeyCode.F7),
            "Клавиша спавна собственного окна магазина (клон префаба, ничего не открывает)");
        _npcDumpHotkey = Config.Bind("General", "NpcDumpHotkey", new KeyboardShortcut(KeyCode.F5),
            "Клавиша дампа прислужницы Bonetown (объекты, FSM, клипы, флаги)");
        SaveCrestCount = Config.Bind("General", "SaveCrestCount", 0,
            "Сколько нормальных гребней мод нашёл в сейвах. Значение пишет мод сам при старте " +
            "после чтения слотов; править вручную не нужно (см. SaveCrestCountOverride).");
        SaveCrestCountOverride = Config.Bind("General", "SaveCrestCountOverride", -1,
            "Ручное значение вместо автоматического SaveCrestCount: если >= 0, берётся оно " +
            "и автоматическая запись не делается. -1 — брать автоматическое значение. " +
            "Читается на лету.");
        GreetEnabled = Config.Bind("General", "GreetEnabled", true,
            "Общий выключатель цепочки прислужницы: машет и окликает Хорнет при подходе, " +
            "если мод нашёл в сейвах больше гребней, чем открыто в текущем прохождении. " +
            "По умолчанию включено. Читается на лету.");
        GreetRadius = Config.Bind("General", "GreetRadius", 7f,
            "Радиус приветствия прислужницы в мировых единицах. Читается на лету.");
        GreetExitRadius = Config.Bind("General", "GreetExitRadius", 9f,
            "Радиус, за которым сбрасывается оклик: чтобы прислужница снова окликнула, " +
            "нужно отойти дальше этого расстояния. Если меньше GreetRadius, берётся GreetRadius. " +
            "Читается на лету.");
        GreetClip = Config.Bind("General", "GreetClip", "Pray",
            "Клип аниматора прислужницы для молитвы. Читается на лету.");
        GreetClipEnd = Config.Bind("General", "GreetClipEnd", "Pray End",
            "Не используется: молитва идёт повтором GreetClip. Ключ оставлен для старых конфигов.");
        SingEveryLoops = Config.Bind("General", "SingEveryLoops", 2,
            "Каждый N-й повтор клипа молитвы прислужница поёт (CM_sing_01/02 по кругу). " +
            "1 — каждый повтор. Читается на лету.");
        OfferLine1 = Config.Bind("General", "Line1", "You carry more than one crest, don't you?",
            "Одна фраза прислужницы перед выбором знака (первая); пусто — фразы нет. " +
            "Читается на лету.");
        OfferLine2 = Config.Bind("General", "Line2", "The others remember what this shell has forgotten.",
            "Одна фраза прислужницы перед выбором знака (вторая); пусто — фразы нет. " +
            "Читается на лету.");
        OfferLine3 = Config.Bind("General", "Line3", "Take one. Choose well.",
            "Одна фраза прислужницы перед выбором знака (третья); пусто — фразы нет. " +
            "Читается на лету.");
        if (GreetClip.Value == "Bonking")
        {
            GreetClip.Value = "Pray";
            Logger.LogInfo("[GREET] GreetClip migrated Bonking -> Pray");
        }

        if (Mathf.Approximately(GreetRadius.Value, 12f))
        {
            GreetRadius.Value = 7f;
            Logger.LogInfo("[GREET] GreetRadius migrated 12 -> 7");
        }

        if (!GreetEnabled.Value)
        {
            Logger.LogInfo("[GREET] disabled by config");
        }
        _useLiveShop = Config.Bind("General", "UseLiveShop", true,
            "Показывать выбор знака в настоящем окне магазина (сток и позиция восстанавливаются)");
        _showCrestSchema = Config.Bind("General", "ShowCrestSchema", true,
            "Рисовать схему знака (силуэт и цветные точки слотов) в панели деталей магазина");
        _schemaRender = Config.Bind("General", "SchemaRender", SchemaRenderMode.Clone,
            "Способ отрисовки схемы знака: clone — клон живой карточки нужного гребня с " +
            "принудительным «невыбранным» видом (основной путь), sprite — силуэт из данных гребня " +
            "плюс спрайты slotTypeSprite из templateSlots, generated — силуэт плюс сгенерированные " +
            "кружки.");
        _crestSchemaScale = Config.Bind("General", "CrestSchemaScale", 1f,
            "Множитель единого масштаба схемы знака: все гребни рисуются в одном масштабе от " +
            "эталонной высоты, схема занимает 80% панели деталей (1 — ровно вписано)");
        _crestSchemaOffsetY = Config.Bind("General", "CrestSchemaOffsetY", -0.12f,
            "Сдвиг схемы знака по вертикали как доля высоты панели деталей " +
            "(минус — вниз, чтобы схема не наезжала на описание)");
        _crestDotSize = Config.Bind("General", "CrestDotSize", 0.373f,
            "Диаметр точки слота как доля эталонной высоты силуэта знака (1 — вся высота, " +
            "0.373 — как слот невыбранной карточки в ванили: 1.267 / 3.400)");
        _crestLockedDotSize = Config.Bind("General", "CrestLockedDotSize", 0.174f,
            "Диаметр точки заблокированного слота как доля эталонной высоты силуэта " +
            "(0.174 — как заблокированный слот в ванили: 0.59 / 3.400)");
        _crestDotOffsetY = Config.Bind("General", "CrestDotOffsetY", -0.02f,
            "Смещение точек слотов по вертикали как доля высоты силуэта (минус — вниз)");
        _confirmText = Config.Bind("General", "ConfirmText", "Вы уверены?",
            "Текст подтверждения покупки знака");
        _confirmButtonText = Config.Bind("General", "ConfirmButtonText", "Выбрать",
            "Текст кнопки подтверждения покупки знака");
        _purchaseText = Config.Bind("General", "PurchaseText", "Знак получен",
            "Текст сообщения о покупке знака в окне подтверждения");
        _shrinkCrestName = Config.Bind("General", "ShrinkCrestNameToFit", true,
            "Уменьшать шрифт названия знака, чтобы оно влезло в строку");
        _rowLabelDebugAscii = Config.Bind("General", "RowLabelDebugAscii", false,
            "Диагностика: писать в метку строки 'AAAA <индекс>' латиницей вместо имени знака. " +
            "Отделяет проблему шрифта и глифов от проблемы размещения и рендера.");
        _rowLabelOffsetX = Config.Bind("General", "RowLabelOffsetX", 0f,
            "Смещение названия знака по X от иконки строки, в мировых единицах. " +
            "Читается каждый кадр.");
        _rowLabelOffsetY = Config.Bind("General", "RowLabelOffsetY", 0f,
            "Смещение названия знака по Y от иконки строки, в мировых единицах (минус — вниз). " +
            "Читается каждый кадр.");
        _textCentreSource = Config.Bind("General", "TextCentreSource", "columns",
            "Источник цели для центрирования названия знака: columns (середина между левым краем " +
            "Window/Shop_Backboard и левым краем Item Details), column (середина между левым краем " +
            "Item List и левым краем Item Details), columnCentre (между левым краем Item Details " +
            "и правым краем окна), rows (среднее X строк), " +
            "list (Item List), listGroup (Item List Group), backboard (Window/Shop_Backboard), " +
            "window (Window), original (не двигать текст). Читается каждый кадр.");

        CrestPoolMode poolMode = _crestPool.Value;
        if (!_grantAllCrests.Value && poolMode == CrestPoolMode.All && IgnoreUnlockState.Value)
        {
            poolMode = CrestPoolMode.FromSaves;
            _crestPool.Value = poolMode;
            Logger.LogInfo("[CONFIG] pool mode migrated to FromSaves");
        }

        CrestPickerService.PoolMode = _grantAllCrests.Value ? CrestPoolMode.All : poolMode;
        EnableSnailChoice = _enableSnailChoice.Value;
        UseLiveShop = _useLiveShop.Value;
        ShopCrestSchema.Enabled = _showCrestSchema.Value;
        ShopCrestSchema.Render = _schemaRender.Value;
        ShopCrestSchema.Scale = ConfigRules.NormalizeSchemaScale(_crestSchemaScale.Value);
        ShopCrestSchema.OffsetY = _crestSchemaOffsetY.Value;
        float dotSizeRaw = _crestDotSize.Value;
        ShopCrestSchema.DotSize = ConfigRules.NormalizeDotSize(dotSizeRaw);
        ShopCrestSchema.LockedDotSize = ConfigRules.NormalizeLockedDotSize(_crestLockedDotSize.Value);
        ShopCrestSchema.DotOffsetY = _crestDotOffsetY.Value;
        if (ShopCrestSchema.DotSize != dotSizeRaw)
        {
            Logger.LogWarning($"[SCHEMA] CrestDotSize={dotSizeRaw:F3}" +
                $" looks like legacy multiplier, using {ShopCrestSchema.DotSize:F3}");
        }
        ShopCosmetics.ConfirmText = _confirmText.Value;
        ShopCosmetics.ConfirmButtonText = _confirmButtonText.Value;
        ShopCosmetics.PurchaseText = _purchaseText.Value;
        ShopCosmetics.ShrinkNameToFit = _shrinkCrestName.Value;
        ShopCosmetics.DebugAsciiRowLabel = _rowLabelDebugAscii.Value;
        ShopCosmetics.RowLabelOffsetX = _rowLabelOffsetX;
        ShopCosmetics.RowLabelOffsetY = _rowLabelOffsetY;
        ShopCosmetics.CentreSourceConfig = _textCentreSource;
        Logger.LogInfo(CrestPickerService.PoolMode == CrestPoolMode.FromSaves
            ? "[POOL] mode=FromSaves (IgnoreUnlockState ignored)."
            : $"[POOL] mode={CrestPickerService.PoolMode}" +
              $" (IgnoreUnlockState={IgnoreUnlockState.Value}, GrantAllCrests={_grantAllCrests.Value}," +
              $" CrestPool={_crestPool.Value}).");
        CrestSelectMenu.Closed += OnMenuClosed;
        CrestSelectMenu.RunSelfCheck();
        CrestOfferState.RunSelfCheck();

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        try
        {
            AlphaTrace.Apply(_harmony);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[ALPHATRACE] apply failed: {ex.Message}");
        }

        gameObject.AddComponent<ShopWatch>();

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void Update()
    {
        if (!_loggedAlive)
        {
            _loggedAlive = true;
            Log.LogInfo($"[CREST] Update alive (gameState={DescribeGameState()}).");
        }

        InventoryDiag.Poll();
        OfferGateRuntime.Tick();
        UpdatePendingOffer();
        OwnShopWindow.Tick();
        ChurchkeeperGreeting.Tick();
        ShopCrestMenu.TickOpenRetry();

        if (_shopDumpHotkey.Value.IsDown())
        {
            Log.LogInfo($"[CREST] Hotkey {_shopDumpHotkey.Value} pressed: shop dump.");
            ShopDiag.DumpShopNpc();
            ShopDiag.DumpLiveShop();
            ShopTrace.Begin("hotkey");
            ShopRenderDiag.Capture("hotkey", null);
        }

        if (_ownWindowHotkey.Value.IsDown())
        {
            Log.LogInfo($"[OWN] Hotkey {_ownWindowHotkey.Value} pressed: spawn shop window clone.");
            OwnShopWindow.TrySpawn();
        }

        if (_npcDumpHotkey.Value.IsDown())
        {
            Log.LogInfo($"[NPC] Hotkey {_npcDumpHotkey.Value} pressed: churchkeeper dump.");
            ChurchkeeperDiag.Dump();
        }

        if (_spriteDumpHotkey.Value.IsDown())
        {
            Log.LogInfo($"[DUMP] Hotkey {_spriteDumpHotkey.Value} pressed: crest sprite dump.");
            SpriteDump.DumpAll();
        }

        if (!IsInGame() || CrestSelectMenu.IsOpen)
        {
            return;
        }

        if (_testHotkey.Value.IsDown())
        {
            Log.LogInfo($"[CREST] Hotkey {_testHotkey.Value} pressed.");
            QueueOpen(fromDialogue: false);
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

            bool alreadyOffered = CrestOfferState.IsOffered(slot);
            bool ignoreOffered = IgnoreAlreadyOffered.Value;
            if (OfferTalkFlow.ShouldSkipOffer(alreadyOffered, ignoreOffered))
            {
                Log.LogInfo($"[CREST] Offer skipped: already used in slot {slot}.");
                return;
            }

            if (alreadyOffered)
            {
                Log.LogInfo("[CREST] Offer alreadyUsed ignored=True");
            }

            QueueOpen(fromDialogue: true);
            Log.LogInfo($"[CREST] Offer requested for slot {slot}.");
        }
        catch (Exception ex)
        {
            Log.LogError($"[CREST] RequestOffer failed: {ex}");
        }
    }

    /// <summary>
    /// Defers an open until the cross-save pool is settled: the read is started here so the
    /// window never opens on the all-normals fallback while the slots are still loading.
    /// </summary>
    /// <param name="fromDialogue">True when the maid dialogue asked for the picker.</param>
    private static void QueueOpen(bool fromDialogue)
    {
        CrestPoolFromSaves.Begin(CurrentSlotId());
        _offerFromDialogue = fromDialogue;
        _offerDeadline = Time.unscaledTime + OfferGrace;
    }

    /// <summary>Opens a pending offer once the game is playable; drops it after a grace period.</summary>
    private void UpdatePendingOffer()
    {
        CrestPoolFromSaves.Tick();

        if (_offerDeadline <= 0f || CrestSelectMenu.IsOpen)
        {
            return;
        }

        // While the other save slots are being read the offer stays pending: the read
        // finishes (or times out to the all-normals fallback) inside its own 2s budget.
        bool savesPending = CrestPickerService.PoolMode == CrestPoolMode.FromSaves
            && CrestPoolFromSaves.State == CrestPoolFromSaves.LoadState.Loading;

        if (savesPending)
        {
            return;
        }

        if (IsInGame())
        {
            switch (CrestSelectMenu.TryOpen(fromDialogue: _offerFromDialogue))
            {
                case CrestSelectMenu.OpenResult.Opened:
                    _offerDeadline = 0f;
                    return;

                case CrestSelectMenu.OpenResult.NoPool:
                    _offerDeadline = 0f;
                    return;
            }
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
    internal static int CurrentSlotId()
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
        CrestPoolFromSaves.Reset();
        CrestSelectMenu.Closed -= OnMenuClosed;
        OwnShopWindow.RestoreVanilla();
        _harmony?.UnpatchSelf();
    }
}
