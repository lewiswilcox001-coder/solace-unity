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

            var scrollRt = UiKit.Rect(Root, "Scroll", 0f, 0f, 1f, 1f, 12f, 64f, -12f, -44f);
            scrollRt.gameObject.AddComponent<RectMask2D>();
            _scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;

            var content = UiKit.Rect(scrollRt, "Content", 0f, 1f, 1f, 1f, 0f, 0f, 0f, 0f);
            content.pivot = new Vector2(0.5f, 1f);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = content;

            _log = UiKit.Label(content, "Log", "", 14, UiKit.Ink, TextAnchor.UpperLeft);
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
            Root = MakeRoot("Journal — a multi-generational chronicle", 900f, 640f);

            var scrollRt = UiKit.Rect(Root, "Scroll", 0f, 0f, 1f, 1f, 12f, 12f, -12f, -44f);
            scrollRt.gameObject.AddComponent<RectMask2D>();
            _scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;

            _content = UiKit.Rect(scrollRt, "Content", 0f, 1f, 1f, 1f, 0f, 0f, 0f, 0f);
            _content.pivot = new Vector2(0.5f, 1f);
            var layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            layout.padding = new RectOffset(4, 4, 4, 4);
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

        private void Rebuild()
        {
            var sim = GameBootstrap.Instance.Sim;
            _builtCount = sim.State.Journal.Count;
            for (int i = _content.childCount - 1; i >= 0; i--)
                Object.Destroy(_content.GetChild(i).gameObject);

            var entries = sim.State.Journal.Recent(80);
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                bool chapter = e.Category == JournalCategory.Chapter;
                string line = (chapter ? "[CHAPTER] " : "") +
                              "[G" + e.Generation + " · " + UiKit.FormatGameTime(e.Time) + "] " + e.Text;
                var t = UiKit.Label(_content, "E" + i, line, chapter ? 15 : 14,
                                    chapter ? UiKit.Gold : UiKit.Ink, TextAnchor.UpperLeft);
                var f = t.gameObject.AddComponent<ContentSizeFitter>();
                f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
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
            Root = MakeRoot("Pack", 600f, 440f);
            _body = UiKit.Label(Root, "Body", "", 15, UiKit.Ink, TextAnchor.UpperLeft);
            _body.GetComponent<RectTransform>().offsetMin = new Vector2(14f, -400f);
            _body.GetComponent<RectTransform>().offsetMax = new Vector2(-14f, -44f);
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

            var mapRt = UiKit.Rect(Root, "Map", 0f, 1f, 0f, 1f, 14f, -500f, 484f, -46f);
            _raw = mapRt.gameObject.AddComponent<RawImage>();
            _raw.raycastTarget = false;
            _tex = new Texture2D(MapRes, MapRes, TextureFormat.RGBA32, false);
            _tex.filterMode = FilterMode.Point;
            _raw.texture = _tex;

            _poiList = UiKit.Label(Root, "POIs", "", 14, UiKit.Ink, TextAnchor.UpperLeft);
            _poiList.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
            _poiList.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 1f);
            _poiList.GetComponent<RectTransform>().offsetMin = new Vector2(498f, -500f);
            _poiList.GetComponent<RectTransform>().offsetMax = new Vector2(-14f, -46f);
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
            foreach (var p in w.Pois)
            {
                if (!p.Discovered) continue;
                Plot(px, ToMap(p.X, half), ToMap(p.Z, half), poiGold, 1);
                sb.Append("• ").Append(p.DisplayName).Append('\n');
            }
            _poiList.text = sb.ToString();

            // Agent arrow (white, rotated by facing).
            AgentState a = sim.State.Agent;
            int ax = ToMap(a.X, half), az = ToMap(a.Z, half);
            var arrow = new Color32(255, 255, 255, 255);
            float fx = Mathf.Sin(a.Facing), fz = Mathf.Cos(a.Facing);
            Plot(px, ax, az, arrow, 1);
            Plot(px, ax + (int)(fx * 3f), az + (int)(fz * 3f), arrow, 1);

            _tex.SetPixels32(px);
            _tex.Apply();
        }
    }
}
