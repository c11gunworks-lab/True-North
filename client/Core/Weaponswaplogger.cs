// using System;
// using System.Collections.Generic;
// using System.Reflection;
// using System.Text;
// using Comfort.Common;
// using EFT;
// using UnityEngine;
//
// namespace C11_TN4_Client.Core
// {
//     /// <summary>
//     /// Debug tool. Watches the weapon swap boundary specifically, to answer why
//     /// a re-equip sometimes replays the draw animation and sometimes comes up
//     /// already in IDLE.
//     ///
//     /// WeaponAnimationLogger cannot answer this. It re-resolves on a 0.25s poll
//     /// and clears its state when the weapon changes, so the two windows that
//     /// decide the outcome — the tail of the outgoing weapon's holster, and the
//     /// first few frames of the incoming animator — are precisely the ones it
//     /// misses. This component resolves per-frame, keeps a reference to the
//     /// OUTGOING animator across the swap, and keeps sampling both.
//     ///
//     /// What it reports, per swap:
//     ///
//     ///   • Where the outgoing Hands layer was left — state, normalized time,
//     ///     and whether it was mid-transition. A holster cut short leaves the
//     ///     layer somewhere other than the out state, which is the leading
//     ///     theory for why the return trip skips SPAWN.
//     ///   • Whether the incoming Animator is a NEW instance or one seen before.
//     ///     A reused instance carries its state machine position with it; a fresh
//     ///     one starts at the layer default, which IS the draw.
//     ///   • keepAnimatorControllerStateOnDisable and enabled transitions. If the
//     ///     animator is disabled and re-enabled with state retention on, no
//     ///     rebind happens and SPAWN is never re-entered.
//     ///   • A compressed replay of the last few seconds of Hands layer state
//     ///     leading into the swap.
//     ///   • A one-line verdict once the incoming weapon settles: DRAW PLAYED or
//     ///     NO DRAW, with the evidence that decided it.
//     ///
//     /// Attach alongside the other debug components:
//     ///     gameObject.AddComponent&lt;WeaponSwapLogger&gt;();
//     ///
//     /// Per-frame resolution and sampling — debug only, do not ship.
//     /// </summary>
//     public class WeaponSwapLogger : MonoBehaviour
//     {
//         // ── Tuning ────────────────────────────────────────────────────────────
//
//         private const string HandsLayerName = "Hands";
//
//         /// <summary>Frames of Hands layer history retained for replay.</summary>
//         private const int RingSize = 300;
//
//         /// <summary>How many frames of history to dump on a swap.</summary>
//         private const int ReplayFrames = 90;
//
//         /// <summary>
//         /// How long to keep sampling the OUTGOING animator after the swap. The
//         /// holster often continues to play on the old animator for a moment
//         /// after the controller has already handed over.
//         /// </summary>
//         private const float OutgoingWatchSeconds = 1.0f;
//
//         /// <summary>How long to watch the incoming weapon before ruling.</summary>
//         private const float VerdictSeconds = 2.0f;
//
//         /// <summary>
//         /// Sample every frame but only emit a history entry when something
//         /// changed, or every Nth frame regardless so the timeline stays readable
//         /// during a long hold.
//         /// </summary>
//         private const int HeartbeatFrames = 20;
//
//         // ── Equip state identification ────────────────────────────────────────
//
//         private static readonly string[] EquipStateNames =
//         {
//             "SPAWN",
//             "OUT TO IDLE",
//             "OUT TO IDLE ARM",
//             "OUT TO IDLE ARM BOLT",
//         };
//
//         private static readonly int[] EquipShortNameHashes;
//
//         static WeaponSwapLogger()
//         {
//             EquipShortNameHashes = new int[EquipStateNames.Length];
//             for (int i = 0; i < EquipStateNames.Length; i++)
//                 EquipShortNameHashes[i] = Animator.StringToHash(EquipStateNames[i]);
//         }
//
//         // ── History ───────────────────────────────────────────────────────────
//
//         private struct Snap
//         {
//             public int    Frame;
//             public int    StateHash;
//             public float  NormTime;
//             public bool   InTransition;
//             public bool   Active;
//             public bool   Enabled;
//             public string Clip;
//         }
//
//         private readonly Snap[] _ring = new Snap[RingSize];
//         private int _ringHead;
//         private int _ringCount;
//
//         // ── Tracked animators ─────────────────────────────────────────────────
//
//         private Player.FirearmController _controller;
//         private object                   _weapon;
//         private string                   _templateId = "?";
//
//         private Animator _animator;
//         private int      _handsLayer = -1;
//         private int      _activeHash = -1;
//
//         private Animator _outgoing;
//         private int      _outgoingHands = -1;
//         private string   _outgoingTemplate = "?";
//         private float    _outgoingTimer;
//
//         /// <summary>
//         /// Instance IDs seen before, with a count. A reused Animator is the
//         /// single most likely reason the state machine does not restart.
//         /// </summary>
//         private readonly Dictionary<int, int> _seenAnimators = new Dictionary<int, int>();
//
//         // ── Verdict tracking ──────────────────────────────────────────────────
//
//         private bool   _verdictPending;
//         private float  _verdictTimer;
//         private bool   _sawEquipState;
//         private string _entryClip = "?";
//         private float  _entryNormTime;
//         private bool   _entryWasEquip;
//         private bool   _incomingReused;
//         private int    _incomingSeenCount;
//         private bool   _incomingKeepState;
//
//         // Last emitted sample, for change detection.
//         private int  _lastStateHash;
//         private bool _lastEnabled = true;
//         private bool _lastActive;
//         private bool _seeded;
//
//         private int _frame;
//
//         private static void Log(string msg) => C11Plugin.DebugLog($"[SwapLog] {msg}");
//
//         // ── Main loop ─────────────────────────────────────────────────────────
//
//         private void LateUpdate()
//         {
//             _frame++;
//
//             ResolveCurrent();
//
//             if (_animator != null && _handsLayer >= 0)
//                 SampleCurrent();
//
//             WatchOutgoing();
//             UpdateVerdict();
//         }
//
//         /// <summary>
//         /// Resolved per-frame rather than on a poll. The whole question is what
//         /// happens in the handful of frames either side of the handover, so a
//         /// poll interval is the wrong granularity by an order of magnitude.
//         /// </summary>
//         private void ResolveCurrent()
//         {
//             var player = Singleton<GameWorld>.Instance?.MainPlayer;
//             var controller = player?.HandsController as Player.FirearmController;
//
//             if (controller == null)
//             {
//                 if (_animator != null)
//                 {
//                     Log($"f{_frame} — hands empty, releasing {_templateId} —");
//                     HandOver(null, null, "?");
//                 }
//                 return;
//             }
//
//             object weapon = controller.Item;
//             if (weapon == null) return;
//
//             if (ReferenceEquals(weapon, _weapon) && _animator != null) return;
//
//             _controller = controller;
//
//             string templateId;
//             try { templateId = controller.Item?.TemplateId.ToString() ?? "?"; }
//             catch { templateId = "?"; }
//
//             Animator incoming = FindUnityAnimator(controller);
//             if (incoming == null)
//             {
//                 // Prefab may not be ready yet — retry next frame rather than
//                 // caching the miss.
//                 return;
//             }
//
//             HandOver(weapon, incoming, templateId);
//         }
//
//         private void HandOver(object weapon, Animator incoming, string templateId)
//         {
//             Animator previous         = _animator;
//             int      previousHands    = _handsLayer;
//             string   previousTemplate = _templateId;
//
//             // Snapshot where the outgoing animator was left BEFORE we let go of
//             // it. This is the line that matters most.
//             string outgoingSummary = previous != null && previousHands >= 0
//                 ? DescribeLayer(previous, previousHands)
//                 : "(none)";
//
//             var sb = new StringBuilder();
//             sb.Append($"===== SWAP f{_frame}: {previousTemplate} → {templateId} =====");
//             Log(sb.ToString());
//             Log($"  outgoing {Describe(previous)} Hands left at: {outgoingSummary}");
//
//             if (_ringCount > 0)
//             {
//                 Log($"  outgoing Hands history (last {Math.Min(ReplayFrames, _ringCount)} frames):");
//                 FlushRing(ReplayFrames);
//             }
//
//             _outgoing         = previous;
//             _outgoingHands    = previousHands;
//             _outgoingTemplate = previousTemplate;
//             _outgoingTimer    = previous != null ? OutgoingWatchSeconds : 0f;
//
//             _weapon     = weapon;
//             _animator   = incoming;
//             _templateId = templateId;
//             _handsLayer = -1;
//             _activeHash = -1;
//             _ringHead   = 0;
//             _ringCount  = 0;
//             _seeded     = false;
//
//             if (incoming == null) return;
//
//             int id = incoming.GetInstanceID();
//             int seen;
//             _seenAnimators.TryGetValue(id, out seen);
//             _seenAnimators[id] = seen + 1;
//
//             _incomingReused    = seen > 0;
//             _incomingSeenCount = seen + 1;
//
//             try
//             {
//                 for (int i = 0; i < incoming.layerCount; i++)
//                 {
//                     if (incoming.GetLayerName(i) != HandsLayerName) continue;
//                     _handsLayer = i;
//                     break;
//                 }
//             }
//             catch { }
//
//             try
//             {
//                 foreach (AnimatorControllerParameter p in incoming.parameters)
//                 {
//                     if (p.name != "Active") continue;
//                     _activeHash = p.nameHash;
//                     break;
//                 }
//             }
//             catch { }
//
//             try { _incomingKeepState = incoming.keepAnimatorStateOnDisable; }
//             catch { _incomingKeepState = false; }
//
//             string reuse = _incomingReused
//                 ? $"REUSED (seen {_incomingSeenCount}x)"
//                 : "NEW instance";
//
//             Log($"  incoming {Describe(incoming)} — {reuse}, " +
//                 $"keepStateOnDisable={_incomingKeepState}, handsLayer={_handsLayer}");
//
//             if (_handsLayer >= 0)
//             {
//                 AnimatorStateInfo info;
//                 bool ok = true;
//                 try { info = incoming.GetCurrentAnimatorStateInfo(_handsLayer); }
//                 catch { info = default(AnimatorStateInfo); ok = false; }
//
//                 if (ok)
//                 {
//                     _entryClip     = DescribeClips(incoming, _handsLayer);
//                     _entryNormTime = info.normalizedTime;
//                     _entryWasEquip = IsEquipState(info);
//
//                     Log($"  incoming Hands at entry: {_entryClip} t={_entryNormTime:F2} " +
//                         $"equipState={_entryWasEquip} len={info.length:F2}s");
//                 }
//             }
//             else
//             {
//                 Log($"  incoming has no '{HandsLayerName}' layer — verdict unavailable.");
//             }
//
//             // Start the verdict window.
//             _verdictPending = _handsLayer >= 0;
//             _verdictTimer   = 0f;
//             _sawEquipState  = _entryWasEquip;
//         }
//
//         // ── Sampling ──────────────────────────────────────────────────────────
//
//         private void SampleCurrent()
//         {
//             AnimatorStateInfo info;
//             bool inTransition;
//             bool enabled;
//
//             try
//             {
//                 info         = _animator.GetCurrentAnimatorStateInfo(_handsLayer);
//                 inTransition = _animator.IsInTransition(_handsLayer);
//                 enabled      = _animator.enabled;
//             }
//             catch { return; }
//
//             bool active = false;
//             if (_activeHash != -1)
//             {
//                 try { active = _animator.GetBool(_activeHash); }
//                 catch { }
//             }
//
//             if (IsEquipState(info)) _sawEquipState = true;
//
//             bool stateChanged = info.shortNameHash != _lastStateHash;
//             bool flagChanged  = enabled != _lastEnabled || active != _lastActive;
//             bool heartbeat    = _frame % HeartbeatFrames == 0;
//
//             if (!_seeded || stateChanged || flagChanged || heartbeat)
//             {
//                 // Clip name allocates, so only fetch it when the state actually
//                 // moved; a heartbeat reuses whatever the last one was.
//                 string clip = (!_seeded || stateChanged)
//                     ? DescribeClips(_animator, _handsLayer)
//                     : LastClip();
//
//                 Push(new Snap
//                 {
//                     Frame        = _frame,
//                     StateHash    = info.shortNameHash,
//                     NormTime     = info.normalizedTime,
//                     InTransition = inTransition,
//                     Active       = active,
//                     Enabled      = enabled,
//                     Clip         = clip,
//                 });
//
//                 // Animator being switched off and on again without a rebind is
//                 // the mechanism that would preserve state across a swap, so it
//                 // is called out live rather than only in the replay.
//                 if (_seeded && enabled != _lastEnabled)
//                     Log($"f{_frame} {_templateId} animator.enabled {_lastEnabled} → {enabled} " +
//                         $"(keepStateOnDisable={_incomingKeepState})");
//
//                 if (_seeded && active != _lastActive)
//                     Log($"f{_frame} {_templateId} Active {_lastActive} → {active} " +
//                         $"| Hands {clip} t={info.normalizedTime:F2}");
//
//                 _lastStateHash = info.shortNameHash;
//                 _lastEnabled   = enabled;
//                 _lastActive    = active;
//                 _seeded        = true;
//             }
//         }
//
//         /// <summary>
//         /// Keeps sampling the weapon we just swapped away from. The holster
//         /// frequently runs on past the handover, and where it ends up is the
//         /// state the animator will still be sitting in if it gets reused.
//         /// </summary>
//         private void WatchOutgoing()
//         {
//             if (_outgoing == null || _outgoingHands < 0) return;
//
//             _outgoingTimer -= Time.deltaTime;
//
//             bool expired = _outgoingTimer <= 0f;
//
//             string summary;
//             try { summary = DescribeLayer(_outgoing, _outgoingHands); }
//             catch { summary = "(animator gone)"; expired = true; }
//
//             if (_frame % HeartbeatFrames == 0 || expired)
//                 Log($"f{_frame}   [outgoing {_outgoingTemplate}] {summary}");
//
//             if (!expired) return;
//
//             Log($"f{_frame}   [outgoing {_outgoingTemplate}] FINAL RESTING STATE: {summary}");
//             _outgoing      = null;
//             _outgoingHands = -1;
//         }
//
//         // ── Verdict ───────────────────────────────────────────────────────────
//
//         private void UpdateVerdict()
//         {
//             if (!_verdictPending) return;
//
//             _verdictTimer += Time.deltaTime;
//             if (_verdictTimer < VerdictSeconds) return;
//
//             _verdictPending = false;
//
//             string outcome = _sawEquipState ? "DRAW PLAYED" : "NO DRAW";
//
//             var sb = new StringBuilder();
//             sb.Append($"===== VERDICT {_templateId}: {outcome} =====");
//             Log(sb.ToString());
//
//             Log($"  entered on: {_entryClip} t={_entryNormTime:F2} equipState={_entryWasEquip}");
//             Log($"  animator: {(_incomingReused ? $"REUSED ({_incomingSeenCount}x)" : "NEW")}, " +
//                 $"keepStateOnDisable={_incomingKeepState}");
//
//             if (!_sawEquipState)
//             {
//                 // Spell out the correlation rather than leaving it to be
//                 // reconstructed from three separate lines later.
//                 if (_incomingReused && _incomingKeepState)
//                     Log("  → animator instance was reused AND retains controller state on " +
//                         "disable, so no rebind occurred and the layer stayed where the " +
//                         "holster left it. SPAWN is unreachable on this path.");
//                 else if (_incomingReused)
//                     Log("  → animator instance was reused. If it was never disabled, or was " +
//                         "disabled without a Rebind, the layer keeps its position and SPAWN " +
//                         "is never re-entered.");
//                 else
//                     Log("  → fresh animator instance but no equip state observed. Either the " +
//                         "layer default is not SPAWN, or the draw was skipped upstream.");
//             }
//         }
//
//         // ── Formatting ────────────────────────────────────────────────────────
//
//         private static string Describe(Animator a)
//         {
//             if (a == null) return "(null animator)";
//             try
//             {
//                 string ctrl = a.runtimeAnimatorController != null
//                     ? a.runtimeAnimatorController.name
//                     : "(no controller)";
//                 return $"animator#{a.GetInstanceID()} '{ctrl}' enabled={a.enabled}";
//             }
//             catch { return "(animator unreadable)"; }
//         }
//
//         private static string DescribeLayer(Animator a, int layer)
//         {
//             try
//             {
//                 AnimatorStateInfo info = a.GetCurrentAnimatorStateInfo(layer);
//                 return $"{DescribeClips(a, layer)} t={info.normalizedTime:F2} " +
//                        $"len={info.length:F2}s loop={info.loop} " +
//                        $"inTransition={a.IsInTransition(layer)} " +
//                        $"w={a.GetLayerWeight(layer):F2} enabled={a.enabled}";
//             }
//             catch { return "(unreadable)"; }
//         }
//
//         private static string DescribeClips(Animator a, int layer)
//         {
//             AnimatorClipInfo[] infos;
//             try { infos = a.GetCurrentAnimatorClipInfo(layer); }
//             catch { return "(clip info unavailable)"; }
//
//             if (infos == null || infos.Length == 0) return "(no clip)";
//             if (infos.Length == 1)
//                 return infos[0].clip != null ? infos[0].clip.name : "(null clip)";
//
//             var sb = new StringBuilder();
//             for (int i = 0; i < infos.Length; i++)
//             {
//                 if (i > 0) sb.Append(" + ");
//                 sb.Append(infos[i].clip != null ? infos[i].clip.name : "(null)");
//                 sb.Append($"@{infos[i].weight:F2}");
//             }
//             return sb.ToString();
//         }
//
//         private static bool IsEquipState(AnimatorStateInfo info)
//         {
//             for (int i = 0; i < EquipShortNameHashes.Length; i++)
//                 if (info.shortNameHash == EquipShortNameHashes[i]) return true;
//             return false;
//         }
//
//         // ── Ring buffer ───────────────────────────────────────────────────────
//
//         private void Push(Snap s)
//         {
//             _ring[_ringHead] = s;
//             _ringHead = (_ringHead + 1) % RingSize;
//             if (_ringCount < RingSize) _ringCount++;
//         }
//
//         private string LastClip()
//         {
//             if (_ringCount == 0) return "?";
//             int idx = (_ringHead - 1 + RingSize) % RingSize;
//             return _ring[idx].Clip ?? "?";
//         }
//
//         private void FlushRing(int max)
//         {
//             int count = Math.Min(max, _ringCount);
//             int start = (_ringHead - count + RingSize) % RingSize;
//
//             for (int i = 0; i < count; i++)
//             {
//                 Snap s = _ring[(start + i) % RingSize];
//                 Log($"    f{s.Frame} {s.Clip} t={s.NormTime:F2}" +
//                     (s.InTransition ? " [trans]" : "") +
//                     $" Active={s.Active}" +
//                     (s.Enabled ? "" : " DISABLED"));
//             }
//         }
//
//         // ── Animator resolution ───────────────────────────────────────────────
//
//         private static Animator FindUnityAnimator(Player.FirearmController controller)
//         {
//             const BindingFlags FLAGS =
//                 BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
//
//             object firearmsAnimator = null;
//             try { firearmsAnimator = controller.FirearmsAnimator; }
//             catch { }
//
//             if (firearmsAnimator == null) return null;
//
//             try
//             {
//                 object wrapper = null;
//                 PropertyInfo p = firearmsAnimator.GetType().GetProperty("Animator", FLAGS);
//                 if (p != null) wrapper = p.GetValue(firearmsAnimator, null);
//
//                 if (wrapper is Animator direct) return direct;
//
//                 if (wrapper != null)
//                 {
//                     Animator found = SearchForAnimator(wrapper, FLAGS);
//                     if (found != null) return found;
//                 }
//
//                 return SearchForAnimator(firearmsAnimator, FLAGS);
//             }
//             catch { return null; }
//         }
//
//         private static Animator SearchForAnimator(object host, BindingFlags flags)
//         {
//             Type t = host.GetType();
//
//             foreach (FieldInfo f in t.GetFields(flags))
//             {
//                 try { if (f.GetValue(host) is Animator a && a != null) return a; }
//                 catch { }
//             }
//
//             foreach (PropertyInfo p in t.GetProperties(flags))
//             {
//                 if (p.GetIndexParameters().Length != 0) continue;
//                 try { if (p.GetValue(host, null) is Animator a && a != null) return a; }
//                 catch { }
//             }
//
//             return null;
//         }
//     }
// }