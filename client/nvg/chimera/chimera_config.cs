using C11_TN4_Client.Core;
using EFT.InventoryLogic;
using UnityEngine;

namespace C11_TN4_Client.nvg.chimera
{
    public static class ChimeraConfig
    {
        public static void Register()
        {
            C11Plugin.RegisterDevice(new DeviceDefinition
            {
                PrefabNameContains = "Chimeara",
                LeftPodPath        = "axis_1/axis_3",
                RightPodPath       = "axis_1/axis_2",
                SinglePodMask      = NightVisionComponent.EMask.Anvis,
                DefaultLeftOn      = new Quaternion(-0.284139991f, -1.14295787e-07f,  3.38721087e-08f,  0.958782852f),
                DefaultLeftOff     = new Quaternion(-0.200030774f,  0.680940151f,    -0.201800182f,     0.674970269f),
                DefaultRightOn     = new Quaternion(-0.284139991f, -1.14295801e-07f,  3.38721193e-08f,  0.958782911f),
                DefaultRightOff    = new Quaternion(-0.199279293f, -0.683443785f,     0.202542245f,     0.672435164f),
            });
        }
    }
}