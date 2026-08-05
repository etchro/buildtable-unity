using UnityEngine;

namespace BuildtableSdk
{
    /// <summary>
    /// Builds the <c>deviceInfo</c> object attached to bug reports. The server
    /// stores it as free-form JSON (<c>bugs.bugs.device_info</c>), so the shape
    /// here is the contract in practice — keep keys stable.
    ///
    /// Everything comes from <see cref="SystemInfo"/> and
    /// <see cref="Application"/>: hardware and build facts, no user identifiers
    /// beyond the anonymous install id.
    /// </summary>
    public static class BuildtableDeviceInfo
    {
        /// <summary>
        /// Returns a standalone <c>{ … }</c> fragment. It is captured at report
        /// time and stored verbatim in the offline queue, so a report sent an
        /// hour later still describes the moment it was filed.
        /// </summary>
        public static string BuildFragment()
        {
            var json = new BuildtableJson();
            json.BeginObject();

            json.String("sdk", "unity");
            json.String("sdkVersion", Buildtable.SdkVersion);
            json.String("installId", BuildtableInstall.InstallId);

            json.String("platform", Application.platform.ToString());
            json.String("operatingSystem", SystemInfo.operatingSystem);
            json.String("deviceModel", SystemInfo.deviceModel);
            json.String("deviceType", SystemInfo.deviceType.ToString());
            json.String("processor", SystemInfo.processorType);
            json.Number("processorCount", SystemInfo.processorCount);
            json.Number("systemMemoryMB", SystemInfo.systemMemorySize);

            json.String("graphicsDevice", SystemInfo.graphicsDeviceName);
            json.String("graphicsApi", SystemInfo.graphicsDeviceType.ToString());
            json.Number("graphicsMemoryMB", SystemInfo.graphicsMemorySize);

            json.String("resolution", $"{Screen.width}x{Screen.height}");
            json.String("screenMode", Screen.fullScreenMode.ToString());
            json.Number("targetFrameRate", Application.targetFrameRate);
            json.String("quality", QualityLevelName());

            json.String("unityVersion", Application.unityVersion);
            json.String("applicationVersion", Application.version);
            json.String("productName", Application.productName);
            json.String("language", Application.systemLanguage.ToString());
            json.Number("playtimeSeconds", Mathf.RoundToInt(Time.realtimeSinceStartup));
            json.String("scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);

            var stamp = BuildtableBuildStamp.Current;
            if (stamp != null)
            {
                json.StringIfSet("buildId", stamp.buildId);
                json.StringIfSet("versionNumber", stamp.versionNumber);
            }

            json.EndObject();
            return json.ToString();
        }

        private static string _compactFragment;

        /// <summary>
        /// The short <c>{ … }</c> fragment attached to telemetry events. Events
        /// are sent every 30 seconds; repeating the full hardware dump on each
        /// one would dwarf the log lines it's meant to describe, so this keeps
        /// only what's needed to tell one build and one install apart.
        ///
        /// Built once and cached — none of these values change during a run.
        /// </summary>
        public static string BuildCompactFragment()
        {
            if (_compactFragment != null) return _compactFragment;

            var json = new BuildtableJson();
            json.BeginObject();

            json.String("sdk", "unity");
            json.String("sdkVersion", Buildtable.SdkVersion);
            json.String("installId", BuildtableInstall.InstallId);
            json.String("platform", Application.platform.ToString());
            json.String("deviceModel", SystemInfo.deviceModel);
            json.String("operatingSystem", SystemInfo.operatingSystem);
            json.String("unityVersion", Application.unityVersion);
            json.String("applicationVersion", Application.version);
            json.Bool("isDebugBuild", Debug.isDebugBuild);

            var stamp = BuildtableBuildStamp.Current;
            if (stamp != null)
            {
                json.StringIfSet("buildId", stamp.buildId);
                json.StringIfSet("versionNumber", stamp.versionNumber);
            }

            json.EndObject();
            _compactFragment = json.ToString();
            return _compactFragment;
        }

        private static string QualityLevelName()
        {
            var names = QualitySettings.names;
            var level = QualitySettings.GetQualityLevel();
            return names != null && level >= 0 && level < names.Length ? names[level] : "unknown";
        }
    }
}
