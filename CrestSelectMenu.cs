using System;
using System.Collections.Generic;
using TMProOld;
using UnityEngine;
using UnityEngine.UI;

namespace CrestPicker;

/// <summary>
/// In-game crest picker built from scratch: a full-screen dim layer with a centred
/// list of crests, driven by the game's own input actions, menu sounds and hero
/// input blockers.
/// </summary>
internal sealed class CrestSelectMenu : MonoBehaviour
{
    private const float RowHeight = 40f;
    private const float RowSpacing = 8f;
    private const float RowPadding = 12f;
    private const float IconSize = 40f;
    private const float IconSpacing = 10f;
    private const float TitleHeight = 56f;
    private const float LabelFontSize = 32f;
    private const float TitleFontSize = 40f;
    private const string TitleLabel = "Выберите знак";
    private const string KeepCurrentLabel = "Оставить как есть";

    private static readonly Color SelectedColor = Color.white;
    private static readonly Color IdleColor = new Color(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Color SelectedBackdrop = new Color(1f, 1f, 1f, 0.15f);
    private static readonly Color ClearBackdrop = new Color(1f, 1f, 1f, 0f);
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.55f);

    /// <summary>One selectable line: its label and the backdrop that marks the cursor.</summary>
    private sealed class Row
    {
        internal TMP_Text Text = null!;
        internal Image Backdrop = null!;
    }

    private readonly List<Row> _rows = new List<Row>();
    private List<ToolCrest> _crests = new List<ToolCrest>();
    private int _index;
    private bool _closed;
    private bool _heroBlocked;
    private bool _fromDialogue;

    /// <summary>True while a picker menu (custom or cloned shop) exists in the scene.</summary>
    internal static bool IsOpen { get; set; }

    /// <summary>
    /// Raised when a picker closes. Args: true when a crest was selected, and true
    /// when the picker was opened by the dialogue offer rather than the test hotkey.
    /// </summary>
    internal static event Action<bool, bool>? Closed;

    /// <summary>
    /// Builds and shows the picker when the game is ready. Never throws.
    /// </summary>
    /// <param name="fromDialogue">True when opened by the maid dialogue offer.</param>
    /// <returns>True when the menu was created and is on screen.</returns>
    internal static bool TryOpen(bool fromDialogue = false)
    {
        if (IsOpen)
        {
            return false;
        }

        try
        {
            List<ToolCrest> crests = CrestPickerService.GetSelectableCrests(out bool waiting);
            if (crests.Count == 0)
            {
                if (waiting)
                {
                    Plugin.Log.LogInfo("[CREST] crest pool is still loading, menu not opened.");
                }
                else
                {
                    Plugin.Log.LogWarning("[CREST] No selectable crests to show, menu not opened.");
                }

                return false;
            }

            bool deferred = false;
            if (Plugin.UseLiveShop)
            {
                // A deferred open is a success for this call: the custom menu must not
                // take over while the clone's FSM is still starting.
                if (ShopCrestMenu.TryOpen(crests, fromDialogue, out deferred) || deferred)
                {
                    return true;
                }
            }

            return OpenCustom(crests, fromDialogue);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CREST] Failed to open the crest menu: {ex}");
            IsOpen = false;
            return false;
        }
    }

    /// <summary>
    /// Builds the custom crest menu. Used when the live window is switched off, has
    /// really failed, or its open attempt timed out. Never throws.
    /// </summary>
    /// <param name="crests">Crests to show; must not be empty.</param>
    /// <param name="fromDialogue">True when the choice came from the maid dialogue.</param>
    /// <returns>True when the menu was created and is on screen.</returns>
    internal static bool OpenCustom(List<ToolCrest> crests, bool fromDialogue)
    {
        try
        {
            UIManager? ui = UIManager.instance;
            if (ui == null || ui.UICanvas == null)
            {
                Plugin.Log.LogWarning("[CREST] UIManager or UICanvas is unavailable, menu not opened.");
                return false;
            }

            GameObject root = new GameObject("CrestPickerMenu",
                typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            root.transform.SetParent(ui.UICanvas.transform, false);
            root.transform.SetAsLastSibling();

            CrestSelectMenu menu = root.AddComponent<CrestSelectMenu>();
            menu.Init(crests, fromDialogue);
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CREST] Failed to open the crest menu: {ex}");
            IsOpen = false;
            return false;
        }
    }

    /// <summary>
    /// Wraps a list index after a move by <paramref name="delta"/>.
    /// </summary>
    /// <param name="current">Current index; may be out of range.</param>
    /// <param name="delta">Movement, negative for up.</param>
    /// <param name="count">Number of entries; 0 yields 0.</param>
    /// <returns>Index inside <c>[0, count)</c>.</returns>
    internal static int NextIndex(int current, int delta, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        int next = (current + delta) % count;
        return next < 0 ? next + count : next;
    }

    /// <summary>
    /// Logs the result of a few known-value navigation checks. Returns true on success.
    /// </summary>
    internal static bool RunSelfCheck()
    {
        bool ok =
            NextIndex(0, -1, 7) == 6 &&
            NextIndex(6, 1, 7) == 0 &&
            NextIndex(3, 1, 7) == 4 &&
            NextIndex(3, -1, 7) == 2 &&
            NextIndex(0, 0, 7) == 0 &&
            NextIndex(5, 1, 0) == 0;

        Plugin.Log.LogInfo($"[CREST] Navigation self-check {(ok ? "passed" : "FAILED")}.");
        return ok;
    }

    private void Init(List<ToolCrest> crests, bool fromDialogue)
    {
        _crests = crests;
        _fromDialogue = fromDialogue;

        RectTransform rect = (RectTransform)transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image dim = GetComponent<Image>();
        dim.color = DimColor;
        dim.raycastTarget = false;

        VerticalLayoutGroup layout = GetComponent<VerticalLayoutGroup>();
        layout.spacing = RowSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        layout.padding = new RectOffset(0, 0, 0, 0);

        TMP_FontAsset? font = FindGameFont();
        CreateTitle(transform, font);

        for (int i = 0; i < crests.Count; i++)
        {
            _rows.Add(CreateRow(transform, i, crests[i], font));
        }

        _rows.Add(CreateTextRow(transform, "KeepCurrent", KeepCurrentLabel, font));

        _index = 0;
        RefreshHighlight();
        IsOpen = true;
        BlockHero();

        Plugin.Log.LogInfo(
            $"[CREST] Menu opened: {crests.Count} crests, font=" +
            (font != null ? font.name : "TMP default") + ".");
    }

    /// <summary>Picks the font of a live in-game text so labels match the game UI.</summary>
    private static TMP_FontAsset? FindGameFont()
    {
        try
        {
            TMP_Text[] texts = Resources.FindObjectsOfTypeAll<TMP_Text>();
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text candidate = texts[i];
                if (candidate != null && candidate.gameObject.activeInHierarchy && candidate.font != null)
                {
                    return candidate.font;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CREST] Game font lookup failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>Adds the menu heading above the crest list.</summary>
    private static void CreateTitle(Transform parent, TMP_FontAsset? font)
    {
        GameObject go = new GameObject("CrestTitle", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        LayoutElement element = go.GetComponent<LayoutElement>();
        element.minHeight = TitleHeight;
        element.preferredHeight = TitleHeight;
        element.flexibleHeight = 0f;

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = TitleLabel;
        text.font = font != null ? font : TMP_FontAsset.defaultFontAsset;
        text.fontSize = TitleFontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
    }

    /// <summary>Adds one crest line: optional icon on the left, name to its right.</summary>
    private static Row CreateRow(Transform parent, int index, ToolCrest crest, TMP_FontAsset? font)
    {
        Row row = CreateRowShell(parent, $"CrestRow{index}");

        Sprite? sprite = CrestSprite(crest);
        if (sprite != null)
        {
            CreateIcon(row, sprite);
        }

        row.Text = CreateLabel(row, "Name", CrestPickerService.GetLabel(crest), font, LabelFontSize);
        return row;
    }

    /// <summary>Adds a plain text line without an icon; used for the keep-current entry.</summary>
    private static Row CreateTextRow(Transform parent, string name, string label, TMP_FontAsset? font)
    {
        Row row = CreateRowShell(parent, name);
        row.Text = CreateLabel(row, "Name", label, font, LabelFontSize);
        return row;
    }

    /// <summary>Builds the row container: backdrop image plus horizontal flow for its content.</summary>
    private static Row CreateRowShell(Transform parent, string name)
    {
        GameObject go = new GameObject(name,
            typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        LayoutElement element = go.GetComponent<LayoutElement>();
        element.minHeight = RowHeight;
        element.preferredHeight = RowHeight;
        element.flexibleHeight = 0f;

        Image backdrop = go.GetComponent<Image>();
        backdrop.color = ClearBackdrop;
        backdrop.raycastTarget = false;

        HorizontalLayoutGroup flow = go.GetComponent<HorizontalLayoutGroup>();
        flow.spacing = IconSpacing;
        flow.childAlignment = TextAnchor.MiddleCenter;
        flow.childControlHeight = true;
        flow.childControlWidth = true;
        flow.childForceExpandHeight = false;
        flow.childForceExpandWidth = false;
        flow.padding = new RectOffset((int)RowPadding, (int)RowPadding, 0, 0);

        return new Row { Backdrop = backdrop };
    }

    private static void CreateIcon(Row row, Sprite sprite)
    {
        GameObject go = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(row.Backdrop.transform, false);

        LayoutElement element = go.GetComponent<LayoutElement>();
        element.minWidth = IconSize;
        element.preferredWidth = IconSize;
        element.minHeight = IconSize;
        element.preferredHeight = IconSize;
        element.flexibleWidth = 0f;

        Image icon = go.GetComponent<Image>();
        icon.sprite = sprite;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
    }

    private static TMP_Text CreateLabel(
        Row row, string name, string label, TMP_FontAsset? font, float fontSize)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(row.Backdrop.transform, false);

        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.font = font != null ? font : TMP_FontAsset.defaultFontAsset;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>Crest sprite lookup that never throws; null means the row has no icon.</summary>
    private static Sprite? CrestSprite(ToolCrest crest)
    {
        try
        {
            return crest.CrestSprite;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CREST] CrestSprite lookup failed for '{crest.name}': {ex.Message}");
            return null;
        }
    }

    private void Update()
    {
        if (_closed)
        {
            return;
        }

        try
        {
            HandleInput();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CREST] Menu input failed: {ex}");
            Close(apply: false);
        }
    }

    private void HandleInput()
    {
        InputHandler? handler = ManagerSingleton<InputHandler>.Instance;
        if (handler == null)
        {
            return;
        }

        HeroActions? actions = handler.inputActions;
        if (actions == null)
        {
            return;
        }

        if (actions.Up.WasPressed)
        {
            Move(-1);
            return;
        }

        if (actions.Down.WasPressed)
        {
            Move(1);
            return;
        }

        Platform? platform = Platform.Current;
        if (platform == null)
        {
            if (actions.MenuSubmit.WasPressed)
            {
                Submit();
            }
            else if (actions.MenuCancel.WasPressed)
            {
                Close(apply: false);
            }

            return;
        }

        switch (platform.GetMenuAction(actions))
        {
            case Platform.MenuActions.Submit:
                Submit();
                break;
            case Platform.MenuActions.Cancel:
                Close(apply: false);
                break;
        }
    }

    private void Move(int delta)
    {
        int next = NextIndex(_index, delta, _rows.Count);
        if (next == _index)
        {
            return;
        }

        _index = next;
        RefreshHighlight();
        PlaySelectSound();
    }

    /// <summary>Confirms the cursor: applies a crest, or closes untouched on the last row.</summary>
    private void Submit()
    {
        if (_index >= 0 && _index < _crests.Count)
        {
            Close(apply: true);
            return;
        }

        Close(apply: false);
    }

    private void RefreshHighlight()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            Row row = _rows[i];
            bool selected = i == _index;
            row.Text.color = selected ? SelectedColor : IdleColor;
            row.Backdrop.color = selected ? SelectedBackdrop : ClearBackdrop;
        }
    }

    private void Close(bool apply)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        IsOpen = false;

        bool applied = apply && _index >= 0 && _index < _crests.Count;
        if (applied)
        {
            PlaySubmitSound();
            CrestPickerService.ApplyCrest(_crests[_index]);
        }
        else
        {
            PlayCancelSound();
            Plugin.Log.LogInfo("[CREST] Selection cancelled, crest unchanged.");
        }

        Closed?.Invoke(applied, _fromDialogue);

        UnblockHero();
        Destroy(gameObject);
    }

    private void BlockHero()
    {
        try
        {
            HeroController? hero = HeroController.instance;
            if (hero == null)
            {
                Plugin.Log.LogWarning("[CREST] HeroController unavailable, hero input not blocked.");
                return;
            }

            hero.AddInputBlocker(this);
            _heroBlocked = true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CREST] AddInputBlocker failed: {ex.Message}");
        }
    }

    private void UnblockHero()
    {
        if (!_heroBlocked)
        {
            return;
        }

        try
        {
            HeroController? hero = HeroController.instance;
            hero?.RemoveInputBlocker(this);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CREST] RemoveInputBlocker failed: {ex.Message}");
        }
        finally
        {
            _heroBlocked = false;
        }
    }

    private static MenuAudioController? Audio()
    {
        UIManager? ui = UIManager.instance;
        return ui != null ? ui.uiAudioPlayer : null;
    }

    private static void PlaySelectSound()
    {
        try
        {
            Audio()?.PlaySelect();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CREST] PlaySelect failed: {ex.Message}");
        }
    }

    private static void PlaySubmitSound()
    {
        try
        {
            Audio()?.PlaySubmit();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CREST] PlaySubmit failed: {ex.Message}");
        }
    }

    private static void PlayCancelSound()
    {
        try
        {
            Audio()?.PlayCancel();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CREST] PlayCancel failed: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        IsOpen = false;
        UnblockHero();
    }
}
