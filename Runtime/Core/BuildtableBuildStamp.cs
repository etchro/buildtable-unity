using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace BuildtableSdk
{
    /// <summary>
    /// Reads <c>StreamingAssets/buildtable-build.json</c>, if a build hook wrote
    /// one, so every payload can be tagged with the version it came from.
    ///
    /// The writer is Milestone D (an <c>IPreprocessBuildWithReport</c> that
    /// registers the build via <c>POST /api/v1/builds</c> with a secret key).
    /// The reader ships now so reports from a stamped build are version-tagged
    /// the moment that hook lands, and so the absence of a stamp is a normal,
    /// silent case rather than an error.
    /// </summary>
    [Serializable]
    public class BuildtableBuildStamp
    {
        public const string FileName = "buildtable-build.json";

        /// <summary>A <c>versioning.versions</c> row id — sent as <c>versionId</c>.</summary>
        public string buildId;

        /// <summary>Human-readable version, e.g. "0.4.2-rc1". Informational.</summary>
        public string versionNumber;

        public static BuildtableBuildStamp Current { get; private set; }

        /// <summary>
        /// Loads the stamp. Uses UnityWebRequest because StreamingAssets is
        /// inside a compressed archive on Android and behind a URL on WebGL —
        /// File.ReadAllText only works on desktop.
        /// </summary>
        internal static IEnumerator Load()
        {
            var path = Path.Combine(Application.streamingAssetsPath, FileName);
            var uri = path.Contains("://") ? path : "file://" + path;

            using (var request = UnityWebRequest.Get(uri))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success) yield break;

                var text = request.downloadHandler.text;
                if (string.IsNullOrEmpty(text)) yield break;

                BuildtableBuildStamp parsed = null;
                try
                {
                    parsed = JsonUtility.FromJson<BuildtableBuildStamp>(text);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Buildtable] Ignoring malformed {FileName}: {e.Message}");
                }

                if (parsed != null && !string.IsNullOrEmpty(parsed.buildId)) Current = parsed;
            }
        }
    }
}
