// Solace.Unity — the Codex (X): an in-game encyclopedia of the vale.
// Entries unlock as Lewis discovers things: creatures met, places found,
// phenomena witnessed. Locked secrets show as "???" to tease.
// View only: all unlock state is derived from existing sim state, no sim changes.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Solace.Core;

namespace Solace.Unity
{
    public class CodexPanel : PanelBase
    {
        private RectTransform _content;
        private ScrollRect _scroll;
        private string _builtKey = "";

        // -- entry definitions --------------------------------------------------------

        private class Entry
        {
            public string Id;
            public string Section;   // "Creatures", "Places", "Phenomena"
            public string Name;
            public string Lore;
            public Color Accent;
            public bool IsSecret;
            public string Tease;     // shown when a secret is still locked
        }

        private static readonly Entry[] Entries = new Entry[]
        {
            // -- creatures ---------------------------------------------------------
            new Entry { Id = "fox", Section = "Creatures", Name = "The Lantern-Fox",
                Accent = new Color(1.00f, 0.62f, 0.18f),
                Lore = "We are the last light-keepers of the vale. Our chests hold a coal of the old sun, " +
                       "and it burns brighter when we are brave and dims when we are afraid. No human ever " +
                       "named us. We named ourselves, in the dark, by the light we carry." },
            new Entry { Id = "kindred", Section = "Creatures", Name = "The Kindred",
                Accent = new Color(0.95f, 0.75f, 0.35f),
                Lore = "Others of our kind walk the far trails — lean shadows with dimmer coals. They do not " +
                       "speak as we do, but they understand greeting, and grief, and the warmth of walking " +
                       "together. Some become more than strangers. Some become family." },
            new Entry { Id = "deer", Section = "Creatures", Name = "The Pale Deer",
                Accent = new Color(0.75f, 0.70f, 0.60f),
                Lore = "They move like water over stone, and when they flee they fly — all four hooves leaving " +
                       "the earth at once, in a leap called stotting. It means: I am so fast I can afford to " +
                       "show off. The young ones do it for joy." },
            new Entry { Id = "rabbit", Section = "Creatures", Name = "The Burrowers",
                Accent = new Color(0.80f, 0.72f, 0.62f),
                Lore = "Small thunder in the grass. They live beneath the world and pop up like questions, " +
                       "ears first. The kits love to chase them and never once catch them, which is probably " +
                       "for the best." },
            new Entry { Id = "gloommaw", Section = "Creatures", Name = "The Gloom-Maw",
                Accent = new Color(0.55f, 0.15f, 0.20f),
                Lore = "It hunts the way night hunts — without hurry and without mercy. Its eyes are embers " +
                       "in the dark, and its jaw hangs open when it has your scent. Stand your ground only if " +
                       "you must. Run if you can. Grieve after." },
            new Entry { Id = "skybird", Section = "Creatures", Name = "The Sky-Hunters",
                Accent = new Color(0.45f, 0.55f, 0.70f),
                Lore = "They draw their maps on the sky and never get lost. At dawn they sing the sun up, " +
                       "quarreling and joyful, and at dusk they stitch the day closed. When the fox runs " +
                       "beneath them, they scatter like thrown coins." },
            new Entry { Id = "butterfly", Section = "Creatures", Name = "The Petal-Wings",
                Accent = new Color(0.95f, 0.65f, 0.75f),
                Lore = "They live for a single summer and spend it drunk on flowers — amber, azure, rose, " +
                       "flickering over the meadows like the vale is thinking in color. They are not afraid " +
                       "of the fox. They have no time to be." },
            new Entry { Id = "riverfish", Section = "Creatures", Name = "The River-Shadows",
                Accent = new Color(0.30f, 0.50f, 0.60f),
                Lore = "Dark shapes beneath the jewel water, gliding along the river's own road. Sometimes " +
                       "one leaps — a silver arc, a ring spreading — as if the river were dreaming and " +
                       "startled itself awake." },

            // -- places ------------------------------------------------------------
            new Entry { Id = "den", Section = "Places", Name = "The Den",
                Accent = new Color(0.85f, 0.65f, 0.35f),
                Lore = "Home is a hole in the warm earth, lined with old moss and older stories. Every fox " +
                       "of the line was born here, in the dark, under the weight of the hill. It smells of " +
                       "milk and rain and safety." },
            new Entry { Id = "ruin", Section = "Places", Name = "The Hollow Hive",
                Accent = new Color(0.60f, 0.55f, 0.45f),
                Lore = "Chitin arches on the high fell, empty as sky. Something built here long before our " +
                       "kind — something with too many legs and too much patience — and left nothing but " +
                       "shape. The wind moves through it like breath through a skull. We do not know what " +
                       "they were. We know they are gone, and the knowing feels like a cold paw on the spine." },
            new Entry { Id = "cairn", Section = "Places", Name = "The Fork Cairn",
                Accent = new Color(0.65f, 0.62f, 0.58f),
                Lore = "At the parting of trails, our kind have piled stones for longer than any tale " +
                       "remembers. Each fox adds one. It is not a marker. It is a way of saying: I was here. " +
                       "I chose a path. Remember me when you choose yours." },
            new Entry { Id = "ember", Section = "Places", Name = "Ember-Hollow",
                Accent = new Color(1.00f, 0.55f, 0.25f),
                Lore = "A dip in the earth where glow-moss grows thick as fur. It holds the day's warmth " +
                       "long into the night, and the light there is the color of banked coals. A good place " +
                       "to rest. A good place to heal." },
            new Entry { Id = "glowberry", Section = "Places", Name = "Glowberry Thicket",
                Accent = new Color(0.45f, 0.85f, 0.45f),
                Lore = "The berries hang lit from within, each one a tiny lantern. They taste of sun and " +
                       "rain. In the hungry months, these bushes are the difference between a story and " +
                       "a silence." },
            new Entry { Id = "ruinsite", Section = "Places", Name = "Husk Circles",
                Accent = new Color(0.55f, 0.50f, 0.42f),
                Lore = "Lesser ruins — rings of husk, old burrow-arches, fragments of the great mystery. The " +
                       "old ones' leavings, scattered like a sentence with half the words missing. We walk " +
                       "them carefully, and wonder." },
            new Entry { Id = "overlook", Section = "Places", Name = "The Overlook",
                Accent = new Color(0.55f, 0.70f, 0.90f),
                Lore = "Climb until the whole vale lies below — river, loch, den, and all. The wind up there " +
                       "tastes like distance. Foxes come here to think, or to grieve, or simply to remember " +
                       "how big the world is." },
            new Entry { Id = "crystal", Section = "Places", Name = "The Singing Deep",
                Accent = new Color(0.45f, 0.65f, 1.00f),
                Lore = "A throat in the rock lined with living glass. At night the stones hum faint blue " +
                       "light, like the sky fell in and kept shining. It is dry here, and hidden, and calm. " +
                       "A good place to vanish when the world is too loud." },
            new Entry { Id = "hotspring", Section = "Places", Name = "The Warm Vein",
                Accent = new Color(0.35f, 0.85f, 0.80f),
                Lore = "Water rising warm out of the cold earth, breathing steam into the morning. Stand in " +
                       "it to the belly and feel the bones unclench. The old ones must have known this place. " +
                       "The very old ones. The steam remembers them." },
            new Entry { Id = "hollowlog", Section = "Places", Name = "The Fallen Giant",
                Accent = new Color(0.55f, 0.42f, 0.28f),
                Lore = "A tree that stood for a hundred years and fell in a single storm, hollowed by years " +
                       "into a tunnel just fox-sized. It smells of rain and mushrooms. The kits love it here. " +
                       "Everything echoes, and the echoes sound like laughter." },
            new Entry { Id = "rainbow", Section = "Places", Name = "The Rainbow Grove", IsSecret = true,
                Tease = "Somewhere in the deep pines, light sleeps in the moss, waiting for a fox who wanders " +
                        "for the love of wandering.",
                Accent = new Color(0.90f, 0.50f, 0.90f),
                Lore = "A grove of crystal, every color ever seen and some not, growing out of the moss like " +
                       "frozen light. They hum when the wind touches them — a chord so pure it hurts, in the " +
                       "good way. This place was waiting. It had been waiting a very long time." },

            // -- phenomena ---------------------------------------------------------
            new Entry { Id = "colossi", Section = "Phenomena", Name = "The Walking Trees",
                Accent = new Color(0.40f, 0.60f, 0.40f),
                Lore = "They are older than the hills they walk. Kilometer-tall, slow as glaciers, they " +
                       "migrate across the vale on roots like rivers, and where they pass, the earth remembers " +
                       "their weight for years. Birds nest in their crowns. Seeds fall from them like slow " +
                       "rain. No one knows where they are going. Perhaps neither do they." },
            new Entry { Id = "spring", Section = "Phenomena", Name = "Spring — The Waking",
                Accent = new Color(0.55f, 0.85f, 0.50f),
                Lore = "The snow lets go. Petals fall like slow snow in reverse. The world inhales, and " +
                       "everything green pushes up at once, urgent and tender. Kits are born now, when the " +
                       "world is soft enough to land in." },
            new Entry { Id = "summer", Section = "Phenomena", Name = "Summer — The Abundance",
                Accent = new Color(1.00f, 0.80f, 0.35f),
                Lore = "Long light, fat berries, warm rivers. The fox grows sleek and the kits grow bold. It " +
                       "feels like it will never end, which is exactly why the old foxes eat extra and say " +
                       "nothing." },
            new Entry { Id = "autumn", Section = "Phenomena", Name = "Autumn — The Change",
                Accent = new Color(0.90f, 0.55f, 0.25f),
                Lore = "The pines go amber. Leaves fall like the year's slow applause. The air turns sharp " +
                       "and honest. Everything prepares, in its own way, for the white silence." },
            new Entry { Id = "winter", Section = "Phenomena", Name = "Winter — The White Silence",
                Accent = new Color(0.75f, 0.82f, 0.95f),
                Lore = "Thirteen-hour nights. The fox's light matters most now — a small sun walking through " +
                       "the dark, and the dark is very large. Food is scarce. Rest is deep. Those who see " +
                       "spring again have earned it." },
            new Entry { Id = "dreams", Section = "Phenomena", Name = "Prophetic Dreams",
                Accent = new Color(0.65f, 0.55f, 0.95f),
                Lore = "Sometimes, asleep in the den, the fox dreams of places it has never been — a river " +
                       "flowing backward, snow falling upward — and wakes with the taste of elsewhere in its " +
                       "mouth. And sometimes, impossibly, the dream is true. The world dreams too, and " +
                       "sometimes it dreams us." },
            new Entry { Id = "seedisles", Section = "Phenomena", Name = "The Seed-Isles",
                Accent = new Color(0.60f, 0.75f, 0.55f),
                Lore = "They broke off the walking trees a thousand years ago and never landed — islands of " +
                       "earth and moss adrift on the wind, trailing seeds like slow comets. The old tales say " +
                       "the whole vale was once like this: unmoored, wandering, free." },
            new Entry { Id = "spirit", Section = "Phenomena", Name = "The Pale Watcher", IsSecret = true,
                Tease = "On full-moon nights, something pale walks the high ridges, watching.",
                Accent = new Color(0.85f, 0.90f, 1.00f),
                Lore = "It stands on the ridge in the moonlight — a fox, but made of moonlight, pale and " +
                       "unhurried. It watches. Then it sinks into the ground like water into sand, and is " +
                       "gone. The old tales do not explain it. Some things are not for explaining." },
            new Entry { Id = "meteor", Section = "Phenomena", Name = "Skyfall", IsSecret = true,
                Tease = "Sometimes the sky falls in pieces. It is beautiful, and it means nothing, and it " +
                        "means everything.",
                Accent = new Color(1.00f, 0.85f, 0.50f),
                Lore = "On rare clear nights, the sky comes apart — white-gold fire tearing across the dark, " +
                       "there and gone. The fox watched once, head tilted back, and wrote in the chronicle: " +
                       "'Tonight the sky fell in pieces and it was beautiful.' Nothing more needed saying." },
        };

        private static readonly string[] SectionOrder = new string[] { "Creatures", "Places", "Phenomena" };

        // -- unlock state (view-side, derived from existing sim state) --------------------

        private struct Unlock
        {
            public bool IsUnlocked;
            public float Time; // game seconds first noted; 0 = the first dawn
        }

        private static string PoiEntryId(PoiType t)
        {
            switch (t)
            {
                case PoiType.Den: return "den";
                case PoiType.InsectileRuin: return "ruin";
                case PoiType.Cairn: return "cairn";
                case PoiType.EmberHollow: return "ember";
                case PoiType.GlowberryBush: return "glowberry";
                case PoiType.RuinSite: return "ruinsite";
                case PoiType.Overlook: return "overlook";
                case PoiType.CrystalCave: return "crystal";
                case PoiType.HotSpring: return "hotspring";
                case PoiType.HollowLog: return "hollowlog";
                case PoiType.RainbowGrove: return "rainbow";
                default: return "";
            }
        }

        private static void SetEarliest(Dictionary<string, Unlock> u, string id, float time)
        {
            Unlock cur;
            if (u.TryGetValue(id, out cur))
            {
                if (time < cur.Time) { cur.Time = time; u[id] = cur; }
            }
            else u[id] = new Unlock { IsUnlocked = true, Time = time };
        }

        private static float EarliestDiscoveryTime(GameState s, int poiId)
        {
            float best = float.MaxValue;
            foreach (var e in s.Journal.Entries)
            {
                if (e.Category != JournalCategory.Discovery) continue;
                if (!e.PlaceId.HasValue || e.PlaceId.Value != poiId) continue;
                if (e.Time < best) best = e.Time;
            }
            return best == float.MaxValue ? (float)s.ElapsedSeconds : best;
        }

        private static float EarliestDreamTime(GameState s)
        {
            float best = float.MaxValue;
            foreach (var e in s.Journal.Entries)
            {
                if (e.Category != JournalCategory.Dream) continue;
                if (e.Time < best) best = e.Time;
            }
            return best;
        }

        private Dictionary<string, Unlock> ComputeUnlocks(GameState s)
        {
            var u = new Dictionary<string, Unlock>();

            // Always-known: the self, the home, the ambient world.
            SetEarliest(u, "fox", 0f);
            SetEarliest(u, "den", 0f);
            SetEarliest(u, "skybird", 0f);
            SetEarliest(u, "butterfly", 0f);
            SetEarliest(u, "riverfish", 0f);
            SetEarliest(u, "colossi", 0f);
            SetEarliest(u, "seedisles", 0f);

            // Places: discovered POIs.
            foreach (var poi in s.World.Pois)
            {
                if (!poi.Discovered) continue;
                string id = PoiEntryId(poi.Type);
                if (string.IsNullOrEmpty(id)) continue;
                SetEarliest(u, id, EarliestDiscoveryTime(s, poi.Id));
            }

            // Kindred: met someone.
            float firstMet = float.MaxValue;
            foreach (var p in s.Social.People)
                if (p.LastMet < firstMet) firstMet = p.LastMet;
            if (firstMet < float.MaxValue) SetEarliest(u, "kindred", firstMet);

            // Wildlife: currently in the world.
            float now = (float)s.ElapsedSeconds;
            foreach (var e in s.Entities)
            {
                if (!e.IsAlive) continue;
                if (e.Kind == EntityKind.Deer) SetEarliest(u, "deer", now);
                else if (e.Kind == EntityKind.Rabbit) SetEarliest(u, "rabbit", now);
                else if (e.Kind == EntityKind.Predator) SetEarliest(u, "gloommaw", now);
            }
            // Gloom-maw also unlocks via combat history.
            if (s.Milestones.TotalKills > 0)
            {
                float t = now;
                for (int i = 0; i < s.Milestones.UnlockedIds.Count; i++)
                    if (s.Milestones.UnlockedIds[i] == "first_blood") { t = s.Milestones.UnlockedAt[i]; break; }
                SetEarliest(u, "gloommaw", t);
            }

            // Seasons: survived or current.
            string cur = SeasonSystem.Current(s).ToString();
            foreach (var sn in s.Milestones.SeasonsSurvived)
            {
                string sid = SeasonEntryId(sn);
                if (!string.IsNullOrEmpty(sid)) SetEarliest(u, sid, now);
            }
            string curId = SeasonEntryId(cur);
            if (!string.IsNullOrEmpty(curId)) SetEarliest(u, curId, now);

            // Dreams.
            float dreamT = EarliestDreamTime(s);
            if (dreamT < float.MaxValue) SetEarliest(u, "dreams", dreamT);

            // Secrets.
            if (s.Eggs.SpiritSeen) SetEarliest(u, "spirit", now);
            if (s.Eggs.MeteorSeen) SetEarliest(u, "meteor", now);

            return u;
        }

        private static string SeasonEntryId(string seasonName)
        {
            switch (seasonName)
            {
                case "Spring": return "spring";
                case "Summer": return "summer";
                case "Autumn": return "autumn";
                case "Winter": return "winter";
                default: return "";
            }
        }

        // -- ui -------------------------------------------------------------------

        public override void Build(HudController hud)
        {
            base.Build(hud);
            Root = MakeRoot("The Codex", 860f, 640f, UiKit.Parchment);

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
            layout.padding = new RectOffset(12, 12, 10, 10);
            var fitter = _content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = _content;
        }

        protected override void OnOpen() { Rebuild(); }

        private static int PeopleCount(GameState s)
        {
            int n = 0;
            foreach (var p in s.Social.People) n++;
            return n;
        }

        private void Update()
        {
            if (!IsOpen) return;
            var sim = GameBootstrap.Instance != null ? GameBootstrap.Instance.Sim : null;
            if (sim == null) return;
            // Rebuild when discovery count changes (cheap key).
            int discovered = 0;
            foreach (var poi in sim.State.World.Pois) if (poi.Discovered) discovered++;
            string key = discovered + "|" + PeopleCount(sim.State) + "|" +
                         sim.State.Milestones.UnlockedIds.Count + "|" +
                         (sim.State.Eggs.SpiritSeen ? "1" : "0") + (sim.State.Eggs.MeteorSeen ? "1" : "0");
            if (key != _builtKey) Rebuild();
        }

        private static string DateStr(float time)
        {
            if (time <= 0.01f) return "since the first dawn";
            return "first noted " + UiKit.FormatGameTime(time);
        }

        private void AddSectionHeader(string section, int unlocked, int total)
        {
            var h = UiKit.Label(_content, "Sec" + section,
                UiKit.Spaced(section.ToUpper()) + "   ·   " + unlocked + " / " + total,
                13, UiKit.GoldDim, TextAnchor.UpperLeft);
            var f = h.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UiKit.Divider(_content, "SecRule" + section, new Color(0.72f, 0.55f, 0.28f, 0.45f),
                0f, 0f, 1f, 0f, 12f, 0f, -12f, 1f);
        }

        private void AddIcon(Transform row, Color accent, bool locked)
        {
            var box = UiKit.Rect(row, "Icon", 0f, 1f, 0f, 1f, 0f, -56f, 56f, 0f);
            var img = box.gameObject.AddComponent<Image>();
            img.sprite = UiKit.DotSprite();
            img.color = locked ? new Color(0.25f, 0.23f, 0.20f) : accent;
            img.raycastTarget = false;
            var rt = img.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-16f, -16f); rt.offsetMax = new Vector2(16f, 16f);
            rt.rotation = Quaternion.Euler(0f, 0f, 45f); // diamond
            if (!locked)
            {
                var glow = new GameObject("Glow");
                var grt = glow.AddComponent<RectTransform>();
                grt.SetParent(box, false);
                grt.anchorMin = new Vector2(0.5f, 0.5f); grt.anchorMax = new Vector2(0.5f, 0.5f);
                grt.offsetMin = new Vector2(-24f, -24f); grt.offsetMax = new Vector2(24f, 24f);
                var gimg = glow.AddComponent<Image>();
                gimg.sprite = UiKit.SoftSprite();
                gimg.color = new Color(accent.r, accent.g, accent.b, 0.35f);
                gimg.raycastTarget = false;
            }
        }

        private void AddEntryCard(Entry e, Unlock u)
        {
            var card = new GameObject("Codex" + e.Id);
            var cardRt = card.AddComponent<RectTransform>();
            cardRt.SetParent(_content, false);
            var bg = card.AddComponent<Image>();
            bg.color = u.IsUnlocked
                ? new Color(0.16f, 0.12f, 0.075f, 0.85f)
                : new Color(0.08f, 0.07f, 0.06f, 0.55f);
            bg.raycastTarget = false;

            var header = UiKit.Rect(cardRt, "Header", 0f, 1f, 1f, 1f, 0f, -56f, 0f, 0f);
            AddIcon(header, e.Accent, !u.IsUnlocked);

            string title, body;
            Color titleColor, bodyColor;
            if (u.IsUnlocked)
            {
                title = e.Name; // secrets reveal their true name once found
                titleColor = UiKit.ParchmentInk;
                var sb = new StringBuilder();
                sb.Append("<color=#B2A68C><i>").Append(DateStr(u.Time)).Append("</i></color>\n");
                sb.Append(e.Lore);
                body = sb.ToString();
                bodyColor = new Color(0.82f, 0.76f, 0.62f);
            }
            else if (e.IsSecret)
            {
                title = "???"; // tease until discovered
                titleColor = new Color(0.45f, 0.42f, 0.38f);
                body = "<i>" + e.Tease + "</i>";
                bodyColor = new Color(0.55f, 0.52f, 0.47f);
            }
            else
            {
                title = e.Name;
                titleColor = new Color(0.45f, 0.42f, 0.38f);
                body = "<i>Not yet encountered.</i>";
                bodyColor = new Color(0.55f, 0.52f, 0.47f);
            }

            var titleT = UiKit.Label(header, "Title", title, 17, titleColor, TextAnchor.UpperLeft);
            UiKit.AddShadow(titleT, 0.4f);
            var trt = titleT.GetComponent<RectTransform>();
            trt.offsetMin = new Vector2(66f, -30f); trt.offsetMax = new Vector2(-8f, 0f);

            // Body: measure then size.
            var bodyT = UiKit.Label(cardRt, "Body", body, 13, bodyColor, TextAnchor.UpperLeft);
            bodyT.lineSpacing = 1.3f;
            var brt = bodyT.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 1f); brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.offsetMin = new Vector2(66f, -1056f);
            brt.offsetMax = new Vector2(-12f, -56f);
            Canvas.ForceUpdateCanvases();
            float bh = bodyT.preferredHeight;
            brt.offsetMin = new Vector2(66f, -56f - bh);
            brt.offsetMax = new Vector2(-12f, -56f);

            float cardH = 56f + bh + 12f;
            var le = card.AddComponent<LayoutElement>();
            le.preferredHeight = cardH;
            cardRt.anchorMin = new Vector2(0f, 1f); cardRt.anchorMax = new Vector2(1f, 1f);
            cardRt.pivot = new Vector2(0.5f, 1f);
        }

        private void Rebuild()
        {
            var sim = GameBootstrap.Instance.Sim;
            if (sim == null) return;
            var s = sim.State;
            var unlocks = ComputeUnlocks(s);

            int discovered = 0;
            foreach (var poi in s.World.Pois) if (poi.Discovered) discovered++;
            _builtKey = discovered + "|" + PeopleCount(s) + "|" +
                        s.Milestones.UnlockedIds.Count + "|" +
                        (s.Eggs.SpiritSeen ? "1" : "0") + (s.Eggs.MeteorSeen ? "1" : "0");

            for (int i = _content.childCount - 1; i >= 0; i--)
                Object.Destroy(_content.GetChild(i).gameObject);

            // Progress header.
            int total = Entries.Length, got = 0;
            foreach (var e in Entries) { Unlock x; if (unlocks.TryGetValue(e.Id, out x) && x.IsUnlocked) got++; }
            var prog = UiKit.Label(_content, "Progress",
                UiKit.Spaced("THE CODEX") + "\n" + got + " / " + total + " recorded",
                14, UiKit.ParchmentInk, TextAnchor.UpperLeft);
            prog.lineSpacing = 1.35f;
            UiKit.AddShadow(prog, 0.4f);
            var pf = prog.gameObject.AddComponent<ContentSizeFitter>();
            pf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            UiKit.Divider(_content, "ProgRule", new Color(0.72f, 0.55f, 0.28f, 0.45f),
                0f, 0f, 1f, 0f, 12f, 0f, -12f, 1f);

            foreach (var section in SectionOrder)
            {
                int st = 0, sg = 0;
                foreach (var e in Entries)
                    if (e.Section == section)
                    {
                        st++;
                        Unlock x;
                        if (unlocks.TryGetValue(e.Id, out x) && x.IsUnlocked) sg++;
                    }
                AddSectionHeader(section, sg, st);
                foreach (var e in Entries)
                {
                    if (e.Section != section) continue;
                    Unlock x;
                    if (!unlocks.TryGetValue(e.Id, out x)) x = new Unlock();
                    AddEntryCard(e, x);
                }
            }

            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 1f;
        }
    }
}
