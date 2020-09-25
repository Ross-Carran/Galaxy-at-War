using System;
using System.IO;
using System.Linq;
using BattleTech;
using Harmony;
using UnityEngine;
// ReSharper disable UnusedType.Global 
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
                var cmdCenter = Mod.Globals.Sim.RoomManager.CmdCenterRoom;
                Mod.Globals.Sim.CurSystem.GenerateInitialContracts(() => cmdCenter.OnContractsFetched());
                //File.WriteAllText("mods/GalaxyAtWar/tag.txt", Mod.Globals.Sim.CompanyTags.First(x => x.StartsWith("GalaxyAtWar")));
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
                    FileLog.Log($"{starSystem.Name} owned by {owner.Name}");
                    FileLog.Log($"Employers in {starSystem.Name}");
                    contractEmployers.Do(x => FileLog.Log($"  {x}"));
                    FileLog.Log($"Targets in {starSystem.Name}");
                    contractTargets.Do(x => FileLog.Log($"  {x}"));
                    Mod.Globals.Sim.GetAllCurrentlySelectableContracts().Do(x => FileLog.Log($"{x.Name,-25} {x.Difficulty} ({x.Override.GetUIDifficulty()})"));
                    var systemStatus = SystemStatus.All[starSystem.Name];
                    var employers = systemStatus.influenceTracker.OrderByDescending(x => x.Value).Select(x => x.Key).Take(2);
                    foreach (var faction in Mod.Globals.IncludedFactions.Intersect(employers))
                    {
                        FileLog.Log($"{faction} Enemies:");
                        FactionEnumeration.GetFactionByName(faction).factionDef?.Enemies.Distinct().Do(x => FileLog.Log($"  {x}"));
                        FileLog.Log($"{faction} Allies:");
                        FactionEnumeration.GetFactionByName(faction).factionDef?.Allies.Do(x => FileLog.Log($"  {x}"));
                    }

                    FileLog.Log("Player allies:");
                    foreach (var faction in Mod.Globals.Sim.AlliedFactions)
                    {
                        FileLog.Log($"  {faction}");
                    }
                }
                catch (Exception ex)
                {
                    FileLog.Log(ex.ToString());
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
                FileLog.Log($"Running {loops} full ticks.");
                for (var i = 0; i < loops; i++)
                {
                    FileLog.Log("Tick " + $"{i,3}...");
                    try
                    {
                        
                        WarTick.Tick(true, false);
                        WarTick.Tick(true, false);
                        WarTick.Tick(true, false);
                        WarTick.Tick(true, true);
                    }
                    catch (Exception ex)
                    {
                        FileLog.Log(ex.ToString());
                    }
                }
            }
        }
    }
}
