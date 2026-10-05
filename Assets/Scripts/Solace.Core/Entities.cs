// Solace.Core — the other lives in the glen: deer, wolves, villagers, rabbits.
// Small behavior sets, stepped by the Simulation. Wolves hunt when hungry;
// deer and rabbits flee; villagers keep to their rounds and can be greeted.
// Combat is a few readable rounds with morale — never a grind.
using System;
using System.Collections.Generic;

namespace Solace.Core
{
    public enum EntityKind
    {
        Deer,
        Wolf,
        Villager,
        Rabbit
    }

    public class EntityState
    {
        public int Id;
        public EntityKind Kind;
        public string Name = "";
        public float X, Z;
        public float Facing;
        public float Health = 100f;
        public string Behavior = "Idle"; // Graze, Flee, Roam, Hunt, Wander, Idle, Greeted, Hop, Dead
        public float HomeX, HomeZ;
        public float Hunger;      // wolves: 0..100
        public float StateTimer;  // behavior-local clock
        public int TargetKind;    // 0 none, 1 agent, 2 entity
        public int TargetEntityId = -1;
        public float TargetX, TargetZ; // wander destination

        public V2 Pos { get { return new V2(X, Z); } }
        public bool IsAlive { get { return Health > 0 && Behavior != "Dead"; } }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o.Add("id", Id);
            o.Add("kind", Kind.ToString());
            o.Add("name", Name);
            o.Add("x", X); o.Add("z", Z);
            o.Add("facing", Facing);
            o.Add("health", Health);
            o.Add("behavior", Behavior);
            o.Add("homeX", HomeX); o.Add("homeZ", HomeZ);
            o.Add("hunger", Hunger);
            o.Add("stateTimer", StateTimer);
            o.Add("targetKind", TargetKind);
            o.Add("targetEntityId", TargetEntityId);
            o.Add("targetX", TargetX); o.Add("targetZ", TargetZ);
            return o;
        }

        public static EntityState FromJson(JsonObject o)
        {
            var e = new EntityState();
            e.Id = JsonHelpers.GetInt(o, "id", 0);
            e.Kind = (EntityKind)Enum.Parse(typeof(EntityKind), JsonHelpers.GetString(o, "kind", "Deer"));
            e.Name = JsonHelpers.GetString(o, "name", "");
            e.X = JsonHelpers.GetFloat(o, "x", 0f); e.Z = JsonHelpers.GetFloat(o, "z", 0f);
            e.Facing = JsonHelpers.GetFloat(o, "facing", 0f);
            e.Health = JsonHelpers.GetFloat(o, "health", 100f);
            e.Behavior = JsonHelpers.GetString(o, "behavior", "Idle");
            e.HomeX = JsonHelpers.GetFloat(o, "homeX", 0f); e.HomeZ = JsonHelpers.GetFloat(o, "homeZ", 0f);
            e.Hunger = JsonHelpers.GetFloat(o, "hunger", 0f);
            e.StateTimer = JsonHelpers.GetFloat(o, "stateTimer", 0f);
            e.TargetKind = JsonHelpers.GetInt(o, "targetKind", 0);
            e.TargetEntityId = JsonHelpers.GetInt(o, "targetEntityId", -1);
            e.TargetX = JsonHelpers.GetFloat(o, "targetX", 0f);
            e.TargetZ = JsonHelpers.GetFloat(o, "targetZ", 0f);
            return e;
        }
    }

    /// <summary>What an entity can see of the world this step.</summary>
    public class EntityContext
    {
        public WorldData World;
        public AgentState Agent;
        public List<EntityState> Entities;
        public float Now;
        public SeededRandom Rng; // event stream
        public List<string> EventLog = new List<string>(); // journal-worthy lines for Simulation
    }

    public static class EntityBehaviors
    {
        public static void Step(EntityState e, EntityContext ctx, float dt)
        {
            if (!e.IsAlive) return;
            e.StateTimer -= dt;
            switch (e.Kind)
            {
                case EntityKind.Deer: StepDeer(e, ctx, dt); break;
                case EntityKind.Rabbit: StepRabbit(e, ctx, dt); break;
                case EntityKind.Wolf: StepWolf(e, ctx, dt); break;
                case EntityKind.Villager: StepVillager(e, ctx, dt); break;
            }
        }

        // -- movement helper: walks, avoids water, updates facing ------------

        private static void Move(EntityState e, EntityContext ctx, float dx, float dz, float speed, float dt)
        {
            float len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 1e-5f) return;
            dx /= len; dz /= len;
            float nx = e.X + dx * speed * dt;
            float nz = e.Z + dz * speed * dt;
            if (ctx.World.IsWater(nx, nz))
            {
                // Try sidestepping; otherwise hold.
                float sx = e.X - dz * speed * dt;
                float sz = e.Z + dx * speed * dt;
                if (!ctx.World.IsWater(sx, sz)) { nx = sx; nz = sz; }
                else return;
            }
            float half = ctx.World.HalfSize - 4f;
            e.X = MathX.Clamp(nx, -half, half);
            e.Z = MathX.Clamp(nz, -half, half);
            e.Facing = MathF.Atan2(dx, dz);
        }

        private static void Wander(EntityState e, EntityContext ctx, float dt, float speed, float range)
        {
            float dx = e.TargetX - e.X, dz = e.TargetZ - e.Z;
            if (dx * dx + dz * dz < 4f || e.StateTimer <= 0f)
            {
                float ang = ctx.Rng.NextFloat(0f, MathF.PI * 2f);
                float dist = ctx.Rng.NextFloat(6f, range);
                e.TargetX = e.HomeX + MathF.Cos(ang) * dist;
                e.TargetZ = e.HomeZ + MathF.Sin(ang) * dist;
                e.StateTimer = ctx.Rng.NextFloat(4f, 10f);
                // Occasionally just stand and rest.
                if (ctx.Rng.NextFloat() < 0.35f)
                {
                    e.Behavior = e.Kind == EntityKind.Deer ? "Graze" : "Idle";
                    e.StateTimer = ctx.Rng.NextFloat(3f, 7f);
                    return;
                }
            }
            else if (e.Behavior == "Graze" || e.Behavior == "Idle")
            {
                return; // standing, resting
            }
            Move(e, ctx, dx, dz, speed, dt);
        }

        private struct Threat
        {
            public bool Found;
            public bool IsAgent;
            public EntityState Entity;
            public float Dist;
        }

        private static Threat NearestThreat(EntityState e, EntityContext ctx, float radius)
        {
            var t = new Threat { Found = false, Dist = float.MaxValue };
            // The agent.
            if (ctx.Agent.IsAlive)
            {
                float d = V2.Distance(e.Pos, ctx.Agent.Pos);
                if (d < radius) { t.Found = true; t.IsAgent = true; t.Dist = d; }
            }
            foreach (var o in ctx.Entities)
            {
                if (o == e || !o.IsAlive) continue;
                if (o.Kind != EntityKind.Wolf) continue;
                float d = V2.Distance(e.Pos, o.Pos);
                if (d < radius && d < t.Dist) { t.Found = true; t.IsAgent = false; t.Entity = o; t.Dist = d; }
            }
            return t;
        }

        private static void FleeFrom(EntityState e, EntityContext ctx, float dt, float speed,
                                     float threatX, float threatZ)
        {
            float dx = e.X - threatX, dz = e.Z - threatZ;
            Move(e, ctx, dx, dz, speed, dt);
        }

        // -- deer ---------------------------------------------------------------

        private static void StepDeer(EntityState e, EntityContext ctx, float dt)
        {
            if (e.Behavior == "Flee")
            {
                V2 fleeFrom = ThreatPos(e, ctx);
                FleeFrom(e, ctx, dt, 6f, fleeFrom.X, fleeFrom.Z);
                if (e.StateTimer <= 0f) { e.Behavior = "Graze"; e.StateTimer = 3f; }
                return;
            }
            var threat = NearestThreat(e, ctx, 15f);
            if (threat.Found)
            {
                e.Behavior = "Flee";
                e.StateTimer = 5f;
                return;
            }
            if (e.Behavior != "Graze") e.Behavior = "Wander";
            Wander(e, ctx, dt, 1.6f, 40f);
        }

        private static V2 ThreatPos(EntityState e, EntityContext ctx)
        {
            // Recompute the current threat position.
            var t = NearestThreat(e, ctx, 30f);
            if (t.Found)
            {
                if (!t.IsAgent && t.Entity != null) return t.Entity.Pos;
                if (t.IsAgent && ctx.Agent.IsAlive) return ctx.Agent.Pos;
            }
            return new V2(e.HomeX, e.HomeZ);
        }

        // -- rabbit ---------------------------------------------------------------

        private static void StepRabbit(EntityState e, EntityContext ctx, float dt)
        {
            var threat = NearestThreat(e, ctx, 8f);
            if (threat.Found)
            {
                V2 tp = ThreatPos(e, ctx);
                FleeFrom(e, ctx, dt, 4.5f, tp.X, tp.Z);
                e.Behavior = "Flee";
                e.StateTimer = 2.5f;
                return;
            }
            if (e.Behavior == "Flee" && e.StateTimer > 0f)
            {
                V2 tp = ThreatPos(e, ctx);
                FleeFrom(e, ctx, dt, 4.5f, tp.X, tp.Z);
                return;
            }
            // Hop: short bursts, then nibble.
            e.Behavior = "Hop";
            if (e.StateTimer <= 0f)
            {
                float ang = ctx.Rng.NextFloat(0f, MathF.PI * 2f);
                e.TargetX = e.X + MathF.Cos(ang) * ctx.Rng.NextFloat(2f, 5f);
                e.TargetZ = e.Z + MathF.Sin(ang) * ctx.Rng.NextFloat(2f, 5f);
                e.StateTimer = ctx.Rng.NextFloat(0.8f, 2f);
            }
            float dx = e.TargetX - e.X, dz = e.TargetZ - e.Z;
            Move(e, ctx, dx, dz, 3.2f, dt);
        }

        // -- wolf -----------------------------------------------------------------

        private static void StepWolf(EntityState e, EntityContext ctx, float dt)
        {
            e.Hunger = MathX.Clamp(e.Hunger + dt * 0.06f, 0f, 100f);

            if (e.Behavior == "Flee")
            {
                V2 tp = ThreatPos(e, ctx);
                FleeFrom(e, ctx, dt, 5.5f, tp.X, tp.Z);
                if (e.StateTimer <= 0f) { e.Behavior = "Roam"; e.StateTimer = 4f; }
                return;
            }

            // Morale: badly hurt wolves break off.
            if (e.Health < 30f && e.Behavior == "Hunt")
            {
                e.Behavior = "Flee";
                e.StateTimer = 8f;
                return;
            }

            if (e.Behavior == "Hunt")
            {
                V2 tp = ResolveHuntTarget(e, ctx);
                float d = V2.Distance(e.Pos, tp);
                if (d > 60f) { e.Behavior = "Roam"; e.TargetKind = 0; return; }
                if (d < 2.4f)
                {
                    AttackTarget(e, ctx, tp);
                }
                else
                {
                    float dx = tp.X - e.X, dz = tp.Z - e.Z;
                    Move(e, ctx, dx, dz, 4.6f, dt);
                }
                return;
            }

            // Roam: look for a meal when hungry.
            if (e.Hunger > 55f)
            {
                if (AcquirePrey(e, ctx))
                {
                    e.Behavior = "Hunt";
                    return;
                }
            }
            if (e.Behavior != "Roam") e.Behavior = "Roam";
            Wander(e, ctx, dt, 1.8f, 70f);
        }

        private static bool AcquirePrey(EntityState e, EntityContext ctx)
        {
            // Prefer deer; the agent only when very hungry and close, or weak.
            EntityState bestDeer = null;
            float bestD = 32f;
            foreach (var o in ctx.Entities)
            {
                if (!o.IsAlive || o.Kind != EntityKind.Deer) continue;
                float d = V2.Distance(e.Pos, o.Pos);
                if (d < bestD) { bestD = d; bestDeer = o; }
            }
            if (bestDeer != null)
            {
                e.TargetKind = 2; e.TargetEntityId = bestDeer.Id;
                return true;
            }
            if (ctx.Agent.IsAlive && e.Hunger > 72f)
            {
                float d = V2.Distance(e.Pos, ctx.Agent.Pos);
                if (d < 24f && ctx.Agent.Health < 75f)
                {
                    e.TargetKind = 1; e.TargetEntityId = -1;
                    return true;
                }
            }
            return false;
        }

        private static V2 ResolveHuntTarget(EntityState e, EntityContext ctx)
        {
            if (e.TargetKind == 1 && ctx.Agent.IsAlive) return ctx.Agent.Pos;
            if (e.TargetKind == 2)
            {
                foreach (var o in ctx.Entities)
                    if (o.Id == e.TargetEntityId && o.IsAlive) return o.Pos;
            }
            return new V2(e.HomeX, e.HomeZ);
        }

        private static void AttackTarget(EntityState e, EntityContext ctx, V2 tp)
        {
            // Attack cooldown via StateTimer.
            if (e.StateTimer > 0f) return;
            e.StateTimer = 1.8f;

            if (e.TargetKind == 1 && ctx.Agent.IsAlive)
            {
                // Wolf bites the agent: a couple of quick rounds.
                float dmg = ctx.Rng.NextFloat(6f, 13f);
                ctx.Agent.Health = MathX.Clamp(ctx.Agent.Health - dmg, 0f, 100f);
                ctx.Agent.Mood = MathX.Clamp(ctx.Agent.Mood - 8f, 0f, 100f);
                ctx.EventLog.Add("bite:" + dmg.ToString("F0"));
                // Being mauled teaches caution fast.
                ctx.Agent.Traits.Nudge("Caution", 0.03f);
                return;
            }
            if (e.TargetKind == 2)
            {
                foreach (var o in ctx.Entities)
                {
                    if (o.Id != e.TargetEntityId || !o.IsAlive) continue;
                    o.Health -= ctx.Rng.NextFloat(18f, 30f);
                    if (o.Health <= 0)
                    {
                        o.Behavior = "Dead";
                        e.Hunger = MathX.Clamp(e.Hunger - 55f, 0f, 100f);
                        e.Behavior = "Roam";
                        e.TargetKind = 0;
                        float dAgent = V2.Distance(e.Pos, ctx.Agent.Pos);
                        if (dAgent < 70f)
                            ctx.EventLog.Add("kill:I watched a wolf bring down a deer. The glen does not waste anything.");
                    }
                    break;
                }
            }
        }

        // -- villager ---------------------------------------------------------------

        private static void StepVillager(EntityState e, EntityContext ctx, float dt)
        {
            if (e.Behavior == "Greeted")
            {
                if (e.StateTimer <= 0f) e.Behavior = "Wander";
                return; // stands, chatting
            }
            if (e.Behavior != "Wander" && e.Behavior != "Idle") e.Behavior = "Wander";
            // Villagers amble near home, pausing often.
            if (e.StateTimer <= 0f && ctx.Rng.NextFloat() < 0.5f)
            {
                e.Behavior = "Idle";
                e.StateTimer = ctx.Rng.NextFloat(4f, 12f);
                return;
            }
            if (e.Behavior == "Idle") return;
            Wander(e, ctx, dt, 1.2f, 34f);
        }
    }

    /// <summary>
    /// Readable combat: a few rounds with hit chances, damage, and morale.
    /// Used for agent-vs-wolf fights. Wolves break at low health.
    /// </summary>
    public static class Combat
    {
        public struct RoundResult
        {
            public float AgentDamageDealt;
            public float AgentDamageTaken;
            public bool WolfFlees;
            public bool WolfDies;
            public string Log;
        }

        public static RoundResult ResolveRound(AgentState agent, EntityState wolf, bool agentHasSword, SeededRandom rng)
        {
            var r = new RoundResult();

            // Agent strikes.
            float hitChance = agentHasSword ? 0.78f : 0.6f;
            if (rng.NextFloat() < hitChance)
            {
                float dmg = agentHasSword ? rng.NextFloat(9f, 17f) : rng.NextFloat(4f, 9f);
                wolf.Health = Math.Max(0f, wolf.Health - dmg);
                r.AgentDamageDealt = dmg;
            }
            agent.Energy = MathX.Clamp(agent.Energy - 5f, 0f, 100f);

            // Morale check before the wolf replies.
            if (wolf.Health <= 0f)
            {
                r.WolfDies = true;
                r.Log = "My blow landed true and the wolf went down.";
                return r;
            }
            if (wolf.Health < 30f && rng.NextFloat() < 0.7f)
            {
                r.WolfFlees = true;
                r.Log = "Hurt and suddenly afraid, the wolf broke and ran.";
                return r;
            }

            // Wolf replies.
            float wolfHit = 0.55f;
            bool blocked = agentHasSword && rng.NextFloat() < 0.3f;
            if (!blocked && rng.NextFloat() < wolfHit)
            {
                float dmg = rng.NextFloat(5f, 12f);
                agent.Health = MathX.Clamp(agent.Health - dmg, 0f, 100f);
                agent.Mood = MathX.Clamp(agent.Mood - 6f, 0f, 100f);
                r.AgentDamageTaken = dmg;
                r.Log = "It got past my guard — pain, bright and sudden.";
            }
            else if (blocked)
            {
                r.Log = "I turned its lunge aside with the sword.";
            }
            else
            {
                r.Log = "It snapped at me and missed.";
            }
            return r;
        }
    }
}
