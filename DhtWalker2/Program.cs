using CLRCLI;
using CLRCLI.Widgets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DhtWalker2
{
    public class Program
    {
        public static bool NoLog;
        public static bool SaveSeed;
        public static bool NoMongo;
        public static bool Console;
        public static void Main(string[] args)
        {
            MainUI ui;
            ResolveArgs(args);
            if (!NoLog)
                Trace.Listeners.Add(new TextWriterTraceListener(string.Format("spider.{0}.log", DateTime.Now.ToBinary())));
            if (Console)
            {
                ui = new MainConsole();
                Trace.Listeners.Add(new ConsoleTraceListener());
            }
            else
            {
                ui = new MainWindow();
            }
            ui.Run();
        }

        private static void ResolveArgs(string[] args)
        {
            foreach (var item in args)
            {
                switch (item)
                {
                    case "nl":
                    case "no-log":
                        NoLog = true;
                        break;
                    case "s":
                    case "save-seed":
                        SaveSeed = true;
                        break;
                    case "nm":
                    case "no-mongo":
                        NoMongo = true;
                        break;
                    case "c":
                    case "console":
                        Console = true;
                        break;
                    default:
                        break;
                }
            }
        }
    }
}
