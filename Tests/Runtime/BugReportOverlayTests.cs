using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace BuildtableSdk.Tests
{
    /// <summary>
    /// Drives the real reporter and overlay against <see cref="FakeBuildtableServer"/>.
    /// The screenshot is skipped: capture waits for end of frame, which never
    /// arrives when the editor runs in batch mode.
    /// </summary>
    public class BugReportOverlayTests
    {
        private FakeBuildtableServer _server;
        private BuildtableConfig _config;
        private BuildtableBugReporter _reporter;
        private BuildtableBugReportOverlay _overlay;
        private string _savedName;
        private string _savedEmail;

        private static string QueueDirectory => new BuildtableReportQueue(1).Directory;

        [SetUp]
        public void SetUp()
        {
            // A project with autoStart on has already spawned its own reporter
            // with its own config; these tests need one built from theirs.
            if (BuildtableBugReporter.Instance != null)
                UnityEngine.Object.DestroyImmediate(BuildtableBugReporter.Instance.gameObject);

            ClearQueue();
            _savedName = BuildtableInstall.ReporterName;
            _savedEmail = BuildtableInstall.ReporterEmail;
            BuildtableInstall.ReporterName = "";
            BuildtableInstall.ReporterEmail = "";

            _server = new FakeBuildtableServer();

            _config = ScriptableObject.CreateInstance<BuildtableConfig>();
            _config.apiBaseUrl = _server.Url;
            _config.ingestApiKey = "test-key";
            _config.autoStart = false;
            _config.hotkeyEnabled = false;
            _config.captureScreenshot = false;
            _config.pauseWhileOpen = false;
            _config.releaseCursorWhileOpen = false;
            _config.requestTimeoutSeconds = 5;

            Buildtable.Config = _config;
            _reporter = Buildtable.Initialize();
            _overlay = BuildtableBugReportOverlay.Create(_reporter);
            _overlay.Open(null);
        }

        [TearDown]
        public void TearDown()
        {
            // Immediate, so the next SetUp doesn't find the old Instance still alive.
            if (_reporter != null) UnityEngine.Object.DestroyImmediate(_reporter.gameObject);
            UnityEngine.Object.DestroyImmediate(_config);
            Buildtable.Config = null;
            _server.Dispose();

            BuildtableInstall.ReporterName = _savedName;
            BuildtableInstall.ReporterEmail = _savedEmail;
            ClearQueue();
        }

        private static void ClearQueue()
        {
            if (Directory.Exists(QueueDirectory)) Directory.Delete(QueueDirectory, true);
        }

        private void Submit() => _overlay.SubmitButton.onClick.Invoke();

        private static IEnumerator WaitUntil(Func<bool> condition, float seconds = 10f)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        }

        // ------------------------------------------------------------- Form

        [Test]
        public void CancelKeepsTheDraft()
        {
            _overlay.TitleField.text = "Falls through floor";
            _overlay.DescriptionField.text = "Near the bridge";
            _overlay.StepsField.text = "1. Jump";
            _overlay.SetPriority("high");

            _overlay.Close();
            Assert.IsFalse(_overlay.IsOpen);
            _overlay.Open(null);

            Assert.AreEqual("Falls through floor", _overlay.TitleField.text);
            Assert.AreEqual("Near the bridge", _overlay.DescriptionField.text);
            Assert.AreEqual("1. Jump", _overlay.StepsField.text);
            Assert.AreEqual("high", _overlay.Priority);
        }

        [Test]
        public void SubmitWithoutTitleStaysOpen()
        {
            _overlay.DescriptionField.text = "No title here";

            Submit();

            Assert.IsTrue(_overlay.IsOpen);
            Assert.AreEqual("A title is required.", _overlay.StatusMessage);
            Assert.AreEqual(0, _reporter.PendingReportCount);
            Assert.AreEqual("No title here", _overlay.DescriptionField.text);
        }

        [UnityTest]
        public IEnumerator SubmitClearsTheDraftAndRemembersIdentity()
        {
            _overlay.TitleField.text = "Crash on load";
            _overlay.DescriptionField.text = "Black screen";
            _overlay.StepsField.text = "1. Load save";
            _overlay.NameField.text = "Ada";
            _overlay.EmailField.text = "ada@example.com";
            _overlay.SetPriority("critical");

            Submit();
            Assert.IsFalse(_overlay.IsOpen);
            yield return WaitUntil(() => _server.BugRequests > 0);

            _overlay.Open(null);
            Assert.AreEqual("", _overlay.TitleField.text);
            Assert.AreEqual("", _overlay.DescriptionField.text);
            Assert.AreEqual("", _overlay.StepsField.text);
            Assert.AreEqual("medium", _overlay.Priority);
            Assert.AreEqual("Ada", _overlay.NameField.text);
            Assert.AreEqual("ada@example.com", _overlay.EmailField.text);
        }

        [Test]
        public void ReopeningKeepsATypedNameOverTheSavedOne()
        {
            _overlay.Close();
            BuildtableInstall.ReporterName = "Saved Name";
            _overlay.NameField.text = "";
            _overlay.Open(null);
            Assert.AreEqual("Saved Name", _overlay.NameField.text);

            _overlay.NameField.text = "Typed Name";
            _overlay.Close();
            _overlay.Open(null);

            Assert.AreEqual("Typed Name", _overlay.NameField.text);
        }

        [Test]
        public void TabCyclesThroughFieldsAndWraps()
        {
            var events = EventSystem.current;
            Assert.AreEqual(_overlay.TitleField.gameObject, events.currentSelectedGameObject, "title focused on open");

            var order = new[]
            {
                _overlay.DescriptionField, _overlay.StepsField, _overlay.NameField, _overlay.EmailField,
                _overlay.TitleField,
            };
            foreach (var expected in order)
            {
                _overlay.FocusNextField();
                Assert.AreEqual(expected.gameObject, events.currentSelectedGameObject);
            }
        }

        // ------------------------------------------------------------ Toast

        [UnityTest]
        public IEnumerator DeliveredReportShowsSentToast()
        {
            _overlay.TitleField.text = "Works";
            Submit();

            Assert.IsTrue(_overlay.IsToastVisible);
            Assert.AreEqual("Sending report…", _overlay.ToastMessage);

            yield return WaitUntil(() => _overlay.ToastMessage != "Sending report…");

            Assert.AreEqual("Report sent. Thanks!", _overlay.ToastMessage);
            Assert.AreEqual(1, _server.BugRequests);
            Assert.AreEqual(0, _reporter.PendingReportCount);
        }

        [UnityTest]
        public IEnumerator ServerErrorKeepsReportAndSaysSo()
        {
            _server.BugStatus = 503;
            _overlay.TitleField.text = "Server down";
            Submit();

            yield return WaitUntil(() => _overlay.ToastMessage != "Sending report…");

            Assert.AreEqual("Couldn't reach Buildtable. Your report is saved and will send later.", _overlay.ToastMessage);
            Assert.AreEqual(1, _reporter.PendingReportCount);
        }

        [UnityTest]
        public IEnumerator RejectedReportSaysItCouldNotBeSent()
        {
            _server.BugStatus = 400;
            LogAssert.Expect(LogType.Error, new Regex("Report rejected and discarded"));
            _overlay.TitleField.text = "Bad payload";
            Submit();

            yield return WaitUntil(() => _overlay.ToastMessage != "Sending report…");

            Assert.AreEqual("Your report couldn't be sent.", _overlay.ToastMessage);
            Assert.AreEqual(0, _reporter.PendingReportCount);
        }

        [Test]
        public void MissingApiKeyWarnsImmediately()
        {
            _config.ingestApiKey = "";
            _overlay.TitleField.text = "No key";
            Submit();

            Assert.AreEqual("Report saved, but it can't be sent: no Buildtable API key is set.", _overlay.ToastMessage);
            Assert.AreEqual(1, _reporter.PendingReportCount);
            Assert.AreEqual(0, _server.BugRequests);
        }

        [Test]
        public void OpeningTheFormHidesTheToast()
        {
            _config.ingestApiKey = "";
            _overlay.TitleField.text = "No key";
            Submit();
            Assert.IsTrue(_overlay.IsToastVisible);

            _overlay.Open(null);

            Assert.IsFalse(_overlay.IsToastVisible);
        }

        [UnityTest]
        public IEnumerator FeedbackOffShowsNoToast()
        {
            _config.showSubmitFeedback = false;
            _overlay.TitleField.text = "Quiet";
            Submit();

            yield return WaitUntil(() => _server.BugRequests > 0);
            yield return null;

            Assert.IsFalse(_overlay.IsToastVisible);
        }
    }
}
