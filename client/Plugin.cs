using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using C11_TN4_Client.amp_arms;
using C11_TN4_Client.compat;
using C11_TN4_Client.config;
using C11_TN4_Client.Core;
using C11_TN4_Client.nvg.chimera;
using C11_TN4_Client.nvg.dtnvs;
using C11_TN4_Client.patches;
using C11_TN4_Client.Patches;
using SPT.Reflection.Patching;
using HarmonyLib;
using UnityEngine;

namespace C11_TN4_Client
{
    // Soft: load after Borkel's NVGs if it's installed, so BorkelCompat can find it
    [BepInDependency(BorkelCompat.BorkelGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(PluginConstants.Guid, PluginConstants.Name, PluginConstants.Version)]
    public class C11Plugin : BaseUnityPlugin
    {
        internal static C11Plugin       Instance;
        internal static ManualLogSource Log;
        internal static string          PluginFolder;

        // ── Keybinds & settings ───────────────────────────────────────────────
        internal static ConfigEntry<KeyboardShortcut> BothPodsFoldKey;
        internal static ConfigEntry<KeyboardShortcut> LeftPodFoldKey;
        internal static ConfigEntry<KeyboardShortcut> RightPodFoldKey;
        internal static ConfigEntry<KeyboardShortcut> ResetRotationsKey;
        internal static ConfigEntry<float>            ManualFoldSpeed;
        internal static ConfigEntry<bool>             PodFlipAnimation;
        internal static ConfigEntry<bool>             PodFlipSound;
        internal static ConfigEntry<bool>             DebugLogging;
        internal static ConfigEntry<KeyboardShortcut> CopyConfigKey;
        internal static ConfigEntry<KeyboardShortcut> ScanMissingScriptsKey;

        // ── AMP arms preview editor ───────────────────────────────────────────
        internal static ConfigEntry<bool>             AmpPreviewTuning;
        internal static ConfigEntry<KeyboardShortcut> AmpEditKey;
        internal static ConfigEntry<KeyboardShortcut> AmpEditToolKey;
        internal static ConfigEntry<KeyboardShortcut> AmpEditPartKey;
        internal static ConfigEntry<float>            AmpEditGizmoSize;

        // ── Device/helmet registries ──────────────────────────────────────────
        internal static Dictionary<string, NvgDeviceProfile> Profiles      = new Dictionary<string, NvgDeviceProfile>();
        internal static Dictionary<string, HelmetSlotConfig> HelmetConfigs = new Dictionary<string, HelmetSlotConfig>();

        // ── Weapon forearm correction ─────────────────────────────────────────
        internal static HashSet<string>    MagRotatorTemplateIds = new HashSet<string>();
        public static ConfigEntry<Vector3> MagCheckMarkerOffset;
        public static ConfigEntry<bool>    MagCheckDriveIkMarker;
        internal static ConfigEntry<float> ForearmBlendSpeed;

        private void Awake()
        {
            Instance     = this;
            Log          = Logger;
            PluginFolder = Path.GetDirectoryName(Info.Location);

            // ── Config bindings ───────────────────────────────────────────────
            BothPodsFoldKey = Config.Bind("General", "Both Pods Fold Key",
                new KeyboardShortcut(KeyCode.P), "Toggle both pods. Both folded = NVG powers off.");
            LeftPodFoldKey = Config.Bind("General", "Left Pod Only Key",
                new KeyboardShortcut(KeyCode.P, KeyCode.LeftShift), "Toggle left pod only.");
            RightPodFoldKey = Config.Bind("General", "Right Pod Only Key",
                new KeyboardShortcut(KeyCode.P, KeyCode.LeftControl), "Toggle right pod only.");
            ResetRotationsKey = Config.Bind("General", "Reset Rotations To Defaults",
                new KeyboardShortcut(KeyCode.None),
                new ConfigDescription("Press to restore all rotation values to defaults.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ManualFoldSpeed = Config.Bind("General", "Manual Fold Speed", 4f,
                new ConfigDescription("Pod animation speed",
                    new AcceptableValueRange<float>(0.5f, 10f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            PodFlipAnimation = Config.Bind("General", "Pod Flip Animation", true,
                "Plays the character's reach-to-helmet animation when a pod key is pressed.");
            PodFlipSound = Config.Bind("General", "Pod Flip Sound", true,
                "Plays the NVG click when a pod key is pressed.");
            DebugLogging = Config.Bind("General", "Debug Logging", false,
                new ConfigDescription("Verbose logging for adding new devices.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CopyConfigKey = Config.Bind("General", "Copy Helmet Configs To Log",
                new KeyboardShortcut(KeyCode.None),
                new ConfigDescription("Prints current F12 helmet slot values to BepInEx console as copy-pasteable C# code.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            MagCheckMarkerOffset = Config.Bind("Mag Check", "Marker Offset",
                new Vector3(0f, 0.03f, 0f),
                "Local-space shift applied to the left-hand IK marker while the mag is out.");

            MagCheckDriveIkMarker = Config.Bind("Mag Check", "Drive IK Marker", false,
                "Also offset weapon_L_IK_marker. Leave off until the hand marker alone is tuned.");
            ForearmBlendSpeed = Config.Bind("Weapon", "Forearm Blend Speed", 90f,
                new ConfigDescription("Degrees/sec blend between mag and no-mag forearm poses.",
                    new AcceptableValueRange<float>(10f, 360f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ScanMissingScriptsKey = Config.Bind("Diagnostics", "Scan For Missing Scripts",
                new KeyboardShortcut(KeyCode.F8),
                new ConfigDescription("Dumps every GameObject with an unresolved script component.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            AmpPreviewTuning = Config.Bind("AMP Arms Editor", "Tune Preview Models", true,
                "Applies AMP arm positions to inspect / modding-window previews. Turn off to rule it out if a menu crashes.");
            AmpEditKey = Config.Bind("AMP Arms Editor", "Edit Mode Key",
                new KeyboardShortcut(KeyCode.F10),
                "With a helmet open in an inspect or modding window, toggles mouse editing of its AMP parts.");
            AmpEditToolKey = Config.Bind("AMP Arms Editor", "Move / Rotate Key",
                new KeyboardShortcut(KeyCode.R),
                "Switches the gizmo between move arrows and rotation rings.");
            AmpEditPartKey = Config.Bind("AMP Arms Editor", "Next Part Key",
                new KeyboardShortcut(KeyCode.T),
                "Cycles through the AMP parts mounted on the helmet (not Tab - that closes the inventory).");
            AmpEditGizmoSize = Config.Bind("AMP Arms Editor", "Gizmo Size", 90f,
                new ConfigDescription("Length of the gizmo arrows / radius of the rings, in screen pixels.",
                    new AcceptableValueRange<float>(30f, 300f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // ── Weapons using the no-mag forearm correction ───────────────────
            // Replace with the real template ID(s). Add one line per weapon.
            MagRotatorTemplateIds.Add("6a3fd390c90e2ddc932ee1c6");

            // ── Register NVG devices ──────────────────────────────────────────
            ChimeraConfig.Register();
            DtnvsConfig.Register();

            // ── Register AMP-arm helmet configs ───────────────────────────────
            AmpArmsConfig.Register();

            // ── Apply patches ─────────────────────────────────────────────────
            // Each patch is enabled on its own, so one bad target (e.g. a member
            // name that changed in 4.1) can't stop the others from applying.
            EnablePatch(new NightVisionMaskApply());
            EnablePatch(new CurveRotatorPatch());
            EnablePatch(new AmpArmsEquipPatch());
            EnablePatch(new AmpArmsPreviewPatch());

            // Only does anything if Borkel's Realistic NVGs is installed
            BorkelCompat.TryEnable(new Harmony(PluginConstants.Guid + ".borkelcompat"));
            DebugLog("[C11-TN4-Client] Patch setup finished.");

            // ── Forearm rotator ───────────────────────────────────────────────
            // Patch-free: polls Singleton<GameWorld>.MainPlayer, so there is no
            // obfuscated method name to resolve and nothing to break on update.
            gameObject.AddComponent<MagCheckForearmRotator>();
            gameObject.AddComponent<AmpArmsEditor>();
            //gameObject.AddComponent<WeaponSwapLogger>();
            DebugLog("[C11-TN4-Client] MagCheckForearmRotator attached.");
            //gameObject.AddComponent<MissingScriptScanner>();
           // DebugLog("[C11-TN4-Client] MissingScriptScanner attached.");
        }

        private void Update()
        {
            if (ResetRotationsKey.Value.IsDown())
            {
                foreach (var p in Profiles.Values)
                    p.ResetToDefaults();
                DebugLog("[C11-TN4-Client] All rotations reset to defaults.");
            }
            if (CopyConfigKey.Value.IsDown())
                PrintHelmetConfigs();
        }

        // ── Registration helpers ──────────────────────────────────────────────

        internal static void RegisterDevice(DeviceDefinition def) =>
            Profiles[def.PrefabNameContains] = new NvgDeviceProfile(def, Instance.Config);

        internal static void RegisterHelmet(HelmetSlotConfig cfg, string label, int order = 0)
        {
            cfg.BindConfig(Instance.Config, label, order);
            HelmetConfigs[cfg.HelmetTemplateId] = cfg;
        }

        private static void EnablePatch(ModulePatch patch)
        {
            try
            {
                patch.Enable();
                DebugLog($"[C11-TN4-Client] Enabled {patch.GetType().Name}.");
            }
            catch (Exception e)
            {
                Log.LogError($"[C11-TN4-Client] Failed to enable {patch.GetType().Name}: {e}");
            }
        }

        internal static void DebugLog(string msg)
        {
            if (DebugLogging.Value) Log.LogInfo(msg);
        }

        private static void PrintHelmetConfigs()
        {
            Log.LogInfo("===== HELMET CONFIG SNAPSHOT =====");
            foreach (var cfg in HelmetConfigs.Values)
                Log.LogInfo(cfg.ToCSharpSnippet());
            Log.LogInfo("==================================");
        }
    }
}