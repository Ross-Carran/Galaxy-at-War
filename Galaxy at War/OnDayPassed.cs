using System.Linq;
using BattleTech;
using Harmony;
using static GalaxyatWar.Logger;
using static GalaxyatWar.Helpers;
// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global


namespace GalaxyatWar
{
    public class OnDayPassed
    {
        [HarmonyPatch(typeof(SimGameState), "OnDayPassed")]
        public static class SimGameStateOnDayPassedPatch
        {
            private static void Prefix()
            {
                LogDebug($"OnDayPassed {Mod.Globals.Sim.CurrentDate.ToShortDateString()}");
                //var starSystem = Mod.Globals.Sim.CurSystem;
                //var contractEmployers = starSystem.Def.contractEmployerIDs;
                //var contractTargets = starSystem.Def.contractTargetIDs;
                //var owner = starSystem.OwnerValue;
                //LogDebug($"{starSystem.Name} owned by {owner.Name}");
                //LogDebug($"Employers in {starSystem.Name}");
                //contractEmployers.Do(x => LogDebug($"  {x}"));
                //LogDebug($"Targets in {starSystem.Name}");
                //contractTargets.Do(x => LogDebug($"  {x}"));
                //Mod.Globals.Sim.GetAllCurrentlySelectableContracts().Do(x => LogDebug($"{x.Name,-25} {x.Difficulty} ({x.Override.GetUIDifficulty()})"));
                //var systemStatus = Mod.Globals.WarStatusTracker.systems.Find(x => x.starSystem == starSystem);
                //var employers = systemStatus.influenceTracker.OrderByDescending(x=> x.Value).Select(x => x.Key).Take(2); 
                //foreach (var faction in Mod.Settings.IncludedFactions.Intersect(employers))
                //{
                    //LogDebug($"{faction} Enemies:");
                    //FactionEnumeration.GetFactionByName(faction).factionDef?.Enemies.Distinct().Do(x => LogDebug($"  {x}"));
                    //LogDebug($"{faction} Allies:");
                    //FactionEnumeration.GetFactionByName(faction).factionDef?.Allies.Do(x => LogDebug($"  {x}"));
                    //Log("");
                //}
                //LogDebug("Player allies:");
                //foreach (var faction in Mod.Globals.Sim.AlliedFactions)
                //{
                //    LogDebug($"  {faction}");
                //}
                
                if (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                    return;

                Mod.Globals.WarStatusTracker.CurSystem = Mod.Globals.Sim.CurSystem.Name;
                if (Mod.Globals.WarStatusTracker.HotBox.Contains(Mod.Globals.Sim.CurSystem.Name) && !Mod.Globals.WarStatusTracker.HotBoxTravelling)
                {
                    Mod.Globals.WarStatusTracker.EscalationDays--;

                    if (!Mod.Globals.WarStatusTracker.Deployment)
                    {
                        if (Mod.Globals.WarStatusTracker.EscalationDays == 0)
                        {
                            HotSpots.CompleteEscalation();
                        }

                        if (Mod.Globals.WarStatusTracker.EscalationOrder != null)
                        {
                            Mod.Globals.WarStatusTracker.EscalationOrder.PayCost(1);
                            var activeItems = Mod.Globals.TaskTimelineWidget.ActiveItems;
                            if (activeItems.TryGetValue(Mod.Globals.WarStatusTracker.EscalationOrder, out var taskManagementElement4))
                            {
                                taskManagementElement4.UpdateItem(0);
                            }
                        }
                    }
                    else
                    {
                        if (Mod.Globals.WarStatusTracker.EscalationOrder != null)
                        {
                            Mod.Globals.WarStatusTracker.EscalationOrder.PayCost(1);
                            var activeItems = Mod.Globals.TaskTimelineWidget.ActiveItems;
                            if (activeItems.TryGetValue(Mod.Globals.WarStatusTracker.EscalationOrder, out var taskManagementElement4))
                            {
                                taskManagementElement4.UpdateItem(0);
                            }
                        }

                        if (Mod.Globals.WarStatusTracker.EscalationDays <= 0)
                        {
                            Mod.Globals.Sim.StopPlayMode();

                            Mod.Globals.Sim.CurSystem.activeSystemContracts.Clear();
                            Mod.Globals.Sim.CurSystem.activeSystemBreadcrumbs.Clear();
                            HotSpots.TemporaryFlip(Mod.Globals.Sim.CurSystem, Mod.Globals.WarStatusTracker.DeploymentEmployer);

                            var maxHolder = Mod.Globals.Sim.CurSystem.CurMaxBreadcrumbs;
                            var rand = Mod.Globals.Rng.Next(1, (int) Mod.Settings.DeploymentContracts + 1);

                            Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurMaxBreadcrumbs").SetValue(rand);
                            Mod.Globals.Sim.GeneratePotentialContracts(true, null, Mod.Globals.Sim.CurSystem);
                            Traverse.Create(Mod.Globals.Sim.CurSystem).Property("CurMaxBreadcrumbs").SetValue(maxHolder);

                            Mod.Globals.Sim.QueueCompleteBreadcrumbProcess(true);
                            Mod.Globals.SimGameInterruptManager.QueueTravelPauseNotification("New Mission", "Our Employer has launched an attack. We must take a mission to support their operation. Let's check out our contracts and get to it!", Mod.Globals.Sim.GetCrewPortrait(SimGameCrew.Crew_Darius),
                                string.Empty, null, "Proceed");
                        }
                    }
                }

                if (!Mod.Globals.WarStatusTracker.StartGameInitialized)
                {
                    LogDebug("Reinitializing contracts because !StartGameInitialized");
                    var cmdCenter = Mod.Globals.Sim.RoomManager.CmdCenterRoom;
                    Mod.Globals.Sim.CurSystem.GenerateInitialContracts(() => Traverse.Create(cmdCenter).Method("OnContractsFetched"));
                    Mod.Globals.WarStatusTracker.StartGameInitialized = true;
                }
            }

            public static void Postfix()
            {
                if (Mod.Globals.WarStatusTracker == null || Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                    return;

                if (!Mod.Globals.WarStatusTracker.GaW_Event_PopUp)
                {
                    GaW_Notification();
                    Mod.Globals.WarStatusTracker.GaW_Event_PopUp = true;
                }

                //TEST: run 100 WarTicks and stop
                if (Mod.Settings.LongWarTesting)
                {
                    LogDebug("LongWarTesting underway...");
                    for (var i = 0; i < 100; i++)
                    {
                        WarTick.Tick(true, false);
                        WarTick.Tick(true, false);
                        WarTick.Tick(true, false);
                        WarTick.Tick(true, true);
                    }

                    Mod.Globals.Sim.StopPlayMode();
                    return;
                }

                //Remove systems from the protected pool.
                foreach (var tag in Mod.Globals.Sim.CompanyTags)
                {
                    if (Mod.Settings.FlashpointReleaseSystems.Keys.Contains(tag))
                    {
                        if (Mod.Globals.WarStatusTracker.FlashpointSystems.Contains(Mod.Settings.FlashpointReleaseSystems[tag]))
                            Mod.Globals.WarStatusTracker.FlashpointSystems.Remove(Mod.Settings.FlashpointReleaseSystems[tag]);
                    }
                }

                if (Mod.Globals.Sim.DayRemainingInQuarter % Mod.Settings.WarFrequency == 0)
                {
                    //LogDebug(">>> PROC");
                    if (Mod.Globals.Sim.DayRemainingInQuarter != 30)
                    {
                        LogDebug("Tick...");
                        WarTick.Tick(false, false);
                    }
                    else
                    {
                        //GenerateMonthlyContracts();
                        LogDebug("Full tick...");
                        WarTick.Tick(true, true);
                        var hasFlashPoint = Mod.Globals.Sim.CurSystem.SystemContracts.Any(x => x.IsFlashpointContract || x.IsFlashpointCampaignContract);
                        if (!Mod.Globals.WarStatusTracker.HotBoxTravelling && !Mod.Globals.WarStatusTracker.HotBox.Contains(Mod.Globals.Sim.CurSystem.Name) && !hasFlashPoint)
                        {
                            LogDebug("Regenerating contracts because month-end.");
                            var cmdCenter = Mod.Globals.Sim.RoomManager.CmdCenterRoom;
                            Mod.Globals.Sim.CurSystem.GenerateInitialContracts(() => cmdCenter.OnContractsFetched());
                        }
                    }

                    LogDebug("Tick complete.");
                }
            }
        }
    }
}
