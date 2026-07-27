using System;
using System.Collections;
using UnityEngine;

namespace BuildtableSdk
{
    /// <summary>
    /// Captures the current frame as JPEG bytes, downscaled so it stays well
    /// inside the server's 5 MB per-attachment cap.
    /// </summary>
    public static class BuildtableScreenshot
    {
        /// <summary>
        /// Must run before the reporter overlay is drawn, or the capture will
        /// contain the overlay instead of the game. The caller yields on this,
        /// then shows the UI.
        /// </summary>
        public static IEnumerator Capture(int maxDimension, int quality, Action<byte[]> onDone)
        {
            // CaptureScreenshotAsTexture is only valid after the frame has
            // finished rendering.
            yield return new WaitForEndOfFrame();

            Texture2D source = null;
            Texture2D scaled = null;
            byte[] jpeg = null;

            try
            {
                source = ScreenCapture.CaptureScreenshotAsTexture();
                scaled = Downscale(source, maxDimension);
                jpeg = (scaled ?? source).EncodeToJPG(Mathf.Clamp(quality, 30, 100));
            }
            catch (Exception e)
            {
                // A failed screenshot must never cost the tester their report.
                Debug.LogWarning($"[Buildtable] Screenshot capture failed: {e.Message}");
            }
            finally
            {
                if (source != null) UnityEngine.Object.Destroy(source);
                if (scaled != null) UnityEngine.Object.Destroy(scaled);
            }

            onDone?.Invoke(jpeg);
        }

        /// <summary>
        /// Returns null when the source already fits, so the caller can encode
        /// the original and skip a copy.
        /// </summary>
        private static Texture2D Downscale(Texture2D source, int maxDimension)
        {
            if (source == null) return null;

            var longest = Mathf.Max(source.width, source.height);
            if (maxDimension <= 0 || longest <= maxDimension) return null;

            var scale = (float)maxDimension / longest;
            var width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));

            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;

                var result = new Texture2D(width, height, TextureFormat.RGB24, false);
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply();
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>Small preview texture for the overlay thumbnail.</summary>
        public static Texture2D DecodePreview(byte[] jpeg)
        {
            if (jpeg == null || jpeg.Length == 0) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            return texture.LoadImage(jpeg) ? texture : null;
        }
    }
}
