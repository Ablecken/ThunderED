﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Newtonsoft.Json.Linq;
using ThunderED.Classes;
using ThunderED.Classes.Entities;
using ThunderED.Helpers;
using ThunderED.Json;
using ThunderED.Json.ZKill;
using ThunderED.Modules.Sub;

namespace ThunderED.Modules.OnDemand
{
    public partial class LiveKillFeedModule: AppModuleBase
    {
        private static readonly ConcurrentDictionary<string, long> LastPostedDictionary = new ConcurrentDictionary<string, long>();

        protected readonly Dictionary<string, Dictionary<string, Dictionary<string, List<long>>>> ParsedVictimsLists = new Dictionary<string, Dictionary<string, Dictionary<string, List<long>>>>();
        protected readonly Dictionary<string, Dictionary<string, Dictionary<string, List<long>>>> ParsedAttackersLists = new Dictionary<string, Dictionary<string, Dictionary<string, List<long>>>>();
        protected readonly Dictionary<string, Dictionary<string, Dictionary<string, List<long>>>> ParsedLocationLists = new Dictionary<string, Dictionary<string, Dictionary<string, List<long>>>>();
        protected readonly Dictionary<string, Dictionary<string, Dictionary<string, List<long>>>> ParsedShipsLists = new Dictionary<string, Dictionary<string, Dictionary<string, List<long>>>>();

        public override LogCat Category => LogCat.KillFeed;

        public LiveKillFeedModule()
        {
            LogHelper.LogModule("Initializing LiveKillFeed module...", Category).GetAwaiter().GetResult();
            ZKillLiveFeedModule.Queryables.Add(ProcessKill);
        }

        public override async Task Initialize()
        {
            //check for group name dupes
            var dupes = SettingsManager.Settings.LiveKillFeedModule.Groups.Keys.GetDupes();
            if(dupes.Any())
                await LogHelper.LogWarning($"Module has groups with identical names: {string.Join(',', dupes)}\n Please set unique group names to avoid inconsistency during KM checks.", Category);

            foreach (var (key, value) in Settings.LiveKillFeedModule.Groups)
            {
                dupes = value.Filters.Keys.GetDupes();
                if(dupes.Any())
                    await LogHelper.LogWarning($"Group {key} has filters with identical names: {string.Join(',', dupes)}\n Please set unique filter names to avoid inconsistency during KM checks.", Category);
            }

            //check for Discord channels
            foreach (var (key, value) in Settings.LiveKillFeedModule.Groups)
            {
                if(!value.DiscordChannels.Any() && value.Filters.Values.Any(a=> !a.DiscordChannels.Any()))
                    await LogHelper.LogWarning($"Module group {key} has no Discord channels specified or has filters without channels!", Category);
            }

            //check filters
            var groupNames = Settings.LiveKillFeedModule.Groups.Where(a => !a.Value.Filters.Any()).Select(a => a.Key);
            if(groupNames.Any())
                await LogHelper.LogWarning($"Groups {string.Join(',', groupNames)} has no filters!", Category);

            groupNames = Settings.LiveKillFeedModule.Groups.Where(a => !a.Value.FeedPvpKills && !a.Value.FeedPveKills || !a.Value.FeedAwoxKills && !a.Value.FeedNotAwoxKills || !a.Value.FeedSoloKills && !a.Value.FeedGroupKills).Select(a => a.Key);
            if(groupNames.Any())
                await LogHelper.LogWarning($"Groups {string.Join(',', groupNames)} has mutually exclusive Feed params!", Category);

            //check templates
            foreach (var templateFile in Settings.LiveKillFeedModule.Groups.Where(a=> !string.IsNullOrWhiteSpace(a.Value.MessageTemplateFileName)).Select(a=> a.Value.MessageTemplateFileName))
            {
                if(!File.Exists(Path.Combine(SettingsManager.DataDirectory, "Templates", "Messages", templateFile)))
                    await LogHelper.LogWarning($"Specified template file {templateFile} not found!", Category);
            }

            //parse data
            foreach (var (key, value) in Settings.LiveKillFeedModule.Groups)
            {
                var aGroupDic = new Dictionary<string, Dictionary<string, List<long>>>();
                var vGroupDic = new Dictionary<string, Dictionary<string, List<long>>>();
                var lGroupDic = new Dictionary<string, Dictionary<string, List<long>>>();
                var sGroupDic = new Dictionary<string, Dictionary<string, List<long>>>();
                foreach (var (fKey, fValue) in value.Filters)
                {
                    var aData = await ParseMemberDataArray(fValue.AttackerEntities);
                    aGroupDic.Add(fKey, aData);
                    var vData = await ParseMemberDataArray(fValue.VictimEntities);
                    vGroupDic.Add(fKey, vData);
                    var lData = await ParseLocationDataArray(fValue.LocationEntities);
                    lGroupDic.Add(fKey, lData);
                    var sData = await ParseTypeDataArray(fValue.ShipEntities);
                    sGroupDic.Add(fKey, sData);
                }
                ParsedAttackersLists.Add(key, aGroupDic);
                ParsedVictimsLists.Add(key, vGroupDic);
                ParsedLocationLists.Add(key, lGroupDic);
                ParsedShipsLists.Add(key, sGroupDic);
            }

        }

        private async Task ProcessKill(JsonZKill.Killmail kill)
        {
            try
            {
                var hasBeenPosted = false;
                foreach (var (groupName, group) in Settings.LiveKillFeedModule.Groups)
                {
                    if (Settings.ZKBSettingsModule.AvoidDupesAcrossAllFeeds && ZKillLiveFeedModule.IsInSharedPool(kill.killmail_id))
                        return;

                    if (hasBeenPosted && Settings.LiveKillFeedModule.StopOnFirstGroupMatch) break;

                    if(UpdateLastPosted(groupName, kill.killmail_id)) continue;

                    var isPveKill = kill.zkb.npc;
                    var isPvpKill = !kill.zkb.npc;

                    if (!@group.FeedPveKills && isPveKill || !@group.FeedPvpKills && isPvpKill) continue;
                    if(!group.FeedAwoxKills && kill.zkb.awox) continue;
                    if(!group.FeedNotAwoxKills && !kill.zkb.awox) continue;
                    if(!group.FeedSoloKills && kill.zkb.solo) continue;
                    if(!group.FeedGroupKills && !kill.zkb.solo) continue;

                    foreach (var (filterName, filter) in group.Filters)
                    {
                        var isInclusive = filter.Inclusive;
                        var isLoss = false;
                        var isPassed = false;
                        var isFirstMatchOnly = !filter.AllMustMatch;
                        var isAllowedToFeed = false;

                        #region Person checks
                        //character check
                        var fChars = GetTier2CharacterIds(ParsedAttackersLists, groupName, filterName);
                        if (fChars.Any())
                        {
                            var attackers = kill.attackers.Select(a => a.character_id);
                            if(isInclusive && !fChars.ContainsAnyFromList(attackers))
                                continue;
                            if(!isInclusive && fChars.ContainsAnyFromList(attackers))
                                continue;
                            if (isInclusive)
                            {
                                isPassed = true;
                                isAllowedToFeed = isFirstMatchOnly;
                            }
                        }

                        if (!isPassed && !isAllowedToFeed)
                        {
                            fChars = GetTier2CharacterIds(ParsedVictimsLists, groupName, filterName);
                            if (fChars.Any())
                            {
                                if (isInclusive && !fChars.Contains(kill.victim.character_id))
                                    continue;
                                if (!isInclusive && fChars.Contains(kill.victim.character_id))
                                    continue;
                                isLoss = true;
                                if (isInclusive)
                                {
                                    isPassed = true;
                                    isAllowedToFeed = isFirstMatchOnly;
                                }
                            }
                        }

                        //corp check
                        if (!isPassed && !isAllowedToFeed)
                        {
                            fChars = GetTier2CorporationIds(ParsedAttackersLists, groupName, filterName);
                            if (fChars.Any())
                            {
                                var attackers = kill.attackers.Select(a => a.corporation_id);
                                if (isInclusive && !fChars.ContainsAnyFromList(attackers))
                                    continue;
                                if (!isInclusive && fChars.ContainsAnyFromList(attackers))
                                    continue;
                                if (isInclusive)
                                {
                                    isPassed = true;
                                    isAllowedToFeed = isFirstMatchOnly;
                                }
                            }
                        }

                        if (!isPassed && !isAllowedToFeed)
                        {
                            fChars = GetTier2CorporationIds(ParsedVictimsLists, groupName, filterName);
                            if (fChars.Any())
                            {
                                if (isInclusive && !fChars.Contains(kill.victim.corporation_id))
                                    continue;
                                if (!isInclusive && fChars.Contains(kill.victim.corporation_id))
                                    continue;
                                isLoss = true;
                                if (isInclusive)
                                {
                                    isPassed = true;
                                    isAllowedToFeed = isFirstMatchOnly;
                                }
                            }
                        }

                        //alliance check
                        if (!isPassed && !isAllowedToFeed)
                        {
                            fChars = GetTier2AllianceIds(ParsedAttackersLists, groupName, filterName);
                            if (fChars.Any())
                            {
                                var attackers = kill.attackers.Where(a=> a.alliance_id > 0).Select(a => a.alliance_id);
                                if (isInclusive && !fChars.ContainsAnyFromList(attackers))
                                    continue;
                                if (!isInclusive && fChars.ContainsAnyFromList(attackers))
                                    continue;
                                if (isInclusive)
                                {
                                    isPassed = true;
                                    isAllowedToFeed = isFirstMatchOnly;
                                }
                            }
                        }

                        if (!isPassed && !isAllowedToFeed)
                        {
                            fChars = GetTier2AllianceIds(ParsedVictimsLists, groupName, filterName);
                            if (fChars.Any() && kill.victim.alliance_id > 0)
                            {
                                if (isInclusive && !fChars.Contains(kill.victim.alliance_id))
                                    continue;
                                if (!isInclusive && fChars.Contains(kill.victim.alliance_id))
                                    continue;
                                isLoss = true;
                                if (isInclusive)
                                {
                                    isPassed = true;
                                    isAllowedToFeed = isFirstMatchOnly;
                                }
                            }
                        }
                        isPassed = false;
                        #endregion

                        //value checks
                        if (!isAllowedToFeed)
                        {
                            if (isLoss && filter.MinimumLossValue >= kill.zkb.totalValue) continue;
                            if (filter.MaximumLossValue > 0 && isLoss && filter.MaximumLossValue <= kill.zkb.totalValue) continue;
                            if (!isLoss && filter.MinimumKillValue >= kill.zkb.totalValue) continue;
                            if (filter.MaximumKillValue > 0 && !isLoss && filter.MaximumKillValue <= kill.zkb.totalValue) continue;

                            if (isFirstMatchOnly && (filter.MinimumKillValue > 0 || filter.MinimumLossValue > 0 || filter.MaximumLossValue > 0 || filter.MaximumKillValue > 0))
                                isAllowedToFeed = true;
                        }

                        #region Location checks

                        var rSystem = await APIHelper.ESIAPI.GetSystemData(Reason, kill.solar_system_id);
                        if (filter.Radius == 0 && !isAllowedToFeed)
                        {
                            if (!CheckLocation(rSystem, kill, isInclusive, groupName, filterName)) continue;
                            if (isInclusive && isFirstMatchOnly)
                                isAllowedToFeed = true;
                        }

                        #endregion

                        #region Type checks

                        var types = GetTier2TypeIds(ParsedShipsLists, groupName, filterName);
                        if (types.Any() && !isAllowedToFeed)
                        {
                            if(isInclusive && !types.Contains(kill.victim.ship_type_id)) continue;
                            if(!isInclusive && types.Contains(kill.victim.ship_type_id)) continue;
                            if (isInclusive && isFirstMatchOnly)
                                isAllowedToFeed = true;
                        }
                        #endregion

                        var discordChannels = filter.DiscordChannels.Any() ? filter.DiscordChannels : group.DiscordChannels;

                        if (filter.Radius > 0)
                        {
                            #region Process radius

                            var msgType = MessageTemplateType.KillMailRadius;
                            var isDone = false;
                            foreach (var radiusSystemId in GetTier2SystemIds(ParsedLocationLists, groupName, filterName))
                            {
                                if (await ProcessLocation(radiusSystemId, kill, group, filter, groupName))
                                {
                                    isDone = true;
                                    hasBeenPosted = true;
                                    if (Settings.ZKBSettingsModule.AvoidDupesAcrossAllFeeds)
                                        ZKillLiveFeedModule.UpdateSharedIdPool(kill.killmail_id);
                                    await LogHelper.LogInfo($"Posting     {(isLoss ? "RLoss" : "RKill")}: {kill.killmail_id}  Value: {kill.zkb.totalValue:n0} ISK", Category);

                                    break;
                                }
                            }

                            if( isDone && group.StopOnFirstFilterMatch) break; //goto next group

                            #endregion
                        }
                        else
                        {
                            if (group.FeedUrlsOnly)
                                foreach (var channel in discordChannels)
                                    await APIHelper.DiscordAPI.SendMessageAsync(channel, kill.zkb.url);
                            else
                            {
                                var hasTemplate = !string.IsNullOrWhiteSpace(group.MessageTemplateFileName);
                                var msgColor = isLoss ? new Color(0xD00000) : new Color(0x00FF00);
                                var msgType = !hasTemplate ? MessageTemplateType.KillMailGeneral : MessageTemplateType.Custom;
                                var km = new KillDataEntry();

                                if (await km.Refresh(Reason, kill))
                                {
                                    km.dic["{isLoss}"] = isLoss ? "true" : "false";
                                    var isDone = hasTemplate
                                        ? await TemplateHelper.PostTemplatedMessage(group.MessageTemplateFileName, km.dic, discordChannels, group.ShowGroupName ? groupName : " ")
                                        : await TemplateHelper.PostTemplatedMessage(msgType, km.dic, discordChannels,
                                            group.ShowGroupName ? groupName : " ");
                                    if(!isDone)
                                        await APIHelper.DiscordAPI.SendEmbedKillMessage(discordChannels, msgColor, km, null);
                                }
                            }

                            if (Settings.ZKBSettingsModule.AvoidDupesAcrossAllFeeds)
                                ZKillLiveFeedModule.UpdateSharedIdPool(kill.killmail_id);
                            await LogHelper.LogInfo($"Posting     {(isLoss ? "Loss" : "Kill")}: {kill.killmail_id}  Value: {kill.zkb.totalValue:n0} ISK", Category);

                            hasBeenPosted = true;
                            if(group.StopOnFirstFilterMatch) break; //goto next group
                        }

                        continue; //goto next filter
                    }
                }

                /* if (_lastPosted == kill.killmail_id) return;
 
                 var postedGlobalBigKill = false;
                 var bigKillGlobalValue = SettingsManager.Settings.LiveKillFeedModule.BigKill;
                 var bigKillGlobalChan = SettingsManager.Settings.LiveKillFeedModule.BigKillChannel;
                 var isNPCKill = kill.zkb.npc;
 
                 var km = new KillDataEntry();
 
                 foreach (var groupPair in Settings.LiveKillFeedModule.GroupsConfig)
                 {
                     if(Settings.ZKBSettingsModule.AvoidDupesAcrossAllFeeds && ZKillLiveFeedModule.IsInSharedPool(kill.killmail_id))
                         return;
 
                     var group = groupPair.Value;
                     if ((!group.FeedPveKills && isNPCKill) || (!group.FeedPvpKills && !isNPCKill)) continue;
 
                     var minimumValue = group.MinimumValue;
                     var minimumLossValue = group.MinimumLossValue;
                     var allianceIdList = group.AllianceID;
                     var corpIdList = group.CorpID;
                     var bigKillValue = group.BigKillValue;
                     var c = group.DiscordChannel;
                     var sendBigToGeneral = group.BigKillSendToGeneralToo;
                     var bigKillChannel = group.BigKillChannel;
                     var discordGroupName = groupPair.Key;
                     var isUrlOnly = group.FeedUrlsOnly;
 
                     if (c == 0)
                     {
                         await LogHelper.LogWarning($"Group {groupPair.Key} has no 'discordChannel' specified! Kills will be skipped.", Category);
                         continue;
                     }
 
                     var value = kill.zkb.totalValue;
 
 
                     if (bigKillGlobalChan != 0 && bigKillGlobalValue != 0 && value >= bigKillGlobalValue && !postedGlobalBigKill)
                     {
                         postedGlobalBigKill = true;
 
                         if (isUrlOnly)
                             await APIHelper.DiscordAPI.SendMessageAsync(bigKillGlobalChan, kill.zkb.url);
                         else
                         {                            
                             if (await km.Refresh(Reason, kill) && !await TemplateHelper.PostTemplatedMessage(MessageTemplateType.KillMailBig, km.dic, bigKillGlobalChan, groupPair.Value.ShowGroupName ? discordGroupName : " "))
                             {
                                 await APIHelper.DiscordAPI.SendEmbedKillMessage(bigKillGlobalChan, new Color(0xFA2FF4), km, null);
                             }
                         }
 
                         if (Settings.ZKBSettingsModule.AvoidDupesAcrossAllFeeds)
                             ZKillLiveFeedModule.UpdateSharedIdPool(kill.killmail_id);
                         await LogHelper.LogInfo($"Posting Global Big Kill: {kill.killmail_id}  Value: {value:n0} ISK", Category);
                     }
 
                     if (!allianceIdList.Any() && !corpIdList.Any())
                     {
                         if (value >= minimumValue)
                         {
                             if (isUrlOnly)
                                 await APIHelper.DiscordAPI.SendMessageAsync(c, kill.zkb.url);
                             else if (await km.Refresh(Reason, kill) && !await TemplateHelper.PostTemplatedMessage(MessageTemplateType.KillMailGeneral, km.dic, c, groupPair.Value.ShowGroupName ? discordGroupName : " "))
                             {
                                 await APIHelper.DiscordAPI.SendEmbedKillMessage(c, new Color(0x00FF00), km, null);
                             }
 
                             if (Settings.ZKBSettingsModule.AvoidDupesAcrossAllFeeds)
                                 ZKillLiveFeedModule.UpdateSharedIdPool(kill.killmail_id);
                             await LogHelper.LogInfo($"Posting Global Kills: {kill.killmail_id}  Value: {value:n0} ISK", Category);
                         }
                     }
                     else
                     {
                         //ally & corp 
 
                         //Losses
                         //Big
                         if (bigKillChannel != 0 && bigKillValue != 0 && value >= bigKillValue)
                         {
                             if (kill.victim.alliance_id != 0 && allianceIdList.Contains(kill.victim.alliance_id) || corpIdList.Contains(kill.victim.corporation_id))
                             {
                                 if (isUrlOnly)
                                 {
                                     await APIHelper.DiscordAPI.SendMessageAsync(bigKillChannel, kill.zkb.url);
                                     if (sendBigToGeneral && c != bigKillChannel)
                                         await APIHelper.DiscordAPI.SendMessageAsync(c, kill.zkb.url);
                                 }
                                 else if (await km.Refresh(Reason, kill))
                                 {
                                     km.dic["{isLoss}"] = "true";
                                     try
                                     {
                                         if (!await TemplateHelper.PostTemplatedMessage(MessageTemplateType.KillMailBig, km.dic, bigKillChannel, 
                                             groupPair.Value.ShowGroupName ? discordGroupName : " "))
                                         {
                                             await APIHelper.DiscordAPI.SendEmbedKillMessage(bigKillChannel, new Color(0xD00000), km, null,
                                                 groupPair.Value.ShowGroupName ? discordGroupName : " ");
                                             if (sendBigToGeneral && c != bigKillChannel)
                                                 if (!await TemplateHelper.PostTemplatedMessage(MessageTemplateType.KillMailBig, km.dic, c, 
                                                     groupPair.Value.ShowGroupName ? discordGroupName : " "))
                                                     await APIHelper.DiscordAPI.SendEmbedKillMessage(c, new Color(0xD00000), km, null,
                                                         groupPair.Value.ShowGroupName ? discordGroupName : " ");
                                         }
                                     }
                                     finally
                                     {
                                         km.dic.Remove("{isLoss}");
                                     }
                                 }
 
                                 if (Settings.ZKBSettingsModule.AvoidDupesAcrossAllFeeds)
                                     ZKillLiveFeedModule.UpdateSharedIdPool(kill.killmail_id);
                                 await LogHelper.LogInfo($"Posting     Big Loss: {kill.killmail_id}  Value: {value:n0} ISK", Category);
                                 continue;
                             }
                         }
 
                         //Common
                         if (minimumLossValue == 0 || minimumLossValue <= value)
                         {
                             if (kill.victim.alliance_id != 0 && allianceIdList.Contains(kill.victim.alliance_id) || corpIdList.Contains(kill.victim.corporation_id))
                             {
                                 if (isUrlOnly)
                                     await APIHelper.DiscordAPI.SendMessageAsync(c, kill.zkb.url);
                                 else if (await km.Refresh(Reason, kill))
                                 {
                                     km.dic["{isLoss}"] = "true";
                                     try
                                     {
                                         if (!await TemplateHelper.PostTemplatedMessage(MessageTemplateType.KillMailGeneral, km.dic, c, 
                                             groupPair.Value.ShowGroupName ? discordGroupName : " "))
                                         {
                                             await APIHelper.DiscordAPI.SendEmbedKillMessage(c, new Color(0xFF0000), km, null,
                                                 groupPair.Value.ShowGroupName ? discordGroupName : " ");
                                         }
                                     }
                                     finally
                                     {
                                         km.dic.Remove("{isLoss}");
                                     }
                                 }
 
                                 if (Settings.ZKBSettingsModule.AvoidDupesAcrossAllFeeds)
                                     ZKillLiveFeedModule.UpdateSharedIdPool(kill.killmail_id);
                                 await LogHelper.LogInfo($"Posting         Loss: {kill.killmail_id}  Value: {value:n0} ISK", Category);
 
                                 continue;
                             }
                         }
 
                         //Kills
                         foreach (var attacker in kill.attackers.ToList())
                         {
                             if (bigKillChannel != 0 && bigKillValue != 0 && value >= bigKillValue && !isNPCKill)
                             {
                                 if ((attacker.alliance_id != 0 && allianceIdList.Contains(attacker.alliance_id)) ||
                                     (!allianceIdList.Any() && corpIdList.Contains(attacker.corporation_id)))
                                 {
                                     if (isUrlOnly)
                                     {
                                         await APIHelper.DiscordAPI.SendMessageAsync(bigKillChannel, kill.zkb.url);
                                         if (sendBigToGeneral && c != bigKillChannel)
                                             await APIHelper.DiscordAPI.SendMessageAsync(c, kill.zkb.url);
                                     }
                                     else if (await km.Refresh(Reason, kill))
                                     {
                                         km.dic["{isLoss}"] = "false";
                                         try
                                         {
                                             if (!await TemplateHelper.PostTemplatedMessage(MessageTemplateType.KillMailBig, km.dic, bigKillChannel, 
                                                 groupPair.Value.ShowGroupName ? discordGroupName : " "))
                                             {
                                                 await APIHelper.DiscordAPI.SendEmbedKillMessage(bigKillChannel, new Color(0x00D000), km, null,
                                                     groupPair.Value.ShowGroupName ? discordGroupName : " ");
                                                 if (sendBigToGeneral && c != bigKillChannel)
                                                 {
                                                     if (!await TemplateHelper.PostTemplatedMessage(MessageTemplateType.KillMailBig, km.dic, c, 
                                                         groupPair.Value.ShowGroupName ? discordGroupName : " "))
                                                         await APIHelper.DiscordAPI.SendEmbedKillMessage(c, new Color(0x00D000), km, null,
                                                             groupPair.Value.ShowGroupName ? discordGroupName : " ");
                                                 }
 
                                                 if (Settings.ZKBSettingsModule.AvoidDupesAcrossAllFeeds)
                                                     ZKillLiveFeedModule.UpdateSharedIdPool(kill.killmail_id);
 
                                                 await LogHelper.LogInfo($"Posting     Big Kill: {kill.killmail_id}  Value: {value:#,##0} ISK", Category);
                                             }
                                         }
                                         finally
                                         {
                                             km.dic.Remove("{isLoss}");
                                         }
                                     }
 
                                     break;
                                 }
                             }
                             else if (!isNPCKill && attacker.alliance_id != 0 && allianceIdList.Any() && allianceIdList.Contains(attacker.alliance_id) ||
                                      !isNPCKill && !allianceIdList.Any() && corpIdList.Contains(attacker.corporation_id))
                             {
                                 if (isUrlOnly)
                                     await APIHelper.DiscordAPI.SendMessageAsync(c, kill.zkb.url);
                                 else if (await km.Refresh(Reason, kill))
                                 {
                                     km.dic["{isLoss}"] = "false";
                                     try
                                     {
                                         if (!await TemplateHelper.PostTemplatedMessage(MessageTemplateType.KillMailGeneral, km.dic, c, 
                                             groupPair.Value.ShowGroupName ? discordGroupName : " "))
                                         {
                                             await APIHelper.DiscordAPI.SendEmbedKillMessage(c, new Color(0x00FF00), km, null,
                                                 groupPair.Value.ShowGroupName ? discordGroupName : " ");
                                         }
                                     }
                                     finally
                                     {
                                         km.dic.Remove("{isLoss}");
                                     }
                                 }
 
                                 if (Settings.ZKBSettingsModule.AvoidDupesAcrossAllFeeds)
                                     ZKillLiveFeedModule.UpdateSharedIdPool(kill.killmail_id);
                                 await LogHelper.LogInfo($"Posting         Kill: {kill.killmail_id}  Value: {value:#,##0} ISK", Category);
                                 break;
                             }
                         }
                     }
                 }
 
                 _lastPosted = kill.killmail_id;*/
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(ex.Message, ex, Category);
                await LogHelper.LogWarning($"Error processing kill ID {kill?.killmail_id} ! Msg: {ex.Message}", Category);
            }
        }

        private async Task<bool> ProcessLocation(long radiusId, JsonZKill.Killmail kill, KillFeedGroup @group, KillMailFilter filter, string groupName)
        {
            var mode = RadiusMode.Range;
            var isUrlOnly = group.FeedUrlsOnly;
            var radius = filter.Radius;

            if (radiusId <= 0)
            {
                await LogHelper.LogError("Radius feed must have systemId!", Category);
                return false;
            }

            var km = new KillDataEntry();
            await km.Refresh(Reason, kill);

            var routeLength = 0;
            JsonClasses.ConstellationData rConst = null;
            JsonClasses.RegionData rRegion;
            var srcSystem = mode == RadiusMode.Range ? await APIHelper.ESIAPI.GetSystemData(Reason, radiusId) : null;

            if (radiusId == km.systemId)
            {
                //right there
                rConst = km.rSystem.constellation_id == 0 ? null : await APIHelper.ESIAPI.GetConstellationData(Reason, km.rSystem.constellation_id);
                rRegion = rConst?.region_id == null ||  rConst.region_id == 0 ? null : await APIHelper.ESIAPI.GetRegionData(Reason, rConst.region_id);
            }
            else
            {
                switch (mode)
                {
                    case RadiusMode.Range:

                        if (radius == 0 || km.isUnreachableSystem || (srcSystem?.IsUnreachable() ?? true)) //Thera WH Abyss
                            return false;

                        var route = await APIHelper.ESIAPI.GetRawRoute(Reason, radiusId, km.systemId);
                        if (string.IsNullOrEmpty(route)) return false;
                        JArray data;
                        try
                        {
                            data = JArray.Parse(route);
                        }
                        catch (Exception ex)
                        {
                            await LogHelper.LogEx("Route parse: " + ex.Message, ex, Category);
                            return false;
                        }

                        routeLength = data.Count - 1;
                        //not in range
                        if (routeLength > radius) return false;

                        var rSystemName = radiusId > 0 ? srcSystem?.name ?? LM.Get("Unknown") : LM.Get("Unknown");
                        km.dic.Add("{radiusSystem}", rSystemName);
                        km.dic.Add("{radiusJumps}", routeLength.ToString());

                        break;
                    case RadiusMode.Constellation:
                        if (km.rSystem.constellation_id != radiusId) return false;
                        break;
                    case RadiusMode.Region:
                        if (km.rSystem.DB_RegionId > 0)
                        {
                            if (km.rSystem.DB_RegionId != radiusId) return false;
                        }
                        else
                        {
                            rConst = await APIHelper.ESIAPI.GetConstellationData(Reason, km.rSystem.constellation_id);
                            if (rConst == null || rConst.region_id != radiusId) return false;
                        }

                        break;
                }
                rConst = rConst ?? await APIHelper.ESIAPI.GetConstellationData(Reason, km.rSystem.constellation_id);
                rRegion = await APIHelper.ESIAPI.GetRegionData(Reason, rConst.region_id);
            }

            //var rSystemName = rSystem?.name ?? LM.Get("Unknown");

            km.dic.Add("{isRangeMode}", (mode == RadiusMode.Range).ToString());
            km.dic.Add("{isConstMode}", (mode == RadiusMode.Constellation).ToString());
            km.dic.Add("{isRegionMode}", (mode == RadiusMode.Region).ToString());
            km.dic.AddOrUpdateEx("{constName}", rConst?.name);
            km.dic.AddOrUpdateEx("{regionName}", rRegion?.name);

            var template = isUrlOnly ? null : await TemplateHelper.GetTemplatedMessage(MessageTemplateType.KillMailRadius, km.dic);
            var channels = filter.DiscordChannels.Any() ? filter.DiscordChannels : group.DiscordChannels;
            foreach (var channel in channels)
            {
                if (isUrlOnly)
                    await APIHelper.DiscordAPI.SendMessageAsync(channel, kill.zkb.url);
                else
                {
                    if (template != null)
                        await APIHelper.DiscordAPI.SendMessageAsync(channel, group.ShowGroupName ? groupName : " ", template).ConfigureAwait(false);
                    else
                    {
                        var jumpsText = routeLength > 0 ? $"{routeLength} {LM.Get("From")} {srcSystem?.name}" : $"{LM.Get("InSmall")} {km.sysName} ({km.systemSecurityStatus})";
                        await APIHelper.DiscordAPI.SendEmbedKillMessage(new List<ulong> {channel}, new Color(0x989898), km, string.IsNullOrEmpty(jumpsText) ? "-" : jumpsText, group.ShowGroupName ? groupName : " ");
                    }
                }
            }

            return true;
        }

        private bool CheckLocation(JsonClasses.SystemName rSystem, JsonZKill.Killmail kill, bool isInclusive, string groupName, string filterName)
        {
            var isPassed = false;
            if (rSystem == null)
            {
                LogHelper.LogError($"System not found: {kill.solar_system_id}!", Category).GetAwaiter().GetResult();
                return false;
            }

            //System
            var fLocs = GetTier2SystemIds(ParsedLocationLists, groupName, filterName);
            if (fLocs.Any())
            {
                if (isInclusive && !fLocs.Contains(kill.solar_system_id))
                    return false;
                if (!isInclusive && fLocs.Contains(kill.solar_system_id))
                    return false;
                if (isInclusive) isPassed = true;
            }

            //Constellation
            if (!isPassed)
            {
                fLocs = GetTier2ConstellationIds(ParsedLocationLists, groupName, filterName);
                if (fLocs.Any())
                {
                    if (isInclusive && !fLocs.Contains(rSystem.constellation_id))
                        return false;
                    if (!isInclusive && fLocs.Contains(rSystem.constellation_id))
                        return false;
                    if (isInclusive) isPassed = true;
                }
            }

            //Region
            if (!isPassed)
            {
                fLocs = GetTier2RegionIds(ParsedLocationLists, groupName, filterName);
                if (fLocs.Any() && rSystem.DB_RegionId.HasValue)
                {
                    if (isInclusive && !fLocs.Contains(rSystem.DB_RegionId.Value))
                        return false;
                    if (!isInclusive && fLocs.Contains(rSystem.DB_RegionId.Value))
                        return false;
                    if (isInclusive) isPassed = true;
                }
            }

            return true;
        }

        private bool UpdateLastPosted(string groupName, long id)
        {
            if (!LastPostedDictionary.ContainsKey(groupName))
                LastPostedDictionary.AddOrUpdateEx(groupName, 0);

            if (LastPostedDictionary[groupName] == id) return true;
            LastPostedDictionary[groupName] = id;
            return false;
        }

        private enum RadiusMode
        {
            Range,
            Constellation,
            Region
        }
    }
}
