using System;
using System.Collections.Generic;
using Harmony;
using UnityEngine;

// ReSharper disable InconsistentNaming
// ReSharper disable ClassNeverInstantiated.Global

namespace GalaxyatWar
{
    internal class PiratesAndLocals
    {
        public static float CurrentPAResources;
        internal static readonly List<SystemStatus> FullPirateListSystems = new List<SystemStatus>();

        public static void CorrectResources()
        {
            Mod.Globals.WarStatusTracker.PirateResources -= Mod.Globals.WarStatusTracker.TempPRGain;
            if (Mod.Globals.WarStatusTracker.LastPRGain > Mod.Globals.WarStatusTracker.TempPRGain)
            {
                Mod.Globals.WarStatusTracker.PirateResources = Mod.Globals.WarStatusTracker.MinimumPirateResources;
                Mod.Globals.WarStatusTracker.MinimumPirateResources *= 1.1f;
            }
            else
            {
                Mod.Globals.WarStatusTracker.MinimumPirateResources /= 1.1f;
                if (Mod.Globals.WarStatusTracker.MinimumPirateResources < Mod.Globals.WarStatusTracker.StartingPirateResources)
                    Mod.Globals.WarStatusTracker.MinimumPirateResources = Mod.Globals.WarStatusTracker.StartingPirateResources;
            }

            foreach (var warFaction in Mod.Globals.WarStatusTracker.warFactionTracker)
            {
                warFaction.AttackResources += warFaction.PirateARLoss;
                warFaction.DefensiveResources += warFaction.PirateDRLoss;
            }

            Mod.Globals.WarStatusTracker.LastPRGain = Mod.Globals.WarStatusTracker.TempPRGain;
        }

        public static void DefendAgainstPirates()
        {
            var factionEscalateDefense = new Dictionary<WarFaction, bool>();
            foreach (var warFaction in Mod.Globals.WarStatusTracker.warFactionTracker)
            {
                var defenseValue = 100 * (warFaction.PirateARLoss + warFaction.PirateDRLoss) /
                                   (warFaction.AttackResources + warFaction.DefensiveResources + warFaction.PirateARLoss + warFaction.PirateDRLoss);
                if (defenseValue > 5)
                    factionEscalateDefense.Add(warFaction, true);
                else
                    factionEscalateDefense.Add(warFaction, false);
            }

            var tempFullPirateListSystems = new List<SystemStatus>(FullPirateListSystems);
            foreach (var system in tempFullPirateListSystems)
            {
                if (WarFaction.All.TryGetValue(system.owner, out var warFaction))
                {
                    float PAChange;
                    if (factionEscalateDefense.ContainsKey(warFaction) &&
                        factionEscalateDefense[warFaction])
                        PAChange = (float) (Mod.Globals.Rng.NextDouble() * (system.PirateActivity - system.PirateActivity / 3) + system.PirateActivity / 3);
                    else
                        PAChange = (float) (Mod.Globals.Rng.NextDouble() * (system.PirateActivity / 3));

                    var attackResources = warFaction.AttackResources;

                    if (Mod.Settings.DefendersUseARforDR && Mod.Settings.DefensiveFactions.Contains(warFaction.faction))
                        attackResources = warFaction.DefensiveResources;

                    var defenseCost = Mathf.Min(PAChange * system.TotalResources / 100, warFaction.AttackResources * 0.01f);

                    if (attackResources >= defenseCost)
                    {
                        PAChange = Math.Min(PAChange, system.PirateActivity);
                        system.PirateActivity -= PAChange;
                        if (Mod.Settings.DefendersUseARforDR && Mod.Settings.DefensiveFactions.Contains(warFaction.faction))
                            warFaction.DR_Against_Pirates += defenseCost;
                        else
                            warFaction.AR_Against_Pirates += defenseCost;
                        //warFaction.PirateDRLoss += PAChange * system.TotalResources / 100;
                    }
                    else
                    {
                        PAChange = Math.Min(attackResources, system.PirateActivity);
                        system.PirateActivity -= PAChange;
                        if (Mod.Settings.DefendersUseARforDR && Mod.Settings.DefensiveFactions.Contains(warFaction.faction))
                            warFaction.DR_Against_Pirates += defenseCost;
                        else
                            warFaction.AR_Against_Pirates += defenseCost;
                        //warFaction.PirateDRLoss += PAChange * system.TotalResources / 100;
                    }
                }
                else
                {
                    FileLog.Log($"Pirate system {system.owner} doesn't exist in WarFaction.All");
                }

                if (system.PirateActivity == 0)
                {
                    FullPirateListSystems.Remove(system);
                    Mod.Globals.WarStatusTracker.FullPirateSystems.Remove(system.name);
                }
            }
        }

        public static void PiratesStealResources()
        {
            Mod.Globals.WarStatusTracker.TempPRGain = 0;
            foreach (var warFaction in Mod.Globals.WarStatusTracker.warFactionTracker)
            {
                warFaction.PirateARLoss = 0;
                warFaction.PirateDRLoss = 0;
            }

            for (var i = 0; i < FullPirateListSystems.Count; i++)
            {
                var system = FullPirateListSystems[i];
                Mod.Globals.WarStatusTracker.PirateResources += system.TotalResources * system.PirateActivity / 100;
                Mod.Globals.WarStatusTracker.TempPRGain += system.TotalResources * system.PirateActivity / 100;

                var warFaction = WarFaction.All[system.owner];
                var warFARChange = system.AttackResources * system.PirateActivity / 100;
                if (Mod.Settings.DefendersUseARforDR && Mod.Settings.DefensiveFactions.Contains(warFaction.faction))
                    warFaction.PirateDRLoss += warFARChange;
                else
                    warFaction.PirateARLoss += warFARChange;

                if (Mod.Settings.DefendersUseARforDR && Mod.Settings.DefensiveFactions.Contains(warFaction.faction))
                    warFaction.DefensiveResources -= warFARChange;
                else
                    warFaction.AttackResources -= warFARChange;

                var warFDRChange = system.DefenseResources * system.PirateActivity / 100;
                warFaction.PirateDRLoss += warFDRChange;
                warFaction.DefensiveResources = Math.Max(0, warFaction.DefensiveResources - warFDRChange);
            }
        }

        public static void DistributePirateResources()
        {
            var i = 0;
            while (CurrentPAResources > 0 && i != 1000)
            {
                var systemStatus = Mod.Globals.WarStatusTracker.systems.GetRandomElement();
                if (systemStatus.owner == "NoFaction" ||
                    Mod.Settings.ImmuneToWar.Contains(systemStatus.owner) ||
                    Mod.Globals.WarStatusTracker.HotBox.Contains(systemStatus.name) ||
                    Mod.Globals.WarStatusTracker.FlashpointSystems.Contains(systemStatus.name) ||
                    Mod.Globals.WarStatusTracker.HyadesRimGeneralPirateSystems.Contains(systemStatus.name) ||
                    Mod.Settings.HyadesPirates.Contains(systemStatus.owner))
                {
                    systemStatus.PirateActivity = 0;
                    continue;
                }

                var currentPA = systemStatus.PirateActivity;
                float basicPA = 11 - systemStatus.DifficultyRating;

                var bonusPA = currentPA / 50;
                var totalPA = basicPA + bonusPA;

                var pirateSystemsContainsSystemStatus = Mod.Globals.WarStatusTracker.FullPirateSystems.Contains(systemStatus.name);
                //Log(systemStatus.name);
                if (currentPA + totalPA <= 100)
                {
                    if (totalPA <= CurrentPAResources)
                    {
                        systemStatus.PirateActivity += Math.Min(totalPA, 100 - systemStatus.PirateActivity) * Mod.Globals.SpendFactor;
                        CurrentPAResources -= Math.Min(totalPA, 100 - systemStatus.PirateActivity) * Mod.Globals.SpendFactor;
                        i = 0;
                        if (!pirateSystemsContainsSystemStatus)
                        {
                            Mod.Globals.WarStatusTracker.FullPirateSystems.Add(systemStatus.name);
                            FullPirateListSystems.Add(systemStatus);
                        }
                    }
                    else
                    {
                        systemStatus.PirateActivity += Math.Min(CurrentPAResources, 100 - systemStatus.PirateActivity);
                        CurrentPAResources = 0;
                        if (!pirateSystemsContainsSystemStatus)
                        {
                            Mod.Globals.WarStatusTracker.FullPirateSystems.Add(systemStatus.name);
                            FullPirateListSystems.Add(systemStatus);
                        }
                    }
                }
                else
                {
                    if (100 - systemStatus.PirateActivity <= CurrentPAResources)
                    {
                        systemStatus.PirateActivity += (100 - systemStatus.PirateActivity) * Mod.Globals.SpendFactor;
                        CurrentPAResources -= (100 - systemStatus.PirateActivity) * Mod.Globals.SpendFactor;
                        i++;
                        if (!pirateSystemsContainsSystemStatus)
                        {
                            Mod.Globals.WarStatusTracker.FullPirateSystems.Add(systemStatus.name);
                            FullPirateListSystems.Add(systemStatus);
                        }
                    }
                    else
                    {
                        systemStatus.PirateActivity += Math.Min(CurrentPAResources, 100 - systemStatus.PirateActivity);
                        CurrentPAResources = 0;
                        if (!pirateSystemsContainsSystemStatus)
                        {
                            Mod.Globals.WarStatusTracker.FullPirateSystems.Add(systemStatus.name);
                            FullPirateListSystems.Add(systemStatus);
                        }
                    }
                }
            }
        }
    }
}
