using System;
using BepInEx.Configuration;
using UnityEngine;

namespace C11_TN4_Client.amp_arms
{
    public enum MountMode { Amp, AmpArms, Raclink, RaclinkArms, UnityAmpArms }

    public class HelmetSlotTuner : MonoBehaviour
    {
        private HelmetSlotConfig     _cfg;
        private MountMode            _mode;
        private ConfigEntry<float>[] _entries;

        private Vector3 _targetPos;
        private Vector3 _targetEul;

        public void Init(HelmetSlotConfig cfg, MountMode mode)
        {
            _cfg  = cfg;
            _mode = mode;
            _entries = GetEntries();
            
            foreach (var e in _entries) 
            {
                if (e != null) e.SettingChanged += OnChanged;
            }
            
            Apply();
            C11Plugin.DebugLog($"[HelmetSlotTuner] Init ({_mode}) on '{gameObject.name}'");
        }

        private void OnChanged(object sender, EventArgs e) => Apply();

        private ConfigEntry<float>[] GetEntries()
        {
            return _mode switch
            {
                MountMode.Amp          => new[] { _cfg.PosX,            _cfg.PosY,            _cfg.PosZ,            _cfg.RotX,            _cfg.RotY,            _cfg.RotZ            },
                MountMode.AmpArms      => new[] { _cfg.ArmsPosX,        _cfg.ArmsPosY,        _cfg.ArmsPosZ,        _cfg.ArmsRotX,        _cfg.ArmsRotY,        _cfg.ArmsRotZ        },
                MountMode.Raclink      => new[] { _cfg.RaclinkPosX,     _cfg.RaclinkPosY,     _cfg.RaclinkPosZ,     _cfg.RaclinkRotX,     _cfg.RaclinkRotY,     _cfg.RaclinkRotZ     },
                MountMode.RaclinkArms  => new[] { _cfg.RaclinkArmsPosX, _cfg.RaclinkArmsPosY, _cfg.RaclinkArmsPosZ, _cfg.RaclinkArmsRotX, _cfg.RaclinkArmsRotY, _cfg.RaclinkArmsRotZ },
                MountMode.UnityAmpArms => new[] { _cfg.UnityAmpsPosX,   _cfg.UnityAmpsPosY,   _cfg.UnityAmpsPosZ,   _cfg.UnityAmpsRotX,   _cfg.UnityAmpsRotY,   _cfg.UnityAmpsRotZ   },
                _                      => new ConfigEntry<float>[0]
            };
        }

        private void Apply()
        {
            if (_cfg == null) return;
            switch (_mode)
            {
                case MountMode.Amp:
                    _targetPos = _cfg.GetPosition();
                    _targetEul = _cfg.GetEuler();
                    break;
                case MountMode.AmpArms:
                    _targetPos = _cfg.GetArmsPosition();
                    _targetEul = _cfg.GetArmsEuler();
                    break;
                case MountMode.Raclink:
                    _targetPos = _cfg.GetRaclinkPosition();
                    _targetEul = _cfg.GetRaclinkEuler();
                    break;
                case MountMode.RaclinkArms:
                    _targetPos = _cfg.GetRaclinkArmsPosition();
                    _targetEul = _cfg.GetRaclinkArmsEuler();
                    break;
                case MountMode.UnityAmpArms:
                    _targetPos = _cfg.GetUnityAmpsPosition();
                    _targetEul = _cfg.GetUnityAmpsEuler();
                    break;
            }
            
            UpdateTransform();
            C11Plugin.DebugLog($"[HelmetSlotTuner] Apply ({_mode}) pos={_targetPos} euler={_targetEul}");
        }

        private void UpdateTransform()
        {
            transform.localPosition    = _targetPos;
            transform.localEulerAngles = _targetEul;
        }

        // EFT animators will often reset equipment bone transforms every frame.
        // LateUpdate enforces our custom position/rotation after the animator runs.
        private void LateUpdate()
        {
            UpdateTransform();
        }

        private void OnDestroy()
        {
            if (_cfg == null || _entries == null) return;
            foreach (var e in _entries) 
            {
                if (e != null) e.SettingChanged -= OnChanged;
            }
        }
    }
}