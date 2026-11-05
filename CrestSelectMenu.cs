using System;
using System.Collections.Generic;
using TMProOld;
using UnityEngine;
using UnityEngine.UI;

namespace CrestPicker;

/// <summary>
/// In-game crest picker built from scratch: a uGUI panel with one row per crest,
/// driven by the game's own input actions, menu sounds and hero input blockers.
/// </summary>
internal sealed class CrestSelectMenu : MonoBehaviour
{
    private const float RowHeight = 40f;
    private const float RowSpacing = 10f;
    private const float PanelWidth = 640f;
    private const float PanelPadding = 20f;
    private const float LabelFontSize = 32f;

    private static readonly Color SelectedColor = Color.white;
    private static readonly Color IdleColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    private readonly List<TMP_Text> _rows = new List<TMP_Text>();
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
            List<ToolCrest> crests = CrestPickerService.GetSelectableCrests();
            if (crests.Count == 0)
            {
                Plugin.Log.LogWarning("[CREST] No selectable crests to show, menu not opened.");
                return false;
            }

            if (ShopCrestMenu.TryOpen(crests, fromDialogue))
            {
                return true;
            }

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
        float rowsHeight = RowHeight * crests.Count + RowSpacing * (crests.Count - 1);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(PanelWidth, rowsHeight + PanelPadding * 2f);

        Image background = GetComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.75f);
        background.raycastTarget = false;

        VerticalLayoutGroup layout = GetComponent<VerticalLayoutGroup>();
        layout.spacing = RowSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        layout.padding = new RectOffset(
            (int)PanelPadding, (int)PanelPadding, (int)PanelPadding, (int)PanelPadding);

        TMP_FontAsset? font = FindGameFont();

        for (int i = 0; i < crests.Count; i++)
        {
            _rows.Add(CreateRow(transform, i, CrestPickerService.GetLabel(crests[i]), font));
        }

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

    private static TMP_Text CreateRow(Transform parent, int index, string label, TMP_FontAsset? font)
    {
        GameObject row = new GameObject($"CrestRow{index}",
            typeof(RectTransform), typeof(LayoutElement));
        row.transform.SetParent(parent, false);

        RectTransform rect = (RectTransform)row.transform;
        rect.sizeDelta = new Vector2(0f, RowHeight);

        LayoutElement element = row.GetComponent<LayoutElement>();
        element.minHeight = RowHeight;
        element.preferredHeight = RowHeight;
        element.flexibleHeight = 0f;

        TextMeshProUGUI text = row.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.font = font != null ? font : TMP_FontAsset.defaultFontAsset;
        text.fontSize = LabelFontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;

        return text;
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
                Close(apply: true);
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
                Close(apply: true);
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

    private void RefreshHighlight()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            TMP_Text? row = _rows[i];
            if (row != null)
            {
                row.color = i == _index ? SelectedColor : IdleColor;
            }
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
