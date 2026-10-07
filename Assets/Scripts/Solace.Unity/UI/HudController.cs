// Solace.Unity — the heads-up display, built in code.
//
// Design: a nature-documentary overlay, not a dashboard. All chrome lives in
// a fade group that dissolves to nearly invisible after a few idle seconds and
// blooms back the moment the mouse moves — watching should feel like looking
// through a window. Needs are delicate glowing rings, the compass is a real
// rotating dial with markers for discovered places, and text is shadowed for
// readability over bright scenes.
//
// Layout (1920×1080 reference): top-left SOLACE card, top-center compass dial,
// right AI STATUS, bottom-left need orbs, bottom-center hotbar, bottom-right
// hints. Panels (chat/journal/inventory/map) toggle with H/J/I/M; the sim
// keeps running while they're open. Dynamic text refreshes at 4Hz with
// change-detection to avoid per-frame allocations.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Solace.Core;

namespace Solace.Unity
{
    public class HudController : MonoBehaviour
    {
        private GameBootstrap _boot;
        private Canvas _canvas;

        // Idle-fade chrome.
        private CanvasGroup _chromeFade;
        private float _idleTime;
        private Vector3 _lastMouse;
        private bool _mouseInit;

        // Widgets.
        private Text _titleInfo;
        private RectTransform _rose;
        private Text _heading;
        private readonly List<Image> _poiMarks = new List<Image>();
        private float _smoothYaw;
        private Text _aiName;
        private Text _aiGen;
        private Text _aiLight;
        private Image _glowDot;
        private Text _aiMood;
        private Text _aiRelation;
        private Text _aiGoal;
        private Text _aiMemory;
        private Image _orbHealth;
        private Image _orbLight;
        private Image _orbHunger;
        private readonly List<Image> _orbGlows = new List<Image>();
        private float _tHealth = 1f, _tLight = 1f, _tHunger = 1f;
        private Text _hotFood;
        private Text _hotSalve;
        private Text _hotKeeps;
        private Text _toast;
        private Text _chapterEyebrow;
        private Text _chapterTitle;
        private Text _chapterSub;
        private GameObject _chapterCard;

        // Title card + whispers (opening sequence).
        private GameObject _titleCard;
        private Text _titleCardTitle;
        private Text _titleCardSub;
        private CanvasGroup _titleFade;
        private float _titlePhase; // 0=hidden, 1=fading in, 2=holding, 3=fading out
        private float _titleT;
        private Text _whisper;
        private CanvasGroup _whisperFade;
        private float _whisperUntil = -1f;
        private float _whisperDuration = 3f;

        // Panels.
        private ChatPanel _chat;
        private JournalPanel _journal;
        private InventoryPanel _inventory;
        private MapPanel _map;
        private SettingsPanel _settings;
        private LineagePanel _lineage;
        private CodexPanel _codex;

        // Accessibility: audio captions.
        private Text _caption;
        private float _captionUntil = -1f;

        private float _toastUntil = -1f;
        private float _chapterUntil = -1f;
        private float _refreshT;
        private readonly StringBuilder _sb = new StringBuilder(1024);

        // Change-detection cache.
        private string _cInfo = "", _cCompass = "", _cName = "", _cGen = "",
                       _cLight = "", _cMood = "", _cRel = "", _cGoal = "", _cMem = "",
                       _cFood = "", _cSalve = "", _cKeeps = "";
        private float _cHp = -1f, _cLi = -1f, _cHu = -1f;

        /// <summary>True while the player is typing in a field (guards hotkeys).</summary>
        public bool IsTyping
        {
            get
            {
                return _chat != null && _chat.IsOpen && _chat.IsTyping;
            }
        }

        /// <summary>Canvas transform, for HUD-attached widgets (bug button).</summary>
        public Transform CanvasRoot { get { return _canvas.transform; } }

        public void Build(GameBootstrap boot)
        {
            _boot = boot;
            _canvas = UiKit.CreateCanvas("HUD", 10);
            _canvas.transform.SetParent(transform, false);

            // Chrome: everything ambient fades together on idle.
            var chrome = new GameObject("Chrome");
            chrome.transform.SetParent(_canvas.transform, false);
            var crt = chrome.AddComponent<RectTransform>();
            crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
            crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
            _chromeFade = chrome.AddComponent<CanvasGroup>();

            BuildTitleCard(chrome.transform);
            BuildCompass(chrome.transform);
            BuildAiStatus(chrome.transform);
            BuildOrbs(chrome.transform);
            BuildHotbar(chrome.transform);
            BuildHints(chrome.transform);

            // Transient moments live outside the fade group.
            BuildToast();
            BuildChapterCard();
            BuildTitleCardOverlay();
            BuildWhisper();
            BuildAudioCaption();
            BuildPanels();

            ApplyUiScale();
            AmbientAudio.OnAudioCue += ShowAudioCaption;

            gameObject.AddComponent<BugReportTool>();
        }

        // -- construction --------------------------------------------------------

        private void BuildTitleCard(Transform parent)
        {
            var card = UiKit.Panel(parent, "TitleCard", UiKit.WhisperPanel,
                0f, 1f, 0f, 1f, 16f, -110f, 384f, -16f);
            var title = UiKit.Label(card, "Title", "SOLACE", 30, UiKit.Gold, TextAnchor.UpperLeft);
            UiKit.AddShadow(title);
            title.GetComponent<RectTransform>().offsetMin = new Vector2(14f, -46f);
            var sub = UiKit.Label(card, "Sub", "Find purpose in a world that's yours to explore.",
                                  13, UiKit.DimInk, TextAnchor.UpperLeft);
            sub.fontStyle = FontStyle.Italic;
            UiKit.AddShadow(sub, 0.4f);
            sub.GetComponent<RectTransform>().offsetMin = new Vector2(14f, -70f);
            _titleInfo = UiKit.Label(card, "Info", "", 13, UiKit.Ink, TextAnchor.UpperLeft);
            UiKit.AddShadow(_titleInfo, 0.4f);
            _titleInfo.GetComponent<RectTransform>().offsetMin = new Vector2(14f, -94f);
        }

        private void BuildCompass(Transform parent)
        {
            var root = UiKit.Rect(parent, "Compass", 0.5f, 1f, 0.5f, 1f, -90f, -164f, 90f, -10f);

            // Dial ring.
            var dial = UiKit.Rect(root, "Dial", 0.5f, 1f, 0.5f, 1f, -60f, -134f, 60f, -14f);
            var ring = dial.gameObject.AddComponent<Image>();
            ring.sprite = UiKit.RingSprite();
            ring.color = new Color(0.95f, 0.85f, 0.60f, 0.45f);
            ring.raycastTarget = false;

            // Rotating compass card.
            _rose = UiKit.Rect(root, "Rose", 0.5f, 1f, 0.5f, 1f, -60f, -134f, 60f, -14f);
            string[] letters = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                bool cardinal = i % 2 == 0;
                var t = UiKit.Label(_rose, "C" + i, letters[i], cardinal ? 17 : 11,
                                    cardinal ? UiKit.Ink : UiKit.DimInk, TextAnchor.MiddleCenter);
                var trt = t.GetComponent<RectTransform>();
                trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
                trt.anchoredPosition = new Vector2(Mathf.Sin(a) * 42f, Mathf.Cos(a) * 42f);
                trt.sizeDelta = new Vector2(44f, 24f);
                UiKit.AddShadow(t, 0.5f);
            }

            // Discovered-place markers (pooled).
            for (int i = 0; i < 24; i++)
            {
                var mrt = UiKit.Rect(_rose, "Poi" + i, 0.5f, 0.5f, 0.5f, 0.5f, -5f, -5f, 5f, 5f);
                var img = mrt.gameObject.AddComponent<Image>();
                img.sprite = UiKit.DotSprite();
                img.color = new Color(0.95f, 0.75f, 0.35f, 0.95f);
                img.raycastTarget = false;
                img.gameObject.SetActive(false);
                _poiMarks.Add(img);
            }

            // Fixed needle at the top of the dial.
            var needle = UiKit.Rect(root, "Needle", 0.5f, 1f, 0.5f, 1f, -1.5f, -36f, 1.5f, -16f);
            var nimg = needle.gameObject.AddComponent<Image>();
            nimg.color = UiKit.Gold;
            nimg.raycastTarget = false;

            // Heading readout beneath the dial.
            _heading = UiKit.Label(root, "Heading", "", 13, UiKit.DimInk, TextAnchor.MiddleCenter);
            var hrt = _heading.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0f, 1f); hrt.anchorMax = new Vector2(1f, 1f);
            hrt.offsetMin = new Vector2(0f, -158f); hrt.offsetMax = new Vector2(0f, -134f);
            UiKit.AddShadow(_heading, 0.4f);
        }

        private void BuildAiStatus(Transform parent)
        {
            var card = UiKit.Panel(parent, "AIStatus", UiKit.GlassPanel,
                1f, 1f, 1f, 1f, -336f, -428f, -16f, -16f);
            _aiName = UiKit.Label(card, "Name", "", 22, UiKit.Ink, TextAnchor.UpperLeft);
            UiKit.AddShadow(_aiName);
            _aiName.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -58f);
            _aiGen = UiKit.Label(card, "Gen", "", 13, UiKit.Gold, TextAnchor.UpperLeft);
            UiKit.AddShadow(_aiGen, 0.4f);
            _aiGen.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -86f);

            // Light row: label + glow dot.
            var lightRow = UiKit.Rect(card, "LightRow", 0f, 1f, 1f, 1f, 12f, -116f, -12f, -90f);
            _aiLight = UiKit.Label(lightRow, "Light", "", 14, UiKit.Ink, TextAnchor.MiddleLeft);
            UiKit.AddShadow(_aiLight, 0.4f);
            var dotRt = UiKit.Rect(lightRow, "GlowDot", 1f, 0.5f, 1f, 0.5f, -30f, -9f, -12f, 9f);
            _glowDot = dotRt.gameObject.AddComponent<Image>();
            _glowDot.sprite = UiKit.DotSprite();
            _glowDot.raycastTarget = false;

            _aiMood = UiKit.Label(card, "Mood", "", 14, UiKit.Ink, TextAnchor.UpperLeft);
            UiKit.AddShadow(_aiMood, 0.4f);
            _aiMood.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -144f);
            _aiRelation = UiKit.Label(card, "Rel", "", 14, UiKit.Ink, TextAnchor.UpperLeft);
            UiKit.AddShadow(_aiRelation, 0.4f);
            _aiRelation.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -170f);
            _aiGoal = UiKit.Label(card, "Goal", "", 14, UiKit.Ink, TextAnchor.UpperLeft);
            UiKit.AddShadow(_aiGoal, 0.4f);
            _aiGoal.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -196f);
            _aiGoal.verticalOverflow = VerticalWrapMode.Overflow;

            UiKit.Divider(card, "MemRule", new Color(0.72f, 0.55f, 0.28f, 0.35f),
                0f, 1f, 1f, 1f, 12f, -252f, -12f, -251f);
            var memHead = UiKit.Label(card, "MemHead", UiKit.Spaced("MEMORY"), 11, UiKit.GoldDim, TextAnchor.UpperLeft);
            memHead.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -276f);
            _aiMemory = UiKit.Label(card, "Mem", "", 13, UiKit.DimInk, TextAnchor.UpperLeft);
            _aiMemory.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -408f);
            _aiMemory.verticalOverflow = VerticalWrapMode.Overflow;
            _aiMemory.lineSpacing = 1.25f;
        }

        private Image MakeOrb(Transform parent, string name, string label, Color color, float x)
        {
            var cell = UiKit.Rect(parent, name, 0f, 0f, 0f, 0f, x, 0f, x + 72f, 100f);
            // Soft breathing glow behind the ring.
            var glowRt = UiKit.Rect(cell, "Glow", 0.5f, 0.5f, 0.5f, 0.5f, -34f, -34f, 34f, 34f);
            var glow = glowRt.gameObject.AddComponent<Image>();
            glow.sprite = UiKit.SoftSprite();
            glow.color = new Color(color.r, color.g, color.b, 0.30f);
            glow.raycastTarget = false;
            _orbGlows.Add(glow);
            // Dark track ring.
            var trackRt = UiKit.Rect(cell, "Track", 0.5f, 0.5f, 0.5f, 0.5f, -27f, -27f, 27f, 27f);
            var track = trackRt.gameObject.AddComponent<Image>();
            track.sprite = UiKit.RingSprite();
            track.color = new Color(0f, 0f, 0f, 0.45f);
            track.raycastTarget = false;
            // Value ring (radial fill).
            var fillRt = UiKit.Rect(cell, "Fill", 0.5f, 0.5f, 0.5f, 0.5f, -27f, -27f, 27f, 27f);
            var fill = fillRt.gameObject.AddComponent<Image>();
            fill.sprite = UiKit.RingSprite();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 1f;
            fill.color = color;
            fill.raycastTarget = false;
            // Caption.
            var lab = UiKit.Label(cell, "Lab", UiKit.Spaced(label), 10, UiKit.DimInk, TextAnchor.LowerCenter);
            UiKit.AddShadow(lab, 0.5f);
            return fill;
        }

        private void BuildOrbs(Transform parent)
        {
            var row = UiKit.Rect(parent, "Orbs", 0f, 0f, 0f, 0f, 20f, 16f, 260f, 116f);
            _orbHealth = MakeOrb(row, "Health", "HEALTH", new Color(0.88f, 0.36f, 0.30f), 0f);
            _orbLight = MakeOrb(row, "Light", "LIGHT", new Color(1.00f, 0.80f, 0.42f), 80f);
            _orbHunger = MakeOrb(row, "Full", "FULLNESS", new Color(0.95f, 0.66f, 0.30f), 160f);
        }

        private void BuildHotbar(Transform parent)
        {
            _hotFood = UiKit.Label(parent, "Food", "", 13, UiKit.DimInk, TextAnchor.MiddleCenter);
            var r1 = _hotFood.GetComponent<RectTransform>();
            r1.anchorMin = new Vector2(0.5f, 0f); r1.anchorMax = new Vector2(0.5f, 0f);
            r1.offsetMin = new Vector2(-280f, 18f); r1.offsetMax = new Vector2(-94f, 46f);
            UiKit.AddShadow(_hotFood, 0.5f);
            _hotSalve = UiKit.Label(parent, "Salve", "", 13, UiKit.DimInk, TextAnchor.MiddleCenter);
            var r2 = _hotSalve.GetComponent<RectTransform>();
            r2.anchorMin = new Vector2(0.5f, 0f); r2.anchorMax = new Vector2(0.5f, 0f);
            r2.offsetMin = new Vector2(-93f, 18f); r2.offsetMax = new Vector2(93f, 46f);
            UiKit.AddShadow(_hotSalve, 0.5f);
            _hotKeeps = UiKit.Label(parent, "Keeps", "", 13, UiKit.DimInk, TextAnchor.MiddleCenter);
            var r3 = _hotKeeps.GetComponent<RectTransform>();
            r3.anchorMin = new Vector2(0.5f, 0f); r3.anchorMax = new Vector2(0.5f, 0f);
            r3.offsetMin = new Vector2(94f, 18f); r3.offsetMax = new Vector2(280f, 46f);
            UiKit.AddShadow(_hotKeeps, 0.5f);
        }

        private void BuildHints(Transform parent)
        {
            var hints = UiKit.Label(parent, "Hints",
                "H chat · J journal · I inventory · M map · L lineage · X codex · O options · N new life · F12 report",
                12, UiKit.DimInk, TextAnchor.LowerRight);
            UiKit.AddShadow(hints, 0.5f);
            var rt = hints.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 0f);
            rt.offsetMin = new Vector2(-540f, 16f); rt.offsetMax = new Vector2(-16f, 44f);
        }

        private void BuildToast()
        {
            _toast = UiKit.Label(_canvas.transform, "Toast", "", 18, UiKit.Ink, TextAnchor.UpperCenter);
            UiKit.AddShadow(_toast);
            var rt = _toast.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(-420f, -150f); rt.offsetMax = new Vector2(420f, -70f);
            _toast.gameObject.SetActive(false);
        }

        /// <summary>Applies the accessibility UI scale to the HUD canvas.</summary>
        public void ApplyUiScale()
        {
            if (_canvas == null) return;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.scaleFactor = AccessibilitySettings.UiScale;
        }

        // -- accessibility: audio captions -------------------------------------------

        private void BuildAudioCaption()
        {
            _caption = UiKit.Label(_canvas.transform, "AudioCaption", "", 14,
                new Color(1f, 1f, 1f, 0.9f), TextAnchor.LowerRight);
            UiKit.AddShadow(_caption, 0.5f);
            var rt = _caption.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 0f);
            rt.offsetMin = new Vector2(-360f, 52f); rt.offsetMax = new Vector2(-16f, 76f);
            _caption.canvasRenderer.SetAlpha(0f);
        }

        /// <summary>Shows a brief visual caption for an audio cue (accessibility).</summary>
        public void ShowAudioCaption(string text)
        {
            if (!AccessibilitySettings.AudioCaptions || _caption == null) return;
            _caption.text = text;
            _caption.canvasRenderer.SetAlpha(1f);
            _captionUntil = Time.time + 2.5f;
        }

        private void UpdateAudioCaption()
        {
            if (_caption == null) return;
            bool show = Time.time < _captionUntil;
            _caption.canvasRenderer.SetAlpha(show ? 1f : 0f);
        }

        private void BuildChapterCard()
        {
            _chapterCard = new GameObject("ChapterCard");
            _chapterCard.transform.SetParent(_canvas.transform, false);
            var rt = _chapterCard.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-500f, -110f); rt.offsetMax = new Vector2(500f, 110f);
            _chapterEyebrow = UiKit.Label(_chapterCard.transform, "CEye", "", 17, UiKit.GoldDim, TextAnchor.MiddleCenter);
            UiKit.AddShadow(_chapterEyebrow, 0.4f);
            var ert = _chapterEyebrow.GetComponent<RectTransform>();
            ert.offsetMin = new Vector2(0f, 66f); ert.offsetMax = new Vector2(0f, 100f);
            _chapterTitle = UiKit.Label(_chapterCard.transform, "CTitle", "", 54, UiKit.Gold, TextAnchor.MiddleCenter);
            UiKit.AddShadow(_chapterTitle);
            _chapterSub = UiKit.Label(_chapterCard.transform, "CSub", "", 26, UiKit.Ink, TextAnchor.MiddleCenter);
            UiKit.AddShadow(_chapterSub, 0.5f);
            _chapterSub.GetComponent<RectTransform>().offsetMin = new Vector2(0f, -180f);
            var ruleColor = new Color(0.72f, 0.55f, 0.28f, 0.5f);
            UiKit.Divider(_chapterCard.transform, "RuleT", ruleColor, 0.5f, 0.5f, 0.5f, 0.5f, -260f, 108f, 260f, 109f);
            UiKit.Divider(_chapterCard.transform, "RuleB", ruleColor, 0.5f, 0.5f, 0.5f, 0.5f, -260f, -108f, 260f, -107f);
            _chapterCard.SetActive(false);
        }

        private void BuildTitleCardOverlay()
        {
            _titleCard = new GameObject("TitleCardOverlay");
            _titleCard.transform.SetParent(_canvas.transform, false);
            var rt = _titleCard.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-600f, -120f); rt.offsetMax = new Vector2(600f, 120f);
            _titleFade = _titleCard.AddComponent<CanvasGroup>();
            _titleFade.alpha = 0f;

            _titleCardTitle = UiKit.Label(_titleCard.transform, "TTitle", "", 84, UiKit.Gold, TextAnchor.MiddleCenter);
            UiKit.AddShadow(_titleCardTitle, 0.6f);
            var trt = _titleCardTitle.GetComponent<RectTransform>();
            trt.offsetMin = new Vector2(0f, 10f); trt.offsetMax = new Vector2(0f, 120f);
            // Letterspaced for cinematic feel.
            _titleCardSub = UiKit.Label(_titleCard.transform, "TSub", "", 20, UiKit.Ink, TextAnchor.MiddleCenter);
            _titleCardSub.fontStyle = FontStyle.Italic;
            UiKit.AddShadow(_titleCardSub, 0.5f);
            var srt = _titleCardSub.GetComponent<RectTransform>();
            srt.offsetMin = new Vector2(0f, -80f); srt.offsetMax = new Vector2(0f, -10f);

            _titleCard.SetActive(false);
            _titlePhase = 0f;
        }

        private void BuildWhisper()
        {
            _whisper = UiKit.Label(_canvas.transform, "Whisper", "", 22, UiKit.Ink, TextAnchor.LowerCenter);
            _whisper.fontStyle = FontStyle.Italic;
            UiKit.AddShadow(_whisper, 0.5f);
            var rt = _whisper.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f); rt.anchorMax = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(-400f, 120f); rt.offsetMax = new Vector2(400f, 180f);
            _whisperFade = _whisper.gameObject.AddComponent<CanvasGroup>();
            _whisperFade.alpha = 0f;
            _whisper.gameObject.SetActive(false);
        }

        private void BuildPanels()
        {
            _chat = AddPanel<ChatPanel>("ChatPanel");
            _journal = AddPanel<JournalPanel>("JournalPanel");
            _inventory = AddPanel<InventoryPanel>("InventoryPanel");
            _map = AddPanel<MapPanel>("MapPanel");
            _settings = AddPanel<SettingsPanel>("SettingsPanel");
            _lineage = AddPanel<LineagePanel>("LineagePanel");
            _codex = AddPanel<CodexPanel>("CodexPanel");
        }

        private T AddPanel<T>(string name) where T : PanelBase
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);
            var p = go.AddComponent<T>();
            p.Build(this);
            p.SetVisible(false);
            return p;
        }

        // -- runtime -----------------------------------------------------------------

        private bool AnyPanelOpen()
        {
            return (_chat != null && _chat.IsOpen) ||
                   (_journal != null && _journal.IsOpen) ||
                   (_inventory != null && _inventory.IsOpen) ||
                   (_map != null && _map.IsOpen) ||
                   (_settings != null && _settings.IsOpen) ||
                   (_lineage != null && _lineage.IsOpen) ||
                   (_codex != null && _codex.IsOpen);
        }

        /// <summary>Public for gamepad/mute gating in other systems.</summary>
        public bool IsAnyPanelOpen() { return AnyPanelOpen(); }

        private void Update()
        {
            if (_canvas == null) return;

            // Idle fade: the chrome dissolves when untouched, blooms on mouse move.
            Vector3 mp = Input.mousePosition;
            if (!_mouseInit) { _lastMouse = mp; _mouseInit = true; }
            else if ((mp - _lastMouse).sqrMagnitude > 9f) { _idleTime = 0f; _lastMouse = mp; }
            else _idleTime += Time.deltaTime;
            bool busy = AnyPanelOpen() || _idleTime < 4f;
            float target = busy ? 1f : 0.07f;
            float cur = _chromeFade.alpha;
            float rate = target > cur ? 5f : 1.6f;
            _chromeFade.alpha = Mathf.Lerp(cur, target, 1f - Mathf.Exp(-rate * Time.deltaTime));

            // Orb gauges glide toward their targets; glows breathe.
            float k = 1f - Mathf.Exp(-6f * Time.deltaTime);
            _orbHealth.fillAmount = Mathf.Lerp(_orbHealth.fillAmount, _tHealth, k);
            _orbLight.fillAmount = Mathf.Lerp(_orbLight.fillAmount, _tLight, k);
            _orbHunger.fillAmount = Mathf.Lerp(_orbHunger.fillAmount, _tHunger, k);
            for (int i = 0; i < _orbGlows.Count; i++)
            {
                var g = _orbGlows[i];
                Color c = g.color;
                float pulse = 0.24f + 0.10f * (0.5f + 0.5f * Mathf.Sin(Time.time * 2.1f + i * 2.094f));
                g.color = new Color(c.r, c.g, c.b, pulse);
            }

            // Compass card eases toward the camera heading.
            if (_boot != null && _boot.Sim != null && _boot.Camera != null)
            {
                _smoothYaw = Mathf.LerpAngle(_smoothYaw, _boot.Camera.YawDegrees,
                                            1f - Mathf.Exp(-8f * Time.deltaTime));
                _rose.rotation = Quaternion.Euler(0f, 0f, -_smoothYaw);
            }

            if (_boot == null || _boot.Sim == null) return;

            // Toast / chapter-card timers.
            if (_toast.gameObject.activeSelf && Time.time > _toastUntil)
                _toast.gameObject.SetActive(false);
            if (_chapterCard.activeSelf && Time.time > _chapterUntil)
                _chapterCard.SetActive(false);

            // Title card cinematic: 1.5s fade in, 4s hold, 2.5s fade out.
            if (_titlePhase > 0f)
            {
                _titleT += Time.deltaTime;
                float a = 0f;
                if (_titlePhase == 1f) // fading in
                {
                    a = Mathf.Clamp01(_titleT / 1.5f);
                    if (_titleT >= 1.5f) { _titlePhase = 2f; _titleT = 0f; }
                }
                else if (_titlePhase == 2f) // holding
                {
                    a = 1f;
                    if (_titleT >= 4f) { _titlePhase = 3f; _titleT = 0f; }
                }
                else if (_titlePhase == 3f) // fading out
                {
                    a = 1f - Mathf.Clamp01(_titleT / 2.5f);
                    if (_titleT >= 2.5f)
                    {
                        _titlePhase = 0f;
                        _titleCard.SetActive(false);
                    }
                }
                if (_titleFade != null) _titleFade.alpha = a;
            }

            // Whisper: fade in fast, hold, fade out.
            if (_whisper.gameObject.activeSelf)
            {
                float remaining = _whisperUntil - Time.time;
                float whisperTarget = 0f;
                if (remaining > 0f)
                {
                    float elapsed = _whisperDuration - remaining;
                    whisperTarget = Mathf.Clamp01(elapsed / 0.8f) * Mathf.Clamp01(remaining / 0.8f);
                }
                else
                {
                    _whisper.gameObject.SetActive(false);
                }
                if (_whisperFade != null)
                    _whisperFade.alpha = Mathf.Lerp(_whisperFade.alpha, whisperTarget,
                        1f - Mathf.Exp(-8f * Time.deltaTime));
            }

            // Panel hotkeys (not while typing).
            if (!IsTyping)
            {
                if (Input.GetKeyDown(KeyCode.H)) Toggle(_chat);
                if (Input.GetKeyDown(KeyCode.J)) Toggle(_journal);
                if (Input.GetKeyDown(KeyCode.I)) Toggle(_inventory);
                if (Input.GetKeyDown(KeyCode.M)) Toggle(_map);
                if (Input.GetKeyDown(KeyCode.O)) Toggle(_settings);
                if (Input.GetKeyDown(KeyCode.L)) Toggle(_lineage);
                if (Input.GetKeyDown(KeyCode.X)) Toggle(_codex);
                HandleGamepadPanels();
            }

            _refreshT += Time.deltaTime;
            if (_refreshT >= 0.25f) { _refreshT = 0f; Refresh(); }
            UpdateAudioCaption();
        }

        private void OnDestroy()
        {
            AmbientAudio.OnAudioCue -= ShowAudioCaption;
        }

        private static void Toggle(PanelBase p) { p.SetVisible(!p.IsOpen); }

        public void CloseAllPanels()
        {
            _chat.SetVisible(false);
            _journal.SetVisible(false);
            _inventory.SetVisible(false);
            _map.SetVisible(false);
            _settings.SetVisible(false);
            _lineage.SetVisible(false);
        }

        // -- gamepad -------------------------------------------------------------------
        //
        // Button map (Xbox layout): X inventory, Y journal, LB map, RB lineage,
        // Start settings, Back chat, B close/back, R3 snap camera to fox.
        // D-pad navigates open menus; on the HUD it fires shortcuts instead.

        private PanelBase[] _padCycle;
        private PhotoMode _photoMode;

        private void HandleGamepadPanels()
        {
            if (!GamepadInput.IsConnected) return;
            // Photo mode owns all input while active (it has its own pad map).
            if (PhotoModeActive()) return;

            if (GamepadInput.GetButtonDown(PadButton.X)) Toggle(_inventory);
            if (GamepadInput.GetButtonDown(PadButton.Y)) Toggle(_journal);
            if (GamepadInput.GetButtonDown(PadButton.LB)) Toggle(_map);
            if (GamepadInput.GetButtonDown(PadButton.RB)) Toggle(_lineage);
            if (GamepadInput.GetButtonDown(PadButton.Start)) Toggle(_settings);
            if (GamepadInput.GetButtonDown(PadButton.Back)) Toggle(_chat);
            if (GamepadInput.GetButtonDown(PadButton.B))
            {
                CloseAllPanels();
                GamepadInput.ClearFocus();
            }
            if (GamepadInput.GetButtonDown(PadButton.R3))
            {
                var cam = GameBootstrap.Instance != null ? GameBootstrap.Instance.Camera : null;
                if (cam != null) cam.SnapToAgent();
            }

            if (AnyPanelOpen())
            {
                EnsurePadFocus();
                GamepadInput.UpdateMenuNavigation();
            }
            else
            {
                // HUD d-pad shortcuts: up = photo mode, left/right = cycle panels.
                // (Down = mute is handled in AmbientAudio.)
                if (GamepadInput.GetButtonDown(PadButton.DUp)) TogglePhotoMode();
                if (GamepadInput.GetButtonDown(PadButton.DLeft)) CyclePanels(-1);
                if (GamepadInput.GetButtonDown(PadButton.DRight)) CyclePanels(1);
            }
        }

        private void TogglePhotoMode()
        {
            if (_photoMode == null) _photoMode = FindFirstObjectByType<PhotoMode>();
            if (_photoMode != null) _photoMode.ToggleFromPad();
        }

        private bool PhotoModeActive()
        {
            if (_photoMode == null) _photoMode = FindFirstObjectByType<PhotoMode>();
            return _photoMode != null && _photoMode.IsActive;
        }

        private void CyclePanels(int dir)
        {
            if (_padCycle == null)
                _padCycle = new PanelBase[] { _chat, _journal, _inventory, _map, _lineage, _settings, _codex };
            int cur = -1;
            for (int i = 0; i < _padCycle.Length; i++)
                if (_padCycle[i] != null && _padCycle[i].IsOpen) { cur = i; break; }
            if (cur >= 0) _padCycle[cur].SetVisible(false);
            int next = cur < 0
                ? (dir > 0 ? 0 : _padCycle.Length - 1)
                : (cur + dir + _padCycle.Length) % _padCycle.Length;
            if (_padCycle[next] != null) _padCycle[next].SetVisible(true);
        }

        private void EnsurePadFocus()
        {
            var es = EventSystem.current;
            if (es == null) return;
            PanelBase open = null;
            if (_padCycle == null)
                _padCycle = new PanelBase[] { _chat, _journal, _inventory, _map, _lineage, _settings, _codex };
            for (int i = 0; i < _padCycle.Length; i++)
                if (_padCycle[i] != null && _padCycle[i].IsOpen) { open = _padCycle[i]; break; }
            if (open == null) return;
            GameObject curSel = es.currentSelectedGameObject;
            if (curSel != null && curSel.transform.IsChildOf(open.transform)) return;
            GamepadInput.FocusFirstButton(open.gameObject);
        }

        public void ShowToast(string text, float duration)
        {
            _toast.text = text;
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.time + duration;
        }

        public void ShowChapterCard(string title, string subtitle)
        {
            _chapterEyebrow.text = UiKit.Spaced(title.ToUpperInvariant());
            _chapterTitle.text = title;
            _chapterSub.text = subtitle;
            _chapterCard.SetActive(true);
            _chapterUntil = Time.time + 6f;
        }

        /// <summary>Cinematic title overlay: fades in, holds, dissolves.</summary>
        public void ShowTitleCard(string title, string subtitle)
        {
            _titleCardTitle.text = UiKit.Spaced(title.ToUpperInvariant());
            _titleCardSub.text = subtitle;
            _titleCard.SetActive(true);
            _titlePhase = 1f;
            _titleT = 0f;
        }

        /// <summary>Gentle onboarding whisper: italic text that fades in and out.</summary>
        public void ShowWhisper(string text, float duration)
        {
            _whisper.text = text;
            _whisper.gameObject.SetActive(true);
            _whisperDuration = duration;
            _whisperUntil = Time.time + duration;
        }

        // -- 4Hz refresh ---------------------------------------------------------------

        private void SetText(Text t, string v, ref string cache)
        {
            if (v != cache) { cache = v; t.text = v; }
        }

        private void Refresh()
        {
            GameState s = _boot.Sim.State;
            AgentState a = s.Agent;
            if (a == null) return;

            string seedPart = (s.DailyInfo != null && s.DailyInfo.IsDailyWorld)
                ? "◆ Today's Vale · " + DailyVale.PrettyDate(s.DailyInfo.DateString)
                : "Seed " + _boot.Seed;
            ChallengeState ch = _boot.Challenge;
            string modePart = (ch != null && ch.Mode != ChallengeMode.Standard)
                ? " · ◆ " + ChallengeModes.DisplayName(ch.Mode) : "";
            SetText(_titleInfo, seedPart + " · " + UiKit.FormatGameTime((float)s.ElapsedSeconds) +
                               " · " + s.Weather + modePart, ref _cInfo);

            float yaw = _boot.Camera != null ? _boot.Camera.YawDegrees : 0f;
            SetText(_heading, UiKit.CompassLetter(yaw) + " · " +
                    ((int)(((yaw % 360f) + 360f) % 360f)).ToString("D3") + "°", ref _cCompass);
            RefreshPoiMarks(s, a);

            SetText(_aiName, a.Name, ref _cName);
            SetText(_aiGen, "Gen " + UiKit.Roman(s.Lineage.Generation) + " · " +
                            a.Stage + " · " + a.Age.ToString("F1") + "y", ref _cGen);
            int lightPct = Mathf.RoundToInt(a.Glow * 100f);
            SetText(_aiLight, "Light " + lightPct + "%", ref _cLight);
            _glowDot.color = Color.HSVToRGB(a.LightShade * 0.16f + 0.02f, 0.7f, 0.25f + 0.75f * a.Glow);
            SetText(_aiMood, "Mood " + MoodWord(a.Mood), ref _cMood);
            SetText(_aiRelation, "Bond " + s.Companion.Level, ref _cRel);
            string goal = string.IsNullOrEmpty(a.CurrentGoal) ? a.CurrentActivity : a.CurrentGoal;
            SetText(_aiGoal, "Goal  " + goal, ref _cGoal);

            _sb.Length = 0;
            var recent = s.Journal.Recent(3);
            for (int i = 0; i < recent.Count; i++)
            {
                var e = recent[i];
                _sb.Append("[G").Append(e.Generation).Append("] ");
                string t = e.Text;
                _sb.Append(t.Length > 90 ? t.Substring(0, 90) + "…" : t);
                if (i + 1 < recent.Count) _sb.Append('\n');
            }
            SetText(_aiMemory, _sb.ToString(), ref _cMem);

            if (Mathf.Abs(a.Health - _cHp) > 0.5f) { _cHp = a.Health; _tHealth = a.Health / 100f; }
            if (Mathf.Abs(a.Energy - _cLi) > 0.5f) { _cLi = a.Energy; _tLight = a.Energy / 100f; }
            if (Mathf.Abs(a.Hunger - _cHu) > 0.5f) { _cHu = a.Hunger; _tHunger = 1f - a.Hunger / 100f; }

            SetText(_hotFood, "Seedcakes ×" + s.Inventory.Bread, ref _cFood);
            SetText(_hotSalve, "Salves ×" + s.Inventory.Potions, ref _cSalve);
            SetText(_hotKeeps, "Keepsakes (" + s.Inventory.Keepsakes.Count + ")", ref _cKeeps);
        }

        private void RefreshPoiMarks(GameState s, AgentState a)
        {
            int mi = 0;
            var pois = s.World.Pois;
            for (int i = 0; i < pois.Count && mi < _poiMarks.Count; i++)
            {
                var p = pois[i];
                if (!p.Discovered) continue;
                float bearing = Mathf.Atan2(p.X - a.X, p.Z - a.Z) * Mathf.Rad2Deg;
                float br = bearing * Mathf.Deg2Rad;
                var m = _poiMarks[mi++];
                m.GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(Mathf.Sin(br) * 30f, Mathf.Cos(br) * 30f);
                m.gameObject.SetActive(true);
            }
            for (int i = mi; i < _poiMarks.Count; i++)
                _poiMarks[i].gameObject.SetActive(false);
        }

        private static string MoodWord(float mood)
        {
            if (mood >= 75f) return "bright";
            if (mood >= 55f) return "steady";
            if (mood >= 35f) return "low";
            return "dark";
        }
    }

    // -- panel base ---------------------------------------------------------------------

    public abstract class PanelBase : MonoBehaviour
    {
        protected HudController Hud;
        protected RectTransform Root;
        public bool IsOpen { get; private set; }

        public virtual void Build(HudController hud)
        {
            Hud = hud;
        }

        protected RectTransform MakeRoot(string title, float w, float h)
        {
            return MakeRoot(title, w, h, UiKit.PanelBg);
        }

        protected RectTransform MakeRoot(string title, float w, float h, Color bg)
        {
            Root = UiKit.Panel(transform, "Root", bg,
                0.5f, 0.5f, 0.5f, 0.5f, -w / 2f, -h / 2f, w / 2f, h / 2f);
            var head = UiKit.Label(Root, "Title", title, 20, UiKit.Gold, TextAnchor.UpperLeft);
            UiKit.AddShadow(head);
            head.GetComponent<RectTransform>().offsetMin = new Vector2(16f, -38f);
            UiKit.Divider(Root, "Rule", new Color(0.72f, 0.55f, 0.28f, 0.45f),
                0f, 1f, 1f, 1f, 16f, -48f, -16f, -47f);
            var hint = UiKit.Label(Root, "Hint", "H/J/I/M/O/L/X · Esc — or pad: Y/X/LB/RB/Start · B to close",
                                   12, UiKit.DimInk, TextAnchor.UpperRight);
            hint.GetComponent<RectTransform>().offsetMin = new Vector2(-420f, -34f);
            return Root;
        }

        public virtual void SetVisible(bool open)
        {
            IsOpen = open;
            if (Root != null) Root.gameObject.SetActive(open);
            if (open) OnOpen();
        }

        protected virtual void OnOpen() { }
    }
}
