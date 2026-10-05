// Solace.Core — the reflective voice. One identity: Solace speaking from the
// same beliefs, memories, relationships, and live state that drive action.
//
// TRUTH CONTRACT: every factual claim comes from the passed GameState.
// Unknown facts are answered as unknown. Plans are intentions, not promises.
// Suggestions are logged as social inputs, never silently executed commands.
using System;
using System.Collections.Generic;
using System.Text;

namespace Solace.Core
{
    public enum RelationshipLevel
    {
        Stranger,
        Acquaintance,
        Familiar,
        Friend,
        Confidant
    }

    public class CompanionState
    {
        public RelationshipLevel Level = RelationshipLevel.Stranger;
        public int GenuineTurns;
        public int Greetings;
        public float LastTalkTime = -9999f;
        public int RecentSuggestions;
        public float SuggestionWindowStart = -9999f;

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("level", Level.ToString());
            o.Add("genuineTurns", GenuineTurns);
            o.Add("greetings", Greetings);
            o.Add("lastTalkTime", LastTalkTime);
            o.Add("recentSuggestions", RecentSuggestions);
            o.Add("suggestionWindowStart", SuggestionWindowStart);
            return o;
        }

        public static CompanionState FromJson(JsonObject o)
        {
            var c = new CompanionState();
            c.Level = (RelationshipLevel)Enum.Parse(typeof(RelationshipLevel),
                JsonHelpers.GetString(o, "level", "Stranger"));
            c.GenuineTurns = JsonHelpers.GetInt(o, "genuineTurns", 0);
            c.Greetings = JsonHelpers.GetInt(o, "greetings", 0);
            c.LastTalkTime = JsonHelpers.GetFloat(o, "lastTalkTime", -9999f);
            c.RecentSuggestions = JsonHelpers.GetInt(o, "recentSuggestions", 0);
            c.SuggestionWindowStart = JsonHelpers.GetFloat(o, "suggestionWindowStart", -9999f);
            return c;
        }
    }

    public class CompanionReply
    {
        public string Text = "";
        public string Intent = "";
        /// <summary>Non-null when the player made a suggestion: the logged influence.</summary>
        public PlayerInfluence Influence;
    }

    /// <summary>
    /// Template-driven dialogue over live game state. No ML, no invention:
    /// intent matching is keyword + scoring, and every fact is state-sourced.
    /// </summary>
    public static class Companion
    {
        public static CompanionReply Respond(GameState s, string playerText)
        {
            var reply = new CompanionReply();
            string text = (playerText ?? "").Trim();
            string low = " " + text.ToLowerInvariant() + " ";

            string intent = DetectIntent(low);
            reply.Intent = intent;
            var comp = s.Companion;

            // Relationship bookkeeping.
            bool genuine = intent == "status" || intent == "reason" || intent == "recap"
                        || intent == "memory" || intent == "suggestion";
            if (intent == "greeting") comp.Greetings++;
            if (genuine) comp.GenuineTurns++;
            UpdateLevel(comp);

            // Remember presence, throttled: being there matters.
            float now = s.ElapsedSeconds;
            if ((intent == "greeting" || intent == "status" || intent == "recap")
                && now - comp.LastTalkTime > 300f)
            {
                comp.LastTalkTime = now;
                s.Social.RecordPlayerPresence(now,
                    "checked in " + TimePhrase(s.TimeOfDay) + ", " + s.Weather.ToString().ToLowerInvariant());
            }

            switch (intent)
            {
                case "greeting": reply.Text = Greet(s, text); break;
                case "farewell": reply.Text = Farewell(s); break;
                case "whoareyou": reply.Text = WhoAreYou(s); break;
                case "help": reply.Text = Help(s); break;
                case "status": reply.Text = Status(s); break;
                case "reason": reply.Text = Reason(s); break;
                case "recap": reply.Text = Recap(s); break;
                case "memory": reply.Text = MemoryQuery(s, low); break;
                case "suggestion": reply.Influence = LogSuggestion(s, text, low); reply.Text = SuggestReply(s, reply.Influence); break;
                default: reply.Text = Reflect(s, text); break;
            }
            return reply;
        }

        // -- intent detection --------------------------------------------------------

        private static string DetectIntent(string low)
        {
            if (HasAny(low, "goodbye", "good bye", " goodnight ", " good night ", " see you ", "farewell")
                || low.Trim() == "bye")
                return "farewell";
            // NOTE: status is checked before whoareyou because "what are you doing"
            // contains "what are you".
            if (HasAny(low, "what are you doing", "how are you", "where are you", "how do you feel",
                "are you ok", "are you okay", "are you well", "what's happening", "whats happening",
                "how's it going", "hows it going"))
                return "status";
            if (HasAny(low, "who are you", "your name", "what are you", "introduce yourself"))
                return "whoareyou";
            if (HasAny(low, " help ", "what can you do", "what do you do", "commands", "how does this work"))
                return "help";
            // Suggestive "why don't you…" must beat the generic "why" → reason.
            if (HasAny(low, "you should", "why don't you", "why dont you", "maybe you should",
                "have you tried", "be careful", "go to ", "rest up", "eat something", "drink some"))
                return "suggestion";
            if (low.Contains("why"))
                return "reason";
            if (HasAny(low, "recap", "what happened", "catch me up", "summary", "what did i miss",
                "since i", "tell me everything", "fill me in"))
                return "recap";
            if (HasAny(low, "remember", "who is", "tell me about", "do you know", "what do you know"))
                return "memory";
            if (HasAny(low, "hello", " hi ", " hey ", "good morning", "good evening", "good day", "greetings", "morning"))
                return "greeting";
            return "reflect";
        }

        private static bool HasAny(string low, params string[] needles)
        {
            foreach (var n in needles)
                if (low.Contains(n)) return true;
            return false;
        }

        private static void UpdateLevel(CompanionState c)
        {
            int score = c.GenuineTurns * 2 + c.Greetings;
            RelationshipLevel next = score >= 70 ? RelationshipLevel.Confidant
                : score >= 34 ? RelationshipLevel.Friend
                : score >= 14 ? RelationshipLevel.Familiar
                : score >= 4 ? RelationshipLevel.Acquaintance
                : RelationshipLevel.Stranger;
            c.Level = next;
        }

        // -- phrasing helpers ----------------------------------------------------------

        private static string TimePhrase(float tod)
        {
            if (tod < 5f) return "in the small hours";
            if (tod < 8f) return "in the early morning";
            if (tod < 12f) return "this morning";
            if (tod < 14f) return "at midday";
            if (tod < 18f) return "this afternoon";
            if (tod < 22f) return "this evening";
            return "tonight";
        }

        private static string WeatherPhrase(Weather w)
        {
            switch (w)
            {
                case Weather.Clear: return "the sky is clear";
                case Weather.Cloudy: return "cloud is piled over the fell";
                case Weather.Rain: return "rain is falling soft on the heather";
                case Weather.Storm: return "a storm is working through the vale";
                default: return "the weather is turning";
            }
        }

        private static string MoodPhrase(float mood)
        {
            if (mood >= 70f) return "my spirits are good";
            if (mood >= 45f) return "I'm steady enough";
            if (mood >= 25f) return "I'm a bit low";
            return "I'm struggling, honestly";
        }

        private static string BiomePhrase(Biome b)
        {
            switch (b)
            {
                case Biome.Mistmoor: return "out on the mistmoor";
                case Biome.Foxpine: return "in among the pines";
                case Biome.FellCrag: return "up on the crag";
                case Biome.SnowPeak: return "high where the snow holds";
                case Biome.Riverbank: return "down by the water";
                case Biome.DenGrounds: return "at the den";
                default: return "out in the vale";
            }
        }

        private static string WhereAmI(GameState s)
        {
            var a = s.Agent;
            PointOfInterest best = null;
            float bestD = float.MaxValue;
            foreach (var p in s.World.Pois)
            {
                if (!p.LearnedName) continue;
                float d = V2.Distance(a.Pos, new V2(p.X, p.Z));
                if (d < 80f && d < bestD) { bestD = d; best = p; }
            }
            if (best != null) return "near " + best.Name;
            return BiomePhrase(s.World.GetBiome(a.X, a.Z));
        }

        private static string NeedSentence(GameState s)
        {
            var a = s.Agent;
            if (a.Hunger > 70f) return "My stomach is empty and it's all I can think about.";
            if (a.Thirst > 70f) return "I need water soon.";
            if (a.Energy < 25f) return "My light is guttering — I'll need to rest and let it burn back up.";
            if (a.Health < 50f) return "I'm hurt, and moving carefully.";
            if (a.Hunger > 45f) return "I could eat.";
            return "";
        }

        private static string Warmth(GameState s, string stranger, string familiar)
        {
            return s.Companion.Level >= RelationshipLevel.Familiar ? familiar : stranger;
        }

        private static int Variant(GameState s, string salt, int count)
        {
            uint h = SeededRandom.StableHash(salt + "|" + s.Companion.GenuineTurns + s.Companion.Greetings);
            return (int)(h % (uint)count);
        }

        // -- intents ---------------------------------------------------------------------

        private static string Greet(GameState s, string raw)
        {
            string tod = s.TimeOfDay < 12f ? "Morning" : s.TimeOfDay < 18f ? "Afternoon" : "Evening";
            string[] openers =
            {
                tod + ". You're here — good.",
                tod + ". I was just thinking about the weather.",
                "Oh — hello. " + tod + "."
            };
            string opener = openers[Variant(s, "greet" + raw, openers.Length)];
            string tail = Warmth(s,
                "What brings you by?",
                s.Companion.Level >= RelationshipLevel.Friend
                    ? "It's good to see you. Really."
                    : "How are you finding the vale today?");
            return opener + " " + tail;
        }

        private static string Farewell(GameState s)
        {
            string[] lines =
            {
                "Rest well. The vale will keep — and so will I.",
                "Go on, then. I'll be here, living.",
                "Goodbye for now. Don't worry about me; worry is my job."
            };
            string line = lines[Variant(s, "farewell", lines.Length)];
            if (s.Companion.Level >= RelationshipLevel.Friend)
                line += " Come back and tell me what the world looks like from out there.";
            return line;
        }

        private static string GlowPhrase(AgentState a)
        {
            float g = a.Glow;
            if (g > 0.85f) return "My light is burning bright.";
            if (g > 0.6f) return "My light is steady.";
            if (g > 0.35f) return "My light is running thin.";
            return "My light is barely a coal — I need rest and food, soon.";
        }

        private static string AgePhrase(AgentState a)
        {
            var stage = LineageSystem.StageFor(a.Age, a.LifespanYears);
            switch (stage)
            {
                case LifeStage.Kit: return "a kit, " + ((int)(a.Age * 12f)) + " months";
                case LifeStage.Juvenile: return ((int)a.Age) + " years and still learning";
                case LifeStage.Elder: return "old, " + ((int)a.Age) + " years";
                default: return ((int)a.Age) + " years";
            }
        }

        private static string WhoAreYou(GameState s)
        {
            return "I'm Solace. I live in " + s.World.ValleyName + " — I walk it, I get hungry in it, " +
                   "I remember it. My light is my life; my tales are my children's instincts. " +
                   "You're the one who watches, and sometimes the one I talk to. " +
                   "That's the whole of it, and it's enough.";
        }

        private static string Help(GameState s)
        {
            return "You can't steer me — I have my own legs and my own mind. But you can ask: " +
                   "what I'm doing, why I chose it, what's happened while you were gone, " +
                   "who someone is, or what I remember. " +
                   "And you can suggest things. I'll weigh what you say — honestly — " +
                   "but hunger, weather, and fear get their vote first.";
        }

        private static string Status(GameState s)
        {
            var a = s.Agent;
            if (!a.IsAlive)
                return "I'm gone — but the line goes on. Find my eldest kit and keep watching. That's the deal we all sign.";
            var sb = new StringBuilder();
            sb.Append("I'm ");
            sb.Append(string.IsNullOrEmpty(a.CurrentActivity) ? "between things" : a.CurrentActivity);
            sb.Append(", ");
            sb.Append(WhereAmI(s));
            sb.Append(". ");
            sb.Append(Capitalize(WeatherPhrase(s.Weather)));
            sb.Append(", and ");
            sb.Append(MoodPhrase(a.Mood));
            sb.Append(".");
            string need = NeedSentence(s);
            if (need.Length > 0) { sb.Append(" "); sb.Append(need); }
            // The readable signals: glow, age, generation.
            sb.Append(" ");
            sb.Append(GlowPhrase(a));
            sb.Append(" I'm ");
            sb.Append(AgePhrase(a));
            sb.Append(", generation ");
            sb.Append(s.Lineage.Generation);
            sb.Append(" of the line.");
            if (s.World.NightRiverGlow)
                sb.Append(s.IsNight
                    ? " The river is glowing tonight."
                    : " The river will glow again tonight.");
            if (!string.IsNullOrEmpty(a.CurrentGoal))
            {
                sb.Append(" ");
                string[] tails =
                {
                    "My mind is on " + GoalPhrase(a.CurrentGoal) + ".",
                    "Right now it's all about " + GoalPhrase(a.CurrentGoal) + ".",
                };
                sb.Append(tails[Variant(s, "goal", tails.Length)]);
            }
            return sb.ToString();
        }

        private static string GoalPhrase(string goal)
        {
            switch (goal)
            {
                case "Eat": return "finding food";
                case "Drink": return "getting to water";
                case "Rest": return "resting";
                case "Explore": return "seeing what's out there";
                case "Observe": return "watching the wild";
                case "Greet": return "my neighbours";
                case "Flee": return "getting away safely";
                case "Fight": return "surviving this";
                case "Loot": return "searching the old places";
                case "SeekBond": return "finding the one I walk beside";
                case "Tale": return "passing on the tales";
                default: return "whatever comes next";
            }
        }

        private static string Reason(GameState s)
        {
            var t = s.LastDecision;
            if (t == null || string.IsNullOrEmpty(t.ActionName))
                return "I haven't really decided anything yet — ask me again in a bit.";
            if (t.ActionName == "(idle)")
                return "Honestly? Nothing needed doing, so I stood a while. Not every moment needs a reason.";
            var sb = new StringBuilder();
            sb.Append(t.Reason);
            // Surface the strongest score component for legibility, in human words.
            if (t.Components != null && t.Components.Count > 0)
            {
                ScoreComponent top = null;
                foreach (var c in t.Components)
                    if (top == null || c.Contribution > top.Contribution) top = c;
                if (top != null && Math.Abs(top.Contribution) > 0.05f && top.Name != "suggestion")
                {
                    sb.Append(" Mostly it came down to ");
                    sb.Append(ComponentPhrase(top.Name));
                    sb.Append(".");
                }
            }
            return sb.ToString();
        }

        private static string ComponentPhrase(string name)
        {
            switch (name)
            {
                case "hunger": return "how hungry I was";
                case "thirst": return "needing water";
                case "fatigue": return "how tired I was";
                case "danger": return "the danger I could feel";
                case "novelty": return "wanting to see something new";
                case "curiosity": return "curiosity getting the better of me";
                case "sociability": return "wanting company";
                case "self-defense": return "having no better choice";
                case "caution": return "not wanting to be a fool about it";
                case "comfort": return "needing somewhere kind to be";
                case "wonder": return "the wild showing itself";
                case "promise": return "what might be there";
                default: return name;
            }
        }

        private static string Recap(GameState s)
        {
            var recent = s.Journal.Recent(4);
            if (recent.Count == 0)
                return "Nothing's written down yet — my life is still all ahead of me.";
            var sb = new StringBuilder();
            sb.Append(Warmth(s, "Here's what's happened lately. ", "Let me think back. "));
            foreach (var e in recent)
            {
                sb.Append("\n- ");
                sb.Append(ProvenancePrefix(e));
                sb.Append(e.Text);
                sb.Append(" (");
                sb.Append(AgoPhrase(s.ElapsedSeconds - e.Time));
                sb.Append(")");
            }
            return sb.ToString();
        }

        private static string ProvenancePrefix(JournalEntry e)
        {
            if (e.Source == "self" || string.IsNullOrEmpty(e.Source)) return "";
            if (e.Source.StartsWith("told by ")) return "[" + e.Source + "] ";
            if (e.Source.StartsWith("tool:")) return "[a strange intervention] ";
            return "";
        }

        private static string AgoPhrase(float secondsAgo)
        {
            if (secondsAgo < 90f) return "just now";
            if (secondsAgo < 3600f) return (int)(secondsAgo / 60f) + " min ago";
            if (secondsAgo < 86400f)
            {
                int h = (int)(secondsAgo / 3600f);
                return h == 1 ? "an hour ago" : h + " hours ago";
            }
            int d = (int)(secondsAgo / 86400f);
            return d == 1 ? "yesterday" : d + " days ago";
        }

        private static string MemoryQuery(GameState s, string low)
        {
            // Extract the candidate name: words after "who is" / "tell me about" / "remember".
            string[] markers = { "who is", "tell me about", "do you remember", "remember", "do you know", "what do you know about" };
            string subject = "";
            foreach (var m in markers)
            {
                int idx = low.IndexOf(m);
                if (idx >= 0) { subject = low.Substring(idx + m.Length).Trim(' ', '?', '.', '!'); break; }
            }
            if (subject.Length == 0)
                return "I don't know what you're asking about — give me a name.";

            // People first.
            foreach (var p in s.Social.People)
            {
                if (p.Name.ToLowerInvariant().Contains(subject) || subject.Contains(p.Name.ToLowerInvariant()))
                {
                    string trust = p.Trust > 0.65f ? "I trust them." : p.Trust > 0.4f ? "We're getting to know each other." : "I don't know them well yet.";
                    string note = p.Notes.Count > 0 ? " " + p.Notes[p.Notes.Count - 1] : "";
                    return p.Name + "? " + trust + note + " I've greeted them " +
                           (p.Greetings == 1 ? "once" : p.Greetings + " times") + ".";
                }
            }
            // Places (only learned names — I won't invent geography).
            foreach (var poi in s.World.Pois)
            {
                if (!poi.LearnedName) continue;
                string nm = poi.Name.ToLowerInvariant();
                if (nm.Contains(subject) || subject.Contains(nm.Replace("the ", "")))
                {
                    string seen = poi.Discovered ? "I've been there." : "I've heard the name, but I haven't seen it myself.";
                    var belief = s.Beliefs.Get("poi." + poi.Id + ".name");
                    string src = belief != null ? " " + Capitalize(SourcePhrase(belief.Source)) + "." : "";
                    return poi.Name + ". " + seen + src;
                }
            }
            // Beliefs.
            foreach (var b in s.Beliefs.CurrentBeliefs)
            {
                if (b.Claim.ToLowerInvariant().Contains(subject))
                    return Capitalize(b.Claim) + ". " + Capitalize(SourcePhrase(b.Source)) + ".";
            }
            return "I don't know " + subject + ". If it's out there in the vale, I'll learn it — or maybe you'll tell me.";
        }

        private static string SourcePhrase(string source)
        {
            if (source == "saw") return "I saw it myself";
            if (source == "inferred") return "I'm only guessing";
            if (source.StartsWith("told by ")) return source + ", so take it as talk";
            return "that's what I believe";
        }

        private static PlayerInfluence LogSuggestion(GameState s, string raw, string low)
        {
            string topic = TopicFrom(low);
            float weight = 0.25f + 0.1f * (int)s.Companion.Level;
            // Spammy suggestions get heard with a cooler ear.
            var c = s.Companion;
            float now = s.ElapsedSeconds;
            if (now - c.SuggestionWindowStart > 3600f)
            {
                c.SuggestionWindowStart = now;
                c.RecentSuggestions = 0;
            }
            c.RecentSuggestions++;
            if (c.RecentSuggestions > 3) weight *= 0.5f;

            var inf = new PlayerInfluence
            {
                Text = raw.Length > 120 ? raw.Substring(0, 120) : raw,
                Topic = topic,
                Weight = Math.Min(weight, 0.6f),
                Time = now
            };
            s.Agent.Influences.Add(inf);
            if (s.Agent.Influences.Count > 12)
                s.Agent.Influences.RemoveAt(0);
            return inf;
        }

        private static string TopicFrom(string low)
        {
            if (HasAny(low, "rest", "sleep", "tired", "nap")) return "rest";
            if (HasAny(low, "eat", "food", "hungry", "berries", "bread")) return "eat";
            if (HasAny(low, "drink", "water", "thirst")) return "drink";
            if (HasAny(low, "ruin", "hive", "chitin", "stones", "cairn", "old ones")) return "ruin";
            if (HasAny(low, "explore", "look", "discover", "find", "adventure", "go")) return "explore";
            if (HasAny(low, "kindred", "den", "vesper", "moth", "ember", "people", "talk", "greet", "friend", "bond", "mate")) return "social";
            if (HasAny(low, "gloom", "danger", "careful", "run", "fight", "safe", "hide", "predator")) return "safety";
            if (HasAny(low, "kit", "cub", "young", "tale", "stories", "generation", "die", "death", "old")) return "lineage";
            if (HasAny(low, "deer", "watch", "hare", "rabbit")) return "watch";
            return "explore";
        }

        private static string SuggestReply(GameState s, PlayerInfluence inf)
        {
            var a = s.Agent;
            bool critical = a.Hunger > 88f || a.Thirst > 88f || a.Energy < 10f || a.Health < 30f;
            string topicBit;
            switch (inf.Topic)
            {
                case "rest": topicBit = "resting"; break;
                case "eat": topicBit = "eating"; break;
                case "drink": topicBit = "drinking"; break;
                case "explore": topicBit = "exploring"; break;
                case "ruin": topicBit = "the old places"; break;
                case "social": topicBit = "seeing my kindred"; break;
                case "lineage": topicBit = "the line, the kits, the tales"; break;
                case "safety": topicBit = "being careful"; break;
                case "watch": topicBit = "watching the wild"; break;
                default: topicBit = "that"; break;
            }
            var sb = new StringBuilder();
            if (s.Companion.RecentSuggestions > 3)
                sb.Append("You've said a few things in a row — I'm listening, but I need to trust my own read too. ");
            string[] acks =
            {
                "I'll keep that in mind.",
                "Noted. I'll weigh it.",
                "Hm. I'll think on it."
            };
            sb.Append(acks[Variant(s, "sug" + inf.Text, acks.Length)]);
            sb.Append(" ");
            if (critical)
                sb.Append("Right now, though, my body is making the decisions — I'll think about " + topicBit + " when I'm not in a bad way.");
            else
                sb.Append("No promises about " + topicBit + " — but your words carry weight with me.");
            return sb.ToString();
        }

        private static string Reflect(GameState s, string raw)
        {
            // No intent matched: answer from the present, truthfully, without inventing.
            var a = s.Agent;
            string[] lines =
            {
                "I'm not sure what you mean — but I'm here, " + WhereAmI(s) + ", " + MoodPhrase(a.Mood) + ".",
                "Hm. Say it another way? Right now I'm " + (string.IsNullOrEmpty(a.CurrentActivity) ? "between things" : a.CurrentActivity) + ".",
                "I don't follow. The vale is simpler than words, mostly."
            };
            string line = lines[Variant(s, "reflect" + raw, lines.Length)];
            if (s.Companion.Level >= RelationshipLevel.Familiar)
                line += " What are you thinking about, out there?";
            return line;
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
