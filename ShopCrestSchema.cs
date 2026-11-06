using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
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

    /// <summary>Dot diameter as a fraction of the silhouette height.</summary>
    private const float DotFraction = 0.06f;

    /// <summary>Dot diameter factor for a locked slot.</summary>
    private const float LockedDotFactor = 0.8f;

    /// <summary>Dot alpha for a locked slot.</summary>
    private const float LockedAlpha = 0.4f;

    /// <summary>Path of the details panel inside the shop window.</summary>
    private const string DetailsPath = "Item List Group/Item Details";

    /// <summary>Confirm group holding the item picture the confirm schema is centred on.</summary>
    private const string ConfirmGroupPath = "Item Confirm Group";

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
    private static Transform? _confirmGroup;
    private static Transform? _confirmAnchor;

    /// <summary>Schema objects created for the current crest; destroyed on rebuild.</summary>
    private static GameObject? _container;
    private static GameObject? _confirmContainer;
    private static ToolCrest? _builtFor;
    private static Sprite? _dotSprite;
    private static bool _missingLogged;

    /// <summary>True to draw the crest schema at all.</summary>
    internal static bool Enabled = true;

    /// <summary>Multiplier applied to the schema size.</summary>
    internal static float Scale = 1f;

    /// <summary>Multiplier for slot dot positions, which are silhouette-local units.</summary>
    internal static float DotPositionFactor = 1f;

    /// <summary>Multiplier for the slot dot diameter.</summary>
    internal static float DotSize = 1f;

    /// <summary>Confirm-screen schema size relative to the details schema.</summary>
    internal static float ConfirmScale = 1f;

    /// <summary>Remembers the window, hides the duplicate name and draws the schema.</summary>
    /// <param name="root">Shop window root, the object holding <c>shop_control</c>.</param>
    internal static void Begin(Transform root)
    {
        try
        {
            _root = root;
            _details = null;
            _nameLabel = null;
            _confirmGroup = null;
            _confirmAnchor = null;
            _builtFor = null;
            _missingLogged = false;
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
        _confirmGroup = null;
        _confirmAnchor = null;
        _builtFor = null;
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
        if (crest != null && crest != _builtFor)
        {
            Build(crest);
        }

        Place();
    }

    /// <summary>
    /// Puts the details schema in the middle of the lower half of the details panel and
    /// the confirm schema on the confirm screen picture. Runs every frame because the
    /// panel's layout group keeps moving its children.
    /// </summary>
    private static void Place()
    {
        if (_container != null && _details != null && TryBounds(_details, out Bounds bounds))
        {
            float lowerCentreY = Mathf.Lerp(bounds.min.y, bounds.center.y, 0.5f);
            Vector3 target = new Vector3(bounds.center.x, lowerCentreY, bounds.center.z);
            if (_container.transform.position != target)
            {
                _container.transform.position = target;
            }
        }

        if (_confirmContainer == null)
        {
            return;
        }

        bool showing = _confirmGroup != null && _confirmGroup.gameObject.activeInHierarchy;
        if (_confirmContainer.activeSelf != showing)
        {
            _confirmContainer.SetActive(showing);
        }

        if (showing && _confirmAnchor != null)
        {
            Vector3 target = _confirmAnchor.position;
            if (_confirmContainer.transform.position != target)
            {
                _confirmContainer.transform.position = target;
            }
        }
    }

    /// <summary>World bounds of a UI panel: rect corners, then child renderers, then its position.</summary>
    private static bool TryBounds(Transform target, out Bounds bounds)
    {
        bounds = new Bounds(target.position, Vector3.zero);
        RectTransform? rect = target as RectTransform;
        if (rect != null)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 size = corners[2] - corners[0];
            if (Mathf.Abs(size.x) > 0.001f && Mathf.Abs(size.y) > 0.001f)
            {
                bounds = new Bounds((corners[0] + corners[2]) * 0.5f,
                    new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
                return true;
            }
        }

        bool any = false;
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
            {
                continue;
            }

            if (any)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                any = true;
            }
        }

        return true;
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

        _container = BuildSchema(crest, silhouette, darkened, DetailsParent(), 1f, "Crest Schema");
        _confirmContainer = BuildSchema(crest, silhouette, darkened, ConfirmGroup(), ConfirmScale,
            "Confirm Crest Schema");
    }

    /// <summary>
    /// Creates one schema instance: silhouette plus one dot per slot, sized to
    /// <see cref="BaseHeight"/> times <paramref name="sizeFactor"/> world units.
    /// The container is parented outside the layout group and placed by the caller.
    /// </summary>
    private static GameObject? BuildSchema(ToolCrest crest, Sprite silhouette, bool darkened,
        Transform? parent, float sizeFactor, string label)
    {
        if (parent == null)
        {
            if (!_missingLogged)
            {
                _missingLogged = true;
                Plugin.Log.LogWarning($"[SCHEMA] '{label}' parent not found, schema skipped.");
            }

            return null;
        }

        SpriteRenderer? reference = ReferenceRenderer() ?? BackboardRenderer();
        GameObject container = new GameObject(label);
        // A plain Transform child: layout groups only lay out RectTransform children,
        // so this container keeps its hand-placed position.
        container.transform.SetParent(parent, false);
        float parentScale = parent.lossyScale.x;
        container.transform.localScale = Vector3.one * (Mathf.Approximately(parentScale, 0f) ? 1f : 1f / parentScale);
        container.transform.position = parent.position;

        float height = BaseHeight * Scale * sizeFactor;
        float worldHeight = silhouette.rect.height / Mathf.Max(1f, silhouette.pixelsPerUnit);
        float fit = worldHeight > 0.01f ? height / worldHeight : 1f;
        float dotSize = worldHeight * DotFraction * DotSize;

        SpriteRenderer body = AddRenderer(container, "Schema Silhouette", silhouette, reference, 0);
        body.transform.localScale = Vector3.one * fit;
        body.color = darkened ? new Color(0.3f, 0.3f, 0.3f, 1f) : Color.white;

        int dots = AddDots(body.transform, crest, reference, dotSize);
        LogNumbers(label, crest, silhouette, crest.Slots, fit, dotSize, dots);
        return container;
    }

    /// <summary>Parent for the details schema: the details panel's own parent, not the layout group itself.</summary>
    private static Transform? DetailsParent()
    {
        if (_details == null)
        {
            return null;
        }

        return _details.parent != null ? _details.parent : _root;
    }

    /// <summary>Confirm group and the item picture the confirm schema is centred on.</summary>
    private static Transform? ConfirmGroup()
    {
        if (_confirmGroup != null)
        {
            return _confirmGroup;
        }

        if (_root == null)
        {
            return null;
        }

        Transform? direct = _root.Find(ConfirmGroupPath);
        if (direct == null)
        {
            foreach (Transform child in _root.GetComponentsInChildren<Transform>(true))
            {
                if (child != null && child.name == "Item Confirm Group")
                {
                    direct = child;
                    break;
                }
            }
        }

        _confirmGroup = direct;
        if (_confirmGroup != null)
        {
            Transform? picture = _confirmGroup.Find("Item Sprite");
            _confirmAnchor = picture != null ? picture : _confirmGroup;
        }

        return _confirmGroup;
    }

    /// <summary>
    /// Adds one dot per slot as a child of the silhouette, so the dots follow its scale.
    /// Slot positions are silhouette-local units, not pixels.
    /// </summary>
    private static int AddDots(Transform silhouette, ToolCrest crest, SpriteRenderer? reference,
        float dotSize)
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

        float worldScale = Mathf.Abs(silhouette.lossyScale.x);
        float sizeFactor = worldScale > 0.0001f ? 1f / worldScale : 1f;
        int created = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            ToolCrest.SlotInfo slot = slots[i];
            bool locked = IsLocked(crest, i, slot);
            SpriteRenderer renderer = AddRenderer(silhouette.gameObject, $"Schema Dot {i}", dot, reference, 1);
            Vector2 position = slot.Position * DotPositionFactor;
            renderer.transform.localPosition = new Vector3(position.x, position.y, 0f);
            float size = (locked ? dotSize * LockedDotFactor : dotSize) * sizeFactor;
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
    private static void LogNumbers(string label, ToolCrest crest, Sprite silhouette,
        ToolCrest.SlotInfo[]? slots, float fit, float dotSize, int dots)
    {
        float ppu = Mathf.Max(1f, silhouette.pixelsPerUnit);
        float worldWidth = silhouette.rect.width / ppu;
        float worldHeight = silhouette.rect.height / ppu;
        StringBuilder line = new StringBuilder();
        line.Append($"[SCHEMA] {label} crest='{crest.name}' sprite='{silhouette.name}'");
        line.Append($" rect={silhouette.rect.width:F0}x{silhouette.rect.height:F0} ppu={silhouette.pixelsPerUnit:F1}");
        line.Append($" spriteSize=({worldWidth:F3},{worldHeight:F3}) fit={fit:F3}");
        line.Append($" dot={dotSize:F3} dots={dots} dotFactor={DotPositionFactor:F2} scale={Scale:F2}");
        if (slots != null)
        {
            int shown = Mathf.Min(3, slots.Length);
            for (int i = 0; i < shown; i++)
            {
                Vector2 raw = slots[i].Position;
                line.Append($" slot{i}:pos=({raw.x:F2},{raw.y:F2})");
                line.Append($" local=({raw.x * DotPositionFactor:F3},{raw.y * DotPositionFactor:F3})");
            }
        }

        Plugin.Log.LogInfo(line.ToString());
    }

    /// <summary>Destroys the schema objects of the current crest, details and confirm alike.</summary>
    private static void DestroySchema()
    {
        if (_container != null)
        {
            UnityEngine.Object.Destroy(_container);
            _container = null;
        }

        if (_confirmContainer != null)
        {
            UnityEngine.Object.Destroy(_confirmContainer);
            _confirmContainer = null;
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
