using System;
using System.Reflection;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Own instance of the vanilla shop window, spawned from the live
/// <see cref="ShopOwnerBase"/> prefab. The clone is independent of the scene's shared
/// window: nothing is opened, the stock is not touched and the position is not moved.
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

    /// <summary>Clone root name; must not match the vanilla <c>Shop Menu(Clone)</c>.</summary>
    private const string RootName = "CrestPicker Shop Menu";

    /// <summary>Scene container the clone is parented to, so it stays a separate root.</summary>
    private const string ContainerName = "CrestPicker Shop Window";

    /// <summary>Live clone, null when there is none or it was destroyed.</summary>
    internal static ShopMenuStock? Current { get; private set; }

    /// <summary>Owner the clone was spawned for, null when there is none.</summary>
    internal static ShopOwnerBase? Owner { get; private set; }

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

            GameObject container = new GameObject(ContainerName);
            ShopMenuStock clone = UnityEngine.Object.Instantiate(prefab, container.transform);
            clone.gameObject.name = RootName;
            Current = clone;
            Owner = owner;
            Plugin.Log.LogInfo(
                $"[OWN] clone spawned path='{PathOf(clone.transform)}' stock={StockCount(clone)}");
        }
        catch (Exception ex)
        {
            Current = null;
            Owner = null;
            Fail(ex.Message);
        }
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

    private static string PathOf(Transform transform)
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
