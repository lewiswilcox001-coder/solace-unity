// Solace.Unity — lifetime statistics dashboard.
// Self-bootstrapping via RuntimeInitializeOnLoadMethod; GameBootstrap untouched.
// Tracks real playtime (Unity-side) and displays the Core-accumulated StatState:
// distance, discoveries, generations, kits, kills, greetings, tales, dreams,
// plus a per-day playtime bar chart. Press G to toggle.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Solace.Core;

namespace Solace.Unity
{
    public class StatView : MonoBehaviour
    {
        // -- bootstrap ----------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoEnsure()
        {
            if (UnityEngine.Object.FindObjectOfType<StatView>() != null) return;
            var go = new GameObject("StatView");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<StatView>();
        }

        // -- state ---------------------------------------------------------------

        private Canvas _canvas;
        private bool _built;
        private int _builtSeed = -1;

        private GameObject _panelRoot;
        private bool _panelOpen;

        // Stat rows: rebuilt on refresh.
        private Transform _rowsRoot;
        private readonly List<RowWidgets> _rows = new List<RowWidgets>();

        // Bar chart (14 slots, built once).
        private const int ChartBars = 14;
        private readonly Image[] _barFills = new Image[ChartBars];
        private readonly Text[] _barLabels = new Text[ChartBars];
        private Text _chartMaxLabel;
        private GameObject _chartEmpty;

        private class RowWidgets
        {
            public Text Label;
            public Text Value;
        }

        // -- palette (matches MilestoneView) -------------------------------------

        private static readonly Color Gold = new Color(0.95f, 0.75f, 0.35f);
        private static readonly Color Cream = new Color(0.88f, 0.83f, 0.72f);
        private static readonly Color Dim = new Color(0.55f, 0.52f, 0.45f);
        private static readonly Color PanelBg = new Color(0.06f, 0.05f, 0.08f, 0.96f);
        private static readonly Color BarColor = new Color(0.85f, 0.62f, 0.30f);
        private static readonly Color BarMaxColor = new Color(1.0f, 0.80f, 0.42f);

        // -- update ----------------------------------------------------------------

        private void Update()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Sim == null) return;
            if (!_built) { Build(); _built = true; }

            // New life (seed change): the stats live on GameState, so they reset
            // with the sim; just refresh the display.
            if (boot.Seed != _builtSeed)
            {
                _builtSeed = boot.Seed;
                if (_panelOpen) RefreshPanel();
            }

            TickPlaytime(boot.Sim.State);
            TickPanelInput();
        }

        /// <summary>
        /// Real playtime + daily buckets. Unity-owned: Core never touches these,
        /// so sim determinism is unaffected.
        /// </summary>
        private void TickPlaytime(GameState s)
        {
            if (s.Stats == null) return;
            var st = s.Stats;
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            if (st.DayDates.Count == 0 || st.DayDates[st.DayDates.Count - 1] != today)
            {
                st.DayDates.Add(today);
                st.DaySeconds.Add(0f);
                while (st.DayDates.Count > 60)
                {
                    st.DayDates.RemoveAt(0);
                    st.DaySeconds.RemoveAt(0);
                }
            }
            float dt = Time.deltaTime;
            st.DaySeconds[st.DaySeconds.Count - 1] += dt;
            st.RealPlaySeconds += dt;
        }

        private void TickPanelInput()
        {
            var hud = GameBootstrap.Instance.Hud;
            if (hud != null && hud.IsTyping) return;

            if (Input.GetKeyDown(KeyCode.G))
                SetPanelOpen(!_panelOpen);
            else if (_panelOpen && Input.GetKeyDown(KeyCode.Escape))
                SetPanelOpen(false);
            if (GamepadInput.IsConnected && _panelOpen && GamepadInput.GetButtonDown(PadButton.B))
                SetPanelOpen(false);
        }

        // -- construction -----------------------------------------------------------

        private void Build()
        {
            var go = new GameObject("StatCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 900; // same tier as milestones gallery
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();

            BuildPanel();
        }

        private static Text MakeText(Transform parent, string name, string text,
                                     int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.55f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return t;
        }

        private static Image MakeImage(Transform parent, string name, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return img;
        }

        private void BuildPanel()
        {
            var root = new GameObject("StatsPanel");
            root.transform.SetParent(_canvas.transform, false);
            _panelRoot = root;

            // Dim backdrop.
            var bg = MakeImage(root.transform, "Backdrop", new Color(0f, 0f, 0f, 0.55f));
            var bgrt = bg.GetComponent<RectTransform>();
            bgrt.anchorMin = Vector2.zero;
            bgrt.anchorMax = Vector2.one;
            bgrt.pivot = new Vector2(0.5f, 0.5f);
            bgrt.sizeDelta = Vector2.zero;
            bgrt.anchoredPosition = Vector2.zero;

            // Centered panel.
            var panel = MakeImage(root.transform, "Panel", PanelBg);
            var prt = panel.GetComponent<RectTransform>();
            prt.sizeDelta = new Vector2(600f, 560f);
            prt.anchoredPosition = Vector2.zero;

            // Gold hairline border.
            var gold = new Color(0.95f, 0.75f, 0.35f, 0.55f);
            AddEdge(root.transform, gold, 600f, 1.5f, 0f, 280f);
            AddEdge(root.transform, gold, 600f, 1.5f, 0f, -280f);
            AddEdge(root.transform, gold, 1.5f, 560f, -300f, 0f);
            AddEdge(root.transform, gold, 1.5f, 560f, 300f, 0f);

            var title = MakeText(root.transform, "Title", "S T A T I S T I C S", 22, Gold,
                                 TextAnchor.MiddleCenter);
            var trt = title.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(560f, 30f);
            trt.anchoredPosition = new Vector2(0f, 242f);

            var sub = MakeText(root.transform, "Subtitle", "the measure of a life", 13, Dim,
                               TextAnchor.MiddleCenter);
            var srt = sub.GetComponent<RectTransform>();
            srt.sizeDelta = new Vector2(560f, 20f);
            srt.anchoredPosition = new Vector2(0f, 216f);

            var hint = MakeText(root.transform, "Hint",
                "G or Esc to close — the world keeps turning", 12, Dim,
                TextAnchor.MiddleCenter);
            var hrt = hint.GetComponent<RectTransform>();
            hrt.sizeDelta = new Vector2(560f, 20f);
            hrt.anchoredPosition = new Vector2(0f, -260f);

            // Scrollable content: stat rows + chart.
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(root.transform, false);
            var srt2 = scrollGo.GetComponent<RectTransform>();
            srt2.anchorMin = new Vector2(0.5f, 0.5f);
            srt2.anchorMax = new Vector2(0.5f, 0.5f);
            srt2.pivot = new Vector2(0.5f, 0.5f);
            srt2.sizeDelta = new Vector2(560f, 430f);
            srt2.anchoredPosition = new Vector2(0f, -18f);
            scrollGo.AddComponent<RectMask2D>();
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(scrollGo.transform, false);
            var crt = contentGo.GetComponent<RectTransform>();
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = new Vector2(24f, -500f);
            crt.offsetMax = new Vector2(-24f, 0f);
            scroll.content = crt;
            _rowsRoot = crt;

            BuildChart(crt);

            root.SetActive(false);
        }

        private static void AddEdge(Transform parent, Color c, float w, float h, float x, float y)
        {
            var go = new GameObject("Edge");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        // -- stat rows ----------------------------------------------------------------

        private void BuildRows(int count)
        {
            // Rows are built once (fixed set); values refresh.
            while (_rows.Count < count)
            {
                var go = new GameObject("Row" + _rows.Count);
                go.transform.SetParent(_rowsRoot, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                // Stacked top-down: each row 26px tall.
                int i = _rows.Count;
                rt.offsetMin = new Vector2(0f, -(i + 1) * 26f);
                rt.offsetMax = new Vector2(0f, -i * 26f);

                var label = MakeText(go.transform, "Label", "", 14, Dim, TextAnchor.MiddleLeft);
                var lrt = label.GetComponent<RectTransform>();
                lrt.anchorMin = new Vector2(0f, 0.5f);
                lrt.anchorMax = new Vector2(0.55f, 0.5f);
                lrt.pivot = new Vector2(0f, 0.5f);
                lrt.sizeDelta = new Vector2(0f, 24f);
                lrt.anchoredPosition = new Vector2(0f, 0f);

                var value = MakeText(go.transform, "Value", "", 14, Cream, TextAnchor.MiddleRight);
                var vrt = value.GetComponent<RectTransform>();
                vrt.anchorMin = new Vector2(0.55f, 0.5f);
                vrt.anchorMax = new Vector2(1f, 0.5f);
                vrt.pivot = new Vector2(1f, 0.5f);
                vrt.sizeDelta = new Vector2(0f, 24f);
                vrt.anchoredPosition = new Vector2(0f, 0f);

                _rows.Add(new RowWidgets { Label = label, Value = value });
            }
        }

        // -- bar chart ------------------------------------------------------------------

        private void BuildChart(Transform content)
        {
            // Chart sits below the rows: rows take 10 * 26 = 260px, then a gap.
            const float chartTop = -286f;

            var title = MakeText(content, "ChartTitle", "PLAYTIME PER DAY", 13, Gold,
                                 TextAnchor.MiddleLeft);
            var trt = title.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.offsetMin = new Vector2(0f, chartTop - 20f);
            trt.offsetMax = new Vector2(0f, chartTop);

            _chartMaxLabel = MakeText(content, "ChartMax", "", 11, Dim, TextAnchor.MiddleRight);
            var mrt = _chartMaxLabel.GetComponent<RectTransform>();
            mrt.anchorMin = new Vector2(0f, 1f);
            mrt.anchorMax = new Vector2(1f, 1f);
            mrt.pivot = new Vector2(1f, 1f);
            mrt.offsetMin = new Vector2(0f, chartTop - 20f);
            mrt.offsetMax = new Vector2(0f, chartTop);

            // Chart plot area: 512 wide, 130 tall for bars.
            const float plotW = 512f;
            const float plotH = 130f;
            const float barW = 28f;
            const float gap = (plotW - ChartBars * barW) / (ChartBars - 1); // ~6.5

            for (int i = 0; i < ChartBars; i++)
            {
                float x = i * (barW + gap) + barW / 2f;

                // Bar container (bottom-aligned).
                var barGo = new GameObject("Bar" + i);
                barGo.transform.SetParent(content, false);
                var brt = barGo.AddComponent<RectTransform>();
                brt.anchorMin = new Vector2(0f, 1f);
                brt.anchorMax = new Vector2(0f, 1f);
                brt.pivot = new Vector2(0.5f, 0f);
                brt.sizeDelta = new Vector2(barW, plotH);
                brt.anchoredPosition = new Vector2(x, chartTop - 24f - plotH);

                // Track (faint background).
                var track = barGo.AddComponent<Image>();
                track.color = new Color(1f, 1f, 1f, 0.05f);

                // Fill (bottom-anchored, height set on refresh).
                var fillGo = new GameObject("Fill");
                fillGo.transform.SetParent(barGo.transform, false);
                var fill = fillGo.AddComponent<Image>();
                fill.color = BarColor;
                var frt = fill.GetComponent<RectTransform>();
                frt.anchorMin = new Vector2(0f, 0f);
                frt.anchorMax = new Vector2(1f, 0f);
                frt.pivot = new Vector2(0.5f, 0f);
                frt.sizeDelta = new Vector2(0f, 0f);
                frt.anchoredPosition = Vector2.zero;
                _barFills[i] = fill;

                // Day label under the bar.
                var lab = MakeText(content, "BarLabel" + i, "", 10, Dim, TextAnchor.MiddleCenter);
                var lrt = lab.GetComponent<RectTransform>();
                lrt.anchorMin = new Vector2(0f, 1f);
                lrt.anchorMax = new Vector2(0f, 1f);
                lrt.pivot = new Vector2(0.5f, 1f);
                lrt.sizeDelta = new Vector2(barW + 10f, 16f);
                lrt.anchoredPosition = new Vector2(x, chartTop - 24f - plotH - 4f);
                _barLabels[i] = lab;
            }

            _chartEmpty = MakeText(content, "ChartEmpty",
                "No playtime recorded yet — the vale awaits.", 13, Dim,
                TextAnchor.MiddleCenter).gameObject;
            var ert = _chartEmpty.GetComponent<RectTransform>();
            ert.anchorMin = new Vector2(0f, 1f);
            ert.anchorMax = new Vector2(1f, 1f);
            ert.pivot = new Vector2(0.5f, 1f);
            ert.offsetMin = new Vector2(0f, chartTop - 120f);
            ert.offsetMax = new Vector2(0f, chartTop - 60f);
        }

        // -- panel ------------------------------------------------------------------------

        private void SetPanelOpen(bool open)
        {
            _panelOpen = open;
            _panelRoot.SetActive(open);
            if (open) RefreshPanel();
        }

        private void RefreshPanel()
        {
            var boot = GameBootstrap.Instance;
            if (boot == null || boot.Sim == null) return;
            var st = boot.Sim.State.Stats;
            if (st == null) return;

            string[] labels = {
                "Time in the vale",
                "Distance traveled",
                "Places discovered",
                "Generations",
                "Kits born",
                "Predators defeated",
                "Greetings shared",
                "Tales distilled",
                "Dreams",
                "Farthest from den",
            };
            string[] values = {
                FormatDuration(st.RealPlaySeconds),
                FormatDistance(st.DistanceTraveled),
                st.PoisDiscovered.ToString(),
                st.Generations.ToString(),
                st.KitsBorn.ToString(),
                st.PredatorsDefeated.ToString(),
                st.Greetings.ToString(),
                st.TalesDistilled.ToString(),
                st.DreamsDreamed + " dreamed · " + st.DreamsFulfilled + " fulfilled",
                FormatDistance(st.MaxDistanceFromDen),
            };

            BuildRows(labels.Length);
            for (int i = 0; i < labels.Length; i++)
            {
                _rows[i].Label.text = labels[i];
                _rows[i].Value.text = values[i];
            }

            RefreshChart(st);
        }

        private void RefreshChart(StatState st)
        {
            int n = st.DayDates.Count;
            int take = Math.Min(n, ChartBars);
            bool anyData = false;
            float max = 1f;
            for (int i = n - take; i < n; i++)
            {
                if (st.DaySeconds[i] > 0.5f) anyData = true;
                if (st.DaySeconds[i] > max) max = st.DaySeconds[i];
            }

            _chartEmpty.SetActive(!anyData);
            _chartMaxLabel.text = anyData ? "max " + FormatDuration(max) : "";

            for (int i = 0; i < ChartBars; i++)
            {
                int src = n - take + i;
                float frac = 0f;
                string dayLabel = "";
                bool isMax = false;
                if (src >= 0 && src < n)
                {
                    float secs = st.DaySeconds[src];
                    frac = Math.Min(1f, secs / max);
                    if (frac <= 0f && secs > 0f) frac = 0.03f; // sliver for tiny days
                    dayLabel = DayOfMonth(st.DayDates[src]);
                    isMax = anyData && Math.Abs(secs - max) < 0.01f && secs > 0.5f;
                }
                var frt = _barFills[i].GetComponent<RectTransform>();
                frt.sizeDelta = new Vector2(0f, frac * 130f);
                _barFills[i].color = isMax ? BarMaxColor : BarColor;
                _barFills[i].gameObject.SetActive(frac > 0f);
                _barLabels[i].text = dayLabel;
            }
        }

        private static string DayOfMonth(string yyyyMmDd)
        {
            // "2026-10-06" -> "06"
            if (yyyyMmDd != null && yyyyMmDd.Length >= 10)
                return yyyyMmDd.Substring(8, 2);
            return "";
        }

        // -- formatting ---------------------------------------------------------------------

        private static string FormatDuration(float seconds)
        {
            if (seconds < 60f) return Math.Max(1, (int)seconds) + "s";
            if (seconds < 3600f)
            {
                int m = (int)(seconds / 60f);
                return m + "m";
            }
            if (seconds < 86400f)
            {
                int h = (int)(seconds / 3600f);
                int m = (int)((seconds % 3600f) / 60f);
                return m > 0 ? h + "h " + m + "m" : h + "h";
            }
            int d = (int)(seconds / 86400f);
            int h2 = (int)((seconds % 86400f) / 3600f);
            return h2 > 0 ? d + "d " + h2 + "h" : d + "d";
        }

        private static string FormatDistance(float meters)
        {
            if (meters < 1000f) return ((int)meters) + " m";
            float km = meters / 1000f;
            return km.ToString("0.0") + " km";
        }
    }
}
