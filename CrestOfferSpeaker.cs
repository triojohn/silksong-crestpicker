using System;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Talk partner for the chapel maid's lines before the crest offer. Lives on a child object
/// without a collider, so it never shows a second "Speak" prompt and never joins
/// <see cref="InteractManager"/>; it only feeds the shared dialogue window and the maid's
/// voice table. Nothing is sent to any PlayMaker FSM.
/// </summary>
internal sealed class CrestOfferSpeaker : NPCControlBase
{
    /// <summary>Child object holding the speaker; the maid object itself stays untouched.</summary>
    private const string ObjectName = "CrestPicker Speaker";

    /// <summary>Voice table used as a fallback when the maid has no <see cref="NPCSpeakingAudio"/>.</summary>
    private const string VoiceTableName = "CM_talk";

    /// <summary>Tag the game splits NPC dialogue pages on.</summary>
    private const string PageTag = "<page>";

    private string _text = string.Empty;
    private string? _voiceName;
    private Action? _onEnd;
    private Action? _onCancel;
    private bool _finished;

    /// <summary>The hero is not walked anywhere: the lines start where the prayer left her.</summary>
    protected override bool AllowMovePlayer => false;

    /// <summary>No end action: <see cref="NPCControlBase.CallEndAction"/> would touch the maid's FSM and save.</summary>
    protected override bool AutoCallEndAction => false;

    /// <summary>
    /// Creates the speaker under <paramref name="host"/>, copies the maid's voice table onto it
    /// and returns null only when the host is gone. The voice component is added after this one
    /// because its <c>Awake</c> looks the <see cref="NPCControlBase"/> up on the same object.
    /// </summary>
    /// <param name="host">The maid object; the speaker is parented to it.</param>
    internal static CrestOfferSpeaker? Attach(GameObject host)
    {
        if (host == null)
        {
            return null;
        }

        RandomAudioClipTable? table = FindVoiceTable(host);

        GameObject go = new GameObject(ObjectName);
        go.transform.SetParent(host.transform, false);
        go.transform.position = host.transform.position;

        CrestOfferSpeaker speaker = go.AddComponent<CrestOfferSpeaker>();
        NPCSpeakingAudio audio = go.AddComponent<NPCSpeakingAudio>();

        if (table != null)
        {
            audio.SetTableForSpeaker(null, table);
            speaker._voiceName = table.name;
        }
        else
        {
            Plugin.Log.LogWarning("[TALK] voice missing");
        }

        return speaker;
    }

    /// <summary>
    /// Shows the composed text in the shared window and reports the outcome once. Empty text
    /// starts nothing, so the caller can open the offer right away.
    /// </summary>
    /// <param name="composed">Text built by <see cref="Pure.OfferLines.Compose"/>.</param>
    /// <param name="onEnd">Called when the last page was shown.</param>
    /// <param name="onCancel">Called when the window was taken over or the scene was left.</param>
    internal bool Say(string composed, Action? onEnd, Action? onCancel)
    {
        if (string.IsNullOrEmpty(composed))
        {
            Plugin.Log.LogInfo("[TALK] skipped: empty text");
            return false;
        }

        _text = composed;
        _onEnd = onEnd;
        _onCancel = onCancel;
        _finished = false;
        StartDialogueImmediately();
        return true;
    }

    protected override void OnStartDialogue()
    {
        DisableInteraction();
        Plugin.Log.LogInfo($"[TALK] start lines={CountLines(_text)}");
        if (_voiceName != null)
        {
            Plugin.Log.LogInfo($"[TALK] voice table='{_voiceName}'");
        }

        DialogueBox.StartConversation(
            _text,
            this,
            overrideContinue: false,
            DialogueBox.DisplayOptions.Default,
            onDialogueEnd: () => Finish("end"),
            onDialogueCancelled: () => Finish("cancelled"));
    }

    protected override void OnEndDialogue()
    {
        EnableInteraction();
    }

    /// <summary>
    /// One outcome per <see cref="Say"/>: the game may report the end and the cancellation of
    /// the same conversation, and a forced close also runs the end callback.
    /// </summary>
    private void Finish(string reason)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        Plugin.Log.LogInfo($"[TALK] {reason}");

        Action? callback = reason == "end" ? _onEnd : _onCancel;
        _onEnd = null;
        _onCancel = null;
        callback?.Invoke();
    }

    /// <summary>Restores hero control if the object dies with the dialogue still up.</summary>
    private void OnDestroy()
    {
        if (_finished || HeroController.instance == null)
        {
            return;
        }

        EnableInteraction();
    }

    /// <summary>
    /// The maid's own voice table, else the table found by name. The public getter logs an
    /// error when the component carries no speaker, hence the catch and the name fallback.
    /// </summary>
    private static RandomAudioClipTable? FindVoiceTable(GameObject host)
    {
        try
        {
            NPCSpeakingAudio? audio = host.GetComponentInChildren<NPCSpeakingAudio>(true);
            RandomAudioClipTable? table = audio != null ? audio.Table : null;
            if (table != null)
            {
                return table;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[TALK] voice table read failed: {ex.Message}");
        }

        foreach (RandomAudioClipTable table in Resources.FindObjectsOfTypeAll<RandomAudioClipTable>())
        {
            if (table != null && table.name == VoiceTableName)
            {
                return table;
            }
        }

        return null;
    }

    /// <summary>Dialogue pages in the text: one more than the number of page tags.</summary>
    private static int CountLines(string text)
    {
        int lines = 1;
        int at = text.IndexOf(PageTag, StringComparison.Ordinal);
        while (at >= 0)
        {
            lines++;
            at = text.IndexOf(PageTag, at + PageTag.Length, StringComparison.Ordinal);
        }

        return lines;
    }
}
