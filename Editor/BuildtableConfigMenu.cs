using System.IO;
using UnityEditor;
using UnityEngine;

namespace BuildtableSdk.Editor
{
    /// <summary>
    /// Editor conveniences for getting a project connected. Creating the config
    /// by hand works too — this just puts it in the one place the runtime looks
    /// (<c>Assets/Resources</c>) so the "why is my config null" step is skipped.
    /// </summary>
    public static class BuildtableConfigMenu
    {
        private const string ResourcesDirectory = "Assets/Resources";
        private const string ConfigPath = ResourcesDirectory + "/BuildtableConfig.asset";

        [MenuItem("Tools/Buildtable/Create or Select Config", priority = 0)]
        public static void CreateOrSelectConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<BuildtableConfig>(ConfigPath);
            if (existing == null)
            {
                if (!Directory.Exists(ResourcesDirectory))
                {
                    Directory.CreateDirectory(ResourcesDirectory);
                    AssetDatabase.Refresh();
                }

                existing = ScriptableObject.CreateInstance<BuildtableConfig>();
                AssetDatabase.CreateAsset(existing, ConfigPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Buildtable] Created {ConfigPath}. Paste your project's ingest key into it.");
            }

            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
        }

        [MenuItem("Tools/Buildtable/Open Pending Report Folder", priority = 20)]
        public static void OpenPendingFolder()
        {
            // Mirrors BuildtableReportQueue's layout; handy when a tester says
            // "I filed it" and the board disagrees.
            var path = Path.Combine(Application.persistentDataPath, "buildtable", "pending");
            if (!Directory.Exists(path))
            {
                Debug.Log($"[Buildtable] No queued reports ({path} does not exist).");
                return;
            }
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Tools/Buildtable/Reset Install Id", priority = 21)]
        public static void ResetInstallId()
        {
            BuildtableInstall.Reset();
            Debug.Log("[Buildtable] Install id and remembered reporter cleared.");
        }
    }
}
