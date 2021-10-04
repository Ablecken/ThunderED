﻿using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Timers;
using ThunderED.API;
using ThunderED.Classes;
using ThunderED.Helpers;

namespace ThunderED.Modules.OnDemand
{
    public class SystemLogFeeder: AppModuleBase
    {
        public override LogCat Category => LogCat.SLF;
        private Timer _timer;

        public override async Task Initialize()
        {
            _timer?.Dispose();
            _timer = new Timer(Settings.SystemLogFeederModule.SendInterval) {AutoReset = true};
            _timer.Elapsed += async (sender, e) =>
            {
                _timer.Stop();
                try
                {
                    if (!APIHelper.IsDiscordAvailable) return;
                    if (Package.Count > 0)
                        await SendMessage().ConfigureAwait(false);
                }
                catch
                {
                    //ignore
                }
                finally
                {
                    _timer.Start();
                }
            };

            try{LogHelper.OnLogMessage -= FeedMessage;} catch {}
            LogHelper.OnLogMessage += FeedMessage;

            _timer.Start();
            await Task.CompletedTask;
        }

        private static readonly ConcurrentQueue<string> Package = new ConcurrentQueue<string>();

        private static async Task SendMessage()
        {
            if(!APIHelper.IsDiscordAvailable) return;

            var message = string.Join(Environment.NewLine, Package.ToArray());
            Package.Clear();
            if(message.Length > DiscordAPI.MAX_MSG_LENGTH)
                foreach (var line in message.SplitToLines(DiscordAPI.MAX_MSG_LENGTH))
                {
                    await APIHelper.DiscordAPI.SendMessageAsync(
                        SettingsManager.Settings.SystemLogFeederModule.DiscordChannelId, line);
                    await Task.Delay(500);
                }
            else await APIHelper.DiscordAPI.SendMessageAsync(SettingsManager.Settings.SystemLogFeederModule.DiscordChannelId, message);
        }

        public override void Cleanup()
        {
            _timer?.Stop();
            Package.Clear();
        }

        public static async Task FeedMessage(string message, LogSeverity severity)
        {
            try
            {
                if (!SettingsManager.Settings.Config.ModuleSystemLogFeeder || SettingsManager.Settings.SystemLogFeederModule.DiscordChannelId == 0) return;

                var sv = SettingsManager.Settings?.SystemLogFeederModule?.LogSeverity.ToSeverity() ?? LogSeverity.Module;
                if ((int) sv > (int) severity) return;

                if(message.Length > DiscordAPI.MAX_MSG_LENGTH)
                    foreach (var line in message.SplitToLines(DiscordAPI.MAX_MSG_LENGTH))
                        Package.Enqueue(line);
                else Package.Enqueue(message);
            }
            catch
            {
                //ignore
            }

            await Task.Delay(1);
        }
    }
}
