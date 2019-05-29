using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BattleTech;
using Harmony;
using Newtonsoft.Json;
using UnityEngine;
using static Logger;
using Random = System.Random;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable InconsistentNaming

public class Core
{
    #region Init

    public static void Init(string modDir, string settings)
    {
        var harmony = HarmonyInstance.Create("ca.gnivler.BattleTech.DevMod");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        // read settings
        try
        {
            Settings = JsonConvert.DeserializeObject<ModSettings>(settings);
            Settings.modDirectory = modDir;
        }
        catch (Exception)
        {
            Settings = new ModSettings();
        }

        // blank the logfile
        Clear();
        PrintObjectFields(Settings, "Settings");
    }

    // logs out all the settings and their values at runtime
    internal static void PrintObjectFields(object obj, string name)
    {
        LogDebug($"[START {name}]");

        var settingsFields = typeof(ModSettings)
            .GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        foreach (var field in settingsFields)
        {
            if (field.GetValue(obj) is IEnumerable &&
                !(field.GetValue(obj) is string))
            {
                LogDebug(field.Name);
                foreach (var item in (IEnumerable) field.GetValue(obj))
                {
                    LogDebug("\t" + item);
                }
            }
            else
            {
                LogDebug($"{field.Name,-30}: {field.GetValue(obj)}");
            }
        }

        LogDebug($"[END {name}]");
    }

    #endregion

    internal static ModSettings Settings;
    public static WarStatus WarStatus;

    [HarmonyPatch(typeof(SimGameState), "OnDayPassed")]
    public static class SimGameState_OnDayPassed_Patch
    {
        internal static readonly SimGameState sim = UnityGameInstance.BattleTechGame.Simulation;

        static void Prefix(SimGameState __instance, int timeLapse)
        {
            // already have a save?
            var fileName = $"WarStatus_{sim.InstanceGUID}.json";

            if (WarStatus == null && File.Exists("Mods\\GalaxyAtWar\\" + fileName))
            {
                Log(">>> Loading " + fileName);
                WarStatus = SaveHandling.DeserializeWar();

                WarStatus.attackTargets.Clear();
                WarStatus.defenseTargets.Clear();

                foreach (var faction in sim.FactionsDict.Keys)
                    LogPotentialTargets(faction);
            }

            //// first time setup if not
            if (WarStatus == null)
            {
                Log(">>> First-time initialization");

                //This generates the initial distribution of Influence amongst the systems.
                WarStatus = new WarStatus();

                foreach (var faction in sim.FactionsDict.Keys)
                    LogPotentialTargets(faction);
            }

            // if (sim.DayRemainingInQuarter % Settings.WarFrequency != 0)
            //     return;

            Log(">>> PROC");

            // Proc effects

            foreach (var system in WarStatus.systems)
            {
                system.CalculateNeighbours(sim);
            }

            //Add resources for adjacent systems
            try
            {
                foreach (var system in WarStatus.systems)
                {
                    //Log($"\n\n{system.name}");
                    foreach (var neighbor in system.neighborSystems)
                    {
                        //Log(neighbor.Key.ToString());
                        //Log("Dictionary:");
                        //foreach (var kvp in system.influenceTracker)
                        //    Log($"\t{kvp.Key}: {kvp.Value}");
                        //Log(system.influenceTracker.ContainsKey(neighbor.Key).ToString());

                        if (system.influenceTracker.ContainsKey(neighbor.Key))
                            system.influenceTracker[neighbor.Key] += neighbor.Value;
                        else
                            system.influenceTracker.Add(neighbor.Key, neighbor.Value);
                    }

                    Log($"\n{system.name} influenceTracker:");
                    system.influenceTracker.Do(x => Log($"{x.Key.ToString()} {x.Value}"));
                }
            }

            catch (Exception ex)
            {
                Log("\n2");
                Error(ex);
            }

            //Log("Refreshing Resources");
            try
            {
                RefreshResources(__instance);
            }
            catch (Exception ex)
            {
                Log("3");
                Error(ex);
            }

            try
            {
                // attacking
                Log($"WarStatus.attackTargets {WarStatus.attackTargets.Count}");
                if (WarStatus.attackTargets.Count > 0)
                {
                    foreach (var faction in WarStatus.attackTargets.Keys)
                        AllocateAttackResources(faction);

                    //defending
                    foreach (var faction in WarStatus.attackTargets.Keys)
                        AllocateDefensiveResources(faction);
                }
            }
            catch (Exception ex)
            {
                Log("5");
                Error(ex);
            }

            try
            {
                UpdateInfluenceFromAttacks(sim);
            }
            catch (Exception ex)
            {
                Log("6");
                Error(ex);
            }

            SaveHandling.SerializeWar();
            Log(">>> DONE PROC");
        }
    }

    public static void LogPotentialTargets(Faction faction)
    {
        if (WarStatus.attackTargets.Keys.Contains(faction))
        {
            Log("------------------------------------------------------");
            Log(faction.ToString());
            Log("Attack Targets");
            foreach (StarSystem attackedSystem in WarStatus.attackTargets[faction])
                Log($"{attackedSystem.Name,-30} : {attackedSystem.Owner}");
        }
        else
        {
            Log($"No Attack Targets for {faction.ToString()}!");
        }

        if (WarStatus.defenseTargets.Keys.Contains(faction))
        {
            Log("------------------------------------------------------");
            Log(faction.ToString());
            Log("Defense Targets");
            foreach (StarSystem defensedsystem in WarStatus.defenseTargets[faction])
                Log($"{defensedsystem.Name,-30} : {defensedsystem.Owner}");
        }
        else
        {
            Log($"No Defense Targets for {faction.ToString()}!");
        }
    }

    public static void DivideAttackResources(SimGameState sim, Faction faction)
    {
        Dictionary<Faction, float> uniqueFactions = new Dictionary<Faction, float>();
        foreach (StarSystem attackSystem in WarStatus.attackTargets[faction])
        {
            if (!uniqueFactions.ContainsKey(attackSystem.Owner))
                uniqueFactions.Add(attackSystem.Owner, 0f);
        }

        RefreshResources(sim);
        var killList = WarStatus.relationTracker.factions.First(f => f.faction == faction).killList;
        WarFaction warFaction = WarStatus.factionTracker.Find(x => x.faction == faction);
        float resources = warFaction.resources;
        float total = uniqueFactions.Values.Sum();

        foreach (Faction tempfaction in uniqueFactions.Keys)
        {
            uniqueFactions[tempfaction] = killList[tempfaction] * resources / total;
        }

        WarStatus.attackResources.Add(faction, uniqueFactions);
    }

    public static void AllocateAttackResources(Faction faction)
    {
        var random = new Random();
        Log("AllocateAttackResources faction: " + faction);
        try
        {
            if (!WarStatus.attackResources.ContainsKey(faction)) return;
            Log(WarStatus.attackResources.ContainsKey(faction).ToString());

            //Go through the different resources allocated from attacking faction to spend against each targetfaction
            foreach (var targetfaction in WarStatus.attackResources[faction].Keys)
            {
                var attacklist = new List<StarSystem>();

                //Generate the list of all systems being attacked by faction and pulls out the ones that match the targetfaction
                foreach (var system in WarStatus.attackTargets[faction])
                    if (WarStatus.attackResources[faction].ContainsKey(system.Owner))
                        attacklist.Add(system);

                //Allocate all the resources against the targetfaction to systems controlled by targetfaction.
                var i = 0;
                do
                {
                    var rand = random.Next(0, attacklist.Count);
                    var systemStatus = WarStatus.systems.First(f => f.name == attacklist[rand].Name);
                    systemStatus.influenceTracker[faction] += 1;
                } while (i < WarStatus.attackResources[faction][targetfaction]);
            }
        }
        catch (Exception ex)
        {
            Error(ex);
            Application.Quit();
        }
    }

    public static void AllocateDefensiveResources(Faction faction)
    {
        var random = new Random();
        if (WarStatus.factionTracker.Find(x => x.faction == faction) == null)
            return;

        WarFaction warFaction = WarStatus.factionTracker.Find(x => x.faction == faction);
        var DefensiveResources = warFaction.DefensiveResources;
        if (!WarStatus.defenseTargets.ContainsKey(faction))
            return;

        var systems = WarStatus.defenseTargets[faction];

        // TODO fix so != 0
        while (DefensiveResources > 0)
        {
            float highest = 0f;
            Faction highestFaction = faction;
            var rand = random.Next(0, systems.Count());
            var systemStatus = WarStatus.systems.First(f => f.name == systems[rand].Name);

            foreach (Faction tempfaction in systemStatus.influenceTracker.Keys)
            {
                if (systemStatus.influenceTracker[tempfaction] > highest)
                {
                    highest = systemStatus.influenceTracker[tempfaction];
                    highestFaction = tempfaction;
                }
            }

            if (highestFaction == faction)
            {
                systemStatus.influenceTracker[faction] += 1;
                DefensiveResources -= 1;
            }
            else
            {
                var diffRes = systemStatus.influenceTracker[highestFaction] - systemStatus.influenceTracker[faction];
                if (diffRes >= DefensiveResources)
                {
                    systemStatus.influenceTracker[faction] += DefensiveResources;
                    DefensiveResources = 0;
                }
                else
                {
                    systemStatus.influenceTracker[faction] += diffRes;
                    DefensiveResources -= diffRes;
                }
            }

            //Log($"DefensiveResources: {DefensiveResources}");
        }
    }

    public static void ChangeSystemOwnership(SimGameState sim, StarSystem system, Faction faction, bool ForceFlip)
    {
        if (faction != system.Owner || ForceFlip)
        {
            system.Def.Tags.Remove(Settings.FactionTags[system.Owner]);
            system.Def.Tags.Add(Settings.FactionTags[faction]);
            system.Def.SystemShopItems.Add(Settings.FactionShops[faction]);
            if (system.Def.FactionShopItems != null)
            {
                Traverse.Create(system.Def).Property("FactionShopOwner").SetValue(faction);
                system.Def.FactionShopItems.Remove(Settings.FactionShopItems[system.Def.Owner]);
                system.Def.FactionShopItems.Add(Settings.FactionShopItems[faction]);
            }

            system.Def.ContractEmployers.Clear();
            system.Def.ContractTargets.Clear();
            List<Faction> ContractEmployers = new List<Faction>();
            ContractEmployers.Add(system.Owner);

            foreach (var systemNeighbor in sim.Starmap.GetAvailableNeighborSystem(system))
            {
                if (!ContractEmployers.Contains(systemNeighbor.Owner))
                    ContractEmployers.Add(systemNeighbor.Owner);
            }

            Traverse.Create(system.Def).Property("ContractEmployers").SetValue(ContractEmployers);

            if (ContractEmployers.Count() > 1)
            {
                Traverse.Create(system.Def).Property("ContractTargets").SetValue(ContractEmployers);
            }
            else
            {
                // TODO, not sure this generates intended result (a list of enemies?)
                FactionDef FactionEnemies;
                FactionEnemies = sim.FactionsDict[faction];

                Traverse.Create(system.Def).Property("ContractTargets").SetValue(FactionEnemies.Enemies.ToList());
            }

            Traverse.Create(system.Def.Owner).Property("Owner").SetValue(faction);
        }
    }

    private static void UpdateInfluenceFromAttacks(SimGameState sim)
    {
        foreach (var systemstatus in WarStatus.systems)
        {
            var tempDict = new Dictionary<Faction, float>();
            var totalInfluence = systemstatus.influenceTracker.Values.Sum();
            var highest = 0f;
            var highestfaction = systemstatus.owner;
            Log($"Attacking status for {systemstatus.name}");
            foreach (var kvp in systemstatus.influenceTracker)
            {
                tempDict.Add(kvp.Key, kvp.Value / totalInfluence * 100);
                Log($"{kvp.Key}: {tempDict[kvp.Key]}");
                if (kvp.Value > highest)
                {
                    highest = kvp.Value;
                    highestfaction = kvp.Key;
                }
            }

            systemstatus.influenceTracker = tempDict;
            //Need to add changes to the Kill List. Here is a good spot. 
            if (highestfaction != systemstatus.owner)
            {
                var previousOwner = systemstatus.owner;
                var starSystem = sim.StarSystems.Find(x => x.Name == systemstatus.name);
                if (starSystem != null)
                    ChangeSystemOwnership(sim, starSystem, highestfaction, true);
                else
                {
                    Log("+=======+++== NULL");
                }

                Log(">>> Ownership changed to " + highestfaction);
                if (highestfaction == Faction.NoFaction || highestfaction == Faction.Locals)
                {
                    Log("NoFaction or Locals, continuing");
                    continue;
                }

                // BUG NRE on deserialization
                var WarFactionWinner = WarStatus.factionTracker.Find(x => x.faction == highestfaction);
                if (WarFactionWinner != null)
                    WarFactionWinner.DaysSinceSystemAttacked = 0;

                try
                {
                    var WarFactionLoser = WarStatus.factionTracker.Find(x => x.faction == previousOwner);
                    if (WarFactionLoser != null)
                        WarFactionLoser.DaysSinceSystemLost = 0;
                }
                catch (Exception ex)
                {
                    Log("TWO");
                    Error(ex);
                }
            }
        }
    }

    public static int GetTotalResources(StarSystem system)
    {
        int result = 0;
        if (system.Tags.Contains("planet_industry_poor"))
            result += Settings.planet_industry_poor;
        if (system.Tags.Contains("planet_industry_mining"))
            result += Settings.planet_industry_mining;
        if (system.Tags.Contains("planet_industry_rich"))
            result += Settings.planet_industry_rich;
        if (system.Tags.Contains("planet_other_comstar"))
            result += Settings.planet_other_comstar;
        if (system.Tags.Contains("planet_industry_manufacturing"))
            result += Settings.planet_industry_manufacturing;
        if (system.Tags.Contains("planet_industry_research"))
            result += Settings.planet_industry_research;
        if (system.Tags.Contains("planet_other_starleague"))
            result += Settings.planet_other_starleague;
        return result;
    }

    public static int GetTotalDefensiveResources(StarSystem system)
    {
        int result = 0;
        if (system.Tags.Contains("planet_industry_agriculture"))
            result += Settings.planet_industry_agriculture;
        if (system.Tags.Contains("planet_industry_aquaculture"))
            result += Settings.planet_industry_aquaculture;
        if (system.Tags.Contains("planet_other_capital"))
            result += Settings.planet_other_capital;
        if (system.Tags.Contains("planet_other_megacity"))
            result += Settings.planet_other_megacity;
        if (system.Tags.Contains("planet_pop_large"))
            result += Settings.planet_pop_large;
        if (system.Tags.Contains("planet_pop_medium"))
            result += Settings.planet_pop_medium;
        if (system.Tags.Contains("planet_pop_none"))
            result += Settings.planet_pop_none;
        if (system.Tags.Contains("planet_pop_small"))
            result += Settings.planet_pop_small;
        if (system.Tags.Contains("planet_other_hub"))
            result += Settings.planet_other_hub;
        return result;
    }

    public class WarFaction
    {
        public float DaysSinceSystemAttacked;
        public float DaysSinceSystemLost;
        public float DefensiveResources;
        public float resources;
        public readonly Faction faction;

        public WarFaction(Faction faction, float resources, float DefensiveResources)
        {
            this.faction = faction;
            this.resources = resources;
            this.DefensiveResources = DefensiveResources;
        }
    }

    public static void RefreshResources(SimGameState sim)
    {
        // no point iterating over a KVP if you aren't using the values
        foreach (var faction in sim.FactionsDict.Select(x => x.Key).Except(Settings.ExcludedFactions))
        {
            //Log(faction.ToString());
            if (Settings.ResourceMap.ContainsKey(faction))
            {
                // initialize resources from the ResourceMap
                if (WarStatus.factionTracker.Find(x => x.faction == faction) == null)
                {
                    var StartingResources = Settings.ResourceMap[faction];
                    var DefensiveStartingResources = Settings.DefensiveResourceMap[faction];
                    WarStatus.factionTracker.Add(new WarFaction(faction, StartingResources, DefensiveStartingResources));
                }
                else
                {
                    WarFaction warFaction = WarStatus.factionTracker.Find(x => x.faction == faction);
                    if (warFaction != null)
                    {
                        warFaction.resources = Settings.ResourceMap[faction];
                        warFaction.DefensiveResources = Settings.DefensiveResourceMap[faction];
                    }
                    else
                        Log($"warFaction {faction} was null");
                }
            }
        }

        if (sim.Starmap == null) return;

        System.Random random = new System.Random();
        foreach (var system in sim.StarSystems)
        {
            var resources = GetTotalResources(system);
            var DefensiveResources = GetTotalDefensiveResources(system);
            var owner = system.Owner;
            Log($"{system.Name + ":",-20} {owner,-20}, total resources: {resources}, total defensive resources: {DefensiveResources}");
            try
            {
                var faction = WarStatus.factionTracker.Where(x => x != null).FirstOrDefault(x => x.faction == owner);
                if (faction != null)
                {
                    faction.resources += resources;
                    faction.DefensiveResources += DefensiveResources;
                }
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        }

        foreach (var faction in WarStatus.factionTracker)
        {
            float tempnum = 0f;
            int i = 0;
            do
            {
                tempnum += random.Next(1, Settings.ResourceRandomizer + 1);
                i++;
            } while (i < faction.resources);
            faction.resources = tempnum * (100f + (float)faction.DaysSinceSystemLost * (float)Settings.ResourceAdjustmentPerCycle) / 100f;

            tempnum = 0f;
            i = 0;
            do
            {
                tempnum += random.Next(1, Settings.ResourceRandomizer + 1);
                i++;
            } while (i < faction.DefensiveResources);
            faction.DefensiveResources = tempnum * (100f * (float)Settings.GlobalDefenseFactor
                        - faction.DaysSinceSystemLost * (float)Settings.ResourceAdjustmentPerCycle) / 100f;

            Logger.Log($"Faction: {faction.faction}, Attack Resources: {faction.resources}, " +
                       $"Defensive Resources: {faction.DefensiveResources}");
        }
    }

    //try
    //{
    //    foreach (KeyValuePair<Faction, FactionDef> pair in Sim.FactionsDict)
    //    {
    //        {
    //            if (Fields.factionResources.Find(x => x.faction == pair.Key) == null)
    //            {
    //                Fields.factionResources.Add(new FactionResources(pair.Key, 0, 0));
    //            }
    //            else
    //            {
    //                FactionResources resources = Fields.factionResources.Find(x => x.faction == pair.Key);
    //                resources.offence = 0;
    //                resources.defence = 0;
    //            }
    //        }
    //    }
}
