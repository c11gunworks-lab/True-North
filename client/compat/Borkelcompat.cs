using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Bootstrap;
using C11_TN4_Client.Core;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace C11_TN4_Client.compat
{
    /// <summary>
    /// Compatibility with Borkel's Realistic NVGs (com.borkel.nvgmasks).
    ///
    /// Borkel loads each NVG from its own folder, BorkelRNVG\Assets\NVG\&lt;name&gt;\, holding
    /// config.json (with "itemId" / "itemIds"), lens.png and mask.png, and renders night vision
    /// through his own renderer. When his mod is installed NvgPodRotator does no masking at all;
    /// Borkel's mask.png covers both-pods-down. The only thing he can't do is a flipped pod, so
    /// this adds optional files beside his mask.png and lens.png:
    ///
    ///   mask_left.png  / lens_left.png    only the LEFT pod down  (right flipped up)
    ///   mask_right.png / lens_right.png   only the RIGHT pod down (left flipped up)
    ///
    /// While a pod is flipped, the lens and mask he passes to
    /// RealisticNightVisionRenderer.ConfigureRuntime (its "lens" and "overlay" arguments) are
    /// swapped for the matching ones. Each is optional: a missing file keeps his own.
    /// His NvgData is never touched.
    /// His loader only reads the three files above, so the extra PNGs don't affect him.
    ///
    /// Everything goes through reflection: no compile-time reference to his DLL.
    /// </summary>
    internal static class BorkelCompat
    {
        public const string BorkelGuid = "com.borkel.nvgmasks";

        /// <summary>True once his plugin is found and the patch is in. NvgPodRotator stops masking when set.</summary>
        public static bool Active { get; private set; }

        // itemId -> that NVG's folder under Assets\NVG, built from his config.json files
        private static readonly Dictionary<string, string> _folderByItem = new Dictionary<string, string>();
        private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();
        private static readonly HashSet<string> _reportedMissing = new HashSet<string>();

        public static void TryEnable(Harmony harmony)
        {
            if (!Chainloader.PluginInfos.TryGetValue(BorkelGuid, out var info))
            {
                C11Plugin.DebugLog("[BorkelCompat] Borkel's Realistic NVGs not installed - using our own masks.");
                return;
            }

            try
            {
                // ConfigureRuntime(Shader shader, Texture lens, Texture overlay, ...)
                var renderer  = AccessTools.TypeByName("BorkelRNVG.Controllers.RealisticNightVisionRenderer");
                var configure = renderer != null ? AccessTools.Method(renderer, "ConfigureRuntime") : null;
                var ps = configure?.GetParameters();

                if (ps == null || ps.Length < 3
                    || ps[1].ParameterType != typeof(Texture) || ps[1].Name != "lens"
                    || ps[2].ParameterType != typeof(Texture) || ps[2].Name != "overlay")
                {
                    C11Plugin.Log.LogWarning("[BorkelCompat] Borkel's NVGs is installed, but its renderer has changed " +
                                             "(ConfigureRuntime lens/overlay not found). Flipped-pod textures won't be applied to it.");
                    return;
                }

                // Same folder he reads: <his DLL folder>\Assets\NVG
                string nvgRoot = Path.Combine(Path.GetDirectoryName(info.Location) ?? "", "Assets", "NVG");
                IndexFolders(nvgRoot);

                harmony.Patch(configure, prefix: new HarmonyMethod(typeof(BorkelCompat), nameof(ConfigurePrefix)));
                Active = true;
                C11Plugin.Log.LogInfo($"[BorkelCompat] Borkel's Realistic NVGs detected - our masking is off " +
                                      $"({_folderByItem.Count} NVG ids indexed from {nvgRoot}).");
            }
            catch (Exception e)
            {
                C11Plugin.Log.LogError($"[BorkelCompat] Failed to patch Borkel's NVGs: {e}");
            }
        }

        /// <summary>Reads each NVG folder's config.json so a worn item id can be traced to its folder.</summary>
        private static void IndexFolders(string nvgRoot)
        {
            if (!Directory.Exists(nvgRoot)) { C11Plugin.Log.LogWarning($"[BorkelCompat] {nvgRoot} not found."); return; }

            foreach (var dir in Directory.GetDirectories(nvgRoot))
            {
                string cfgPath = Path.Combine(dir, "config.json");
                if (!File.Exists(cfgPath)) continue;
                try
                {
                    var cfg = JObject.Parse(File.ReadAllText(cfgPath));
                    var single = (string)cfg["itemId"];
                    if (!string.IsNullOrWhiteSpace(single)) _folderByItem[single] = dir;
                    if (cfg["itemIds"] is JArray ids)
                        foreach (var id in ids)
                            if (!string.IsNullOrWhiteSpace((string)id)) _folderByItem[(string)id] = dir;
                }
                catch (Exception e)
                {
                    C11Plugin.Log.LogWarning($"[BorkelCompat] Couldn't read {cfgPath}: {e.Message}");
                }
            }
        }

        // ReSharper disable InconsistentNaming
        // Prefix on ConfigureRuntime(Shader shader, Texture lens, Texture overlay, ...).
        // __1 is the lens, __2 the overlay (mask). Plain by-value arguments, so replacing them
        // is safe; his cached NvgData and the out parameter of his lookup are never touched.
        private static void ConfigurePrefix(ref Texture __1, ref Texture __2)
        {
            // This runs inside Borkel's renderer setup: anything thrown here breaks his NVGs
            // (and the hideout/menu build), and shows up as a crash in *his* or BSG's code with
            // nothing logged against ours. So it must never throw.
            try { SwapTextures(ref __1, ref __2); }
            catch (Exception e)
            {
                if (!_reportedFailure)
                {
                    _reportedFailure = true;
                    C11Plugin.Log.LogError($"[BorkelCompat] Texture swap failed, leaving Borkel's textures in place: {e}");
                }
            }
        }

        private static bool _reportedFailure;

        private static void SwapTextures(ref Texture lensArg, ref Texture maskArg)
        {
            if (NvgPodRotator.ActiveProfile == null) return;      // not one of our pod NVGs

            string side;
            switch (NvgPodRotator.StowedPod)
            {
                case NvgPodRotator.PodSide.Right: side = "left";  break;   // right up -> left pod only
                case NvgPodRotator.PodSide.Left:  side = "right"; break;   // left up  -> right pod only
                default: return;       // both down: Borkel's own lens/mask. Both up: NVG is off.
            }

            string itemId = WornNvgItemId();
            if (itemId == null || !_folderByItem.TryGetValue(itemId, out var folder)) return;

            // Each is optional: a missing file leaves Borkel's own texture in place
            var lens = Load(Path.Combine(folder, $"lens_{side}.png"));
            var mask = Load(Path.Combine(folder, $"mask_{side}.png"));
            if (lens != null) lensArg = lens;
            if (mask != null) maskArg = mask;
        }
        // ReSharper restore InconsistentNaming

        /// <summary>Template id of the local player's NVG - the same lookup Borkel does.</summary>
        private static string WornNvgItemId()
        {
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            return player?.NightVisionObserver?.Component?.Item?.StringTemplateId;
        }

        private static Texture2D Load(string path)
        {
            if (_cache.TryGetValue(path, out var cached) && cached != null) return cached;
            if (_reportedMissing.Contains(path)) return null;      // already known missing / bad

            if (!File.Exists(path))
            {
                if (_reportedMissing.Add(path))
                    C11Plugin.Log.LogWarning($"[BorkelCompat] {path} not found - flipped pod keeps Borkel's own texture for it.");
                return null;
            }

            try
            {
                // Same format Borkel uses for his own textures
                var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(tex);
                    if (_reportedMissing.Add(path))
                        C11Plugin.Log.LogWarning($"[BorkelCompat] {path} isn't a readable PNG - keeping Borkel's own texture.");
                    return null;
                }
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.name = Path.GetFileNameWithoutExtension(path);

                _cache[path] = tex;
                C11Plugin.DebugLog($"[BorkelCompat] Loaded {path}");
                return tex;
            }
            catch (Exception e)
            {
                if (_reportedMissing.Add(path))
                    C11Plugin.Log.LogWarning($"[BorkelCompat] Couldn't load {path} ({e.Message}) - keeping Borkel's own texture.");
                return null;
            }
        }
    }
}