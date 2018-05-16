﻿using System;
using System.Globalization;
using System.Threading;
using ThunderED.Classes;
using ThunderED.Helpers;

namespace ThunderED
{
    internal class Program
    {
        private static Timer _timer;
        public const string VERSION = "1.1.1";

        private static void Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            //load settings
            SettingsManager.Prepare();
            LogHelper.LogInfo($"ThunderED v{VERSION} is running!").GetAwaiter().GetResult();
            //load database provider
            var result = SQLiteHelper.LoadProvider();
            if (!string.IsNullOrEmpty(result))
            {
                Console.BackgroundColor = ConsoleColor.Red;
                Console.WriteLine(result);
                Console.ReadKey();
                return;
            }
            //update config settings
            if (SettingsManager.GetBool("config", "moduleNotificationFeed"))
            {
                var dateStr = SQLiteHelper.SQLiteDataQuery<string>("cacheData", "data", "name", "nextNotificationCheck").GetAwaiter().GetResult();
                if(DateTime.TryParseExact(dateStr, new [] {"dd.MM.yyyy HH:mm:ss", $"{CultureInfo.InvariantCulture.DateTimeFormat.ShortDatePattern} {CultureInfo.InvariantCulture.DateTimeFormat.LongTimePattern}"}, CultureInfo.InvariantCulture.DateTimeFormat, DateTimeStyles.None, out var x))
                    SettingsManager.NextNotificationCheck = x;
            }

            //load language
            LM.Load().GetAwaiter().GetResult();
            //load APIs
            APIHelper.Prepare().GetAwaiter().GetResult();
            //Load modules
            TickManager.LoadModules();
            //initiate core timer
            _timer = new Timer(TickManager.Tick, new AutoResetEvent(true), 100, 100);

            while (true)
            {
                var command = Console.ReadLine();
                switch (command?.Split(" ")[0])
                {
                    case "quit":
                        Console.WriteLine("Quitting...");
                        _timer.Dispose();
                        APIHelper.DiscordAPI.Stop();
                        return;
                    case "flushn":
                        Console.WriteLine("Flushing all notifications DB list");
                        SQLiteHelper.RunCommand("delete from notificationsList").GetAwaiter().GetResult();
                        break;
                    case "flushcache":
                        Console.WriteLine("Flushing all cache from DB");
                        SQLiteHelper.RunCommand("delete from cache").GetAwaiter().GetResult();
                        break;
                    case "help":
                        Console.WriteLine("List of available commands:");
                        Console.WriteLine(" quit    - quit app");
                        Console.WriteLine(" flushn  - flush all notification IDs from database");
                        Console.WriteLine(" getnurl - display notification auth url");
                        Console.WriteLine(" flushcache - flush all cache from database");
                        break;
                }
                Thread.Sleep(10);
            }
        }
    }
}
