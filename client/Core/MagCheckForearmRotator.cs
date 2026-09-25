using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace C11_TN4_Client.Core
{

    public class MagCheckForearmRotator : MonoBehaviour
    {
        // ── Configuration ─────────────────────────────────────────────────────

        private const string MagCheckLayerName = "MagCheck";

        private const string HandsLayerName = "Hands";


        private const float PollInterval = 0.1f;

        private const float DebounceDelay = 0.5f;

 
        private const bool InstantReleaseOnHandAction = false;


        private const float EquipGateTimeout = 1.5f;


        private const float EquipGraceSeconds = 0.25f;


        private const float DrawTailBlendStart = 0.75f;


        private const bool VerboseDiagnostics = false;



        private static readonly string[] EquipStateNames =
        {
            "SPAWN",
            "OUT TO IDLE",
            "OUT TO IDLE ARM",
            "OUT TO IDLE ARM BOLT",
        };


        private static readonly string[] SettledStateNames =
        {
            "IDLE",
            "IDLE ARM",
        };

        private static readonly int[] EquipFullPathHashes;
        private static readonly int[] EquipShortNameHashes;
        private static readonly int[] SettledShortNameHashes;

        static MagCheckForearmRotator()
        {
            EquipFullPathHashes  = new int[EquipStateNames.Length];
            EquipShortNameHashes = new int[EquipStateNames.Length];

            for (int i = 0; i < EquipStateNames.Length; i++)
            {
                EquipFullPathHashes[i]  =
                    Animator.StringToHash(HandsLayerName + "." + EquipStateNames[i]);
                EquipShortNameHashes[i] = Animator.StringToHash(EquipStateNames[i]);
            }

            SettledShortNameHashes = new int[SettledStateNames.Length];
            for (int i = 0; i < SettledStateNames.Length; i++)
                SettledShortNameHashes[i] = Animator.StringToHash(SettledStateNames[i]);
        }


        private Player.FirearmController _controller;
        private Weapon                   _weapon;

        private IAnimator _animator;
        private int       _magCheckLayer = -1;


        private Animator _unityAnimator;
        private int      _handsLayer = -1;

        // ── State ─────────────────────────────────────────────────────────────

        private bool  _tracking;                // false = no eligible weapon in hands
        private bool  _hasMag        = true;    // committed state
        private bool  _pendingHasMag = true;    // observed state, awaiting debounce
        private float _pendingTimer;

        private float _blendT;                  // 0 = mag present, 1 = full pose
        private float _targetBlendT;

        private float _pollTimer;

        private bool  _equipGateOpen;
        private float _equipGateTimer;
        private bool  _sawEquipState;


        private float _equipGateScale;


        private float _lastWrittenWeight = -1f;

        // ── Diagnostics ───────────────────────────────────────────────────────

        private float _nextDiagLog;

        private void LogDiag(string msg)
        {
            if (Time.time < _nextDiagLog) return;
            _nextDiagLog = Time.time + 2f;
            C11Plugin.DebugLog($"[MagCheckForearmRotator][diag] {msg}");
        }

        // ── Poll + apply ──────────────────────────────────────────────────────

        private void Update()
        {
            _pollTimer -= Time.deltaTime;
            if (_pollTimer <= 0f)
            {
                _pollTimer = PollInterval;
                RefreshHandsState();

                if (_tracking)
                    PollMagState();
            }

            if (!_tracking && _blendT <= 0f) return;

            if (!_tracking)
            {
                _targetBlendT = 0f;
            }


            if (_tracking && !_equipGateOpen)
                UpdateEquipGate();


            bool leftHandBusy = _tracking && IsLeftHandBusy();
            if (leftHandBusy)
            {
                _targetBlendT = 0f;
                if (InstantReleaseOnHandAction) _blendT = 0f;
            }

            if (!_equipGateOpen)
            {

                _blendT = _targetBlendT * _equipGateScale;
            }
            else
            {

                _blendT = Mathf.MoveTowards(
                    _blendT, _targetBlendT,
                    Time.deltaTime * Mathf.Max(0.01f, C11Plugin.ForearmBlendSpeed.Value));
            }

            ApplyLayerWeight(leftHandBusy);
        }


        private void UpdateEquipGate()
        {
            _equipGateTimer += Time.deltaTime;

            if (_unityAnimator == null || _handsLayer < 0)
            {
 
                _equipGateScale = 0f;
                if (_equipGateTimer >= EquipGraceSeconds)
                    OpenEquipGate("no animator — grace elapsed");
                return;
            }

            AnimatorStateInfo current;
            bool inTransition;
            bool equipping;

            try
            {
                current      = _unityAnimator.GetCurrentAnimatorStateInfo(_handsLayer);
                inTransition = _unityAnimator.IsInTransition(_handsLayer);

                equipping = IsEquipState(current);

                if (!equipping && inTransition)
                    equipping = IsEquipState(_unityAnimator.GetNextAnimatorStateInfo(_handsLayer));
            }
            catch
            {
                _equipGateScale = 0f;
                if (_equipGateTimer >= EquipGraceSeconds)
                    OpenEquipGate("animator lost — grace elapsed");
                return;
            }

            if (equipping)
            {
                _sawEquipState  = true;
                _equipGateScale = DrawTailScale(current);

                if (_equipGateTimer >= EquipGateTimeout)
                    OpenEquipGate("timeout (still in equip state)");
                return;
            }

            if (_sawEquipState)
            {
                OpenEquipGate($"draw finished ({_equipGateTimer:F2}s)");
                return;
            }

            if (!inTransition && IsSettledState(current))
            {
                OpenEquipGate("no draw — hands layer already settled");
                return;
            }

            _equipGateScale = 0f;

            if (_equipGateTimer >= EquipGraceSeconds)
            {

                OpenEquipGate("no draw detected — animator was already past SPAWN");
            }
        }


        private static float DrawTailScale(AnimatorStateInfo info)
        {
            float t = info.loop
                ? Mathf.Repeat(info.normalizedTime, 1f)
                : Mathf.Clamp01(info.normalizedTime);

            float s = Mathf.InverseLerp(DrawTailBlendStart, 1f, t);
            return s * s * (3f - 2f * s);
        }


        private static bool IsSettledState(AnimatorStateInfo info)
        {
            for (int i = 0; i < SettledShortNameHashes.Length; i++)
                if (info.shortNameHash == SettledShortNameHashes[i]) return true;

            return info.normalizedTime >= 1f;
        }

        private void OpenEquipGate(string reason)
        {
            _equipGateOpen  = true;
            _equipGateScale = 1f;

            C11Plugin.DebugLog(
                $"[MagCheckForearmRotator] Equip gate open after " +
                $"{_equipGateTimer:F2}s — {reason}.");
        }

        private static bool IsEquipState(AnimatorStateInfo info)
        {
            for (int i = 0; i < EquipFullPathHashes.Length; i++)
            {
                if (info.fullPathHash  == EquipFullPathHashes[i])  return true;
                if (info.shortNameHash == EquipShortNameHashes[i]) return true;
            }
            return false;
        }

        private void ApplyLayerWeight(bool leftHandBusy)
        {
            if (_animator == null || _magCheckLayer < 0) return;

   
            if (!Mathf.Approximately(_blendT, _lastWrittenWeight))
            {
                _animator.SetLayerWeight(_magCheckLayer, _blendT);
                _lastWrittenWeight = _blendT;
            }

            if (VerboseDiagnostics)
                LogDiag(
                    $"blendT={_blendT:F3} target={_targetBlendT:F3} " +
                    $"hasMag={_hasMag} leftHandBusy={leftHandBusy} " +
                    $"gate={(_equipGateOpen ? "open" : "closed")} " +
                    $"layer={_magCheckLayer}");
        }


        private bool IsLeftHandBusy()
        {
            var firearmsAnimator = _controller?.FirearmsAnimator;
            if (firearmsAnimator == null) return false;

            try
            {
                return firearmsAnimator.IsHandsProcessing();
            }
            catch (Exception ex)
            {

                LogOnce(ref _loggedHandsProcessingFailure,
                    $"IsHandsProcessing() unavailable ({ex.GetType().Name}) — " +
                    $"pose will not release during left-hand actions.");
                return false;
            }
        }


        private void ReleaseLayer()
        {
            if (_animator == null || _magCheckLayer < 0) return;

            try { _animator.SetLayerWeight(_magCheckLayer, 0f); }
            catch { /* animator already torn down */ }

            _lastWrittenWeight = -1f;
        }

        private void OnDisable()
        {
            ReleaseLayer();
        }

        private void RefreshHandsState()
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null)
            {
                if (_tracking) ClearState();
                return;
            }

            var controller = player.HandsController as Player.FirearmController;
            if (controller == null)
            {
                if (_tracking) ClearState();
                return;
            }

            Weapon weapon = controller.Item;
            if (weapon == null)
            {
                if (_tracking) ClearState();
                return;
            }

            if (ReferenceEquals(weapon, _weapon)) return;


            ReleaseLayer();
            _animator      = null;
            _unityAnimator = null;
            _magCheckLayer = -1;
            _handsLayer    = -1;
            _tracking      = false;

            _controller = controller;
            _weapon     = weapon;


            string templateId = weapon.TemplateId.ToString();

            if (!C11Plugin.MagRotatorTemplateIds.Contains(templateId))
                return;   

            _animator = controller.FirearmsAnimator?.Animator;
            if (_animator == null)
            {

                _weapon = null;
                return;
            }

            _magCheckLayer = _animator.GetLayerIndex(MagCheckLayerName);
            if (_magCheckLayer < 0)
            {
                C11Plugin.DebugLog(
                    $"[MagCheckForearmRotator] No '{MagCheckLayerName}' layer on " +
                    $"{templateId} — has the layer been added and the bundle rebuilt?");
                _animator = null;
                return;
            }


            ResolveUnityAnimator(controller);

            _tracking     = true;
            _hasMag       = _pendingHasMag = CheckHasMag();
            _pendingTimer = 0f;

      
            _blendT        = 0f;
            _lastWrittenWeight = -1f;
            _targetBlendT  = _hasMag ? 0f : 1f;

            _equipGateOpen  = false;
            _equipGateTimer = 0f;
            _sawEquipState  = false;
            _equipGateScale = 0f;

            C11Plugin.DebugLog(
                $"[MagCheckForearmRotator] Tracking {templateId} | " +
                $"hasMag:{_hasMag} | layer={_magCheckLayer} | " +
                $"handsLayer={_handsLayer} | gate=closed");
        }

       
        private void ResolveUnityAnimator(Player.FirearmController controller)
        {
            _unityAnimator = null;
            _handsLayer    = -1;

            const BindingFlags FLAGS =
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            object firearmsAnimator = null;
            try { firearmsAnimator = controller.FirearmsAnimator; }
            catch { }

            if (firearmsAnimator == null)
            {
                LogOnce(ref _loggedUnityAnimatorFailure,
                    "FirearmsAnimator unavailable — equip gate will use its timeout.");
                return;
            }

            Animator found = null;

            try
            {
                object wrapper = null;
                PropertyInfo p = firearmsAnimator.GetType().GetProperty("Animator", FLAGS);
                if (p != null) wrapper = p.GetValue(firearmsAnimator, null);

                if (wrapper is Animator direct) found = direct;
                else if (wrapper != null)       found = SearchForAnimator(wrapper, FLAGS);

                if (found == null) found = SearchForAnimator(firearmsAnimator, FLAGS);
            }
            catch { }

            if (found == null)
            {
                LogOnce(ref _loggedUnityAnimatorFailure,
                    "Could not reach the UnityEngine.Animator — equip gate will " +
                    "use its timeout instead of watching the draw.");
                return;
            }

            _unityAnimator = found;

            try
            {
                for (int i = 0; i < found.layerCount; i++)
                {
                    if (found.GetLayerName(i) != HandsLayerName) continue;
                    _handsLayer = i;
                    break;
                }
            }
            catch { }

            if (_handsLayer < 0)
                LogOnce(ref _loggedHandsLayerFailure,
                    $"No '{HandsLayerName}' layer found — equip gate will use its timeout.");
        }

        private static Animator SearchForAnimator(object host, BindingFlags flags)
        {
            Type t = host.GetType();

            foreach (FieldInfo f in t.GetFields(flags))
            {
                try { if (f.GetValue(host) is Animator a && a != null) return a; }
                catch { }
            }

            foreach (PropertyInfo p in t.GetProperties(flags))
            {
                if (p.GetIndexParameters().Length != 0) continue;
                try { if (p.GetValue(host, null) is Animator a && a != null) return a; }
                catch { }
            }

            return null;
        }

        private void ClearState()
        {
            ReleaseLayer();

            _tracking      = false;
            _weapon        = null;
            _controller    = null;
            _animator      = null;
            _unityAnimator = null;
            _magCheckLayer = -1;
            _handsLayer    = -1;

            _equipGateOpen  = false;
            _equipGateTimer = 0f;
            _sawEquipState  = false;
            _equipGateScale = 0f;
        }

        private void PollMagState()
        {
            bool observed = CheckHasMag();

            if (observed != _pendingHasMag)
            {
                _pendingHasMag = observed;
                _pendingTimer  = 0f;
                return;
            }

            if (observed == _hasMag) return;   

            _pendingTimer += PollInterval;
            if (_pendingTimer < DebounceDelay) return;

            _hasMag       = observed;
            _targetBlendT = _hasMag ? 0f : 1f;
        }

        // ── Detection helpers ─────────────────────────────────────────────────

        private bool CheckHasMag()
        {
            if (_weapon == null) return true;   

            try
            {
                return _weapon.GetCurrentMagazine() != null;
            }
            catch (Exception ex)
            {
                LogOnce(ref _loggedMagPrimaryFailure,
                    $"GetCurrentMagazine() threw {ex.GetType().Name}: {ex.Message} " +
                    $"— using slot fallback.");

                try
                {
                    foreach (Slot slot in _weapon.Slots)
                    {
                        if (slot.Name != "mod_magazine") continue;
                        return slot.ContainedItem != null;
                    }

                    LogOnce(ref _loggedMagFallbackFailure,
                        "No 'mod_magazine' slot on this weapon — mag state cannot " +
                        "be detected.");
                }
                catch (Exception inner)
                {
                    LogOnce(ref _loggedMagFallbackFailure,
                        $"CheckHasMag fallback failed: {inner.Message}");
                }
                return true;
            }
        }

        

        private bool _loggedHandsProcessingFailure;
        private bool _loggedMagPrimaryFailure;
        private bool _loggedMagFallbackFailure;
        private bool _loggedUnityAnimatorFailure;
        private bool _loggedHandsLayerFailure;

        private static void LogOnce(ref bool flag, string msg)
        {
            if (flag) return;
            flag = true;
            C11Plugin.DebugLog($"[MagCheckForearmRotator] {msg}");
        }
    }
}