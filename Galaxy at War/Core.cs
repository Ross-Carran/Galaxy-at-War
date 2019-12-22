using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BattleTech;
using Harmony;
using Newtonsoft.Json;
using static Logger;
using Random = System.Random;
using BattleTech.UI;
using HBS;
using Localize;
using BattleTech.Framework;
using BattleTech.UI.TMProWrapper;
using UnityEngine.UI;
using BattleTech.Data;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable InconsistentNaming

public static class Core
{
    #region Init

    public static void Init(string modDir, string settings)
    {
        var harmony = HarmonyInstance.Create("com.Same.BattleTech.GalaxyAtWar");
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
        // PrintObjectFields(Settings, "Settings");
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
    public static readonly Random Random = new Random();
    public static string teamfaction;
    public static string enemyfaction;
    public static int difficulty;
    public static MissionResult missionResult;
    public static bool isGoodFaithEffort;
    public static List<string> FactionEnemyHolder = new List<string>();
    public static Dictionary<string, List<StarSystem>> attackTargets = new Dictionary<string, List<StarSystem>>();
    public static List<StarSystem> defenseTargets = new List<StarSystem>();
    public static string contractType;
    public static bool NeedsProcessing = false;
    public static List<FactionValue> FactionValues = new List<FactionValue>();
    public static bool BorkedSave;
    public static bool IsFlashpointContract;

    [HarmonyPatch(typeof(SimGameState), "OnDayPassed")]
    public static class SimGameState_OnDayPassed_Patch
    {
        static void Prefix(SimGameState __instance, int timeLapse)
        {
            var sim = UnityGameInstance.BattleTechGame.Simulation;
            if (sim.IsCampaign && !sim.CompanyTags.Contains("story_complete"))
                return;

            if (WarStatus == null || BorkedSave)
            {
                WarStatus = new WarStatus();
                SystemDifficulty();
                WarTick(true, true);
                BorkedSave = false;
            }

            
            WarStatus.CurSystem = sim.CurSystem.Name;
            try
            {
                StarmapMod.eventPanel.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(StarmapMod.eventPanel);

            }
            catch
            {
                UnityEngine.Object.Destroy(StarmapMod.eventPanel);
            }
            if (WarStatus.HotBox.Contains(sim.CurSystem.Name) && !WarStatus.HotBoxTravelling)
            {
                WarStatus.EscalationDays--;

                if (WarStatus.EscalationDays == 0)
                {
                    Galaxy_at_War.HotSpots.CompleteEscalation();
                }
                if (WarStatus.EscalationOrder != null)
                {
                    WarStatus.EscalationOrder.PayCost(1);
                    TaskManagementElement taskManagementElement4 = null;
                    TaskTimelineWidget timelineWidget = (TaskTimelineWidget)AccessTools.Field(typeof(SGRoomManager), "timelineWidget").GetValue(__instance.RoomManager);
                    Dictionary<WorkOrderEntry, TaskManagementElement> ActiveItems = 
                        (Dictionary<WorkOrderEntry, TaskManagementElement>)AccessTools.Field(typeof(TaskTimelineWidget), "ActiveItems").GetValue(timelineWidget);
                    if (ActiveItems.TryGetValue(WarStatus.EscalationOrder, out taskManagementElement4))
                    {
                        taskManagementElement4.UpdateItem(0);
                    }
                }
            }
            if (!Core.WarStatus.StartGameInitialized)
            {
                NeedsProcessing = true;
                var cmdCenter = UnityGameInstance.BattleTechGame.Simulation.RoomManager.CmdCenterRoom;
                sim.CurSystem.GenerateInitialContracts(() => Traverse.Create(cmdCenter).Method("OnContractsFetched"));
                Core.WarStatus.StartGameInitialized = true;
                NeedsProcessing = false;
            }
        }

        public static void Postfix(SimGameState  __instance)
        {
            var sim = UnityGameInstance.BattleTechGame.Simulation;
            if (Core.WarStatus == null || (sim.IsCampaign && !sim.CompanyTags.Contains("story_complete")))
                return;

            if (!WarStatus.GaW_Event_PopUp)
            {
                GaW_Notification();
                WarStatus.GaW_Event_PopUp = true;
            }

            //int i = 0;
            //do
            //{
            //    WarTick(true, true);
            //    i++;
            //} while (i < 100);
            //__instance.StopPlayMode();
            //return;
            if (__instance.DayRemainingInQuarter % Settings.WarFrequency == 0)
            {
                //LogDebug(">>> PROC");
                if (__instance.DayRemainingInQuarter != 30)
                {
                    WarTick(false, false);
                }
                else
                {
                    bool HasFlashpoint = false;
                    WarTick(true, true);
                    foreach (var contract in sim.CurSystem.SystemContracts)
                    {
                        if (contract.IsFlashpointContract)
                            HasFlashpoint = true;
                    }
                    if (!WarStatus.HotBoxTravelling && !WarStatus.HotBox.Contains(sim.CurSystem.Name) && !HasFlashpoint)
                    {
                        NeedsProcessing = true;
                        var cmdCenter = UnityGameInstance.BattleTechGame.Simulation.RoomManager.CmdCenterRoom;
                        sim.CurSystem.GenerateInitialContracts(() => Traverse.Create(cmdCenter).Method("OnContractsFetched"));
                        NeedsProcessing = false;
                    }
                }
                SaveHandling.SerializeWar();
                LogDebug(">>> DONE PROC");
            }
        }
    }


    internal static void WarTick(bool UseFullSet, bool CheckForSystemChange)
    {
        var sim = UnityGameInstance.BattleTechGame.Simulation;
        WarStatus.PrioritySystems.Clear();

        int SystemSubsetSize = WarStatus.systems.Count;
        if (Settings.UseSubsetOfSystems && !UseFullSet)
            SystemSubsetSize = (int)(SystemSubsetSize * Settings.SubSetFraction);
        var SystemSubset = WarStatus.systems.OrderBy(x => Guid.NewGuid()).Take(SystemSubsetSize);

        //Distribute Pirate Influence throughout the StarSystems
        Galaxy_at_War.PiratesAndLocals.CorrectResources();
        Galaxy_at_War.PiratesAndLocals.PiratesStealResources();
        Galaxy_at_War.PiratesAndLocals.CurrentPAResources = Core.WarStatus.PirateResources;
        Galaxy_at_War.PiratesAndLocals.DistributePirateResources();
        Galaxy_at_War.PiratesAndLocals.DefendAgainstPirates();

        foreach (var systemStatus in SystemSubset)
        {
            //if (systemStatus.PirateActivity >= 75 && systemStatus.owner != Faction.Locals)
            //{
            //    ChangeSystemOwnership(sim, systemStatus.starSystem, Faction.Locals, true);
            //    foreach (var system in WarStatus.SystemChangedOwners)
            //    {
            //        var ChangesystemStatus = WarStatus.systems.Find(x => x.name == system);
            //        ChangesystemStatus.CurrentlyAttackedBy.Clear();
            //        CalculateAttackAndDefenseTargets(ChangesystemStatus.starSystem);
            //        RefreshContracts(ChangesystemStatus.starSystem);
            //    }
            //    WarStatus.SystemChangedOwners.Clear();
            //}

            if (!systemStatus.owner.Equals("Locals") && systemStatus.influenceTracker.Keys.Contains("Locals"))
            {
                systemStatus.influenceTracker["Locals"] *= 1.1f;
                var warFaction = (WarStatus.warFactionTracker.Find(x => x.faction == systemStatus.owner));
                if (!warFaction.defenseTargets.Contains(systemStatus.name))
                    warFaction.defenseTargets.Add(systemStatus.name);
            }

            systemStatus.PriorityAttack = false;
            systemStatus.PriorityDefense = false;
            if (WarStatus.InitializeAtStart)
            {
                systemStatus.CurrentlyAttackedBy.Clear();
                CalculateAttackAndDefenseTargets(systemStatus.starSystem);
                RefreshContracts(systemStatus.starSystem);
            }
            if (systemStatus.Contended || Core.WarStatus.HotBox.Contains(systemStatus.name))
                continue;

            //Add resources from neighboring systems.
            if (systemStatus.neighborSystems.Count != 0)
            {
                foreach (var neighbor in systemStatus.neighborSystems.Keys)
                {
                    if (!Settings.ImmuneToWar.Contains(neighbor) && !Settings.DefensiveFactions.Contains(neighbor))
                    {
                        var PushFactor = Settings.APRPush * Random.Next(1, Settings.APRPushRandomizer + 1);
                        systemStatus.influenceTracker[neighbor] += systemStatus.neighborSystems[neighbor] * PushFactor;
                    }
                }
            }
            if (systemStatus.PirateActivity >= Settings.PirateSystemFlagValue)
            {
                if (!WarStatus.PirateHighlight.Contains(systemStatus.name))
                    WarStatus.PirateHighlight.Add(systemStatus.name);
            }
            else
            {
                if (WarStatus.PirateHighlight.Contains(systemStatus.name))
                    WarStatus.PirateHighlight.Remove(systemStatus.name);
            }
        }

        WarStatus.InitializeAtStart = false;
        //Attack!
        //LogDebug("Attacking Fool");
        foreach (var warFaction in WarStatus.warFactionTracker)
        {
            DivideAttackResources(warFaction, UseFullSet);
            AllocateAttackResources(warFaction);
        }
        foreach (var warFaction in WarStatus.warFactionTracker)
        {
            AllocateDefensiveResources(warFaction, UseFullSet);
        }

        UpdateInfluenceFromAttacks(sim, CheckForSystemChange);

        //Increase War Escalation or decay defenses.
        foreach (var warfaction in WarStatus.warFactionTracker)
        {
            if (!warfaction.GainedSystem)
                warfaction.DaysSinceSystemAttacked += 1;
            else
            {
                warfaction.DaysSinceSystemAttacked = 0;
                warfaction.GainedSystem = false;
            }

            if (!warfaction.LostSystem)
                warfaction.DaysSinceSystemLost += 1;
            else
            {
                warfaction.DaysSinceSystemLost = 0;
                warfaction.LostSystem = false;
            }
        }

        foreach (var system in WarStatus.SystemChangedOwners)
        {
            var systemStatus = WarStatus.systems.Find(x => x.name == system);
            systemStatus.CurrentlyAttackedBy.Clear();
            CalculateAttackAndDefenseTargets(systemStatus.starSystem);
            RefreshContracts(systemStatus.starSystem);
        }

        WarStatus.SystemChangedOwners.Clear();


//        Log("===================================================");
//        Log("TESTING ZONE");
//        Log("===================================================");
//        ////TESTING ZONE
//        foreach (WarFaction WF in WarStatus.warFactionTracker)
//        {
//            Log("----------------------------------------------");
//            Log(WF.faction.ToString());
//            try
//            {
//                var DLT = WarStatus.deathListTracker.Find(x => x.faction == WF.faction);
////                Log("\tAttacked By :");
////                foreach (Faction fac in DLT.AttackedBy)
////                    Log("\t\t" + fac.ToString());
////                Log("\tOwner :" + DLT.);
////                Log("\tAttack Resources :" + WF.AttackResources.ToString());
////                Log("\tDefensive Resources :" + WF.DefensiveResources.ToString());
//                Log("\tDeath List:");
//                foreach (var faction in DLT.deathList.Keys)
//                {
//                    Log("\t\t" + faction.ToString() + ": " + DLT.deathList[faction]);
//                }
//            }
//            catch (Exception e)
//            {
//                Error(e);
//            }
        //       }
    }

    public static void CalculateAttackAndDefenseTargets(StarSystem starSystem)
    {
        SimGameState sim = UnityGameInstance.BattleTechGame.Simulation;
        var warSystem = WarStatus.systems.Find(x => x.name == starSystem.Name);
        var OwnerNeighborSystems = warSystem.neighborSystems;
        OwnerNeighborSystems.Clear();
        if (starSystem == null || sim.Starmap.GetAvailableNeighborSystem(starSystem).Count == 0)
            return;

        foreach (var neighborSystem in sim.Starmap.GetAvailableNeighborSystem(starSystem))
        {
            if (!neighborSystem.OwnerValue.Name.Equals(starSystem.OwnerValue.Name) && !Settings.ImmuneToWar.Contains(neighborSystem.OwnerValue.Name))
            {
                var warFac = WarStatus.warFactionTracker.Find(x => x.faction == starSystem.OwnerValue.Name);
                if (warFac == null)
                    return;

                if (!warFac.attackTargets.ContainsKey(neighborSystem.OwnerValue.Name))
                {
                    var tempList = new List<string> { neighborSystem.Name };
                    warFac.attackTargets.Add(neighborSystem.OwnerValue.Name, tempList);
                }
                else if (warFac.attackTargets.ContainsKey(neighborSystem.OwnerValue.Name) 
                         && !warFac.attackTargets[neighborSystem.OwnerValue.Name].Contains(neighborSystem.Name))
                {
                    warFac.attackTargets[neighborSystem.OwnerValue.Name].Add(neighborSystem.Name);
                }
                if (!warFac.defenseTargets.Contains(starSystem.Name))
                {
                    warFac.defenseTargets.Add(starSystem.Name);
                }
            }
            RefreshNeighbors(OwnerNeighborSystems, neighborSystem);
        }
    }

    public static void RefreshNeighbors(Dictionary<string, int> starSystem, StarSystem neighborSystem)
    {
        var neighborSystemOwner = neighborSystem.OwnerValue.Name;

        if (starSystem.ContainsKey(neighborSystemOwner))
            starSystem[neighborSystemOwner] += 1;
        else
            starSystem.Add(neighborSystemOwner, 1);
    }

    //public static void CalculateDefenseTargets(StarSystem starSystem)
    //{
    //    SimGameState sim = UnityGameInstance.BattleTechGame.Simulation;

    //    foreach (var neighborSystem in sim.Starmap.GetAvailableNeighborSystem(starSystem))
    //    {
    //        var warFac = WarStatus.warFactionTracker.Find(x => x.faction == starSystem.Owner);
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

    public static void DivideAttackResources(WarFaction warFaction, bool UseFullSet)
    {
        //Log("Attacking");
        var deathList = WarStatus.deathListTracker.Find(x => x.faction == warFaction.faction);
        var warFAR = warFaction.warFactionAttackResources;
        warFAR.Clear();
        var tempTargets = new Dictionary<string, float>();
        foreach (string fact in warFaction.attackTargets.Keys)
        {
            tempTargets.Add(fact, deathList.deathList[fact]);
        }

        var total = tempTargets.Values.Sum();
        float attackResources = warFaction.AttackResources;
        
        attackResources = attackResources * (1 + warFaction.DaysSinceSystemAttacked * Settings.AResourceAdjustmentPerCycle / 100);
        attackResources = attackResources * (float)(Random.NextDouble() * (2 * Settings.ResourceSpread) + (1 - Settings.ResourceSpread));
        foreach (string Rfact in tempTargets.Keys)
        {
            warFAR.Add(Rfact, tempTargets[Rfact] * attackResources / total);
        }
    }

    public static void AllocateAttackResources(WarFaction warFaction)
    {
        var sim = UnityGameInstance.BattleTechGame.Simulation;
        var FactionRep = sim.GetRawReputation(FactionValues.Find(x => x.Name == warFaction.faction));
        int maxContracts = Galaxy_at_War.HotSpots.ProcessReputation(FactionRep);
        if (warFaction.warFactionAttackResources.Keys.Count == 0)
            return;
        var warFAR = warFaction.warFactionAttackResources;
        //Go through the different resources allocated from attacking faction to spend against each targetFaction
        foreach (var targetFaction in warFAR.Keys)
        {
            var targetFAR = warFAR[targetFaction];
            var factionDLT = Core.WarStatus.deathListTracker.Find(x => x.faction == warFaction.faction);
            while (targetFAR > 0.0)
            {
                if (!warFaction.attackTargets.Keys.Contains(targetFaction))
                    break;
                if (warFaction.attackTargets[targetFaction].Count == 0)
                    break;
                var rand = Random.Next(0, warFaction.attackTargets[targetFaction].Count);
                var system = WarStatus.systems.Find(f => f.name == warFaction.attackTargets[targetFaction][rand]);

                //Find most valuable target for attacking for later. Used in HotSpots.
                if (factionDLT.deathList[targetFaction] >= Core.Settings.PriorityHatred && system.DifficultyRating <= maxContracts 
                    && system.DifficultyRating >= maxContracts - 4)
                {
                    system.PriorityAttack = true;
                    if (!system.CurrentlyAttackedBy.Contains(warFaction.faction))
                    {
                        system.CurrentlyAttackedBy.Add(warFaction.faction);
                    }
                    if (!WarStatus.PrioritySystems.Contains(system.starSystem.Name))
                    {
                        WarStatus.PrioritySystems.Add(system.starSystem.Name);
                    }
                }

                //Distribute attacking resources to systems.
                if (system.Contended || Core.WarStatus.HotBox.Contains(system.name))
                {
                    warFaction.attackTargets[targetFaction].Remove(system.starSystem.Name);
                    if (warFaction.attackTargets[targetFaction].Count == 0 || !warFaction.attackTargets.Keys.Contains(targetFaction))
                    {
                        break;
                    }
                    else
                        continue;
                }
                
                var maxValueList = system.influenceTracker.Values.OrderByDescending(x => x).ToList();
                float PmaxValue = 200.0f;
                if (maxValueList.Count > 1)
                    PmaxValue = maxValueList[1];
                var ITValue = system.influenceTracker[warFaction.faction];
                float basicAR = (float)(11 - system.DifficultyRating) / 2;

                float bonusAR = 0f;
                if (ITValue > PmaxValue)
                    bonusAR = (ITValue - PmaxValue) * 0.15f;

                float TotalAR = basicAR + bonusAR;

                if (targetFAR > TotalAR)
                {
                    system.influenceTracker[warFaction.faction] += TotalAR;
                    targetFAR -= TotalAR;
                }
                else
                {
                    system.influenceTracker[warFaction.faction] += targetFAR;
                    targetFAR = 0;
                }
            }
        }
    }

    public static void AllocateDefensiveResources(WarFaction warFaction, bool UseFullSet)
    {
        var faction = warFaction.faction;
        if (warFaction.defenseTargets.Count == 0 || !WarStatus.warFactionTracker.Contains(warFaction) || faction == "NoFaction")
            return;

        float defensiveResources = warFaction.DefensiveResources;
        
        var defensiveCorrection = defensiveResources * (100 * Settings.GlobalDefenseFactor -
                Settings.DResourceAdjustmentPerCycle * warFaction.DaysSinceSystemLost) / 100;

        defensiveResources = Math.Max(defensiveResources, defensiveCorrection); 
        defensiveResources = defensiveResources * (float)(Random.NextDouble() * (2 * Settings.ResourceSpread) + (1 - Settings.ResourceSpread));

            while (defensiveResources > 0.0)
            {
                float highest = 0f;
                string highestFaction = faction;
                var rand = Random.Next(0, warFaction.defenseTargets.Count);
                var system = warFaction.defenseTargets[rand];
                var systemStatus = WarStatus.systems.Find(x => x.name == system);
                if (systemStatus.Contended || Core.WarStatus.HotBox.Contains(systemStatus.name))
                {
                    warFaction.defenseTargets.Remove(systemStatus.starSystem.Name);
                    if (warFaction.defenseTargets.Count == 0 || warFaction.defenseTargets == null)
                    {
                        break;
                    }
                    else
                        continue;
                }
                foreach (string tempfaction in systemStatus.influenceTracker.Keys)
                {
                    if (systemStatus.influenceTracker[tempfaction] > highest)

                    {
                        highest = systemStatus.influenceTracker[tempfaction];
                        highestFaction = tempfaction;
                    }
                }
                if (highestFaction == faction)
                {
                    if (defensiveResources > 0)
                    {
                        systemStatus.influenceTracker[faction] += 1;
                        defensiveResources -= 1;
                    }
                    else
                    {
                        systemStatus.influenceTracker[faction] += defensiveResources;
                        defensiveResources = 0;
                    }
                }
                else
                {
                    var totalInfluence = systemStatus.influenceTracker.Values.Sum();
                    var diffRes = systemStatus.influenceTracker[highestFaction] / totalInfluence - systemStatus.influenceTracker[faction] / totalInfluence;
                    var bonusDefense = 1 + (diffRes * totalInfluence - (Settings.TakeoverThreshold / 100) * totalInfluence) / (Settings.TakeoverThreshold / 100 + 1);

                    if (100 * diffRes > Settings.TakeoverThreshold)
                        if (defensiveResources >= bonusDefense)
                        {
                            systemStatus.influenceTracker[faction] += bonusDefense;
                            defensiveResources -= bonusDefense;
                        }
                        else
                        {
                            systemStatus.influenceTracker[faction] += Math.Min(defensiveResources, 5);
                            defensiveResources -= Math.Min(defensiveResources, 5);
                        }
                    else
                    {
                        systemStatus.influenceTracker[faction] += Math.Min(defensiveResources, 5);
                        defensiveResources -= Math.Min(defensiveResources, 5);
                    }
                }
            }
    }

    public static void ChangeSystemOwnership(SimGameState sim, StarSystem system, string faction, bool ForceFlip)
    {
        if (faction != system.OwnerValue.Name || ForceFlip)
        {
            FactionValue OldFaction = system.OwnerValue;
            if (system.Def.Tags.Contains(Settings.FactionTags[OldFaction.Name]))
                system.Def.Tags.Remove(Settings.FactionTags[OldFaction.Name]);
            system.Def.Tags.Add(Settings.FactionTags[faction]);

            if (!Core.WarStatus.AbandonedSystems.Contains(system.Name))
            {
                if (system.Def.SystemShopItems.Count != 0)
                {
                    List<string> TempList = system.Def.SystemShopItems;
                    TempList.Add(Core.Settings.FactionShops[system.OwnerValue.Name]);
                    Traverse.Create(system.Def).Property("SystemShopItems").SetValue(TempList);
                }

                if (system.Def.FactionShopItems != null)
                {
                    Traverse.Create(system.Def).Property("FactionShopOwnerValue").SetValue(FactionValues.Find(x => x.Name == faction));
                    Traverse.Create(system.Def).Property("FactionShopOwnerID").SetValue(faction);
                    List<string> FactionShops = system.Def.FactionShopItems;
                    if (FactionShops.Contains(Settings.FactionShopItems[system.Def.OwnerValue.Name]))
                        FactionShops.Remove(Settings.FactionShopItems[system.Def.OwnerValue.Name]);
                    FactionShops.Add(Settings.FactionShopItems[faction]);
                    Traverse.Create(system.Def).Property("FactionShopItems").SetValue(FactionShops);
                }
            }
            var systemStatus = WarStatus.systems.Find(x => x.name == system.Name);
            var oldOwner = systemStatus.owner;
            systemStatus.owner = faction;
            Traverse.Create(system.Def).Property("OwnerID").SetValue(faction);
            Traverse.Create(system.Def).Property("OwnerValue").SetValue(Core.FactionValues.Find(x => x.Name == faction));
            
            //Change the Kill List for the factions.
            var TotalAR = GetTotalAttackResources(system);
            var TotalDR = GetTotalDefensiveResources(system);
            var SystemValue = TotalAR + TotalDR;
            
            WarFaction WFWinner = WarStatus.warFactionTracker.Find(x => x.faction == faction);
            WFWinner.GainedSystem = true;
            WFWinner.MonthlySystemsChanged += 1;
            WFWinner.TotalSystemsChanged += 1;
            if (Settings.DefendersUseARforDR && Settings.DefensiveFactions.Contains(WFWinner.faction))
            {
                WFWinner.DefensiveResources += TotalAR;
                WFWinner.DefensiveResources += TotalDR;
            }
            else
            {
                WFWinner.AttackResources += TotalAR;
                WFWinner.DefensiveResources += TotalDR;
            }
            
            WarFaction WFLoser = WarStatus.warFactionTracker.Find(x => x.faction == OldFaction.Name);
            WFLoser.LostSystem = true;
            WFLoser.MonthlySystemsChanged -= 1;
            WFLoser.TotalSystemsChanged -= 1;
            RemoveAndFlagSystems(WFLoser, system);
            if (Settings.DefendersUseARforDR && Settings.DefensiveFactions.Contains(WFWinner.faction))
            {
                WFLoser.DefensiveResources -= TotalAR;
                WFLoser.DefensiveResources -= TotalDR;
            }
            else
            {
                WFLoser.AttackResources -= TotalAR;
                WFLoser.DefensiveResources -= TotalDR;
            }
            if (WFLoser.AttackResources < 0)
                WFLoser.AttackResources = 0;
            if (WFLoser.DefensiveResources < 0)
                WFLoser.DefensiveResources = 0;

            if (!WarStatus.SystemChangedOwners.Contains(system.Name))
                WarStatus.SystemChangedOwners.Add(system.Name);

            if (ForceFlip)
            {
                RecalculateSystemInfluence(systemStatus, faction, oldOwner);
                systemStatus.PirateActivity = 0;
            }

            foreach (var neighbor in sim.Starmap.GetAvailableNeighborSystem(system))
            {
                if (!WarStatus.SystemChangedOwners.Contains(neighbor.Name))
                    WarStatus.SystemChangedOwners.Add(neighbor.Name);
            }
        }
    }

    public static void ChangeDeathlistFromAggression(StarSystem system, string faction, string OldFaction)
    {
        var sim = UnityGameInstance.BattleTechGame.Simulation;
        var TotalAR = GetTotalAttackResources(system);
        var TotalDR = GetTotalDefensiveResources(system);
        var SystemValue = TotalAR + TotalDR;
        var KillListDelta = Math.Max(10, SystemValue);
        var factionTracker = WarStatus.deathListTracker.Find(x => x.faction == OldFaction);
        if (factionTracker.deathList[faction] < 50)
            factionTracker.deathList[faction] = 50;

        factionTracker.deathList[faction] += KillListDelta;
        //Allies are upset that their friend is being beaten up.
        if (!Settings.DefensiveFactions.Contains(OldFaction))
        {
            foreach (var ally in sim.GetFactionDef(OldFaction).Allies)
            {
                if (!Settings.IncludedFactions.Contains(ally) || faction  == ally)
                    continue;
                Log(ally);
                var factionAlly = WarStatus.deathListTracker.Find(x => x.faction == ally);
                factionAlly.deathList[faction] += KillListDelta / 2;
                Log("Ally Passed");
            }
        }
        //Enemies of the target faction are happy with the faction doing the beating.
        if (!Settings.DefensiveFactions.Contains(OldFaction))
        {
            foreach (var enemy in sim.GetFactionDef(OldFaction).Enemies)
            {
                Log(enemy);
                if (!Settings.IncludedFactions.Contains(enemy) || enemy == faction)
                    continue;
                var factionEnemy = WarStatus.deathListTracker.Find(x => x.faction == enemy);
                factionEnemy.deathList[faction] -= KillListDelta / 2;
                Log("Enemy Passed");
            }
        }
        factionTracker.AttackedBy.Add(faction);
    }

    public static void CalculateHatred()
    {
        foreach (var warFaction in WarStatus.warFactionTracker)
        {
            Dictionary<string, int> AttackCount = new Dictionary<string, int>();
            foreach (var target in warFaction.attackTargets)
            {
                if (Settings.DefensiveFactions.Contains(target.Key) || Settings.ImmuneToWar.Contains(target.Key))
                    continue;
                AttackCount.Add(target.Key, target.Value.Count);
            }
            int i = 0;
            int tophalf = AttackCount.Count / 2;
            foreach (var attacktarget in AttackCount.OrderByDescending(x => x.Value))
            {
                var warfaction = WarStatus.warFactionTracker.Find(x => x.faction == attacktarget.Key);
                if (i < tophalf)
                    warfaction.IncreaseAggression[warFaction.faction] = true;
                else
                    warFaction.IncreaseAggression[warFaction.faction] = false;
                i++;
            }
        }
    }

    private static void RemoveAndFlagSystems(WarFaction OldOwner, StarSystem system)
    {
        OldOwner.defenseTargets.Remove(system.Name);
        if (!WarStatus.SystemChangedOwners.Contains(system.Name))
            WarStatus.SystemChangedOwners.Add(system.Name);
        foreach (var neighborsystem in UnityGameInstance.BattleTechGame.Simulation.Starmap.GetAvailableNeighborSystem(system))
        {
            var WFAT = WarStatus.warFactionTracker.Find(x => x.faction == neighborsystem.OwnerValue.Name).attackTargets;
            if (WFAT.Keys.Contains(OldOwner.faction) && !WFAT[OldOwner.faction].Contains(system.Name))
                WFAT[OldOwner.faction].Remove(system.Name);
        }
    }

    private static void UpdateInfluenceFromAttacks(SimGameState sim, bool CheckForSystemChange)
    {
        var tempRTFactions = WarStatus.deathListTracker;
        foreach (var deathListTracker in tempRTFactions)
        {
            deathListTracker.AttackedBy.Clear();
        }

        if (CheckForSystemChange)
            WarStatus.LostSystems.Clear();

        LogDebug($"Updating influence for {WarStatus.systems.Count.ToString()} systems");
        foreach (var systemStatus in Core.WarStatus.systems)
        {
            var tempDict = new Dictionary<string, float>();
            var totalInfluence = systemStatus.influenceTracker.Values.Sum();
            var highest = 0f;
            var highestfaction = systemStatus.owner;
            foreach (var kvp in systemStatus.influenceTracker)
            {
                tempDict.Add(kvp.Key, kvp.Value / totalInfluence * 100);
                if (kvp.Value > highest)
                {
                    highest = kvp.Value;
                    highestfaction = kvp.Key;
                }
            }

            systemStatus.influenceTracker = tempDict;
            var diffStatus = systemStatus.influenceTracker[highestfaction] - systemStatus.influenceTracker[systemStatus.owner];
            var starSystem = systemStatus.starSystem;
            
            if (highestfaction != systemStatus.owner && (diffStatus > Settings.TakeoverThreshold && !Core.WarStatus.HotBox.Contains(systemStatus.name)
                && (!Settings.DefensiveFactions.Contains(highestfaction) || highestfaction == "Locals") && !Settings.ImmuneToWar.Contains(starSystem.OwnerValue.Name)))
            {
                if (!systemStatus.Contended)
                {
                    systemStatus.Contended = true;
                    ChangeDeathlistFromAggression(starSystem, highestfaction, starSystem.OwnerValue.Name);
                }
                else if (CheckForSystemChange)
                { 
                    ChangeSystemOwnership(sim, starSystem, highestfaction, false);
                    systemStatus.Contended = false;
                    WarStatus.LostSystems.Add(starSystem.Name);
                }
                   
            }
        }
        CalculateHatred();
        foreach (var deathListTracker in tempRTFactions)
        {
            AdjustDeathList(deathListTracker, sim, false);
        }
    }

    public static string MonthlyWarReport()
    {
        string summaryString = "";
        string summaryString2 = "";
        string combinedString = "";

        foreach (string faction in Settings.IncludedFactions)
        {
            WarFaction warFaction = WarStatus.warFactionTracker.Find(x => x.faction == faction);
            combinedString = combinedString + "<b><u>" + Settings.FactionNames[faction] + "</b></u>\n";
            summaryString = "Monthly Change in Systems: " + warFaction.MonthlySystemsChanged + "\n";
            summaryString2 = "Overall Change in Systems: " + warFaction.TotalSystemsChanged + "\n\n";

            combinedString = combinedString + summaryString + summaryString2;
            warFaction.MonthlySystemsChanged = 0;
        }

        char[] trim = {'\n'};
        combinedString = combinedString.TrimEnd(trim);
        return combinedString;
    }

    public static void RefreshContracts(StarSystem starSystem)
    {
        if (WarStatus.HotBox.Contains(starSystem.Name))
            return;
        var ContractEmployers = starSystem.Def.ContractEmployerIDList;
        var ContractTargets = starSystem.Def.ContractTargetIDList;
        SimGameState sim = UnityGameInstance.BattleTechGame.Simulation;
        var owner = starSystem.OwnerValue;
        ContractEmployers.Clear();
        ContractTargets.Clear();
        if (owner == Core.FactionValues.FirstOrDefault(f => f.Name == "NoFaction"))
        {
            ContractEmployers.Add("AuriganPirates");
            ContractTargets.Add("AuriganPirates");
        }
        else
        {
            ContractEmployers.Add(owner.Name);
            ContractTargets.Add(owner.Name);
        }

        var WarSystem = WarStatus.systems.Find(x => x.name == starSystem.Name);
        var neighborSystems = WarSystem.neighborSystems;
        foreach (var systemNeighbor in neighborSystems.Keys)
        {
            if (Settings.ImmuneToWar.Contains(systemNeighbor) || systemNeighbor == "NoFaction")
                continue;
            if (!ContractEmployers.Contains(systemNeighbor) && !Settings.DefensiveFactions.Contains(systemNeighbor))
                ContractEmployers.Add(systemNeighbor);

            if (!ContractTargets.Contains(systemNeighbor) && !Settings.DefensiveFactions.Contains(systemNeighbor))
                ContractTargets.Add(systemNeighbor);
        }
        if (ContractEmployers.Count == 1 && Settings.DefensiveFactions.Contains(ContractEmployers[0]))
        {
            FactionValue faction = Core.FactionValues.Find(x => x.Name == "AuriganRestoration");
            List<string> TempFaction = new List<string>(Settings.IncludedFactions);
            do
            {
                var randFaction = Random.Next(0, TempFaction.Count);
                faction = Core.FactionValues.Find(x => x.Name == Settings.IncludedFactions[randFaction]);
                if (Settings.DefensiveFactions.Contains(faction.Name))
                {
                    TempFaction.RemoveAt(randFaction);
                    continue;
                }
                else
                    break;
            } while (TempFaction.Count != 0);

            ContractEmployers.Add(faction.Name);
            if (!ContractTargets.Contains(faction.Name))
                ContractTargets.Add(faction.Name);
        }

        if ((ContractEmployers.Count == 1 || WarSystem.PirateActivity > 0) && !ContractEmployers.Contains("AuriganPirates"))
        {
            ContractEmployers.Add("AuriganPirates");
            ContractTargets.Add("AuriganPirates");
        }

        if (!WarStatus.AbandonedSystems.Contains(starSystem.Name))
        {
            if (!ContractEmployers.Contains("Locals"))
                ContractEmployers.Add("Locals");
            if (!ContractTargets.Contains("Locals"))
                ContractTargets.Add("Locals");
        }
    }

    [HarmonyPatch(typeof(SimGameState), "GenerateContractParticipants")]
    public static class SimGameState_GenerateContractParticipants_Patch
    {
        public static void Prefix(FactionDef employer, StarSystemDef system)
        {
            var sim = UnityGameInstance.BattleTechGame.Simulation;
            if (Core.WarStatus == null || (sim.IsCampaign && !sim.CompanyTags.Contains("story_complete")))
                return;

            FactionEnemyHolder.Clear();
            var NewEnemies = system.ContractTargetIDList;
            FactionEnemyHolder = employer.Enemies.ToList();
            var NewFactionEnemies = FactionEnemyHolder;
            foreach (var Enemy in NewEnemies)
            {
                if (!NewFactionEnemies.Contains(Enemy) && !employer.Allies.Contains(Enemy) && Enemy != employer.FactionValue.Name)
                {
                    NewFactionEnemies.Add(Enemy);
                }
            }
            Traverse.Create(employer).Property("Enemies").SetValue(NewFactionEnemies.ToArray());
        }

        public static void Postfix(FactionDef employer)
        {
            var sim = UnityGameInstance.BattleTechGame.Simulation;
            if (Core.WarStatus == null || (sim.IsCampaign && !sim.CompanyTags.Contains("story_complete")))
                return;

            Traverse.Create(employer).Property("Enemies").SetValue(FactionEnemyHolder.ToArray());
        }
    }


    public static void AdjustDeathList(DeathListTracker deathListTracker, SimGameState sim, bool ReloadFromSave)
    {
        var deathList = deathListTracker.deathList;
        var KL_List = new List<string>(deathList.Keys);
        var warFaction = WarStatus.warFactionTracker.Find(x => x.faction == deathListTracker.faction);

        var deathListFaction = deathListTracker.faction;
        foreach (string faction in KL_List)
        {
            if (!ReloadFromSave)
            {
                //Factions adjust hatred based upon how much they are being attacked. But there is diminishing returns further from 50.
                int direction = -1;
                if (warFaction.IncreaseAggression.Keys.Contains(faction) && warFaction.IncreaseAggression[faction])
                    direction = 1;
                {
                    if (deathList[faction] > 50)
                        deathList[faction] += direction * (1 - (deathList[faction] - 50) / 50);
                    else if (deathList[faction] <= 50)
                        deathList[faction] += direction * (1 - (50 - deathList[faction]) / 50);
                }

                //Ceiling and floor for faction enmity. 
                if (deathList[faction] > 99)
                    deathList[faction] = 99;

                if (deathList[faction] < 1)
                    deathList[faction] = 1;

                //Defensive Only factions are always neutral
                if (Settings.DefensiveFactions.Contains(faction))
                    deathList[faction] = 50;
            }
            if (deathList[faction] > 75)
            {
                if (!sim.GetFactionDef(deathListFaction).Enemies.Contains((faction)))
                {
                    var enemies = new List<string>(sim.GetFactionDef(deathListFaction).Enemies);
                    enemies.Add(faction);
                    Traverse.Create(sim.GetFactionDef(deathListFaction)).Property("Enemies").SetValue(enemies.ToArray());
                }

                if (sim.GetFactionDef(deathListFaction).Allies.Contains(faction))
                {
                    var allies = new List<String>(sim.GetFactionDef(deathListFaction).Allies);
                    allies.Remove(faction);
                    Traverse.Create(sim.GetFactionDef(deathListFaction)).Property("Allies").SetValue(allies.ToArray());
                }
            }

            if (deathList[faction] <= 75 && deathList[faction] > 25)
            {
                if (sim.GetFactionDef(deathListFaction).Enemies.Contains(faction))
                {
                    var enemies = new List<string>(sim.GetFactionDef(deathListFaction).Enemies);
                    enemies.Remove(faction);
                    Traverse.Create(sim.GetFactionDef(deathListFaction)).Property("Enemies").SetValue(enemies.ToArray());
                }

                if (sim.GetFactionDef(deathListFaction).Allies.Contains(faction))
                {
                    var allies = new List<string>(sim.GetFactionDef(deathListFaction).Allies);
                    allies.Remove(faction);
                    Traverse.Create(sim.GetFactionDef(deathListFaction)).Property("Allies").SetValue(allies.ToArray());
                }
            }

            if (deathList[faction] <= 25)
            {
                if (!sim.GetFactionDef(deathListFaction).Allies.Contains(faction))
                {
                    var allies = new List<string>(sim.GetFactionDef(deathListFaction).Allies);
                    allies.Add(faction);
                    Traverse.Create(sim.GetFactionDef(deathListFaction)).Property("Allies").SetValue(allies.ToArray());
                }

                if (sim.GetFactionDef(deathListFaction).Enemies.Contains(faction))
                {
                    var enemies = new List<string>(sim.GetFactionDef(deathListFaction).Enemies);
                    enemies.Remove(faction);
                    Traverse.Create(sim.GetFactionDef(deathListFaction)).Property("Enemies").SetValue(enemies.ToArray());
                }
            }
        }
    }

    public static float GetTotalAttackResources(StarSystem system)
    {
        float result = 0;
        if (system.Tags.Contains("planet_industry_poor"))
            result += Settings.planet_industry_poor;
        if (system.Tags.Contains("planet_industry_mining"))
            result += Settings.planet_industry_mining;
        if (system.Tags.Contains("planet_industry_rich"))
            result += Settings.planet_industry_rich;
        if (system.Tags.Contains("planet_industry_manufacturing"))
            result += Settings.planet_industry_manufacturing;
        if (system.Tags.Contains("planet_industry_research"))
            result += Settings.planet_industry_research;
        if (system.Tags.Contains("planet_other_starleague"))
            result += Settings.planet_other_starleague;
        return result;
    }

    public static float GetTotalDefensiveResources(StarSystem system)
    {
        float result = 0;
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
        if (system.Tags.Contains("planet_other_comstar"))
            result += Settings.planet_other_comstar;
        return result;
    }

    [HarmonyPatch(typeof(Contract), "CompleteContract")]
    public static class CompleteContract_Patch
    {
        public static void Postfix(Contract __instance, MissionResult result, bool isGoodFaithEffort)
        {
            var sim = UnityGameInstance.BattleTechGame.Simulation;
            if (Core.WarStatus == null || (sim.IsCampaign && !sim.CompanyTags.Contains("story_complete")))
                return;

            teamfaction = __instance.GetTeamFaction("ecc8d4f2-74b4-465d-adf6-84445e5dfc230").Name;
            enemyfaction = __instance.GetTeamFaction("be77cadd-e245-4240-a93e-b99cc98902a5").Name;
            difficulty = __instance.Difficulty;
            missionResult = result;
            contractType = __instance.Override.contractTypeID;
            if (__instance.IsFlashpointContract || __instance.IsFlashpointCampaignContract)
                IsFlashpointContract = true;
            else
                IsFlashpointContract = false;
            
        }

        [HarmonyPatch(typeof(SimGameState), "ResolveCompleteContract")]
        public static class SimGameState_ResolveCompleteContract_Patch
        {
            public static void Postfix(SimGameState __instance)
            {
                var sim = UnityGameInstance.BattleTechGame.Simulation;
                if (Core.WarStatus == null || (sim.IsCampaign && !sim.CompanyTags.Contains("story_complete")))
                    return;

                if (IsFlashpointContract)
                    return;

                var warsystem = WarStatus.systems.Find(x => x.name == __instance.CurSystem.Name);

                if (missionResult == MissionResult.Victory)
                {
                    double deltaInfluence = 0;
                    if (teamfaction == "AuriganPirates")
                    {
                        deltaInfluence = Core.DeltaInfluence(__instance.CurSystem.Name, difficulty, contractType, enemyfaction, true);
                        warsystem.PirateActivity += (float)deltaInfluence;
                    }
                    else if (enemyfaction == "AuriganPirates")
                    {
                        deltaInfluence = Core.DeltaInfluence(__instance.CurSystem.Name, difficulty, contractType, enemyfaction, true);
                        warsystem.PirateActivity -= (float)deltaInfluence;
                    }
                    else
                    {
                        deltaInfluence = Core.DeltaInfluence(__instance.CurSystem.Name, difficulty, contractType, enemyfaction, false);
                        warsystem.influenceTracker[teamfaction] += (float)deltaInfluence;
                        warsystem.influenceTracker[enemyfaction] -= (float)deltaInfluence;
                    }

                    //if (contractType == ContractType.AttackDefend || contractType == ContractType.FireMission)
                    //{
                    //    if (Settings.IncludedFactions.Contains(teamfaction))
                    //    {
                    //        if (!Settings.DefensiveFactions.Contains(teamfaction))
                    //            WarStatus.warFactionTracker.Find(x => x.faction == teamfaction).AttackResources += difficulty;
                    //        else
                    //            WarStatus.warFactionTracker.Find(x => x.faction == teamfaction).DefensiveResources += difficulty;
                    //    }

                    //    if (Settings.IncludedFactions.Contains(enemyfaction))
                    //    {
                    //        WarStatus.warFactionTracker.Find(x => x.faction == enemyfaction).DefensiveResources -= difficulty;
                    //        if (WarStatus.warFactionTracker.Find(x => x.faction == enemyfaction).DefensiveResources < 0)
                    //            WarStatus.warFactionTracker.Find(x => x.faction == enemyfaction).DefensiveResources = 0;
                    //    }
                    //    else if (enemyfaction == "AuriganPirates")
                    //    {
                    //        warsystem.PirateActivity -= difficulty;
                    //        if (warsystem.PirateActivity < 0)
                    //            warsystem.PirateActivity = 0;
                    //    }
                    //}

                    var OldOwner = sim.CurSystem.OwnerValue.Name;
                    if (Core.WillSystemFlip(__instance.CurSystem.Name, teamfaction, enemyfaction, deltaInfluence, false))
                    {
                        ChangeSystemOwnership(__instance, warsystem.starSystem, teamfaction, false);
                        GameInstance game = LazySingletonBehavior<UnityGameInstance>.Instance.Game;
                        SimGameInterruptManager interruptQueue = (SimGameInterruptManager)AccessTools
                            .Field(typeof(SimGameState), "interruptQueue").GetValue(game.Simulation);
                        interruptQueue.QueueGenericPopup_NonImmediate("ComStar Bulletin: Galaxy at War", __instance.CurSystem.Name + " taken! "
                            + Settings.FactionNames[teamfaction] + " conquered from " + Settings.FactionNames[OldOwner], true, null);

                        if (WarStatus.HotBox.Contains(sim.CurSystem.Name))
                        {
                            WarStatus.HotBox.Remove(sim.CurSystem.Name);
                            WarStatus.EscalationDays = 0;
                            warsystem.BonusCBills = false;
                            warsystem.BonusSalvage = false;
                            warsystem.BonusXP = false;
                            if (WarStatus.EscalationOrder != null)
                            {
                                WarStatus.EscalationOrder.SetCost(0);
                                TaskManagementElement taskManagementElement4 = null;
                                TaskTimelineWidget timelineWidget = (TaskTimelineWidget)AccessTools.Field(typeof(SGRoomManager), "timelineWidget").GetValue(__instance.RoomManager);
                                Dictionary<WorkOrderEntry, TaskManagementElement> ActiveItems =
                                    (Dictionary<WorkOrderEntry, TaskManagementElement>)AccessTools.Field(typeof(TaskTimelineWidget), "ActiveItems").GetValue(timelineWidget);
                                if (ActiveItems.TryGetValue(WarStatus.EscalationOrder, out taskManagementElement4))
                                {
                                    taskManagementElement4.UpdateItem(0);
                                }
                            }
                        }

                        foreach (var system in WarStatus.SystemChangedOwners)
                        {
                            var systemStatus = WarStatus.systems.Find(x => x.name == system);
                            systemStatus.CurrentlyAttackedBy.Clear();
                            CalculateAttackAndDefenseTargets(systemStatus.starSystem);
                            RefreshContracts(systemStatus.starSystem);
                        }
                        WarStatus.SystemChangedOwners.Clear();

                        bool HasFlashpoint = false;
                        foreach (var contract in __instance.CurSystem.SystemContracts)
                        {
                            if (contract.IsFlashpointContract)
                                HasFlashpoint = true;
                        }
                        if (!HasFlashpoint)
                        {
                            NeedsProcessing = true;
                            var cmdCenter = UnityGameInstance.BattleTechGame.Simulation.RoomManager.CmdCenterRoom;
                            __instance.CurSystem.GenerateInitialContracts(() => Traverse.Create(cmdCenter).Method("OnContractsFetched"));
                            NeedsProcessing = false;
                        }

                        __instance.StopPlayMode();
                    }
                }
            }
        }
    }

    public static void SystemDifficulty()
    {
        bool GetPirateFlex = true;
        var sim = UnityGameInstance.BattleTechGame.Simulation;
        var TotalSystems = WarStatus.systems.Count;
        var DifficultyCutoff = TotalSystems / 10;
        int i = 0;
        foreach (var system in WarStatus.systems.OrderBy(x => x.TotalResources))
        {
            var SimSystem = sim.StarSystems.Find(x => x.Name == system.name);
            if (Settings.ChangeDifficulty)
            {
                if (i <= DifficultyCutoff)
                {
                    system.DifficultyRating = 1;
                    List<int> difficultyList = new List<int> { 1, 1 };
                    Traverse.Create(SimSystem.Def).Field("DifficultyList").SetValue(difficultyList);
                    Traverse.Create(SimSystem.Def).Field("DefaultDifficulty").SetValue(1);
                }
                if (i <= DifficultyCutoff * 2 && i > DifficultyCutoff)
                {
                    system.DifficultyRating = 2;
                    List<int> difficultyList = new List<int> { 2, 2 };
                    Traverse.Create(SimSystem.Def).Field("DifficultyList").SetValue(difficultyList);
                    Traverse.Create(SimSystem.Def).Field("DefaultDifficulty").SetValue(2);
                }
                if (i <= DifficultyCutoff * 3 && i > 2 * DifficultyCutoff)
                {
                    system.DifficultyRating = 3;
                    List<int> difficultyList = new List<int> { 3, 3 };
                    Traverse.Create(SimSystem.Def).Field("DifficultyList").SetValue(difficultyList);
                    Traverse.Create(SimSystem.Def).Field("DefaultDifficulty").SetValue(3);
                }
                if (i <= DifficultyCutoff * 4 && i > 3 * DifficultyCutoff)
                {
                    system.DifficultyRating = 4;
                    List<int> difficultyList = new List<int> { 4, 4 };
                    Traverse.Create(SimSystem.Def).Field("DifficultyList").SetValue(difficultyList);
                    Traverse.Create(SimSystem.Def).Field("DefaultDifficulty").SetValue(4);
                }
                if (i <= DifficultyCutoff * 5 && i > 4 * DifficultyCutoff)
                {
                    system.DifficultyRating = 5;
                    List<int> difficultyList = new List<int> { 5, 5 };
                    Traverse.Create(SimSystem.Def).Field("DifficultyList").SetValue(difficultyList);
                    Traverse.Create(SimSystem.Def).Field("DefaultDifficulty").SetValue(5);
                }
                if (i <= DifficultyCutoff * 6 && i > 5 * DifficultyCutoff)
                {
                    system.DifficultyRating = 6;
                    List<int> difficultyList = new List<int> { 6, 6 };
                    Traverse.Create(SimSystem.Def).Field("DifficultyList").SetValue(difficultyList);
                    Traverse.Create(SimSystem.Def).Field("DefaultDifficulty").SetValue(6);
                    if (GetPirateFlex)
                    {
                        WarStatus.PirateFlex = system.TotalResources;
                        GetPirateFlex = false;
                    }
                }
                if (i <= DifficultyCutoff * 7 && i > 6 * DifficultyCutoff)
                {
                    system.DifficultyRating = 7;
                    List<int> difficultyList = new List<int> { 7, 7 };
                    Traverse.Create(SimSystem.Def).Field("DifficultyList").SetValue(difficultyList);
                    Traverse.Create(SimSystem.Def).Field("DefaultDifficulty").SetValue(7);
                }
                if (i <= DifficultyCutoff * 8 && i > 7 * DifficultyCutoff)
                {
                    system.DifficultyRating = 8;
                    List<int> difficultyList = new List<int> { 8, 8 };
                    Traverse.Create(SimSystem.Def).Field("DifficultyList").SetValue(difficultyList);
                    Traverse.Create(SimSystem.Def).Field("DefaultDifficulty").SetValue(8);
                }
                if (i <= DifficultyCutoff * 9 && i > 8 * DifficultyCutoff)
                {
                    system.DifficultyRating = 9;
                    List<int> difficultyList = new List<int> { 9, 9 };
                    Traverse.Create(SimSystem.Def).Field("DifficultyList").SetValue(difficultyList);
                    Traverse.Create(SimSystem.Def).Field("DefaultDifficulty").SetValue(9);
                }
                if (i > 9 * DifficultyCutoff)
                {
                    system.DifficultyRating = 10;
                    List<int> difficultyList = new List<int> { 10, 10 };
                    Traverse.Create(SimSystem.Def).Field("DifficultyList").SetValue(difficultyList);
                    Traverse.Create(SimSystem.Def).Field("DefaultDifficulty").SetValue(10);
                }
                i++;
            }
            else
            {
                system.DifficultyRating = SimSystem.Def.DefaultDifficulty;
                if (GetPirateFlex)
                {
                    WarStatus.PirateFlex = 50;
                    GetPirateFlex = false;
                }
            }
            if (SimSystem.Def.OwnerValue.Name != "NoFaction" && SimSystem.Def.SystemShopItems.Count == 0)
            {
                List<string> TempList = new List<string>();
                TempList.Add("itemCollection_minor_Locals");
                Traverse.Create(SimSystem.Def).Property("SystemShopItems").SetValue(TempList);
                if (sim.CurSystem.Name == SimSystem.Def.Description.Name)
                {
                    Shop.RefreshType refreshShop = Shop.RefreshType.RefreshIfEmpty;
                    SimSystem.SystemShop.Rehydrate(sim, SimSystem, SimSystem.Def.SystemShopItems, refreshShop,
                        Shop.ShopType.System);
                }
            }

        }
    }
    //82 characters per line.
    public static void GaW_Notification()
    {
        SimGameResultAction simGameResultAction = new SimGameResultAction();
        simGameResultAction.Type = SimGameResultAction.ActionType.System_ShowSummaryOverlay;
        simGameResultAction.value = Strings.T("Galaxy at War");
        simGameResultAction.additionalValues = new string[1];
        simGameResultAction.additionalValues[0] = Strings.T("In Galaxy at War, the Great Houses of the Inner Sphere will not simply wait for a wedding invitation" +
                                                            " to show their disdain for each other. To that end, war will break out as petty bickering turns into all out conflict. Your reputation with the factions" +
                                                            " is key - the more they like you, the more they'll bring you to the front lines and the greater the rewards. Perhaps an enterprising mercenary could make their" +
                                                            " fortune changing the tides of battle and helping a faction dominate the Inner Sphere.\n\n <b>New features in Galaxy at War:</b>" +
                                                            "\n� Each planet generates Attack Resources and Defensive Resources that they will be constantly " +
                                                            "spending to spread their influence and protect their own systems." +
                                                            "\n� Planetary Resources and Faction Influence can be seen on the Star Map by hovering over any system." +
                                                            "\n� Successfully completing missions will swing the influence towards the Faction granting the contract." +
                                                            "\n� Target Acquisition Missions & Attack and Defend Missions will give a permanent bonus to the winning faction's Attack Resources and a permanent deduction to the losing faction's Defensive Resources." +
                                                            "\n� If you accept a travel contract the Faction will blockade the system for 30 days. A bonus will be granted for every mission you complete within that system during that time." +
                                                            "\n� Pirates are active and will reduce Resources in a system. High Pirate activity will be highlighted in red." +
                                                            "\n� Sumire will flag the systems in purple on the Star Map that are the most valuable local targets." +
                                                            "\n� Sumire will also highlight systems in yellow that have changed ownership during the previous month." +
                                                            "\n� Hitting Control-R will bring up a summary of the Faction's relationships and their overall war status." +
                                                            "\n\n****Press Enter to Continue****");


        SimGameState.ApplyEventAction(simGameResultAction, null);
        UnityGameInstance.BattleTechGame.Simulation.StopPlayMode();
    }

    [HarmonyPatch(typeof(SGContractsWidget), "GetContractComparePriority")]
    public static class SGContractsWidget_GetContractComparePriority_Patch
    {
        static bool Prefix(ref int __result, Contract contract)
        {
            var sim = UnityGameInstance.BattleTechGame.Simulation;
            if (Core.WarStatus == null || (sim.IsCampaign && !sim.CompanyTags.Contains("story_complete")))
                return true;

            int difficulty = contract.Override.GetUIDifficulty();
            int result = 100;
            var Sim = UnityGameInstance.BattleTechGame.Simulation;
            if (Sim.ContractUserMeetsReputation(contract))
            {
                if (contract.IsFlashpointContract)
                    result = 0;
                else if (contract.Override.contractDisplayStyle == ContractDisplayStyle.BaseCampaignRestoration)
                    result = 1;
                else if (contract.Override.contractDisplayStyle == ContractDisplayStyle.BaseCampaignStory)
                    result = difficulty + 11;
                else if (contract.TargetSystem.Replace("starsystemdef_", "").Equals(Sim.CurSystem.Name))
                    result = difficulty + 1;
                else
                    result = difficulty + 21;
            }
            else
                result = difficulty + 31;

            __result = result;
            return false;
        }
    }

    public static void RecalculateSystemInfluence(SystemStatus systemStatus, string NewOwner, string OldOwner)
    {
        systemStatus.influenceTracker.Clear();
        systemStatus.influenceTracker.Add(NewOwner, Settings.DominantInfluence);
        systemStatus.influenceTracker.Add(OldOwner, Settings.MinorInfluencePool);

        foreach (var faction in Settings.IncludedFactions)
        {
            if (!systemStatus.influenceTracker.Keys.Contains(faction))
                systemStatus.influenceTracker.Add(faction, 0);
        }
    }

    //Show on the Contract Description how this will impact the war. 
    [HarmonyPatch(typeof(SGContractsWidget), "PopulateContract")]
    public static class SGContractsWidget_PopulateContract_Patch
    {
        static void Prefix(ref Contract contract, ref string __state)
        {
            var sim = UnityGameInstance.BattleTechGame.Simulation;
            if (Core.WarStatus == null || (sim.IsCampaign && !sim.CompanyTags.Contains("story_complete")))
                return;

            if (contract.IsFlashpointContract || contract.IsFlashpointCampaignContract)
                return;

            __state = contract.Override.shortDescription;
            var StringHolder = contract.Override.shortDescription;
            var EmployerFaction = contract.GetTeamFaction("ecc8d4f2-74b4-465d-adf6-84445e5dfc230");
            var DefenseFaction = contract.GetTeamFaction(("be77cadd-e245-4240-a93e-b99cc98902a5"));

            var TargetSystem = contract.TargetSystem;
            var SystemName = sim.StarSystems.Find(x => x.ID == TargetSystem);

            bool pirates = false;
            if (EmployerFaction.Name == "AuriganPirates" || DefenseFaction.Name == "AuriganPirates")
                pirates = true;

            double DeltaInfluence = Core.DeltaInfluence(SystemName.Name, contract.Difficulty, contract.Override.contractTypeID, DefenseFaction.Name, pirates);

            bool SystemFlip = false;
            if (EmployerFaction.Name != "AuriganPirates" && DefenseFaction.Name != "AuriganPirates")
                SystemFlip = Core.WillSystemFlip(SystemName.Name, EmployerFaction.Name, DefenseFaction.Name, DeltaInfluence, true);

            string AttackerString = Settings.FactionNames[EmployerFaction.Name] + ": +" + DeltaInfluence.ToString();
            string DefenderString = Settings.FactionNames[DefenseFaction.Name] + ": -" + DeltaInfluence.ToString();

            if (EmployerFaction.Name != "AuriganPirates" && DefenseFaction.Name != "AuriganPirates")
            {
                if (!SystemFlip)
                    StringHolder = "<b>Impact on System Conflict:</b>\n " + AttackerString + "; " + DefenderString + "\n\n" + StringHolder;
                else
                    StringHolder = "<b>***SYSTEM WILL CHANGE OWNERS*** Impact on System Conflict:</b>\n " + AttackerString + "; " + DefenderString + "\n\n" + StringHolder;
            }
            else if (EmployerFaction.Name == "AuriganPirates")
                StringHolder = "<b>Impact on Pirate Activity:</b>\n " + AttackerString + "\n\n" + StringHolder;
            else if (DefenseFaction.Name == "AuriganPirates")
                StringHolder = "<b>Impact on Pirate Activity:</b>\n " + DefenderString + "\n\n" + StringHolder;

            contract.Override.shortDescription = StringHolder;
        }
        static void Postfix(ref Contract contract, ref string __state)
        {
            contract.Override.shortDescription = __state;
        }
    }

    internal static double DeltaInfluence(string system, int contractDifficulty, string contractTypeID, string DefenseFaction, bool PiratesInvolved)
    {
        var TargetSystem = WarStatus.systems.Find(x => x.name == system);
        var MaximumInfluence = TargetSystem.PirateActivity;
        if (!PiratesInvolved)
            MaximumInfluence = TargetSystem.influenceTracker[DefenseFaction];

        var InfluenceChange = (11 + contractDifficulty - 2 * TargetSystem.DifficultyRating) * Settings.ContractImpact[contractTypeID] / 1.5;
        if (PiratesInvolved)
            InfluenceChange *= 2;
        InfluenceChange = Math.Max(InfluenceChange, 0.5);
        InfluenceChange = Math.Min(InfluenceChange, MaximumInfluence);
        if (PiratesInvolved && DefenseFaction != "AuriganPirates")
            InfluenceChange = Math.Min(InfluenceChange, 100 - MaximumInfluence);
        InfluenceChange = Math.Round(InfluenceChange, 1);
        return InfluenceChange;
    }

    internal static bool WillSystemFlip(string system, string Winner, string Loser, double deltainfluence, bool PreBattle)
    {
        var Sim = UnityGameInstance.BattleTechGame.Simulation;
        var warsystem = WarStatus.systems.Find(x => x.name == system);
        var tempIT = new Dictionary<string, float>(warsystem.influenceTracker);

        if (PreBattle)
        {
            tempIT[Winner] += (float)deltainfluence;
            tempIT[Loser] -= (float)deltainfluence;
        }
        var highKey = tempIT.OrderByDescending(x => x.Value).Select(x => x.Key).First();
        var highValue = tempIT.OrderByDescending(x => x.Value).Select(x => x.Value).First();
        tempIT.Remove(highKey);
        var secondValue = tempIT.OrderByDescending(x => x.Value).Select(x => x.Value).First();

        if (highKey != warsystem.owner && highKey == Winner && highValue - secondValue > Settings.TakeoverThreshold
            && (!Settings.DefensiveFactions.Contains(Winner) && !Settings.ImmuneToWar.Contains(Loser)))
            return true;
        else
            return false;
    }

    //Logging a part of contract generation to see if I can track down an infinite load problem. 
    //[HarmonyPatch(typeof(SimGameState), "FillMapEncounterContractData")]
    //public static class MainMenu_Init_Patch
    //{
    //    static void Prefix(StarSystem system, SimGameState.ContractDifficultyRange diffRange, Dictionary<int, List<ContractOverride>> potentialContracts,
    //        Dictionary<string, WeightedList<SimGameState.ContractParticipants>> validTargets, MapAndEncounters level)
    //    {
    //        Log("-------------------");
    //        Log("Stuck in here?");
    //        Log(system.Name);
    //        foreach (var i in potentialContracts)
    //            Log(potentialContracts[i.Key].Count().ToString());
    //        foreach (var targets in validTargets)
    //        {
    //            Log(targets.Key);
    //            foreach (var participant in validTargets[targets.Key])
    //                Log("    " + participant.Target.Name);
    //        }
    //    }
    //}

    //internal static void WarSummary(string eventString)
    //{
    //    var simGame = UnityGameInstance.BattleTechGame.Simulation;
    //    var eventDef = new SimGameEventDef(
    //            SimGameEventDef.EventPublishState.PUBLISHED,
    //            SimGameEventDef.SimEventType.UNSELECTABLE,
    //            EventScope.Company,
    //            new DescriptionDef(
    //                "SalvageOperationsEventID",
    //                "Salvage Operations",
    //                eventString,
    //                "uixTxrSpot_YangWorking.png",
    //                0, 0, false, "", "", ""),
    //            new RequirementDef { Scope = EventScope.Company },
    //            new RequirementDef[0],
    //            new SimGameEventObject[0],
    //            null, 1, false);


    //    var eventTracker = new SimGameEventTracker();
    //    eventTracker.Init(new[] { EventScope.Company }, 0, 0, SimGameEventDef.SimEventType.NORMAL, simGame);
    //    simGame.InterruptQueue.QueueEventPopup(eventDef, EventScope.Company, eventTracker);


    //}


}