using System;
using System.IO;
using System.Linq;
using BattleTech;
using Harmony;
using UnityEngine;
using static GalaxyatWar.Logger;

// ReSharper disable InconsistentNaming

namespace GalaxyatWar
{
    [HarmonyPatch(typeof(UnityGameInstance), "Update")]
    public static class SimGameStateUpdatePatch
    {
        public static void Postfix()
        {
            var hotkeyD = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetKeyDown(KeyCode.D) &&
                          (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            if (hotkeyD)
            {
                File.WriteAllText("mods/GalaxyAtWar/tag.txt", Mod.Globals.Sim.CompanyTags.First(x => x.StartsWith("GalaxyAtWar")));
            }

            var hotkeyG = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetKeyDown(KeyCode.G) &&
                          (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            if (hotkeyG)
            {
                try
                {
                    var starSystem = Mod.Globals.Sim.CurSystem;
                    var contractEmployers = starSystem.Def.contractEmployerIDs;
                    var contractTargets = starSystem.Def.contractTargetIDs;
                    var owner = starSystem.OwnerValue;
                    LogDebug($"{starSystem.Name} owned by {owner.Name}");
                    LogDebug($"Employers in {starSystem.Name}");
                    contractEmployers.Do(x => LogDebug($"  {x}"));
                    LogDebug($"Targets in {starSystem.Name}");
                    contractTargets.Do(x => LogDebug($"  {x}"));
                    Mod.Globals.Sim.GetAllCurrentlySelectableContracts().Do(x => LogDebug($"{x.Name,-25} {x.Difficulty} ({x.Override.GetUIDifficulty()})"));
                    var systemStatus = Mod.Globals.WarStatusTracker.systems.Find(x => x.starSystem == starSystem);
                    var employers = systemStatus.influenceTracker.OrderByDescending(x => x.Value).Select(x => x.Key).Take(2);
                    foreach (var faction in Mod.Settings.IncludedFactions.Intersect(employers))
                    {
                        LogDebug($"{faction} Enemies:");
                        FactionEnumeration.GetFactionByName(faction).factionDef?.Enemies.Distinct().Do(x => LogDebug($"  {x}"));
                        LogDebug($"{faction} Allies:");
                        FactionEnumeration.GetFactionByName(faction).factionDef?.Allies.Do(x => LogDebug($"  {x}"));
                        Log("");
                    }

                    LogDebug("Player allies:");
                    foreach (var faction in Mod.Globals.Sim.AlliedFactions)
                    {
                        LogDebug($"  {faction}");
                    }
                }
                catch (Exception ex)
                {
                    Error(ex);
                }
            }

            var hotkeyJ = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetKeyDown(KeyCode.J) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            if (hotkeyJ)
            {
                Mod.Globals.Sim.CurSystem.activeSystemContracts.Clear();
                Mod.Globals.Sim.CurSystem.activeSystemBreadcrumbs.Clear();
                //Helpers.FillContracts(3, 2, 1, 2, 1);
                var cmdCenter = Mod.Globals.Sim.RoomManager.CmdCenterRoom;
                cmdCenter.contractsWidget.ListContracts(Mod.Globals.Sim.GetAllCurrentlySelectableContracts(), cmdCenter.contractDisplayAutoSelect);
            }

            var hotkeyT = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) &&
                          (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.T);
            if (hotkeyT)
            {
                const int loops = 100;
                LogDebug($"Running {loops} full ticks.");
                for (var i = 0; i < loops; i++)
                {
                    LogDebug("Tick " + $"{i,3}...");
                    try
                    {
                        WarTick.Tick(true, true);
                    }
                    catch (Exception ex)
                    {
                        Error(ex);
                    }
                }
            }
        }
    }
}
