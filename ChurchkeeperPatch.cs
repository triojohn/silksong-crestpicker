using System;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Offers the crest choice once the chapel maid dialogue finishes: the FSM action
/// is injected at the head of the <c>End</c> state, which every dialogue branch
/// of <c>Churchkeeper/Conversation</c> funnels into.
/// </summary>
internal static class ChurchkeeperPatch
{
    private const string SceneName = "Bonetown";
    private const string ObjectName = "Churchkeeper";
    private const string FsmName = "Conversation";
    private const string EndStateName = "End";

    /// <summary>
    /// Injects <see cref="OfferCrestAction"/> into the conversation FSM. Never throws,
    /// so a failure cannot stop the FSM from starting.
    /// </summary>
    /// <param name="fsm">FSM that just started.</param>
    internal static void OnFsmStarted(PlayMakerFSM fsm)
    {
        try
        {
            if (!IsChurchkeeperConversation(fsm))
            {
                return;
            }

            FsmState? end = fsm.Fsm != null ? fsm.Fsm.GetState(EndStateName) : null;
            if (end == null)
            {
                Plugin.Log.LogWarning($"[CREST] {ObjectName}/{FsmName} has no '{EndStateName}' state.");
                return;
            }

            FsmStateAction[] current = end.Actions;
            foreach (FsmStateAction action in current)
            {
                if (action is OfferCrestAction)
                {
                    return;
                }
            }

            FsmStateAction[] patched = new FsmStateAction[current.Length + 1];
            patched[0] = new OfferCrestAction();
            Array.Copy(current, 0, patched, 1, current.Length);
            end.Actions = patched;

            Plugin.Log.LogInfo($"[CREST] Offer action inserted into {ObjectName}/{FsmName}/End.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CREST] FSM hook failed for '{fsm?.gameObject?.name}': {ex}");
        }
    }

    private static bool IsChurchkeeperConversation(PlayMakerFSM? fsm)
    {
        GameObject? go = fsm?.gameObject;
        if (go == null || go.scene.name != SceneName)
        {
            return false;
        }

        return fsm!.FsmName == FsmName && go.name == ObjectName;
    }
}

/// <summary>Runs the injection right after each PlayMaker FSM starts.</summary>
[HarmonyPatch(typeof(PlayMakerFSM), "Start")]
internal static class PlayMakerFsmStartPatch
{
    private static void Postfix(PlayMakerFSM __instance)
    {
        ChurchkeeperPatch.OnFsmStarted(__instance);
    }
}

/// <summary>
/// FSM action that asks the plugin to show the crest picker, then finishes
/// immediately so the rest of the <c>End</c> state runs unchanged.
/// </summary>
internal sealed class OfferCrestAction : FsmStateAction
{
    /// <inheritdoc/>
    public override void OnEnter()
    {
        try
        {
            Plugin.RequestOffer();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CREST] Offer action failed: {ex}");
        }
        finally
        {
            Finish();
        }
    }
}
