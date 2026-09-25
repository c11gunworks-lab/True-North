using BepInEx.Configuration;
using C11_TN4_Client.config;
using UnityEngine;

namespace C11_TN4_Client.amp_arms
{
public class HelmetSlotConfig
{
    public string  HelmetTemplateId;
    public string  HelmetLabel;

    /// <summary>
    /// True only for helmets that use the Unity Adapter (Team Wendy family).
    /// When true, binds the Unity AMP Arms (mod_amps) config entries.
    /// The adapter itself sits in mod_equipment_000 (SlotPosition/SlotEuler),
    /// and its mod_arms child is tuned via the UnityAmps entries below.
    /// </summary>
    public bool HasUnityArms;

    // AMP arms offsets
    public Vector3 SlotPosition;
    public Vector3 SlotEuler;
    public Vector3 ArmsPosition;
    public Vector3 ArmsEuler;

    // RACLINK offsets
    public Vector3 RaclinkSlotPosition;
    public Vector3 RaclinkSlotEuler;
    public Vector3 RaclinkArmsPosition;
    public Vector3 RaclinkArmsEuler;

    // AMP config entries
    public ConfigEntry<float> PosX, PosY, PosZ;
    public ConfigEntry<float> RotX, RotY, RotZ;
    public ConfigEntry<float> ArmsPosX, ArmsPosY, ArmsPosZ;
    public ConfigEntry<float> ArmsRotX, ArmsRotY, ArmsRotZ;

    // RACLINK config entries
    public ConfigEntry<float> RaclinkPosX, RaclinkPosY, RaclinkPosZ;
    public ConfigEntry<float> RaclinkRotX, RaclinkRotY, RaclinkRotZ;
    public ConfigEntry<float> RaclinkArmsPosX, RaclinkArmsPosY, RaclinkArmsPosZ;
    public ConfigEntry<float> RaclinkArmsRotX, RaclinkArmsRotY, RaclinkArmsRotZ;

    // Unity Adapter — mod_arms child (only bound when HasUnityArms == true)
    public Vector3 UnityAmpsPosition;
    public Vector3 UnityAmpsEuler;
    public ConfigEntry<float> UnityAmpsPosX, UnityAmpsPosY, UnityAmpsPosZ;
    public ConfigEntry<float> UnityAmpsRotX, UnityAmpsRotY, UnityAmpsRotZ;


    public void BindConfig(ConfigFile config, string helmetLabel, int order)
    {
        string section = $"Helmet - {helmetLabel}"
            .Replace("[", "").Replace("]", "").Replace("\\", "")
            .Replace("\"", "").Replace("'", "").Replace("\n", "").Replace("\t", "");

        ConfigEntry<float> Bind(string key, float def, string desc, int ord) =>
            config.Bind(section, key, def,
                new ConfigDescription(desc,
                    new AcceptableValueRange<float>(-180f, 180f),
                    new ConfigurationManagerAttributes { IsAdvanced = true, Order = ord }));

        ConfigEntry<float> BindPos(string key, float def, string desc, int ord) =>
            config.Bind(section, key, def,
                new ConfigDescription(desc,
                    new AcceptableValueRange<float>(-0.5f, 0.5f),
                    new ConfigurationManagerAttributes { IsAdvanced = true, Order = ord }));

        if (HasUnityArms)
        {
            // Unity Adapter: sits in mod_equipment_000 on Team Wendy helmets.
            // SlotPos/SlotEuler tune where the adapter body mounts (mod_equipment_000).
            // UnityAmps entries tune the mod_arms child on the adapter (where AMP arms attach).
            RotX     = Bind   ("Unity Adapter Rotation X",          SlotEuler.x,          "Euler X — Unity Adapter (mod_equipment_000).",        order + 23);
            RotY     = Bind   ("Unity Adapter Rotation Y",          SlotEuler.y,          "Euler Y — Unity Adapter (mod_equipment_000).",        order + 22);
            RotZ     = Bind   ("Unity Adapter Rotation Z",          SlotEuler.z,          "Euler Z — Unity Adapter (mod_equipment_000).",        order + 21);
            PosX     = BindPos("Unity Adapter Position X",          SlotPosition.x,       "Local X — Unity Adapter (mod_equipment_000).",        order + 20);
            PosY     = BindPos("Unity Adapter Position Y",          SlotPosition.y,       "Local Y — Unity Adapter (mod_equipment_000).",        order + 19);
            PosZ     = BindPos("Unity Adapter Position Z",          SlotPosition.z,       "Local Z — Unity Adapter (mod_equipment_000).",        order + 18);

            UnityAmpsRotX = Bind   ("AMP Arms Rotation X",          UnityAmpsEuler.x,    "Euler X — AMP Arms on Unity Adapter (mod_arms).",     order + 17);
            UnityAmpsRotY = Bind   ("AMP Arms Rotation Y",          UnityAmpsEuler.y,    "Euler Y — AMP Arms on Unity Adapter (mod_arms).",     order + 16);
            UnityAmpsRotZ = Bind   ("AMP Arms Rotation Z",          UnityAmpsEuler.z,    "Euler Z — AMP Arms on Unity Adapter (mod_arms).",     order + 15);
            UnityAmpsPosX = BindPos("AMP Arms Position X",          UnityAmpsPosition.x, "Local X — AMP Arms on Unity Adapter (mod_arms).",     order + 14);
            UnityAmpsPosY = BindPos("AMP Arms Position Y",          UnityAmpsPosition.y, "Local Y — AMP Arms on Unity Adapter (mod_arms).",     order + 13);
            UnityAmpsPosZ = BindPos("AMP Arms Position Z",          UnityAmpsPosition.z, "Local Z — AMP Arms on Unity Adapter (mod_arms).",     order + 12);
        }
        else
        {
            // AMP arm mount (mod_equipment_001)
            RotX     = Bind   ("AMP Arms Rotation X",               SlotEuler.x,          "Euler X — AMP Arms (mod_equipment_001).",             order + 23);
            RotY     = Bind   ("AMP Arms Rotation Y",               SlotEuler.y,          "Euler Y — AMP Arms (mod_equipment_001).",             order + 22);
            RotZ     = Bind   ("AMP Arms Rotation Z",               SlotEuler.z,          "Euler Z — AMP Arms (mod_equipment_001).",             order + 21);
            PosX     = BindPos("AMP Arms Position X",               SlotPosition.x,       "Local X — AMP Arms (mod_equipment_001).",             order + 20);
            PosY     = BindPos("AMP Arms Position Y",               SlotPosition.y,       "Local Y — AMP Arms (mod_equipment_001).",             order + 19);
            PosZ     = BindPos("AMP Arms Position Z",               SlotPosition.z,       "Local Z — AMP Arms (mod_equipment_001).",             order + 18);

            // AMP headset mount (mod_arms)
            ArmsRotX = Bind   ("RACLINK Rotation X",                ArmsEuler.x,          "Euler X — RACLINK (mod_arms).",                       order + 17);
            ArmsRotY = Bind   ("RACLINK Rotation Y",                ArmsEuler.y,          "Euler Y — RACLINK (mod_arms).",                       order + 16);
            ArmsRotZ = Bind   ("RACLINK Rotation Z",                ArmsEuler.z,          "Euler Z — RACLINK (mod_arms).",                       order + 15);
            ArmsPosX = BindPos("RACLINK Position X",                ArmsPosition.x,       "Local X — RACLINK (mod_arms).",                       order + 14);
            ArmsPosY = BindPos("RACLINK Position Y",                ArmsPosition.y,       "Local Y — RACLINK (mod_arms).",                       order + 13);
            ArmsPosZ = BindPos("RACLINK Position Z",                ArmsPosition.z,       "Local Z — RACLINK (mod_arms).",                       order + 12);

            // RAC Headset (mod_equipment_001 on RACLINK helmets)
            RaclinkRotX     = Bind   ("RAC Headset Rotation X",     RaclinkSlotEuler.x,    "Euler X — RAC Headset.",                            order + 11);
            RaclinkRotY     = Bind   ("RAC Headset Rotation Y",     RaclinkSlotEuler.y,    "Euler Y — RAC Headset.",                            order + 10);
            RaclinkRotZ     = Bind   ("RAC Headset Rotation Z",     RaclinkSlotEuler.z,    "Euler Z — RAC Headset.",                            order + 9);
            RaclinkPosX     = BindPos("RAC Headset Position X",     RaclinkSlotPosition.x, "Local X — RAC Headset.",                            order + 8);
            RaclinkPosY     = BindPos("RAC Headset Position Y",     RaclinkSlotPosition.y, "Local Y — RAC Headset.",                            order + 7);
            RaclinkPosZ     = BindPos("RAC Headset Position Z",     RaclinkSlotPosition.z, "Local Z — RAC Headset.",                            order + 6);

            // RAC Headset arms (mod_arms on RACLINK helmets)
            RaclinkArmsRotX = Bind   ("RAC Headset Arms Rotation X", RaclinkArmsEuler.x,    "Euler X — RAC Headset mod_arms.",                  order + 5);
            RaclinkArmsRotY = Bind   ("RAC Headset Arms Rotation Y", RaclinkArmsEuler.y,    "Euler Y — RAC Headset mod_arms.",                  order + 4);
            RaclinkArmsRotZ = Bind   ("RAC Headset Arms Rotation Z", RaclinkArmsEuler.z,    "Euler Z — RAC Headset mod_arms.",                  order + 3);
            RaclinkArmsPosX = BindPos("RAC Headset Arms Position X", RaclinkArmsPosition.x, "Local X — RAC Headset mod_arms.",                  order + 2);
            RaclinkArmsPosY = BindPos("RAC Headset Arms Position Y", RaclinkArmsPosition.y, "Local Y — RAC Headset mod_arms.",                  order + 1);
            RaclinkArmsPosZ = BindPos("RAC Headset Arms Position Z", RaclinkArmsPosition.z, "Local Z — RAC Headset mod_arms.",                  order + 0);
        }
    }

    public Vector3 GetPosition()      => PosX         != null ? new Vector3(PosX.Value,         PosY.Value,         PosZ.Value)         : SlotPosition;
    public Vector3 GetEuler()         => RotX         != null ? new Vector3(RotX.Value,         RotY.Value,         RotZ.Value)         : SlotEuler;
    public Vector3 GetArmsPosition()  => ArmsPosX     != null ? new Vector3(ArmsPosX.Value,     ArmsPosY.Value,     ArmsPosZ.Value)     : ArmsPosition;
    public Vector3 GetArmsEuler()     => ArmsRotX     != null ? new Vector3(ArmsRotX.Value,     ArmsRotY.Value,     ArmsRotZ.Value)     : ArmsEuler;

    public Vector3 GetRaclinkPosition()     => RaclinkPosX     != null ? new Vector3(RaclinkPosX.Value,     RaclinkPosY.Value,     RaclinkPosZ.Value)     : RaclinkSlotPosition;
    public Vector3 GetRaclinkEuler()        => RaclinkRotX     != null ? new Vector3(RaclinkRotX.Value,     RaclinkRotY.Value,     RaclinkRotZ.Value)     : RaclinkSlotEuler;
    public Vector3 GetRaclinkArmsPosition() => RaclinkArmsPosX != null ? new Vector3(RaclinkArmsPosX.Value, RaclinkArmsPosY.Value, RaclinkArmsPosZ.Value) : RaclinkArmsPosition;
    public Vector3 GetRaclinkArmsEuler()    => RaclinkArmsRotX != null ? new Vector3(RaclinkArmsRotX.Value, RaclinkArmsRotY.Value, RaclinkArmsRotZ.Value) : RaclinkArmsEuler;

    public Vector3 GetUnityAmpsPosition() => UnityAmpsPosX != null
        ? new Vector3(UnityAmpsPosX.Value, UnityAmpsPosY.Value, UnityAmpsPosZ.Value) : UnityAmpsPosition;
    public Vector3 GetUnityAmpsEuler() => UnityAmpsRotX != null
        ? new Vector3(UnityAmpsRotX.Value, UnityAmpsRotY.Value, UnityAmpsRotZ.Value) : UnityAmpsEuler;

    public string ToCSharpSnippet() =>
        $"RegisterHelmet(\"{HelmetTemplateId}\", \"{HelmetLabel}\",\n" +
        $"    ampPos:      new Vector3({GetPosition().x}f,            {GetPosition().y}f,            {GetPosition().z}f),\n" +
        $"    ampEul:      new Vector3({GetEuler().x}f,               {GetEuler().y}f,               {GetEuler().z}f),\n" +
        $"    armsPos:     new Vector3({GetArmsPosition().x}f,        {GetArmsPosition().y}f,        {GetArmsPosition().z}f),\n" +
        $"    armsEul:     new Vector3({GetArmsEuler().x}f,           {GetArmsEuler().y}f,           {GetArmsEuler().z}f),\n" +
        $"    raclinkPos:  new Vector3({GetRaclinkPosition().x}f,     {GetRaclinkPosition().y}f,     {GetRaclinkPosition().z}f),\n" +
        $"    raclinkEul:  new Vector3({GetRaclinkEuler().x}f,        {GetRaclinkEuler().y}f,        {GetRaclinkEuler().z}f),\n" +
        $"    raclinkArmsPos: new Vector3({GetRaclinkArmsPosition().x}f, {GetRaclinkArmsPosition().y}f, {GetRaclinkArmsPosition().z}f),\n" +
        $"    raclinkArmsEul: new Vector3({GetRaclinkArmsEuler().x}f,    {GetRaclinkArmsEuler().y}f,    {GetRaclinkArmsEuler().z}f));";
}
}