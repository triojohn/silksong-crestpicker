using System;
using System.Collections.Generic;
using System.Reflection;
using GlobalSettings;
using TMProOld;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Cosmetic pass over the shop details panel while our crests are shown: the crest
/// name label repeats the row label, so it is hidden, and a crest schema takes its
/// place - the monochrome silhouette plus one coloured dot per slot. Every created
/// object is destroyed and every hidden object restored by <see cref="End"/>.
/// Never throws.
/// </summary>
internal static class ShopCrestSchema
{
    /// <summary>Schema height in world units before <see cref="Scale"/> is applied.</summary>
    private const float BaseHeight = 1.2f;

    /// <summary>Dot diameter as a fraction of the schema height.</summary>
    private const float DotFraction = 0.1f;

    /// <summary>Dot diameter factor for a locked slot.</summary>
    private const float LockedDotFactor = 0.8f;

    /// <summary>Dot alpha for a locked slot.</summary>
    private const float LockedAlpha = 0.4f;

    /// <summary>Path of the details panel inside the shop window.</summary>
    private const string DetailsPath = "Item List Group/Item Details";

    /// <summary>Fallback colours when the game's tool type palette is unavailable.</summary>
    private static readonly Dictionary<ToolItemType, Color> FallbackColors =
        new Dictionary<ToolItemType, Color>
        {
            [ToolItemType.Red] = new Color(1f, 0.42f, 0.42f, 1f),
            [ToolItemType.Blue] = new Color(0.36f, 0.82f, 1f, 1f),
            [ToolItemType.Yellow] = new Color(1f, 0.82f, 0.4f, 1f),
            [ToolItemType.Skill] = new Color(0.88f, 0.88f, 0.88f, 1f),
        };

    /// <summary>Vanilla active state of every object we hid.</summary>
    private static readonly Dictionary<GameObject, bool> ObjectStates = new Dictionary<GameObject, bool>();

    private static Transform? _root;
    private static Transform? _details;
    private static TMP_Text? _nameLabel;

    /// <summary>Schema objects created for the current crest; destroyed on rebuild.</summary>
    private static GameObject? _container;
    private static ToolCrest? _builtFor;
    private static Sprite? _dotSprite;
    private static bool _missingLogged;
    private static bool _numbersLogged;

    /// <summary>True to draw the crest schema at all.</summary>
    internal static bool Enabled = true;

    /// <summary>Multiplier applied to the schema size.</summary>
    internal static float Scale = 1f;

    /// <summary>Remembers the window, hides the duplicate name and draws the schema.</summary>
    /// <param name="root">Shop window root, the object holding <c>shop_control</c>.</param>
    internal static void Begin(Transform root)
    {
        try
        {
            _root = root;
            _details = null;
            _nameLabel = null;
            _builtFor = null;
            _missingLogged = false;
            _numbersLogged = false;
            Apply();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] begin failed: {ex.Message}");
        }
    }

    /// <summary>Redraws the schema when the selected crest changed; cheap on a match.</summary>
    internal static void Tick()
    {
        if (_root == null)
        {
            return;
        }

        try
        {
            Apply();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] tick failed: {ex.Message}");
        }
    }

    /// <summary>Destroys the schema objects and puts the hidden name back.</summary>
    internal static void End()
    {
        _root = null;
        _details = null;
        _nameLabel = null;
        _builtFor = null;
        _numbersLogged = false;
        try
        {
            DestroySchema();
            foreach (KeyValuePair<GameObject, bool> pair in ObjectStates)
            {
                if (pair.Key != null)
                {
                    pair.Key.SetActive(pair.Value);
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] restore failed: {ex.Message}");
        }
        finally
        {
            ObjectStates.Clear();
        }
    }

    /// <summary>Hides the duplicate name and keeps the schema in step with the selection.</summary>
    private static void Apply()
    {
        if (!ResolveDetails())
        {
            return;
        }

        HideName();
        if (!Enabled)
        {
            return;
        }

        ToolCrest? crest = SelectedCrest();
        if (crest == null || crest == _builtFor)
        {
            return;
        }

        Build(crest);
    }

    /// <summary>Finds the details panel and its name label, once per session.</summary>
    private static bool ResolveDetails()
    {
        if (_details != null && _nameLabel != null)
        {
            return true;
        }

        if (_root == null)
        {
            return false;
        }

        _details = FindDetails(_root);
        _nameLabel = _details != null ? FindText(_details, "Item name") : null;
        if (_details == null || _nameLabel == null)
        {
            _details = null;
            _nameLabel = null;
            if (!_missingLogged)
            {
                _missingLogged = true;
                Plugin.Log.LogWarning($"[SCHEMA] details panel or 'Item name' not found under '{ShopCrestMenu.Path(_root)}'.");
            }

            return false;
        }

        return true;
    }

    /// <summary>Hides the details name label, remembering its vanilla state once.</summary>
    private static void HideName()
    {
        GameObject? go = _nameLabel != null ? _nameLabel.gameObject : null;
        if (go == null)
        {
            return;
        }

        if (!ObjectStates.ContainsKey(go))
        {
            ObjectStates[go] = go.activeSelf;
        }

        if (go.activeSelf)
        {
            go.SetActive(false);
        }
    }

    /// <summary>
    /// Crest the details panel currently describes: the selected row's name text is
    /// compared with every crest shown in the window, description text as a fallback.
    /// </summary>
    private static ToolCrest? SelectedCrest()
    {
        if (_nameLabel == null || _root == null)
        {
            return null;
        }

        string? name = SafeText(_nameLabel);
        ToolCrest? byName = Match(name, true);
        if (byName != null)
        {
            return byName;
        }

        TMP_Text? desc = _details != null ? FindText(_details, "Item desc") : null;
        return Match(desc != null ? SafeText(desc) : null, false);
    }

    /// <summary>Compares one text against every crest of the window.</summary>
    /// <param name="text">Text shown by the details panel.</param>
    /// <param name="byName">True to compare display names, false to compare descriptions.</param>
    private static ToolCrest? Match(string? text, bool byName)
    {
        if (string.IsNullOrEmpty(text) || _root == null)
        {
            return null;
        }

        foreach (ShopItemStats row in _root.GetComponentsInChildren<ShopItemStats>(true))
        {
            ToolCrest? crest = row != null ? ShopCrestMenu.CrestFor(row.Item) : null;
            if (crest == null)
            {
                continue;
            }

            string candidate = byName ? crest.DisplayName : crest.Description;
            if (!string.IsNullOrEmpty(candidate) && candidate == text)
            {
                return crest;
            }
        }

        return null;
    }

    private static string SafeText(TMP_Text label)
    {
        try
        {
            return label.text ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Builds the silhouette and the slot dots for <paramref name="crest"/>.</summary>
    private static void Build(ToolCrest crest)
    {
        DestroySchema();
        _builtFor = crest;

        Sprite? silhouette = crest.CrestSilhouette;
        bool darkened = silhouette == null;
        if (silhouette == null)
        {
            silhouette = crest.CrestSprite;
        }

        if (silhouette == null || _details == null || _nameLabel == null)
        {
            Plugin.Log.LogWarning($"[SCHEMA] '{crest.name}' has no silhouette or sprite, schema skipped.");
            return;
        }

        SpriteRenderer? reference = ReferenceRenderer() ?? BackboardRenderer();
        Transform anchor = _nameLabel.transform;
        GameObject container = new GameObject("Crest Schema");
        container.transform.SetParent(_details, false);
        float parentScale = _details.lossyScale.x;
        container.transform.localScale = Vector3.one * (Mathf.Approximately(parentScale, 0f) ? 1f : 1f / parentScale);
        container.transform.position = anchor.position;
        _container = container;

        float worldHeight = silhouette.rect.height / Mathf.Max(1f, silhouette.pixelsPerUnit);
        float fit = worldHeight > 0.01f ? BaseHeight * Scale / worldHeight : 1f;
        float dotFactor = fit / Mathf.Max(1f, silhouette.pixelsPerUnit);
        float dotSize = BaseHeight * Scale * DotFraction;

        SpriteRenderer body = AddRenderer(container, "Schema Silhouette", silhouette, reference, 0);
        body.transform.localScale = Vector3.one * fit;
        body.color = darkened ? new Color(0.3f, 0.3f, 0.3f, 1f) : Color.white;

        int dots = AddDots(container, crest, reference, dotFactor, dotSize);
        LogNumbers(crest, silhouette, reference, fit, dotFactor, dotSize, dots);
    }

    /// <summary>Adds one dot per slot; returns how many were created.</summary>
    private static int AddDots(GameObject container, ToolCrest crest, SpriteRenderer? reference,
        float dotFactor, float dotSize)
    {
        ToolCrest.SlotInfo[]? slots = crest.Slots;
        if (slots == null || slots.Length == 0)
        {
            Plugin.Log.LogInfo($"[SCHEMA] '{crest.name}' has no slots, schema drawn without dots.");
            return 0;
        }

        Sprite? dot = DotSprite();
        if (dot == null)
        {
            return 0;
        }

        int created = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            ToolCrest.SlotInfo slot = slots[i];
            bool locked = IsLocked(crest, i, slot);
            SpriteRenderer renderer = AddRenderer(container, $"Schema Dot {i}", dot, reference, 1);
            Vector2 position = slot.Position;
            renderer.transform.localPosition = new Vector3(position.x * dotFactor, position.y * dotFactor, 0f);
            float size = locked ? dotSize * LockedDotFactor : dotSize;
            renderer.transform.localScale = new Vector3(size, size, 1f);
            Color color = TypeColor(slot.Type);
            color.a = locked ? LockedAlpha : 1f;
            renderer.color = color;
            created++;
        }

        return created;
    }

    /// <summary>True when the slot is locked by the crest itself or by the save data.</summary>
    private static bool IsLocked(ToolCrest crest, int index, ToolCrest.SlotInfo slot)
    {
        if (slot.IsLocked)
        {
            return true;
        }

        try
        {
            List<ToolCrestsData.SlotData>? saved = crest.SaveData.Slots;
            if (saved == null || index >= saved.Count)
            {
                return false;
            }

            return !saved[index].IsUnlocked;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Slot colour from the game palette, with a local fallback.</summary>
    private static Color TypeColor(ToolItemType type)
    {
        try
        {
            return UI.GetToolTypeColor(type);
        }
        catch (Exception)
        {
            return FallbackColors.TryGetValue(type, out Color color) ? color : Color.white;
        }
    }

    /// <summary>Adds one world-space sprite child with the window's layer and sorting.</summary>
    private static SpriteRenderer AddRenderer(GameObject parent, string name, Sprite sprite,
        SpriteRenderer? reference, int orderOffset)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.layer = 5;
        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        if (reference != null)
        {
            go.layer = reference.gameObject.layer;
            renderer.sortingLayerID = reference.sortingLayerID;
            renderer.sortingOrder = reference.sortingOrder + orderOffset;
        }

        return renderer;
    }

    /// <summary>Row icon renderer, the closest neighbour sprite of the window.</summary>
    private static SpriteRenderer? ReferenceRenderer()
    {
        if (_root == null)
        {
            return null;
        }

        foreach (ShopItemStats row in _root.GetComponentsInChildren<ShopItemStats>(true))
        {
            SpriteRenderer? icon = ShopCosmetics.RowIcon(row);
            if (icon != null && icon.enabled)
            {
                return icon;
            }
        }

        return null;
    }

    /// <summary>Backboard sprite as a second sorting reference.</summary>
    private static SpriteRenderer? BackboardRenderer()
    {
        Transform? backboard = _root != null ? _root.Find("Window/Shop_Backboard") : null;
        return backboard != null ? backboard.GetComponent<SpriteRenderer>() : null;
    }

    /// <summary>Generated circle used as a slot dot.</summary>
    private static Sprite? DotSprite()
    {
        if (_dotSprite != null)
        {
            return _dotSprite;
        }

        try
        {
            const int size = 32;
            float radius = size / 2f - 0.5f;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - radius;
                    float dy = y - radius;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius + 0.5f - distance);
                    pixels[(y * size) + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            _dotSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SCHEMA] dot sprite creation failed: {ex.Message}");
        }

        return _dotSprite;
    }

    /// <summary>One line of real numbers, so the slot scaling can be tuned from the log.</summary>
    private static void LogNumbers(ToolCrest crest, Sprite silhouette, SpriteRenderer? reference,
        float fit, float dotFactor, float dotSize, int dots)
    {
        if (_numbersLogged)
        {
            return;
        }

        _numbersLogged = true;
        string sorting = reference != null
            ? $"layer={reference.gameObject.layer} sort='{reference.sortingLayerName}' order={reference.sortingOrder}"
            : "layer=5 default sorting";
        Plugin.Log.LogInfo(
            $"[SCHEMA] crest='{crest.name}' sprite='{silhouette.name}'" +
            $" rect={silhouette.rect.width:F0}x{silhouette.rect.height:F0} ppu={silhouette.pixelsPerUnit:F1}" +
            $" fit={fit:F3} dotFactor={dotFactor:F4} dot={dotSize:F3} dots={dots} {sorting}" +
            $" anchor={_nameLabel?.transform.position}");
    }

    /// <summary>Destroys the schema objects of the current crest.</summary>
    private static void DestroySchema()
    {
        if (_container != null)
        {
            UnityEngine.Object.Destroy(_container);
            _container = null;
        }
    }

    /// <summary>Details panel transform by path, then by name.</summary>
    private static Transform? FindDetails(Transform root)
    {
        Transform? direct = root.Find(DetailsPath);
        if (direct != null)
        {
            return direct;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child != null && child.name == "Item Details")
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>One text child of the details panel by name.</summary>
    private static TMP_Text? FindText(Transform details, string name)
    {
        Transform? direct = details.Find(name);
        TMP_Text? found = direct != null ? direct.GetComponent<TMP_Text>() : null;
        if (found != null)
        {
            return found;
        }

        foreach (TMP_Text text in details.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text != null && text.name == name)
            {
                return text;
            }
        }

        return null;
    }
}
