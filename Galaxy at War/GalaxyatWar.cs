using System;
using System.Collections.Generic;
using System.Reflection;
using Harmony;
using Newtonsoft.Json;
using static GalaxyatWar.Globals;
using static GalaxyatWar.Helpers;

// ReSharper disable UnusedType.Global 
// ReSharper disable UnusedMember.Global

namespace GalaxyatWar
{
    public class GalaxyatWar
    {
        public static void Init(string modDir, string settings)
        {
            // read settings
            try
            {
               Mod.Globals.Settings = JsonConvert.DeserializeObject<ModSettings>(settings);
               Mod.Globals.Settings.modDirectory = modDir;
            }
            catch (Exception)
            {
                Mod.Globals.Settings = new ModSettings();
            }

            Logger.Clear();
            Logger.LogDebug("GaW Starting up...");
            
            foreach (var value in Mod.Globals.Settings.GetType().GetFields())
            {
                var v = value.GetValue(Mod.Globals.Settings);
                Logger.LogDebug($"{value.Name}: {v}");
                if (v is List<string> list)
                {
                    foreach (var item in list)
                    {
                        Logger.LogDebug($"  {item}");
                    }
                }
            }

            var harmony = HarmonyInstance.Create("com.Same.BattleTech.GalaxyAtWar");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
           
            // blank the logfile

            CopySettingsToState();
        }
    }
}
