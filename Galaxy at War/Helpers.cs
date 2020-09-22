using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BattleTech;
using BattleTech.Framework;
using BattleTech.UI;
using BattleTech.UI.TMProWrapper;
using Harmony;
using HBS.Extensions;
using UnityEngine;
using UnityEngine.UI;
using static GalaxyatWar.Logger;

// ReSharper disable StringLiteralTypo
// ReSharper disable ClassNeverInstantiated.Global

namespace GalaxyatWar
{
    public class Helpers
    {
        internal static void PopulateFactions()
        {
            if (Mod.Settings.ISMCompatibility)
                Mod.Globals.IncludedFactions = new List<string>(Mod.Settings.IncludedFactions_ISM);
            else
                Mod.Globals.IncludedFactions = new List<string>(Mod.Settings.IncludedFactions);

            Mod.Globals.OffensiveFactions = Mod.Globals.IncludedFactions.Except(Mod.Settings.DefensiveFactions).ToList();
        }

        public static void SystemDifficulty()
        {
            var totalSystems = Mod.Globals.WarStatusTracker.systems.Count;
            var difficultyCutoff = totalSystems / 10;
            var i = 0;

            foreach (var systemStatus in Mod.Globals.WarStatusTracker.systemsByResources)
            {
                try
                {
                    //Define the original owner of the system for revolt purposes.
                    if (systemStatus.OriginalOwner == null)
                        systemStatus.OriginalOwner = systemStatus.owner;

                    if (Mod.Settings.ChangeDifficulty && !systemStatus.starSystem.Tags.Contains("planet_start_world"))
                    {
                        Mod.Globals.Sim.Constants.Story.ContractDifficultyMod = 0;
                        Mod.Globals.Sim.CompanyStats.Set<float>("Difficulty", 0);
                        if (i <= difficultyCutoff)
                        {
                            systemStatus.DifficultyRating = 1;
                        }

                        if (i <= difficultyCutoff * 2 && i > difficultyCutoff)
                        {
                            systemStatus.DifficultyRating = 2;
                        }

                        if (i <= difficultyCutoff * 3 && i > 2 * difficultyCutoff)
                        {
                            systemStatus.DifficultyRating = 3;
                        }

                        if (i <= difficultyCutoff * 4 && i > 3 * difficultyCutoff)
                        {
                            systemStatus.DifficultyRating = 4;
                        }

                        if (i <= difficultyCutoff * 5 && i > 4 * difficultyCutoff)
                        {
                            systemStatus.DifficultyRating = 5;
                        }

                        if (i <= difficultyCutoff * 6 && i > 5 * difficultyCutoff)
                        {
                            systemStatus.DifficultyRating = 6;
                        }

                        if (i <= difficultyCutoff * 7 && i > 6 * difficultyCutoff)
                        {
                            systemStatus.DifficultyRating = 7;
                        }

                        if (i <= difficultyCutoff * 8 && i > 7 * difficultyCutoff)
                        {
                            systemStatus.DifficultyRating = 8;
                        }

                        if (i <= difficultyCutoff * 9 && i > 8 * difficultyCutoff)
                        {
                            systemStatus.DifficultyRating = 9;
                        }

                        if (i > 9 * difficultyCutoff)
                        {
                            systemStatus.DifficultyRating = 10;
                        }

                        i++;

                        var amount = systemStatus.DifficultyRating;
                        var difficultyList = new List<int> {amount, amount};
                        systemStatus.starSystem.Def.DifficultyList = difficultyList;
                        systemStatus.starSystem.Def.DefaultDifficulty = amount;
                    }
                    else
                    {
                        systemStatus.DifficultyRating = systemStatus.starSystem.Def.DefaultDifficulty;
                        i++;
                    }

                    if (systemStatus.starSystem.Def.OwnerValue.Name != "NoFaction" && systemStatus.starSystem.Def.SystemShopItems.Count == 0)
                    {
                        var tempList = new List<string>
                        {
                            "itemCollection_minor_Locals"
                        };
                        systemStatus.starSystem.Def.SystemShopItems = tempList;
                        if (Mod.Globals.Sim.CurSystem.Name == systemStatus.starSystem.Def.Description.Name)
                        {
                            var refreshShop = Shop.RefreshType.RefreshIfEmpty;
                            systemStatus.starSystem.SystemShop.Rehydrate(Mod.Globals.Sim, systemStatus.starSystem, systemStatus.starSystem.Def.SystemShopItems, refreshShop,
                                Shop.ShopType.System);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Error(ex);
                }
            }
        }

        public static float GetTotalAttackResources(StarSystem system)
        {
            float result = 0;
            if (system.Tags.Contains("planet_industry_poor"))
                result += Mod.Settings.planet_industry_poor;
            if (system.Tags.Contains("planet_industry_mining"))
                result += Mod.Settings.planet_industry_mining;
            if (system.Tags.Contains("planet_industry_rich"))
                result += Mod.Settings.planet_industry_rich;
            if (system.Tags.Contains("planet_industry_manufacturing"))
                result += Mod.Settings.planet_industry_manufacturing;
            if (system.Tags.Contains("planet_industry_research"))
                result += Mod.Settings.planet_industry_research;
            if (system.Tags.Contains("planet_other_starleague"))
                result += Mod.Settings.planet_other_starleague;

            return result;
        }

        public static float GetTotalDefensiveResources(StarSystem system)
        {
            float result = 0;
            if (system.Tags.Contains("planet_industry_agriculture"))
                result += Mod.Settings.planet_industry_agriculture;
            if (system.Tags.Contains("planet_industry_aquaculture"))
                result += Mod.Settings.planet_industry_aquaculture;
            if (system.Tags.Contains("planet_other_capital"))
                result += Mod.Settings.planet_other_capital;
            if (system.Tags.Contains("planet_other_megacity"))
                result += Mod.Settings.planet_other_megacity;
            if (system.Tags.Contains("planet_pop_large"))
                result += Mod.Settings.planet_pop_large;
            if (system.Tags.Contains("planet_pop_medium"))
                result += Mod.Settings.planet_pop_medium;
            if (system.Tags.Contains("planet_pop_none"))
                result += Mod.Settings.planet_pop_none;
            if (system.Tags.Contains("planet_pop_small"))
                result += Mod.Settings.planet_pop_small;
            if (system.Tags.Contains("planet_other_hub"))
                result += Mod.Settings.planet_other_hub;
            if (system.Tags.Contains("planet_other_comstar"))
                result += Mod.Settings.planet_other_comstar;
            return result;
        }

        internal static void CalculateComstarSupport()
        {
            if (Mod.Globals.WarStatusTracker.ComstarCycle < Mod.Settings.GaW_Police_SupportTime)
            {
                Mod.Globals.WarStatusTracker.ComstarCycle++;
                return;
            }

            Mod.Globals.WarStatusTracker.ComstarCycle = 1;
            var warFactionList = new List<WarFaction>();
            var omit = Mod.Settings.DefensiveFactions.Concat(Mod.Settings.HyadesPirates)
                .Concat(Mod.Settings.NoOffensiveContracts).Concat(new[] {"AuriganPirates"}).ToList();
            foreach (var warFarTemp in Mod.Globals.WarStatusTracker.warFactionTracker)
            {
                warFarTemp.ComstarSupported = false;
                if (omit.Contains(warFarTemp.faction))
                    continue;
                warFactionList.Add(warFarTemp);
            }

            var warFactionHolder = warFactionList.OrderBy(x => x.TotalSystemsChanged).ElementAt(0);
            var warFactionListTrimmed = warFactionList.FindAll(x => x.TotalSystemsChanged == warFactionHolder.TotalSystemsChanged);
            warFactionListTrimmed.Shuffle();
            var warFaction = WarFaction.All[warFactionListTrimmed.First().faction];
            warFaction.ComstarSupported = true;
            Mod.Globals.WarStatusTracker.ComstarAlly = warFaction.faction;
            var factionDef = Mod.Globals.Sim.GetFactionDef(warFaction.faction);
            if (Mod.Settings.GaW_PoliceSupport && !factionDef.Allies.Contains(Mod.Settings.GaW_Police))
            {
                var tempList = factionDef.Allies.ToList();
                tempList.Add(Mod.Settings.GaW_Police);
                factionDef.Allies = tempList.ToArray();
            }

            if (Mod.Settings.GaW_PoliceSupport && factionDef.Enemies.Contains(Mod.Settings.GaW_Police))
            {
                var tempList = factionDef.Enemies.ToList();
                tempList.Remove(Mod.Settings.GaW_Police);
                factionDef.Enemies = tempList.ToArray();
            }
        }

        public static void CalculateAttackAndDefenseTargets(StarSystem starSystem)
        {
            try
            {
                WarFaction.All.TryGetValue(starSystem.OwnerValue.Name, out var warFac);
                if (warFac == null)
                {
                    Error($"Can't find WarFaction for {starSystem.OwnerValue.Name}.");
                    return;
                }

                var isFlashpointSystem = Mod.Globals.WarStatusTracker.FlashpointSystems.Contains(starSystem.Name);
                SystemStatus.All.TryGetValue(starSystem.Name, out var warSystem);
                if (warSystem == null)
                {
                    Error("Can't find " + starSystem.Name);
                    Error(new StackTrace());
                    return;
                }
                var ownerNeighborSystems = warSystem.neighborSystems;
                ownerNeighborSystems.Clear();
                if (Mod.Globals.Sim.Starmap.GetAvailableNeighborSystem(starSystem).Count == 0)
                    return;

                foreach (var neighborSystem in Mod.Globals.Sim.Starmap.GetAvailableNeighborSystem(starSystem).Where(x => !Mod.Settings.ImmuneToWar.Contains(x.OwnerValue.Name)))
                {
                    if (neighborSystem.OwnerValue.Name != starSystem.OwnerValue.Name &&
                        !isFlashpointSystem)
                    {
                        if (!warFac.attackTargets.ContainsKey(neighborSystem.OwnerValue.Name))
                        {
                            var tempList = new List<string> {neighborSystem.Name};
                            //var tempList = new List<string>(warFac.attackTargets[neighborSystem.OwnerValue.Name]);
                            warFac.attackTargets.Add(neighborSystem.OwnerValue.Name, tempList);
                        }
                        else if (warFac.attackTargets.ContainsKey(neighborSystem.OwnerValue.Name)
                                 && !warFac.attackTargets[neighborSystem.OwnerValue.Name].Contains(neighborSystem.Name))
                        {
                            // bug should Locals be here?
                            warFac.attackTargets[neighborSystem.OwnerValue.Name].Add(neighborSystem.Name);
                        }

                        //if (!warFac.defenseTargets.Contains(starSystem.Name))
                        //{
                        //    warFac.defenseTargets.Add(starSystem.Name);
                        //}
                        if (!warFac.adjacentFactions.Contains(starSystem.OwnerValue.Name) && !Mod.Settings.DefensiveFactions.Contains(starSystem.OwnerValue.Name))
                            warFac.adjacentFactions.Add(starSystem.OwnerValue.Name);
                    }

                    RefreshNeighbors(ownerNeighborSystems, neighborSystem);
                }
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        }

        public static void RefreshNeighbors(Dictionary<string, int> starSystem, StarSystem neighborSystem)
        {
            if (Mod.Globals.WarStatusTracker.FlashpointSystems.Contains(neighborSystem.Name))
                return;

            var neighborSystemOwner = neighborSystem.OwnerValue.Name;

            if (starSystem.ContainsKey(neighborSystemOwner))
                starSystem[neighborSystemOwner] += 1;
            else
                starSystem.Add(neighborSystemOwner, 1);
        }

        //public static void CalculateDefenseTargets(StarSystem starSystem)
        //{
        //    foreach (var neighborSystem in Mod.Globals.Sim.Starmap.GetAvailableNeighborSystem(starSystem))
        //    {
        //        var warFac = Mod.Globals.WarStatusTracker.warFactionTracker.Find(x => x.faction == starSystem.Owner);
        //        if (warFac == null)
        //        {
        //            return;
        //        }

        //        if ((neighborSystem.Owner != starSystem.Owner) && !warFac.defenseTargets.Contains(starSystem))
        //        {
        //            warFac.defenseTargets.Add(starSystem);
        //        }
        //    }
        //}


        public static void CalculateDefensiveSystems()
        {
            foreach (var warFaction in Mod.Globals.WarStatusTracker.warFactionTracker)
                warFaction.defenseTargets.Clear();

            foreach (var system in Mod.Globals.WarStatusTracker.systems)
            {
                if (Mod.Globals.WarStatusTracker.FlashpointSystems.Contains(system.name))
                    continue;

                var totalInfluence = system.influenceTracker.Values.Sum();
                if ((totalInfluence - 100) / 100 > Mod.Settings.SystemDefenseCutoff)
                {
                    var warFaction = WarFaction.All[system.owner];
                    warFaction.defenseTargets.Add(system.name);
                }
            }

            //foreach (var warFaction in Mod.Globals.WarStatusTracker.warFactionTracker)
            //{
            //    Log("=============");
            //    Log(warFaction.faction);
            //    foreach (var system in warFaction.defenseTargets)
            //        Log("   " + system);
            //}
        }


        public static void ChangeSystemOwnership(StarSystem system, string faction, bool forceFlip)
        {
            if (faction != system.OwnerValue.Name || forceFlip)
            {
                // todo test
                if (Mod.Settings.ImmuneToWar.Contains(faction))
                {
                    return;
                }

                var oldFaction = system.OwnerValue;
                if ((oldFaction.Name == "NoFaction" || oldFaction.Name == "Locals") && system.Def.Tags.Contains("planet_region_hyadesrim") && !forceFlip)
                {
                    if (Mod.Globals.WarStatusTracker.HyadesRimGeneralPirateSystems.Contains(system.Name))
                        Mod.Globals.WarStatusTracker.HyadesRimGeneralPirateSystems.Remove(system.Name);
                    Mod.Globals.WarStatusTracker.HyadesRimsSystemsTaken++;
                }

                if (system.Def.Tags.Contains(Mod.Settings.FactionTags[oldFaction.Name]))
                    system.Def.Tags.Remove(Mod.Settings.FactionTags[oldFaction.Name]);
                system.Def.Tags.Add(Mod.Settings.FactionTags[faction]);

                if (!Mod.Globals.WarStatusTracker.AbandonedSystems.Contains(system.Name))
                {
                    if (system.Def.SystemShopItems.Count != 0)
                    {
                        var tempList = system.Def.SystemShopItems;
                        tempList.Add(Mod.Settings.FactionShops[system.OwnerValue.Name]);
                        system.Def.SystemShopItems = tempList;
                    }

                    if (system.Def.FactionShopItems != null)
                    {
                        system.Def.FactionShopOwnerValue = Mod.Globals.FactionValues.Find(x => x.Name == faction);
                        system.Def.factionShopOwnerID = faction;
                        var factionShops = system.Def.FactionShopItems;
                        if (factionShops.Contains(Mod.Settings.FactionShopItems[system.Def.OwnerValue.Name]))
                            factionShops.Remove(Mod.Settings.FactionShopItems[system.Def.OwnerValue.Name]);
                        factionShops.Add(Mod.Settings.FactionShopItems[faction]);
                        system.Def.FactionShopItems = factionShops;
                    }
                }

                var systemStatus = SystemStatus.All[system.Name];
                var oldOwner = systemStatus.owner;
                systemStatus.owner = faction;
                system.Def.factionShopOwnerID = faction;
                system.Def.OwnerValue = Mod.Globals.FactionValues.Find(x => x.Name == faction);

                //Change the Kill List for the factions.
                var totalAR = GetTotalAttackResources(system);
                var totalDr = GetTotalDefensiveResources(system);

                var wfWinner = WarFaction.All[faction];
                wfWinner.GainedSystem = true;
                wfWinner.MonthlySystemsChanged += 1;
                wfWinner.TotalSystemsChanged += 1;
                if (Mod.Settings.DefendersUseARforDR && Mod.Settings.DefensiveFactions.Contains(wfWinner.faction))
                {
                    wfWinner.DefensiveResources += totalAR;
                    wfWinner.DefensiveResources += totalDr;
                }
                else
                {
                    wfWinner.AttackResources += totalAR;
                    wfWinner.DefensiveResources += totalDr;
                }

                var wfLoser = WarFaction.All[oldFaction.Name];
                wfLoser.LostSystem = true;
                wfLoser.MonthlySystemsChanged -= 1;
                wfLoser.TotalSystemsChanged -= 1;
                RemoveAndFlagSystems(wfLoser, system);
                if (Mod.Settings.DefendersUseARforDR && Mod.Settings.DefensiveFactions.Contains(wfWinner.faction))
                {
                    wfLoser.DefensiveResources -= totalAR;
                    wfLoser.DefensiveResources -= totalDr;
                }
                else
                {
                    wfLoser.AttackResources -= totalAR;
                    wfLoser.DefensiveResources -= totalDr;
                }

                if (wfLoser.AttackResources < 0)
                    wfLoser.AttackResources = 0;
                if (wfLoser.DefensiveResources < 0)
                    wfLoser.DefensiveResources = 0;

                if (!Mod.Globals.WarStatusTracker.SystemChangedOwners.Contains(system.Name))
                    Mod.Globals.WarStatusTracker.SystemChangedOwners.Add(system.Name);

                if (forceFlip)
                {
                    RecalculateSystemInfluence(systemStatus, faction, oldOwner);
                    systemStatus.PirateActivity = 0;
                }

                foreach (var neighbor in Mod.Globals.Sim.Starmap.GetAvailableNeighborSystem(system))
                {
                    if (!Mod.Globals.WarStatusTracker.SystemChangedOwners.Contains(neighbor.Name))
                        Mod.Globals.WarStatusTracker.SystemChangedOwners.Add(neighbor.Name);
                }
            }
        }

        public static void ChangeDeathListFromAggression(StarSystem system, string faction, string oldFaction)
        {
            var totalAR = GetTotalAttackResources(system);
            var totalDr = GetTotalDefensiveResources(system);
            var systemValue = totalAR + totalDr;
            var killListDelta = Math.Max(10, systemValue);
            try
            {
                if (DeathListTracker.All.TryGetValue(oldFaction, out var deathListTracker))
                {
                    if (deathListTracker.deathList.ContainsKey(faction))
                    {
                        if (deathListTracker.deathList[faction] < 50)
                            deathListTracker.deathList[faction] = 50;

                        deathListTracker.deathList[faction] += killListDelta;
                    }
                    else
                    {
                        Error($"deathListTracker for {oldFaction} is missing faction {faction}, ignoring.");
                    }
                }
                else
                {
                    Error($"DeathListTracker not found for {oldFaction}");
                }
            }
            catch (Exception ex)
            {
                Error(ex);
            }

            try
            {
                //Allies are upset that their friend is being beaten up.
                if (!Mod.Settings.DefensiveFactions.Contains(oldFaction))
                {
                    foreach (var ally in Mod.Globals.Sim.GetFactionDef(oldFaction).Allies)
                    {
                        if (!Mod.Globals.IncludedFactions.Contains(ally) || faction == ally || DeathListTracker.All[ally] == null)
                            continue;
                        var factionAlly = DeathListTracker.All[ally];
                        factionAlly.deathList[faction] += killListDelta / 2;
                    }

                    //Enemies of the target faction are happy with the faction doing the beating. 
                    foreach (var enemy in Mod.Globals.Sim.GetFactionDef(oldFaction).Enemies)
                    {
                        if (!Mod.Globals.IncludedFactions.Contains(enemy) || enemy == faction || DeathListTracker.All[enemy] == null)
                            continue;
                        var factionEnemy = DeathListTracker.All[enemy];
                        factionEnemy.deathList[faction] -= killListDelta / 2;
                    }
                }
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        }

        public static void CalculateHatred()
        {
            foreach (var faction in Mod.Globals.WarStatusTracker.warFactionTracker)
            {
                var attackCount = new Dictionary<string, int>();
                foreach (var target in faction.attackTargets)
                {
                    if (Mod.Settings.DefensiveFactions.Contains(target.Key) || Mod.Settings.ImmuneToWar.Contains(target.Key))
                        continue;
                    attackCount.Add(target.Key, target.Value.Count);
                }

                var i = 0;
                var topHalf = attackCount.Count / 2;
                foreach (var attackTarget in attackCount.OrderByDescending(x => x.Value))
                {
                    var warFaction = WarFaction.All[attackTarget.Key];
                    if (i < topHalf)
                        warFaction.IncreaseAggression[warFaction.faction] = true;
                    else
                        warFaction.IncreaseAggression[warFaction.faction] = false;
                    i++;
                }
            }
        }

        private static void RemoveAndFlagSystems(WarFaction oldOwner, StarSystem system)
        {
            //OldOwner.defenseTargets.Remove(system.Name);
            if (!Mod.Globals.WarStatusTracker.SystemChangedOwners.Contains(system.Name))
                Mod.Globals.WarStatusTracker.SystemChangedOwners.Add(system.Name);
            foreach (var neighborSystem in Mod.Globals.Sim.Starmap.GetAvailableNeighborSystem(system))
            {
                var wfat = WarFaction.All[neighborSystem.OwnerValue.Name].attackTargets;
                if (wfat.Keys.Contains(oldOwner.faction) && wfat[oldOwner.faction].Contains(system.Name))
                    wfat[oldOwner.faction].Remove(system.Name);
            }
        }

        internal static void UpdateInfluenceFromAttacks(bool checkForSystemChange)
        {
            if (checkForSystemChange)
                Mod.Globals.WarStatusTracker.LostSystems.Clear();

            //LogDebug($"Updating influence for {Mod.Globals.WarStatusTracker.SystemStatuses.Count.ToString()} systems");
            foreach (var systemStatus in Mod.Globals.WarStatusTracker.systems)
            {
                var tempDict = new Dictionary<string, float>();
                var totalInfluence = systemStatus.influenceTracker.Values.Sum();
                var highest = 0f;
                var highestFaction = systemStatus.owner;
                foreach (var kvp in systemStatus.influenceTracker)
                {
                    tempDict.Add(kvp.Key, kvp.Value / totalInfluence * 100);
                    if (kvp.Value > highest)
                    {
                        highest = kvp.Value;
                        highestFaction = kvp.Key;
                    }
                }

                systemStatus.influenceTracker = tempDict;
                var diffStatus = systemStatus.influenceTracker[highestFaction] - systemStatus.influenceTracker[systemStatus.owner];
                var starSystem = systemStatus.starSystem;

                if (highestFaction != systemStatus.owner &&
                    !Mod.Globals.WarStatusTracker.FlashpointSystems.Contains(systemStatus.name) &&
                    diffStatus > Mod.Settings.TakeoverThreshold &&
                    !Mod.Globals.WarStatusTracker.HotBox.Contains(systemStatus.name) &&
                    (!Mod.Settings.DefensiveFactions.Contains(highestFaction) || highestFaction == "Locals") &&
                    !Mod.Settings.ImmuneToWar.Contains(starSystem.OwnerValue.Name))
                {
                    if (!systemStatus.Contended)
                    {
                        systemStatus.Contended = true;
                        ChangeDeathListFromAggression(starSystem, highestFaction, starSystem.OwnerValue.Name);
                    }
                    else if (checkForSystemChange)
                    {
                        ChangeSystemOwnership(starSystem, highestFaction, false);
                        systemStatus.Contended = false;
                        Mod.Globals.WarStatusTracker.LostSystems.Add(starSystem.Name);
                    }
                }

                //Local Government can take a system.
                if (systemStatus.owner != "Locals" && systemStatus.OriginalOwner == "Locals" &&
                    (highestFaction == "Locals" && systemStatus.influenceTracker[highestFaction] >= 75))
                {
                    ChangeSystemOwnership(starSystem, "Locals", true);
                    systemStatus.Contended = false;
                    Mod.Globals.WarStatusTracker.LostSystems.Add(starSystem.Name);
                }
            }

            CalculateHatred();
            foreach (var deathListTracker in Mod.Globals.WarStatusTracker.deathListTracker)
            {
                AdjustDeathList(deathListTracker, false);
            }
        }

        public static string MonthlyWarReport()
        {
            var combinedString = "";
            foreach (var faction in Mod.Globals.IncludedFactions)
            {
                var warFaction = WarFaction.All[faction];
                combinedString = combinedString + "<b><u>" + Mod.Settings.FactionNames[faction] + "</b></u>\n";
                var summaryString = "Monthly Change in Systems: " + warFaction.MonthlySystemsChanged + "\n";
                var summaryString2 = "Overall Change in Systems: " + warFaction.TotalSystemsChanged + "\n\n";

                combinedString = combinedString + summaryString + summaryString2;
                warFaction.MonthlySystemsChanged = 0;
            }

            combinedString = combinedString.TrimEnd('\n');
            return combinedString;
        }

        public static void RefreshContractsEmployersAndTargets(SystemStatus systemStatus)
        {
            var starSystem = systemStatus.starSystem;
            //LogDebug("RefreshContracts for " + starSystem.Name);
            if (Mod.Globals.WarStatusTracker.HotBox.Contains(starSystem.Name) ||
                starSystem.Tags.Contains("planet_region_hyadesrim") &&
                (starSystem.OwnerDef.Name == "Locals" || starSystem.OwnerDef.Name == "NoFaction"))
            {
                LogDebug("Skipping HotBox or THR Neutrals");
                return;
            }

            var contractEmployers = starSystem.Def.contractEmployerIDs;
            var contractTargets = starSystem.Def.contractTargetIDs;
            var owner = starSystem.OwnerValue;
            contractEmployers.Clear();
            contractTargets.Clear();

            contractEmployers.Add("Locals");
            contractTargets.Add("Locals");

            if (starSystem.Tags.Contains("planet_other_pirate") || Mod.Globals.WarStatusTracker.AbandonedSystems.Contains(starSystem.Name))
            {
                contractEmployers.Add("AuriganPirates");
                contractTargets.Add("AuriganPirates");
            }

            if (!Equals(owner, Mod.Globals.FactionValues.FirstOrDefault(f => f.Name == "NoFaction")) &&
                !Equals(owner, Mod.Globals.FactionValues.FirstOrDefault(f => f.Name == "Locals")))
            {
                contractEmployers.Add(owner.Name);
                contractTargets.Add(owner.Name);
            }

            if (Mod.Settings.GaW_PoliceSupport && Mod.Globals.WarStatusTracker.ComstarAlly == owner.Name)
            {
                contractEmployers.Add(Mod.Settings.GaW_Police);
                contractTargets.Add(Mod.Settings.GaW_Police);
            }

            var neighborSystems = systemStatus.neighborSystems;
            foreach (var systemNeighbor in neighborSystems.Keys)
            {
                if (Mod.Settings.ImmuneToWar.Contains(systemNeighbor) || Mod.Settings.DefensiveFactions.Contains(systemNeighbor))
                    continue;

                if (!contractEmployers.Contains(systemNeighbor))
                    contractEmployers.Add(systemNeighbor);

                if (!contractTargets.Contains(systemNeighbor))
                    contractTargets.Add(systemNeighbor);
            }

            if (systemStatus.PirateActivity > 0 && !contractEmployers.Contains("AuriganPirates"))
            {
                contractEmployers.Add("AuriganPirates");
                contractTargets.Add("AuriganPirates");
            }

            if (contractEmployers.Count == 1)
            {
                var faction = Mod.Globals.OffensiveFactions.GetRandomElement();
                contractEmployers.Add(faction);
                if (!contractTargets.Contains(faction))
                    contractTargets.Add(faction);
            }

            if (starSystem.Tags.Contains("planet_region_hyadesrim") && Mod.Settings.HyadesRimCompatible)
            {
                foreach (var alliedFaction in owner.FactionDef.Allies)
                {
                    if (!contractEmployers.Contains(alliedFaction) && !Mod.Settings.HyadesTargetsOnly.Contains(alliedFaction))
                        contractEmployers.Add(alliedFaction);
                }

                foreach (var enemyFaction in owner.FactionDef.Enemies)
                {
                    if (!contractTargets.Contains(enemyFaction) && !Mod.Settings.HyadesEmployersOnly.Contains(enemyFaction))
                        contractTargets.Add(enemyFaction);
                }
            }

            var tempContractEmployers = new List<string>(contractEmployers);
            foreach (var tempEmployer in tempContractEmployers)
            {
                if (Mod.Settings.NoOffensiveContracts.Contains(tempEmployer))
                    contractEmployers.Remove(tempEmployer);
            }
        }

        internal static double DeltaInfluence(StarSystem system, double contractDifficulty, string contractTypeID, string defenseFaction, bool piratesInvolved)
        {
            var targetSystem = SystemStatus.All[system.Name];
            if (targetSystem == null)
            {
                LogDebug($"null systemStatus {system.Name} at DeltaInfluence");
            }

            if (targetSystem?.influenceTracker.Count == 0)
            {
                LogDebug("Empty influenceTracker.");
            }

            float maximumInfluence;
            if (piratesInvolved && defenseFaction == "AuriganPirates")
                maximumInfluence = targetSystem.PirateActivity;
            else if (piratesInvolved)
                maximumInfluence = 100 - targetSystem.PirateActivity;
            else
                maximumInfluence = targetSystem.influenceTracker[defenseFaction];

            double influenceChange;
            contractDifficulty = Mathf.Max((int) contractDifficulty, targetSystem.DifficultyRating);

            //If contracts are not properly designed, this provides a failsafe.
            try
            {
                influenceChange = (11 + contractDifficulty - 2 * targetSystem.DifficultyRating) * Mod.Settings.ContractImpact[contractTypeID] / Mod.Settings.InfluenceDivisor;
            }
            catch
            {
                influenceChange = (11 + contractDifficulty - 2 * targetSystem.DifficultyRating) / Mod.Settings.InfluenceDivisor;
            }

            //Log("System Delta Influence");
            //Log(TargetSystem.name);
            //Log(Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease.ToString());
            //Log(contractDifficulty.ToString());
            //Log(TargetSystem.DifficultyRating.ToString());
            //Log(Mod.Settings.InfluenceDivisor.ToString());
            //Log(InfluenceChange.ToString());


            if (piratesInvolved)
                influenceChange *= 2;
            influenceChange = Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease * Math.Max(influenceChange, 0.5);
            if (influenceChange > maximumInfluence && !piratesInvolved)
            {
                Mod.Globals.AttackerInfluenceHolder = influenceChange;
                Mod.Globals.AttackerInfluenceHolder = Math.Round(Mod.Globals.AttackerInfluenceHolder, 1);
                Mod.Globals.InfluenceMaxed = true;
            }
            else
                Mod.Globals.InfluenceMaxed = false;

            influenceChange = Math.Min(influenceChange, maximumInfluence);
            influenceChange = Math.Round(influenceChange, 1);
            //Log(InfluenceChange.ToString());
            //Log("--------------------------");
            return influenceChange;
        }

        internal static bool WillSystemFlip(StarSystem system, string winner, string loser, double deltaInfluence, bool preBattle)
        {
            var warSystem = SystemStatus.All[system.Name];
            if (warSystem == null)
            {
                LogDebug($"null systemStatus {system.Name} at WillSystemFlip");
            }

            var tempIt = new Dictionary<string, float>(warSystem.influenceTracker);

            if (preBattle && !Mod.Globals.InfluenceMaxed)
            {
                tempIt[winner] += (float) deltaInfluence;
                tempIt[loser] -= (float) deltaInfluence;
            }
            else if (preBattle && Mod.Globals.InfluenceMaxed)
            {
                tempIt[winner] += (float) Math.Min(Mod.Globals.AttackerInfluenceHolder, 100 - tempIt[winner]);
                tempIt[loser] -= (float) deltaInfluence;
            }

            var highKey = tempIt.OrderByDescending(x => x.Value).Select(x => x.Key).First();
            var highValue = tempIt.OrderByDescending(x => x.Value).Select(x => x.Value).First();
            tempIt.Remove(highKey);
            var secondValue = tempIt.OrderByDescending(x => x.Value).Select(x => x.Value).First();

            if (highKey != warSystem.owner &&
                highKey == winner &&
                highValue - secondValue > Mod.Settings.TakeoverThreshold &&
                !Mod.Globals.WarStatusTracker.FlashpointSystems.Contains(system.Name) &&
                !Mod.Settings.DefensiveFactions.Contains(winner) &&
                !Mod.Settings.ImmuneToWar.Contains(loser))
                return true;
            return false;
        }

        internal static int CalculateFlipMissions(string attacker, StarSystem system)
        {
            var warSystem = SystemStatus.All[system.Name];
            var tempIt = new Dictionary<string, float>(warSystem.influenceTracker);
            var missionCounter = 0;
            var influenceDifference = 0.0f;
            double contractDifficulty = warSystem.DifficultyRating;
            var deploymentIfHolder = Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease;
            Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease = 1;
            while (influenceDifference <= Mod.Settings.TakeoverThreshold)
            {
                var defenseFaction = "";
                foreach (var faction in tempIt.OrderByDescending(x => x.Value))
                {
                    if (faction.Key != attacker)
                    {
                        defenseFaction = faction.Key;
                        break;
                    }
                }

                var influenceChange = DeltaInfluence(system, contractDifficulty, "CaptureBase", defenseFaction, false);
                tempIt[attacker] += (float) influenceChange;
                tempIt[defenseFaction] -= (float) influenceChange;
                influenceDifference = tempIt[attacker] - tempIt[defenseFaction];
                Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease *= Mod.Settings.DeploymentEscalationFactor;
                missionCounter++;
            }

            Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease = deploymentIfHolder;
            return missionCounter;
        }

        public static void RecalculateSystemInfluence(SystemStatus systemStatus, string newOwner, string oldOwner)
        {
            systemStatus.influenceTracker.Clear();
            systemStatus.influenceTracker.Add(newOwner, Mod.Settings.DominantInfluence);
            systemStatus.influenceTracker.Add(oldOwner, Mod.Settings.MinorInfluencePool);
            foreach (var faction in Mod.Globals.IncludedFactions)
            {
                if (!systemStatus.influenceTracker.Keys.Contains(faction))
                    systemStatus.influenceTracker.Add(faction, 0);
            }
        }

        public static void AdjustDeathList(DeathListTracker deathListTracker, bool reloadFromSave)
        {
            var trackerDeathList = deathListTracker.deathList;
            var trackerFaction = deathListTracker.faction;
            var trackerFactionDef = Mod.Globals.Sim.GetFactionDef(trackerFaction);
            var trackerFactionEnemies = new List<string>(trackerFactionDef.Enemies);
            var trackerFactionAllies = new List<string>(trackerFactionDef.Allies);
            if (Mod.Globals.WarStatusTracker.InactiveTHRFactions.Contains(trackerFaction) || Mod.Globals.WarStatusTracker.NeverControl.Contains(trackerFaction))
                return;

            //Check to see if it is an ally or enemy of itself and remove it if so.
            if (trackerDeathList.ContainsKey(trackerFaction))
            {
                trackerDeathList.Remove(trackerFaction);
                if (trackerFactionAllies.Contains(trackerFaction))
                {
                    trackerFactionAllies.Remove(trackerFaction);
                    trackerFactionDef.Allies = trackerFactionAllies.ToArray();
                }

                if (trackerFactionEnemies.Contains(trackerFaction))
                {
                    trackerFactionEnemies.Remove(trackerFaction);
                    trackerFactionDef.Enemies = trackerFactionEnemies.ToArray();
                }
            }

            var deathListOffensiveFactions = new List<string>(trackerDeathList.Keys.Except(Mod.Settings.DefensiveFactions));
            var warFaction = deathListTracker.WarFaction;

            var hasEnemy = false;
            //Defensive Only factions are always neutral
            Mod.Settings.DefensiveFactions.Do(x => trackerDeathList[x] = 50);
            if (Mod.Settings.GaW_PoliceSupport && warFaction.ComstarSupported)
                trackerDeathList[Mod.Settings.GaW_Police] = 99;

            foreach (var offensiveFaction in deathListOffensiveFactions)
            {
                if (Mod.Globals.WarStatusTracker.InactiveTHRFactions.Contains(offensiveFaction) || Mod.Globals.WarStatusTracker.NeverControl.Contains(offensiveFaction))
                    continue;

                //Check to see if factions are always allied with each other.
                if (Mod.Settings.FactionsAlwaysAllies.Keys.Contains(warFaction.faction) && Mod.Settings.FactionsAlwaysAllies[warFaction.faction].Contains(offensiveFaction))
                {
                    trackerDeathList[offensiveFaction] = 99;
                    continue;
                }

                if (!reloadFromSave)
                {
                    //Factions adjust hatred based upon how much they are being attacked. But there is diminishing returns further from 50.
                    var direction = -1;
                    if (warFaction.IncreaseAggression.Keys.Contains(offensiveFaction) && warFaction.IncreaseAggression[offensiveFaction])
                        direction = 1;
                    {
                        if (trackerDeathList[offensiveFaction] > 50)
                            trackerDeathList[offensiveFaction] += direction * (1 - (trackerDeathList[offensiveFaction] - 50) / 50);
                        else if (trackerDeathList[offensiveFaction] <= 50)
                            trackerDeathList[offensiveFaction] += direction * (1 - (50 - trackerDeathList[offensiveFaction]) / 50);
                    }

                    //Ceiling and floor for faction enmity. 
                    if (trackerDeathList[offensiveFaction] > 99)
                        trackerDeathList[offensiveFaction] = 99;

                    if (trackerDeathList[offensiveFaction] < 1)
                        trackerDeathList[offensiveFaction] = 1;
                }

                // If the faction target is Pirates, the faction always hates them. Alternatively, if the faction we are checking
                // the DeathList for is Pirates themselves, we must set everybody else to be an enemy.
                if (offensiveFaction == "AuriganPirates")
                    trackerDeathList[offensiveFaction] = 80;

                if (trackerFaction == "AuriganPirates")
                    trackerDeathList[offensiveFaction] = 80;

                if (trackerDeathList[offensiveFaction] > 75)
                {
                    if (offensiveFaction != "AuriganPirates")
                        hasEnemy = true;
                    if (!trackerFactionEnemies.Contains(offensiveFaction))
                    {
                        trackerFactionEnemies.Add(offensiveFaction);
                        if (trackerFactionEnemies.Contains(trackerFaction))
                            trackerFactionEnemies.Remove(trackerFaction);
                        if (trackerFactionEnemies.Contains("AuriganDirectorate"))
                            trackerFactionEnemies.Remove("AuriganDirectorate");
                        trackerFactionDef.Enemies = trackerFactionEnemies.ToArray();
                    }

                    if (trackerFactionAllies.Contains(offensiveFaction))
                    {
                        trackerFactionAllies.Remove(offensiveFaction);
                        if (trackerFactionAllies.Contains(trackerFaction))
                            trackerFactionAllies.Remove(trackerFaction);
                        if (trackerFactionAllies.Contains("AuriganDirectorate"))
                            trackerFactionAllies.Remove("AuriganDirectorate");
                        trackerFactionDef.Allies = trackerFactionAllies.ToArray();
                    }
                }
                else if (trackerDeathList[offensiveFaction] <= 75 && trackerDeathList[offensiveFaction] > 25)
                {
                    if (trackerFactionEnemies.Contains(offensiveFaction))
                    {
                        trackerFactionEnemies.Remove(offensiveFaction);
                        if (trackerFactionEnemies.Contains(trackerFaction))
                            trackerFactionEnemies.Remove(trackerFaction);
                        if (trackerFactionEnemies.Contains("AuriganDirectorate"))
                            trackerFactionEnemies.Remove("AuriganDirectorate");
                        trackerFactionDef.Enemies = trackerFactionEnemies.ToArray();
                    }


                    if (trackerFactionAllies.Contains(offensiveFaction))
                    {
                        trackerFactionAllies.Remove(offensiveFaction);
                        if (trackerFactionAllies.Contains(trackerFaction))
                            trackerFactionAllies.Remove(trackerFaction);
                        if (trackerFactionAllies.Contains("AuriganDirectorate"))
                            trackerFactionAllies.Remove("AuriganDirectorate");
                        trackerFactionDef.Allies = trackerFactionAllies.ToArray();
                    }
                }
                else if (trackerDeathList[offensiveFaction] <= 25)
                {
                    if (!trackerFactionAllies.Contains(offensiveFaction))
                    {
                        trackerFactionAllies.Add(offensiveFaction);
                        if (trackerFactionAllies.Contains(trackerFaction))
                            trackerFactionAllies.Remove(trackerFaction);
                        if (trackerFactionAllies.Contains("AuriganDirectorate"))
                            trackerFactionAllies.Remove("AuriganDirectorate");
                        trackerFactionDef.Allies = trackerFactionAllies.ToArray();
                    }

                    if (trackerFactionEnemies.Contains(offensiveFaction))
                    {
                        trackerFactionEnemies.Remove(offensiveFaction);
                        if (trackerFactionEnemies.Contains(trackerFaction))
                            trackerFactionEnemies.Remove(trackerFaction);
                        if (trackerFactionEnemies.Contains("AuriganDirectorate"))
                            trackerFactionEnemies.Remove("AuriganDirectorate");
                        trackerFactionDef.Enemies = trackerFactionEnemies.ToArray();
                    }
                }
            }

            if (!hasEnemy)
            {
                var rand = Mod.Globals.Rng.Next(0, Mod.Globals.IncludedFactions.Count);
                var newEnemy = Mod.Globals.IncludedFactions[rand];

                while (newEnemy == trackerFaction || Mod.Settings.ImmuneToWar.Contains(newEnemy) || Mod.Settings.DefensiveFactions.Contains(newEnemy))
                {
                    rand = Mod.Globals.Rng.Next(0, Mod.Globals.IncludedFactions.Count);
                    newEnemy = Mod.Globals.IncludedFactions[rand];
                }

                if (warFaction.adjacentFactions.Count != 0)
                {
                    rand = Mod.Globals.Rng.Next(0, warFaction.adjacentFactions.Count);
                    newEnemy = warFaction.adjacentFactions[rand];
                }

                if (trackerFactionAllies.Contains(newEnemy))
                {
                    trackerFactionAllies.Remove(newEnemy);
                    if (trackerFactionAllies.Contains(trackerFaction))
                        trackerFactionAllies.Remove(trackerFaction);
                    if (trackerFactionAllies.Contains("AuriganDirectorate"))
                        trackerFactionAllies.Remove("AuriganDirectorate");
                    trackerFactionDef.Allies = trackerFactionAllies.ToArray();
                }

                if (!trackerFactionEnemies.Contains(newEnemy))
                {
                    trackerFactionEnemies.Add(newEnemy);
                    if (trackerFactionEnemies.Contains(trackerFaction))
                        trackerFactionEnemies.Remove(trackerFaction);
                    if (trackerFactionEnemies.Contains("AuriganDirectorate"))
                        trackerFactionEnemies.Remove("AuriganDirectorate");
                    trackerFactionDef.Enemies = trackerFactionEnemies.ToArray();
                }

                trackerDeathList[newEnemy] = 80;
            }
        }

        public static void GaW_Notification()
        {
            //82 characters per line. 
            //Mod.Globals.SimGameResultAction Mod.Globals.SimGameResultAction = new Mod.Globals.SimGameResultAction();
            //Mod.Globals.SimGameResultAction.Type = Mod.Globals.SimGameResultAction.ActionType.System_ShowSummaryOverlay;
            //Mod.Globals.SimGameResultAction.value = Strings.T("Galaxy at War");
            //Mod.Globals.SimGameResultAction.additionalValues = new string[1];
            //Mod.Globals.SimGameResultAction.additionalValues[0] = Strings.T("In Galaxy at War, the Great Houses of the Inner Sphere will not Mod.Globals.Simply wait for a wedding invitation" +
            //                                                    " to show their disdain for each other. To that end, war will break out as petty bickering turns into all out conflict. Your reputation with the factions" +
            //                                                    " is key - the more they like you, the more they'll bring you to the front lines and the greater the rewards. Perhaps an enterprising mercenary could make their" +
            //                                                    " fortune changing the tides of battle and helping a faction dominate the Inner Sphere.\n\n <b>New features in Galaxy at War:</b>" +
            //                                                    "\n• Each planet generates Attack Resources and Defensive Resources that they will be constantly " +
            //                                                    "spending to spread their influence and protect their own systems." +
            //                                                    "\n• Planetary Resources and Faction Influence can be seen on the Star Map by hovering over any system." +
            //                                                    "\n• Successfully completing missions will swing the influence towards the Faction granting the contract." +
            //                                                    "\n• Target Acquisition Missions & Attack and Defend Missions will give a permanent bonus to the winning faction's Attack Resources and a permanent deduction to the losing faction's Defensive Resources." +
            //                                                    "\n• If you accept a travel contract the Faction will blockade the system for 30 days. A bonus will be granted for every mission you complete within that system during that time." +
            //                                                    "\n• Pirates are active and will reduce Resources in a system. High Pirate activity will be highlighted in red." +
            //                                                    "\n• Sumire will flag the systems in purple on the Star Map that are the most valuable local targets." +
            //                                                    "\n• Sumire will also highlight systems in yellow that have changed ownership during the previous month." +
            //                                                    "\n• Hitting Control-R will bring up a summary of the Faction's relationships and their overall war status." +
            //                                                    "\n\n****Press Enter to Continue****");


            //Mod.Globals.SimGameState.ApplyEventAction(Mod.Globals.SimGameResultAction, null);
            //UnityGameInstance.BattleTechGame.Mod.Globals.Simulation.StopPlayMode();
        }

        public static void GenerateMonthlyContracts()
        {
            var contracts = new List<Contract>();
            var system = Mod.Globals.Sim.CurSystem;
            var systemStatus = SystemStatus.All[system.Name];
            var influenceTracker = systemStatus.influenceTracker;
            var owner = influenceTracker.First().Key;
            var second = influenceTracker.Skip(1).First().Key;
            LogDebug(0);
            var contract = Contracts.GenerateContract(system, 2, 2, owner);
            contracts.Add(contract);
            LogDebug(1);
            contract = Contracts.GenerateContract(system, 4, 4, owner);
            contracts.Add(contract);
            LogDebug(2);
            contract = Contracts.GenerateContract(system, 2, 2, second);
            contracts.Add(contract);
            LogDebug(3);
            contract = Contracts.GenerateContract(system, 4, 4, second);
            contracts.Add(contract);
            LogDebug(4);
            contract = Contracts.GenerateContract(system, 6, 6, Mod.Globals.IncludedFactions.Where(x => x != "NoFaction").GetRandomElement());
            contracts.Add(contract);
            LogDebug(5);
            Mod.Globals.Sim.CurSystem.activeSystemContracts = contracts;
        }

        internal static void BackFillContracts()
        {
            const int ownerContracts = 5;
            const int secondContracts = 3;
            const int otherContracts = 2;

            //var loops = 10 - ownerContracts + secondContracts;
            for (var i = 0; i < 10; i++)
            {
                int variedDifficulty;
                var isTravel = Mod.Globals.Rng.Next(0, 2) == 0;
                var isPriority = Mod.Globals.Rng.Next(0, 11) == 0;
                var system = isTravel ? Mod.Globals.Sim.StarSystems.Where(x => x.JumpDistance < 10).GetRandomElement() : Mod.Globals.Sim.CurSystem;
                var systemStatus = SystemStatus.All[system.Name];
                var owner = systemStatus.influenceTracker.OrderByDescending(x => x.Value).Select(x => x.Key).First();
                var second = systemStatus.influenceTracker.OrderByDescending(x =>
                    x.Value).Select(x => x.Key).Skip(1).Take(1).First();
                var currentOwnerContracts = Mod.Globals.Sim.GetAllCurrentlySelectableContracts().Count(x => x.Override.employerTeam.faction == owner);
                var currentSecondContracts = Mod.Globals.Sim.GetAllCurrentlySelectableContracts().Count(x => x.Override.employerTeam.faction == second);
                var currentOtherContracts = Mod.Globals.Sim.GetAllCurrentlySelectableContracts().Count - currentOwnerContracts - currentSecondContracts;
                if (currentOwnerContracts < ownerContracts)
                {
                    variedDifficulty = Variance(system);
                    AddContract(system, variedDifficulty, owner, isTravel, isPriority);
                }
                else if (currentSecondContracts < secondContracts)
                {
                    variedDifficulty = Variance(system);
                    AddContract(system, variedDifficulty, second, isTravel, isPriority);
                }
                else if (currentOtherContracts < otherContracts)
                {
                    variedDifficulty = Variance(system);
                    AddContract(system, variedDifficulty, systemStatus.influenceTracker.Select(x => x.Key).GetRandomElement(), isTravel, isPriority);
                }
            }
        }

        private static void AddContract(StarSystem system, int variedDifficulty, string employer, bool isTravel, bool isPriority)
        {
            var contract = Contracts.GenerateContract(system, variedDifficulty, variedDifficulty, employer, null, isTravel);
            if (isPriority)
            {
                contract.Override.contractDisplayStyle = ContractDisplayStyle.BaseCampaignStory;
            }

            system.activeSystemContracts.Add(contract);
        }

        internal static int Variance(StarSystem starSystem)
        {
            const int variance = 2;
            return starSystem.Def.GetDifficulty(SimGameState.SimGameType.CAREER) + Mod.Globals.Rng.Next(-variance, variance + 1);
        }
    }
}
