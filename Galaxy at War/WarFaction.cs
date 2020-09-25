using System.Collections.Generic;
using System.Linq;
using Harmony;
using Newtonsoft.Json;

namespace GalaxyatWar
{
    public class WarFaction
    {
        public string faction;
        public bool GainedSystem;
        public bool LostSystem;
        public float DaysSinceSystemAttacked;
        public float DaysSinceSystemLost;
        public float AttackResources;
        public float DefensiveResources;
        public int MonthlySystemsChanged;
        public int TotalSystemsChanged;
        public float PirateARLoss;
        public float PirateDRLoss;
        public float AR_Against_Pirates = 0;
        public float DR_Against_Pirates = 0;
        public bool ComstarSupported = false;
        public float AR_PerPlanet = 0;
        public float DR_PerPlanet = 0;
        internal static Dictionary<string, WarFaction> All = new Dictionary<string, WarFaction>();

        // removing this will break saves 
        public int NumberOfSystems
        {
            get
            {
                return Mod.Globals.Sim.StarSystems.Count(system => system.OwnerDef == Mod.Globals.Sim.factions[faction]);
            }
        }

        public Dictionary<string, float> warFactionAttackResources = new Dictionary<string, float>();
        public Dictionary<string, List<string>> attackTargets = new Dictionary<string, List<string>>();
        public List<string> defenseTargets = new List<string>();
        public Dictionary<string, bool> IncreaseAggression = new Dictionary<string, bool>();
        public List<string> adjacentFactions = new List<string>();
        private DeathListTracker deathListTrackerBackingField;

        // cached, regenerating
        internal DeathListTracker DeathListTracker
        {
            get
            {
                if (deathListTrackerBackingField == null)
                {
                    if (!DeathListTracker.All.TryGetValue(faction, out deathListTrackerBackingField))
                    {
                        deathListTrackerBackingField = new DeathListTracker {faction = faction};
                    }
                }

                return deathListTrackerBackingField;
            }
            set => deathListTrackerBackingField = value;
        }

        [JsonConstructor]
        public WarFaction()
        {
            // deser ctor
        }

        public WarFaction(string faction)
        {
            this.faction = faction;
            GainedSystem = false;
            LostSystem = false;
            DaysSinceSystemAttacked = 0;
            DaysSinceSystemLost = 0;
            MonthlySystemsChanged = 0;
            TotalSystemsChanged = 0;
            PirateARLoss = 0;
            PirateDRLoss = 0;
            foreach (var startFaction in Mod.Globals.IncludedFactions)
                IncreaseAggression.Add(startFaction, false);
            All.Add(this.faction, this);
        }
    }
}
