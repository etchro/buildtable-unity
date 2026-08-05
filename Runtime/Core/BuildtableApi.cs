using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BuildtableSdk
{
    /// <summary>
    /// Outcome of one API call. <see cref="Retryable"/> is the important bit for
    /// the offline queue: a 400 will fail identically forever and must be
    /// dropped, while a network drop or a 429/5xx should be tried again.
    /// </summary>
    public struct BuildtableApiResult
    {
        public bool Success;
        public long StatusCode;
        public string Body;
        public string ErrorCode;
        public string ErrorMessage;
        public bool Retryable;

        public override string ToString() =>
            Success ? $"{StatusCode} OK" : $"{StatusCode} {ErrorCode}: {ErrorMessage}";
    }

    /// <summary>
    /// Thin UnityWebRequest wrapper for the <c>/api/v1</c> surface: Bearer auth,
    /// the per-install client header, and consistent error decoding.
    ///
    /// Coroutine-based rather than async/await so it behaves predictably on
    /// 2021.3 across all platforms including WebGL, where threads are absent.
    /// </summary>
    public static class BuildtableApi
    {
        [Serializable] private class ErrorEnvelope { public ApiErrorBody error; }
        [Serializable] private class ApiErrorBody { public string code; public string message; }

        [Serializable] private class UploadEnvelope { public UploadData data; }
        [Serializable] private class UploadData { public string path; public string mimeType; public long sizeBytes; }

        [Serializable] private class BugEnvelope { public BugData data; }
        [Serializable] private class BugData { public string id; public string status; }

        [Serializable] private class EventsEnvelope { public EventsData data; }
        [Serializable] private class EventsData { public int accepted; }

        /// <summary>
        /// POST /api/v1/uploads — multipart screenshot upload. Returns the
        /// storage path to pass as an <c>attachments</c> entry on the bug.
        /// </summary>
        public static IEnumerator UploadScreenshot(
            BuildtableConfig config,
            byte[] jpeg,
            Action<BuildtableApiResult, string> onDone)
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", jpeg, "screenshot.jpg", "image/jpeg"),
            };

            using (var request = UnityWebRequest.Post($"{config.ApiRoot}/api/v1/uploads", form))
            {
                // Content-Type (with its multipart boundary) is set by
                // UnityWebRequest.Post — do not override it here.
                ApplyAuth(request, config);
                request.timeout = config.requestTimeoutSeconds;
                yield return request.SendWebRequest();

                var result = Interpret(request);
                string path = null;
                if (result.Success)
                {
                    var envelope = SafeParse<UploadEnvelope>(result.Body);
                    path = envelope?.data?.path;
                    if (string.IsNullOrEmpty(path))
                    {
                        // A 2xx with no path means the contract changed; retrying
                        // won't help this build.
                        result.Success = false;
                        result.Retryable = false;
                        result.ErrorCode = "malformed_response";
                        result.ErrorMessage = "Upload succeeded but returned no storage path.";
                    }
                }

                onDone?.Invoke(result, path);
            }
        }

        /// <summary>POST /api/v1/bugs — create the bug. Returns the new bug id.</summary>
        public static IEnumerator PostBug(
            BuildtableConfig config,
            string jsonBody,
            Action<BuildtableApiResult, string> onDone)
        {
            using (var request = new UnityWebRequest($"{config.ApiRoot}/api/v1/bugs", UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                ApplyAuth(request, config);
                request.timeout = config.requestTimeoutSeconds;
                yield return request.SendWebRequest();

                var result = Interpret(request);
                var bugId = result.Success ? SafeParse<BugEnvelope>(result.Body)?.data?.id : null;
                onDone?.Invoke(result, bugId);
            }
        }

        /// <summary>
        /// POST /api/v1/events — telemetry batch. Returns how many events the
        /// server accepted, or -1 when the response didn't say.
        ///
        /// Unlike a bug report, an event is disposable: the caller drops the
        /// batch on failure rather than persisting it, so this never touches the
        /// offline queue.
        /// </summary>
        public static IEnumerator PostEvents(
            BuildtableConfig config,
            string jsonBody,
            Action<BuildtableApiResult, int> onDone)
        {
            using (var request = new UnityWebRequest($"{config.ApiRoot}/api/v1/events", UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                ApplyAuth(request, config);
                request.timeout = config.requestTimeoutSeconds;
                yield return request.SendWebRequest();

                var result = Interpret(request);
                var accepted = -1;
                if (result.Success)
                {
                    var envelope = SafeParse<EventsEnvelope>(result.Body);
                    if (envelope?.data != null) accepted = envelope.data.accepted;
                }

                onDone?.Invoke(result, accepted);
            }
        }

        private static void ApplyAuth(UnityWebRequest request, BuildtableConfig config)
        {
            request.SetRequestHeader("Authorization", "Bearer " + config.ingestApiKey);
            // Per-install id: without it every player shares one rate-limit
            // bucket keyed on the (often NAT'd) source IP.
            request.SetRequestHeader("X-Buildtable-Client", BuildtableInstall.InstallId);
        }

        private static BuildtableApiResult Interpret(UnityWebRequest request)
        {
            var status = request.responseCode;
            var body = request.downloadHandler != null ? request.downloadHandler.text : null;

            if (request.result == UnityWebRequest.Result.Success && status >= 200 && status < 300)
            {
                return new BuildtableApiResult { Success = true, StatusCode = status, Body = body };
            }

            var result = new BuildtableApiResult
            {
                Success = false,
                StatusCode = status,
                Body = body,
                ErrorCode = "request_failed",
                ErrorMessage = request.error ?? "Request failed.",
            };

            var envelope = SafeParse<ErrorEnvelope>(body);
            if (envelope?.error != null)
            {
                result.ErrorCode = envelope.error.code ?? result.ErrorCode;
                result.ErrorMessage = envelope.error.message ?? result.ErrorMessage;
            }

            // status 0 means the request never reached the server (offline, DNS,
            // timeout) — always worth another attempt. 429 and 5xx are transient
            // by definition. Every other 4xx is a payload or key problem that
            // will reproduce exactly, so queueing it forever is just noise.
            result.Retryable = status == 0 || status == 408 || status == 429 || status >= 500;
            return result;
        }

        private static T SafeParse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }
    }
}
