using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BSG.CameraEffects;
using C11_TN4_Client.compat;
using EFT.CameraControl;
using UnityEngine;

namespace C11_TN4_Client.Core
{
    public class NvgPodRotator : MonoBehaviour
    {
        private CurveRotator     _baseRotator;
        private NvgDeviceProfile _profile;
        private FieldInfo        _float0Field;
        private Transform        _leftPod;
        private Transform        _rightPod;

        private float _manualTLeft        = 1f;
        private float _manualTargetTLeft  = 1f;
        private float _manualTRight       = 1f;
        private float _manualTargetTRight = 1f;
        private bool  _leftOverride;
        private bool  _rightOverride;

        private bool _wasFullyStowed;
        private bool _wasLeftStowed;
        private bool _wasRightStowed;

        // Static so the Harmony patch can read it without a component reference
        public static Texture     ActiveMask      = null;
        public static NightVision ManagedInstance = null;

        /// <summary>Which pod the local player has flipped up (read by BorkelCompat).</summary>
        public enum PodSide { None, Left, Right, Both }
        public static PodSide StowedPod = PodSide.None;

        /// <summary>Profile of the NVG the local player is wearing (read by BorkelCompat).</summary>
        public static NvgDeviceProfile ActiveProfile;

        // NightVision caching
        private NightVision _nightVision;

        // Backup original textures for restore
        private Texture _originalAnvis;
        private Texture _originalBino;
        private Texture _originalMono;

        // Custom masks loaded from disk
        private Texture2D _maskFull;
        private Texture2D _maskLeft;
        private Texture2D _maskRight;
        private bool      _masksBuilt;

        // Reflection fields — resolved once at class load.
        // Found by type, not name: 4.1 renamed obfuscated members like material_0 / float_0.
        private static readonly FieldInfo _material0Field =
            FindField(typeof(TextureMask), typeof(Material), "material_0");

        private static readonly MethodInfo _tryToEnable = typeof(TextureMask)
            .GetMethod("TryToEnable", BindingFlags.Public | BindingFlags.Instance);

        // ── Initialisation ────────────────────────────────────────────────────

        public void Init(CurveRotator rotator, NvgDeviceProfile nvgProfile)
        {
            _baseRotator = rotator;
            _profile     = nvgProfile;

            // The mount's animation progress (0..1, fed into AnimationCurve.Evaluate).
            // Without it Update() bails out and the pods never move.
            _float0Field = FindField(typeof(CurveRotator), typeof(float), "float_0", "_t");

            Transform searchRoot = string.IsNullOrEmpty(_profile.ChildNvgName)
                ? transform
                : FindChildContainingStatic(transform, _profile.ChildNvgName);

            if (searchRoot == null) return;

            _leftPod  = searchRoot.Find(_profile.LeftPodPath);
            _rightPod = searchRoot.Find(_profile.RightPodPath);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private bool IsLocalPlayerNVG()
        {
            if (transform.root == null) return false;
            return transform.root.name.StartsWith("PlayerSuperior");
        }

        private void EnsureNightVisionCached()
        {
            if (_nightVision != null) return;

            _nightVision = CameraManager.Instance?.NightVision; // 4.1: CameraClass -> EFT.CameraControl.CameraManager

            if (_nightVision != null && _originalAnvis == null)
            {
                _originalAnvis = _nightVision.AnvisMaskTexture;
                _originalBino  = _nightVision.BinocularMaskTexture;
                _originalMono  = _nightVision.OldMonocularMaskTexture;
                C11Plugin.DebugLog("[NvgPodRotator] NightVision cached via CameraManager.Instance");
                StartCoroutine(LogShaderDiagnostics());
            }
        }

        private IEnumerator LogShaderDiagnostics()
        {
            yield return new WaitForSeconds(5f);

            if (_nightVision?.TextureMask != null)
            {
                // Reflection by name - not a build error if it changes, so guard it
                var mat = _material0Field != null
                    ? (Material)_material0Field.GetValue(_nightVision.TextureMask)
                    : null;
                if (_material0Field == null)
                    C11Plugin.DebugLog("[NvgPodRotator] TextureMask.material_0 not found - field name may have changed in 4.1");
                C11Plugin.DebugLog($"[NvgPodRotator] TextureMask.Shader: {_nightVision.TextureMask.Shader?.name ?? "NULL"}");
                C11Plugin.DebugLog($"[NvgPodRotator] TextureMask material shader: {mat?.shader?.name ?? "NULL"}");
                C11Plugin.DebugLog($"[NvgPodRotator] TextureMask GameObject: {_nightVision.TextureMask.gameObject.name}");
                C11Plugin.DebugLog($"[NvgPodRotator] Same GameObject as NightVision: {_nightVision.TextureMask.gameObject == _nightVision.gameObject}");
                C11Plugin.DebugLog($"[NvgPodRotator] TextureMask enabled: {_nightVision.TextureMask.enabled}");
                C11Plugin.DebugLog($"[NvgPodRotator] Mask on material: {mat?.GetTexture("_Mask")?.name ?? "NULL"}");
            }
            else
            {
                C11Plugin.DebugLog("[NvgPodRotator] TextureMask is null after 5s");
            }
        }

        // ── Mount state ───────────────────────────────────────────────────────

        public void OnMountStateChanged(bool isOn, bool initial)
        {
            if (!IsLocalPlayerNVG()) return;

            EnsureNightVisionCached();

            if (isOn && (_leftOverride || _rightOverride))
            {
                _leftOverride  = false;
                _rightOverride = false;
            }

            // With Borkel's NVGs installed he owns the mask; touching it here (and re-enabling
            // the vanilla TextureMask) is what flashed the base-game mask when pressing N.
            if (BorkelCompat.Active) return;

            if (isOn && _nightVision != null)
            {
                EnsureMasksBuilt();
                if (_maskFull != null) ApplyCustomMask(_maskFull);
                else RestoreOriginalMask();
            }
        }

        // ── Update loops ──────────────────────────────────────────────────────

        private void Update()
        {
            if (_baseRotator == null || _leftPod == null || _rightPod == null || _float0Field == null) return;

            float mountProgress = (float)_float0Field.GetValue(_baseRotator);
            float mountT        = _baseRotator.AnimationCurve.Evaluate(mountProgress);
            bool  mountDeployed = mountProgress > 0.05f;

            bool isLocal = IsLocalPlayerNVG();
            if (isLocal) ActiveProfile = _profile;

            if (isLocal && mountDeployed)
            {
                if (C11Plugin.BothPodsFoldKey.Value.IsDown())
                {
                    TogglePod(ref _leftOverride,  ref _manualTargetTLeft,  ref _manualTLeft);
                    TogglePod(ref _rightOverride, ref _manualTargetTRight, ref _manualTRight);
                    PlayPodFlipFeedback();
                }
                else if (C11Plugin.LeftPodFoldKey.Value.IsDown())
                {
                    TogglePod(ref _leftOverride, ref _manualTargetTLeft, ref _manualTLeft);
                    PlayPodFlipFeedback();
                }
                else if (C11Plugin.RightPodFoldKey.Value.IsDown())
                {
                    TogglePod(ref _rightOverride, ref _manualTargetTRight, ref _manualTRight);
                    PlayPodFlipFeedback();
                }
            }

            float tLeft  = Animate(ref _manualTLeft,  ref _manualTargetTLeft,  _leftOverride,  mountT);
            float tRight = Animate(ref _manualTRight, ref _manualTargetTRight, _rightOverride, mountT);

            _leftPod.localRotation  = Quaternion.Lerp(_profile.LeftOff,  _profile.LeftOn,  tLeft);
            _rightPod.localRotation = Quaternion.Lerp(_profile.RightOff, _profile.RightOn, tRight);

            if (!isLocal) return;

            float targetLeft  = _leftOverride  ? _manualTargetTLeft  : mountT;
            float targetRight = _rightOverride ? _manualTargetTRight : mountT;

            bool leftStowed  = targetLeft  < 0.5f;
            bool rightStowed = targetRight < 0.5f;
            bool bothStowed  = leftStowed && rightStowed;

            StowedPod = bothStowed ? PodSide.Both : leftStowed ? PodSide.Left : rightStowed ? PodSide.Right : PodSide.None;

            if      (bothStowed  && !_wasFullyStowed) OnBothPodsStowed();
            else if (!bothStowed &&  _wasFullyStowed) OnPodsDeployed();
            else if (!bothStowed)
            {
                if      (leftStowed  && !_wasLeftStowed)  OnSinglePodStowed(leftStowed: true);
                else if (!leftStowed &&  _wasLeftStowed)  OnSinglePodDeployed();

                if      (rightStowed  && !_wasRightStowed) OnSinglePodStowed(leftStowed: false);
                else if (!rightStowed &&  _wasRightStowed) OnSinglePodDeployed();
            }

            _wasFullyStowed = bothStowed;
            _wasLeftStowed  = leftStowed;
            _wasRightStowed = rightStowed;
        }

        private void LateUpdate()
        {
            if (!IsLocalPlayerNVG() || _nightVision == null || _baseRotator == null || _float0Field == null) return;

            float mountProgress = (float)_float0Field.GetValue(_baseRotator);
            bool  mountDeployed = mountProgress > 0.05f;

            // Borkel's NVGs installed: he owns masking. We only switch the NVG on/off with the
            // pods, and ApplySettings makes his renderer pick up On and the pod mask.
            if (BorkelCompat.Active)
            {
                if (_wasFullyStowed && _nightVision.On)
                {
                    _nightVision.On = false;
                    _nightVision.ApplySettings();
                }
                else if (!_wasFullyStowed && mountDeployed && !_nightVision.On)
                {
                    _nightVision.On = true;
                    _nightVision.ApplySettings();
                }
                return;
            }

            if (_wasFullyStowed)
            {
                _nightVision.On = false;
                _tryToEnable?.Invoke(_nightVision.TextureMask, new object[] { _nightVision, false });
            }
            else if (mountDeployed && !_nightVision.On)
            {
                _nightVision.On = true;
                _tryToEnable?.Invoke(_nightVision.TextureMask, new object[] { _nightVision, true });

                EnsureMasksBuilt();
                if (_maskFull != null) ApplyCustomMask(_maskFull);
                else RestoreOriginalMask();
            }
        }

        private void OnDestroy()
        {
            if (ActiveProfile == _profile) { ActiveProfile = null; StowedPod = PodSide.None; }
        }

        // ── Reflection helper ─────────────────────────────────────────────────

        /// <summary>
        /// Finds a private instance field by type. Tries the old obfuscated name first,
        /// then falls back to the only field of that type on the class. If there are
        /// several, it picks none and logs them all, so the right one can be chosen.
        /// </summary>
        private static FieldInfo FindField(Type owner, Type fieldType, string oldName, params string[] knownNames)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;

            var byName = owner.GetField(oldName, flags);
            if (byName != null && byName.FieldType == fieldType) return byName;

            // Names confirmed from a 4.1 log (e.g. CurveRotator._t, beside _targetT)
            foreach (var n in knownNames)
            {
                var f = owner.GetField(n, flags);
                if (f != null && f.FieldType == fieldType) return f;
            }

            var candidates = new List<FieldInfo>();
            foreach (var f in owner.GetFields(flags))
                if (f.FieldType == fieldType) candidates.Add(f);

            if (candidates.Count == 1)
            {
                C11Plugin.DebugLog($"[NvgPodRotator] {owner.Name}.{oldName} is now '{candidates[0].Name}'");
                return candidates[0];
            }

            C11Plugin.Log.LogWarning($"[NvgPodRotator] {owner.Name}: {candidates.Count} private {fieldType.Name} fields, " +
                                     $"can't tell which replaced {oldName}. All private fields:");
            foreach (var f in owner.GetFields(flags))
                C11Plugin.Log.LogWarning($"    {f.FieldType.Name} {f.Name}");
            return null;
        }

        // ── Pod state events ──────────────────────────────────────────────────

        private void OnBothPodsStowed()
        {
            if (BorkelCompat.Active) { RefreshBorkel(); return; }

            NvgPodRotator.ActiveMask = null;
            C11Plugin.DebugLog("[NvgPodRotator] Both pods stowed → NVG OFF");
        }

        private void OnPodsDeployed()
        {
            if (BorkelCompat.Active) { RefreshBorkel(); return; }

            EnsureNightVisionCached();
            if (_nightVision == null) return;

            EnsureMasksBuilt();
            if (_maskFull != null) ApplyCustomMask(_maskFull);
            else RestoreOriginalMask();
        }

        private void OnSinglePodStowed(bool leftStowed)
        {
            if (BorkelCompat.Active) { RefreshBorkel(); return; }

            EnsureNightVisionCached();
            if (_nightVision == null || _profile.SinglePodMask == null) return;

            EnsureMasksBuilt();
            Texture2D shiftedTex = leftStowed ? _maskRight : _maskLeft;

            if (shiftedTex != null) ApplyCustomMask(shiftedTex);
            else RestoreOriginalMask();
        }

        private void OnSinglePodDeployed()
        {
            if (BorkelCompat.Active) { RefreshBorkel(); return; }

            EnsureNightVisionCached();
            if (_nightVision == null) return;

            EnsureMasksBuilt();
            if (_maskFull != null) ApplyCustomMask(_maskFull);
            else RestoreOriginalMask();
        }

        // ── Mask application ──────────────────────────────────────────────────

        /// <summary>Borkel's renderer re-reads its mask in ApplySettings; BorkelCompat supplies ours.</summary>
        private void RefreshBorkel()
        {
            EnsureNightVisionCached();
            if (_nightVision != null && _nightVision.On) _nightVision.ApplySettings();
        }

        private void ApplyCustomMask(Texture mask)
        {
            if (BorkelCompat.Active) return;             // Borkel owns masking
            if (_nightVision == null || mask == null) return;

            NvgPodRotator.ManagedInstance = _nightVision;
            NvgPodRotator.ActiveMask      = mask;
            _nightVision.ApplySettings();

            if (_nightVision.TextureMask != null && !_nightVision.TextureMask.enabled)
                _tryToEnable?.Invoke(_nightVision.TextureMask, new object[] { _nightVision, true });

            C11Plugin.DebugLog($"[NvgPodRotator] ApplyCustomMask: '{mask.name}'");
        }

        private void RestoreOriginalMask()
        {
            NvgPodRotator.ActiveMask      = null;
            NvgPodRotator.ManagedInstance = null;

            if (BorkelCompat.Active) return;             // Borkel owns masking
            if (_nightVision == null) return;
            if (_originalAnvis != null) _nightVision.AnvisMaskTexture        = _originalAnvis;
            if (_originalBino  != null) _nightVision.BinocularMaskTexture    = _originalBino;
            if (_originalMono  != null) _nightVision.OldMonocularMaskTexture = _originalMono;

            if (_profile.SinglePodMask.HasValue)
                _nightVision.SetMask(_profile.SinglePodMask.Value);

            C11Plugin.DebugLog("[NvgPodRotator] Restored default game mask logic");
        }

        // ── Mask loading ──────────────────────────────────────────────────────

        private void EnsureMasksBuilt()
        {
            if (_masksBuilt) return;

            string masksFolder = Path.Combine(C11Plugin.PluginFolder, "masks");

            if (_profile.PrefabNameContains == "Chimeara")
            {
                _maskFull  = TryLoadPng(Path.Combine(masksFolder, "chimera_full.png"),  "chimera_full")
                          ?? TryLoadPng(Path.Combine(masksFolder, "mask_chimera_full.png"), "chimera_full");
                _maskLeft  = TryLoadPng(Path.Combine(masksFolder, "chimera_left.png"),  "chimera_left")
                          ?? TryLoadPng(Path.Combine(masksFolder, "mask_chimera_left.png"), "chimera_left");
                _maskRight = TryLoadPng(Path.Combine(masksFolder, "chimera_right.png"), "chimera_right")
                          ?? TryLoadPng(Path.Combine(masksFolder, "mask_chimera_right.png"), "chimera_right");
            }
            else if (_profile.PrefabNameContains == "nvg_wilcox_l4g24_mount")
            {
                _maskFull  = TryLoadPng(Path.Combine(masksFolder, "mask_binocular.png"),     "mask_binocular");
                _maskLeft  = TryLoadPng(Path.Combine(masksFolder, "mask_old_monocular.png"), "mask_old_monocular");
                _maskRight = TryLoadPng(Path.Combine(masksFolder, "mask_old_monocular.png"), "mask_old_monocular");
            }
            else
            {
                _maskLeft  = TryLoadPng(Path.Combine(masksFolder, "mask_left.png"),  "mask_left");
                _maskRight = TryLoadPng(Path.Combine(masksFolder, "mask_right.png"), "mask_right");
            }

            // Generate shifted half-masks at runtime if left/right not found on disk
            if (_maskLeft == null || _maskRight == null)
            {
                Texture sourceMask = _maskFull != null ? (Texture)_maskFull : _originalAnvis;
                if (sourceMask != null)
                {
                    Texture2D readable = MakeReadableCopy(sourceMask);
                    if (readable != null)
                    {
                        int shift = readable.width / 4;
                        if (_maskLeft  == null) _maskLeft  = GenerateShiftedMask(readable, -shift, "mask_left_gen");
                        if (_maskRight == null) _maskRight = GenerateShiftedMask(readable,  shift, "mask_right_gen");
                        Destroy(readable);
                    }
                }
            }

            _masksBuilt = true;
            C11Plugin.DebugLog($"[NvgPodRotator] Masks built — full:{_maskFull?.name ?? "NULL"} left:{_maskLeft?.name ?? "NULL"} right:{_maskRight?.name ?? "NULL"}");
        }

        private static Texture2D TryLoadPng(string path, string texName)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path))) return null;
                tex.name     = texName;
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            catch (Exception) { return null; }
        }

        internal static Texture2D MakeReadableCopy(Texture source)
        {
            try
            {
                var rt   = RenderTexture.GetTemporary(source.width, source.height, 0);
                var prev = RenderTexture.active;
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply();
                copy.wrapMode = TextureWrapMode.Clamp;
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                return copy;
            }
            catch (Exception) { return null; }
        }

        internal static Texture2D GenerateShiftedMask(Texture2D src, int shiftX, string texName)
        {
            int     w    = src.width;
            int     h    = src.height;
            Color[] srcP = src.GetPixels();
            Color[] dstP = new Color[w * h];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sx = x - shiftX;
                dstP[y * w + x] = (sx >= 0 && sx < w) ? srcP[y * w + sx] : Color.black;
            }

            var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            result.name     = texName;
            result.wrapMode = TextureWrapMode.Clamp;
            result.SetPixels(dstP);
            result.Apply();
            return result;
        }

        // ── Pod flip feedback ─────────────────────────────────────────────────

        // Player.SwitchHeadLightsAnimation() plays the hand-to-helmet reach on its own, without
        // switching any device. ToggleGoggles() (the N key) would flip the whole mount instead.
        // Player.PlayNightVisionSound() is the click N makes. Both found by the 4.1 probe;
        // looked up by name so a future rename just disables the feedback instead of breaking.
        private static readonly MethodInfo _headReachAnim =
            typeof(EFT.Player).GetMethod("SwitchHeadLightsAnimation", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
        private static readonly MethodInfo _nvgClickSound =
            typeof(EFT.Player).GetMethod("PlayNightVisionSound", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);

        private static void PlayPodFlipFeedback()
        {
            var player = Comfort.Common.Singleton<EFT.GameWorld>.Instance?.MainPlayer;
            if (player == null) return;

            try
            {
                if (C11Plugin.PodFlipAnimation.Value) _headReachAnim?.Invoke(player, null);
                if (C11Plugin.PodFlipSound.Value)     _nvgClickSound?.Invoke(player, null);
            }
            catch (Exception e)
            {
                C11Plugin.DebugLog($"[NvgPodRotator] Pod flip feedback failed: {e.InnerException?.Message ?? e.Message}");
            }
        }

        // ── Utilities ─────────────────────────────────────────────────────────

        private static void TogglePod(ref bool isOverride, ref float targetT, ref float currentT)
        {
            isOverride = !isOverride;
            targetT    = isOverride ? 0f : 1f;
            if (!isOverride) currentT = 0f;
        }

        private static float Animate(ref float currentT, ref float targetT, bool isOverride, float mountT)
        {
            if (!isOverride && Mathf.Approximately(currentT, targetT))
            {
                currentT = targetT = mountT;
                return mountT;
            }
            currentT = Mathf.MoveTowards(currentT, targetT, Time.deltaTime * C11Plugin.ManualFoldSpeed.Value);
            return currentT;
        }

        public static Transform FindChildContainingStatic(Transform root, string nameFragment)
        {
            var matches = new List<Transform>();
            CollectChildrenContainingStatic(root, nameFragment, matches);
            foreach (var t in matches)
                if (t.name.Contains("(Clone)")) return t;
            return matches.Count > 0 ? matches[0] : null;
        }

        private static void CollectChildrenContainingStatic(Transform root, string nameFragment, List<Transform> results)
        {
            foreach (Transform child in root)
            {
                if (child.name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    results.Add(child);
                CollectChildrenContainingStatic(child, nameFragment, results);
            }
        }
    }
}