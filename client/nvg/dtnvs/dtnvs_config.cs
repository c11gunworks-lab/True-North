using C11_TN4_Client.Core;
using EFT.InventoryLogic;
using UnityEngine;

namespace C11_TN4_Client.nvg.dtnvs
{
    public static class DtnvsConfig
    {
        public static void Register()
        {
            C11Plugin.RegisterDevice(new DeviceDefinition
            {
                PrefabNameContains = "nvg_wilcox_l4g24_mount",
                ChildNvgName       = "DTNVS",
                LeftPodPath        = "axis_2",
                RightPodPath       = "axis_1",
                SinglePodMask      = NightVisionComponent.EMask.OldMonocular,
                DefaultLeftOn      = new Quaternion(-0.707106829f,  0f,             0f,             0.707106709f),
                DefaultLeftOff    = new Quaternion(-0.358683348f, -0.609381855f,   0.609381974f,   0.358683318f),
                DefaultRightOn     = new Quaternion(-0.707106829f,  0f,             0f,             0.707106709f),
                DefaultRightOff  = new Quaternion(-0.358683348f,  0.609381855f, -0.609381974f,  0.358683318f),

            });
        }
    }
}