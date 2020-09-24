using System.Linq;
using BattleTech;
using Harmony;
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
                FileLog.Log($"OnDayPassed {Mod.Globals.Sim.CurrentDate.ToShortDateString()}");
                if (Mod.Globals.Sim.IsCampaign && !Mod.Globals.Sim.CompanyTags.Contains("story_complete"))
                    return;

                Mod.Globals.WarStatusTracker.CurSystem = Mod.Globals.Sim.CurSystem.Name;
                if (Mod.Globals.WarStatusTracker.HotBox.Contains(Mod.Globals.Sim.CurSystem.Name) &&
                    !Mod.Globals.WarStatusTracker.HotBoxTravelling)
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
                            if (activeItems.TryGetValue(Mod.Globals.WarStatusTracker.EscalationOrder, out var order))
                            {
                                order.UpdateItem(0);
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
                            Mod.Globals.Sim.CurSystem.CurMaxBreadcrumbs = rand;
                            Mod.Globals.Sim.GeneratePotentialContracts(true, null, Mod.Globals.Sim.CurSystem);
                            Mod.Globals.Sim.CurSystem.CurMaxBreadcrumbs = maxHolder;
                            Mod.Globals.Sim.QueueCompleteBreadcrumbProcess(true);
                            Mod.Globals.SimGameInterruptManager.QueueTravelPauseNotification("New Mission", "Our Employer has launched an attack. We must take a mission to support their operation. Let's check out our contracts and get to it!", Mod.Globals.Sim.GetCrewPortrait(SimGameCrew.Crew_Darius),
                                string.Empty, null, "Proceed");
                        }
                    }
                }

                if (!Mod.Globals.WarStatusTracker.StartGameInitialized)
                {
                    FileLog.Log("Reinitializing contracts because !StartGameInitialized");
                    var cmdCenter = Mod.Globals.Sim.RoomManager.CmdCenterRoom;
                    Mod.Globals.Sim.CurSystem.GenerateInitialContracts(() => cmdCenter.OnContractsFetched());
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
                    FileLog.Log("LongWarTesting underway...");
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
                    //FileLog.Log(">>> PROC");
                    if (Mod.Globals.Sim.DayRemainingInQuarter != 30)
                    {
                        FileLog.Log("Tick...");
                        WarTick.Tick(false, false);
                    }
                    else
                    {
                        //GenerateMonthlyContracts();
                        FileLog.Log("Full tick...");
                        WarTick.Tick(true, true);
                        var hasFlashPoint = Mod.Globals.Sim.CurSystem.SystemContracts.Any(x => x.IsFlashpointContract || x.IsFlashpointCampaignContract);
                        if (!Mod.Globals.WarStatusTracker.HotBoxTravelling && !Mod.Globals.WarStatusTracker.HotBox.Contains(Mod.Globals.Sim.CurSystem.Name) && !hasFlashPoint)
                        {
                            FileLog.Log("Regenerating contracts because month-end.");
                            var cmdCenter = Mod.Globals.Sim.RoomManager.CmdCenterRoom;
                            Mod.Globals.Sim.CurSystem.GenerateInitialContracts(() => cmdCenter.OnContractsFetched());
                        }
                    }

                    FileLog.Log("Tick complete.");
                }
            }
        }
    }
}
