// Solace.Unity — the heads-up display, built in code.
//
// Layout (1920×1080 reference): top-left SOLACE card, top-center compass,
// right AI STATUS, bottom-left need bars, bottom-center hotbar, bottom-right
// hints. Panels (chat/journal/inventory/map) toggle with H/J/I/M; the sim
// keeps running while they're open. Dynamic text refreshes at 4Hz with
// change-detection to avoid per-frame allocations.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Solace.Core;

namespace Solace.Unity
{
    public class HudController : MonoBehaviour
    {
        private GameBootstrap _boot;
        private Canvas _canvas;

        // Widgets.
        private Text _titleSeed;
        private Text _titleTime;
        private Text _compass;
        private Text _aiName;
        private Text _aiGen;
        private Text _aiLight;
        private Image _glowDot;
        private Text _aiMood;
        private Text _aiRelation;
        private Text _aiGoal;
        private Text _aiMemory;
        private Image _barHealth;
        private Image _barLight;
        private Image _barHunger;
        private Text _hotFood;
        private Text _hotSalve;
        private Text _hotKeeps;
        private Text _toast;
        private Text _chapterTitle;
        private Text _chapterSub;
        private GameObject _chapterCard;

        // Panels.
        private ChatPanel _chat;
        private JournalPanel _journal;
        private InventoryPanel _inventory;
        private MapPanel _map;

        private float _toastUntil = -1f;
        private float _chapterUntil = -1f;
        private float _refreshT;
        private readonly StringBuilder _sb = new StringBuilder(1024);

        // Change-detection cache.
        private string _cSeed = "", _cTime = "", _cCompass = "", _cName = "", _cGen = "",
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

            BuildTitleCard();
            BuildCompass();
            BuildAiStatus();
            BuildBars();
            BuildHotbar();
            BuildHints();
            BuildToast();
            BuildChapterCard();
            BuildPanels();

            gameObject.AddComponent<BugReportTool>();
        }

        // -- construction --------------------------------------------------------

        private void BuildTitleCard()
        {
            var card = UiKit.Panel(_canvas.transform, "TitleCard", UiKit.PanelBg,
                0f, 1f, 0f, 1f, 16f, -156f, 396f, -16f);
            var title = UiKit.Label(card, "Title", "SOLACE", 26, UiKit.Gold, TextAnchor.UpperLeft);
            title.GetComponent<RectTransform>().offsetMin = new Vector2(14f, -44f);
            var sub = UiKit.Label(card, "Sub", "Find purpose in a world that's yours to explore.",
                                  13, UiKit.DimInk, TextAnchor.UpperLeft);
            sub.GetComponent<RectTransform>().offsetMin = new Vector2(14f, -70f);
            _titleSeed = UiKit.Label(card, "Seed", "", 14, UiKit.Ink, TextAnchor.UpperLeft);
            _titleSeed.GetComponent<RectTransform>().offsetMin = new Vector2(14f, -96f);
            _titleTime = UiKit.Label(card, "Time", "", 14, UiKit.Ink, TextAnchor.UpperLeft);
            _titleTime.GetComponent<RectTransform>().offsetMin = new Vector2(14f, -120f);
        }

        private void BuildCompass()
        {
            var card = UiKit.Panel(_canvas.transform, "Compass", UiKit.PanelBg,
                0.5f, 1f, 0.5f, 1f, -160f, -58f, 160f, -16f);
            _compass = UiKit.Label(card, "Compass", "N", 20, UiKit.Ink, TextAnchor.MiddleCenter);
        }

        private void BuildAiStatus()
        {
            var card = UiKit.Panel(_canvas.transform, "AIStatus", UiKit.PanelBg,
                1f, 1f, 1f, 1f, -336f, -420f, -16f, -16f);
            var head = UiKit.Label(card, "Head", "AI STATUS", 15, UiKit.Gold, TextAnchor.UpperLeft);
            head.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -30f);
            _aiName = UiKit.Label(card, "Name", "", 20, UiKit.Ink, TextAnchor.UpperLeft);
            _aiName.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -60f);
            _aiGen = UiKit.Label(card, "Gen", "", 14, UiKit.DimInk, TextAnchor.UpperLeft);
            _aiGen.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -86f);

            // Light row: label + glow dot.
            var lightRow = UiKit.Rect(card, "LightRow", 0f, 1f, 1f, 1f, 12f, -114f, -12f, -88f);
            _aiLight = UiKit.Label(lightRow, "Light", "", 14, UiKit.Ink, TextAnchor.MiddleLeft);
            var dotRt = UiKit.Rect(lightRow, "GlowDot", 1f, 0.5f, 1f, 0.5f, -30f, -9f, -12f, 9f);
            _glowDot = dotRt.gameObject.AddComponent<Image>();
            _glowDot.sprite = Sprite.Create(UiKit.DotTexture(),
                new Rect(0f, 0f, 24f, 24f), new Vector2(0.5f, 0.5f));
            _glowDot.raycastTarget = false;

            _aiMood = UiKit.Label(card, "Mood", "", 14, UiKit.Ink, TextAnchor.UpperLeft);
            _aiMood.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -142f);
            _aiRelation = UiKit.Label(card, "Rel", "", 14, UiKit.Ink, TextAnchor.UpperLeft);
            _aiRelation.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -168f);
            _aiGoal = UiKit.Label(card, "Goal", "", 14, UiKit.Ink, TextAnchor.UpperLeft);
            _aiGoal.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -194f);
            _aiGoal.verticalOverflow = VerticalWrapMode.Overflow;
            var memHead = UiKit.Label(card, "MemHead", "MEMORY", 13, UiKit.Gold, TextAnchor.UpperLeft);
            memHead.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -248f);
            _aiMemory = UiKit.Label(card, "Mem", "", 13, UiKit.DimInk, TextAnchor.UpperLeft);
            _aiMemory.GetComponent<RectTransform>().offsetMin = new Vector2(12f, -396f);
            _aiMemory.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void BuildBars()
        {
            var card = UiKit.Panel(_canvas.transform, "Bars", UiKit.PanelBg,
                0f, 0f, 0f, 0f, 16f, 16f, 396f, 118f);
            var r1 = UiKit.Rect(card, "R1", 0f, 1f, 1f, 1f, 12f, -36f, -12f, -10f);
            var r2 = UiKit.Rect(card, "R2", 0f, 1f, 1f, 1f, 12f, -64f, -12f, -38f);
            var r3 = UiKit.Rect(card, "R3", 0f, 1f, 1f, 1f, 12f, -92f, -12f, -66f);
            _barHealth = UiKit.Bar(r1, "Health", "Health", UiKit.HealthRed, 220f);
            _barLight = UiKit.Bar(r2, "Light", "Light", UiKit.LightGreen, 220f);
            _barHunger = UiKit.Bar(r3, "Hunger", "Hunger", UiKit.HungerAmber, 220f);
        }

        private void BuildHotbar()
        {
            var bar = UiKit.Panel(_canvas.transform, "Hotbar", UiKit.PanelBg,
                0.5f, 0f, 0.5f, 0f, -280f, 16f, 280f, 60f);
            _hotFood = UiKit.Label(bar, "Food", "", 14, UiKit.Ink, TextAnchor.MiddleCenter);
            _hotFood.GetComponent<RectTransform>().offsetMax = new Vector2(-186f, 0f);
            _hotSalve = UiKit.Label(bar, "Salve", "", 14, UiKit.Ink, TextAnchor.MiddleCenter);
            _hotSalve.GetComponent<RectTransform>().offsetMin = new Vector2(186f, 0f);
            _hotSalve.GetComponent<RectTransform>().offsetMax = new Vector2(-186f, 0f);
            _hotKeeps = UiKit.Label(bar, "Keeps", "", 14, UiKit.Ink, TextAnchor.MiddleCenter);
            _hotKeeps.GetComponent<RectTransform>().offsetMin = new Vector2(186f, 0f);
        }

        private void BuildHints()
        {
            var hints = UiKit.Label(_canvas.transform, "Hints",
                "H chat · J journal · I inventory · M map · N new life · F12 report",
                13, UiKit.DimInk, TextAnchor.LowerRight);
            var rt = hints.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = new Vector2(1f, 0f);
            rt.offsetMin = new Vector2(-460f, 16f); rt.offsetMax = new Vector2(-16f, 44f);
        }

        private void BuildToast()
        {
            _toast = UiKit.Label(_canvas.transform, "Toast", "", 16, UiKit.Ink, TextAnchor.UpperCenter);
            var rt = _toast.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(-420f, -150f); rt.offsetMax = new Vector2(420f, -70f);
            _toast.gameObject.SetActive(false);
        }

        private void BuildChapterCard()
        {
            _chapterCard = new GameObject("ChapterCard");
            _chapterCard.transform.SetParent(_canvas.transform, false);
            var rt = _chapterCard.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-500f, -90f); rt.offsetMax = new Vector2(500f, 90f);
            _chapterTitle = UiKit.Label(_chapterCard.transform, "CTitle", "", 54, UiKit.Gold, TextAnchor.MiddleCenter);
            _chapterSub = UiKit.Label(_chapterCard.transform, "CSub", "", 26, UiKit.Ink, TextAnchor.MiddleCenter);
            _chapterSub.GetComponent<RectTransform>().offsetMin = new Vector2(0f, -160f);
            _chapterCard.SetActive(false);
        }

        private void BuildPanels()
        {
            _chat = AddPanel<ChatPanel>("ChatPanel");
            _journal = AddPanel<JournalPanel>("JournalPanel");
            _inventory = AddPanel<InventoryPanel>("InventoryPanel");
            _map = AddPanel<MapPanel>("MapPanel");
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

        private void Update()
        {
            if (_boot == null || _boot.Sim == null) return;

            // Toast / chapter-card timers.
            if (_toast.gameObject.activeSelf && Time.time > _toastUntil)
                _toast.gameObject.SetActive(false);
            if (_chapterCard.activeSelf && Time.time > _chapterUntil)
                _chapterCard.SetActive(false);

            // Panel hotkeys (not while typing).
            if (!IsTyping)
            {
                if (Input.GetKeyDown(KeyCode.H)) Toggle(_chat);
                if (Input.GetKeyDown(KeyCode.J)) Toggle(_journal);
                if (Input.GetKeyDown(KeyCode.I)) Toggle(_inventory);
                if (Input.GetKeyDown(KeyCode.M)) Toggle(_map);
            }

            _refreshT += Time.deltaTime;
            if (_refreshT >= 0.25f) { _refreshT = 0f; Refresh(); }
        }

        private static void Toggle(PanelBase p) { p.SetVisible(!p.IsOpen); }

        public void CloseAllPanels()
        {
            _chat.SetVisible(false);
            _journal.SetVisible(false);
            _inventory.SetVisible(false);
            _map.SetVisible(false);
        }

        public void ShowToast(string text, float duration)
        {
            _toast.text = text;
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.time + duration;
        }

        public void ShowChapterCard(string title, string subtitle)
        {
            _chapterTitle.text = title;
            _chapterSub.text = subtitle;
            _chapterCard.SetActive(true);
            _chapterUntil = Time.time + 6f;
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

            SetText(_titleSeed, "Seed " + _boot.Seed, ref _cSeed);
            SetText(_titleTime, UiKit.FormatGameTime(s.ElapsedSeconds) + " · " + s.Weather, ref _cTime);

            float yaw = _boot.Camera != null ? _boot.Camera.YawDegrees : 0f;
            SetText(_compass, UiKit.CompassLetter(yaw) + "  " +
                    ((int)(((yaw % 360f) + 360f) % 360f)) + "°", ref _cCompass);

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

            if (Mathf.Abs(a.Health - _cHp) > 0.5f) { _cHp = a.Health; _barHealth.fillAmount = a.Health / 100f; }
            if (Mathf.Abs(a.Energy - _cLi) > 0.5f) { _cLi = a.Energy; _barLight.fillAmount = a.Energy / 100f; }
            if (Mathf.Abs(a.Hunger - _cHu) > 0.5f) { _cHu = a.Hunger; _barHunger.fillAmount = 1f - a.Hunger / 100f; }

            SetText(_hotFood, "Seedcakes ×" + s.Inventory.Bread, ref _cFood);
            SetText(_hotSalve, "Salves ×" + s.Inventory.Potions, ref _cSalve);
            SetText(_hotKeeps, "Keepsakes (" + s.Inventory.Keepsakes.Count + ")", ref _cKeeps);
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
            Root = UiKit.Panel(transform, "Root", UiKit.PanelBg,
                0.5f, 0.5f, 0.5f, 0.5f, -w / 2f, -h / 2f, w / 2f, h / 2f);
            var head = UiKit.Label(Root, "Title", title, 18, UiKit.Gold, TextAnchor.UpperLeft);
            head.GetComponent<RectTransform>().offsetMin = new Vector2(14f, -34f);
            var hint = UiKit.Label(Root, "Hint", "H/J/I/M or Esc to close — the world keeps turning",
                                   12, UiKit.DimInk, TextAnchor.UpperRight);
            hint.GetComponent<RectTransform>().offsetMin = new Vector2(-420f, -30f);
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
