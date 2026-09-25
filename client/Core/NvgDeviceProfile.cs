using BepInEx.Configuration;
using EFT.InventoryLogic;
using UnityEngine;
using C11_TN4_Client.config;

namespace C11_TN4_Client.Core
{
    public class NvgDeviceProfile
    {
        public readonly string                      PrefabNameContains;
        public readonly string                      ChildNvgName;
        public readonly string                      LeftPodPath;
        public readonly string                      RightPodPath;
        public readonly NightVisionComponent.EMask? SinglePodMask;

        private readonly Quaternion _defLOn, _defLOff, _defROn, _defROff;

        private readonly ConfigEntry<float>[] _lOn  = new ConfigEntry<float>[4];
        private readonly ConfigEntry<float>[] _lOff = new ConfigEntry<float>[4];
        private readonly ConfigEntry<float>[] _rOn  = new ConfigEntry<float>[4];
        private readonly ConfigEntry<float>[] _rOff = new ConfigEntry<float>[4];

        public Quaternion LeftOn   => Q(_lOn);
        public Quaternion LeftOff  => Q(_lOff);
        public Quaternion RightOn  => Q(_rOn);
        public Quaternion RightOff => Q(_rOff);

        private static Quaternion Q(ConfigEntry<float>[] e) =>
            new Quaternion(e[0].Value, e[1].Value, e[2].Value, e[3].Value);

        private static void SetQ(ConfigEntry<float>[] arr, Quaternion q)
        {
            arr[0].Value = q.x; arr[1].Value = q.y;
            arr[2].Value = q.z; arr[3].Value = q.w;
        }

        public void ResetToDefaults()
        {
            SetQ(_lOn, _defLOn); SetQ(_lOff, _defLOff);
            SetQ(_rOn, _defROn); SetQ(_rOff, _defROff);
        }

        public NvgDeviceProfile(DeviceDefinition def, ConfigFile cfg)
        {
            PrefabNameContains = def.PrefabNameContains;
            ChildNvgName       = def.ChildNvgName;
            LeftPodPath        = def.LeftPodPath;
            RightPodPath       = def.RightPodPath;
            SinglePodMask      = def.SinglePodMask;
            _defLOn  = def.DefaultLeftOn;  _defLOff = def.DefaultLeftOff;
            _defROn  = def.DefaultRightOn; _defROff = def.DefaultRightOff;

            string   sec  = $"Device: {def.PrefabNameContains}";
            string[] comp = { "X", "Y", "Z", "W" };

            void Bind(ConfigEntry<float>[] arr, string label, Quaternion q)
            {
                float[] vals = { q.x, q.y, q.z, q.w };
                for (int i = 0; i < 4; i++)
                    arr[i] = cfg.Bind(sec, $"{label} {comp[i]}", vals[i],
                        new ConfigDescription($"{label} quaternion {comp[i]} — copy from Unity Inspector",
                            null,
                            new ConfigurationManagerAttributes { IsAdvanced = true }));
            }

            Bind(_lOn,  "Left Pod On",  def.DefaultLeftOn);
            Bind(_lOff, "Left Pod Off", def.DefaultLeftOff);
            Bind(_rOn,  "Right Pod On",  def.DefaultRightOn);
            Bind(_rOff, "Right Pod Off", def.DefaultRightOff);
        }
    }
}