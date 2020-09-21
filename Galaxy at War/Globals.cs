using System;
using System.Collections.Generic;
using System.Diagnostics;
using BattleTech;
using BattleTech.UI;
using TMPro;

// ReSharper disable ClassNeverInstantiated.Global

namespace GalaxyatWar
{
    public class Globals
    {
        internal WarStatus WarStatusTracker;
        internal readonly Random Rng = new Random();
        internal readonly Stopwatch T = new Stopwatch();
        internal SimGameState Sim;
        internal string TeamFaction;
        internal string EnemyFaction;
        internal double Difficulty;
        internal MissionResult MissionResult;
        internal string ContractType;
        internal bool IsFlashpointContract;
        internal bool HoldContracts;
        internal double AttackerInfluenceHolder;
        internal bool InfluenceMaxed;
        internal List<string> IncludedFactions;
        internal List<string> OffensiveFactions;
        internal List<FactionValue> FactionValues => FactionEnumeration.FactionList;
        internal float SpendFactor = 5;
        internal SimGameInterruptManager SimGameInterruptManager;
        internal TaskTimelineWidget TaskTimelineWidget;
        internal TMP_FontAsset Font;
        internal bool ModInitialized;
        internal List<StarSystem> GaWSystems = new List<StarSystem>();
    }
}
