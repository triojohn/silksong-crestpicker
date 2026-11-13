using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CrestPicker.Pure;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrestPicker;

/// <summary>
/// Prays without pause while the hero stands near the chapel maid after the first talk,
/// but only while <see cref="OfferGate"/> finds more crests in the saves than unlocked here.
/// The component lives on the maid object and is attached by <see cref="Tick"/> once the
/// Bonetown scene is loaded. Nothing is sent to any PlayMaker FSM.
/// </summary>
internal sealed class ChurchkeeperGreeting : MonoBehaviour
{
    private const string SceneName = "Bonetown";
    private const string ObjectName = "Churchkeeper";

    /// <summary>FSM on the maid whose state changes are logged during the first prayer loop.</summary>
    private const string FsmName = "Conversation";

    /// <summary>Voice table the maid calls out with when the hero walks in.</summary>
    private const string NoticeTableName = "CM_notice";

    /// <summary>Voice table used when a sung clip cannot be found by name.</summary>
    private const string SingTableName = "CM_sing";

    /// <summary>Sung clips, played in this order round-robin so the two never repeat back to back.</summary>
    private static readonly string[] SingClipNames = { "CM_sing_01", "CM_sing_02" };

    /// <summary>Clips tried for the call-out when the <see cref="NoticeTableName"/> table is not loaded.</summary>
    private static readonly string[] NoticeClipNames = { "CM_startle_short_01", "CM_startle_04" };

    /// <summary>Child object holding the maid's own voice source.</summary>
    private const string VoiceObjectName = "CrestPicker Voice";

    /// <summary>Seconds between two scene searches for the maid.</summary>
    private const float FindInterval = 1f;

    /// <summary>Longest wait for the look animator's own turn clip before the prayer starts.</summary>
    private const float TurnSettle = 0.15f;

    /// <summary>Extra time after a clip ends during which the animator and the FSM are watched.</summary>
    private const float ClipTail = 1f;

    /// <summary>How many times a clip may be restarted if something overrides it.</summary>
    private const int MaxRestarts = 2;

    /// <summary>Longest the lines may stay up before the flow is dropped as stuck.</summary>
    private const float TalkTimeout = 60f;

    private static float _nextFind;

    private readonly Dictionary<string, AudioClip> _clips = new();
    private readonly HashSet<string> _missing = new();

    private tk2dSpriteAnimator? _animator;
    private tk2dLookAnimNPC? _look;
    private AudioSource? _voice;
    private RandomAudioClipTable? _notice;
    private RandomAudioClipTable? _sing;
    private bool _noticeSearched;
    private bool _singSearched;
    private PlayMakerNPC? _npc;
    private PlayMakerFSM? _fsm;
    private Action<FsmState>? _fsmHandler;
    private string? _fsmState;
    private bool _fsmHooked;
    private string? _lastClip;
    private float _diagStart;
    private float _diagUntil = float.NegativeInfinity;
    private bool _praying;
    private int _loopCount;

    /// <summary>
    /// Round-robin position in <see cref="SingClipNames"/>. Static on purpose: the maid object
    /// is rebuilt with the scene, so an instance field started over on every walk-in.
    /// </summary>
    private static int _singIndex;

    /// <summary>Running number of sung lines over all visits, logged as <c>n</c>.</summary>
    private static int _singCount;

    private bool _noticed;
    private bool _lookOff;
    private bool _skipLogged;
    private bool _noticeUnavailableLogged;

    private readonly OfferTalkFlow _flow = new();
    private CrestOfferSpeaker? _speaker;
    private float _talkStart;

    /// <summary>Finds the maid in Bonetown and attaches the greeting once. Never throws.</summary>
    internal static void Tick()
    {
        if (Time.unscaledTime < _nextFind)
        {
            return;
        }

        _nextFind = Time.unscaledTime + FindInterval;
        try
        {
            Attach();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GREET] attach failed: {ex.Message}");
        }
    }

    private static void Attach()
    {
        if (SceneManager.GetActiveScene().name != SceneName)
        {
            return;
        }

        foreach (PlayMakerNPC npc in Resources.FindObjectsOfTypeAll<PlayMakerNPC>())
        {
            if (npc == null)
            {
                continue;
            }

            GameObject go = npc.gameObject;
            if (!go.scene.IsValid() || go.scene.name != SceneName || go.name != ObjectName)
            {
                continue;
            }

            if (go.GetComponent<ChurchkeeperGreeting>() != null)
            {
                return;
            }

            go.AddComponent<ChurchkeeperGreeting>();
            Plugin.Log.LogInfo($"[GREET] attached to '{Path(go.transform)}'");
            return;
        }
    }

    private void Awake()
    {
        _npc = GetComponent<PlayMakerNPC>();
        _look = GetComponent<tk2dLookAnimNPC>();
        _animator = GetComponent<tk2dSpriteAnimator>();
        _voice = CreateVoice();

        FindFsm();
        DumpActors();
        DumpClipEvents();
    }

    private void OnDestroy()
    {
        if (_fsm != null && _fsm.Fsm != null && _fsmHandler != null)
        {
            _fsm.Fsm.StateChanged -= _fsmHandler;
        }
    }

    /// <summary>Remembers the conversation FSM so its state switches can be logged.</summary>
    private void FindFsm()
    {
        if (_fsm != null)
        {
            return;
        }

        foreach (PlayMakerFSM fsm in GetComponents<PlayMakerFSM>())
        {
            if (fsm != null && fsm.FsmName == FsmName)
            {
                _fsm = fsm;
                return;
            }
        }
    }

    /// <summary>Logs once which components on the maid can touch the sprite animation.</summary>
    private void DumpActors()
    {
        StringBuilder line = new StringBuilder("[GREET] actors:");
        line.Append($" tk2dLookAnimNPC(enabled={_look != null && _look.enabled} state={(_look != null ? _look.State.ToString() : "none")})");
        line.Append($" tk2dSpriteAnimator(playing={_animator != null && _animator.Playing} clip='{_animator?.CurrentClip?.name ?? "none"}')");

        PlayMakerFSM[] fsms = GetComponents<PlayMakerFSM>();
        line.Append($" fsms={fsms.Length}");
        foreach (PlayMakerFSM fsm in fsms)
        {
            if (fsm != null)
            {
                line.Append($" '{fsm.FsmName}'='{fsm.ActiveStateName}'");
            }
        }

        tk2dAnimationTriggerResponder? responder = GetComponent<tk2dAnimationTriggerResponder>();
        line.Append(responder != null
            ? $" tk2dAnimationTriggerResponder(enabled={responder.enabled})"
            : " tk2dAnimationTriggerResponder=none");
        Plugin.Log.LogInfo(line.ToString());
    }

    /// <summary>Logs every animator clip with its duration and the frames that carry events, once on attach.</summary>
    private void DumpClipEvents()
    {
        tk2dSpriteAnimation? library = _animator != null ? _animator.Library : null;
        if (library == null || library.clips == null)
        {
            Plugin.Log.LogInfo("[GREET] clips unavailable");
            return;
        }

        foreach (tk2dSpriteAnimationClip clip in library.clips)
        {
            if (clip == null || clip.frames == null)
            {
                continue;
            }

            StringBuilder line = new StringBuilder();
            int events = 0;
            for (int i = 0; i < clip.frames.Length; i++)
            {
                tk2dSpriteAnimationFrame? frame = clip.frames[i];
                if (frame == null)
                {
                    continue;
                }

                string info = frame.eventInfo ?? string.Empty;
                if (info.Length == 0 && frame.eventInt == 0 && frame.eventFloat == 0f)
                {
                    continue;
                }

                events++;
                line.Append($" frame={i} info='{info}' int={frame.eventInt} float={frame.eventFloat}");
            }

            string header = $"[GREET] clip '{clip.name}' dur={clip.Duration:F2}";
            Plugin.Log.LogInfo(events == 0 ? $"{header} events: none" : $"{header} events: {events}{line}");
        }
    }

    private void Update()
    {
        try
        {
            HookFsm();
            Watch();
            WatchTalk();
            Step();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GREET] tick failed: {ex.Message}");
        }
    }

    /// <summary>Subscribes to the conversation FSM once it exists; it is not built in Awake.</summary>
    private void HookFsm()
    {
        if (_fsmHooked || _fsm == null || _fsm.Fsm == null)
        {
            return;
        }

        _fsmHooked = true;
        _fsmState = _fsm.Fsm.ActiveStateName;
        _fsmHandler = OnFsmStateChanged;
        _fsm.Fsm.StateChanged += _fsmHandler;
    }

    private void OnFsmStateChanged(FsmState state)
    {
        string now = state != null ? state.Name : "none";
        if (Time.unscaledTime <= _diagUntil)
        {
            Plugin.Log.LogInfo($"[GREET] fsm '{FsmName}' state '{_fsmState ?? "none"}' -> '{now}'");
        }

        _fsmState = now;
    }

    /// <summary>Inside the wave window: logs every animator clip switch with its time offset.</summary>
    private void Watch()
    {
        if (Time.unscaledTime > _diagUntil)
        {
            return;
        }

        string? name = _animator != null && _animator.CurrentClip != null ? _animator.CurrentClip.name : null;
        if (name == _lastClip)
        {
            return;
        }

        bool playing = _animator != null && _animator.Playing;
        Plugin.Log.LogInfo(
            $"[GREET] clip changed '{_lastClip ?? "none"}' -> '{name ?? "none"}'" +
            $" playing={playing} t={Time.unscaledTime - _diagStart:F2}");
        _lastClip = name;
    }

    /// <summary>Starts the diagnostic window of one wave and resets the watched clip.</summary>
    private void OpenWindow(tk2dSpriteAnimationClip clip)
    {
        _diagStart = Time.unscaledTime;
        _diagUntil = _diagStart + clip.Duration + ClipTail;
        _lastClip = _animator != null && _animator.CurrentClip != null ? _animator.CurrentClip.name : null;
    }

    /// <summary>Plays a clip and names the clip it replaces, so every override is traceable.</summary>
    private void PlayLogged(tk2dSpriteAnimationClip clip)
    {
        string previous = _animator != null && _animator.CurrentClip != null ? _animator.CurrentClip.name : "none";
        Plugin.Log.LogInfo($"[GREET] play '{clip.name}' (previous='{previous}')");
        _animator?.Play(clip);
    }

    /// <summary>One frame: keeps the prayer running while the hero stands near, stops it otherwise.</summary>
    private void Step()
    {
        if (!DistanceToHero(out float distance, out float dx))
        {
            return;
        }

        float radius = Plugin.GreetRadius.Value;
        float exitRadius = Mathf.Max(radius, Plugin.GreetExitRadius.Value);
        bool dialogue = _npc != null && _npc.IsRunningDialogue;

        if (distance > exitRadius)
        {
            _noticed = false;
            _skipLogged = false;
            _flow.Rearm();
        }

        if (!CanPray(distance, dialogue, out string reason))
        {
            if (_praying)
            {
                StopPray(dialogue ? "dialogue" : reason);
            }

            if (distance <= radius)
            {
                Skip(reason);
            }

            return;
        }

        _skipLogged = false;
        if (!_praying)
        {
            BeginPray(dx, distance);
        }
    }

    /// <summary>True while the prayer may run: hero near, no talk, no menu, chain still pending.</summary>
    private bool CanPray(float distance, bool dialogue, out string reason)
    {
        if (!Plugin.GreetEnabled.Value)
        {
            reason = "greet disabled";
            return false;
        }

        if (distance > Plugin.GreetRadius.Value)
        {
            reason = "far";
            return false;
        }

        if (_flow.Talking)
        {
            reason = "talk";
            return false;
        }

        if (dialogue)
        {
            reason = "dialogue running";
            return false;
        }

        if (CrestSelectMenu.IsOpen)
        {
            reason = "menu open";
            return false;
        }

        if (!Plugin.EnableSnailChoice)
        {
            reason = "snail choice off";
            return false;
        }

        PlayerData? data = PlayerData.instance;
        if (data == null || !data.churchKeeperIntro)
        {
            reason = "intro not done";
            return false;
        }

        if (!OfferGateRuntime.ShouldRunChain())
        {
            reason = "chain done";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>Starts the prayer loop: turns to the hero, calls out once, dumps the audio on the first loop.</summary>
    private void BeginPray(float dx, float distance)
    {
        tk2dSpriteAnimationClip? clip = FindClip(Plugin.GreetClip.Value);
        if (clip == null)
        {
            Plugin.Log.LogWarning($"[GREET] skipped: clip '{Plugin.GreetClip.Value}' not found");
            return;
        }

        _praying = true;
        _loopCount = 0;
        _noticeSearched = _notice == null;
        _singSearched = _sing == null;
        FindFsm();
        Plugin.Log.LogInfo($"[GREET] pray start (dist={distance:F1}, clip={clip.name})");
        StartCoroutine(Pray(GreetingPolicy.FacingLeft(dx), clip));

        if (!_noticed)
        {
            _noticed = true;
            PlayNotice();
        }

        DumpAudio();
    }

    /// <summary>
    /// Turns to the hero, lets the look animator finish its own turn clip, then repeats the prayer
    /// clip while <see cref="_praying"/> holds. The look animator stays off for the whole loop:
    /// deactivating the component alone would not stop its running coroutines, hence the public call.
    /// </summary>
    private IEnumerator Pray(bool left, tk2dSpriteAnimationClip clip)
    {
        if (_look != null)
        {
            _look.ForceTurn(left);
            _look.UnlockTurn();
        }

        float settle = Time.unscaledTime + TurnSettle;
        while (_animator != null && _animator.Playing && Time.unscaledTime < settle)
        {
            yield return null;
        }

        if (_animator == null)
        {
            _praying = false;
            yield break;
        }

        HoldIdle();

        int every = Mathf.Max(1, Plugin.SingEveryLoops.Value);
        while (_praying)
        {
            _loopCount++;
            if (_loopCount % every == 0)
            {
                Sing();
            }

            yield return PlayAndWait(clip, _loopCount == 1);

            if (_loopCount == 1)
            {
                TryTalk();
            }
        }
    }

    /// <summary>Stops the loop, hands the maid back to her idle animator and names the reason.</summary>
    private void StopPray(string reason)
    {
        _praying = false;
        ReleaseIdle();
        StopSing();
        Plugin.Log.LogInfo($"[GREET] pray stopped (reason={reason})");
    }

    /// <summary>
    /// Speaks the configured lines once per approach. Nothing to say, a missing speaker or an
    /// already used offer falls through to <see cref="Plugin.RequestOffer"/> at once.
    /// </summary>
    private void TryTalk()
    {
        bool alreadyOffered = CrestOfferState.IsOffered(Plugin.CurrentSlotId());
        bool ignoreOffered = Plugin.IgnoreAlreadyOffered.Value;
        if (OfferTalkFlow.ShouldSkipTalk(alreadyOffered, ignoreOffered))
        {
            _flow.Abort();
            Plugin.RequestOffer();
            return;
        }

        if (alreadyOffered)
        {
            Plugin.Log.LogInfo("[TALK] alreadyOffered ignored=True");
        }

        if (!_flow.Begin())
        {
            return;
        }

        _talkStart = Time.unscaledTime;
        _speaker ??= CrestOfferSpeaker.Attach(gameObject);

        string text = OfferLines.Compose(new[]
        {
            Plugin.OfferLine1.Value,
            Plugin.OfferLine2.Value,
            Plugin.OfferLine3.Value,
        });

        if (_speaker == null || !_speaker.Say(text, OnTalkFinished, OnTalkFinished))
        {
            _flow.End();
            Plugin.RequestOffer();
        }
    }

    /// <summary>The lines are over: asks for the picker once, whichever callback reported it.</summary>
    private void OnTalkFinished()
    {
        if (_flow.End())
        {
            Plugin.RequestOffer();
        }
    }

    /// <summary>Drops a talk whose callbacks will not come: the speaker is gone or the window is stuck.</summary>
    private void WatchTalk()
    {
        if (!_flow.Talking)
        {
            return;
        }

        if (_speaker == null || _speaker.gameObject == null)
        {
            AbortTalk("speaker gone");
            return;
        }

        if (Time.unscaledTime - _talkStart > TalkTimeout)
        {
            AbortTalk("timeout");
        }
    }

    private void AbortTalk(string reason)
    {
        _flow.Abort();
        Plugin.Log.LogWarning($"[TALK] aborted: {reason}");
    }

    /// <summary>Plays one clip, restarts it if something overrides it, and waits for its end.</summary>
    private IEnumerator PlayAndWait(tk2dSpriteAnimationClip clip, bool log)
    {
        if (log)
        {
            OpenWindow(clip);
            PlayLogged(clip);
        }
        else
        {
            _animator?.Play(clip);
        }

        int restarts = 0;
        float until = Time.unscaledTime + clip.Duration;
        while (_praying && _animator != null && Time.unscaledTime < until)
        {
            tk2dSpriteAnimationClip? current = _animator.CurrentClip;
            if (_animator.Playing && (current == null || current.name != clip.name) && restarts < MaxRestarts)
            {
                restarts++;
                if (log)
                {
                    PlayLogged(clip);
                    Plugin.Log.LogInfo($"[GREET] clip restarted (was='{current?.name ?? "none"}')");
                }
                else
                {
                    _animator.Play(clip);
                }
            }

            yield return null;
        }
    }

    /// <summary>Distance and horizontal offset from the maid to the hero; false without a hero.</summary>
    private bool DistanceToHero(out float distance, out float dx)
    {
        HeroController? hero = HeroController.instance;
        if (hero == null)
        {
            distance = 0f;
            dx = 0f;
            return false;
        }

        Vector3 heroPos = hero.transform.position;
        distance = Vector2.Distance(heroPos, transform.position);
        dx = heroPos.x - transform.position.x;
        return true;
    }

    /// <summary>Turns the look animator off for the whole prayer.</summary>
    private void HoldIdle()
    {
        if (_lookOff || _look == null)
        {
            return;
        }

        _look.Deactivate();
        _lookOff = true;
    }

    /// <summary>Hands the maid back to her idle animator after the prayer.</summary>
    private void ReleaseIdle()
    {
        if (!_lookOff)
        {
            return;
        }

        _lookOff = false;
        if (_look != null)
        {
            _look.Activate();
        }
    }

    /// <summary>
    /// Own source for the sung lines. A one-shot on someone else's source cannot be stopped,
    /// so the prayer piled up and kept sounding; a dedicated source stops on demand.
    /// </summary>
    private AudioSource CreateVoice()
    {
        GameObject go = new GameObject(VoiceObjectName);
        go.transform.SetParent(transform, false);
        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.mute = false;
        source.volume = 1f;
        source.priority = 128;
        source.bypassEffects = true;
        source.spatialBlend = 0f;
        return source;
    }

    /// <summary>Plays a clip on the dedicated voice source, replacing whatever it held.</summary>
    private void PlayVoiceClip(AudioClip clip)
    {
        if (_voice == null)
        {
            return;
        }

        _voice.enabled = true;
        _voice.Stop();
        _voice.clip = clip;
        _voice.Play();
    }

    /// <summary>Stops the sung line, so nothing keeps sounding after the prayer ends.</summary>
    private void StopSing()
    {
        if (_voice == null || !_voice.isPlaying)
        {
            return;
        }

        _voice.Stop();
        Plugin.Log.LogInfo("[GREET] sing stopped");
    }

    /// <summary>One call-out when the hero walks into the radius.</summary>
    private void PlayNotice()
    {
        RandomAudioClipTable? table = FindTable(NoticeTableName, ref _notice, ref _noticeSearched);
        if (table != null)
        {
            NPCSpeakingAudio.PlayVoice(table, transform.position);
            Plugin.Log.LogInfo("[GREET] notice sound");
            return;
        }

        PlayNoticeClip();
    }

    /// <summary>
    /// The notice table is not reachable by name, so the call-out falls back to a clip the maid
    /// already uses and goes through the same source as the singing.
    /// </summary>
    private void PlayNoticeClip()
    {
        foreach (string name in NoticeClipNames)
        {
            Plugin.Log.LogInfo($"[GREET] notice table missing, trying clip '{name}'");
            AudioClip? clip = FindAudio(name);
            if (clip == null || _voice == null)
            {
                continue;
            }

            PlayVoiceClip(clip);
            Plugin.Log.LogInfo($"[GREET] notice clip '{clip.name}'");
            return;
        }

        if (_noticeUnavailableLogged)
        {
            return;
        }

        _noticeUnavailableLogged = true;
        Plugin.Log.LogWarning("[GREET] notice unavailable (no table, no clip)");
    }

    /// <summary>
    /// One sung line. The two clips are played by name and in turn, so the maid never sings the
    /// same line twice in a row; the table stays as a fallback when a clip is not loaded.
    /// A line already sounding is left alone instead of being restarted.
    /// </summary>
    private void Sing()
    {
        if (_voice == null || _voice.isPlaying)
        {
            return;
        }

        string name = SingClipNames[_singIndex];
        _singIndex = (_singIndex + 1) % SingClipNames.Length;
        _singCount++;

        AudioClip? clip = FindAudio(name);
        if (clip != null)
        {
            PlayVoiceClip(clip);
            Plugin.Log.LogInfo(
                $"[GREET] sing '{clip.name}' n={_singCount} playing={_voice != null && _voice.isPlaying}" +
                $" time={clip.length:F2} isActive={_voice != null && _voice.isActiveAndEnabled}");
            return;
        }

        Missing(name);
        RandomAudioClipTable? table = FindTable(SingTableName, ref _sing, ref _singSearched);
        if (table == null)
        {
            Missing(SingTableName);
            return;
        }

        NPCSpeakingAudio.PlayVoice(table, transform.position);
        Plugin.Log.LogInfo($"[GREET] sing table='{table.name}' n={_singCount}");
    }

    /// <summary>Searches the loaded tables by name once and keeps the hit.</summary>
    private static RandomAudioClipTable? FindTable(string name, ref RandomAudioClipTable? cache, ref bool searched)
    {
        if (cache != null)
        {
            return cache;
        }

        if (searched)
        {
            return null;
        }

        searched = true;
        foreach (RandomAudioClipTable table in Resources.FindObjectsOfTypeAll<RandomAudioClipTable>())
        {
            if (table != null && table.name == name)
            {
                cache = table;
                return table;
            }
        }

        return null;
    }

    /// <summary>Loaded clip by name; a miss is retried, so a late load is still picked up.</summary>
    private AudioClip? FindAudio(string name)
    {
        if (_clips.TryGetValue(name, out AudioClip? cached) && cached != null)
        {
            return cached;
        }

        foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>())
        {
            if (clip != null && clip.name == name)
            {
                _clips[name] = clip;
                return clip;
            }
        }

        return null;
    }

    /// <summary>Logs a missing table or clip once per name.</summary>
    private void Missing(string name)
    {
        if (_missing.Add(name))
        {
            Plugin.Log.LogWarning($"[GREET] sound missing '{name}'");
        }
    }

    /// <summary>Logs the audio sources under the maid and the voice table once per approach.</summary>
    private void DumpAudio()
    {
        foreach (AudioSource source in GetComponentsInChildren<AudioSource>(true))
        {
            if (source == null)
            {
                continue;
            }

            string clip = source.clip != null ? source.clip.name : "none";
            Plugin.Log.LogInfo(
                $"[GREET] audio '{Path(source.transform)}' playing={source.isPlaying}" +
                $" clip='{clip}' vol={source.volume:F2}");
        }

        Plugin.Log.LogInfo($"[GREET] voice {TableDump(_notice)}");
        Plugin.Log.LogInfo($"[GREET] sing {TableDump(_sing)}");
    }

    /// <summary>Table name and its readable fields; anything unreadable is reported as n/a.</summary>
    private static string TableDump(RandomAudioClipTable? table)
    {
        if (table == null)
        {
            return "table=none";
        }

        StringBuilder line = new StringBuilder($"table='{table.name}'");
        try
        {
            foreach (FieldInfo field in table.GetType().GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                string value;
                try
                {
                    value = Describe(field.GetValue(table));
                }
                catch (Exception)
                {
                    value = "n/a";
                }

                line.Append($" {field.Name}={value}");
            }
        }
        catch (Exception)
        {
            line.Append(" fields=n/a");
        }

        return line.ToString();
    }

    private static string Describe(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is AudioClip clip)
        {
            return $"'{clip.name}'";
        }

        if (value is string text)
        {
            return $"'{text}'";
        }

        if (value is IEnumerable list)
        {
            var names = new List<string>();
            foreach (object? item in list)
            {
                names.Add(item is AudioClip itemClip ? itemClip.name : item?.ToString() ?? "null");
            }

            return $"[{string.Join(", ", names)}]";
        }

        return value.ToString() ?? "null";
    }

    /// <summary>Clip by name; an empty or blank name means there is no clip.</summary>
    private tk2dSpriteAnimationClip? FindClip(string name)
    {
        if (_animator == null || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _animator.GetClipByName(name);
    }

    /// <summary>Logs a skip reason once per approach.</summary>
    private void Skip(string reason)
    {
        if (_skipLogged)
        {
            return;
        }

        _skipLogged = true;
        Plugin.Log.LogInfo($"[GREET] skipped: {reason}");
    }

    private static string Path(Transform node)
    {
        string path = node.name;
        for (Transform? parent = node.parent; parent != null; parent = parent.parent)
        {
            path = parent.name + "/" + path;
        }

        return path;
    }
}
