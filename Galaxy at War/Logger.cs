using System;
using System.IO;
using System.Reflection;

namespace GalaxyatWar
{
    public class Logger
    {
        private string logFilePath;
        private readonly ModLogWriter writer;

        private string LogFilePath =>
            logFilePath ??
            (logFilePath = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName + "/Galaxy-at-War.log");

        public Logger()
        {
            try
            {
                File.Delete(LogFilePath);
            }
            catch (Exception)
            {
                //ignore
            }

            var streamWriter = File.AppendText(LogFilePath);
            writer = new ModLogWriter(streamWriter);
        }

        internal ModLogWriter? Debug => !Mod.Settings.Debug ? null : (ModLogWriter?) writer;
        internal ModLogWriter? Error => writer;
    }

    internal struct ModLogWriter
    {
        private readonly StreamWriter streamWriter;

        public ModLogWriter(StreamWriter sw)
        {
            streamWriter = sw;
        }

        internal void Write(object input)
        {
            using (streamWriter)
            {
                streamWriter.Write("POOP");
            }
            streamWriter.WriteLine($"{Helpers.GetFormattedStartupTime()}  {input ?? "null"}");
            streamWriter.Flush();
        }
    }
}
