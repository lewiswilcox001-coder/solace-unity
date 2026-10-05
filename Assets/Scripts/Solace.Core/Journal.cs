// Solace.Core — the journal: evidence, not omniscience.
// Entries are written from Solace's perspective, carry provenance, and may be
// corrected later. Capped at 300 entries; the lowest-salience oldest go first.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    public enum JournalCategory
    {
        Discovery,
        Combat,
        Social,
        Survival,
        Weather,
        Travel,
        Reflection,
        Dream,     // dreams and dream-recognitions: the mind shaping the world
        System,
        Chapter   // chapter boundaries of the multi-generational chronicle
    }

    public class JournalEntry
    {
        public float Time;            // game seconds since life began
        public int Generation = 1;    // lineage generation that wrote this
        public string Text;
        public JournalCategory Category;
        public int? PlaceId;          // POI id, if the entry is about a place
        public int? PersonId;         // entity id, if about a person
        public float Certainty = 1f;  // 0..1 — rumors and guesses are marked
        public float Salience = 0.5f; // 0..1 — eviction + episode priority
        public string Source = "self"; // "self" | "told by <name>" | "tool:<name>"

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("time", Time);
            o.Add("generation", Generation);
            o.Add("text", Text);
            o.Add("category", Category.ToString());
            o.Add("placeId", PlaceId.HasValue ? (JsonValue)JsonNumber.From(PlaceId.Value) : JsonNull.Instance);
            o.Add("personId", PersonId.HasValue ? (JsonValue)JsonNumber.From(PersonId.Value) : JsonNull.Instance);
            o.Add("certainty", Certainty);
            o.Add("salience", Salience);
            o.Add("source", Source);
            return o;
        }

        public static JournalEntry FromJson(JsonObject o)
        {
            var e = new JournalEntry();
            e.Time = JsonHelpers.GetFloat(o, "time", 0f);
            e.Generation = JsonHelpers.GetInt(o, "generation", 1);
            e.Text = JsonHelpers.GetString(o, "text", "");
            e.Category = (JournalCategory)Enum.Parse(typeof(JournalCategory), JsonHelpers.GetString(o, "category", "System"));
            JsonValue pv;
            e.PlaceId = o.TryGet("placeId", out pv) && !pv.IsNull ? (int?)((JsonNumber)pv).AsInt() : null;
            JsonValue qv;
            e.PersonId = o.TryGet("personId", out qv) && !qv.IsNull ? (int?)((JsonNumber)qv).AsInt() : null;
            e.Certainty = JsonHelpers.GetFloat(o, "certainty", 1f);
            e.Salience = JsonHelpers.GetFloat(o, "salience", 0.5f);
            e.Source = JsonHelpers.GetString(o, "source", "self");
            return e;
        }
    }

    public class Journal
    {
        public const int Cap = 300;

        private readonly List<JournalEntry> _entries = new List<JournalEntry>();

        public int Count { get { return _entries.Count; } }
        public IEnumerable<JournalEntry> Entries { get { return _entries; } }

        public void Add(JournalEntry entry)
        {
            if (entry == null) return;
            entry.Salience = MathX.Clamp01(entry.Salience);
            entry.Certainty = MathX.Clamp01(entry.Certainty);
            _entries.Add(entry);
            while (_entries.Count > Cap)
                EvictOne();
        }

        public JournalEntry Add(float time, string text, JournalCategory category,
                                float salience = 0.5f, float certainty = 1f,
                                int? placeId = null, int? personId = null, string source = "self",
                                int generation = 1)
        {
            var e = new JournalEntry
            {
                Time = time,
                Generation = generation,
                Text = text,
                Category = category,
                Salience = salience,
                Certainty = certainty,
                PlaceId = placeId,
                PersonId = personId,
                Source = source
            };
            Add(e);
            return e;
        }

        /// <summary>Newest n entries, newest first.</summary>
        public List<JournalEntry> Recent(int n)
        {
            var out_ = new List<JournalEntry>(Math.Min(n, _entries.Count));
            for (int i = _entries.Count - 1; i >= 0 && out_.Count < n; i--)
                out_.Add(_entries[i]);
            return out_;
        }

        /// <summary>Most salient entries — the makings of a life summary.</summary>
        public List<JournalEntry> Episodes(int n)
        {
            var copy = new List<JournalEntry>(_entries);
            copy.Sort((a, b) =>
            {
                int c = b.Salience.CompareTo(a.Salience);
                return c != 0 ? c : b.Time.CompareTo(a.Time);
            });
            if (copy.Count > n) copy.RemoveRange(n, copy.Count - n);
            return copy;
        }

        private void EvictOne()
        {
            // Drop the lowest-salience entry; ties break toward the oldest.
            int victim = 0;
            float worst = float.MaxValue;
            for (int i = 0; i < _entries.Count; i++)
            {
                // Slight age penalty so ancient trivia yields to fresh trivia.
                float score = _entries[i].Salience * 1000f + i * 0.001f;
                if (score < worst) { worst = score; victim = i; }
            }
            _entries.RemoveAt(victim);
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            var a = new JsonArray();
            for (int i = 0; i < _entries.Count; i++) a.Add(_entries[i].ToJson());
            o.Add("entries", a);
            return o;
        }

        public static Journal FromJson(JsonObject o)
        {
            var j = new Journal();
            var a = o["entries"].AsArray();
            for (int i = 0; i < a.Count; i++)
            {
                var e = JournalEntry.FromJson(a[i].AsObject());
                if (j._entries.Count < Cap) j._entries.Add(e);
            }
            return j;
        }
    }
}
