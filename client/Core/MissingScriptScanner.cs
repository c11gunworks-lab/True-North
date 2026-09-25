// using System;
// using System.Collections.Generic;
// using System.Text;
// using UnityEngine;
// using UnityEngine.SceneManagement;
//
// namespace C11_TN4_Client.Core
// {
//     /// <summary>
//     /// Finds GameObjects carrying MonoBehaviour slots whose script failed to
//     /// resolve ("The referenced script on this Behaviour is missing!").
//     ///
//     /// Those warnings come from Unity's native deserializer, so they cannot be
//     /// caught with a Harmony patch on Debug.LogError. Instead we scan for the
//     /// symptom: GetComponents returns a null entry where the missing script sits.
//     ///
//     /// Attach to the plugin GameObject; press the configured key in raid.
//     /// </summary>
//     public class MissingScriptScanner : MonoBehaviour
//     {
//         private void Update()
//         {
//             if (C11Plugin.ScanMissingScriptsKey.Value.IsDown())
//                 Scan();
//         }
//
//         public static void Scan()
//         {
//             C11Plugin.Log.LogInfo("===== MISSING SCRIPT SCAN =====");
//
//             int objectsScanned = 0;
//             int brokenObjects  = 0;
//             int brokenSlots    = 0;
//
//             // Group findings by root object so bundle sources are obvious.
//             var byRoot = new Dictionary<string, List<string>>();
//
//             foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
//             {
//                 // Skip editor/preview objects and our own plugin object.
//                 if (go == null) continue;
//                 if (go.hideFlags == HideFlags.HideAndDontSave) continue;
//
//                 objectsScanned++;
//
//                 Component[] components;
//                 try { components = go.GetComponents<Component>(); }
//                 catch (Exception) { continue; }
//
//                 int nullCount = 0;
//                 for (int i = 0; i < components.Length; i++)
//                     if (components[i] == null) nullCount++;
//
//                 if (nullCount == 0) continue;
//
//                 brokenObjects++;
//                 brokenSlots += nullCount;
//
//                 string rootName = go.transform.root != null ? go.transform.root.name : "(no root)";
//                 string path     = GetHierarchyPath(go.transform);
//                 string scene    = go.scene.IsValid() ? go.scene.name : "(no scene / asset)";
//
//                 if (!byRoot.TryGetValue(rootName, out var list))
//                 {
//                     list = new List<string>();
//                     byRoot[rootName] = list;
//                 }
//
//                 list.Add($"    {path}   [{nullCount} missing]   scene: {scene}");
//             }
//
//             foreach (var kvp in byRoot)
//             {
//                 C11Plugin.Log.LogInfo($"  ROOT: {kvp.Key}   ({kvp.Value.Count} broken objects)");
//                 foreach (string line in kvp.Value)
//                     C11Plugin.Log.LogInfo(line);
//             }
//
//             C11Plugin.Log.LogInfo(
//                 $"===== SCANNED {objectsScanned} objects | " +
//                 $"{brokenObjects} broken | {brokenSlots} missing script slots =====");
//         }
//
//         private static string GetHierarchyPath(Transform t)
//         {
//             var sb = new StringBuilder(t.name);
//             Transform cur = t.parent;
//             int guard = 0;
//
//             while (cur != null && guard++ < 64)
//             {
//                 sb.Insert(0, cur.name + "/");
//                 cur = cur.parent;
//             }
//             return sb.ToString();
//         }
//     }
// }