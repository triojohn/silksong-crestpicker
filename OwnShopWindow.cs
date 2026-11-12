using System;
using System.Reflection;
using CrestPicker.Pure;
using TeamCherry.Localization;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrestPicker;

/// <summary>
/// Own instance of the vanilla shop window, spawned from the live
/// <see cref="ShopOwnerBase"/> prefab. The clone is independent of the scene's shared
/// window: a session swaps the clone's own stock and moves the clone, never the vanilla
/// object.
/// Never throws.
/// </summary>
internal static class OwnShopWindow
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>Prefab field of <see cref="ShopOwnerBase"/> (decompiled: private ShopMenuStock).</summary>
    private static readonly FieldInfo? ShopPrefabField =
        typeof(ShopOwnerBase).GetField("shopPrefab", PrivateInstance);

    private static readonly FieldInfo? StockArrayField =
        typeof(ShopMenuStock).GetField("stock", PrivateInstance);

    /// <summary>Title field the vanilla owner copies onto its own window.</summary>
    private static readonly FieldInfo? ShopTitleField =
        typeof(ShopOwnerBase).GetField("shopTitle", PrivateInstance);

    /// <summary>Scene container the clone is parented to, so it stays a separate root.</summary>
    private const string ContainerName = "CrestPicker Shop Window";

    /// <summary>Live clone, null when there is none or it was destroyed.</summary>
    internal static ShopMenuStock? Current { get; private set; }

    /// <summary>Owner the clone was spawned for, null when there is none.</summary>
    internal static ShopOwnerBase? Owner { get; private set; }

    /// <summary>Scene handle the clone belongs to; 0 when no clone was spawned.</summary>
    internal static int SceneHandle { get; private set; }

    /// <summary>Instance id of the owner the clone was spawned for; 0 when none.</summary>
    internal static int OwnerId { get; private set; }

    /// <summary>
    /// Shop title of the owner, the text the vanilla window shows. Empty when the
    /// field cannot be read, so the caller keeps whatever the clone already has.
    /// </summary>
    internal static string Title
    {
        get
        {
            try
            {
                return ShopTitleField?.GetValue(Owner) is LocalisedString title
                    ? (string)title
                    : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }

    private static GameObject? _container;

    /// <summary>Drops the reference once the clone is gone; call every frame.</summary>
    internal static void Tick()
    {
        ShopFsmDiag.Poll();

        if (SceneHandle == 0)
        {
            return;
        }

        int ownerId = Owner != null ? Owner.GetInstanceID() : 0;
        bool stale = OwnWindowPolicy.ShouldRebuild(
            SceneHandle, SceneManager.GetActiveScene().handle, OwnerId, ownerId);
        if (Current != null && !stale)
        {
            return;
        }

        string reason = ReleaseReason();
        Release(reason);
    }

    private static string ReleaseReason()
    {
        if (SceneManager.GetActiveScene().handle != SceneHandle)
        {
            return "scene";
        }

        return Owner == null || Owner.GetInstanceID() != OwnerId ? "owner" : "external";
    }

    /// <summary>Spawns the clone once; a live clone is left untouched.</summary>
    internal static void TrySpawn()
    {
        try
        {
            if (Current != null)
            {
                Plugin.Log.LogInfo("[OWN] clone already alive");
                return;
            }

            ShopOwnerBase? owner = FindLiveOwner();
            if (owner == null)
            {
                Fail("no live ShopOwnerBase in scene");
                return;
            }

            ShopMenuStock? prefab = ShopPrefabField?.GetValue(owner) as ShopMenuStock;
            if (prefab == null)
            {
                Fail("prefab missing");
                return;
            }

            Plugin.Log.LogInfo($"[OWN] prefab ok '{prefab.name}'");

            // Scene object: unloaded together with the scene, never DontDestroyOnLoad.
            GameObject container = new GameObject(ContainerName);
            ShopMenuStock clone = UnityEngine.Object.Instantiate(prefab, container.transform);
            clone.gameObject.name = OwnWindowPolicy.RootName;
            _container = container;
            Current = clone;
            Owner = owner;
            SceneHandle = owner.gameObject.scene.handle;
            OwnerId = owner.GetInstanceID();
            Plugin.Log.LogInfo(
                $"[OWN] clone spawned path='{PathOf(clone.transform)}' stock={StockCount(clone)}");
        }
        catch (Exception ex)
        {
            Current = null;
            Owner = null;
            SceneHandle = 0;
            OwnerId = 0;
            _container = null;
            Fail(ex.Message);
        }
    }

    /// <summary>
    /// Switches the whole clone off, so nothing of it stays on screen between sessions.
    /// The clone object is kept: the next session reuses it.
    /// </summary>
    internal static void Hide()
    {
        try
        {
            if (_container != null && _container.activeSelf)
            {
                _container.SetActive(false);
                Plugin.Log.LogInfo("[OWN] window hidden");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[OWN] hide failed: {ex.Message}");
        }
    }

    /// <summary>Switches the clone back on before a session uses it; a no-op when it is already on.</summary>
    internal static void EnsureActive()
    {
        try
        {
            if (_container != null && !_container.activeSelf)
            {
                _container.SetActive(true);
                Plugin.Log.LogInfo("[OWN] window reactivated");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[OWN] reactivate failed: {ex.Message}");
        }
    }

    /// <summary>Destroys the clone root and clears the cached references.</summary>
    private static void Release(string reason)
    {
        try
        {
            if (_container != null)
            {
                UnityEngine.Object.Destroy(_container);
            }
            else if (Current != null)
            {
                UnityEngine.Object.Destroy(Current.gameObject);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[OWN] release failed: {ex.Message}");
        }

        Current = null;
        Owner = null;
        SceneHandle = 0;
        OwnerId = 0;
        _container = null;
        Plugin.Log.LogInfo($"[OWN] released reason={reason}");
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

    private static int StockCount(ShopMenuStock stock)
    {
        return StockArrayField?.GetValue(stock) is ShopItem[] items ? items.Length : -1;
    }

    internal static string PathOf(Transform transform)
    {
        string path = transform.name;
        for (Transform? current = transform.parent; current != null; current = current.parent)
        {
            path = current.name + "/" + path;
        }

        return path;
    }

    private static void Fail(string reason)
    {
        Plugin.Log.LogWarning($"[OWN] clone spawn failed: {reason}");
    }
}
