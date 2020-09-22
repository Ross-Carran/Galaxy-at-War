using System;
using System.Collections.Generic;
using System.Linq;
using BattleTech;
using BattleTech.Framework;
using BattleTech.UI;
using Harmony;
using UnityEngine;
using static GalaxyatWar.Logger;
using static GalaxyatWar.Helpers;
using Random = System.Random;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedType.Global

namespace GalaxyatWar
{
    public class HotSpots
    {
        private static bool isBreadcrumb;
        public static int BonusMoney = 0;

        public static Dictionary<string, List<StarSystem>> ExternalPriorityTargets = new Dictionary<string, List<StarSystem>>();
        public static readonly List<StarSystem> HomeContendedSystems = new List<StarSystem>();
        public static readonly Dictionary<StarSystem, float> FullHomeContendedSystems = new Dictionary<StarSystem, float>();

        public static void ProcessHotSpots()
        {
            try
            {
                var curSystem = Mod.Globals.WarStatusTracker.systems.Find(x => x.name == Mod.Globals.WarStatusTracker.CurSystem);
                if (curSystem == null)
                {
                    LogDebug("Major problems!");
                    return;
                }

                var dominantFaction = curSystem.owner;
                if (dominantFaction == null)
                {
                    curSystem.owner = curSystem.influenceTracker.OrderByDescending(x => x.Value).Select(x => x.Key).First();
                }

                FullHomeContendedSystems.Clear();
                HomeContendedSystems.Clear();
                ExternalPriorityTargets.Clear();
                Mod.Globals.WarStatusTracker.HomeContendedStrings.Clear();
                var factRepDict = new Dictionary<string, int>();
                foreach (var faction in Mod.Globals.IncludedFactions)
                {
                    ExternalPriorityTargets.Add(faction, new List<StarSystem>());
                    var maxContracts = ProcessReputation(Mod.Globals.Sim.GetRawReputation(Mod.Globals.FactionValues.Find(x => x.Name == faction)));
                    factRepDict.Add(faction, maxContracts);
                }

                //Populate lists with planets that are in danger of flipping
                foreach (var systemStatus in Mod.Globals.WarStatusTracker.systems)
                {
                    if (!Mod.Globals.WarStatusTracker.HotBox.Contains(systemStatus.name))
                    {
                        systemStatus.BonusCBills = false;
                        systemStatus.BonusSalvage = false;
                        systemStatus.BonusXP = false;
                    }

                    if (systemStatus.Contended && systemStatus.DifficultyRating <= factRepDict[systemStatus.owner]
                                               && systemStatus.DifficultyRating >= factRepDict[systemStatus.owner] - 4)
                        systemStatus.PriorityDefense = true;
                    if (systemStatus.PriorityDefense)
                    {
                        if (systemStatus.owner == dominantFaction)
                            FullHomeContendedSystems.Add(systemStatus.starSystem, systemStatus.TotalResources);
                        else
                            ExternalPriorityTargets[systemStatus.owner].Add(systemStatus.starSystem);
                    }

                    if (systemStatus.PriorityAttack)
                    {
                        foreach (var attacker in systemStatus.CurrentlyAttackedBy)
                        {
                            if (attacker == dominantFaction)
                            {
                                if (!FullHomeContendedSystems.Keys.Contains(systemStatus.starSystem))
                                    FullHomeContendedSystems.Add(systemStatus.starSystem, systemStatus.TotalResources);
                            }
                            else
                            {
                                if (!ExternalPriorityTargets[attacker].Contains(systemStatus.starSystem))
                                    ExternalPriorityTargets[attacker].Add(systemStatus.starSystem);
                            }
                        }
                    }
                }

                var i = 0;
                foreach (var system in FullHomeContendedSystems.OrderByDescending(x => x.Value))
                {
                    if (i < FullHomeContendedSystems.Count)
                    {
                        Mod.Globals.WarStatusTracker.HomeContendedStrings.Add(system.Key.Name);
                    }

                    HomeContendedSystems.Add(system.Key);
                    i++;
                }
            }
            catch (Exception ex)
            {
                Error(ex);
            }
        }

        [HarmonyPatch(typeof(StarSystem), "GenerateInitialContracts")]
        public static class SimGameStateGenerateInitialContractsPatch
        {
            private static void Prefix(ref float __state)
            {
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                Traverse.Create(Mod.Globals.Sim.CurSystem).Property("MissionsCompleted").SetValue(0);
                Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurBreadcrumbOverride").SetValue(0);
                Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurMaxBreadcrumbs").SetValue(0);
                __state = Mod.Globals.Sim.CurSystem.CurMaxContracts;

                foreach (var theFaction in Mod.Globals.IncludedFactions)
                {
                    // todo refactor this into a dict lookup and populate it properly
                    var deathListTracker = Mod.Globals.WarStatusTracker.deathListTracker.Find(x => x.faction == theFaction);
                    if (deathListTracker == null)
                    {
                        var _ = new DeathListTracker {faction = theFaction};
                        Mod.Globals.WarStatusTracker.deathListTracker.Add(_);
                        deathListTracker = _;
                        LogDebug($"Created new DeathListTracker for {theFaction}");
                    }

                    AdjustDeathList(deathListTracker, true);
                }

                if (Mod.Settings.LimitSystemContracts.ContainsKey(Mod.Globals.Sim.CurSystem.Name))
                {
                    Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurMaxContracts").SetValue(Mod.Settings.LimitSystemContracts[Mod.Globals.Sim.CurSystem.Name]);
                }

                if (Mod.Globals.WarStatusTracker.Deployment)
                {
                    Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurMaxContracts").SetValue(Mod.Settings.DeploymentContracts);
                }
            }


            private static void Postfix(ref float __state)
            {
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                if (Mod.Globals.WarStatusTracker.systems.Count > 0)
                    ProcessHotSpots();

                isBreadcrumb = true;
                Mod.Globals.Sim.CurSystem.activeSystemBreadcrumbs.Clear();
                Traverse.Create(Mod.Globals.Sim.CurSystem).Property("MissionsCompleted").SetValue(20);
                Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurBreadcrumbOverride").SetValue(1);
                Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurMaxBreadcrumbs").SetValue(1);
                Mod.Globals.WarStatusTracker.DeploymentContracts.Clear();

                if (HomeContendedSystems.Count != 0 && !Mod.Settings.DefensiveFactions.Contains(Mod.Globals.Sim.CurSystem.OwnerValue.Name) && !Mod.Globals.WarStatusTracker.Deployment)
                {
                    var i = 0;
                    var twiddle = 0;
                    var RandomSystem = 0;
                    Mod.Globals.WarStatusTracker.HomeContendedStrings.Clear();
                    while (HomeContendedSystems.Count != 0)
                    {
                        Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurBreadcrumbOverride").SetValue(i + 1);
                        Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurMaxBreadcrumbs").SetValue(i + 1);
                        if (twiddle == 0)
                            twiddle = -1;
                        else if (twiddle == 1)
                            RandomSystem = Mod.Globals.Rng.Next(0, 3 * HomeContendedSystems.Count / 4);
                        else if (twiddle == -1)
                            RandomSystem = Mod.Globals.Rng.Next(HomeContendedSystems.Count / 4, 3 * HomeContendedSystems.Count / 4);

                        var MainBCTarget = HomeContendedSystems[RandomSystem];

                        if (MainBCTarget == Mod.Globals.Sim.CurSystem || (Mod.Globals.Sim.CurSystem.OwnerValue.Name == "Locals" && MainBCTarget.OwnerValue.Name != "Locals") ||
                            !Mod.Globals.IncludedFactions.Contains(MainBCTarget.OwnerValue.Name))
                        {
                            HomeContendedSystems.Remove(MainBCTarget);
                            Mod.Globals.WarStatusTracker.HomeContendedStrings.Remove(MainBCTarget.Name);
                            continue;
                        }

                        TemporaryFlip(MainBCTarget, Mod.Globals.Sim.CurSystem.OwnerValue.Name);
                        if (Mod.Globals.Sim.CurSystem.SystemBreadcrumbs.Count == 0 && MainBCTarget.OwnerValue.Name != Mod.Globals.Sim.CurSystem.OwnerValue.Name)
                        {
                            Mod.Globals.Sim.GeneratePotentialContracts(true, null, MainBCTarget);
                            SystemBonuses(MainBCTarget);

                            var PrioritySystem = Mod.Globals.Sim.CurSystem.SystemBreadcrumbs.Find(x => x.TargetSystem == MainBCTarget.ID);
                            PrioritySystem.Override.contractDisplayStyle = ContractDisplayStyle.BaseCampaignStory;
                            Mod.Globals.WarStatusTracker.DeploymentContracts.Add(PrioritySystem.Override.contractName);
                        }
                        else if (twiddle == -1 || MainBCTarget.OwnerValue.Name == Mod.Globals.Sim.CurSystem.OwnerValue.Name)
                        {
                            Mod.Globals.Sim.GeneratePotentialContracts(false, null, MainBCTarget);
                            SystemBonuses(MainBCTarget);
                        }
                        else if (twiddle == 1)
                        {
                            Mod.Globals.Sim.GeneratePotentialContracts(false, null, MainBCTarget);
                            SystemBonuses(MainBCTarget);

                            var PrioritySystem = Mod.Globals.Sim.CurSystem.SystemBreadcrumbs.Find(x => x.TargetSystem == MainBCTarget.ID);
                            PrioritySystem.Override.contractDisplayStyle = ContractDisplayStyle.BaseCampaignStory;
                            Mod.Globals.WarStatusTracker.DeploymentContracts.Add(PrioritySystem.Override.contractName);
                        }

                        var systemStatus = Mod.Globals.WarStatusTracker.systems.Find(x => x.name == MainBCTarget.Name);
                        RefreshContractsEmployersAndTargets(systemStatus);
                        HomeContendedSystems.Remove(MainBCTarget);
                        Mod.Globals.WarStatusTracker.HomeContendedStrings.Add(MainBCTarget.Name);
                        if (Mod.Globals.Sim.CurSystem.SystemBreadcrumbs.Count == Mod.Settings.InternalHotSpots)
                            break;

                        i = Mod.Globals.Sim.CurSystem.SystemBreadcrumbs.Count;
                        twiddle *= -1;
                    }
                }

                if (ExternalPriorityTargets.Count != 0)
                {
                    var startBC = Mod.Globals.Sim.CurSystem.SystemBreadcrumbs.Count;
                    var j = startBC;
                    foreach (var ExtTarget in ExternalPriorityTargets.Keys)
                    {
                        if (ExternalPriorityTargets[ExtTarget].Count == 0 || Mod.Settings.DefensiveFactions.Contains(ExtTarget) ||
                            !Mod.Globals.IncludedFactions.Contains(ExtTarget)) continue;
                        do
                        {
                            var randTarget = Mod.Globals.Rng.Next(0, ExternalPriorityTargets[ExtTarget].Count);
                            Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurBreadcrumbOverride").SetValue(j + 1);
                            Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurMaxBreadcrumbs").SetValue(j + 1);
                            if (ExternalPriorityTargets[ExtTarget][randTarget] == Mod.Globals.Sim.CurSystem)
                            {
                                ExternalPriorityTargets[ExtTarget].Remove(Mod.Globals.Sim.CurSystem);
                                continue;
                            }

                            TemporaryFlip(ExternalPriorityTargets[ExtTarget][randTarget], ExtTarget);
                            if (Mod.Globals.Sim.CurSystem.SystemBreadcrumbs.Count == 0)
                                Mod.Globals.Sim.GeneratePotentialContracts(true, null, ExternalPriorityTargets[ExtTarget][randTarget]);
                            else
                                Mod.Globals.Sim.GeneratePotentialContracts(false, null, ExternalPriorityTargets[ExtTarget][randTarget]);
                            SystemBonuses(ExternalPriorityTargets[ExtTarget][randTarget]);
                            var systemStatus = Mod.Globals.WarStatusTracker.systems.Find(x =>
                                x.name == ExternalPriorityTargets[ExtTarget][randTarget].Name);
                            RefreshContractsEmployersAndTargets(systemStatus);
                            ExternalPriorityTargets[ExtTarget].RemoveAt(randTarget);
                        } while (Mod.Globals.Sim.CurSystem.SystemBreadcrumbs.Count == j && ExternalPriorityTargets[ExtTarget].Count != 0);

                        j = Mod.Globals.Sim.CurSystem.SystemBreadcrumbs.Count;
                        if (j - startBC == Mod.Settings.ExternalHotSpots)
                            break;
                    }
                }

                isBreadcrumb = false;
                Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurMaxContracts").SetValue(__state);
            }
        }


        [HarmonyPatch(typeof(StarSystem))]
        [HarmonyPatch("InitialContractsFetched", MethodType.Getter)]
        public static class StarSystemInitialContractsFetchedPatch
        {
            private static void Postfix(ref bool __result)
            {
                if (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                    return;

                if (Mod.Globals.WarStatusTracker.StartGameInitialized)
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(SimGameState), "GetDifficultyRangeForContract")]
        public static class SimGameStateGetDifficultyRangeForContractsPatch
        {
            private static void Prefix(SimGameState __instance, ref int __state)
            {
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                if (isBreadcrumb)
                {
                    __state = __instance.Constants.Story.ContractDifficultyVariance;
                    __instance.Constants.Story.ContractDifficultyVariance = 0;
                }
            }

            private static void Postfix(SimGameState __instance, ref int __state)
            {
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                if (isBreadcrumb)
                {
                    __instance.Constants.Story.ContractDifficultyVariance = __state;
                }
            }
        }


        public static void TemporaryFlip(StarSystem starSystem, string faction)
        {
            var FactionDef = UnityGameInstance.BattleTechGame.Simulation.GetFactionDef(faction);
            starSystem.Def.contractEmployerIDs.Clear();
            starSystem.Def.contractTargetIDs.Clear();
            var tracker = Mod.Globals.WarStatusTracker.systems.Find(x => x.name == starSystem.Name);

            if (Mod.Settings.NoOffensiveContracts.Contains(faction))
            {
                if (!Mod.Settings.NoOffensiveContracts.Contains(tracker.OriginalOwner))
                {
                    starSystem.Def.contractEmployerIDs.Add(tracker.OriginalOwner);
                    starSystem.Def.contractTargetIDs.Add(faction);
                }
                else
                {
                    List<string> factionList;
                    if (Mod.Settings.ISMCompatibility)
                        factionList = Mod.Settings.IncludedFactions_ISM;
                    else
                        factionList = Mod.Settings.IncludedFactions;

                    factionList.Shuffle();
                    string factionEmployer = "Davion";
                    foreach (var employer in factionList)
                    {
                        if (Mod.Settings.NoOffensiveContracts.Contains(employer) ||
                            Mod.Settings.DefensiveFactions.Contains(employer) ||
                            Mod.Settings.ImmuneToWar.Contains(employer))
                            continue;
                        factionEmployer = employer;
                        break;
                    }

                    starSystem.Def.contractEmployerIDs.Add(factionEmployer);
                    starSystem.Def.contractTargetIDs.Add(faction);
                }

                return;
            }


            starSystem.Def.contractEmployerIDs.Add(faction);
            if (Mod.Settings.GaW_PoliceSupport && faction == Mod.Globals.WarStatusTracker.ComstarAlly)
                starSystem.Def.contractEmployerIDs.Add(Mod.Settings.GaW_Police);


            foreach (var influence in tracker.influenceTracker.OrderByDescending(x => x.Value))
            {
                if (Mod.Globals.WarStatusTracker.PirateDeployment)
                    break;
                if (influence.Value > 1 && influence.Key != faction)
                {
                    if (!starSystem.Def.contractTargetIDs.Contains(influence.Key))
                        starSystem.Def.contractTargetIDs.Add(influence.Key);
                    if (!FactionDef.Enemies.Contains(influence.Key))
                    {
                        var enemies = new List<string>(FactionDef.Enemies)
                        {
                            influence.Key
                        };
                        Traverse.Create(FactionDef).Property("Enemies").SetValue(enemies.ToArray());
                    }

                    if (FactionDef.Allies.Contains(influence.Key))
                    {
                        var allies = new List<string>(FactionDef.Allies);
                        allies.Remove(influence.Key);
                        Traverse.Create(FactionDef).Property("Allies").SetValue(allies.ToArray());
                    }
                }

                if (starSystem.Def.contractTargetIDs.Count == 2)
                    break;
            }

            if (starSystem.Def.contractTargetIDs.Contains(Mod.Globals.WarStatusTracker.ComstarAlly))
                starSystem.Def.contractTargetIDs.Add(Mod.Globals.WarStatusTracker.ComstarAlly);

            if (starSystem.Def.contractTargetIDs.Count == 0)
                starSystem.Def.contractTargetIDs.Add("AuriganPirates");

            if (!starSystem.Def.contractTargetIDs.Contains("Locals"))
                starSystem.Def.contractTargetIDs.Add("Locals");
        }

        //Deployments area.
        [HarmonyPatch(typeof(SimGameState), "PrepareBreadcrumb")]
        public static class SimGameStatePrepareBreadcrumbPatch
        {
            private static void Postfix(SimGameState __instance, Contract contract)
            {
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                if (!__instance.CurSystem.Def.Description.Id.StartsWith(contract.TargetSystem))
                {
                    LogDebug("Preparing the Breadcrumbs");
                    var starSystem = Mod.Globals.GaWSystems.Find(x => x.Def.Description.Id.StartsWith(contract.TargetSystem));
                    Mod.Globals.WarStatusTracker.HotBox.Add(starSystem.Name);
                    Mod.Globals.WarStatusTracker.HotBoxTravelling = true;

                    if (contract.Override.contractDisplayStyle == ContractDisplayStyle.BaseCampaignStory)
                    {
                        Mod.Globals.WarStatusTracker.Deployment = true;
                        Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease = 1.0;
                        Mod.Globals.WarStatusTracker.DeploymentEmployer = contract.Override.employerTeam.FactionValue.Name;
                    }
                    else
                    {
                        Mod.Globals.WarStatusTracker.Deployment = false;
                        Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease = 1.0;
                        Mod.Globals.WarStatusTracker.PirateDeployment = false;
                    }

                    TemporaryFlip(starSystem, contract.Override.employerTeam.FactionValue.Name);
                    if (Mod.Globals.WarStatusTracker.HotBox.Contains(__instance.CurSystem.Name))
                    {
                        Mod.Globals.WarStatusTracker.HotBox.Remove(__instance.CurSystem.Name);
                        Mod.Globals.WarStatusTracker.EscalationDays = 0;
                        Mod.Globals.WarStatusTracker.Escalation = false;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(SGNavigationScreen), "OnTravelCourseAccepted")]
        public static class SGNavigationScreenOnTravelCourseAcceptedPatch
        {
            private static bool Prefix(SGNavigationScreen __instance)
            {
                try
                {
                    if (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                        return true;

                    if (Mod.Globals.WarStatusTracker.Deployment)
                    {
                        var uiManager = (UIManager) AccessTools.Field(typeof(SGNavigationScreen), "uiManager").GetValue(__instance);

                        void Cleanup()
                        {
                            uiManager.ResetFader(UIManagerRootType.PopupRoot);
                            Mod.Globals.Sim.Starmap.Screen.AllowInput(true);
                        }

                        var primaryButtonText = "Break Deployment";
                        var message = "WARNING: This action will break your current Deployment. Your reputation with the employer and the MRB will be negatively impacted.";
                        PauseNotification.Show("Navigation Change", message, Mod.Globals.Sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), string.Empty, true, delegate
                        {
                            Cleanup();
                            Mod.Globals.WarStatusTracker.Deployment = false;
                            Mod.Globals.WarStatusTracker.PirateDeployment = false;
                            if (Mod.Globals.Sim.GetFactionDef(Mod.Globals.WarStatusTracker.DeploymentEmployer).FactionValue.DoesGainReputation)
                            {
                                var employerRepBadFaithMod = Mod.Globals.Sim.Constants.Story.EmployerRepBadFaithMod;
                                int num;

                                if (Mod.Settings.ChangeDifficulty)
                                    num = Mathf.RoundToInt(Mod.Globals.Sim.CurSystem.Def.GetDifficulty(SimGameState.SimGameType.CAREER) * employerRepBadFaithMod);
                                else
                                    num = Mathf.RoundToInt((Mod.Globals.Sim.CurSystem.Def.DefaultDifficulty + Mod.Globals.Sim.GlobalDifficulty) * employerRepBadFaithMod);

                                if (num != 0)
                                {
                                    Mod.Globals.Sim.SetReputation(Mod.Globals.Sim.GetFactionDef(Mod.Globals.WarStatusTracker.DeploymentEmployer).FactionValue, num);
                                    Mod.Globals.Sim.SetReputation(Mod.Globals.Sim.GetFactionValueFromString("faction_MercenaryReviewBoard"), num);
                                }
                            }

                            if (Mod.Globals.WarStatusTracker.HotBox.Count == 2)
                            {
                                Mod.Globals.WarStatusTracker.HotBox.RemoveAt(0);
                            }
                            else if (Mod.Globals.WarStatusTracker.HotBox.Count != 0)
                            {
                                Mod.Globals.WarStatusTracker.HotBox.Clear();
                            }

                            Mod.Globals.WarStatusTracker.Deployment = false;
                            Mod.Globals.WarStatusTracker.PirateDeployment = false;
                            Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease = 1.0;
                            Mod.Globals.WarStatusTracker.Escalation = false;
                            Mod.Globals.WarStatusTracker.EscalationDays = 0;
                            var systemStatus = Mod.Globals.WarStatusTracker.systems.Find(x => x.name == Mod.Globals.Sim.CurSystem.Name);
                            RefreshContractsEmployersAndTargets(systemStatus);
                            if (Mod.Globals.WarStatusTracker.HotBox.Count == 0)
                                Mod.Globals.WarStatusTracker.HotBoxTravelling = false;

                            if (Mod.Globals.WarStatusTracker.EscalationOrder != null)
                            {
                                Mod.Globals.WarStatusTracker.EscalationOrder.SetCost(0);
                                var ActiveItems = Mod.Globals.TaskTimelineWidget.ActiveItems;
                                if (ActiveItems.TryGetValue(Mod.Globals.WarStatusTracker.EscalationOrder, out var taskManagementElement))
                                {
                                    taskManagementElement.UpdateItem(0);
                                }
                            }

                            Mod.Globals.Sim.Starmap.SetActivePath();
                            Mod.Globals.Sim.SetSimRoomState(DropshipLocation.SHIP);
                        }, primaryButtonText, Cleanup, "Cancel");
                        Mod.Globals.Sim.Starmap.Screen.AllowInput(false);
                        uiManager.SetFaderColor(uiManager.UILookAndColorConstants.PopupBackfill, UIManagerFader.FadePosition.FadeInBack, UIManagerRootType.PopupRoot);
                        return false;
                    }

                    return true;
                }
                catch (Exception e)
                {
                    Error(e);
                    return true;
                }
            }

            private static void Postfix()
            {
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                var system = UnityGameInstance.BattleTechGame.Simulation.CurSystem;
                if (Mod.Globals.WarStatusTracker.HotBox.Contains(system.Name))
                {
                    Mod.Globals.WarStatusTracker.HotBox.Remove(system.Name);
                }

                Mod.Globals.WarStatusTracker.Escalation = false;
                Mod.Globals.WarStatusTracker.EscalationDays = 0;
                var systemStatus = Mod.Globals.WarStatusTracker.systems.Find(x => x.name == system.Name);
                RefreshContractsEmployersAndTargets(systemStatus);
                if (Mod.Globals.WarStatusTracker.HotBox.Count == 0)
                    Mod.Globals.WarStatusTracker.HotBoxTravelling = false;
            }
        }

        [HarmonyPatch(typeof(SGNavigationScreen), "OnFlashpointAccepted")]
        public static class SGNavigationScreenOnFlashpointAcceptedPatch
        {
            private static bool Prefix(SGNavigationScreen __instance)
            {
                try
                {
                    if (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                        return true;

                    LogDebug("OnFlashpointAccepted");
                    if (Mod.Globals.WarStatusTracker.Deployment)
                    {
                        LogDebug("Deployment.");
                        var uiManager = __instance.uiManager;

                        void Cleanup()
                        {
                            uiManager.ResetFader(UIManagerRootType.PopupRoot);
                            Mod.Globals.Sim.Starmap.Screen.AllowInput(true);
                        }

                        var primaryButtonText = "Break Deployment";
                        var message = "WARNING: This action will break your current Deployment. Your reputation with the employer and the MRB will be negatively impacted.";
                        PauseNotification.Show("Navigation Change", message, Mod.Globals.Sim.GetCrewPortrait(SimGameCrew.Crew_Sumire), string.Empty, true, delegate
                        {
                            Cleanup();
                            Mod.Globals.WarStatusTracker.Deployment = false;
                            Mod.Globals.WarStatusTracker.PirateDeployment = false;
                            if (Mod.Globals.Sim.GetFactionDef(Mod.Globals.WarStatusTracker.DeploymentEmployer).FactionValue.DoesGainReputation)
                            {
                                var employerRepBadFaithMod = Mod.Globals.Sim.Constants.Story.EmployerRepBadFaithMod;
                                int num;
                                if (Mod.Settings.ChangeDifficulty)
                                    num = Mathf.RoundToInt(Mod.Globals.Sim.CurSystem.Def.GetDifficulty(SimGameState.SimGameType.CAREER) * employerRepBadFaithMod);
                                else
                                    num = Mathf.RoundToInt((Mod.Globals.Sim.CurSystem.Def.DefaultDifficulty + Mod.Globals.Sim.GlobalDifficulty) * employerRepBadFaithMod);

                                if (num != 0)
                                {
                                    Mod.Globals.Sim.SetReputation(Mod.Globals.Sim.GetFactionDef(Mod.Globals.WarStatusTracker.DeploymentEmployer).FactionValue, num);
                                    Mod.Globals.Sim.SetReputation(Mod.Globals.Sim.GetFactionValueFromString("faction_MercenaryReviewBoard"), num);
                                }
                            }

                            if (Mod.Globals.WarStatusTracker.HotBox.Count == 2)
                            {
                                Mod.Globals.WarStatusTracker.HotBox.RemoveAt(0);
                            }
                            else if (Mod.Globals.WarStatusTracker.HotBox.Count != 0)
                            {
                                Mod.Globals.WarStatusTracker.HotBox.Clear();
                            }

                            Mod.Globals.WarStatusTracker.Deployment = false;
                            Mod.Globals.WarStatusTracker.PirateDeployment = false;
                            Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease = 1.0;
                            Mod.Globals.WarStatusTracker.Escalation = false;
                            Mod.Globals.WarStatusTracker.EscalationDays = 0;
                            var systemStatus = Mod.Globals.WarStatusTracker.systems.Find(x => x.name == Mod.Globals.Sim.CurSystem.Name);
                            RefreshContractsEmployersAndTargets(systemStatus);
                            if (Mod.Globals.WarStatusTracker.HotBox.Count == 0)
                                Mod.Globals.WarStatusTracker.HotBoxTravelling = false;

                            if (Mod.Globals.WarStatusTracker.EscalationOrder != null)
                            {
                                Mod.Globals.WarStatusTracker.EscalationOrder.SetCost(0);
                                var ActiveItems = Mod.Globals.TaskTimelineWidget.ActiveItems;
                                if (ActiveItems.TryGetValue(Mod.Globals.WarStatusTracker.EscalationOrder, out var taskManagementElement))
                                {
                                    taskManagementElement.UpdateItem(0);
                                }
                            }

                            Mod.Globals.Sim.Starmap.SetActivePath();
                            Mod.Globals.Sim.SetSimRoomState(DropshipLocation.SHIP);
                        }, primaryButtonText, Cleanup, "Cancel");
                        Mod.Globals.Sim.Starmap.Screen.AllowInput(false);
                        uiManager.SetFaderColor(uiManager.UILookAndColorConstants.PopupBackfill, UIManagerFader.FadePosition.FadeInBack, UIManagerRootType.PopupRoot);
                        return false;
                    }

                    return true;
                }
                catch (Exception e)
                {
                    Error(e);
                    return true;
                }
            }

            private static void Postfix()
            {
                if (Mod.Globals.WarStatusTracker == null || Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                    return;

                var system = Mod.Globals.Sim.CurSystem;
                if (Mod.Globals.WarStatusTracker.HotBox.Contains(system.Name))
                {
                    Mod.Globals.WarStatusTracker.HotBox.Remove(system.Name);
                }

                Mod.Globals.WarStatusTracker.Escalation = false;
                Mod.Globals.WarStatusTracker.EscalationDays = 0;
                var systemStatus = Mod.Globals.WarStatusTracker.systems.Find(x => x.name == system.Name);
                RefreshContractsEmployersAndTargets(systemStatus);
                if (Mod.Globals.WarStatusTracker.HotBox.Count == 0)
                    Mod.Globals.WarStatusTracker.HotBoxTravelling = false;
            }
        }

        [HarmonyPatch(typeof(SGTravelManager), "WarmingEngines_CanEnter")]
        public static class CompletedJumpPatch
        {
            private static void Postfix()
            {
                if (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                    return;

                Mod.Globals.HoldContracts = false;
            }
        }

        [HarmonyPatch(typeof(SGTravelManager), "DisplayEnteredOrbitPopup")]
        public static class EnteredOrbitPatch
        {
            private static void Postfix()
            {
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                var HasFlashpoint = false;
                Mod.Globals.WarStatusTracker.JustArrived = true;

                if (!Mod.Globals.WarStatusTracker.Deployment)
                {
                    LogDebug($"Not a deployment.  Escalation days: {Mod.Settings.EscalationDays}");
                    Mod.Globals.WarStatusTracker.EscalationDays = Mod.Settings.EscalationDays;
                }
                else
                {
                    LogDebug($"Deployment.  Escalation days: {Mod.Settings.EscalationDays}");
                    var rand = new Random();
                    Mod.Globals.WarStatusTracker.EscalationDays = rand.Next(Mod.Settings.DeploymentMinDays, Mod.Settings.DeploymentMaxDays + 1);
                    if (Mod.Globals.WarStatusTracker.EscalationDays < Mod.Settings.DeploymentRerollBound * Mod.Globals.WarStatusTracker.EscalationDays ||
                        Mod.Globals.WarStatusTracker.EscalationDays > (1 - Mod.Settings.DeploymentRerollBound) * Mod.Globals.WarStatusTracker.EscalationDays)
                    {
                        Mod.Globals.WarStatusTracker.EscalationDays = rand.Next(Mod.Settings.DeploymentMinDays, Mod.Settings.DeploymentMaxDays + 1);
                        LogDebug($"New escalation days set to {Mod.Globals.WarStatusTracker.EscalationDays}");
                    }
                }

                foreach (var contract in Mod.Globals.Sim.CurSystem.SystemContracts)
                {
                    if (contract.IsFlashpointContract || contract.IsFlashpointCampaignContract)
                        HasFlashpoint = true;
                }

                if (!Mod.Globals.WarStatusTracker.HotBoxTravelling &&
                    !Mod.Globals.WarStatusTracker.HotBox.Contains(Mod.Globals.Sim.CurSystem.Name) &&
                    !HasFlashpoint && !Mod.Globals.HoldContracts)
                {
                    LogDebug("Regenerating contracts because entering system.");
                    var cmdCenter = Mod.Globals.Sim.RoomManager.CmdCenterRoom;
                    Mod.Globals.Sim.CurSystem.GenerateInitialContracts(() => cmdCenter.OnContractsFetched());
                }

                Mod.Globals.HoldContracts = false;
            }
        }

        [HarmonyPatch(typeof(AAR_SalvageScreen), "OnCompleted")]
        public static class AAR_SalvageScreenOnCompletedPatch
        {
            private static void Prefix()
            {
                try
                {
                    if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                        return;

                    LogDebug("AAR Salvage Screen Completed");
                    Mod.Globals.WarStatusTracker.JustArrived = false;
                    Mod.Globals.WarStatusTracker.HotBoxTravelling = false;
                }
                catch (Exception e)
                {
                    Error(e);
                }
            }
        }

        [HarmonyPatch(typeof(TaskTimelineWidget), "RegenerateEntries")]
        public static class TaskTimelineWidgetRegenerateEntriesPatch
        {
            private static void Postfix(TaskTimelineWidget __instance)
            {
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                if (Mod.Globals.WarStatusTracker != null && Mod.Globals.WarStatusTracker.Escalation)
                {
                    if (!Mod.Globals.WarStatusTracker.Deployment)
                    {
                        Mod.Globals.WarStatusTracker.EscalationOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationGeneric,
                            "Escalation Days Remaining", "Escalation Days Remaining");
                        Mod.Globals.WarStatusTracker.EscalationOrder.SetCost(Mod.Globals.WarStatusTracker.EscalationDays);
                        __instance.AddEntry(Mod.Globals.WarStatusTracker.EscalationOrder, false);
                        __instance.RefreshEntries();
                    }
                    else
                    {
                        Mod.Globals.WarStatusTracker.EscalationOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationGeneric,
                            "Escalation Days Remaining", "Forced Deployment Mission");
                        Mod.Globals.WarStatusTracker.EscalationOrder.SetCost(Mod.Globals.WarStatusTracker.EscalationDays);
                        __instance.AddEntry(Mod.Globals.WarStatusTracker.EscalationOrder, false);
                        __instance.RefreshEntries();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TaskTimelineWidget), "RemoveEntry")]
        public static class TaskTimelineWidgetRemoveEntryPatch
        {
            private static bool Prefix(WorkOrderEntry entry)
            {
                if (Mod.Globals.WarStatusTracker == null || Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                    return true;

                if (!Mod.Globals.WarStatusTracker.JustArrived && entry.ID.Equals("Escalation Days Remaining") && entry.GetRemainingCost() != 0)
                {
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(SimGameState), "OnBreadcrumbArrival")]
        public static class SimGameStateOnBreadcrumbArrivalPatch
        {
            private static void Postfix()
            {
                LogDebug("OnBreadcrumbArrival");
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                Mod.Globals.WarStatusTracker.Escalation = true;
                Mod.Globals.WarStatusTracker.EscalationOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationGeneric, "Escalation Days Remaining", "Escalation Days Remaining");
                if (!Mod.Globals.WarStatusTracker.Deployment)
                {
                    LogDebug("Not deployment.");
                    Mod.Globals.WarStatusTracker.EscalationDays = Mod.Settings.EscalationDays;
                }
                else
                {
                    LogDebug("Is deployment.");
                    Mod.Globals.Sim.CurSystem.activeSystemContracts.Clear();
                }

                Mod.Globals.WarStatusTracker.EscalationOrder.SetCost(Mod.Globals.WarStatusTracker.EscalationDays);
                Mod.Globals.Sim.RoomManager.AddWorkQueueEntry(Mod.Globals.WarStatusTracker.EscalationOrder);
                Mod.Globals.Sim.RoomManager.SortTimeline();
                Mod.Globals.Sim.RoomManager.RefreshTimeline(false);
            }
        }

        //Need to clear out the old stuff if a contract is cancelled to prevent crashing.
        [HarmonyPatch(typeof(SimGameState), "OnBreadcrumbCancelledByUser")]
        public static class SimGameStateBreadCrumbCancelledPatch
        {
            //static bool Prefix(SimGameState __instance)
            //{
            //    try
            //    {
            //        //        if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
            //            return true;

            //        SimGameState Sim = (SimGameState)AccessTools.Property(typeof(SGContractsWidget), "Sim").GetValue(__instance, null);

            //        if (__instance.SelectedContract.Override.contractDisplayStyle == ContractDisplayStyle.BaseCampaignNormal)
            //        {
            //            string message = "Commander, this contract will bring us right to the front lines. If we accept it, we will be forced to take missions when our employer needs us to simultaneously attack in support of their war effort. We will be commited to this Deployment until the system is taken or properly defended and will lose significant reputation if we end up backing out before the job is done. But, oh man, they will certainly reward us well if their operation is ultimately successful! This Deployment may require missions to be done without time between them for repairs or to properly rest our pilots. I stronfgety encourage you to only accept this arrangement if you think we're up to it.";
            //            PauseNotification.Show("Deployment", message,
            //                Mod.Globals.Sim.GetCrewPortrait(SimGameCrew.Crew_Darius), string.Empty, true, delegate {
            //                    __instance.NegotiateContract(__instance.SelectedContract, null);
            //                }, "Do it anyways", null, "Cancel");
            //            return false;
            //        }
            //        else
            //        {
            //            return true;
            //        }
            //    }
            //    catch (Exception e)
            //    {
            //        Logger.Error(e);
            //        return true;
            //    }
            //}


            private static void Postfix()
            {
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                LogDebug("OnBreadcrumbCancelledByUser");
                var system = Mod.Globals.Sim.CurSystem;
                if (Mod.Globals.WarStatusTracker.HotBox.Count == 2)
                {
                    Mod.Globals.WarStatusTracker.HotBox.RemoveAt(0);
                }
                else
                {
                    Mod.Globals.WarStatusTracker.HotBox.Clear();
                }

                Mod.Globals.WarStatusTracker.Deployment = false;
                Mod.Globals.WarStatusTracker.PirateDeployment = false;
                Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease = 1.0;
                Mod.Globals.WarStatusTracker.Escalation = false;
                Mod.Globals.WarStatusTracker.EscalationDays = 0;
                var systemStatus = Mod.Globals.WarStatusTracker.systems.Find(x => x.name == system.Name);
                RefreshContractsEmployersAndTargets(systemStatus);
                if (Mod.Globals.WarStatusTracker.HotBox.Count == 0)
                    Mod.Globals.WarStatusTracker.HotBoxTravelling = false;
            }
        }


        [HarmonyPatch(typeof(Contract), "GenerateSalvage")]
        public static class ContractGenerateSalvagePatch
        {
            private static void Postfix(Contract __instance)
            {
                //Log("****Generate Salvage****");
                //Log("Sim Null? " + (Sim == null).ToString());
                //Log("CurSystem Null? " + (Mod.Globals.Sim.CurSystem == null).ToString());
                //Log("CurSystem: " + Mod.Globals.Sim.CurSystem.Name);
                //Log("WarStatus Null? " + (Mod.Globals.WarStatusTracker == null).ToString());
                //Log("WarStatus System Null? " + (null ==Mod.Globals.WarStatusTracker.systems.Find(x => x.name == Mod.Globals.Sim.CurSystem.Name)).ToString());
                //foreach (SystemStatus systemstatus in Mod.Globals.WarStatusTracker.systems)
                //{
                //    Log(systemstatus.name);
                //    Log(systemstatus.starSystem.Name);
                //}

                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                var system = Mod.Globals.WarStatusTracker.systems.Find(x => x.starSystem == Mod.Globals.Sim.CurSystem);
                if (Mod.Globals.WarStatusTracker.HotBox == null)
                    Mod.Globals.WarStatusTracker.HotBox = new List<string>();

                if (system.BonusSalvage && Mod.Globals.WarStatusTracker.HotBox.Contains(Mod.Globals.Sim.CurSystem.Name))
                {
                    var NewSalvageCount = __instance.FinalSalvageCount + 1;
                    Traverse.Create(__instance).Property("FinalSalvageCount").SetValue(NewSalvageCount);

                    if (__instance.FinalPrioritySalvageCount < 7)
                    {
                        var NewPrioritySalvage = __instance.FinalPrioritySalvageCount + 1;
                        Traverse.Create(__instance).Property("FinalPrioritySalvageCount").SetValue(NewPrioritySalvage);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(AAR_ContractObjectivesWidget), "FillInObjectives")]
        public static class AAR_ContractObjectivesWidget_FillInObjectives
        {
            private static void Postfix(AAR_ContractObjectivesWidget __instance)
            {
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                var system = Mod.Globals.WarStatusTracker.systems.Find(x => x.starSystem == Mod.Globals.Sim.CurSystem);

                if (system.BonusCBills && Mod.Globals.WarStatusTracker.HotBox.Contains(Mod.Globals.Sim.CurSystem.Name))
                {
                    var missionObjectiveResultString = $"BONUS FROM ESCALATION: ¢{String.Format("{0:n0}", BonusMoney)}";
                    if (Mod.Globals.WarStatusTracker.Deployment)
                        missionObjectiveResultString = $"BONUS FROM DEPLOYMENT: ¢{String.Format("{0:n0}", BonusMoney)}";
                    var missionObjectiveResult = new MissionObjectiveResult(missionObjectiveResultString, "7facf07a-626d-4a3b-a1ec-b29a35ff1ac0", false, true, ObjectiveStatus.Succeeded, false);
                    Traverse.Create(__instance).Method("AddObjective", missionObjectiveResult).GetValue();
                }
            }
        }

        [HarmonyPatch(typeof(AAR_UnitStatusWidget), "FillInPilotData")]
        public static class AARUnitStatusWidgetPatch
        {
            private static void Prefix(ref int xpEarned, UnitResult ___UnitData)
            {
                if (Mod.Globals.WarStatusTracker == null || Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                    return;

                var system = Mod.Globals.WarStatusTracker.systems.Find(x => x.name == Mod.Globals.WarStatusTracker.CurSystem);
                if (system.BonusXP && Mod.Globals.WarStatusTracker.HotBox.Contains(system.name))
                {
                    xpEarned = xpEarned + (int) (xpEarned * Mod.Settings.BonusXPFactor);
                    var unspentXP = ___UnitData.pilot.UnspentXP;
                    var XPCorrection = (int) (xpEarned * Mod.Settings.BonusXPFactor);
                    ___UnitData.pilot.StatCollection.Set("ExperienceUnspent", unspentXP + XPCorrection);
                }
            }
        }

        public static void SystemBonuses(StarSystem starSystem)
        {
            var systemStatus = Mod.Globals.WarStatusTracker.systems.Find(x => x.starSystem == starSystem);
            int systemDifficulty;
            if (Mod.Settings.ChangeDifficulty)
                systemDifficulty = systemStatus.DifficultyRating;
            else
                systemDifficulty = systemStatus.DifficultyRating + (int) Mod.Globals.Sim.GlobalDifficulty;

            if (!Mod.Globals.WarStatusTracker.HotBox.Contains(starSystem.Name))
            {
                systemStatus.BonusCBills = false;
                systemStatus.BonusSalvage = false;
                systemStatus.BonusXP = false;

                if (systemDifficulty <= 4)
                {
                    var bonus = Mod.Globals.Rng.Next(0, 3);
                    if (bonus == 0)
                        systemStatus.BonusCBills = true;
                    if (bonus == 1)
                        systemStatus.BonusXP = true;
                    if (bonus == 2)
                        systemStatus.BonusSalvage = true;
                }

                if (systemDifficulty <= 8 && systemDifficulty > 4)
                {
                    systemStatus.BonusCBills = true;
                    systemStatus.BonusSalvage = true;
                    systemStatus.BonusXP = true;
                    var bonus = Mod.Globals.Rng.Next(0, 3);
                    if (bonus == 0)
                        systemStatus.BonusCBills = false;
                    if (bonus == 1)
                        systemStatus.BonusXP = false;
                    if (bonus == 2)
                        systemStatus.BonusSalvage = false;
                }

                if (systemDifficulty > 8)
                {
                    systemStatus.BonusCBills = true;
                    systemStatus.BonusSalvage = true;
                    systemStatus.BonusXP = true;
                }
            }
        }

        public static void CompleteEscalation()
        {
            var systemStatus = Mod.Globals.WarStatusTracker.systems.Find(x => x.starSystem == Mod.Globals.Sim.CurSystem);
            systemStatus.BonusCBills = false;
            systemStatus.BonusSalvage = false;
            systemStatus.BonusXP = false;
            Mod.Globals.WarStatusTracker.Deployment = false;
            Mod.Globals.WarStatusTracker.PirateDeployment = false;
            Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease = 1.0;
            Mod.Globals.WarStatusTracker.HotBox.Remove(systemStatus.name);
            RefreshContractsEmployersAndTargets(systemStatus);
            var hasFlashpoint = false;
            foreach (var contract in Mod.Globals.Sim.CurSystem.SystemContracts)
            {
                if (contract.IsFlashpointContract || contract.IsFlashpointCampaignContract)
                    hasFlashpoint = true;
            }

            if (!hasFlashpoint)
            {
                LogDebug("Refresh contracts because CompleteEscalation.");
                var cmdCenter = Mod.Globals.Sim.RoomManager.CmdCenterRoom;
                Mod.Globals.Sim.CurSystem.GenerateInitialContracts(() => Traverse.Create(cmdCenter).Method("OnContractsFetched"));
            }
        }

        [HarmonyPatch(typeof(SimGameState), "ContractUserMeetsReputation")]
        public static class SimGameStateContractUserMeetsReputationPatch
        {
            private static void Postfix(ref bool __result)
            {
                if (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                    return;

                if (Mod.Globals.WarStatusTracker.Deployment)
                    __result = true;
            }
        }

        public static int ProcessReputation(float FactionRep)
        {
            var simStory = Mod.Globals.Sim.Constants.Story;
            var simCareer = Mod.Globals.Sim.Constants.CareerMode;
            int maxContracts;

            if (FactionRep <= simStory.LoathedReputation)
                maxContracts = Convert.ToInt32(simCareer.LoathedMaxContractDifficulty);
            else if (FactionRep <= simStory.HatedReputation)
                maxContracts = Convert.ToInt32(simCareer.HatedMaxContractDifficulty);
            else if (FactionRep <= simStory.DislikedReputation)
                maxContracts = Convert.ToInt32(simCareer.DislikedMaxContractDifficulty);
            else if (FactionRep <= simStory.LikedReputation)
                maxContracts = Convert.ToInt32(simCareer.IndifferentMaxContractDifficulty);
            else if (FactionRep <= simStory.FriendlyReputation)
                maxContracts = Convert.ToInt32(simCareer.LikedMaxContractDifficulty);
            else if (FactionRep <= simStory.HonoredReputation)
                maxContracts = Convert.ToInt32(simCareer.FriendlyMaxContractDifficulty);
            else
                maxContracts = Convert.ToInt32(simCareer.HonoredMaxContractDifficulty);

            if (maxContracts > 10)
                maxContracts = 10;
            if (maxContracts < 1)
                maxContracts = 1;

            return maxContracts;
        }

        [HarmonyPatch(typeof(SGRoomController_CmdCenter), "StartContractScreen")]
        public static class SGRoomControllerCmdCenterStartContractScreenPatch
        {
            private static void Prefix()
            {
                if (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                    return;

                if (!Mod.Globals.HoldContracts && !Mod.Globals.WarStatusTracker.StartGameInitialized)
                {
                    ProcessHotSpots();
                    LogDebug($"Refreshing contracts at StartContractScreen because !StartGameInitialized ({Mod.Globals.Sim.CurSystem.Name})");
                    var cmdCenter = Mod.Globals.Sim.RoomManager.CmdCenterRoom;
                    Mod.Globals.Sim.CurSystem.GenerateInitialContracts(() => cmdCenter.OnContractsFetched());
                    Mod.Globals.WarStatusTracker.StartGameInitialized = true;
                    //LogDebug("Contracts generated:");
                    //foreach (var contract in Mod.Globals.Sim.GetAllCurrentlySelectableContracts())
                    //{
                    //    LogDebug($"{contract.Name,-25} ({contract.Override.employerTeam.FactionValue.Name} vs {contract.Override.targetTeam.FactionValue.Name}).  Difficulties: C:{contract.Difficulty} CO:{contract.Override.difficulty} CUI:{contract.Override.difficultyUIModifier} UI:{contract.Override.GetUIDifficulty()}");
                    //    LogDebug($"Flashpoint? {contract.IsFlashpointContract}.  Campaign Flashpoint? {contract.IsFlashpointCampaignContract}.  Priority? {contract.IsPriorityContract}.  Travel? {contract.Override.travelSeed != 0}");
                    //}
                }

                if (Mod.Globals.WarStatusTracker.Deployment && Mod.Globals.Sim.ActiveTravelContract != null)
                {
                    Mod.Globals.Sim.CurSystem.activeSystemContracts.Clear();
                    Mod.Globals.Sim.CurSystem.activeSystemBreadcrumbs.RemoveAll(x => x != Mod.Globals.Sim.ActiveTravelContract);
                }

                LogDebug("HoldContracts");
                Mod.Globals.HoldContracts = true;
            }
        }

        [HarmonyPatch(typeof(SGContractsWidget), "OnNegotiateClicked")]
        public static class SGContractsWidgetOnNegotiateClickedPatch
        {
            private static bool Prefix(SGContractsWidget __instance)
            {
                try
                {
                    if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                        return true;

                    if (__instance.SelectedContract.Override.contractDisplayStyle == ContractDisplayStyle.BaseCampaignStory)
                    {
                        var message = "Commander, this contract will bring us right to the front lines. If we accept it, we will be forced to take missions when our employer needs us to simultaneously attack in support of their war effort. We will be committed to this Deployment until the system is taken or properly defended and will lose significant reputation if we end up backing out before the job is done. But, oh man, they will certainly reward us well if their operation is ultimately successful! This Deployment may require missions to be done without time between them for repairs or to properly rest our pilots. I strongly encourage you to only accept this arrangement if you think we're up to it.";
                        PauseNotification.Show("Deployment", message,
                            Mod.Globals.Sim.GetCrewPortrait(SimGameCrew.Crew_Darius), string.Empty, true, delegate { __instance.NegotiateContract(__instance.SelectedContract); }, "Do it anyway", null, "Cancel");
                        return false;
                    }

                    return true;
                }
                catch (Exception e)
                {
                    Error(e);
                    return true;
                }
            }
        }

        [HarmonyPatch(typeof(SGTimePlayPause), "ToggleTime")]
        public static class SGTimePlayPauseToggleTimePatch
        {
            private static bool Prefix()
            {
                try
                {
                    if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                        return true;

                    if (!Mod.Settings.ResetMap && Mod.Globals.WarStatusTracker.Deployment && !Mod.Globals.WarStatusTracker.HotBoxTravelling && Mod.Globals.WarStatusTracker.EscalationDays <= 0)
                    {
                        return false;
                    }

                    return true;
                }
                catch (Exception e)
                {
                    Error(e);
                    return true;
                }
            }
        }


        [HarmonyPatch(typeof(TaskTimelineWidget), "OnTaskDetailsClicked")]
        public static class TaskTimelineWidgetOnTaskDetailsClickedPatch
        {
            public static bool Prefix(TaskManagementElement element)
            {
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return true;

                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    AdvanceToTask.StartAdvancing(element.Entry);
                    return false;
                }

                return true;
            }
        }

        //Make contracts always available for escalations
        [HarmonyPatch(typeof(StarSystem), "CompletedContract")]
        public static class StarSystemCompletedContractPatch
        {
            public static void Prefix(StarSystem __instance, ref float __state)
            {
                LogDebug("CompletedContract");
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                __state = Mod.Globals.Sim.Constants.Story.ContractSuccessReduction;
                var HasFlashpoint = false;
                foreach (var contract in Mod.Globals.Sim.CurSystem.SystemContracts)
                {
                    if (contract.IsFlashpointContract || contract.IsFlashpointCampaignContract)
                        HasFlashpoint = true;
                }

                if (Mod.Globals.WarStatusTracker.HotBox.Contains(Mod.Globals.Sim.CurSystem.Name) && Mod.Globals.WarStatusTracker.Deployment)
                {
                    Mod.Globals.Sim.Constants.Story.ContractSuccessReduction = 100;
                    Mod.Globals.WarStatusTracker.DeploymentInfluenceIncrease *= Mod.Settings.DeploymentEscalationFactor;
                    if (!HasFlashpoint)
                    {
                        Mod.Globals.Sim.CurSystem.activeSystemContracts.Clear();
                        Mod.Globals.Sim.CurSystem.activeSystemBreadcrumbs.Clear();
                    }

                    if (Mod.Globals.WarStatusTracker.EscalationOrder != null)
                    {
                        Mod.Globals.WarStatusTracker.EscalationOrder.SetCost(0);
                        var ActiveItems = Mod.Globals.TaskTimelineWidget.ActiveItems;
                        if (ActiveItems.TryGetValue(Mod.Globals.WarStatusTracker.EscalationOrder, out var taskManagementElement))
                        {
                            taskManagementElement.UpdateItem(0);
                        }
                    }

                    Mod.Globals.WarStatusTracker.Escalation = true;
                    var rand = new Random();
                    Mod.Globals.WarStatusTracker.EscalationDays = rand.Next(Mod.Settings.DeploymentMinDays, Mod.Settings.DeploymentMaxDays + 1);
                    if (Mod.Globals.WarStatusTracker.EscalationDays < Mod.Settings.DeploymentRerollBound * Mod.Globals.WarStatusTracker.EscalationDays ||
                        Mod.Globals.WarStatusTracker.EscalationDays > (1 - Mod.Settings.DeploymentRerollBound) * Mod.Globals.WarStatusTracker.EscalationDays)
                        Mod.Globals.WarStatusTracker.EscalationDays = rand.Next(Mod.Settings.DeploymentMinDays, Mod.Settings.DeploymentMaxDays + 1);

                    Mod.Globals.WarStatusTracker.EscalationOrder = new WorkOrderEntry_Notification(WorkOrderType.NotificationGeneric, "Escalation Days Remaining", "Forced Deployment Mission");
                    Mod.Globals.WarStatusTracker.EscalationOrder.SetCost(Mod.Globals.WarStatusTracker.EscalationDays);
                    Mod.Globals.Sim.RoomManager.AddWorkQueueEntry(Mod.Globals.WarStatusTracker.EscalationOrder);
                    Mod.Globals.Sim.RoomManager.SortTimeline();
                    Mod.Globals.Sim.RoomManager.RefreshTimeline(false);
                }
            }

            public static void Postfix(StarSystem __instance, ref float __state)
            {
                if (Mod.Globals.WarStatusTracker == null || (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete")))
                    return;

                Mod.Globals.Sim.Constants.Story.ContractSuccessReduction = __state;
            }
        }
    }
}
