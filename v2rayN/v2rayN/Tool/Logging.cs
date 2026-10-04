using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using System;
using System.IO;
using System.Threading.Tasks;

namespace v2rayN.Tool
{
    public class Logging
    {
        public static void Setup()
        {
            var hierarchy = (Hierarchy)LogManager.GetRepository();

            var patternLayout = new PatternLayout
            {
                ConversionPattern = "%date [%thread] %-5level %logger - %message%newline"
            };
            patternLayout.ActivateOptions();

            var roller = new RollingFileAppender
            {
                AppendToFile = true,
                RollingStyle = RollingFileAppender.RollingMode.Date,
                DatePattern = "yyyy-MM-dd'.txt'",
                File = Utils.GetPath(@"guiLogs\"),
                Layout = patternLayout,
                StaticLogFileName = false
            };
            roller.ActivateOptions();
            hierarchy.Root.AddAppender(roller);

            // There was a MemoryAppender here too. It was dead weight:
            //   - nothing ever read it. There is no GetEvents() call anywhere in
            //     the tree, and the log view reads the guiLogs files off disk.
            //   - log4net's MemoryAppender retains every event it receives and has
            //     no MaxBufferSize to bound it (2.0.17 exposes only OnlyFixPartial-
            //     EventData and Fix), so it grew for as long as the app was open.
            //     In a tray application expected to run for days that is a slow
            //     leak, and it cost a retention on every single log event to feed
            //     a consumer that did not exist.
            //
            // Left out. If an in-memory log view is ever wanted, add a bounded
            // appender at that point -- do not reintroduce this one.

            hierarchy.Root.Level = Level.Debug;
            hierarchy.Configured = true;
        }

        public static void ClearLogs()
        {
            Task.Run(() =>
            {
                try
                {
                    var now = DateTime.Now.AddMonths(-1);
                    var dir = Utils.GetPath(@"guiLogs\");
                    var files = Directory.GetFiles(dir, "*.txt");
                    foreach (var filePath in files)
                    {
                        var file = new FileInfo(filePath);
                        if (file.CreationTime < now)
                        {
                            try
                            {
                                file.Delete();
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            });
        }
    }
}
