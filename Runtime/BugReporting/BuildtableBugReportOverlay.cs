using System;
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
    /// </summary>
    public class BuildtableBugReportOverlay : MonoBehaviour
    {
        private static readonly Color PanelColor = new Color(0.09f, 0.09f, 0.11f, 0.98f);
        private static readonly Color ScrimColor = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color FieldColor = new Color(0.16f, 0.16f, 0.19f, 1f);
        private static readonly Color TextColor = new Color(0.92f, 0.92f, 0.94f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.62f, 0.68f, 1f);
        private static readonly Color AccentColor = new Color(0.36f, 0.45f, 0.95f, 1f);

        private static readonly string[] Priorities = { "critical", "high", "medium", "low" };

        private BuildtableBugReporter _reporter;
        private Font _font;

        private GameObject _root;
        private InputField _titleField;
        private InputField _descriptionField;
        private InputField _stepsField;
        private InputField _nameField;
        private InputField _emailField;
        private Toggle _includeScreenshotToggle;
        private Text _statusText;
        private Button _submitButton;
        private readonly Button[] _priorityButtons = new Button[4];

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
            SetStatus("", MutedColor);

            _titleField.text = "";
            _descriptionField.text = "";
            _stepsField.text = "";
            _nameField.text = BuildtableInstall.ReporterName;
            _emailField.text = BuildtableInstall.ReporterEmail;
            SetPriority("medium");
            _submitButton.interactable = true;

            ShowPreview(screenshot);

            var pending = _reporter.PendingReportCount;
            if (pending > 0) SetStatus($"{pending} earlier report(s) still waiting to send.", MutedColor);

            _root.SetActive(true);

            if (_reporter.Config.pauseWhileOpen)
            {
                _previousTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }

            if (_reporter.Config.releaseCursorWhileOpen)
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

            if (_reporter.Config.pauseWhileOpen) Time.timeScale = _previousTimeScale;
            if (_reporter.Config.releaseCursorWhileOpen)
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
                StepsToRepro = _stepsField.text,
                Priority = _priority,
                ReporterName = _nameField.text,
                ReporterEmail = _emailField.text,
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
            // Above typical game HUDs, below Unity's own debug overlays.
            canvas.sortingOrder = 30000;

            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();

            var scrim = CreateImage(_root.transform, "Scrim", ScrimColor);
            Stretch(scrim.rectTransform);

            var panel = CreateImage(_root.transform, "Panel", PanelColor);
            var panelRect = panel.rectTransform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(760, 700);

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(panelRect, false);
            Stretch(content, 24);

            // Simple top-down cursor layout: predictable, and avoids the layout
            // group / content-size-fitter interplay that tends to fight nested
            // multiline input fields.
            var y = 0f;

            var heading = CreateText(content, "Heading", "Report a bug", 26, TextColor, FontStyle.Bold);
            Place(heading.rectTransform, y, 34);
            y += 34 + 4;

            var subheading = CreateText(content, "Subheading", "Sent to your team's Buildtable board.", 15, MutedColor);
            Place(subheading.rectTransform, y, 22);
            y += 22 + 14;

            // Screenshot row — collapses entirely when there is nothing to show.
            _previewRow = new GameObject("PreviewRow", typeof(RectTransform)).GetComponent<RectTransform>();
            _previewRow.SetParent(content, false);
            Place(_previewRow, y, 116);

            _previewImage = CreateImage(_previewRow, "Preview", Color.white);
            _previewImage.rectTransform.anchorMin = new Vector2(0, 0);
            _previewImage.rectTransform.anchorMax = new Vector2(0, 1);
            _previewImage.rectTransform.pivot = new Vector2(0, 0.5f);
            _previewImage.rectTransform.anchoredPosition = new Vector2(0, 0);
            _previewImage.rectTransform.sizeDelta = new Vector2(206, 0);
            _previewImage.preserveAspect = true;

            _includeScreenshotToggle = CreateToggle(_previewRow, "IncludeScreenshot", "Include this screenshot", 220);
            y += 116 + 12;

            _titleField = AddLabeledField(content, ref y, "Title *", 36, false, "Short summary of the problem");
            _descriptionField = AddLabeledField(content, ref y, "What happened?", 92, true, "What you saw, and what you expected");
            _stepsField = AddLabeledField(content, ref y, "Steps to reproduce", 74, true, "1. …\n2. …");

            var priorityLabel = CreateText(content, "PriorityLabel", "Priority", 15, MutedColor);
            Place(priorityLabel.rectTransform, y, 20);
            y += 20 + 4;

            var priorityRow = new GameObject("PriorityRow", typeof(RectTransform)).GetComponent<RectTransform>();
            priorityRow.SetParent(content, false);
            Place(priorityRow, y, 34);
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
            y += 34 + 14;

            // Reporter identity, remembered between reports.
            var identityRow = new GameObject("IdentityRow", typeof(RectTransform)).GetComponent<RectTransform>();
            identityRow.SetParent(content, false);
            Place(identityRow, y, 36);
            _nameField = CreateInputField(identityRow, "Name", "Your name", false);
            HalfWidth(_nameField.GetComponent<RectTransform>(), 0);
            _emailField = CreateInputField(identityRow, "Email", "Your email", false);
            HalfWidth(_emailField.GetComponent<RectTransform>(), 1);
            _emailField.contentType = InputField.ContentType.EmailAddress;
            y += 36 + 14;

            _statusText = CreateText(content, "Status", "", 14, MutedColor);
            Place(_statusText.rectTransform, y, 20);

            // Actions pinned to the bottom of the panel rather than the cursor,
            // so they stay put regardless of what the rows above did.
            var actions = new GameObject("Actions", typeof(RectTransform)).GetComponent<RectTransform>();
            actions.SetParent(content, false);
            actions.anchorMin = new Vector2(0, 0);
            actions.anchorMax = new Vector2(1, 0);
            actions.pivot = new Vector2(0.5f, 0);
            actions.anchoredPosition = Vector2.zero;
            actions.sizeDelta = new Vector2(0, 40);

            var cancel = CreateButton(actions, "Cancel", "Cancel (Esc)", Close);
            AnchorRight(cancel.GetComponent<RectTransform>(), 150, 158);

            _submitButton = CreateButton(actions, "Submit", "Send report", Submit, AccentColor);
            AnchorRight(_submitButton.GetComponent<RectTransform>(), 150, 0);

            _root.SetActive(false);
        }

        private RectTransform _previewRow;
        private Image _previewImage;

        private void ShowPreview(byte[] screenshot)
        {
            ReleasePreview();

            var hasScreenshot = screenshot != null && screenshot.Length > 0;
            _previewRow.gameObject.SetActive(hasScreenshot);
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
                image.color = Priorities[i] == priority ? AccentColor : FieldColor;
            }
        }

        private void SetStatus(string message, Color color)
        {
            _statusText.text = message;
            _statusText.color = color;
        }

        // ------------------------------------------------------ UI helpers

        private InputField AddLabeledField(RectTransform parent, ref float y, string label, float height, bool multiline, string placeholder)
        {
            var labelText = CreateText(parent, label + "Label", label, 15, MutedColor);
            Place(labelText.rectTransform, y, 20);
            y += 20 + 4;

            var field = CreateInputField(parent, label + "Field", placeholder, multiline);
            Place(field.GetComponent<RectTransform>(), y, height);
            y += height + 12;
            return field;
        }

        private InputField CreateInputField(RectTransform parent, string name, string placeholder, bool multiline)
        {
            var background = CreateImage(parent, name, FieldColor);
            var field = background.gameObject.AddComponent<InputField>();

            var textComponent = CreateText(background.rectTransform, "Text", "", 16, TextColor);
            Stretch(textComponent.rectTransform, 10, 6);
            textComponent.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            textComponent.supportRichText = false;

            var placeholderText = CreateText(background.rectTransform, "Placeholder", placeholder, 16, MutedColor);
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

            var box = CreateImage(rect, "Box", FieldColor);
            box.rectTransform.anchorMin = new Vector2(0, 0.5f);
            box.rectTransform.anchorMax = new Vector2(0, 0.5f);
            box.rectTransform.pivot = new Vector2(0, 0.5f);
            box.rectTransform.sizeDelta = new Vector2(22, 22);

            var check = CreateImage(box.rectTransform, "Check", AccentColor);
            Stretch(check.rectTransform, 4, 4);

            var text = CreateText(rect, "Label", label, 15, TextColor);
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
            var image = CreateImage(parent, name, color ?? FieldColor);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick());

            var text = CreateText(image.rectTransform, "Label", label, 16, TextColor);
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
