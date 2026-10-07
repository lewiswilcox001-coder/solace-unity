// Solace.Unity — HUD panels. The sim keeps running while any panel is open.
//
// ChatPanel (H): conversation via Companion.Respond (truth contract — the
// reply's Influence is already logged on the agent by Core).
// JournalPanel (J): generation-tagged entries, chapter markers highlighted.
// InventoryPanel (I): food stores, salves, keepsakes.
// MapPanel (M): Texture2D from WorldData — biome colors, river, loch, colossi,
// discovered POIs with learned names ONLY (truth contract), agent arrow.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Solace.Core;

namespace Solace.Unity
{
    // -- chat ---------------------------------------------------------------------

    public class ChatPanel : PanelBase
    {
        private Text _log;
        private InputField _input;
        private ScrollRect _scroll;
        private readonly StringBuilder _sb = new StringBuilder(4096);

        public bool IsTyping { get { return _input != null && _input.isFocused; } }

        public override void Build(HudController hud)
        {
            base.Build(hud);
            Root = MakeRoot("Speak with Solace", 520f, 560f);

            var scrollRt = UiKit.Rect(Root, "Scroll", 0f, 0f, 1f, 1f, 12f, 64f, -12f, -58f);
            scrollRt.gameObject.AddComponent<RectMask2D>();
            _scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;

            var content = UiKit.Rect(scrollRt, "Content", 0f, 1f, 1f, 1f, 0f, 0f, 0f, 0f);
            content.pivot = new Vector2(0.5f, 1f);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = content;

            _log = UiKit.Label(content, "Log", "", 14, UiKit.Ink, TextAnchor.UpperLeft);
            _log.lineSpacing = 1.25f;
            UiKit.AddShadow(_log, 0.4f);
            _log.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
            _log.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 1f);
            _log.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
            var lf = _log.gameObject.AddComponent<ContentSizeFitter>();
            lf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var inputRt = UiKit.Rect(Root, "InputRow", 0f, 0f, 1f, 0f, 12f, 12f, -12f, 52f);
            _input = UiKit.Input(inputRt, "Input", 14, 380f, 40f);
            var inputRect = _input.GetComponent<RectTransform>();
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 1f);
            inputRect.offsetMin = new Vector2(0f, 0f);
            inputRect.offsetMax = new Vector2(-110f, 0f);
            var send = UiKit.Button(inputRt, "Send", "Send", 14);
            var sendRect = send.GetComponent<RectTransform>();
            sendRect.anchorMin = new Vector2(1f, 0f);
            sendRect.anchorMax = new Vector2(1f, 1f);
            sendRect.offsetMin = new Vector2(-100f, 0f);
            sendRect.offsetMax = new Vector2(0f, 0f);
            send.onClick.AddListener(Send);
            _input.onEndEdit.AddListener(OnEndEdit);
        }

        protected override void OnOpen()
        {
            AppendLine("Solace", "I'm here. What's on your mind?");
        }

        private void OnEndEdit(string text)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                Send();
        }

        private void Send()
        {
            string text = _input.text.Trim();
            if (text.Length == 0) return;
            _input.text = "";
            AppendLine("You", text);
            try
            {
                var sim = GameBootstrap.Instance.Sim;
                CompanionReply reply = Companion.Respond(sim.State, text);
                AppendLine("Solace", reply.Text);
            }
            catch (System.Exception ex)
            {
                AppendLine("Solace", "…sorry, I lost the thread. (" + ex.Message + ")");
            }
            _input.ActivateInputField();
        }

        private void AppendLine(string who, string text)
        {
            _sb.Append(who == "You" ? "\nYou: " : "\nSolace: ");
            _sb.Append(text);
            if (_sb.Length > 6000) _sb.Remove(0, _sb.Length - 6000);
            _log.text = _sb.ToString();
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }
    }

    // -- journal --------------------------------------------------------------------

    public class JournalPanel : PanelBase
    {
        private RectTransform _content;
        private ScrollRect _scroll;
        private int _builtCount = -1;

        public override void Build(HudController hud)
        {
            base.Build(hud);
            Root = MakeRoot("The Chronicle", 900f, 640f, UiKit.Parchment);

            var scrollRt = UiKit.Rect(Root, "Scroll", 0f, 0f, 1f, 1f, 12f, 12f, -12f, -58f);
            scrollRt.gameObject.AddComponent<RectMask2D>();
            _scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;

            _content = UiKit.Rect(scrollRt, "Content", 0f, 1f, 1f, 1f, 0f, 0f, 0f, 0f);
            _content.pivot = new Vector2(0.5f, 1f);
            var layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 8f;
            layout.padding = new RectOffset(10, 10, 8, 8);
            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;
        }

        protected override void OnOpen() { Rebuild(); }

        private void Update()
        {
            if (!IsOpen) return;
            var sim = GameBootstrap.Instance != null ? GameBootstrap.Instance.Sim : null;
            if (sim != null && sim.State.Journal.Count != _builtCount) Rebuild();
        }

        private static string Clean(string s)
        {
            // Journal text is prose; strip angle brackets so rich-text stays intact.
            return s.Replace("<", "").Replace(">", "");
        }

        private void AddGenerationHeader(int gen)
        {
            var t = UiKit.Label(_content, "Gen" + gen,
                "· · ·   Generation " + UiKit.Roman(gen) + "   · · ·",
                14, UiKit.Gold, TextAnchor.MiddleCenter);
            UiKit.AddShadow(t, 0.4f);
            var f = t.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void AddEntry(JournalEntry e, int idx)
        {
            bool chapter = e.Category == JournalCategory.Chapter;
            bool dream = e.Category == JournalCategory.Dream;
            string timeTag = "<color=#B2A68C>" + UiKit.FormatGameTime(e.Time) + "</color>   ";
            string body = Clean(e.Text);
            string line;
            Color col;
            int size;
            FontStyle style = FontStyle.Normal;
            if (chapter)
            {
                line = "<color=#F2BF59>" + body + "</color>";
                col = UiKit.Gold;
                size = 16;
            }
            else if (dream)
            {
                line = timeTag + "<i><color=#B89EF2>" + body + "</color></i>";
                col = UiKit.ParchmentInk;
                size = 15;
                style = FontStyle.Italic;
            }
            else
            {
                line = timeTag + body;
                col = UiKit.ParchmentInk;
                size = 15;
            }
            var t = UiKit.Label(_content, "E" + idx, line, size, col, TextAnchor.UpperLeft);
            t.fontStyle = style;
            t.lineSpacing = 1.3f;
            var f = t.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void Rebuild()
        {
            var sim = GameBootstrap.Instance.Sim;
            _builtCount = sim.State.Journal.Count;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Object.Destroy(_content.GetChild(i).gameObject);

            var entries = sim.State.Journal.Recent(80);
            if (entries.Count == 0)
            {
                var t = UiKit.Label(_content, "Empty",
                    "The chronicle is blank. The story has not begun.",
                    15, UiKit.ParchmentInk, TextAnchor.MiddleCenter);
                t.lineSpacing = 1.3f;
                var f = t.gameObject.AddComponent<ContentSizeFitter>();
                f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            int lastGen = -1;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (e.Generation != lastGen)
                {
                    lastGen = e.Generation;
                    AddGenerationHeader(lastGen);
                }
                AddEntry(e, entries.Count - 1 - i);
            }
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 1f;
        }
    }

    // -- inventory ---------------------------------------------------------------------

    public class InventoryPanel : PanelBase
    {
        private Text _body;

        public override void Build(HudController hud)
        {
            base.Build(hud);
            Root = MakeRoot("Pack", 600f, 440f, UiKit.Parchment);
            _body = UiKit.Label(Root, "Body", "", 15, UiKit.ParchmentInk, TextAnchor.UpperLeft);
            _body.lineSpacing = 1.3f;
            _body.GetComponent<RectTransform>().offsetMin = new Vector2(16f, -400f);
            _body.GetComponent<RectTransform>().offsetMax = new Vector2(-16f, -58f);
            _body.verticalOverflow = VerticalWrapMode.Overflow;
        }

        protected override void OnOpen()
        {
            var inv = GameBootstrap.Instance.Sim.State.Inventory;
            var sb = new StringBuilder(512);
            sb.Append("Seedcakes (food stores): ").Append(inv.Bread).Append('\n');
            sb.Append("Salves: ").Append(inv.Potions).Append('\n');
            sb.Append('\n').Append("Keepsakes:\n");
            if (inv.Keepsakes.Count == 0) sb.Append("  — none yet —");
            for (int i = 0; i < inv.Keepsakes.Count; i++)
                sb.Append("  • ").Append(inv.Keepsakes[i]).Append('\n');
            _body.text = sb.ToString();
        }
    }

    // -- map ------------------------------------------------------------------------------

    public class MapPanel : PanelBase
    {
        private const int MapRes = 256;
        private RawImage _raw;
        private Texture2D _tex;
        private Text _poiList;
        private float _redrawT;

        public override void Build(HudController hud)
        {
            base.Build(hud);
            Root = MakeRoot("Vale map — known places only", 760f, 640f);

            var mapRt = UiKit.Rect(Root, "Map", 0f, 1f, 0f, 1f, 14f, -500f, 484f, -58f);
            _raw = mapRt.gameObject.AddComponent<RawImage>();
            _raw.raycastTarget = false;
            _tex = new Texture2D(MapRes, MapRes, TextureFormat.RGBA32, false);
            _tex.filterMode = FilterMode.Point;
            _raw.texture = _tex;

            _poiList = UiKit.Label(Root, "POIs", "", 14, UiKit.Ink, TextAnchor.UpperLeft);
            _poiList.lineSpacing = 1.25f;
            UiKit.AddShadow(_poiList, 0.4f);
            _poiList.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
            _poiList.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 1f);
            _poiList.GetComponent<RectTransform>().offsetMin = new Vector2(498f, -500f);
            _poiList.GetComponent<RectTransform>().offsetMax = new Vector2(-14f, -58f);
            _poiList.verticalOverflow = VerticalWrapMode.Overflow;
        }

        protected override void OnOpen() { Redraw(); _redrawT = 0f; }

        private void Update()
        {
            if (!IsOpen) return;
            _redrawT += Time.deltaTime;
            if (_redrawT > 1f) { _redrawT = 0f; Redraw(); }
        }

        private int ToMap(float worldCoord, float half)
        {
            return Mathf.Clamp((int)((worldCoord + half) / (half * 2f) * MapRes), 0, MapRes - 1);
        }

        private void Plot(Color32[] px, int mx, int mz, Color32 c, int r)
        {
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = mx + dx, z = mz + dz;
                    if (x < 0 || z < 0 || x >= MapRes || z >= MapRes) continue;
                    px[x + z * MapRes] = c;
                }
        }

        private void Redraw()
        {
            var sim = GameBootstrap.Instance.Sim;
            if (sim == null) return;
            WorldData w = sim.State.World;
            float half = w.HalfSize;

            var px = new Color32[MapRes * MapRes];
            // Biomes, darkened for the parchment feel.
            for (int mz = 0; mz < MapRes; mz++)
                for (int mx = 0; mx < MapRes; mx++)
                {
                    float wx = (mx / (float)MapRes) * half * 2f - half;
                    float wz = (mz / (float)MapRes) * half * 2f - half;
                    Biome b = w.GetBiome(wx, wz);
                    Color32 c;
                    switch (b)
                    {
                        case Biome.Foxpine: c = new Color32(28, 66, 56, 255); break;
                        case Biome.FellCrag: c = new Color32(96, 96, 104, 255); break;
                        case Biome.SnowPeak: c = new Color32(200, 205, 215, 255); break;
                        case Biome.Riverbank: c = new Color32(150, 132, 96, 255); break;
                        case Biome.DenGrounds: c = new Color32(84, 104, 56, 255); break;
                        default: c = new Color32(100, 116, 110, 255); break; // Mistmoor
                    }
                    px[mx + mz * MapRes] = w.IsWater(wx, wz) ? new Color32(36, 70, 92, 255) : c;
                }
            // River + loch.
            var riverBlue = new Color32(70, 130, 170, 255);
            for (int i = 0; i < w.RiverPath.Count; i++)
                Plot(px, ToMap(w.RiverPath[i].X, half), ToMap(w.RiverPath[i].Z, half), riverBlue, 1);
            int lakeR = Mathf.Max(2, (int)(w.LakeRadius / (half * 2f) * MapRes));
            int lcx = ToMap(w.LakeCenter.X, half), lcz = ToMap(w.LakeCenter.Z, half);
            Plot(px, lcx, lcz, riverBlue, lakeR);

            // Colossi markers.
            var colTw = new Color32(220, 200, 120, 255);
            var colIsle = new Color32(120, 220, 200, 255);
            foreach (var c in w.Colossi)
                Plot(px, ToMap(c.X, half), ToMap(c.Z, half),
                     c.Kind == ColossusKind.TreeWalker ? colTw : colIsle, 2);

            // Discovered POIs only — learned names only (truth contract).
            var poiGold = new Color32(240, 200, 90, 255);
            var sb = new StringBuilder(512);
            sb.Append("Known places:\n");
            int found = 0;
            foreach (var p in w.Pois)
            {
                if (!p.Discovered) continue;
                Plot(px, ToMap(p.X, half), ToMap(p.Z, half), poiGold, 1);
                sb.Append("• ").Append(p.DisplayName).Append('\n');
                found++;
            }
            if (found == 0) sb.Append("  — none discovered yet —");
            _poiList.text = sb.ToString();

            // Agent arrow (white, rotated by facing).
            AgentState a = sim.State.Agent;
            if (a != null)
            {
                int ax = ToMap(a.X, half), az = ToMap(a.Z, half);
                var arrow = new Color32(255, 255, 255, 255);
                float fx = Mathf.Sin(a.Facing), fz = Mathf.Cos(a.Facing);
                Plot(px, ax, az, arrow, 1);
                Plot(px, ax + (int)(fx * 3f), az + (int)(fz * 3f), arrow, 1);
            }

            _tex.SetPixels32(px);
            _tex.Apply();
        }
    }

    // -- settings ------------------------------------------------------------------
    //
    // SettingsPanel (O): accessibility & comfort options. All settings apply
    // live and persist via PlayerPrefs. The sim keeps running while open.

    public class SettingsPanel : PanelBase
    {
        private const float RowH = 34f;

        public override void Build(HudController hud)
        {
            base.Build(hud);
            Root = MakeRoot("Accessibility & comfort", 520f, 560f);
            BuildContent(hud);
        }

        private void BuildContent(HudController hud)
        {
            // Clear previous content rows (keep title/header from MakeRoot).
            for (int i = Root.childCount - 1; i >= 0; i--)
            {
                var child = Root.GetChild(i);
                if (child.name != "Title" && child.name != "Rule" && child.name != "Hint")
                    Destroy(child.gameObject);
            }

            float y = -58f;
            // UI scale.
            AddCycle("UIScale", "Interface size", AccessibilitySettings.UiScaleNames,
                AccessibilitySettings.UiScaleIndex, ref y, i =>
                {
                    AccessibilitySettings.UiScaleIndex = i;
                    hud.ApplyUiScale();
                });
            // Camera sway.
            AddCycle("Sway", "Camera sway", new[] { "Off", "Reduced", "Full" },
                AccessibilitySettings.CameraSway, ref y, i =>
                {
                    AccessibilitySettings.CameraSway = i;
                });
            // Reduce motion.
            AddToggle("ReduceMotion", "Reduce motion (fades, no sweeps)",
                AccessibilitySettings.ReduceMotion, ref y, v =>
                {
                    AccessibilitySettings.ReduceMotion = v;
                });
            // Colorblind preview.
            AddCycle("Cvd", "Color vision preview", new[] { "Off", "Protanopia", "Deuteranopia", "Tritanopia" },
                AccessibilitySettings.ColorblindMode, ref y, i =>
                {
                    AccessibilitySettings.ColorblindMode = i;
                });
            var cvdNote = UiKit.Label(Root, "CvdNote",
                "Preview how the world appears with color vision deficiency.",
                11, UiKit.DimInk, TextAnchor.UpperLeft);
            var noteRt = cvdNote.GetComponent<RectTransform>();
            noteRt.anchorMin = new Vector2(0f, 1f); noteRt.anchorMax = new Vector2(1f, 1f);
            noteRt.offsetMin = new Vector2(16f, y - 18f); noteRt.offsetMax = new Vector2(-16f, y);
            y -= 22f;

            // Volume sliders.
            AddSlider("VolMaster", "Master volume", AccessibilitySettings.MasterVolume, ref y,
                v => AccessibilitySettings.MasterVolume = v);
            AddSlider("VolAmbient", "Ambient (wind, water, crickets)", AccessibilitySettings.AmbientVolume, ref y,
                v => AccessibilitySettings.AmbientVolume = v);
            AddSlider("VolMusic", "Music (ambient pads)", AccessibilitySettings.MusicVolume, ref y,
                v => AccessibilitySettings.MusicVolume = v);
            AddSlider("VolEffects", "Effects (calls, thunder)", AccessibilitySettings.EffectsVolume, ref y,
                v => AccessibilitySettings.EffectsVolume = v);

            // Audio captions.
            AddToggle("Captions", "Captions for audio cues",
                AccessibilitySettings.AudioCaptions, ref y, v =>
                {
                    AccessibilitySettings.AudioCaptions = v;
                });

            // Reset.
            var resetRt = UiKit.Rect(Root, "ResetRow", 0f, 1f, 1f, 1f, 16f, y - RowH, -16f, y);
            var resetBtn = UiKit.Button(resetRt, "Reset", "Reset to defaults", 13);
            var brt = resetBtn.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(0f, 1f);
            brt.offsetMin = new Vector2(0f, 0f); brt.offsetMax = new Vector2(180f, 0f);
            resetBtn.onClick.AddListener(() =>
            {
                AccessibilitySettings.ResetToDefaults();
                hud.ApplyUiScale();
                BuildContent(hud); // rebuild rows with default values
            });
        }

        private void AddCycle(string name, string label, string[] options, int current,
                             ref float y, System.Action<int> onChange)
        {
            int idx = Mathf.Clamp(current, 0, options.Length - 1);
            var row = UiKit.Rect(Root, name, 0f, 1f, 1f, 1f, 16f, y - RowH, -16f, y);
            var btn = UiKit.Cycle(row, "Cycle", label, options[idx]);
            var valueText = btn.transform.Find("Value").GetComponent<Text>();
            int cur = idx;
            btn.onClick.AddListener(() =>
            {
                cur = (cur + 1) % options.Length;
                valueText.text = options[cur];
                onChange(cur);
            });
            y -= RowH;
        }

        private void AddToggle(string name, string label, bool value,
                               ref float y, System.Action<bool> onChange)
        {
            var row = UiKit.Rect(Root, name, 0f, 1f, 1f, 1f, 16f, y - RowH, -16f, y);
            var btn = UiKit.Toggle(row, "Tgl", label, value);
            var valueText = btn.transform.Find("Text").GetComponent<Text>();
            // UiKit.Toggle already flips its own label; hook the actual value.
            btn.onClick.AddListener(() =>
            {
                onChange(valueText.text == "On");
            });
            y -= RowH;
        }

        private void AddSlider(string name, string label, float value,
                               ref float y, System.Action<float> onChange)
        {
            var row = UiKit.Rect(Root, name, 0f, 1f, 1f, 1f, 16f, y - RowH, -16f, y);
            var slider = UiKit.Slider(row, "Sld", label, 0f, 1f, value, 150f);
            slider.onValueChanged.AddListener(v => onChange(v));
            y -= RowH;
        }

        protected override void OnOpen()
        {
            // Refresh values live each time the panel opens (settings may have
            // changed via PlayerPrefs or other paths). Simplest: rebuild.
            // (Panel content is cheap; rebuild keeps everything in sync.)
        }
    }
}
