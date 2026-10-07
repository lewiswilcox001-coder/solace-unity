// Solace.Core — multiple save slots.
//
// Three independent worlds. Each slot owns:
//   slotN.json       — the full serialized game state
//   slotN-meta.json  — lightweight summary (seed, fox name, generation,
//                      playtime, last-saved time) for the slot menu
//   slotN.bak.json   — auto-backup: the previous save, rotated on each write
//   slotN-thumb.png  — procedural icon, written by the Unity layer
//
// Legacy single saves (save.json / save-meta.txt) are migrated into slot 0
// on first access, so old saves keep loading without any user action.
// Pure System.IO — no Unity dependency, fully unit-testable.
using System;
using System.IO;

namespace Solace.Core
{
    public static class SaveSlots
    {
        public const int MaxSlots = 3;

        /// <summary>Lightweight summary of one slot, for the slot menu.</summary>
        public class SlotInfo
        {
            public int Slot;
            public bool Exists;
            public int Seed;
            public string FoxName = "";
            public int Generation = 1;
            public double PlaytimeSeconds;
            public long LastSavedUtcTicks; // 0 = unknown
            public bool HasBackup;
        }

        // -- paths -----------------------------------------------------------------

        public static string SlotFile(string dir, int slot)
        {
            return Path.Combine(dir, "slot" + slot + ".json");
        }

        public static string SlotMetaFile(string dir, int slot)
        {
            return Path.Combine(dir, "slot" + slot + "-meta.json");
        }

        public static string SlotBackupFile(string dir, int slot)
        {
            return Path.Combine(dir, "slot" + slot + ".bak.json");
        }

        public static string SlotThumbFile(string dir, int slot)
        {
            return Path.Combine(dir, "slot" + slot + "-thumb.png");
        }

        private static string LegacyFile(string dir) { return Path.Combine(dir, "save.json"); }
        private static string LegacyMetaFile(string dir) { return Path.Combine(dir, "save-meta.txt"); }

        private static void CheckSlot(int slot)
        {
            if (slot < 0 || slot >= MaxSlots)
                throw new ArgumentOutOfRangeException("slot", "Slot must be 0.." + (MaxSlots - 1));
        }

        // -- listing ----------------------------------------------------------------

        /// <summary>One SlotInfo per slot. Migrates a legacy save into slot 0 first.</summary>
        public static SlotInfo[] ListSlots(string dir)
        {
            MigrateLegacyIfNeeded(dir);
            var infos = new SlotInfo[MaxSlots];
            for (int i = 0; i < MaxSlots; i++) infos[i] = ReadSlotInfo(dir, i);
            return infos;
        }

        /// <summary>Summary for a single slot. Never throws on corrupt data.</summary>
        public static SlotInfo ReadSlotInfo(string dir, int slot)
        {
            CheckSlot(slot);
            var info = new SlotInfo { Slot = slot };
            try
            {
                string metaPath = SlotMetaFile(dir, slot);
                if (File.Exists(metaPath))
                {
                    var o = JsonValue.Parse(File.ReadAllText(metaPath)).AsObject();
                    info.Exists = true;
                    info.Seed = JsonHelpers.GetInt(o, "seed", 0);
                    info.FoxName = JsonHelpers.GetString(o, "foxName", "");
                    info.Generation = JsonHelpers.GetInt(o, "generation", 1);
                    info.PlaytimeSeconds = JsonHelpers.GetDouble(o, "playtimeSeconds", 0);
                    info.LastSavedUtcTicks = ParseTicks(JsonHelpers.GetString(o, "lastSavedUtcTicks", "0"));
                }
                else if (File.Exists(SlotFile(dir, slot)))
                {
                    // Meta missing (e.g. hand-placed save): derive it from the state once.
                    var state = SaveSystem.Load(File.ReadAllText(SlotFile(dir, slot)));
                    info = FromState(slot, state);
                    info.Exists = true;
                    WriteMeta(dir, slot, state, info.LastSavedUtcTicks);
                }
                info.HasBackup = File.Exists(SlotBackupFile(dir, slot));
            }
            catch
            {
                // Corrupt slot reads as empty; the file stays for manual recovery.
                return new SlotInfo { Slot = slot };
            }
            return info;
        }

        // -- save / load --------------------------------------------------------------

        /// <summary>
        /// Writes a slot: rotates the previous save into the .bak file, writes
        /// the new state atomically (tmp + move), and refreshes the meta file.
        /// </summary>
        public static void SaveSlot(string dir, int slot, GameState state)
        {
            CheckSlot(slot);
            if (state == null) throw new ArgumentNullException("state");
            Directory.CreateDirectory(dir);

            string dst = SlotFile(dir, slot);
            string bak = SlotBackupFile(dir, slot);
            if (File.Exists(dst))
            {
                if (File.Exists(bak)) File.Delete(bak);
                File.Copy(dst, bak);
            }

            string json = SaveSystem.Save(state);
            string tmp = dst + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(tmp, dst);

            WriteMeta(dir, slot, state, DateTime.UtcNow.Ticks);
        }

        /// <summary>Raw JSON for a slot, or null when the slot is empty.</summary>
        public static string LoadSlotJson(string dir, int slot)
        {
            CheckSlot(slot);
            string path = SlotFile(dir, slot);
            if (File.Exists(path)) return File.ReadAllText(path);
            // Old single-save path still loads as slot 0 even before migration.
            if (slot == 0)
            {
                string legacy = LegacyFile(dir);
                if (File.Exists(legacy)) return File.ReadAllText(legacy);
            }
            return null;
        }

        /// <summary>Removes a slot's save, meta, backup, thumbnail, and challenge sidecar.</summary>
        public static void DeleteSlot(string dir, int slot)
        {
            CheckSlot(slot);
            foreach (string p in new[]
            {
                SlotFile(dir, slot), SlotMetaFile(dir, slot),
                SlotBackupFile(dir, slot), SlotThumbFile(dir, slot)
            })
            {
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            }
            ChallengeSave.Delete(dir, slot);
        }

        /// <summary>
        /// How long since this slot was last saved. Falls back to the legacy
        /// save-meta.txt for slot 0 so away-progress still works pre-migration.
        /// </summary>
        public static TimeSpan AwayTime(string dir, int slot)
        {
            CheckSlot(slot);
            try
            {
                long ticks = 0;
                string metaPath = SlotMetaFile(dir, slot);
                if (File.Exists(metaPath))
                {
                    var o = JsonValue.Parse(File.ReadAllText(metaPath)).AsObject();
                    ticks = ParseTicks(JsonHelpers.GetString(o, "lastSavedUtcTicks", "0"));
                }
                else if (slot == 0 && File.Exists(LegacyMetaFile(dir)))
                {
                    ticks = long.Parse(File.ReadAllText(LegacyMetaFile(dir)).Trim());
                }
                if (ticks <= 0) return TimeSpan.Zero;
                TimeSpan away = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
                return away.TotalSeconds > 0 ? away : TimeSpan.Zero;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        // -- legacy migration ----------------------------------------------------------

        /// <summary>
        /// Moves an old single save (save.json) into slot 0 exactly once: only
        /// when no slot files exist yet. Old saves keep loading either way.
        /// </summary>
        public static void MigrateLegacyIfNeeded(string dir)
        {
            try
            {
                for (int i = 0; i < MaxSlots; i++)
                    if (File.Exists(SlotFile(dir, i)) || File.Exists(SlotMetaFile(dir, i)))
                        return; // slots already in use — nothing to migrate
                string legacy = LegacyFile(dir);
                if (!File.Exists(legacy)) return;
                var state = SaveSystem.Load(File.ReadAllText(legacy));
                Directory.CreateDirectory(dir);
                string dst = SlotFile(dir, 0);
                File.Copy(legacy, dst, true);
                long ticks = 0;
                string legacyMeta = LegacyMetaFile(dir);
                if (File.Exists(legacyMeta))
                    try { ticks = long.Parse(File.ReadAllText(legacyMeta).Trim()); } catch { }
                WriteMeta(dir, 0, state, ticks);
                try { File.Delete(legacy); } catch { }
                try { if (File.Exists(legacyMeta)) File.Delete(legacyMeta); } catch { }
            }
            catch
            {
                // Migration is best-effort; the legacy file still loads via LoadSlotJson.
            }
        }

        // -- internals -------------------------------------------------------------------

        private static SlotInfo FromState(int slot, GameState s)
        {
            var info = new SlotInfo { Slot = slot, Exists = true };
            info.Seed = s.Seed;
            info.FoxName = s.Agent != null ? s.Agent.Name : "";
            info.Generation = s.Lineage != null ? s.Lineage.Generation : 1;
            info.PlaytimeSeconds = s.ElapsedSeconds;
            info.LastSavedUtcTicks = DateTime.UtcNow.Ticks;
            return info;
        }

        private static void WriteMeta(string dir, int slot, GameState s, long ticks)
        {
            var o = new JsonObject();
            o.Add("slot", slot);
            o.Add("seed", s.Seed);
            o.Add("foxName", s.Agent != null ? s.Agent.Name : "");
            o.Add("generation", s.Lineage != null ? s.Lineage.Generation : 1);
            o.Add("playtimeSeconds", (double)s.ElapsedSeconds);
            o.Add("lastSavedUtcTicks", ticks.ToString());
            string tmp = SlotMetaFile(dir, slot) + ".tmp";
            File.WriteAllText(tmp, o.ToJson());
            string dst = SlotMetaFile(dir, slot);
            if (File.Exists(dst)) File.Delete(dst);
            File.Move(tmp, dst);
        }

        private static long ParseTicks(string s)
        {
            long v;
            return long.TryParse(s, out v) ? v : 0;
        }
    }
}
