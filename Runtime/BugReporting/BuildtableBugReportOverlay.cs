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
    ///
    /// Colours, copy, and which fields appear all come from
    /// <see cref="BuildtableConfig"/>, so a project can make the form look like
    /// its own UI without forking this file.
    /// </summary>
    public class BuildtableBugReportOverlay : MonoBehaviour
    {
        private const float ContentPadding = 24f;
        private const float ActionsHeight = 40f;
        private const float ActionsSpacing = 16f;

        private static readonly string[] Priorities = { "critical", "high", "medium", "low" };

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
        private Button[] _priorityButtons = Array.Empty<Button>();

        private byte[] _screenshot;
        private Texture2D _previewTexture;
        private string _priority = "medium";

        private float _previousTimeScale = 1f;
        private CursorLockMode _previousCursorLock;
        private bool _previousCursorVisible;

        public bool IsOpen => _root != null && _root.activeSelf;

        public static BuildtableBugReportOverlay Create(BuildtableBugReporter reporter)
        {
            var go = new GameObject("BuildtableBugReportOverlay");
            go.transform.SetParent(reporter.transform, false);
            var overlay = go.AddComponent<BuildtableBugReportOverlay>();
            overlay._reporter = reporter;
            overlay._config = reporter.Config;
            overlay.Build();
            return overlay;
        }

        private void Update()
        {
            if (IsOpen && BuildtableInput.GetKeyDown(KeyCode.Escape)) Close();
        }

        public void Open(byte[] screenshot)
        {
            _screenshot = screenshot;
            SetStatus("", _config.mutedTextColor);

            _titleField.text = "";
            _descriptionField.text = "";
            if (_stepsField != null) _stepsField.text = "";
            if (_nameField != null) _nameField.text = BuildtableInstall.ReporterName;
            if (_emailField != null) _emailField.text = BuildtableInstall.ReporterEmail;
            SetPriority("medium");
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

        private void OnDestroy() => ReleasePreview();

        private void Submit()
        {
            if (string.IsNullOrWhiteSpace(_titleField.text))
            {
                SetStatus("A title is required.", new Color(1f, 0.5f, 0.45f));
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
            _reporter.Submit(draft);

            // Submit() persists to disk before any network call, so the report is
            // safe the moment it returns — close rather than make the tester wait
            // on a request that may be retried for minutes.
            Close();
        }

        // ---------------------------------------------------------------- UI

        private void Build()
        {
            _font = LoadDefaultFont();

            _root = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _root.transform.SetParent(transform, false);

            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = _config.sortingOrder;

            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();

            var scrim = CreateImage(_root.transform, "Scrim", new Color(0f, 0f, 0f, _config.scrimOpacity));
            Stretch(scrim.rectTransform);

            var panel = CreateImage(_root.transform, "Panel", _config.panelColor);
            _panelRect = panel.rectTransform;
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(_panelRect, false);
            Stretch(content, ContentPadding);

            var heading = CreateText(content, "Heading", _config.formTitle, 26, _config.textColor, FontStyle.Bold);
            AddRow(heading.rectTransform, 34, 4);

            if (!string.IsNullOrEmpty(_config.formSubtitle))
            {
                var subheading = CreateText(content, "Subheading", _config.formSubtitle, 15, _config.mutedTextColor);
                AddRow(subheading.rectTransform, 22, 14);
            }

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

            _titleField = AddLabeledField(content, "Title *", 36, false, "Short summary of the problem");
            _descriptionField = AddLabeledField(content, "What happened?", 92, true, "What you saw, and what you expected");

            if (_config.showStepsToRepro)
            {
                _stepsField = AddLabeledField(content, "Steps to reproduce", 74, true, "1. …\n2. …");
            }

            if (_config.showPriority)
            {
                var priorityLabel = CreateText(content, "PriorityLabel", "Priority", 15, _config.mutedTextColor);
                AddRow(priorityLabel.rectTransform, 20, 4);

                var priorityRow = new GameObject("PriorityRow", typeof(RectTransform)).GetComponent<RectTransform>();
                priorityRow.SetParent(content, false);
                AddRow(priorityRow, 34, 14);

                _priorityButtons = new Button[Priorities.Length];
                for (var i = 0; i < Priorities.Length; i++)
                {
                    var priority = Priorities[i];
                    var button = CreateButton(priorityRow, priority, Capitalize(priority), () => SetPriority(priority));
                    var rect = button.GetComponent<RectTransform>();
                    rect.anchorMin = new Vector2(0, 0);
                    rect.anchorMax = new Vector2(0, 1);
                    rect.pivot = new Vector2(0, 0.5f);
                    rect.sizeDelta = new Vector2(120, 0);
                    rect.anchoredPosition = new Vector2(i * 126, 0);
                    _priorityButtons[i] = button;
                }
            }

            if (_config.showReporterIdentity)
            {
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
            var actions = new GameObject("Actions", typeof(RectTransform)).GetComponent<RectTransform>();
            actions.SetParent(content, false);
            actions.anchorMin = new Vector2(0, 0);
            actions.anchorMax = new Vector2(1, 0);
            actions.pivot = new Vector2(0.5f, 0);
            actions.anchoredPosition = Vector2.zero;
            actions.sizeDelta = new Vector2(0, ActionsHeight);

            var cancel = CreateButton(actions, "Cancel", "Cancel (Esc)", Close);
            AnchorRight(cancel.GetComponent<RectTransform>(), 150, 158);

            _submitButton = CreateButton(actions, "Submit", _config.submitButtonLabel, Submit, _config.accentColor);
            AnchorRight(_submitButton.GetComponent<RectTransform>(), 150, 0);

            ApplyLayout(false);
            _root.SetActive(false);
        }

        private void AddRow(RectTransform rect, float height, float spacingAfter, bool previewOnly = false)
        {
            _rows.Add(new Row { Rect = rect, Height = height, SpacingAfter = spacingAfter, PreviewOnly = previewOnly });
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

        private void SetPriority(string priority)
        {
            _priority = priority;
            for (var i = 0; i < _priorityButtons.Length; i++)
            {
                var image = _priorityButtons[i].GetComponent<Image>();
                image.color = Priorities[i] == priority ? _config.accentColor : _config.fieldColor;
            }
        }

        private void SetStatus(string message, Color color)
        {
            _statusText.text = message;
            _statusText.color = color;
        }

        // ------------------------------------------------------ UI helpers

        private InputField AddLabeledField(RectTransform parent, string label, float height, bool multiline, string placeholder)
        {
            var labelText = CreateText(parent, label + "Label", label, 15, _config.mutedTextColor);
            AddRow(labelText.rectTransform, 20, 4);

            var field = CreateInputField(parent, label + "Field", placeholder, multiline);
            AddRow(field.GetComponent<RectTransform>(), height, 12);
            return field;
        }

        private InputField CreateInputField(RectTransform parent, string name, string placeholder, bool multiline)
        {
            var background = CreateImage(parent, name, _config.fieldColor);
            var field = background.gameObject.AddComponent<InputField>();

            var textComponent = CreateText(background.rectTransform, "Text", "", 16, _config.textColor);
            Stretch(textComponent.rectTransform, 10, 6);
            textComponent.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            textComponent.supportRichText = false;

            var placeholderText = CreateText(background.rectTransform, "Placeholder", placeholder, 16, _config.mutedTextColor);
            Stretch(placeholderText.rectTransform, 10, 6);
            placeholderText.alignment = textComponent.alignment;
            placeholderText.fontStyle = FontStyle.Italic;

            field.textComponent = textComponent;
            field.placeholder = placeholderText;
            field.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            // Matches the server's own limits so a tester is never silently
            // truncated after the fact.
            field.characterLimit = multiline ? 10000 : 500;
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

            var check = CreateImage(box.rectTransform, "Check", _config.accentColor);
            Stretch(check.rectTransform, 4, 4);

            var text = CreateText(rect, "Label", label, 15, _config.textColor);
            text.rectTransform.anchorMin = new Vector2(0, 0);
            text.rectTransform.anchorMax = new Vector2(1, 1);
            text.rectTransform.offsetMin = new Vector2(30, 0);
            text.rectTransform.offsetMax = Vector2.zero;
            text.alignment = TextAnchor.MiddleLeft;

            toggle.targetGraphic = box;
            toggle.graphic = check;
            toggle.isOn = true;
            return toggle;
        }

        private static void SetToggleLabel(Toggle toggle, string label)
        {
            var text = toggle.transform.Find("Label")?.GetComponent<Text>();
            if (text != null) text.text = label;
        }

        private Button CreateButton(RectTransform parent, string name, string label, Action onClick, Color? color = null)
        {
            var image = CreateImage(parent, name, color ?? _config.fieldColor);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            var text = CreateText(image.rectTransform, "Label", label, 16, _config.textColor);
            Stretch(text.rectTransform);
            text.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
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
