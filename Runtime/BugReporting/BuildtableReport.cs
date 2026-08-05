using System;
using System.Collections.Generic;

namespace BuildtableSdk
{
    /// <summary>One project-defined custom field value. A class so JsonUtility can serialize a list of them.</summary>
    [Serializable]
    public class BuildtableCustomField
    {
        public string key;
        public string value;
    }

    /// <summary>
    /// What a caller fills in, either from the overlay or from game code via
    /// <see cref="Buildtable.SubmitBugReport"/>.
    /// </summary>
    public class BuildtableReportDraft
    {
        public string Title;
        public string Description;
        public string StepsToRepro;

        /// <summary>critical | high | medium | low. Anything else becomes medium server-side.</summary>
        public string Priority = "medium";

        public string ReporterName;
        public string ReporterEmail;

        /// <summary>JPEG bytes. Null sends a text-only report.</summary>
        public byte[] Screenshot;

        /// <summary>
        /// Values for custom fields defined on the project's bug board. Keys the
        /// project hasn't defined are dropped server-side, so sending an unknown
        /// key is harmless but pointless.
        /// </summary>
        public Dictionary<string, string> CustomFields;
    }

    /// <summary>
    /// A report as persisted to disk. Written before the first send attempt, so
    /// a crash or a quit mid-upload never loses a tester's report.
    ///
    /// Serialized with JsonUtility, so every field must be a public serializable
    /// primitive — this type is a storage record, not a payload builder.
    /// </summary>
    [Serializable]
    public class BuildtableQueuedReport
    {
        public string id;
        public long createdAtUtcTicks;
        public int attempts;

        /// <summary>When the last send was tried, so retries can back off instead of hammering a fixed interval.</summary>
        public long lastAttemptUtcTicks;

        public string title;
        public string description;
        public string stepsToRepro;
        public string priority;
        public string reporterName;
        public string reporterEmail;
        public string versionId;

        /// <summary>Raw <c>{ … }</c> fragment from <see cref="BuildtableDeviceInfo"/>.</summary>
        public string deviceInfoJson;

        /// <summary>Set once the screenshot has been uploaded, so a retry never re-uploads it.</summary>
        public string attachmentPath;

        /// <summary>True while a screenshot file is still sitting next to this record.</summary>
        public bool hasScreenshot;

        /// <summary>Project-defined field values. Filtered against the project's schema server-side.</summary>
        public List<BuildtableCustomField> customFields;

        public DateTime CreatedAtUtc => new DateTime(createdAtUtcTicks, DateTimeKind.Utc);

        /// <summary>
        /// Exponential backoff from the configured sweep interval, capped at an
        /// hour. A report the server keeps rejecting with a 429 shouldn't be
        /// retried every 60s for the life of the install.
        /// </summary>
        public bool IsDueForRetry(int retryIntervalSeconds)
        {
            if (attempts <= 0 || lastAttemptUtcTicks <= 0) return true;

            var backoff = retryIntervalSeconds * Math.Pow(2, Math.Min(attempts - 1, 8));
            var due = new DateTime(lastAttemptUtcTicks, DateTimeKind.Utc)
                .AddSeconds(Math.Min(backoff, 3600));
            return DateTime.UtcNow >= due;
        }

        /// <summary>
        /// Renders the <c>POST /api/v1/bugs</c> body. Attachments are spliced in
        /// at send time because the storage path only exists after the upload.
        /// </summary>
        public string ToRequestBody()
        {
            var json = BuildtableJson.Object();

            json.String("title", title);
            json.StringIfSet("description", description);
            json.StringIfSet("stepsToRepro", stepsToRepro);
            json.StringIfSet("priority", priority);
            json.StringIfSet("reporterName", reporterName);
            json.StringIfSet("reporterEmail", reporterEmail);
            json.StringIfSet("versionId", versionId);
            json.Raw("deviceInfo", deviceInfoJson);

            if (customFields != null && customFields.Count > 0)
            {
                json.BeginObject("customFields");
                foreach (var field in customFields)
                {
                    if (field == null || string.IsNullOrEmpty(field.key)) continue;
                    json.String(field.key, field.value);
                }
                json.EndObject();
            }

            if (!string.IsNullOrEmpty(attachmentPath))
            {
                json.BeginArray("attachments");
                json.Element(attachmentPath);
                json.EndArray();
            }

            return json.EndObject().ToString();
        }
    }
}
