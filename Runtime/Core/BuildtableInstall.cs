using System;
using UnityEngine;

namespace BuildtableSdk
{
    /// <summary>
    /// Stable per-install identity, sent as <c>X-Buildtable-Client</c> on every
    /// request. This is what gives each player their own rate-limit budget: an
    /// ingest key ships inside every copy of the game, so the server can't treat
    /// "one key" as "one client". Without this header the whole playtest shares a
    /// single per-IP bucket.
    ///
    /// Deliberately anonymous — a random id, not a device fingerprint. It is
    /// never reused across installs and carries no personal data.
    /// </summary>
    public static class BuildtableInstall
    {
        private const string InstallIdKey = "buildtable.install_id";
        private const string ReporterNameKey = "buildtable.reporter_name";
        private const string ReporterEmailKey = "buildtable.reporter_email";

        private static string _installId;

        /// <summary>
        /// 32 hex characters — inside the server's <c>[A-Za-z0-9_-]{1,64}</c>
        /// allowance, so it is never silently rejected and downgraded to IP.
        /// </summary>
        public static string InstallId
        {
            get
            {
                if (!string.IsNullOrEmpty(_installId)) return _installId;

                _installId = PlayerPrefs.GetString(InstallIdKey, "");
                if (string.IsNullOrEmpty(_installId))
                {
                    _installId = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(InstallIdKey, _installId);
                    PlayerPrefs.Save();
                }
                return _installId;
            }
        }

        /// <summary>Remembered so a tester types their details once, not once per report.</summary>
        public static string ReporterName
        {
            get => PlayerPrefs.GetString(ReporterNameKey, "");
            set { PlayerPrefs.SetString(ReporterNameKey, value ?? ""); PlayerPrefs.Save(); }
        }

        public static string ReporterEmail
        {
            get => PlayerPrefs.GetString(ReporterEmailKey, "");
            set { PlayerPrefs.SetString(ReporterEmailKey, value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>Forgets the install id and remembered reporter. Mainly for QA.</summary>
        public static void Reset()
        {
            _installId = null;
            PlayerPrefs.DeleteKey(InstallIdKey);
            PlayerPrefs.DeleteKey(ReporterNameKey);
            PlayerPrefs.DeleteKey(ReporterEmailKey);
            PlayerPrefs.Save();
        }
    }
}
