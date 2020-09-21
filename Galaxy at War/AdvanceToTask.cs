using System;
using BattleTech;

namespace GalaxyatWar
{
    public static class AdvanceToTask
    {
        private static WorkOrderEntry advancingTo;
        private static float oldDayElapseTimeNormal;

        public static void StartAdvancing(WorkOrderEntry entry)
        {
            if (Mod.Globals.Sim.CurRoomState != DropshipLocation.SHIP)
                return;

            advancingTo = entry;
            Mod.Globals.Sim.SetTimeMoving(true);

            // set the elapseTime variable so that the days pass faster
            if (Math.Abs(Mod.Globals.Sim.Constants.Time.DayElapseTimeNormal - Mod.Settings.AdvanceToTaskTime) > 0.01)
            {
                oldDayElapseTimeNormal = Mod.Globals.Sim.Constants.Time.DayElapseTimeNormal;
                Mod.Globals.Sim.Constants.Time.DayElapseTimeNormal = Mod.Settings.AdvanceToTaskTime;
            }
        }

        public static void StopAdvancing()
        {
            if (advancingTo == null)
                return;

            advancingTo = null;

            Mod.Globals.Sim.Constants.Time.DayElapseTimeNormal = oldDayElapseTimeNormal;
            Mod.Globals.Sim.SetTimeMoving(false);
        }

        public static void OnDayAdvance()
        {
            if (advancingTo == null)
                return;

            var activeItems = Mod.Globals.TaskTimelineWidget.ActiveItems;

            // if timeline doesn't contain advancingTo or advancingTo is over
            if (!activeItems.ContainsKey(advancingTo) || advancingTo.IsCostPaid())
                StopAdvancing();
        }
    }
}
