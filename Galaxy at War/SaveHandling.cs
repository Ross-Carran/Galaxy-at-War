using System;
using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.Framework;
using BattleTech.Save;
using BattleTech.Save.SaveGameStructure;
using Harmony;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using static GalaxyatWar.Helpers;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace GalaxyatWar
{
    public static class SaveHandling
    {
        private static bool RePopulateMaps;

        [HarmonyPatch(typeof(SaveGameStructure), "Load", typeof(string))]
        public class SimGameStateRehydratePatch
        {
            private static void Postfix() => ResetState();
        }

        [HarmonyPatch(typeof(LoadTransitioning), "GoToMainMenu", new Type[] { })]
        public class OverlayMenuQuitPatch
        {
            private static void Postfix() => ResetState();
        }

        private static void ResetState()
        {
            // no action unless the mod's already set itself up before
            if (Mod.Globals.ModInitialized)
            {
                Mod.Globals = new Globals();
                PopulateFactions();
                RePopulateMaps = true;
                FileLog.Log("State reset.");
            }
        }

        [HarmonyPatch(typeof(Starmap), "PopulateMap", typeof(SimGameState))]
        public class StarmapPopulateMapPatch
        {
            private static void Postfix(Starmap __instance)
            {
                FileLog.Log("StarmapPopulateMapPatch");
                if (Mod.Globals.ModInitialized)
                {
                    return;
                }

                FileLog.Log("Initializing...");
                Mod.Globals.Sim = Mod.Globals.Sim ?? __instance.sim;
                Mod.Globals.SimGameInterruptManager = Mod.Globals.Sim.InterruptQueue;
                if (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                {
                    FileLog.Log("Aborting GaW loading.");
                    return;
                }

                SetFont();
                if (Mod.Settings.ResetMap)
                {
                    FileLog.Log("Resetting map due to settings.");
                    InitializeModState();
                    return;
                }

                // is there a tag?  does it deserialize properly?
                var gawTag = Mod.Globals.Sim.CompanyTags.FirstOrDefault(x => x.StartsWith("GalaxyAtWarSave"));
                if (!string.IsNullOrEmpty(gawTag))
                {
                    DeserializeWar(gawTag);
                    FileLog.Log("Validating state...");
                    ValidateState();
                    FileLog.Log("Populating lookup maps...");
                    PopulateLookupMaps();

                    // try to recover from negative DR
                    foreach (var systemStatus in Mod.Globals.WarStatusTracker.systems)
                    {
                        if (systemStatus.DefenseResources <= 0)
                        {
                            systemStatus.AttackResources = GetTotalAttackResources(systemStatus.starSystem);
                            systemStatus.DefenseResources = GetTotalDefensiveResources(systemStatus.starSystem);
                            systemStatus.TotalResources = systemStatus.AttackResources + systemStatus.DefenseResources;
                        }
                    }

                    if (Mod.Globals.WarStatusTracker.systems.Count == 0)
                    {
                        FileLog.Log("Found tag but it's broken and being respawned:");
                        FileLog.Log($"{gawTag.Substring(0, 500)}");
                        InitializeModState();
                    }
                    else
                    {
                        RebuildState();
                        Mod.Globals.WarStatusTracker.FirstTickInitialization = true;
                    }

                    // copied from WarStatus - establish any systems that are new
                    FileLog.Log("Adding any new StarSystems...");
                    AddNewStarSystems();
                }
                else
                {
                    InitializeModState();
                }

                // necessary to refresh memory maps if a game is loaded
                if (RePopulateMaps)
                {
                    PopulateLookupMaps();
                    RePopulateMaps = false;
                }

                FileLog.Log("Initialization complete.");
                Mod.Globals.ModInitialized = true;
            }
        }

        private static void SetFont()
        {
            // thanks to mpstark for this
            var fonts = Resources.FindObjectsOfTypeAll(typeof(TMP_FontAsset));
            foreach (var o in fonts)
            {
                var font = (TMP_FontAsset) o;
                if (font.name == "UnitedSansSemiExt-Light")
                {
                    Mod.Globals.Font = font;
                }
            }
        }

        private static void PopulateLookupMaps()
        {
            if (WarFaction.All == null)
            {
                WarFaction.All = new Dictionary<string, WarFaction>();
            }

            if (WarFaction.All.Count != Mod.Globals.IncludedFactions.Count)
            {
                WarFaction.All.Clear();
                foreach (var warFaction in Mod.Globals.WarStatusTracker.warFactionTracker)
                {
                    WarFaction.All.Add(warFaction.faction, warFaction);
                }
            }

            if (DeathListTracker.All == null)
            {
                DeathListTracker.All = new Dictionary<string, DeathListTracker>();
            }

            if (DeathListTracker.All.Count != Mod.Globals.IncludedFactions.Count)
            {
                DeathListTracker.All.Clear();
                foreach (var deathListTracker in Mod.Globals.WarStatusTracker.deathListTracker)
                {
                    DeathListTracker.All.Add(deathListTracker.faction, deathListTracker);
                }
            }

            if (SystemStatus.All == null)
            {
                SystemStatus.All = new Dictionary<string, SystemStatus>();
            }

            if (SystemStatus.All.Count != Mod.Globals.Sim.StarSystems.Count)
            {
                SystemStatus.All.Clear();
                foreach (var systemStatus in Mod.Globals.WarStatusTracker.systems)
                {
                    SystemStatus.All.Add(systemStatus.name, systemStatus);
                }
            }
        }

        // remove from the war any systems which are now immune
        private static void ValidateState()
        {
            // TODO make sure WarFaction and DeathListTrackers exist (new factions added to existing save or something?)

            if (Mod.Globals.Sim.StarSystems.Count < Mod.Globals.WarStatusTracker.systems.Count)
            {
                for (var index = 0; index < Mod.Globals.WarStatusTracker.systems.Count; index++)
                {
                    var systemStatus = Mod.Globals.WarStatusTracker.systems[index];
                    if (Mod.Settings.ImmuneToWar.Contains(systemStatus.OriginalOwner))
                    {
                        FileLog.Log($"Removed: {systemStatus.starSystem.Name,-15} -> Immune to war, owned by {systemStatus.starSystem.OwnerValue.Name}.");
                        Mod.Globals.WarStatusTracker.systems.Remove(systemStatus);
                    }
                }
            }

            // remove from trackers any immune factions
            foreach (var deathListTracker in Mod.Globals.WarStatusTracker.deathListTracker)
            {
                if (deathListTracker.Enemies.Any(x => Mod.Settings.ImmuneToWar.Contains(x)))
                {
                    FileLog.Log($"Pruning immune factions from deathListTracker of {deathListTracker.faction}...");
                }

                for (var i = 0; i < deathListTracker.Enemies.Count; i++)
                {
                    if (Mod.Settings.ImmuneToWar.Contains(deathListTracker.Enemies[i]))
                    {
                        FileLog.Log($"Removing enemy {deathListTracker.Enemies[i]} from {deathListTracker.faction}.");
                        deathListTracker.Enemies.Remove(deathListTracker.Enemies[i]);
                    }
                }
            }
        }

        // if the war is missing systems, add them
        private static void AddNewStarSystems()
        {
            for (var index = 0; index < Mod.Globals.Sim.StarSystems.Count; index++)
            {
                var system = Mod.Globals.Sim.StarSystems[index];
                if (Mod.Settings.ImmuneToWar.Contains(Mod.Globals.Sim.StarSystems[index].OwnerValue.Name) ||
                    Mod.Globals.WarStatusTracker.systems.Any(x => x.starSystem == system))
                {
                    continue;
                }

                FileLog.Log($"Trying to add {system.Name}, owner {system.OwnerValue.Name}.");
                var systemStatus = new SystemStatus(system, system.OwnerValue.Name);
                Mod.Globals.WarStatusTracker.systems.Add(systemStatus);
                if (system.Tags.Contains("planet_other_pirate") && !system.Tags.Contains("planet_region_hyadesrim"))
                {
                    Mod.Globals.WarStatusTracker.FullPirateSystems.Add(system.Name);
                    PiratesAndLocals.FullPirateListSystems.Add(systemStatus);
                }

                if (system.Tags.Contains("planet_region_hyadesrim") &&
                    !Mod.Globals.WarStatusTracker.FlashpointSystems.Contains(system.Name) &&
                    (system.OwnerValue.Name == "NoFaction" || system.OwnerValue.Name == "Locals"))
                    Mod.Globals.WarStatusTracker.HyadesRimGeneralPirateSystems.Add(system.Name);
            }
        }

        internal static void InitializeModState()
        {
            FileLog.Log("Spawning new instance...");
            Mod.Globals.WarStatusTracker = new WarStatus();
            PopulateLookupMaps();
            FileLog.Log("New global state created.");
            // TODO is this value unchanging?  this is wrong if not
            Mod.Globals.WarStatusTracker.systemsByResources =
                Mod.Globals.WarStatusTracker.systems.OrderBy(x => x.TotalResources).ToList();
            SystemDifficulty();

            if (!Mod.Globals.WarStatusTracker.StartGameInitialized)
            {
                FileLog.Log($"Refreshing contracts at spawn ({Mod.Globals.Sim.CurSystem.Name}).");
                UpdateInfluenceAndContendedSystems(false);
                Mod.Globals.NeedsProcessing = true;
                HotSpots.ReRollCustomContracts();
                Mod.Globals.NeedsProcessing = false;
                StarmapScreen.isDirty = true;
                Mod.Globals.WarStatusTracker.StartGameInitialized = true;
            }

            Mod.Globals.WarStatusTracker.FirstTickInitialization = true;
            Mod.Globals.WarStatusTracker.StartGameInitialized = false;
            WarTick.Tick(true, true);
        }

        private static void DeserializeWar(string gawTag)
        {
            FileLog.Log("DeserializeWar");
            var tag = gawTag.Substring(15);
            //File.WriteAllText("mods/GalaxyAtWar/tag.txt", tag);
            Mod.Globals.WarStatusTracker = JsonConvert.DeserializeObject<WarStatus>(tag);
            FileLog.Log($">>> Deserialization complete (Size after load: {tag.Length / 1024}kb)");
        }

        [HarmonyPatch(typeof(SimGameState), "Dehydrate")]
        public static class SimGameStateDehydratePatch
        {
            public static void Prefix(SimGameState __instance)
            {
                FileLog.Log("SimGameStateDehydratePatch");
                Mod.Globals.Sim = __instance;
                if (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                    return;

                //if (Mod.Globals.WarStatusTracker == null)
                //{
                //    Mod.Globals.WarStatusTracker = new WarStatus();
                //    SystemDifficulty();
                //    WarTick.Tick(true, true);
                //    SerializeWar();
                //}
                //else
                //{
                ConvertToSave();
                SerializeWar();
                //}
            }

            public static void Postfix()
            {
                if (Mod.Settings.CleanUpCompanyTag)
                {
                    Mod.Globals.Sim.CompanyTags.Where(tag =>
                        tag.StartsWith("GalaxyAtWar")).Do(x => Mod.Globals.Sim.CompanyTags.Remove(x));
                }
            }
        }

        private static void SerializeWar()
        {
            FileLog.Log("SerializeWar");
            var gawTag = Mod.Globals.Sim.CompanyTags.FirstOrDefault(x => x.StartsWith("GalaxyAtWar"));
            Mod.Globals.Sim.CompanyTags.Remove(gawTag);
            gawTag = "GalaxyAtWarSave" + JsonConvert.SerializeObject(Mod.Globals.WarStatusTracker);
            Mod.Globals.Sim.CompanyTags.Add(gawTag);
            FileLog.Log($">>> Serialization complete (object size: {gawTag.Length / 1024}kb)");
        }

        private static void RebuildState()
        {
            FileLog.Log("RebuildState");
            HotSpots.ExternalPriorityTargets.Clear();
            HotSpots.FullHomeContendedSystems.Clear();
            HotSpots.HomeContendedSystems.Clear();
            var starSystemDictionary = Mod.Globals.Sim.StarSystemDictionary;

            // TODO make sure this is being updated elsewhere (should be but worth double-checking)
            Mod.Globals.WarStatusTracker.systemsByResources =
                Mod.Globals.WarStatusTracker.systems.OrderBy(x => x.TotalResources).ToList();

            SystemDifficulty();
            try
            {
                if (Mod.Settings.ResetMap)
                {
                    Mod.Globals.Sim.CompanyTags.Where(tag =>
                        tag.StartsWith("GalaxyAtWar")).Do(x => Mod.Globals.Sim.CompanyTags.Remove(x));
                    return;
                }

                for (var i = 0; i < Mod.Globals.WarStatusTracker.systems.Count; i++)
                {
                    StarSystemDef systemDef;
                    var system = Mod.Globals.WarStatusTracker.systems[i];
                    if (starSystemDictionary.ContainsKey(system.CoreSystemID))
                    {
                        systemDef = starSystemDictionary[system.CoreSystemID].Def;
                    }
                    else
                    {
                        FileLog.Log($"BOMB {system.name} not in StarSystemDictionary, removing it from WarStatusTracker.systems");
                        Mod.Globals.WarStatusTracker.systems.Remove(system);
                        continue;
                    }

                    var systemOwner = systemDef.OwnerValue.Name;
                    // needs to be refreshed since original declaration in Mod
                    var ownerValue = Mod.Globals.FactionValues.Find(x => x.Name == system.owner);
                    systemDef.OwnerValue = ownerValue;
                    systemDef.factionShopOwnerID = system.owner;
                    RefreshContractsEmployersAndTargets(system);
                    if (system.influenceTracker.Keys.Contains("AuriganPirates") && !system.influenceTracker.Keys.Contains("NoFaction"))
                    {
                        system.influenceTracker.Add("NoFaction", system.influenceTracker["AuriganPirates"]);
                        system.influenceTracker.Remove("AuriganPirates");
                    }
                    //if (!system.influenceTracker.Keys.Contains("NoFaction"))
                    //    system.influenceTracker.Add("NoFaction", 0);

                    if (systemDef.OwnerValue.Name != systemOwner && systemOwner != "NoFaction")
                    {
                        if (systemDef.SystemShopItems.Count != 0)
                        {
                            var tempList = systemDef.SystemShopItems;
                            tempList.Add(Mod.Settings.FactionShops[system.owner]);

                            systemDef.SystemShopItems = systemDef.SystemShopItems;
                        }

                        if (systemDef.FactionShopItems != null)
                        {
                            systemDef.FactionShopOwnerValue = Mod.Globals.FactionValues.Find(x => x.Name == system.owner);
                            systemDef.factionShopOwnerID = system.owner;
                            var factionShopItems = systemDef.FactionShopItems;
                            if (factionShopItems.Contains(Mod.Settings.FactionShopItems[systemOwner]))
                                factionShopItems.Remove(Mod.Settings.FactionShopItems[systemOwner]);
                            factionShopItems.Add(Mod.Settings.FactionShopItems[system.owner]);
                            systemDef.FactionShopItems = factionShopItems;
                        }
                    }
                }

                foreach (var faction in Mod.Globals.WarStatusTracker.ExternalPriorityTargets.Keys)
                {
                    HotSpots.ExternalPriorityTargets.Add(faction, new List<StarSystem>());
                    foreach (var system in Mod.Globals.WarStatusTracker.ExternalPriorityTargets[faction])
                        HotSpots.ExternalPriorityTargets[faction].Add(starSystemDictionary[system]);
                }

                foreach (var system in Mod.Globals.WarStatusTracker.FullHomeContendedSystems)
                {
                    HotSpots.FullHomeContendedSystems.Add(new KeyValuePair<StarSystem, float>(starSystemDictionary[system.Key], system.Value));
                }

                foreach (var system in Mod.Globals.WarStatusTracker.HomeContendedSystems)
                {
                    HotSpots.HomeContendedSystems.Add(starSystemDictionary[system]);
                }

                foreach (var starSystem in Mod.Globals.WarStatusTracker.FullPirateSystems)
                {
                    PiratesAndLocals.FullPirateListSystems.Add(SystemStatus.All[starSystem]);
                }

                foreach (var deathListTracker in Mod.Globals.WarStatusTracker.deathListTracker)
                {
                    AdjustDeathList(deathListTracker, true);
                }

                foreach (var defensiveFaction in Mod.Settings.DefensiveFactions)
                {
                    if (WarFaction.All[defensiveFaction] == null)
                        continue;

                    var targetFaction = WarFaction.All[defensiveFaction];
                    if (targetFaction.AttackResources != 0)
                    {
                        targetFaction.DefensiveResources += targetFaction.AttackResources;
                        targetFaction.AttackResources = 0;
                    }
                }

                foreach (var contract in Mod.Globals.Sim.CurSystem.activeSystemBreadcrumbs)
                {
                    if (Mod.Globals.WarStatusTracker.DeploymentContracts.Contains(contract.Override.contractName))
                        contract.Override.contractDisplayStyle = ContractDisplayStyle.BaseCampaignStory;
                }
            }
            catch (Exception ex)
            {
                FileLog.Log(ex.ToString());
            }
        }

        private static void ConvertToSave()
        {
            FileLog.Log("ConvertToSave");
            Mod.Globals.WarStatusTracker.ExternalPriorityTargets.Clear();
            Mod.Globals.WarStatusTracker.FullHomeContendedSystems.Clear();
            Mod.Globals.WarStatusTracker.HomeContendedSystems.Clear();
            Mod.Globals.WarStatusTracker.FullPirateSystems.Clear();
            foreach (var faction in HotSpots.ExternalPriorityTargets.Keys)
            {
                Mod.Globals.WarStatusTracker.ExternalPriorityTargets.Add(faction, new List<string>());
                foreach (var system in HotSpots.ExternalPriorityTargets[faction])
                    Mod.Globals.WarStatusTracker.ExternalPriorityTargets[faction].Add(system.Def.CoreSystemID);
            }

            foreach (var system in HotSpots.FullHomeContendedSystems)
                Mod.Globals.WarStatusTracker.FullHomeContendedSystems.Add(system.Key.Def.CoreSystemID, system.Value);
            foreach (var system in HotSpots.HomeContendedSystems)
                Mod.Globals.WarStatusTracker.HomeContendedSystems.Add(system.Def.CoreSystemID);
        }
    }
}
