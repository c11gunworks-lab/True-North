using System.Linq;
using EFT.InventoryLogic;
using UnityEngine;

namespace C11_TN4_Client.amp_arms
{
    public class HelmetMountManager : MonoBehaviour
    {
        public CompoundItem HelmetItem;
        public HelmetSlotConfig Config;

        private HelmetSlotTuner _equipTuner;
        private HelmetSlotTuner _armsTuner;

        private bool _wasMounted = false;
        private string _lastMountId = null;

        private float _checkTimer = 0f;
        private const float CHECK_INTERVAL = 0.5f;

        public void Init(CompoundItem item, HelmetSlotConfig config)
        {
            HelmetItem = item;
            Config = config;
            CheckMounts();
        }

        private void Update()
        {
            _checkTimer += Time.deltaTime;
            if (_checkTimer >= CHECK_INTERVAL)
            {
                _checkTimer = 0f;
                CheckMounts();
            }
        }

        private void CheckMounts()
        {
            if (HelmetItem == null) return;

            bool hasAmpArm = false;
            bool hasRaclink = false;
            string currentMountId = null;

            void CheckItemForMounts(Item item)
            {
                if (item is CompoundItem compoundItem)
                {
                    foreach (var slot in compoundItem.Slots)
                    {
                        if (slot.ContainedItem == null) continue;
                        string mountId = slot.ContainedItem.TemplateId;

                        if (AmpArmsConfig.AmpArmsTemplateIds.Contains(mountId)) 
                        { 
                            hasAmpArm = true; 
                            currentMountId = mountId; 
                            return; 
                        }
                        if (AmpArmsConfig.RaclinkTemplateIds.Contains(mountId)) 
                        { 
                            hasRaclink = true; 
                            currentMountId = mountId; 
                            return; 
                        }

                        CheckItemForMounts(slot.ContainedItem);
                        if (hasAmpArm || hasRaclink) return;
                    }
                }
            }

            CheckItemForMounts(HelmetItem);

            bool isMounted = hasAmpArm || hasRaclink;

            if (isMounted != _wasMounted || currentMountId != _lastMountId)
            {
                C11Plugin.DebugLog($"[HelmetMountManager] Mount state changed on {HelmetItem.TemplateId}. Mounted: {isMounted}");
                
                _wasMounted = isMounted;
                _lastMountId = currentMountId;

                if (isMounted) ApplyTuners(hasAmpArm);
                else RemoveTuners();
            }
            
            if (isMounted && (_equipTuner == null || _armsTuner == null))
            {
                ApplyTuners(hasAmpArm);
            }
        }

        private void ApplyTuners(bool hasAmpArm)
        {
            MountMode equipMode = hasAmpArm ? MountMode.Amp : MountMode.Raclink;
            MountMode armsMode  = hasAmpArm ? MountMode.AmpArms : MountMode.RaclinkArms;

            string equipBone = "mod_equipment_001";
            if (Config.HasUnityArms)
            {
                equipBone = "mod_equipment_000";
                equipMode = MountMode.Amp;         
                armsMode  = MountMode.UnityAmpArms; 
            }

            Transform modEquip = FindChildContaining(transform, equipBone);
            if (modEquip != null && _equipTuner == null)
            {
                _equipTuner = modEquip.GetComponent<HelmetSlotTuner>();
                if (_equipTuner == null) _equipTuner = modEquip.gameObject.AddComponent<HelmetSlotTuner>();
                _equipTuner.Init(Config, equipMode);
            }
            else if (modEquip == null)
            {
                C11Plugin.DebugLog($"[HelmetMountManager] {equipBone} not found under {gameObject.name}");
            }

            Transform modArms = FindChildContaining(transform, "mod_arms");
            if (modArms == null && Config.HasUnityArms)
                modArms = FindChildContaining(transform, "mod_amps");

            if (modArms != null && _armsTuner == null)
            {
                _armsTuner = modArms.GetComponent<HelmetSlotTuner>();
                if (_armsTuner == null) _armsTuner = modArms.gameObject.AddComponent<HelmetSlotTuner>();
                _armsTuner.Init(Config, armsMode);
            }
            else if (modArms == null)
            {
                C11Plugin.DebugLog($"[HelmetMountManager] arms bone not found under {gameObject.name}");
            }
        }

        private void RemoveTuners()
        {
            if (_equipTuner != null)
            {
                Destroy(_equipTuner);
                _equipTuner = null;
            }
            if (_armsTuner != null)
            {
                Destroy(_armsTuner);
                _armsTuner = null;
            }
        }

        private Transform FindChildContaining(Transform root, string nameFragment)
        {
            if (root.name.IndexOf(nameFragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return root;
            foreach (Transform child in root)
            {
                var result = FindChildContaining(child, nameFragment);
                if (result != null) return result;
            }
            return null;
        }
    }
}