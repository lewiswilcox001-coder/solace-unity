// Solace.Core — beliefs and social memory.
// Beliefs carry source, time, and confidence, and are REVISED rather than
// silently overwritten: superseded claims stay in history. Social memory tracks
// per-person trust with slow decay, plus records of player presence.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    public class Belief
    {
        public string Key;          // stable lookup, e.g. "poi.4.name"
        public string Claim;        // the believed proposition
        public string Source;       // "saw" | "told by <name>" | "inferred"
        public float Confidence;    // 0..1
        public float Time;          // game seconds when formed/revised
        public string SupersededBy; // claim text that replaced this one (null if current)

        public bool IsCurrent { get { return SupersededBy == null; } }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("key", Key);
            o.Add("claim", Claim);
            o.Add("source", Source);
            o.Add("confidence", Confidence);
            o.Add("time", Time);
            o.Add("supersededBy", SupersededBy == null ? (JsonValue)JsonNull.Instance : (JsonValue)new JsonString(SupersededBy));
            return o;
        }

        public static Belief FromJson(JsonObject o)
        {
            var b = new Belief();
            b.Key = JsonHelpers.GetString(o, "key", "");
            b.Claim = JsonHelpers.GetString(o, "claim", "");
            b.Source = JsonHelpers.GetString(o, "source", "inferred");
            b.Confidence = JsonHelpers.GetFloat(o, "confidence", 0.5f);
            b.Time = JsonHelpers.GetFloat(o, "time", 0f);
            JsonValue v;
            b.SupersededBy = o.TryGet("supersededBy", out v) && !v.IsNull ? v.AsString() : null;
            return b;
        }
    }

    /// <summary>Semantic memory: known facts with provenance and confidence.</summary>
    public class BeliefStore
    {
        // Current beliefs by key. History of superseded beliefs kept separately.
        private readonly Dictionary<string, Belief> _current = new Dictionary<string, Belief>();
        private readonly List<Belief> _history = new List<Belief>();

        public Belief Get(string key)
        {
            Belief b;
            return _current.TryGetValue(key, out b) ? b : null;
        }

        public IEnumerable<Belief> CurrentBeliefs { get { return _current.Values; } }
        public IEnumerable<Belief> History { get { return _history; } }

        /// <summary>
        /// Adds a belief, or revises the existing one. The old claim is retained
        /// in history with SupersededBy set — never silently overwritten.
        /// Returns true if this created or changed the current claim.
        /// </summary>
        public bool AddOrUpdate(string key, string claim, string source, float confidence, float time)
        {
            confidence = MathX.Clamp01(confidence);
            Belief existing;
            if (_current.TryGetValue(key, out existing))
            {
                if (existing.Claim == claim)
                {
                    // Same claim, fresh evidence: nudge confidence toward the new reading.
                    existing.Confidence = MathX.Clamp01(existing.Confidence * 0.6f + confidence * 0.4f);
                    existing.Time = time;
                    return false;
                }
                existing.SupersededBy = claim;
                _history.Add(existing);
            }
            _current[key] = new Belief
            {
                Key = key,
                Claim = claim,
                Source = source,
                Confidence = confidence,
                Time = time,
                SupersededBy = null
            };
            return true;
        }

        /// <summary>Contradictory evidence lowers confidence instead of deleting.</summary>
        public void Contradict(string key, float time, float amount = 0.25f)
        {
            Belief b;
            if (_current.TryGetValue(key, out b))
            {
                b.Confidence = MathX.Clamp01(b.Confidence - amount);
                b.Time = time;
            }
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            var keys = new List<string>(_current.Keys);
            keys.Sort(StringComparer.Ordinal);
            var ca = new JsonArray();
            foreach (var k in keys) ca.Add(_current[k].ToJson());
            o.Add("current", ca);
            var ha = new JsonArray();
            for (int i = 0; i < _history.Count; i++) ha.Add(_history[i].ToJson());
            o.Add("history", ha);
            return o;
        }

        public static BeliefStore FromJson(JsonObject o)
        {
            var s = new BeliefStore();
            var ca = o["current"].AsArray();
            for (int i = 0; i < ca.Count; i++)
            {
                var b = Belief.FromJson(ca[i].AsObject());
                s._current[b.Key] = b;
            }
            var ha = o["history"].AsArray();
            for (int i = 0; i < ha.Count; i++) s._history.Add(Belief.FromJson(ha[i].AsObject()));
            return s;
        }
    }

    /// <summary>What Solace remembers about a person.</summary>
    public class PersonRecord
    {
        public int EntityId;
        public string Name;
        public float Trust;        // 0..1
        public float LastMet;      // game seconds
        public int Greetings;
        public List<string> Notes = new List<string>();

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("entityId", EntityId);
            o.Add("name", Name);
            o.Add("trust", Trust);
            o.Add("lastMet", LastMet);
            o.Add("greetings", Greetings);
            var na = new JsonArray();
            for (int i = 0; i < Notes.Count; i++) na.Add(Notes[i]);
            o.Add("notes", na);
            return o;
        }

        public static PersonRecord FromJson(JsonObject o)
        {
            var p = new PersonRecord();
            p.EntityId = JsonHelpers.GetInt(o, "entityId", -1);
            p.Name = JsonHelpers.GetString(o, "name", "?");
            p.Trust = JsonHelpers.GetFloat(o, "trust", 0.3f);
            p.LastMet = JsonHelpers.GetFloat(o, "lastMet", 0f);
            p.Greetings = JsonHelpers.GetInt(o, "greetings", 0);
            var na = o["notes"].AsArray();
            for (int i = 0; i < na.Count; i++) p.Notes.Add(na[i].AsString());
            return p;
        }
    }

    /// <summary>Record of the player being present for something.</summary>
    public class PresenceRecord
    {
        public float Time;
        public string Context; // e.g. "during the storm", "at the hollow hive"

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("time", Time);
            o.Add("context", Context);
            return o;
        }

        public static PresenceRecord FromJson(JsonObject o)
        {
            return new PresenceRecord
            {
                Time = JsonHelpers.GetFloat(o, "time", 0f),
                Context = JsonHelpers.GetString(o, "context", "")
            };
        }
    }

    /// <summary>Social memory: trust per person with slow decay, and player presence.</summary>
    public class SocialMemory
    {
        private readonly Dictionary<int, PersonRecord> _people = new Dictionary<int, PersonRecord>();
        private readonly List<PresenceRecord> _presence = new List<PresenceRecord>();

        public PersonRecord GetPerson(int entityId)
        {
            PersonRecord p;
            return _people.TryGetValue(entityId, out p) ? p : null;
        }

        public IEnumerable<PersonRecord> People { get { return _people.Values; } }
        public IEnumerable<PresenceRecord> Presence { get { return _presence; } }

        public PersonRecord Meet(int entityId, string name, float time)
        {
            PersonRecord p;
            if (!_people.TryGetValue(entityId, out p))
            {
                p = new PersonRecord { EntityId = entityId, Name = name, Trust = 0.35f, LastMet = time };
                _people[entityId] = p;
            }
            p.LastMet = time;
            return p;
        }

        /// <summary>A greeting: trust rises a little, greetings counted.</summary>
        public void RecordGreeting(int entityId, float time)
        {
            PersonRecord p = Meet(entityId, "?", time);
            p.Greetings++;
            p.Trust = MathX.Clamp01(p.Trust + 0.06f);
            p.LastMet = time;
        }

        public void AdjustTrust(int entityId, float delta, float time)
        {
            PersonRecord p;
            if (_people.TryGetValue(entityId, out p))
            {
                p.Trust = MathX.Clamp01(p.Trust + delta);
                p.LastMet = time;
            }
        }

        public void AddNote(int entityId, string note)
        {
            PersonRecord p;
            if (_people.TryGetValue(entityId, out p) && p.Notes.Count < 12)
                p.Notes.Add(note);
        }

        /// <summary>Trust decays slowly toward a neutral 0.35 baseline.</summary>
        public void Decay(float now, float dtSeconds)
        {
            // ~0.02 trust per game-day of not meeting.
            float rate = 0.02f / 86400f;
            foreach (var p in _people.Values)
            {
                if (now - p.LastMet > 3600f)
                {
                    float target = 0.35f;
                    p.Trust += Math.Sign(target - p.Trust) * Math.Min(Math.Abs(target - p.Trust), rate * dtSeconds);
                }
            }
        }

        public void RecordPlayerPresence(float time, string context)
        {
            _presence.Add(new PresenceRecord { Time = time, Context = context });
            if (_presence.Count > 40)
                _presence.RemoveAt(0);
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            var ids = new List<int>(_people.Keys);
            ids.Sort();
            var pa = new JsonArray();
            foreach (var id in ids) pa.Add(_people[id].ToJson());
            o.Add("people", pa);
            var ra = new JsonArray();
            for (int i = 0; i < _presence.Count; i++) ra.Add(_presence[i].ToJson());
            o.Add("presence", ra);
            return o;
        }

        public static SocialMemory FromJson(JsonObject o)
        {
            var s = new SocialMemory();
            var pa = o["people"].AsArray();
            for (int i = 0; i < pa.Count; i++)
            {
                var p = PersonRecord.FromJson(pa[i].AsObject());
                s._people[p.EntityId] = p;
            }
            var ra = o["presence"].AsArray();
            for (int i = 0; i < ra.Count; i++) s._presence.Add(PresenceRecord.FromJson(ra[i].AsObject()));
            return s;
        }
    }
}
