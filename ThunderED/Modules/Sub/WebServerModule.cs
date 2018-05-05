﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ThunderED.Classes;
using ThunderED.Helpers;

namespace ThunderED.Modules.Sub
{
    public class WebServerModule: AppModuleBase
    {
        private static System.Net.Http.HttpListener _listener;
        public override LogCat Category => LogCat.WebServer;

        public static Dictionary<string, Func<HttpListenerRequestEventArgs, Task>> ModuleConnectors { get; } = new Dictionary<string, Func<HttpListenerRequestEventArgs, Task>>();

        public WebServerModule()
        {
            ModuleConnectors.Clear();
        }

        public override async Task Run(object prm)
        {
            if(!SettingsManager.GetBool("config", "moduleWebServer")) return;

            if (_listener == null || !_listener.IsListening)
            {
                await LogHelper.LogInfo("Starting Web Server", Category);
                _listener?.Dispose();
                var port = SettingsManager.GetInt("webServerModule", "webListenPort");
                var extPort = SettingsManager.Get("webServerModule", "webExternalPort");
                var ip = SettingsManager.Get("webServerModule", "webListenIP");
                _listener = new System.Net.Http.HttpListener(IPAddress.Parse(ip), port);
                _listener.Request += async (sender, context) =>
                {
                    try
                    {
                        var request = context.Request;
                        var response = context.Response;

                        if (request.Url.LocalPath == "/" || request.Url.LocalPath == $"{port}/" || request.Url.LocalPath == $"{extPort}/")
                        {
                            var extIp = SettingsManager.Get("webServerModule", "webExternalIP");
                            var authUrl =  $"http://{extIp}:{extPort}/auth.php";
                            var authNurl = GetAuthNotifyURL();

                            response.Headers.ContentEncoding.Add("utf-8");
                            response.Headers.ContentType.Add("text/html;charset=utf-8");
                            var text = File.ReadAllText(SettingsManager.FileTemplateMain).Replace("{authUrl}", authUrl)
                                .Replace("{authNotifyUrl}", authNurl).Replace("{header}", LM.Get("authTemplateHeader"))
                                .Replace("{authButtonDiscordText}", LM.Get("authButtonDiscordText")).Replace("{authButtonNotifyText}", LM.Get("authButtonNotifyText"))
                                .Replace("{authButtonTimersText}", LM.Get("authButtonTimersText"));
                            text = text.Replace("{disableWebAuth}", !SettingsManager.GetBool("config", "moduleAuthWeb") ? "disabled" : "");
                            text = text.Replace("{disableWebNotify}", !SettingsManager.GetBool("config", "moduleNotificationFeed") ? "disabled" : "");
                            text = text.Replace("{disableWebTimers}", !SettingsManager.GetBool("config", "moduleTimers") ? "disabled" : "");
                  
                            await response.WriteContentAsync(text);
                            return;
                        }

                        foreach (var method in ModuleConnectors.Values)
                        {
                            await method(context);
                        }
                    }
                    finally
                    {
                        context.Response.Close();
                    }
                };
                _listener.Start();
            }
        }

        public static string GetWebSiteUrl()
        {
            var extIp = SettingsManager.Get("webServerModule", "webExternalIP");
            var extPort = SettingsManager.Get("webServerModule", "webExternalPort");
            return  $"http://{extIp}:{extPort}";
        }

        
        public static string GetAuthNotifyURL()
        {
            var clientID = SettingsManager.Get("auth","ccpAppClientId");
            var extIp = SettingsManager.Get("webServerModule", "webExternalIP");
            var extPort = SettingsManager.Get("webServerModule", "webExternalPort");
            var callbackurl =  $"http://{extIp}:{extPort}/callback.php";
            return $"https://login.eveonline.com/oauth/authorize/?response_type=code&redirect_uri={callbackurl}&client_id={clientID}&scope=esi-characters.read_notifications.v1+esi-universe.read_structures.v1+esi-characters.read_chat_channels.v1&state=9";
        }
    }
}
