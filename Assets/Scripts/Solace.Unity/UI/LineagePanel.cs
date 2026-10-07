// Solace.Unity — the Bloodline panel (L): a family tree of every fox who has
// lived, their lifespans, causes of death, notable moments, and tales.
// The lineage is the game; this is the legacy view — Lewis should feel proud
// of the dynasty. View only: no sim changes.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Solace.Core;

namespace Solace.Unity
{
    public class LineagePanel : PanelBase
    {
        private RectTransform _content;
        private ScrollRect _scroll;
        private int _builtGen = -1;
        private int _builtChapters = -1;

        public override void Build(HudController hud)
        {
            base.Build(hud);
            Root = MakeRoot("The Bloodline", 860f, 640f, UiKit.Parchment);

            var scrollRt = UiKit.Rect(Root, "Scroll", 0f, 0f, 1f, 1f, 12f, 12f, -12f, -58f);
            scrollRt.gameObject.AddComponent<RectMask2D>();
            _scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
            _scroll.horizontal = false;

            _content = UiKit.Rect(scrollRt, "Content", 0f, 1f, 1f, 1f, 0f, 0f, 0f, 0f);
            _content.pivot = new Vector2(0.5f, 1f);
            var layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 10f;
            layout.padding = new RectOffset(12, 12, 10, 10);
            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;
        }

        protected override void OnOpen() { Rebuild(); }

        private void Update()
        {
            if (!IsOpen) return;
            var sim = GameBootstrap.Instance != null ? GameBootstrap.Instance.Sim : null;
            if (sim == null) return;
            int g = sim.State.Lineage.Generation;
            int c = sim.State.Lineage.Chapters.Count;
            if (g != _builtGen || c != _builtChapters) Rebuild();
        }

        // -- data ---------------------------------------------------------------

        private class GenInfo
        {
            public int Generation;
            public string Name = "?";
            public string Epithet = "";
            public float Years;          // lifespan in game-years
            public string Cause = "";    // "old age", "gloom-maw", ...
            public Color Shade = new Color(0.95f, 0.75f, 0.35f);
            public bool IsCurrent;
            public List<JournalEntry> Moments = new List<JournalEntry>();
            public List<Tale> Tales = new List<Tale>();
        }

        private static string Clean(string s)
        {
            return s.Replace("<", "").Replace(">", "");
        }

        /// <summary>"Chapter 2 ends: Ash dimmed..." -> "Ash". Handles "Ash the Bold".</summary>
        private static void ParseName(string text, out string name, out string epithet)
        {
            name = "?"; epithet = "";
            if (string.IsNullOrEmpty(text)) return;
            int idx = text.IndexOf("ends:");
            bool begins = false;
            if (idx < 0) { idx = text.IndexOf("begins:"); begins = true; }
            if (idx < 0) return;
            string rest = text.Substring(idx + (begins ? 7 : 5)).Trim();
            var parts = rest.Split(' ');
            if (parts.Length == 0) return;
            name = parts[0];
            // Epithet: "the Bold", "the Timid", ...
            if (parts.Length >= 3 && parts[1] == "the")
                epithet = "the " + parts[2].TrimEnd('.', ',', ';');
        }

        private static Color ShadeColor(string shadeWord)
        {
            // Matches LineageSystem.ShadeWord.
            switch (shadeWord)
            {
                case "ember-gold": return new Color(1.00f, 0.62f, 0.18f);
                case "harvest-amber": return new Color(0.95f, 0.72f, 0.30f);
                case "pale honey": return new Color(0.98f, 0.85f, 0.55f);
                case "mist-green": return new Color(0.55f, 0.80f, 0.60f);
                case "moonlit blue": return new Color(0.55f, 0.70f, 1.00f);
                default: return new Color(0.95f, 0.75f, 0.35f);
            }
        }

        /// <summary>"her light the color of ember-gold" -> shade color.</summary>
        private static Color ParseShade(string text)
        {
            if (string.IsNullOrEmpty(text)) return ShadeColor("");
            int idx = text.IndexOf("the color of ");
            if (idx < 0) return ShadeColor("");
            string rest = text.Substring(idx + 13).Trim().TrimEnd('.', ',', ';');
            return ShadeColor(rest);
        }

        private List<GenInfo> BuildGenerations(GameState s)
        {
            var gens = new List<GenInfo>();
            var chapters = s.Lineage.Chapters;

            // Index chapter-boundary journal entries by generation for names.
            var endsByGen = new Dictionary<int, string>();
            var beginsByGen = new Dictionary<int, string>();
            foreach (var e in s.Journal.Entries)
            {
                if (e.Category != JournalCategory.Chapter) continue;
                if (e.Text.Contains("ends:")) endsByGen[e.Generation] = e.Text;
                else if (e.Text.Contains("begins:")) beginsByGen[e.Generation] = e.Text;
            }

            // Closed chapters.
            foreach (var ch in chapters)
            {
                var g = new GenInfo { Generation = ch.Generation };
                float secs = Mathf.Max(0f, ch.EndTime - ch.StartTime);
                g.Years = secs / 86400f / LineageSystem.DaysPerYear;
                g.Cause = string.IsNullOrEmpty(ch.Cause) ? "unknown" : ch.Cause;
                string endText;
                if (endsByGen.TryGetValue(ch.Generation, out endText))
                {
                    string n, ep;
                    ParseName(endText, out n, out ep);
                    g.Name = n; g.Epithet = ep;
                }
                string begText;
                if (beginsByGen.TryGetValue(ch.Generation, out begText))
                    g.Shade = ParseShade(begText);
                CollectMoments(s, g);
                CollectTales(s, g);
                gens.Add(g);
            }

            // Current generation (full live data).
            var cur = new GenInfo { Generation = s.Lineage.Generation, IsCurrent = true };
            var a = s.Agent;
            if (a != null)
            {
                cur.Name = string.IsNullOrEmpty(a.Name) ? "?" : a.Name;
                cur.Epithet = a.Traits != null ? a.Traits.Epithet() : "";
                float secs = (float)s.ElapsedSeconds - s.Lineage.ChapterStartTime;
                cur.Years = Mathf.Max(0f, secs) / 86400f / LineageSystem.DaysPerYear;
                cur.Shade = ShadeFromFloat(a.LightShade);
                cur.Cause = "living";
            }
            CollectMoments(s, cur);
            CollectTales(s, cur);
            gens.Add(cur);

            return gens;
        }

        private static Color ShadeFromFloat(float shade)
        {
            // Approximate the ShadeWord bands as a gradient.
            if (shade < 0.2f) return ShadeColor("ember-gold");
            if (shade < 0.4f) return ShadeColor("harvest-amber");
            if (shade < 0.6f) return ShadeColor("pale honey");
            if (shade < 0.8f) return ShadeColor("mist-green");
            return ShadeColor("moonlit blue");
        }

        private static void CollectMoments(GameState s, GenInfo g)
        {
            var cands = new List<JournalEntry>();
            foreach (var e in s.Journal.Entries)
            {
                if (e.Generation != g.Generation) continue;
                if (e.Category == JournalCategory.Chapter) continue;
                if (e.Category == JournalCategory.System) continue;
                cands.Add(e);
            }
            cands.Sort((a, b) =>
            {
                int c = b.Salience.CompareTo(a.Salience);
                return c != 0 ? c : b.Time.CompareTo(a.Time);
            });
            for (int i = 0; i < cands.Count && g.Moments.Count < 4; i++)
                g.Moments.Add(cands[i]);
        }

        private static void CollectTales(GameState s, GenInfo g)
        {
            foreach (var t in s.Lineage.Tales)
                if (t.OriginGeneration == g.Generation)
                    g.Tales.Add(t);
        }

        // -- stats ----------------------------------------------------------------

        private string BuildStats(List<GenInfo> gens, GameState s)
        {
            var sb = new StringBuilder(512);
            int closed = 0;
            float longest = 0f, shortest = float.MaxValue;
            string longestName = "";
            var causes = new Dictionary<string, int>();
            foreach (var g in gens)
            {
                if (g.IsCurrent) continue;
                closed++;
                if (g.Years > longest) { longest = g.Years; longestName = g.Name; }
                if (g.Years < shortest) shortest = g.Years;
                int n;
                causes.TryGetValue(g.Cause, out n);
                causes[g.Cause] = n + 1;
            }
            int tales = s.Lineage.Tales.Count;
            int kits = 0;
            foreach (var k in s.Kits) if (k.IsAlive) kits++;

            sb.Append(UiKit.Spaced("DYNASTY")).Append('\n');
            sb.Append("Generations: ").Append(gens.Count);
            sb.Append("   ·   Tales kept: ").Append(tales);
            if (closed > 0)
            {
                sb.Append("\nLongest life: ").Append(longest.ToString("F1")).Append(" yrs");
                if (!string.IsNullOrEmpty(longestName)) sb.Append(" (").Append(longestName).Append(')');
                sb.Append("   ·   Briefest: ").Append(shortest.ToString("F1")).Append(" yrs");
            }
            if (kits > 0) sb.Append("\nLiving kits at the den: ").Append(kits);
            return sb.ToString();
        }

        // -- ui -------------------------------------------------------------------

        private void AddStatsCard(string stats)
        {
            var t = UiKit.Label(_content, "Stats", stats, 14, UiKit.ParchmentInk, TextAnchor.UpperLeft);
            t.lineSpacing = 1.35f;
            UiKit.AddShadow(t, 0.4f);
            var f = t.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UiKit.Divider(_content, "StatsRule", new Color(0.72f, 0.55f, 0.28f, 0.45f),
                0f, 0f, 1f, 0f, 12f, 0f, -12f, 1f);
        }

        /// <summary>Geometric fox mark: a diamond in the light-shade with a glowing core.</summary>
        private void AddPortrait(Transform row, Color shade, string name)
        {
            var box = UiKit.Rect(row, "Portrait", 0f, 1f, 0f, 1f, 0f, -64f, 64f, 0f);
            // Diamond (rotated square).
            var dia = box.gameObject.AddComponent<Image>();
            dia.sprite = UiKit.DotSprite();
            dia.color = shade;
            dia.raycastTarget = false;
            var drt = dia.GetComponent<RectTransform>();
            drt.anchorMin = new Vector2(0.5f, 0.5f); drt.anchorMax = new Vector2(0.5f, 0.5f);
            drt.offsetMin = new Vector2(-20f, -20f); drt.offsetMax = new Vector2(20f, 20f);
            drt.rotation = Quaternion.Euler(0f, 0f, 45f);
            // Glowing core.
            var core = new GameObject("Core");
            var crt = core.AddComponent<RectTransform>();
            crt.SetParent(box, false);
            crt.anchorMin = new Vector2(0.5f, 0.5f); crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.offsetMin = new Vector2(-7f, -7f); crt.offsetMax = new Vector2(7f, 7f);
            var cimg = core.AddComponent<Image>();
            cimg.sprite = UiKit.SoftSprite();
            cimg.color = Color.white;
            cimg.raycastTarget = false;
            // Initial.
            var init = UiKit.Label(box, "Initial", name.Length > 0 ? name.Substring(0, 1) : "?",
                18, Color.white, TextAnchor.MiddleCenter);
            UiKit.AddShadow(init, 0.6f);
        }

        private void AddGenerationCard(GenInfo g)
        {
            var card = new GameObject("Gen" + g.Generation);
            var cardRt = card.AddComponent<RectTransform>();
            cardRt.SetParent(_content, false);
            var bg = card.AddComponent<Image>();
            bg.color = g.IsCurrent
                ? new Color(0.20f, 0.15f, 0.08f, 0.85f)
                : new Color(0.10f, 0.08f, 0.06f, 0.55f);
            bg.raycastTarget = false;
            // Fixed internal layout: header (72px) + body stacked manually.
            // We compute heights as we go instead of nested layout groups.
            float y = 0f; // distance from top (negative downward)

            // Header: portrait + name block.
            var header = UiKit.Rect(cardRt, "Header", 0f, 1f, 1f, 1f, 0f, -72f, 0f, 0f);
            AddPortrait(header, g.Shade, g.Name);

            string title = "Generation " + UiKit.Roman(g.Generation);
            if (g.IsCurrent) title += "  ·  <color=#F2BF59>living</color>";
            var titleT = UiKit.Label(header, "Title", title, 13, UiKit.GoldDim, TextAnchor.UpperLeft);
            var trt = titleT.GetComponent<RectTransform>();
            trt.offsetMin = new Vector2(74f, -24f); trt.offsetMax = new Vector2(-8f, 0f);

            string displayName = g.Name;
            if (!string.IsNullOrEmpty(g.Epithet)) displayName += " " + g.Epithet;
            var nameT = UiKit.Label(header, "Name", Clean(displayName), 20, UiKit.ParchmentInk, TextAnchor.UpperLeft);
            UiKit.AddShadow(nameT, 0.4f);
            var nrt = nameT.GetComponent<RectTransform>();
            nrt.offsetMin = new Vector2(74f, -52f); nrt.offsetMax = new Vector2(-8f, -24f);
            y = -72f;

            // Meta line.
            string meta = g.Years.ToString("F1") + " yrs";
            meta += g.IsCurrent ? " so far" : "  ·  " + Clean(g.Cause);
            y = AddBodyLabel(cardRt, "Meta", meta, 12, UiKit.DimInk, y, 74f);

            // Notable moments.
            if (g.Moments.Count > 0)
            {
                y = AddBodyLabel(cardRt, "MomentsH", UiKit.Spaced("REMEMBERED"), 11, UiKit.GoldDim, y - 4f, 10f);
                var sb = new StringBuilder(1024);
                foreach (var e in g.Moments)
                {
                    sb.Append("<color=#B2A68C>").Append(UiKit.FormatGameTime(e.Time)).Append("</color>  ");
                    sb.Append(Clean(e.Text)).Append("\n");
                }
                y = AddBodyLabel(cardRt, "Moments", sb.ToString().TrimEnd('\n'), 13, UiKit.ParchmentInk, y, 10f, 1.3f);
            }

            // Tales.
            if (g.Tales.Count > 0)
            {
                var sb = new StringBuilder(512);
                sb.Append(UiKit.Spaced("TALES")).Append('\n');
                foreach (var t in g.Tales)
                    sb.Append("<i>“").Append(Clean(t.Title)).Append("”</i>").Append('\n');
                y = AddBodyLabel(cardRt, "Tales", sb.ToString().TrimEnd('\n'), 13,
                    new Color(0.72f, 0.62f, 0.95f), y - 4f, 10f, 1.3f);
            }

            // Size the card to fit content.
            float cardH = -y + 10f;
            var le = card.AddComponent<LayoutElement>();
            le.preferredHeight = cardH;
            // Also set explicit size for safety.
            cardRt.anchorMin = new Vector2(0f, 1f); cardRt.anchorMax = new Vector2(1f, 1f);
            cardRt.pivot = new Vector2(0.5f, 1f);

            // Connector line to next generation (visual tree feel).
            var conn = UiKit.Rect(_content, "Conn" + g.Generation, 0.5f, 0f, 0.5f, 0f, -1f, -6f, 1f, 4f);
            var cimg = conn.gameObject.AddComponent<Image>();
            cimg.color = new Color(0.72f, 0.55f, 0.28f, 0.5f);
            cimg.raycastTarget = false;
            var cle = conn.gameObject.AddComponent<LayoutElement>();
            cle.preferredHeight = 10f;
        }

        /// <summary>Adds a body label under the card, returns new y (negative).</summary>
        private float AddBodyLabel(Transform cardRt, string name, string text, int size,
                                   Color color, float y, float leftPad, float lineSpacing = 1f)
        {
            var t = UiKit.Label(cardRt, name, text, size, color, TextAnchor.UpperLeft);
            t.lineSpacing = lineSpacing;
            var rt = t.GetComponent<RectTransform>();
            // Measure: use preferred height via a temporary layout pass.
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(leftPad, y - 1000f);
            rt.offsetMax = new Vector2(-10f, y);
            // Force a layout calculation.
            Canvas.ForceUpdateCanvases();
            float h = t.preferredHeight;
            rt.offsetMin = new Vector2(leftPad, y - h);
            rt.offsetMax = new Vector2(-10f, y);
            return y - h - 4f;
        }

        private void Rebuild()
        {
            var sim = GameBootstrap.Instance.Sim;
            if (sim == null) return;
            _builtGen = sim.State.Lineage.Generation;
            _builtChapters = sim.State.Lineage.Chapters.Count;

            for (int i = _content.childCount - 1; i >= 0; i--)
                Object.Destroy(_content.GetChild(i).gameObject);

            var gens = BuildGenerations(sim.State);
            AddStatsCard(BuildStats(gens, sim.State));
            foreach (var g in gens)
                AddGenerationCard(g);

            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 1f;
        }
    }
}
