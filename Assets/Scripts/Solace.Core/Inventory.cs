// Solace.Core — inventory. Pure data + operations; the Simulation journals
// around calls (see Simulation) so every gain/loss leaves a trace.
using System.Collections.Generic;

namespace Solace.Core
{
    public class Inventory
    {
        public bool HasSword;
        public int Bread;
        public int Potions;
        public List<string> Keepsakes = new List<string>();

        public const int MaxKeepsakes = 12;

        public void AddBread(int n) { if (n > 0) Bread += n; }

        /// <summary>Eats one bread. Returns false if none.</summary>
        public bool EatBread()
        {
            if (Bread <= 0) return false;
            Bread--;
            return true;
        }

        public void AddPotions(int n) { if (n > 0) Potions += n; }

        /// <summary>Drinks one potion. Returns false if none.</summary>
        public bool UsePotion()
        {
            if (Potions <= 0) return false;
            Potions--;
            return true;
        }

        /// <summary>Keepsakes are unique mementos; duplicates are refused.</summary>
        public bool AddKeepsake(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < Keepsakes.Count; i++)
                if (Keepsakes[i] == name) return false;
            if (Keepsakes.Count >= MaxKeepsakes)
                Keepsakes.RemoveAt(0); // oldest memento is let go, not forgotten silently — Simulation journals it
            Keepsakes.Add(name);
            return true;
        }

        public bool HasKeepsake(string name)
        {
            for (int i = 0; i < Keepsakes.Count; i++)
                if (Keepsakes[i] == name) return true;
            return false;
        }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("hasSword", HasSword);
            o.Add("bread", Bread);
            o.Add("potions", Potions);
            var ka = new JsonArray();
            for (int i = 0; i < Keepsakes.Count; i++) ka.Add(Keepsakes[i]);
            o.Add("keepsakes", ka);
            return o;
        }

        public static Inventory FromJson(JsonObject o)
        {
            var inv = new Inventory();
            inv.HasSword = JsonHelpers.GetBool(o, "hasSword", false);
            inv.Bread = JsonHelpers.GetInt(o, "bread", 0);
            inv.Potions = JsonHelpers.GetInt(o, "potions", 0);
            var ka = o["keepsakes"].AsArray();
            for (int i = 0; i < ka.Count; i++) inv.Keepsakes.Add(ka[i].AsString());
            return inv;
        }
    }
}
