using System.Collections.Generic;
using System.Linq;
using BattleTech;
using Harmony;
using Newtonsoft.Json;
using static GalaxyatWar.Helpers;

// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable InconsistentNaming

namespace GalaxyatWar
{
    public class WarStatus
    {
        public List<SystemStatus> systems = new List<SystemStatus>();
        internal List<SystemStatus> systemsByResources = new List<SystemStatus>();
        public List<DeathListTracker> deathListTracker = new List<DeathListTracker>();
        public List<WarFaction> warFactionTracker = new List<WarFaction>();
        public bool JustArrived = true;
        public bool Escalation = false;
        public bool Deployment = false;
        public WorkOrderEntry_Notification EscalationOrder;
        public int EscalationDays = 0;
        public List<string> PrioritySystems = new List<string>();
        public string CurSystem;
        public bool HotBoxTravelling;
        public bool StartGameInitialized = false;
        public bool FirstTickInitialization = true;
        public List<string> SystemChangedOwners = new List<string>();
        public List<string> HotBox = new List<string>();
        public List<string> LostSystems = new List<string>();
        public bool GaW_Event_PopUp = false;

        public List<string> HomeContendedStrings = new List<string>();
        public List<string> AbandonedSystems = new List<string>();
        public List<string> DeploymentContracts = new List<string>();
        public string DeploymentEmployer = "Marik";
        public double DeploymentInfluenceIncrease = 1.0;
        public bool PirateDeployment = false;
        public Dictionary<string, float> FullHomeContendedSystems = new Dictionary<string, float>();
        public List<string> HomeContendedSystems = new List<string>();
        public Dictionary<string, List<string>> ExternalPriorityTargets = new Dictionary<string, List<string>>();
        public List<string> FullPirateSystems = new List<string>();
        public List<string> PirateHighlight = new List<string>();
        public float PirateResources;
        public float TempPRGain;
        public float MinimumPirateResources;
        public float StartingPirateResources;
        public float LastPRGain;
        public List<string> HyadesRimGeneralPirateSystems = new List<string>();
        public int HyadesRimsSystemsTaken = 0;
        public List<string> InactiveTHRFactions = new List<string>();
        public List<string> FlashpointSystems = new List<string>();
        public List<string> NeverControl = new List<string>();
        public int ComstarCycle = 0;
        public string ComstarAlly = "";

        public WarStatus()
        {
            if (Mod.Settings.ISMCompatibility)
                Mod.Settings.IncludedFactions = new List<string>(Mod.Settings.IncludedFactions_ISM);

            SystemStatus.All.Clear();
            WarFaction.All.Clear();
            DeathListTracker.All.Clear();
            CurSystem = Mod.Globals.Sim.CurSystem.Name;
            TempPRGain = 0;
            HotBoxTravelling = false;
            HotBox = new List<string>();
            if (Mod.Settings.HyadesRimCompatible)
            {
                InactiveTHRFactions = Mod.Settings.HyadesAppearingPirates;
                FlashpointSystems = Mod.Settings.HyadesFlashpointSystems;
                NeverControl = Mod.Settings.HyadesNeverControl;
            }

            //initialize all WarFactions, DeathListTrackers, and SystemStatuses
            foreach (var faction in Mod.Globals.IncludedFactions)
            {
                var warFaction = new WarFaction(faction);
                var d = new DeathListTracker(faction)
                {
                    WarFaction = warFaction
                };
                warFaction.DeathListTracker = d;
                warFactionTracker.Add(warFaction);
                deathListTracker.Add(d);
            }

            FileLog.Log("WarFactions and DeathListTrackers created.");
            foreach (var system in Mod.Globals.Sim.StarSystems)
            {
                if (system.OwnerValue.Name == "NoFaction" || system.OwnerValue.Name == "AuriganPirates")
                    AbandonedSystems.Add(system.Name);
                var warFaction = WarFaction.All[system.OwnerValue.Name];
                if (Mod.Settings.DefensiveFactions.Contains(warFaction.faction) && Mod.Settings.DefendersUseARforDR)
                    warFaction.DefensiveResources += GetTotalAttackResources(system);
                else
                    warFaction.AttackResources += GetTotalAttackResources(system);
                warFaction.DefensiveResources += GetTotalDefensiveResources(system);
            }

            FileLog.Log("WarFaction AR/DR set.");
            var maxAR = warFactionTracker.Select(x => x.AttackResources).Max();
            var maxDR = warFactionTracker.Select(x => x.DefensiveResources).Max();

            foreach (var faction in Mod.Globals.IncludedFactions)
            {
                var warFaction = WarFaction.All[faction];
                if (Mod.Settings.DefensiveFactions.Contains(faction) && Mod.Settings.DefendersUseARforDR)
                {
                    if (!Mod.Settings.ISMCompatibility)
                        warFaction.DefensiveResources = maxAR + maxDR + Mod.Settings.BonusAttackResources[faction] +
                                                        Mod.Settings.BonusDefensiveResources[faction];
                    else
                        warFaction.DefensiveResources = maxAR + maxDR + Mod.Settings.BonusAttackResources_ISM[faction] +
                                                        Mod.Settings.BonusDefensiveResources_ISM[faction];

                    warFaction.AttackResources = 0;
                }
                else
                {
                    if (!Mod.Settings.ISMCompatibility)
                    {
                        warFaction.AttackResources = maxAR + Mod.Settings.BonusAttackResources[faction];
                        warFaction.DefensiveResources = maxDR + Mod.Settings.BonusDefensiveResources[faction];
                    }
                    else
                    {
                        warFaction.AttackResources = maxAR + Mod.Settings.BonusAttackResources_ISM[faction];
                        warFaction.DefensiveResources = maxDR + Mod.Settings.BonusDefensiveResources_ISM[faction];
                    }
                }
            }

            FileLog.Log("WarFaction bonus AR/DR set.");
            if (!Mod.Settings.ISMCompatibility)
                PirateResources = maxAR * Mod.Settings.FractionPirateResources + Mod.Settings.BonusPirateResources;
            else
                PirateResources = maxAR * Mod.Settings.FractionPirateResources_ISM + Mod.Settings.BonusPirateResources_ISM;

            MinimumPirateResources = PirateResources;
            StartingPirateResources = PirateResources;
            FileLog.Log("SystemStatus mass creation...");
            systems = new List<SystemStatus>(Mod.Globals.Sim.StarSystems.Count);
            for (var index = 0; index < Mod.Globals.Sim.StarSystems.Count; index++)
            {
                var system = Mod.Globals.Sim.StarSystems[index];
                if (Mod.Settings.ImmuneToWar.Contains(system.OwnerValue.Name))
                {
                    continue;
                }

                var systemStatus = new SystemStatus(system, system.OwnerValue.Name);
                systems.Add(systemStatus);
                if (system.Tags.Contains("planet_other_pirate") && !system.Tags.Contains("planet_region_hyadesrim"))
                {
                    FullPirateSystems.Add(system.Name);
                    PiratesAndLocals.FullPirateListSystems.Add(systemStatus);
                }

                if (system.Tags.Contains("planet_region_hyadesrim") && !FlashpointSystems.Contains(system.Name)
                                                                    && (system.OwnerValue.Name == "NoFaction" || system.OwnerValue.Name == "Locals"))
                    HyadesRimGeneralPirateSystems.Add(system.Name);
            }

            FileLog.Log("Full pirate systems created.");
            systems = systems.OrderBy(x => x.name).ToList();
            systemsByResources = systems.OrderBy(x => x.TotalResources).ToList();
            PrioritySystems = new List<string>(systems.Count);
            FileLog.Log("SystemStatus ordered lists created.");
        }

        [JsonConstructor]
        public WarStatus(bool fake = false)
        {
        }
    }
}
