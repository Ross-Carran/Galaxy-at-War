using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace GalaxyatWar
{
    public static class Logger
    {
        private static string logFilePath;

        private static string LogFilePath =>
            logFilePath ??
            (logFilePath = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName + "/Galaxy-at-War.log");

        public static async void Error(object line)
        {
            using (var writer = new StreamWriter(LogFilePath, true))
            {
                await writer.WriteLineAsync($"{GetFormattedStartupTime()}  {line ?? "null"}");
            }
        }

        public static async void LogDebug(object line)
        {
            if (!Mod.Settings.Debug) return;
            using (var writer = new StreamWriter(LogFilePath, true))
            {
                await writer.WriteLineAsync($"{GetFormattedStartupTime()}  {line}");
            }
        }

        public static async void Log(string line)
        {
            using (var writer = new StreamWriter(LogFilePath, true))
            {
                await writer.WriteLineAsync(line);
            }
        }

        public static async void Clear()
        {
            if (!Mod.Settings.Debug) return;
            using (var writer = new StreamWriter(LogFilePath, false))
            {
                await writer.WriteLineAsync($"{DateTime.Now.ToLongTimeString()} Galaxy-at-War Init");
            }
        }

        // this beauty is from BetterLog from CptMoore's MechEngineer - thanks!
        // https://github.com/BattletechModders/MechEngineer/tree/master/source/Features/BetterLog
        private static string GetFormattedStartupTime()
        {
            var value = TimeSpan.FromSeconds(Time.realtimeSinceStartup);
            var formatted = $"[{value.Hours:D2}:{value.Minutes:D2}:{value.Seconds:D2}.{value.Milliseconds:D3}]";
            return formatted;
        }
    }
}
