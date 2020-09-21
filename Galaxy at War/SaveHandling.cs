using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleTech;
using BattleTech.Framework;
using BattleTech.Save.SaveGameStructure;
using Harmony;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using static GalaxyatWar.Logger;
using static GalaxyatWar.Helpers;

// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace GalaxyatWar
{
    public static class SaveHandling
    {
        [HarmonyPatch(typeof(SaveGameStructure), "Load", typeof(string))]
        public class SimGameStateRehydratePatch
        {
            private static void Postfix()
            {
                // no action unless the mod's already set itself up before
                if (Mod.Globals.ModInitialized)
                {
                    Mod.Globals = new Globals();
                    CopySettingsToState();
                    LogDebug("State reset.");
                }
            }
        }

        [HarmonyPatch(typeof(Starmap), "PopulateMap", typeof(SimGameState))]
        public class StarmapPopulateMapPatch
        {
            private static void Postfix(Starmap __instance)
            {
                LogDebug("PopulateMap");
                if (Mod.Globals.ModInitialized)
                {
                    return;
                }

                LogDebug("Initializing...");
                Mod.Globals.Sim = __instance.sim;
                Mod.Globals.SimGameInterruptManager = Mod.Globals.Sim.InterruptQueue;
                Mod.Globals.GaWSystems = Mod.Globals.Sim.StarSystems.Where(x =>
                    !Mod.Settings.ImmuneToWar.Contains(x.OwnerValue.Name)).ToList();
                if (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                {
                    LogDebug("Aborting GaW loading.");
                    return;
                }

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

                if (Mod.Settings.ResetMap)
                {
                    LogDebug("Resetting map due to settings.");
                    Spawn();
                    return;
                }

                // is there a tag?  does it deserialize properly?
                var gawTag = Mod.Globals.Sim.CompanyTags.FirstOrDefault(x => x.StartsWith("GalaxyAtWarSave"));
                if (!string.IsNullOrEmpty(gawTag))
                {
                    DeserializeWar();
                    // cleaning up old tag data
                    ValidateState();

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
                        LogDebug("Found tag but it's broken and being respawned:");
                        LogDebug($"{gawTag.Substring(0, 500)}");
                        Spawn();
                    }
                    else
                    {
                        RebuildState();
                        Mod.Globals.WarStatusTracker.FirstTickInitialization = true;
                    }

                    // copied from WarStatus - establish any systems that are new
                    AddNewStarSystems();
                }
                else
                {
                    Spawn();
                }

                Mod.Globals.ModInitialized = true;
            }

            // remove from the war any systems which are now immune
            private static void ValidateState()
            {
                if (Mod.Globals.GaWSystems.Count < Mod.Globals.WarStatusTracker.systems.Count)
                {
                    for (var index = 0; index < Mod.Globals.WarStatusTracker.systems.Count; index++)
                    {
                        var systemStatus = Mod.Globals.WarStatusTracker.systems[index];
                        if (Mod.Settings.ImmuneToWar.Contains(systemStatus.OriginalOwner))
                        {
                            LogDebug($"Removed: {systemStatus.starSystem.Name,-15} -> Immune to war, owned by {systemStatus.starSystem.OwnerValue.Name}.");
                            Mod.Globals.WarStatusTracker.systems.Remove(systemStatus);
                        }
                    }
                }

                // remove from trackers any immune factions
                foreach (var deathListTracker in Mod.Globals.WarStatusTracker.deathListTracker)
                {
                    if (deathListTracker.Enemies.Any(x => Mod.Settings.ImmuneToWar.Contains(x)))
                    {
                        LogDebug($"Pruning immune factions from deathListTracker of {deathListTracker.faction}.");
                    }

                    for (var i = 0; i < deathListTracker.Enemies.Count; i++)
                    {
                        if (Mod.Settings.ImmuneToWar.Contains(deathListTracker.Enemies[i]))
                        {
                            LogDebug($"Removing enemy {deathListTracker.Enemies[i]} from {deathListTracker.faction}.");
                            deathListTracker.Enemies.Remove(deathListTracker.Enemies[i]);
                        }
                    }
                }

                // doubtful this is needed...
                var _ = new Dictionary<string, float>();
                foreach (var kvp in Mod.Globals.WarStatusTracker.FullHomeContendedSystems)
                {
                    if (!Mod.Settings.ImmuneToWar.Contains(kvp.Key))
                    {
                        _.Add(kvp.Key, kvp.Value);
                    }
                    else
                    {
                        LogDebug($"Removing {kvp.Key} from FullHomeContendedSystems, as they are immune to war.");
                    }
                }

                Mod.Globals.WarStatusTracker.FullHomeContendedSystems = _;
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

                    LogDebug($"Trying to add {system.Name}, owner {system.OwnerValue.Name}.");
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

            private static void Spawn()
            {
                LogDebug("Spawning new instance.");
                Mod.Globals.WarStatusTracker = new WarStatus();
                LogDebug("New global state created.");
                // TODO is this value unchanging?  this is wrong if not
                Mod.Globals.WarStatusTracker.systemsByResources =
                    Mod.Globals.WarStatusTracker.systems.OrderBy(x => x.TotalResources).ToList();
                if (!Mod.Globals.WarStatusTracker.StartGameInitialized)
                {
                    LogDebug($"Refreshing contracts at spawn ({Mod.Globals.Sim.CurSystem.Name}).");
                    var cmdCenter = Mod.Globals.Sim.RoomManager.CmdCenterRoom;
                    Mod.Globals.Sim.CurSystem.GenerateInitialContracts(() => cmdCenter.OnContractsFetched());
                    Mod.Globals.WarStatusTracker.StartGameInitialized = true;
                }

                SystemDifficulty();
                Mod.Globals.WarStatusTracker.FirstTickInitialization = true;
                Mod.Globals.WarStatusTracker.StartGameInitialized = false;
                WarTick.Tick(true, true);
            }
        }

        private static void DeserializeWar()
        {
            LogDebug("DeserializeWar");
            var tag = Mod.Globals.Sim.CompanyTags.First(x => x.StartsWith("GalaxyAtWarSave{")).Substring(15);
            //File.WriteAllText("mods/GalaxyAtWar/tag.txt", tag);
            Mod.Globals.WarStatusTracker = JsonConvert.DeserializeObject<WarStatus>(tag);
            LogDebug($">>> Deserialization complete (Size after load: {tag.Length / 1024}kb)");
        }

        [HarmonyPatch(typeof(SimGameState), "Dehydrate")]
        public static class SimGameStateDehydratePatch
        {
            public static void Prefix(SimGameState __instance)
            {
                LogDebug("Dehydrate");
                Mod.Globals.Sim = __instance;
                if (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                    return;

                if (Mod.Globals.WarStatusTracker == null)
                {
                    Mod.Globals.WarStatusTracker = new WarStatus();
                    SystemDifficulty();
                    WarTick.Tick(true, true);
                    SerializeWar();
                }
                else
                {
                    ConvertToSave();
                    SerializeWar();
                }
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

        internal static void SerializeWar()
        {
            LogDebug("SerializeWar");
            var gawTag = Mod.Globals.Sim.CompanyTags.FirstOrDefault(x => x.StartsWith("GalaxyAtWar"));
            Mod.Globals.Sim.CompanyTags.Remove(gawTag);
            gawTag = "GalaxyAtWarSave" + JsonConvert.SerializeObject(Mod.Globals.WarStatusTracker);
            Mod.Globals.Sim.CompanyTags.Add(gawTag);
            LogDebug($">>> Serialization complete (object size: {gawTag.Length / 1024}kb)");
        }

        public static void RebuildState()
        {
            LogDebug("RebuildState");
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
                        LogDebug($"BOMB {system.name} not in StarSystemDictionary, removing it from WarStatusTracker.systems");
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

                            Traverse.Create(systemDef).Property("SystemShopItems").SetValue(systemDef.SystemShopItems);
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
                    PiratesAndLocals.FullPirateListSystems.Add(Mod.Globals.WarStatusTracker.systems.Find(x => x.name == starSystem));
                }

                foreach (var deathListTracker in Mod.Globals.WarStatusTracker.deathListTracker)
                {
                    AdjustDeathList(deathListTracker, true);
                }

                foreach (var defensiveFaction in Mod.Settings.DefensiveFactions)
                {
                    if (Mod.Globals.WarStatusTracker.warFactionTracker.Find(x => x.faction == defensiveFaction) == null)
                        continue;

                    var targetFaction = Mod.Globals.WarStatusTracker.warFactionTracker.Find(x => x.faction == defensiveFaction);

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
                Error(ex);
            }
        }

        public static void ConvertToSave()
        {
            LogDebug("ConvertToSave");
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
