using System;
using System.Collections.Generic;
using System.Linq;
using Harmony;
using UnityEngine;
using static GalaxyatWar.Helpers;
using static GalaxyatWar.Resource;
using static GalaxyatWar.Logger;
// ReSharper disable ClassNeverInstantiated.Global

namespace GalaxyatWar
{
    public class WarTick
    {
        internal static void Tick(bool useFullSet, bool checkForSystemChange)
        {
            Mod.Globals.WarStatusTracker.PrioritySystems.Clear();
            var systemSubsetSize = Mod.Globals.WarStatusTracker.systems.Count;
            List<SystemStatus> systemStatuses;

            if (Mod.Settings.UseSubsetOfSystems && !useFullSet)
            {
                systemSubsetSize = (int) (systemSubsetSize * Mod.Settings.SubSetFraction);
                systemStatuses = Mod.Globals.WarStatusTracker.systems
                    .OrderBy(x => Guid.NewGuid()).Take(systemSubsetSize)
                    .ToList();
            }
            else
            {
                systemStatuses = Mod.Globals.WarStatusTracker.systems;
            }

            if (checkForSystemChange && Mod.Settings.GaW_PoliceSupport)
                CalculateComstarSupport();

            if (Mod.Globals.WarStatusTracker.FirstTickInitialization)
            {
                var lowestAR = 5000f;
                var lowestDr = 5000f;
                var sequence = Mod.Globals.WarStatusTracker.warFactionTracker.Where(x =>
                    Mod.Globals.IncludedFactions.Contains(x.faction)).ToList();
                foreach (var faction in sequence)
                {
                    var systemCount = Mod.Globals.WarStatusTracker.systems.Count(x => x.owner == faction.faction);
                    if (!Mod.Settings.ISMCompatibility && systemCount != 0)
                    {
                        faction.AR_PerPlanet = (float) Mod.Settings.BonusAttackResources[faction.faction] / systemCount;
                        faction.DR_PerPlanet = (float) Mod.Settings.BonusDefensiveResources[faction.faction] / systemCount;
                        if (faction.AR_PerPlanet < lowestAR)
                            lowestAR = faction.AR_PerPlanet;
                        if (faction.DR_PerPlanet < lowestDr)
                            lowestDr = faction.DR_PerPlanet;
                    }
                    else if (systemCount != 0)
                    {
                        faction.AR_PerPlanet = (float) Mod.Settings.BonusAttackResources_ISM[faction.faction] / systemCount;
                        faction.DR_PerPlanet = (float) Mod.Settings.BonusDefensiveResources_ISM[faction.faction] / systemCount;
                        if (faction.AR_PerPlanet < lowestAR)
                            lowestAR = faction.AR_PerPlanet;
                        if (faction.DR_PerPlanet < lowestDr)
                            lowestDr = faction.DR_PerPlanet;
                    }
                }

                foreach (var faction in sequence)
                {
                    faction.AR_PerPlanet = Mathf.Min(faction.AR_PerPlanet, 2 * lowestAR);
                    faction.DR_PerPlanet = Mathf.Min(faction.DR_PerPlanet, 2 * lowestDr);
                }

                foreach (var systemStatus in systemStatuses)
                {
                    //Spread out bonus resources and make them fair game for the taking.
                    var warFaction = Mod.Globals.WarStatusTracker.warFactionTracker.Find(x => x.faction == systemStatus.owner);
                    systemStatus.AttackResources += warFaction.AR_PerPlanet;
                    systemStatus.TotalResources += warFaction.AR_PerPlanet;
                    systemStatus.DefenseResources += warFaction.DR_PerPlanet;
                    systemStatus.TotalResources += warFaction.DR_PerPlanet;
                }
            }

            //Distribute Pirate Influence throughout the StarSystems
            LogDebug("Processing pirates.");
            PiratesAndLocals.CorrectResources();
            PiratesAndLocals.PiratesStealResources();
            PiratesAndLocals.CurrentPAResources = Mod.Globals.WarStatusTracker.PirateResources;
            PiratesAndLocals.DistributePirateResources();
            PiratesAndLocals.DefendAgainstPirates();

            if (checkForSystemChange && Mod.Settings.HyadesRimCompatible && Mod.Globals.WarStatusTracker.InactiveTHRFactions.Count != 0
                && Mod.Globals.WarStatusTracker.HyadesRimGeneralPirateSystems.Count != 0)
            {
                var rand = Mod.Globals.Rng.Next(0, 100);
                if (rand < Mod.Globals.WarStatusTracker.HyadesRimsSystemsTaken)
                {
                    var hyadesSystem = Mod.Globals.WarStatusTracker.HyadesRimGeneralPirateSystems.GetRandomElement();
                    var flipSystem = Mod.Globals.WarStatusTracker.systems.Find(x => x.name == hyadesSystem).starSystem;
                    var inactiveFaction = Mod.Globals.WarStatusTracker.InactiveTHRFactions.GetRandomElement();
                    ChangeSystemOwnership(flipSystem, inactiveFaction, true);
                    Mod.Globals.WarStatusTracker.InactiveTHRFactions.Remove(inactiveFaction);
                    Mod.Globals.WarStatusTracker.HyadesRimGeneralPirateSystems.Remove(hyadesSystem);
                }
            }

            LogDebug("Processing systems' influence.");
            foreach (var systemStatus in systemStatuses)
            {
                systemStatus.PriorityAttack = false;
                systemStatus.PriorityDefense = false;
                if (Mod.Globals.WarStatusTracker.FirstTickInitialization)
                {
                    systemStatus.CurrentlyAttackedBy.Clear();
                    CalculateAttackAndDefenseTargets(systemStatus.starSystem);
                    RefreshContractsEmployersAndTargets(systemStatus);
                }

                if (systemStatus.Contended || Mod.Globals.WarStatusTracker.HotBox.Contains(systemStatus.name))
                    continue;

                if (!systemStatus.owner.Equals("Locals") && systemStatus.influenceTracker.Keys.Contains("Locals") &&
                    !Mod.Globals.WarStatusTracker.FlashpointSystems.Contains(systemStatus.name))
                {
                    systemStatus.influenceTracker["Locals"] *= 1.1f;
                }

                //Add resources from neighboring systems.
                if (systemStatus.neighborSystems.Count != 0)
                {
                    foreach (var neighbor in systemStatus.neighborSystems.Keys)
                    {
                        if (!Mod.Settings.ImmuneToWar.Contains(neighbor) && !Mod.Settings.DefensiveFactions.Contains(neighbor) &&
                            !Mod.Globals.WarStatusTracker.FlashpointSystems.Contains(systemStatus.name))
                        {
                            var pushFactor = Mod.Settings.APRPush * Mod.Globals.Rng.Next(1, Mod.Settings.APRPushRandomizer + 1);
                            systemStatus.influenceTracker[neighbor] += systemStatus.neighborSystems[neighbor] * pushFactor;
                        }
                    }
                }

                //Revolt on previously taken systems.
                if (systemStatus.owner != systemStatus.OriginalOwner)
                    systemStatus.influenceTracker[systemStatus.OriginalOwner] *= 0.10f;

                var pirateSystemFlagValue = Mod.Settings.PirateSystemFlagValue;

                if (Mod.Settings.ISMCompatibility)
                    pirateSystemFlagValue = Mod.Settings.PirateSystemFlagValue_ISM;

                var totalPirates = systemStatus.PirateActivity * systemStatus.TotalResources / 100;

                if (totalPirates >= pirateSystemFlagValue)
                {
                    if (!Mod.Globals.WarStatusTracker.PirateHighlight.Contains(systemStatus.name))
                        Mod.Globals.WarStatusTracker.PirateHighlight.Add(systemStatus.name);
                }
                else
                {
                    if (Mod.Globals.WarStatusTracker.PirateHighlight.Contains(systemStatus.name))
                        Mod.Globals.WarStatusTracker.PirateHighlight.Remove(systemStatus.name);
                }
            }

            Mod.Globals.WarStatusTracker.FirstTickInitialization = false;
            LogDebug("Processing resource spending.");
            foreach (var warFaction in Mod.Globals.WarStatusTracker.warFactionTracker)
            {
                DivideAttackResources(warFaction, useFullSet);
            }

            CalculateDefensiveSystems();
            foreach (var warFaction in Mod.Globals.WarStatusTracker.warFactionTracker)
            {
                AllocateDefensiveResources(warFaction, useFullSet);
                AllocateAttackResources(warFaction);
            }

            LogDebug("Processing influence changes.");
            UpdateInfluenceFromAttacks(checkForSystemChange);

            //Increase War Escalation or decay defenses.
            foreach (var warFaction in Mod.Globals.WarStatusTracker.warFactionTracker)
            {
                if (!warFaction.GainedSystem)
                    warFaction.DaysSinceSystemAttacked += 1;
                else
                {
                    warFaction.DaysSinceSystemAttacked = 0;
                    warFaction.GainedSystem = false;
                }

                if (!warFaction.LostSystem)
                    warFaction.DaysSinceSystemLost += 1;
                else
                {
                    warFaction.DaysSinceSystemLost = 0;
                    warFaction.LostSystem = false;
                }
            }

            LogDebug("Processing flipped systems.");
            foreach (var system in Mod.Globals.WarStatusTracker.systems.Where(x => Mod.Globals.WarStatusTracker.SystemChangedOwners.Contains(x.name)))
            {
                system.CurrentlyAttackedBy.Clear();
                CalculateAttackAndDefenseTargets(system.starSystem);
                RefreshContractsEmployersAndTargets(system);
            }

            if (Mod.Globals.WarStatusTracker.SystemChangedOwners.Count > 0)
            {
                LogDebug($"Changed on {Mod.Globals.Sim.CurrentDate.ToShortDateString()}: {Mod.Globals.WarStatusTracker.SystemChangedOwners.Count} systems:");
                Mod.Globals.WarStatusTracker.SystemChangedOwners.OrderBy(x => x).Do(x =>
                    LogDebug($"  {x}"));
                Mod.Globals.WarStatusTracker.SystemChangedOwners.Clear();
                if (StarmapMod.eventPanel != null)
                {
                    StarmapMod.UpdatePanelText();
                }
            }

            //Log("===================================================");
            //Log("TESTING ZONE");
            //Log("===================================================");
            //////TESTING ZONE
            //foreach (WarFaction WF in Mod.Globals.WarStatusTracker.warFactionTracker)
            //{
            //    Log("----------------------------------------------");
            //    Log(WF.faction.ToString());
            //    try
            //    {
            //        var DLT = Mod.Globals.WarStatusTracker.DeathListTrackers.Find(x => x.faction == WF.faction);
            //        //                Log("\tAttacked By :");
            //        //                foreach (Faction fac in DLT.AttackedBy)
            //        //                    Log("\t\t" + fac.ToString());
            //        //                Log("\tOwner :" + DLT.);
            //        //                Log("\tAttack Resources :" + WF.AttackResources.ToString());
            //        //                Log("\tDefensive Resources :" + WF.DefensiveResources.ToString());
            //        Log("\tDeath List:");
            //        foreach (var faction in DLT.deathList.Keys)
            //        {
            //            Log("\t\t" + faction.ToString() + ": " + DLT.deathList[faction]);
            //        }
            //    }
            //    catch (Exception e)
            //    {
            //        Error(e);
            //    }
            //}
        }
    }
}
