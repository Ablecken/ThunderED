﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Discord;
using ThunderED.Classes;
using ThunderED.Helpers;
using ThunderED.Json;
using ThunderED.Modules.OnDemand;
using ThunderED.Modules.Sub;

namespace ThunderED.Modules
{
    public class MailModule: AppModuleBase
    {
        public override LogCat Category => LogCat.Mail;

        private readonly int _checkInterval;
        private DateTime _lastCheckTime = DateTime.MinValue;

        public MailModule()
        {
            _checkInterval = SettingsManager.GetInt("mailModule", "checkIntervalInMinutes");
            if (_checkInterval == 0)
                _checkInterval = 1;
            WebServerModule.ModuleConnectors.Add(Reason, OnAuthRequest);
        }

        private async Task<bool> OnAuthRequest(HttpListenerRequestEventArgs context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                var extPort = SettingsManager.Get("webServerModule", "webExternalPort");
                var port = SettingsManager.Get("webServerModule", "webListenPort");

                if (request.HttpMethod == HttpMethod.Get.ToString())
                {
                    if (request.Url.LocalPath == "/callback.php" || request.Url.LocalPath == $"{extPort}/callback.php" || request.Url.LocalPath == $"{port}/callback.php")
                    {
                        var clientID = SettingsManager.Get("auth", "ccpAppClientId");
                        var secret = SettingsManager.Get("auth", "ccpAppSecret");

                        var prms = request.Url.Query.TrimStart('?').Split('&');
                        var code = prms[0].Split('=')[1];
                        var state = prms.Length > 1 ? prms[1].Split('=')[1] : null;

                        if (state != "12") return false;

                        //state = 12 && have code
                        var result = await WebAuthModule.GetCHaracterIdFromCode(code, clientID, secret);
                        if (result == null)
                        {
                            //TODO invalid auth
                            await response.RedirectAsync(new Uri(WebServerModule.GetWebSiteUrl()));
                            return true;
                        }

                        await SQLiteHelper.SQLiteDataInsertOrUpdateTokens(null, result[0], result[1]);
                        response.Headers.ContentEncoding.Add("utf-8");
                        response.Headers.ContentType.Add("text/html;charset=utf-8");
                        await response.WriteContentAsync(File.ReadAllText(SettingsManager.FileTemplateMailAuthSuccess)
                            .Replace("{header}", "authTemplateHeader")
                            .Replace("{body}", LM.Get("mailAuthSuccessHeader"))
                            .Replace("{body2}", LM.Get("mailAuthSuccessBody"))
                        );
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(ex.Message, ex, Category);
            }

            return false;
        }

        public override async Task Run(object prm)
        {
            if (IsRunning) return;
            IsRunning = true;
            try
            {
                if((DateTime.Now - _lastCheckTime).TotalMinutes < _checkInterval) return;
                _lastCheckTime = DateTime.Now;

                foreach(var group in SettingsManager.GetSubList("mailModule", "authGroups"))
                {
                    var id = group.GetChildren().FirstOrDefault(a=> a.Key == "id")?.Value;
                    var terms = group.GetChildren().FirstOrDefault(a=> a.Key == "labels")?.GetChildren().Select(a=> a.Value).ToList();
                    var chString = group.GetChildren().FirstOrDefault(a=> a.Key == "channel")?.Value;
                    if(string.IsNullOrEmpty(chString))
                        continue;
                    var channel = Convert.ToUInt64(chString);

                    if (string.IsNullOrEmpty(id) || terms == null || terms.Count == 0) continue;

                    var rToken = await SQLiteHelper.SQLiteDataQuery<string>("refreshTokens", "mail", "id", Convert.ToInt32(id));
                    if (string.IsNullOrEmpty(rToken))
                    {
                        continue;
                    }

                    var token = await APIHelper.ESIAPI.RefreshToken(rToken, SettingsManager.Get("auth", "ccpAppClientId"), SettingsManager.Get("auth", "ccpAppSecret"));
                    if (string.IsNullOrEmpty(rToken))
                    {
                        await LogHelper.LogWarning("Unable to get correct token using refresh token! Refresh token might be expired!", Category);
                        continue;
                    }

                    var lastMailId = await SQLiteHelper.SQLiteDataQuery<int>("mail", "mailId", "id", id);
                    var prevMailId = lastMailId;
                    var labelsData= await APIHelper.ESIAPI.GetMailLabels(Reason, id, token);
                    var searchLabels = labelsData.labels.Where(a => a.name.ToLower() != "sent" && a.name.ToLower() != "received");
                    if (labelsData == null || terms.Count == 0)
                    {
                        await LogHelper.LogWarning($"Mail feed for user {id} has no labels or user has no labels in-game!", Category);
                        continue;
                    }
                    var labelIds = labelsData.labels.Where(a=> terms.Contains(a.name)).Select(a => a.label_id).ToList();
                    var mails = await APIHelper.ESIAPI.GetMailHeaders(Reason, id, token, lastMailId, labelIds);

                    foreach (var mailHeader in mails)
                    {
                        if(mailHeader.mail_id <= lastMailId) continue;

                        var mail = await APIHelper.ESIAPI.GetMail(Reason, id, token, mailHeader.mail_id);
                        var labelNames = string.Join(",", mail.labels.Select(a => searchLabels.FirstOrDefault(l => l.label_id == a)?.name)).Trim(',');
                        lastMailId = mailHeader.mail_id;

                        await SendMailNotification(channel, mail, labelNames);

                    }
                    if(prevMailId != lastMailId)
                        await SQLiteHelper.SQLiteDataInsertOrUpdate("mail", new Dictionary<string, object>{{"id", id}, {"mailId", lastMailId}});
                }
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(ex.Message, ex, Category);
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task SendMailNotification(ulong channel, JsonClasses.Mail mail, string labelNames)
        {
            var sender = await APIHelper.ESIAPI.GetCharacterData(Reason, mail.@from);

            var embed = new EmbedBuilder()
                .WithDescription($"Labels:  {labelNames}")
                .WithThumbnailUrl(SettingsManager.Get("resources", "imgMail"))
                .AddField(mail.subject, mail.body.Replace("<br>", Environment.NewLine))
                .WithFooter(DateTime.Parse(mail.timestamp).ToString(SettingsManager.Get("config", "timeFormat")));
            var ch = APIHelper.DiscordAPI.GetChannel(channel);
            await APIHelper.DiscordAPI.SendMessageAsync(ch, $"@everyone Mail from {sender?.name}!", embed.Build());
        }
    }
}
