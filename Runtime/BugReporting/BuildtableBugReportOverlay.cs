using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BuildtableSdk
{
    /// <summary>
    /// The in-game report form, built entirely in code with uGUI.
    ///
    /// No prefabs, no UXML, no fonts of our own: a drop-in SDK shouldn't ask a
    /// project to import assets or add packages before its first report works,
    /// and a code-built canvas renders identically on every 2021.3+ project.
    /// The rounded corners, shadow, and checkmark are textures generated at
    /// build time for the same reason.
    ///
    /// Colours, copy, font, rounding, and which fields appear all come from
    /// <see cref="BuildtableConfig"/>, so a project can make the form look like
    /// its own UI without forking this file.
    /// </summary>
    // Runs before the EventSystem (order 0). Legacy InputField treats Escape as
    // "cancel this edit" and reverts the focused field; closing first disables
    // the field before it sees the key, so the draft survives intact.
    [DefaultExecutionOrder(-1000)]
    public class BuildtableBugReportOverlay : MonoBehaviour
    {
        private const float ContentPadding = 24f;
        private const float ActionsHeight = 40f;
        private const float ActionsSpacing = 24f;
        private const float BorderWidth = 1f;
        private const int ShadowSoftness = 24;
        private const float ToastHeight = 48f;
        private const float ToastFadeSeconds = 0.25f;

        /// <summary>
        /// Rounded sprites are generated at this multiple of their on-screen
        /// radius so corners stay smooth when the canvas scales up past 1080p.
        /// </summary>
        private const int RoundedSpriteScale = 2;

        private static readonly string[] Priorities = { "critical", "high", "medium", "low" };

        private static readonly Color[] PriorityDotColors =
        {
            new Color(0.94f, 0.33f, 0.31f),
            new Color(1f, 0.6f, 0f),
            new Color(0.98f, 0.75f, 0.18f),
            new Color(0.55f, 0.55f, 0.6f),
        };

        private static readonly Color ErrorColor = new Color(1f, 0.5f, 0.45f);
        private static readonly Color WarningColor = new Color(1f, 0.7f, 0.3f);
        private static readonly Color SuccessColor = new Color(0.35f, 0.8f, 0.5f);

        /// <summary>
        /// One stacked row. The layout is a top-down cursor rather than a layout
        /// group: nested multiline input fields fight ContentSizeFitter, and a
        /// cursor is trivial to recompute when a row is hidden.
        /// </summary>
        private struct Row
        {
            public RectTransform Rect;
            public float Height;
            public float SpacingAfter;
            /// <summary>Only laid out when the report actually has a screenshot.</summary>
            public bool PreviewOnly;
        }

        private BuildtableBugReporter _reporter;
        private BuildtableConfig _config;
        private Font _font;

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<InputField> _tabOrder = new List<InputField>();
        private readonly Dictionary<int, Sprite> _roundedSprites = new Dictionary<int, Sprite>();
        private readonly List<UnityEngine.Object> _generatedAssets = new List<UnityEngine.Object>();

        private GameObject _root;
        private RectTransform _panelRect;
        private RectTransform _previewRow;
        private Image _previewImage;
        private InputField _titleField;
        private InputField _descriptionField;
        private InputField _stepsField;
        private InputField _nameField;
        private InputField _emailField;
        private Toggle _includeScreenshotToggle;
        private Text _statusText;
        private Button _submitButton;
        private Image[] _priorityFills = Array.Empty<Image>();
        private Image[] _priorityDots = Array.Empty<Image>();

        private GameObject _toastRoot;
        private CanvasGroup _toastGroup;
        private RectTransform _toastPanel;
        private Text _toastText;
        private Image _toastDot;
        private float _toastHideAt;
        /// <summary>True between a submit from this form and the first delivery result after it.</summary>
        private bool _awaitingDelivery;

        private byte[] _screenshot;
        private Texture2D _previewTexture;
        private string _priority = "medium";

        private float _previousTimeScale = 1f;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;

        public bool IsOpen => _root != null && _root.activeSelf;

        // Read-only handles for tests; the form itself is driven through these
        // the same way a click or keystroke would drive it.
        internal InputField TitleField => _titleField;
        internal InputField DescriptionField => _descriptionField;
        internal InputField StepsField => _stepsField;
        internal InputField NameField => _nameField;
        internal InputField EmailField => _emailField;
        internal Button SubmitButton => _submitButton;
        internal string StatusMessage => _statusText.text;
        internal string Priority => _priority;
        internal bool IsToastVisible => _toastRoot != null && _toastRoot.activeSelf;
        internal string ToastMessage => _toastText.text;

        public static BuildtableBugReportOverlay Create(BuildtableBugReporter reporter)
        {
            var go = new GameObject("BuildtableBugReportOverlay");
            go.transform.SetParent(reporter.transform, false);
            var overlay = go.AddComponent<BuildtableBugReportOverlay>();
            overlay._reporter = reporter;
            overlay._config = reporter.Config;
            overlay.Build();
            overlay.BuildToast();
            reporter.ReportSubmitted += overlay.OnReportSubmitted;
            reporter.ReportFailed += overlay.OnReportFailed;
            return overlay;
        }

        private void Update()
        {
            if (IsOpen)
            {
                if (BuildtableInput.GetKeyDown(KeyCode.Escape)) Close();
                else if (BuildtableInput.GetKeyDown(KeyCode.Tab)) FocusNextField();
            }

            UpdateToast();
        }

        public void Open(byte[] screenshot)
        {
            HideToast();

            _screenshot = screenshot;
            SetStatus("", _config.mutedTextColor);

            // Whatever was typed before a Cancel/Escape is still in the fields;
            // only a submit clears them (ClearDraft). Identity is filled in only
            // when empty, so it can't overwrite a name the tester just typed.
            if (_nameField != null && string.IsNullOrEmpty(_nameField.text)) _nameField.text = BuildtableInstall.ReporterName;
            if (_emailField != null && string.IsNullOrEmpty(_emailField.text)) _emailField.text = BuildtableInstall.ReporterEmail;
            SetPriority(_priority);
            _submitButton.interactable = true;

            ShowPreview(screenshot);

            var pending = _reporter.PendingReportCount;
            if (pending > 0) SetStatus($"{pending} earlier report(s) still waiting to send.", _config.mutedTextColor);

            _root.SetActive(true);

            if (_config.pauseWhileOpen)
            {
                _previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }

            if (_config.releaseCursorWhileOpen)
            {
                _previousCursorLock = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            EventSystem.current?.SetSelectedGameObject(_titleField.gameObject);
        }

        public void Close()
        {
            if (_root == null || !_root.activeSelf) return;
            _root.SetActive(false);

            if (_config.pauseWhileOpen) Time.timeScale = _previousTimeScale;
            if (_config.releaseCursorWhileOpen)
            {
                Cursor.lockState = _previousCursorLock;
                Cursor.visible = _previousCursorVisible;
            }

            ReleasePreview();
            _screenshot = null;
        }

        private void OnDestroy()
        {
            if (_reporter != null)
            {
                _reporter.ReportSubmitted -= OnReportSubmitted;
                _reporter.ReportFailed -= OnReportFailed;
            }

            ReleasePreview();
            foreach (var asset in _generatedAssets)
            {
                if (asset != null) Destroy(asset);
            }
            _generatedAssets.Clear();
            _roundedSprites.Clear();
        }

        private void Submit()
        {
            if (string.IsNullOrWhiteSpace(_titleField.text))
            {
                SetStatus("A title is required.", ErrorColor);
                EventSystem.current?.SetSelectedGameObject(_titleField.gameObject);
                return;
            }

            var draft = new BuildtableReportDraft
            {
                Title = _titleField.text,
                Description = _descriptionField.text,
                StepsToRepro = _stepsField != null ? _stepsField.text : null,
                Priority = _priority,
                ReporterName = _nameField != null ? _nameField.text : null,
                ReporterEmail = _emailField != null ? _emailField.text : null,
                Screenshot = _includeScreenshotToggle.isOn ? _screenshot : null,
            };

            _submitButton.interactable = false;

            // Armed before Submit() so a result raised synchronously inside it
            // still reaches the notice.
            _awaitingDelivery = _config.showSubmitFeedback;
            _reporter.Submit(draft);
            ClearDraft();

            // Submit() persists to disk before any network call, so the report is
            // safe the moment it returns — close rather than make the tester wait
            // on a request that may be retried for minutes. The toast reports the
            // outcome once there is one.
            Close();
            AnnounceSubmission();
        }

        /// <summary>
        /// Resets the form for the next report. Name and email are emptied too
        /// so Open refills them from what Submit just saved to the install.
        /// </summary>
        private void ClearDraft()
        {
            _titleField.text = "";
            _descriptionField.text = "";
            if (_stepsField != null) _stepsField.text = "";
            if (_nameField != null) _nameField.text = "";
            if (_emailField != null) _emailField.text = "";
            _priority = "medium";
        }

        /// <summary>Moves focus to the next text field, wrapping. Legacy InputField has no tab order of its own.</summary>
        internal void FocusNextField()
        {
            if (_tabOrder.Count == 0 || EventSystem.current == null) return;

            var selected = EventSystem.current.currentSelectedGameObject;
            var index = _tabOrder.FindIndex(field => field.gameObject == selected);
            var next = _tabOrder[(index + 1) % _tabOrder.Count];
            EventSystem.current.SetSelectedGameObject(next.gameObject);
        }

        // ------------------------------------------------------------- Toast

        private void AnnounceSubmission()
        {
            // Already resolved synchronously by an event handler.
            if (!_awaitingDelivery) return;

            if (!_config.IsUsable)
            {
                _awaitingDelivery = false;
                ShowToast("Report saved, but it can't be sent: no Buildtable API key is set.", WarningColor, 6f);
            }
            else if (_reporter.PendingReportCount == 0)
            {
                // Nothing reached the disk queue (full disk, unwritable path),
                // so there is nothing that will ever be sent.
                _awaitingDelivery = false;
                ShowToast("The report couldn't be saved on this device.", ErrorColor, 6f);
            }
            else
            {
                // Upload plus post, each bounded by the request timeout. Past
                // this the send is still alive, just not worth a spinner.
                ShowToast("Sending report…", _config.mutedTextColor, _config.requestTimeoutSeconds * 2f + 5f);
            }
        }

        // The reporter's events also fire for older queued reports. The first
        // result after a submit is close enough: a backlog that delivers proves
        // the connection works, and one that fails means this report will too.
        private void OnReportSubmitted(string bugId)
        {
            if (!_awaitingDelivery) return;
            _awaitingDelivery = false;
            ShowToast("Report sent. Thanks!", SuccessColor, 3f);
        }

        private void OnReportFailed(string reason)
        {
            if (!_awaitingDelivery) return;
            _awaitingDelivery = false;

            // A transient failure keeps the report on disk; a permanent one
            // removes it before raising the event.
            if (_reporter.PendingReportCount > 0)
                ShowToast("Couldn't reach Buildtable. Your report is saved and will send later.", WarningColor, 5f);
            else
                ShowToast("Your report couldn't be sent.", ErrorColor, 5f);
        }

        private void ShowToast(string message, Color dotColor, float seconds)
        {
            _toastText.text = message;
            _toastDot.color = dotColor;
            _toastPanel.sizeDelta = new Vector2(Mathf.Min(_toastText.preferredWidth + 68f, 1200f), ToastHeight);
            _toastGroup.alpha = 1f;
            _toastHideAt = Time.unscaledTime + seconds;
            _toastRoot.SetActive(true);
        }

        private void HideToast()
        {
            _awaitingDelivery = false;
            if (_toastRoot != null) _toastRoot.SetActive(false);
        }

        private void UpdateToast()
        {
            if (_toastRoot == null || !_toastRoot.activeSelf) return;

            var now = Time.unscaledTime;
            if (now < _toastHideAt) return;

            if (_awaitingDelivery)
            {
                _awaitingDelivery = false;
                ShowToast("Report saved. It will keep trying to send in the background.", _config.mutedTextColor, 4f);
                return;
            }

            var alpha = 1f - (now - _toastHideAt) / ToastFadeSeconds;
            if (alpha <= 0f) _toastRoot.SetActive(false);
            else _toastGroup.alpha = alpha;
        }

        // ---------------------------------------------------------------- UI

        private void Build()
        {
            _font = _config.font != null ? _config.font : LoadDefaultFont();

            _root = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _root.transform.SetParent(transform, false);
            ConfigureCanvas(_root);

            EnsureEventSystem();

            var scrim = CreateImage(_root.transform, "Scrim", new Color(0f, 0f, 0f, _config.scrimOpacity));
            Stretch(scrim.rectTransform);

            // An empty parent so the shadow and border, which have to draw
            // behind the panel, resize with it.
            _panelRect = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            _panelRect.SetParent(_root.transform, false);
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);

            var panelRadius = _config.cornerRadius * 2;
            CreateShadow(_panelRect, panelRadius);

            var border = CreateDecoration(_panelRect, "Border", White(0.08f));
            Stretch(border.rectTransform, -BorderWidth, -BorderWidth);
            MakeRounded(border, panelRadius + 1);

            var background = CreateImage(_panelRect, "Background", _config.panelColor);
            Stretch(background.rectTransform);
            MakeRounded(background, panelRadius);

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(_panelRect, false);
            Stretch(content, ContentPadding, ContentPadding);

            var heading = CreateText(content, "Heading", _config.formTitle, 26, _config.textColor, FontStyle.Bold);
            AddRow(heading.rectTransform, 34, 4);

            if (!string.IsNullOrEmpty(_config.formSubtitle))
            {
                var subheading = CreateText(content, "Subheading", _config.formSubtitle, 15, _config.mutedTextColor);
                AddRow(subheading.rectTransform, 22, 0);
            }

            AddDivider(content);

            // Screenshot row. Registered as preview-only so a text-only report
            // closes the gap instead of leaving a hole under the heading.
            _previewRow = new GameObject("PreviewRow", typeof(RectTransform)).GetComponent<RectTransform>();
            _previewRow.SetParent(content, false);
            AddRow(_previewRow, 116, 12, previewOnly: true);

            _previewImage = CreateImage(_previewRow, "Preview", Color.white);
            _previewImage.rectTransform.anchorMin = new Vector2(0, 0);
            _previewImage.rectTransform.anchorMax = new Vector2(0, 1);
            _previewImage.rectTransform.pivot = new Vector2(0, 0.5f);
            _previewImage.rectTransform.anchoredPosition = new Vector2(0, 0);
            _previewImage.rectTransform.sizeDelta = new Vector2(206, 0);
            _previewImage.preserveAspect = true;

            _includeScreenshotToggle = CreateToggle(_previewRow, "IncludeScreenshot", "Include this screenshot", 220);

            _titleField = AddLabeledField(content, "Title", "Title", true, 36, false, "Short summary of the problem");
            _descriptionField = AddLabeledField(content, "Description", "What happened?", false, 92, true, "What you saw, and what you expected");

            if (_config.showStepsToRepro)
            {
                _stepsField = AddLabeledField(content, "Steps", "Steps to reproduce", false, 74, true, "1. …\n2. …");
            }

            if (_config.showPriority)
            {
                AddRow(CreateLabel(content, "PriorityLabel", "Priority", false).rectTransform, 20, 6);

                var priorityRow = new GameObject("PriorityRow", typeof(RectTransform)).GetComponent<RectTransform>();
                priorityRow.SetParent(content, false);
                AddRow(priorityRow, 34, 14);

                _priorityFills = new Image[Priorities.Length];
                _priorityDots = new Image[Priorities.Length];
                for (var i = 0; i < Priorities.Length; i++)
                {
                    var priority = Priorities[i];
                    var button = CreateButton(priorityRow, priority, Capitalize(priority), () => SetPriority(priority),
                        _config.fieldColor, false, false, out _priorityFills[i]);
                    var rect = button.GetComponent<RectTransform>();
                    rect.anchorMin = new Vector2(0, 0);
                    rect.anchorMax = new Vector2(0, 1);
                    rect.pivot = new Vector2(0, 0.5f);
                    rect.sizeDelta = new Vector2(120, 0);
                    rect.anchoredPosition = new Vector2(i * 126, 0);
                    _priorityDots[i] = AddDot(button);
                }
            }

            if (_config.showReporterIdentity)
            {
                AddRow(CreateLabel(content, "ContactLabel", "Contact (optional)", false).rectTransform, 20, 6);

                var identityRow = new GameObject("IdentityRow", typeof(RectTransform)).GetComponent<RectTransform>();
                identityRow.SetParent(content, false);
                AddRow(identityRow, 36, 14);

                _nameField = CreateInputField(identityRow, "Name", "Your name", false);
                HalfWidth(_nameField.GetComponent<RectTransform>(), 0);
                _emailField = CreateInputField(identityRow, "Email", "Your email", false);
                HalfWidth(_emailField.GetComponent<RectTransform>(), 1);
                _emailField.contentType = InputField.ContentType.EmailAddress;
            }

            _statusText = CreateText(content, "Status", "", 14, _config.mutedTextColor);
            AddRow(_statusText.rectTransform, 20, 0);

            // Actions pinned to the bottom of the content rect. ApplyLayout sizes
            // the panel so this band never lands on top of the rows above it.
            var actionsDivider = CreateDecoration(content, "ActionsDivider", White(0.07f));
            actionsDivider.rectTransform.anchorMin = new Vector2(0, 0);
            actionsDivider.rectTransform.anchorMax = new Vector2(1, 0);
            actionsDivider.rectTransform.pivot = new Vector2(0.5f, 0);
            actionsDivider.rectTransform.anchoredPosition = new Vector2(0, ActionsHeight + ActionsSpacing / 2f);
            actionsDivider.rectTransform.sizeDelta = new Vector2(0, BorderWidth);

            var actions = new GameObject("Actions", typeof(RectTransform)).GetComponent<RectTransform>();
            actions.SetParent(content, false);
            actions.anchorMin = new Vector2(0, 0);
            actions.anchorMax = new Vector2(1, 0);
            actions.pivot = new Vector2(0.5f, 0);
            actions.anchoredPosition = Vector2.zero;
            actions.sizeDelta = new Vector2(0, ActionsHeight);

            var cancel = CreateButton(actions, "Cancel", "Cancel (Esc)", Close, _config.panelColor, true, false, out _);
            AnchorRight(cancel.GetComponent<RectTransform>(), 150, 158);

            _submitButton = CreateButton(actions, "Submit", _config.submitButtonLabel, Submit,
                _config.accentColor, false, true, out _);
            AnchorRight(_submitButton.GetComponent<RectTransform>(), 150, 0);

            ApplyLayout(false);
            _root.SetActive(false);
        }

        /// <summary>
        /// A separate canvas with no raycaster, so the notice can stay up after
        /// the form closes without ever eating a click meant for the game.
        /// </summary>
        private void BuildToast()
        {
            _toastRoot = new GameObject("Toast", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            _toastRoot.transform.SetParent(transform, false);
            ConfigureCanvas(_toastRoot);

            _toastGroup = _toastRoot.GetComponent<CanvasGroup>();
            _toastGroup.interactable = false;
            _toastGroup.blocksRaycasts = false;

            _toastPanel = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            _toastPanel.SetParent(_toastRoot.transform, false);
            _toastPanel.anchorMin = _toastPanel.anchorMax = new Vector2(0.5f, 0);
            _toastPanel.pivot = new Vector2(0.5f, 0);
            _toastPanel.anchoredPosition = new Vector2(0, 56);
            _toastPanel.sizeDelta = new Vector2(400, ToastHeight);

            var radius = _config.cornerRadius * 2;
            CreateShadow(_toastPanel, radius);

            var border = CreateDecoration(_toastPanel, "Border", White(0.1f));
            Stretch(border.rectTransform, -BorderWidth, -BorderWidth);
            MakeRounded(border, radius + 1);

            var background = CreateDecoration(_toastPanel, "Background", _config.panelColor);
            Stretch(background.rectTransform);
            MakeRounded(background, radius);

            _toastDot = CreateDecoration(_toastPanel, "Dot", _config.mutedTextColor);
            _toastDot.rectTransform.anchorMin = _toastDot.rectTransform.anchorMax = new Vector2(0, 0.5f);
            _toastDot.rectTransform.pivot = new Vector2(0, 0.5f);
            _toastDot.rectTransform.anchoredPosition = new Vector2(22, 0);
            _toastDot.rectTransform.sizeDelta = new Vector2(10, 10);
            MakeRounded(_toastDot, 5, ignoreZero: true);

            _toastText = CreateText(_toastPanel, "Message", "", 16, _config.textColor);
            _toastText.rectTransform.anchorMin = Vector2.zero;
            _toastText.rectTransform.anchorMax = Vector2.one;
            _toastText.rectTransform.offsetMin = new Vector2(44, 0);
            _toastText.rectTransform.offsetMax = new Vector2(-20, 0);
            _toastText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _toastText.raycastTarget = false;

            _toastRoot.SetActive(false);
        }

        private void ConfigureCanvas(GameObject root)
        {
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = _config.sortingOrder;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
        }

        private void AddRow(RectTransform rect, float height, float spacingAfter, bool previewOnly = false)
        {
            _rows.Add(new Row { Rect = rect, Height = height, SpacingAfter = spacingAfter, PreviewOnly = previewOnly });
        }

        /// <summary>A hairline under whatever row came last, with even space either side.</summary>
        private void AddDivider(RectTransform parent)
        {
            if (_rows.Count > 0)
            {
                var previous = _rows[_rows.Count - 1];
                previous.SpacingAfter = 14;
                _rows[_rows.Count - 1] = previous;
            }

            var divider = CreateDecoration(parent, "Divider", White(0.07f));
            AddRow(divider.rectTransform, BorderWidth, 16);
        }

        /// <summary>
        /// Stacks every visible row from the top of the content rect and sizes the
        /// panel to exactly fit them plus the action bar. Recomputed on each open
        /// because the screenshot row's visibility isn't known until then.
        /// </summary>
        private void ApplyLayout(bool hasScreenshot)
        {
            var y = 0f;
            foreach (var row in _rows)
            {
                var visible = !row.PreviewOnly || hasScreenshot;
                row.Rect.gameObject.SetActive(visible);
                if (!visible) continue;

                Place(row.Rect, y, row.Height);
                y += row.Height + row.SpacingAfter;
            }

            var height = ContentPadding * 2 + y + ActionsSpacing + ActionsHeight;
            _panelRect.sizeDelta = new Vector2(_config.panelWidth, height);
        }

        private void ShowPreview(byte[] screenshot)
        {
            ReleasePreview();

            var hasScreenshot = screenshot != null && screenshot.Length > 0;
            ApplyLayout(hasScreenshot);

            if (!hasScreenshot)
            {
                _includeScreenshotToggle.isOn = false;
                return;
            }

            _previewTexture = BuildtableScreenshot.DecodePreview(screenshot);
            if (_previewTexture != null)
            {
                _previewImage.sprite = Sprite.Create(
                    _previewTexture,
                    new Rect(0, 0, _previewTexture.width, _previewTexture.height),
                    new Vector2(0.5f, 0.5f));
            }

            var kilobytes = Mathf.CeilToInt(screenshot.Length / 1024f);
            _includeScreenshotToggle.isOn = true;
            SetToggleLabel(_includeScreenshotToggle, $"Include this screenshot ({kilobytes} KB)");
        }

        private void ReleasePreview()
        {
            if (_previewImage != null && _previewImage.sprite != null)
            {
                Destroy(_previewImage.sprite);
                _previewImage.sprite = null;
            }
            if (_previewTexture != null)
            {
                Destroy(_previewTexture);
                _previewTexture = null;
            }
        }

        internal void SetPriority(string priority)
        {
            _priority = priority;
            for (var i = 0; i < _priorityFills.Length; i++)
            {
                var selected = Priorities[i] == priority;
                _priorityFills[i].color = selected ? _config.accentColor : _config.fieldColor;
                _priorityDots[i].color = selected ? Color.white : PriorityDotColors[i];
            }
        }

        private void SetStatus(string message, Color color)
        {
            _statusText.text = message;
            _statusText.color = color;
        }

        // ------------------------------------------------------ UI helpers

        private InputField AddLabeledField(RectTransform parent, string name, string label, bool required,
            float height, bool multiline, string placeholder)
        {
            AddRow(CreateLabel(parent, name + "Label", label, required).rectTransform, 20, 6);

            var field = CreateInputField(parent, name + "Field", placeholder, multiline);
            AddRow(field.GetComponent<RectTransform>(), height, 12);
            return field;
        }

        private Text CreateLabel(RectTransform parent, string name, string label, bool required)
        {
            var text = CreateText(parent, name, label, 14, _config.mutedTextColor, FontStyle.Bold);
            if (required)
            {
                text.supportRichText = true;
                text.text = $"{label} <color=#{ColorUtility.ToHtmlStringRGBA(_config.accentColor)}>*</color>";
            }
            return text;
        }

        /// <summary>
        /// The InputField sits on a border image with the fill inset by a pixel,
        /// so the Selectable's own tint transition draws the hover and focus ring
        /// with no extra state to keep in sync.
        /// </summary>
        private InputField CreateInputField(RectTransform parent, string name, string placeholder, bool multiline)
        {
            var frame = CreateFramed(parent, name, _config.fieldColor, _config.cornerRadius, out _);
            var field = frame.gameObject.AddComponent<InputField>();
            field.targetGraphic = frame;
            field.colors = FrameColors(White(0.08f), White(0.22f), _config.accentColor, _config.accentColor);

            var textComponent = CreateText(frame.rectTransform, "Text", "", 16, _config.textColor);
            Stretch(textComponent.rectTransform, 10, 6);
            textComponent.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            textComponent.supportRichText = false;

            var placeholderText = CreateText(frame.rectTransform, "Placeholder", placeholder, 16, _config.mutedTextColor);
            Stretch(placeholderText.rectTransform, 10, 6);
            placeholderText.alignment = textComponent.alignment;
            placeholderText.fontStyle = FontStyle.Italic;

            field.textComponent = textComponent;
            field.placeholder = placeholderText;
            field.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            // Matches the server's own limits so a tester is never silently
            // truncated after the fact.
            field.characterLimit = multiline ? 10000 : 500;
            // Tab moves focus instead (see FocusNextField); a multiline field
            // would otherwise also insert the character it's leaving with.
            // Single-line fields already ignore Tab, and a validator would
            // replace their contentType validation (the email field's).
            if (multiline) field.onValidateInput = (text, index, character) => character == '\t' ? '\0' : character;

            _tabOrder.Add(field);
            return field;
        }

        private Toggle CreateToggle(RectTransform parent, string name, string label, float x)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 0.5f);
            rect.anchorMax = new Vector2(0, 0.5f);
            rect.pivot = new Vector2(0, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0);
            rect.sizeDelta = new Vector2(320, 24);

            var toggle = go.AddComponent<Toggle>();

            var box = CreateImage(rect, "Box", _config.fieldColor);
            box.rectTransform.anchorMin = new Vector2(0, 0.5f);
            box.rectTransform.anchorMax = new Vector2(0, 0.5f);
            box.rectTransform.pivot = new Vector2(0, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(22, 22);
            MakeRounded(box, _config.cornerRadius / 2);

            var check = CreateDecoration(box.rectTransform, "Check", Color.white);
            check.sprite = CreateCheckSprite();
            check.preserveAspect = true;
            Stretch(check.rectTransform, 3, 3);

            var text = CreateText(rect, "Label", label, 15, _config.textColor);
            text.rectTransform.anchorMin = new Vector2(0, 0);
            text.rectTransform.anchorMax = new Vector2(1, 1);
            text.rectTransform.offsetMin = new Vector2(30, 0);
            text.rectTransform.offsetMax = Vector2.zero;
            text.alignment = TextAnchor.MiddleLeft;

            toggle.targetGraphic = box;
            toggle.graphic = check;
            // The checkmark is white, so the box carries the on/off colour.
            toggle.onValueChanged.AddListener(on => box.color = on ? _config.accentColor : _config.fieldColor);
            toggle.isOn = true;
            box.color = _config.accentColor;
            return toggle;
        }

        private static void SetToggleLabel(Toggle toggle, string label)
        {
            var text = toggle.transform.Find("Label")?.GetComponent<Text>();
            if (text != null) text.text = label;
        }

        private Button CreateButton(RectTransform parent, string name, string label, Action onClick,
            Color fillColor, bool outlined, bool bold, out Image fill)
        {
            var frame = CreateFramed(parent, name, fillColor, _config.cornerRadius, out fill);
            var button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            // Selected matches normal: a mouse click leaves a button selected,
            // and a ring that lingers after the click reads as a stuck state.
            var normal = outlined ? White(0.16f) : White(0f);
            button.colors = FrameColors(normal, White(0.4f), White(0.25f), normal);
            button.onClick.AddListener(() => onClick());

            var text = CreateText(frame.rectTransform, "Label", label, 16, _config.textColor,
                bold ? FontStyle.Bold : FontStyle.Normal);
            Stretch(text.rectTransform);
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        /// <summary>A small coloured dot centred with the button's label, which shifts right to make room.</summary>
        private Image AddDot(Button button)
        {
            const float size = 8f;
            const float gap = 7f;

            var label = button.transform.Find("Label").GetComponent<Text>();
            var total = size + gap + label.preferredWidth;
            label.rectTransform.offsetMin = new Vector2(size + gap, 0);

            var dot = CreateDecoration(button.transform, "Dot", Color.white);
            dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            dot.rectTransform.pivot = new Vector2(0, 0.5f);
            dot.rectTransform.anchoredPosition = new Vector2(-total / 2f, 0);
            dot.rectTransform.sizeDelta = new Vector2(size, size);
            MakeRounded(dot, (int)(size / 2), ignoreZero: true);
            return dot;
        }

        /// <summary>A border-coloured image with the fill inset inside it. Returns the border.</summary>
        private Image CreateFramed(Transform parent, string name, Color fillColor, int radius, out Image fill)
        {
            var frame = CreateImage(parent, name, Color.white);
            MakeRounded(frame, radius);

            fill = CreateImage(frame.rectTransform, "Fill", fillColor);
            Stretch(fill.rectTransform, BorderWidth, BorderWidth);
            MakeRounded(fill, radius - (int)BorderWidth);
            return frame;
        }

        private static ColorBlock FrameColors(Color normal, Color highlighted, Color pressed, Color selected)
        {
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.pressedColor = pressed;
            colors.selectedColor = selected;
            colors.disabledColor = normal;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            return colors;
        }

        /// <summary>A soft drop shadow behind <paramref name="parent"/>, extending past it on every side.</summary>
        private void CreateShadow(RectTransform parent, int radius)
        {
            var sprite = CreateRoundedSprite(radius, ShadowSoftness, "BuildtableShadow");
            var shadow = CreateDecoration(parent, "Shadow", new Color(0f, 0f, 0f, 0.45f));
            shadow.sprite = sprite;
            shadow.type = Image.Type.Sliced;
            Stretch(shadow.rectTransform, -ShadowSoftness, -ShadowSoftness);
            shadow.rectTransform.anchoredPosition = new Vector2(0, -6);
        }

        /// <summary>
        /// Rounds an image with a generated 9-slice sprite. A radius of 0 (from
        /// <see cref="BuildtableConfig.cornerRadius"/>) leaves it square unless
        /// the shape is meant to be a circle regardless.
        /// </summary>
        private void MakeRounded(Image image, int radius, bool ignoreZero = false)
        {
            if (radius <= 0 || (_config.cornerRadius <= 0 && !ignoreZero))
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
                return;
            }

            if (!_roundedSprites.TryGetValue(radius, out var sprite))
            {
                sprite = CreateRoundedSprite(radius * RoundedSpriteScale, 0, "BuildtableRounded" + radius);
                _roundedSprites[radius] = sprite;
            }

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = RoundedSpriteScale;
        }

        /// <summary>
        /// A white rounded rectangle as a 9-slice sprite. With a feather, the
        /// edge fades over that many pixels either side, for shadows; without
        /// one it gets a single pixel of antialiasing.
        /// </summary>
        private Sprite CreateRoundedSprite(int cornerPx, int featherPx, string name)
        {
            var border = cornerPx + featherPx * 2;
            var size = border * 2 + 2;
            var half = size / 2f;
            var inner = half - featherPx - cornerPx;

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Signed distance from the rounded rectangle's edge.
                    var qx = Mathf.Abs(x + 0.5f - half) - inner;
                    var qy = Mathf.Abs(y + 0.5f - half) - inner;
                    var outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    var distance = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - cornerPx;

                    var alpha = featherPx > 0
                        ? 1f - Mathf.SmoothStep(0f, 1f, (distance + featherPx) / (2f * featherPx))
                        : Mathf.Clamp01(0.5f - distance);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            var texture = CreateTexture(size, name, pixels);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.name = name;
            _generatedAssets.Add(sprite);
            return sprite;
        }

        private Sprite CreateCheckSprite()
        {
            const int size = 32;
            const float halfStroke = 1.8f;
            var a = new Vector2(8f, 16.5f);
            var b = new Vector2(13.5f, 11f);
            var c = new Vector2(24f, 21.5f);

            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var distance = Mathf.Min(DistanceToSegment(p, a, b), DistanceToSegment(p, b, c));
                    var alpha = Mathf.Clamp01(halfStroke + 0.5f - distance);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            var texture = CreateTexture(size, "BuildtableCheck", pixels);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            sprite.name = "BuildtableCheck";
            _generatedAssets.Add(sprite);
            return sprite;
        }

        private Texture2D CreateTexture(int size, string name, Color32[] pixels)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            _generatedAssets.Add(texture);
            return texture;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        private Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>An image that is never clicked: dividers, borders, shadows, dots.</summary>
        private Image CreateDecoration(Transform parent, string name, Color color)
        {
            var image = CreateImage(parent, name, color);
            image.raycastTarget = false;
            return image;
        }

        private Text CreateText(Transform parent, string name, string value, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Color White(float alpha) => new Color(1f, 1f, 1f, alpha);

        /// <summary>Positions a row <paramref name="y"/> pixels below the top of its parent.</summary>
        private static void Place(RectTransform rect, float y, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -y);
            rect.sizeDelta = new Vector2(0, height);
        }

        private static void Stretch(RectTransform rect, float padX = 0, float padY = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padX, padY);
            rect.offsetMax = new Vector2(-padX, -padY);
        }

        private static void HalfWidth(RectTransform rect, int column)
        {
            rect.anchorMin = new Vector2(column * 0.5f, 0);
            rect.anchorMax = new Vector2(column * 0.5f + 0.5f, 1);
            rect.offsetMin = new Vector2(column == 0 ? 0 : 8, 0);
            rect.offsetMax = new Vector2(column == 0 ? -8 : 0, 0);
        }

        private static void AnchorRight(RectTransform rect, float width, float offset)
        {
            rect.anchorMin = new Vector2(1, 0);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 0.5f);
            rect.sizeDelta = new Vector2(width, 0);
            rect.anchoredPosition = new Vector2(-offset, 0);
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem));
            DontDestroyOnLoad(go);

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            // StandaloneInputModule reads UnityEngine.Input, which throws when
            // the legacy manager is off — use the package's module instead.
            var moduleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType != null) go.AddComponent(moduleType);
            else Debug.LogWarning("[Buildtable] No usable input module found; the report form may not accept clicks.");
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        /// <summary>
        /// Unity renamed the built-in font from Arial.ttf to LegacyRuntime.ttf
        /// in 2022.2. Asking for the wrong one logs a console error rather than
        /// returning null quietly, so pick by version first and only fall back
        /// if that misses.
        /// </summary>
        private static Font LoadDefaultFont()
        {
            var legacyName = IsAtLeast2022_2() ? "LegacyRuntime.ttf" : "Arial.ttf";
            var font = Resources.GetBuiltinResource<Font>(legacyName);

            if (font == null)
            {
                var fallback = legacyName == "Arial.ttf" ? "LegacyRuntime.ttf" : "Arial.ttf";
                font = Resources.GetBuiltinResource<Font>(fallback);
            }

            if (font == null) Debug.LogWarning("[Buildtable] No built-in font available; report form text may not render.");
            return font;
        }

        private static bool IsAtLeast2022_2()
        {
            var parts = Application.unityVersion.Split('.');
            if (parts.Length < 2) return false;
            if (!int.TryParse(parts[0], out var major)) return false;
            if (!int.TryParse(parts[1], out var minor)) return false;
            return major > 2022 || (major == 2022 && minor >= 2);
        }

        private static string Capitalize(string value) =>
            string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);
    }
}
