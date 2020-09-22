using System;
using System.Collections.Generic;
using System.Reflection;
using Harmony;
using Newtonsoft.Json;
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
                Mod.Settings = JsonConvert.DeserializeObject<ModSettings>(settings);
                Mod.Settings.modDirectory = modDir;
            }
            catch (Exception)
            {
                Mod.Settings = new ModSettings();
            }

            Logger.Clear();
            Logger.LogDebug("GaW Starting up...");

            foreach (var value in Mod.Settings.GetType().GetFields())
            {
                var v = value.GetValue(Mod.Settings);
                Logger.LogDebug($"{value.Name}: {v}");
                if (v is List<string> list)
                {
                    foreach (var item in list)
                    {
                        Logger.LogDebug($"  {item}");
                    }
                }
            }

            PopulateFactions();
            var harmony = HarmonyInstance.Create("com.Same.BattleTech.GalaxyAtWar");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }
}
