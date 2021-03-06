using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BattleTech;
using BattleTech.Framework;
using Harmony;
using Newtonsoft.Json;
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
            var hotkeyG = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetKeyDown(KeyCode.G) &&
                          (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            if (hotkeyG)
            {
                try
                {
                    var starSystem = Globals.Sim.CurSystem;
                    var contractEmployers = starSystem.Def.contractEmployerIDs;
                    var contractTargets = starSystem.Def.contractTargetIDs;
                    var owner = starSystem.OwnerValue;
                    LogDebug($"{starSystem.Name} owned by {owner.Name}");
                    LogDebug($"Employers in {starSystem.Name}");
                    contractEmployers.Do(x => LogDebug($"  {x}"));
                    LogDebug($"Targets in {starSystem.Name}");
                    contractTargets.Do(x => LogDebug($"  {x}"));
                    Globals.Sim.GetAllCurrentlySelectableContracts().Do(x => LogDebug($"{x.Name,-25} {x.Difficulty} ({x.Override.GetUIDifficulty()})"));
                    var systemStatus = Globals.WarStatusTracker.systems.Find(x => x.starSystem == starSystem);
                    var employers = systemStatus.influenceTracker.OrderByDescending(x => x.Value).Select(x => x.Key).Take(2);
                    foreach (var faction in Globals.Settings.IncludedFactions.Intersect(employers))
                    {
                        LogDebug($"{faction} Enemies:");
                        FactionEnumeration.GetFactionByName(faction).factionDef?.Enemies.Distinct().Do(x => LogDebug($"  {x}"));
                        LogDebug($"{faction} Allies:");
                        FactionEnumeration.GetFactionByName(faction).factionDef?.Allies.Do(x => LogDebug($"  {x}"));
                        Log("");
                    }

                    LogDebug("Player allies:");
                    foreach (var faction in Globals.Sim.AlliedFactions)
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
                Globals.Sim.CurSystem.activeSystemContracts.Clear();
                Globals.Sim.CurSystem.activeSystemBreadcrumbs.Clear();
                Helpers.BackFillContracts();
                var cmdCenter = Globals.Sim.RoomManager.CmdCenterRoom;
                cmdCenter.contractsWidget.ListContracts(Globals.Sim.GetAllCurrentlySelectableContracts(), cmdCenter.contractDisplayAutoSelect);
            }

            var hotkeyB = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetKeyDown(KeyCode.B) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            if (hotkeyB)
            {
                try
                {
                    Logger.LogDebug("Hotkey G");
                    var contracts = new List<Contract>();
                    Globals.Sim.CurSystem.activeSystemContracts.Clear();
                    var system = Globals.Sim.CurSystem;
                    var systemStatus = Globals.WarStatusTracker.systems.Find(x => x.starSystem == system);
                    var influenceTracker = systemStatus.influenceTracker;
                    var owner = influenceTracker.First().Key;
                    var second = influenceTracker.Skip(1).First().Key;
                    Logger.LogDebug(0);
                    var contract = Contracts.GenerateContract(system, 2, 2, owner);
                    contracts.Add(contract);
                    Logger.LogDebug(1);
                    contract = Contracts.GenerateContract(system, 4, 4, owner);
                    contracts.Add(contract);
                    Logger.LogDebug(2);
                    contract = Contracts.GenerateContract(system, 2, 2, second);
                    contracts.Add(contract);
                    Logger.LogDebug(3);
                    contract = Contracts.GenerateContract(system, 4, 4, second);
                    contracts.Add(contract);
                    Logger.LogDebug(4);
                    contract = Contracts.GenerateContract(system, 6, 6);
                    contracts.Add(contract);
                    Logger.LogDebug(5);
                    contract = Contracts.GenerateContract(Globals.Sim.Starmap.GetAvailableNeighborSystem(Globals.Sim.CurSystem).GetRandomElement(), 2, 2, null, null, true);
                    contracts.Add(contract);
                    Logger.LogDebug(6);
                    contract = Contracts.GenerateContract(system, 10, 10, null, null, true);
                    contract.Override.contractDisplayStyle = ContractDisplayStyle.BaseCampaignStory;
                    contracts.Add(contract);
                    Globals.Sim.CurSystem.activeSystemContracts = contracts;
                    var cmdCenter = Globals.Sim.RoomManager.CmdCenterRoom;
                    cmdCenter.contractsWidget.ListContracts(Globals.Sim.GetAllCurrentlySelectableContracts(), cmdCenter.contractDisplayAutoSelect);
                }

                catch (Exception ex)
                {
                    Logger.Error(ex);
                }
            }

            var hotkeyC = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetKeyDown(KeyCode.C) &&
                          (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
            if (hotkeyC)
            {
                try
                {
                    Logger.LogDebug("Hotkey C");
                    var contracts = new List<Contract>();
                    Logger.LogDebug(0);
                    contracts.Add(Contracts.GenerateContract(Globals.Sim.StarSystems.GetRandomElement(), 2, 2, "AuriganPirates", null, true));
                    Logger.LogDebug(1);
                    contracts.Add(Contracts.GenerateContract(Globals.Sim.CurSystem, 4, 4, "Locals", Globals.Settings.IncludedFactions));
                    Logger.LogDebug(2);
                    contracts.Add(Contracts.GenerateContract(Globals.Sim.CurSystem, 6, 6, "Locals", Globals.Settings.IncludedFactions));
                    Logger.LogDebug(3);
                    contracts.Add(Contracts.GenerateContract(Globals.Sim.StarSystems.GetRandomElement(), 6, 6, "Kurita", null, true));
                    Logger.LogDebug(4);
                    contracts.Add(Contracts.GenerateContract(Globals.Sim.StarSystems.GetRandomElement(), 8, 8, "Steiner"));
                    Logger.LogDebug(5);
                    var deployment = Contracts.GenerateContract(Globals.Sim.StarSystems.GetRandomElement(), 10, 10, "TaurianConcordat", null, true);
                    Logger.LogDebug(6);
                    deployment.Override.OnContractSuccessResults.Do(Logger.LogDebug);
                    Logger.LogDebug(7);
                    Logger.LogDebug(deployment.Override.OnContractSuccessResults.First()?.Actions[0].additionalValues[10]);
                    Logger.LogDebug(8);
                    deployment.Override.contractDisplayStyle = ContractDisplayStyle.BaseCampaignStory;
                    contracts.Add(deployment);

                    //for (var j = 0; j < 2; j++)
                    //{
                    //    for (var i = 2; i <= 10; i += 2)
                    //    {
                    //        var system = Globals.Sim.StarSystems.GetRandomElement();
                    //        var contract = Contracts.GenerateContract(system, i, i, "Davion", new List<string> {"Kurita", "Steiner"});
                    //        if (contract == null)
                    //        {
                    //            Logger.LogDebug($"Couldn't find contract for {system.Name,-20} system is {system.Def.GetDifficulty(SimGameState.SimGameType.CAREER),-2} and contract attempt is rank {i,-2}");
                    //            continue;
                    //        }
                    //
                    //        contract.Override.contractDisplayStyle = ContractDisplayStyle.BaseCampaignStory;
                    //        contracts.Add(contract);
                    //    }
                    //}

                    Globals.Sim.CurSystem.activeSystemBreadcrumbs = contracts;
                    Globals.Sim.CurSystem.activeSystemContracts = contracts;
                    var cmdCenter = Globals.Sim.RoomManager.CmdCenterRoom;
                    cmdCenter.contractsWidget.ListContracts(contracts, cmdCenter.contractDisplayAutoSelect);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }
            }

            var hotkeyT = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) &&
                          (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.T);
            if (hotkeyT)
            {
                const int loops = 100;
                Logger.LogDebug($"Running {loops} full ticks.");
                for (var i = 0; i < loops; i++)
                {
                    Logger.LogDebug("Tick " + $"{i,3}...");
                    try
                    {
                        WarTick.Tick(true, true);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex);
                    }
                }
            }
        }
    }
}
