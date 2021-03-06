using System.Collections.Generic;
using System.Linq;
using BattleTech;
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

        // removing this will break saves 
        public int NumberOfSystems
        {
            get
            {
                return Globals.GaWSystems.Count(system => system.OwnerDef == Globals.Sim.factions[faction]);
            }
        }

        public Dictionary<string, float> warFactionAttackResources = new Dictionary<string, float>();
        public Dictionary<string, List<string>> attackTargets= new Dictionary<string, List<string>>();
        internal Dictionary<string, List<StarSystem>> systemTargets = new Dictionary<string, List<StarSystem>>();
        public List<string> defenseTargets = new List<string>();
        public Dictionary<string, bool> IncreaseAggression = new Dictionary<string, bool>();
        public List<string> adjacentFactions = new List<string>();
        private DeathListTracker deathListTrackerBackingField;

        internal DeathListTracker DeathListTracker
        {
            get => deathListTrackerBackingField ?? (deathListTrackerBackingField = Globals.WarStatusTracker.deathListTracker.Find(x => x.faction == faction));
            set => deathListTrackerBackingField = value;
        }

        [JsonConstructor]
        public WarFaction()
        {
            // deser ctor
        }

        public WarFaction(string faction)
        {
            Logger.LogDebug("WarFaction ctor: " + faction);
            this.faction = faction;
            GainedSystem = false;
            LostSystem = false;
            DaysSinceSystemAttacked = 0;
            DaysSinceSystemLost = 0;
            MonthlySystemsChanged = 0;
            TotalSystemsChanged = 0;
            PirateARLoss = 0;
            PirateDRLoss = 0;
            foreach (var startFaction in Globals.IncludedFactions)
                IncreaseAggression.Add(startFaction, false);
        }
    }

}
