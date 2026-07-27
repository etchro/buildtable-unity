using System;

namespace BuildtableSdk
{
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

        /// <summary>Attach the recent console tail. Ignored when the log buffer is off.</summary>
        public bool IncludeLogs = true;
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

        public DateTime CreatedAtUtc => new DateTime(createdAtUtcTicks, DateTimeKind.Utc);

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
