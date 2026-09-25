using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BSG.CameraEffects;
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

        // Reflection fields — resolved once at class load
        private static readonly FieldInfo _material0Field = typeof(TextureMask)
            .GetField("material_0", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo _tryToEnable = typeof(TextureMask)
            .GetMethod("TryToEnable", BindingFlags.Public | BindingFlags.Instance);

        // ── Initialisation ────────────────────────────────────────────────────

        public void Init(CurveRotator rotator, NvgDeviceProfile nvgProfile)
        {
            _baseRotator = rotator;
            _profile     = nvgProfile;

            _float0Field = typeof(CurveRotator)
                .GetField("float_0", BindingFlags.NonPublic | BindingFlags.Instance);

            // Reflection by name won't show up as a build error, so say so loudly if it breaks.
            // Without this field Update() bails out and the pods never move.
            if (_float0Field == null)
                C11Plugin.Log.LogWarning("[NvgPodRotator] CurveRotator.float_0 not found - pod animation disabled. Check the field name in 4.1.");

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

            if (isLocal && mountDeployed)
            {
                if (C11Plugin.BothPodsFoldKey.Value.IsDown())
                {
                    TogglePod(ref _leftOverride,  ref _manualTargetTLeft,  ref _manualTLeft);
                    TogglePod(ref _rightOverride, ref _manualTargetTRight, ref _manualTRight);
                }
                else if (C11Plugin.LeftPodFoldKey.Value.IsDown())
                    TogglePod(ref _leftOverride, ref _manualTargetTLeft, ref _manualTLeft);
                else if (C11Plugin.RightPodFoldKey.Value.IsDown())
                    TogglePod(ref _rightOverride, ref _manualTargetTRight, ref _manualTRight);
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

        // ── Pod state events ──────────────────────────────────────────────────

        private void OnBothPodsStowed()
        {
            NvgPodRotator.ActiveMask = null;
            C11Plugin.DebugLog("[NvgPodRotator] Both pods stowed → NVG OFF");
        }

        private void OnPodsDeployed()
        {
            EnsureNightVisionCached();
            if (_nightVision == null) return;

            EnsureMasksBuilt();
            if (_maskFull != null) ApplyCustomMask(_maskFull);
            else RestoreOriginalMask();
        }

        private void OnSinglePodStowed(bool leftStowed)
        {
            EnsureNightVisionCached();
            if (_nightVision == null || _profile.SinglePodMask == null) return;

            EnsureMasksBuilt();
            Texture2D shiftedTex = leftStowed ? _maskRight : _maskLeft;

            if (shiftedTex != null) ApplyCustomMask(shiftedTex);
            else RestoreOriginalMask();
        }

        private void OnSinglePodDeployed()
        {
            EnsureNightVisionCached();
            if (_nightVision == null) return;

            EnsureMasksBuilt();
            if (_maskFull != null) ApplyCustomMask(_maskFull);
            else RestoreOriginalMask();
        }

        // ── Mask application ──────────────────────────────────────────────────

        private void ApplyCustomMask(Texture mask)
        {
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

        private static Texture2D MakeReadableCopy(Texture source)
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

        private static Texture2D GenerateShiftedMask(Texture2D src, int shiftX, string texName)
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