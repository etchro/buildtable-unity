using UnityEditor;
using UnityEngine;

namespace BuildtableSdk.Editor
{
    /// <summary>
    /// Adds setup validation to the config inspector. The check that matters is
    /// the key scope: this asset is serialized into the player, so a secret key
    /// pasted here would ship a read-capable, build-registering credential to
    /// every player. That mistake is silent at runtime — the API accepts a
    /// secret key wherever an ingest key is allowed — so it has to be caught in
    /// the Editor.
    /// </summary>
    [CustomEditor(typeof(BuildtableConfig))]
    public class BuildtableConfigInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var config = (BuildtableConfig)target;
            EditorGUILayout.Space();

            if (string.IsNullOrEmpty(config.ingestApiKey))
            {
                EditorGUILayout.HelpBox(
                    "No API key set. Create an ingest key in Buildtable under " +
                    "Project settings > API keys, then paste it above.",
                    MessageType.Warning);
            }
            else if (config.ingestApiKey.StartsWith("bt_secret_"))
            {
                EditorGUILayout.HelpBox(
                    "This is a SECRET key. It will be shipped inside your build, where it can be " +
                    "extracted and used to read your bugs and register builds. Replace it with an " +
                    "ingest key (bt_ingest_…) and revoke this one.",
                    MessageType.Error);
            }
            else if (!config.ingestApiKey.StartsWith("bt_ingest_"))
            {
                EditorGUILayout.HelpBox(
                    "This doesn't look like a Buildtable key. Ingest keys start with 'bt_ingest_'.",
                    MessageType.Warning);
            }

            if (string.IsNullOrEmpty(config.apiBaseUrl))
            {
                EditorGUILayout.HelpBox("Base URL is empty. Use https://buildtable.app unless you self-host.", MessageType.Warning);
            }
            else if (config.apiBaseUrl.EndsWith("/"))
            {
                EditorGUILayout.HelpBox("Base URL should not end with a slash.", MessageType.Warning);
            }
            else if (config.apiBaseUrl.StartsWith("http://"))
            {
                EditorGUILayout.HelpBox(
                    "Base URL is not HTTPS — the ingest key would be sent in cleartext.",
                    MessageType.Warning);
            }

            if (config.forwardLogs)
            {
                EditorGUILayout.HelpBox(
                    "Console output will be sent to Buildtable — every Debug.Log at or above " +
                    $"'{config.minimumForwardedLevel}', plus uncaught exceptions, with no changes to your game code. " +
                    "Whatever your game logs is what gets forwarded, so check it doesn't include player names, " +
                    "absolute file paths, or anything else you wouldn't put on the board. Logs are deleted " +
                    "server-side after 7 days.",
                    MessageType.Info);
            }

            if (config.forwardLogsInReleaseBuilds)
            {
                EditorGUILayout.HelpBox(
                    "Release builds will forward logs too. Without this, forwarding is limited to the Editor " +
                    "and Development Builds — which is what you want for anything you ship to players.",
                    config.forwardLogs ? MessageType.Warning : MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Install id (this machine)", BuildtableInstall.InstallId);
            if (GUILayout.Button("Open pending report folder")) BuildtableConfigMenu.OpenPendingFolder();
        }
    }
}
