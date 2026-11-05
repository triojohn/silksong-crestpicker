using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrestPicker;

/// <summary>
/// One-shot diagnostic of the shop objects present in Bonetown, so a log from a
/// player run shows what a real shop window attempt has to work with.
/// </summary>
internal static class ShopDiag
{
    /// <summary>Scene the counts are taken from.</summary>
    private const string TargetScene = "Bonetown";

    private static bool _logged;

    /// <summary>Logs shop object counts once per session for the target scene.</summary>
    /// <param name="scene">Scene that just loaded.</param>
    internal static void OnSceneLoaded(Scene scene)
    {
        if (_logged || scene.name != TargetScene)
        {
            return;
        }

        _logged = true;
        try
        {
            Plugin.Log.LogInfo(
                $"[SHOP] scene={scene.name} counts: ShopOwner={Count<ShopOwner>()}, " +
                $"ShopOwnerBase={Count<ShopOwnerBase>()}, ShopMenuStock={Count<ShopMenuStock>()}, " +
                $"SimpleShopMenu={Count<SimpleShopMenu>()}, " +
                $"SimpleShopMenuOwner={Count<SimpleShopMenuOwner>()}, ShopItem={Count<ShopItem>()}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] scene scan failed: {ex}");
        }
    }

    /// <summary>Number of objects of a type, including inactive and asset copies.</summary>
    private static int Count<T>() where T : UnityEngine.Object
    {
        try
        {
            return Resources.FindObjectsOfTypeAll<T>().Length;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SHOP] count of {typeof(T).Name} failed: {ex.Message}");
            return -1;
        }
    }
}

/// <summary>Fires the shop diagnostics after every scene load.</summary>
[HarmonyPatch(typeof(SceneManager), "Internal_SceneLoaded")]
internal static class ShopDiagScenePatch
{
    private static void Postfix(Scene __0)
    {
        ShopDiag.OnSceneLoaded(__0);
    }
}
