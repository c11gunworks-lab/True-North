using EFT.InventoryLogic;
using UnityEngine;

namespace C11_TN4_Client.Core
{
    public class DeviceDefinition
    {
        public string                      PrefabNameContains;
        public string                      ChildNvgName;
        public string                      LeftPodPath;
        public string                      RightPodPath;
        public NightVisionComponent.EMask? SinglePodMask = NightVisionComponent.EMask.Anvis;
        public Quaternion                  DefaultLeftOn;
        public Quaternion                  DefaultLeftOff;
        public Quaternion                  DefaultRightOn;
        public Quaternion                  DefaultRightOff;
    }
}