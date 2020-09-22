using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace GalaxyatWar
{
    public class DeathListTracker
    {
        public string faction;
        public Dictionary<string, float> deathList = new Dictionary<string, float>();
        public List<string> Enemies => deathList.Where(x => x.Value >= 75).Select(x => x.Key).ToList();
        public List<string> Allies => deathList.Where(x => x.Value <= 25).Select(x => x.Key).ToList();
        internal static Dictionary<string, DeathListTracker> All = new Dictionary<string, DeathListTracker>();
        private WarFaction warFactionBackingField;

        internal WarFaction WarFaction
        {
            get
            {
                if (warFactionBackingField == null)
                {
                    WarFaction.All.TryGetValue(faction, out warFactionBackingField);
                }

                return warFactionBackingField;
            }
            set => warFactionBackingField = value;
        }

        [JsonConstructor]
        public DeathListTracker()
        {
            // deser ctor
        }

        public DeathListTracker(string faction)
        {
            Logger.LogDebug("DeathListTracker ctor: " + faction);
            this.faction = faction;
            var factionDef = Mod.Globals.Sim.GetFactionDef(faction);
            All.Add(faction, this);

            // TODO comment this
            foreach (var includedFaction in Mod.Globals.IncludedFactions)
            {
                var def = Mod.Globals.Sim.GetFactionDef(includedFaction);
                if (!Mod.Globals.IncludedFactions.Contains(def.FactionValue.Name))
                    continue;
                if (factionDef != def && factionDef.Enemies.Contains(def.FactionValue.Name))
                    deathList.Add(def.FactionValue.Name, Mod.Settings.KLValuesEnemies);
                else if (factionDef != def && factionDef.Allies.Contains(def.FactionValue.Name))
                    deathList.Add(def.FactionValue.Name, Mod.Settings.KLValueAllies);
                else if (factionDef != def)
                    deathList.Add(def.FactionValue.Name, Mod.Settings.KLValuesNeutral);
            }
        }
    }
}
