﻿using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ThunderED.Classes;
using ThunderED.Classes.Enums;
using ThunderED.Helpers;
using ThunderED.Json;
using ThunderED.Modules.Sub;
using ThunderED.Thd;

namespace ThunderED
{
    internal partial class Program
    {
        private static Timer _timer;
        private static NamedPipeClientStream pipe;

        private static async Task Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;

            ulong replyChannelId = 0;
            if (args.Length > 0)
                ulong.TryParse(args[0], out replyChannelId);

            // var x = string.IsNullOrWhiteSpace("");

            // var ssss = new List<JsonZKill.ZkillOnly>().Count(a => a.killmail_id == 0);
            if (!await LoadConfig())
                return;

            //load settings
            var result = await SettingsManager.Prepare();
            if (!string.IsNullOrEmpty(result))
            {
                await LogHelper.LogError(result);
                try
                {
                    Console.ReadKey();
                }
                catch
                {
                    // ignored
                }

                return;
            }

            if (replyChannelId > 0)
                LogHelper.WriteConsole($"Launch after restart");

            //restart logix
            if (SettingsManager.Settings.Config.EnableLegacyRestartLogic)
            {
                await Task.Factory.StartNew(async () =>
                {
                    if (pipe == null)
                    {
                        pipe = new NamedPipeClientStream(".", "ThunderED.Restart.Pipe", PipeDirection.In);
                        await pipe.ConnectAsync();
                    }

                    if (!pipe.IsConnected || pipe.ReadByte() == 0) return;
                    await LogHelper.LogInfo("SIGTERM received! Shutdown app...");

                    await Shutdown();
                });
            }

            APIHelper.Prepare();


            await LogHelper.LogInfo($"ThunderED v{VERSION} is running!").ConfigureAwait(false);
            //load database provider
            var rs = await SQLHelper.LoadProvider();
            if (!string.IsNullOrEmpty(rs))
            {
                await LogHelper.LogError(rs);
                try
                {
                    Console.ReadKey();
                }
                catch
                {
                    // ignored
                }

                return;
            }

            await CheckAuthIntegrity();

            await SQLHelper.InitializeBackup();

            //load language
            await LM.Load();
            //load injected settings
            await SimplifiedAuth.UpdateInjectedSettings();
            //load APIs
            if (SettingsManager.Settings.Config.DiscordGuildId != 0)
            {
                await APIHelper.DiscordAPI.Start();

                while (!APIHelper.IsDiscordAvailable)
                {
                    await Task.Delay(10);
                }

                if (APIHelper.DiscordAPI.GetGuild(SettingsManager.Settings.Config.DiscordGuildId) == null)
                {
                    await LogHelper.LogError("[CRITICAL] DiscordGuildId - Discord guild not found!");
                    try
                    {
                        Console.ReadKey();
                    }
                    catch
                    {
                        // ignored
                    }

                    return;
                }
            }

            //initiate core timer
            _timer = new Timer(TickCallback, new AutoResetEvent(true), 100, 100);

            Console.CancelKeyPress += async (sender, e) =>
            {
                e.Cancel = false;
                await Shutdown();
            };

            AppDomain.CurrentDomain.UnhandledException += async (sender, eventArgs) =>
            {
                await LogHelper.LogEx($"[UNHANDLED EXCEPTION]", (Exception)eventArgs.ExceptionObject);
                await LogHelper.LogWarning($"Consider restarting the service...");
            };

            if (replyChannelId > 0)
                await APIHelper.DiscordAPI.SendMessageAsync(replyChannelId, LM.Get("sysRestartComplete"));

            while (true)
            {

                if (!SettingsManager.Settings.Config.RunAsServiceCompatibility)
                {
                    try
                    {
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey();
                            if (key.Key == ConsoleKey.Escape)
                            {
                                await Shutdown();
                                return;
                            }
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    await Task.Delay(10);
                }


                await Task.Delay(10);
                if(_confirmClose) return;
            }
        }

        private static async Task CheckAuthIntegrity()
        {
            //check integrity
            var users = await DbHelper.GetAuthUsers();
            var groups = SettingsManager.Settings.WebAuthModule.AuthGroups.GetKeys();
            var problem = string.Join(',', users.Where(a => !groups.Contains(a.GroupName)).Select(a => a.GroupName ?? "null").Distinct());
            if (!string.IsNullOrEmpty(problem))
            {
                await LogHelper.LogWarning(
                    $"Database table auth_users contains entries with invalid groupName fields! It means that these groups hasn't been found in your config file and this can lead to problems in auth validation. Either tell those users to reauth or fix group names manually!\nUnknown groups: {problem}");
            }
        }

        private static async Task<bool> LoadConfig()
        {
            if (!File.Exists(SettingsManager.FileSettingsPath))
            {
                var defaultFile = "settings.def.json";
                if (File.Exists(defaultFile))
                {
                    File.Copy(defaultFile, Path.Combine(SettingsManager.DataDirectory, "settings.json"));
                }
                else
                {
                    await LogHelper.LogError(
                        "Please make sure you have settings.json file in bot folder! Create it and fill with correct settings.");
                    return false;
                }
            }

            return true;
        }

        private static async void CurrentDomainOnProcessExit(object sender, EventArgs e)
        {
            await Shutdown();
        }

        private static void TickCallback(object state)
        {
            _canClose = IsClosing;
            if (_canClose || IsClosing)
            {
                if (_timer != null)
                {
                    _timer?.Dispose();
                    _timer = null;
                }
                return;
            }

            TickManager.Tick(state);

            if (_canClose)
            {
                _timer?.Dispose();
                _timer = null;
            }

        }

        internal static volatile bool IsClosing = false;
        private static volatile bool _canClose = false;
        private static volatile bool _confirmClose = false;

        internal static async Task Shutdown(bool isRestart = false)
        {
            try
            {
                await LogHelper.LogInfo(isRestart ? "Server restart requested..." : $"Server shutdown requested...");
                APIHelper.StopServices();
                IsClosing = true;
                while (!_canClose || !TickManager.AllModulesReadyToClose())
                {
                    await Task.Delay(10);
                }

                await LogHelper.LogInfo(isRestart ? "Server is ready for restart" : "Server shutdown complete");
                Environment.Exit(isRestart ? 1001 : 1002);

            }
            finally
            {
                _confirmClose = true;
            }

            return;
        }

        public static async Task Restart(ulong channelId)
        {
            await Shutdown(true);
            /* try
             {
                 await LogHelper.LogInfo($"Server restart requested...");
                 APIHelper.DiscordAPI.Stop();
                 IsClosing = true;
                 while (!_canClose || !TickManager.AllModulesReadyToClose())
                 {
                     await Task.Delay(10);
                 }
 
                 var file = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
                     $"Restarter{(SettingsManager.IsLinux ? null : ".exe")}");
 
                 var start = new ProcessStartInfo
                 {
                     UseShellExecute = true,
                     CreateNoWindow = false,
                     FileName = file,
                     //Arguments = channelId.ToString(),
                     WorkingDirectory = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location)),
                 };
                 start.ArgumentList.Add(channelId.ToString());
 
                 await LogHelper.LogInfo("Starting restarter...");
                 using var proc = new Process {StartInfo = start};
                 proc.Start();
             }
             catch (Exception ex)
             {
                 await LogHelper.LogEx("Restart", ex);
 
             }
             finally
             {
                 _confirmClose = true;
             }*/
        }
    }

    public class ExternalAccess
    {
        private static Timer _timer;

        public static string GetVersion()
        {
            return Program.VERSION;
        }

        public static async Task<bool> Start()
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            if (!File.Exists(SettingsManager.FileSettingsPath))
            {
                try
                {
                    File.Copy("settings.def.json", SettingsManager.FileSettingsPath);
                    Console.WriteLine($"Default settings file has been created in {SettingsManager.FileSettingsPath}. The app will run now with default settings.");
                }
                catch
                {
                    var msg = $"Please make sure config file is present in {SettingsManager.FileSettingsPath}";
                    await LogHelper.Log(msg);
                    Console.WriteLine(msg);
                    return false;
                }
            }

            APIHelper.Prepare();

            //var xxx = await APIHelper.ESIAPI.SearchMemberEntity("ddd", "Space Traffic Control", true);


            await LogHelper.LogInfo($"ThunderED v{Program.VERSION} is running!").ConfigureAwait(false);
            //load database provider
            var rs = await SQLHelper.LoadProvider();
            if (!string.IsNullOrEmpty(rs))
            {
                await LogHelper.LogError(rs);
                try
                {
                    Console.ReadKey();
                }
                catch
                {
                    // ignored
                }

                return false;
            }

            /* await DbHelper.UpdateMiningNotification(new ThdMiningNotification
            {
                CitadelId = 1031727644630,
                Date = DateTime.Parse("03.04.2020 16:01"),
                Operator = "Ves Na",
                OreComposition = "ORE!!!"
            });*/

            await CheckAuthIntegrity();

            await SQLHelper.InitializeBackup();

            //load language
            await LM.Load();
            //load injected settings
            await SimplifiedAuth.UpdateInjectedSettings();


            //load APIs
            if (SettingsManager.Settings.Config.DiscordGuildId != 0)
            {
                await APIHelper.DiscordAPI.Start();

                while (!APIHelper.IsDiscordAvailable)
                {
                    await Task.Delay(10);
                }

                if (APIHelper.DiscordAPI.GetGuild(SettingsManager.Settings.Config.DiscordGuildId) == null)
                {
                    await LogHelper.LogError("[CRITICAL] DiscordGuildId - Discord guild not found!");
                    try
                    {
                        Console.ReadKey();
                    }
                    catch
                    {
                        // ignored
                    }

                    return false;
                }
            }

            if (!await MigrateTov2Auth())
            {
                await LogHelper.LogError($"V2 migration failed!");
                return false;
            }

            //initiate core timer
            _timer = new Timer(TickCallback, new AutoResetEvent(true), 100, 100);

            return true;
        }

        private static async Task<bool> MigrateTov2Auth()
        {
            //return true;
            try
            {
                var version = await DbHelper.GetCacheDataEntry("auth_version");
                if (version == null) //v2 go go
                {
                    await LogHelper.LogWarning($"Migrating auth to V2...");
                    var tokens = await DbHelper.GetAllTokens();
                    await LogHelper.LogWarning(
                        $"{tokens.Count} tokens found. There might be errors, it's okay.");
                    await Task.Delay(2000);

                    foreach (var token in tokens)
                    {
                        var r = await APIHelper.ESIAPI.GetAccessToken(token);
                        if (r?.Data == null)
                            continue;
                        if (r.Data.IsNoConnection || r.Data.IsNotDeserialized)
                        {
                            await LogHelper.LogError($"Connection lost! Operation aborted. Please restart.");
                            await Task.Delay(2000);
                            return false;
                        }

                        if (r.Data.IsFailed)
                        {
                            await LogHelper.LogWarning(
                                $"Failed for {token.CharacterId}|{token.Type}. Removing token due to error {r.Data.ErrorCode}. {r.Data.Message}");
                            await DbHelper.DeleteToken(token.CharacterId, token.Type);
                            continue;
                        }

                        if (string.IsNullOrEmpty(r.RefreshToken))
                        {
                            await LogHelper.LogError(
                                $"Refresh token is null, something is wrong. Aborting. Please restart or contacts devs. {r.Data.Message}");
                            await Task.Delay(2000);
                            return false;
                        }

                        token.Token = r.RefreshToken;
                        token.Scopes = APIHelper.ESIAPI.GetScopesFromToken(r.Result);

                        await DbHelper.UpdateToken(token.Token, token.CharacterId, token.Type, token.Scopes);
                    }

                    await DbHelper.UpdateCacheDataEntry("auth_version", "v2");
                    await LogHelper.LogWarning($"Migration to V2 auth has been completed!");
                }

                return true;
            }
            catch (Exception ex)
            {
                await LogHelper.LogError($"Critical failure. Abort.");
                await LogHelper.LogEx(ex);
                return false;
            }
        }

        private static async Task CheckAuthIntegrity()
        {
            //check integrity
            var users = await DbHelper.GetAuthUsers();
            var groups = SettingsManager.Settings.WebAuthModule.AuthGroups.GetKeys();
            var problem = string.Join(',', users.Where(a => !groups.Contains(a.GroupName)).Select(a => a.GroupName ?? "null").Distinct());
            if (!string.IsNullOrEmpty(problem))
            {
                await LogHelper.LogWarning(
                    $"Database table auth_users contains entries with invalid groupName fields! It means that these groups hasn't been found in your config file and this can lead to problems in auth validation. Either tell those users to reauth or fix group names manually!\nUnknown groups: {problem}");
            }
        }

        internal static volatile bool IsClosing = false;
        private static volatile bool _canClose = false;

        public static async Task Shutdown(bool isRestart = false)
        {
            try
            {
                await LogHelper.LogInfo(isRestart ? "Bot restart requested..." : $"Bot shutdown requested...");
                APIHelper.StopServices();
                IsClosing = true;
                while (!_canClose || !TickManager.AllModulesReadyToClose())
                {
                    await Task.Delay(10);
                }

                await LogHelper.LogInfo(isRestart ? "Bot is ready for restart" : "Bot shutdown complete");
            }
            catch
            { 
                // ignore
            }

            return;
        }

        private static void TickCallback(object state)
        {
            _canClose = IsClosing;
            if (_canClose || IsClosing)
            {
                if (_timer != null)
                {
                    _timer?.Dispose();
                    _timer = null;
                }
                return;
            }

            TickManager.Tick(state);

            if (_canClose)
            {
                _timer?.Dispose();
                _timer = null;
            }

        }

        public static async Task<WebQueryResult> ProcessCallback(string queryStringValue, CallbackTypeEnum type,
            string ip, WebAuthUserData data)
        {
            return await WebServerModule.ProcessWebCallbacks(queryStringValue, type, ip, data);
        }
    }
}
