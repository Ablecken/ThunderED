﻿using System;
using System.Threading.Tasks;
using System.Web;
using Discord;
using Discord.Commands;
using ThunderED.Classes;
using ThunderED.Helpers;
using ThunderED.Json;
using ThunderED.Json.ZKill;

namespace ThunderED.Modules.Static
{
    public class CharSearchModule: AppModuleBase
    {
        public override LogCat Category => LogCat.CharSearch;

        internal static async Task SearchCharacter(ICommandContext context, string name)
        {
            var channel = context.Channel;

            var charSearch = await APIHelper.ESIAPI.SearchCharacterId(LogCat.CharSearch.ToString(), name);
            if (charSearch == null)
            {
                await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("charNotFound"), true);
                return;
            }

            var characterId = charSearch.character[0];

            var characterData = await APIHelper.ESIAPI.GetCharacterData(LogCat.CharSearch.ToString(), characterId, true);
            if (characterData == null)
            {
                await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("charNotFound"), true);
                return;
            }

            var corporationData = await APIHelper.ESIAPI.GetCorporationData(LogCat.CharSearch.ToString(), characterData.corporation_id);

            var zkillContent = await APIHelper.ZKillAPI.GetCharacterKills(characterId);
            var characterStats = await APIHelper.ZKillAPI.GetCharacterStats(characterId);
            var zkillLosses = await APIHelper.ZKillAPI.GetCharacterLosses(characterId);

            var zkillLast = zkillContent.Count > 0 ? zkillContent[0] : null;
            var zLosslast = zkillLosses.Count > 0 ? zkillLosses[0] : new JsonClasses.ESIKill();
            var km = zkillLast == null ? zLosslast : (zLosslast == null ? zkillLast : (zLosslast.killmail_time > zkillLast.killmail_time ? zLosslast : zkillLast));

            JsonClasses.SystemName systemData = null;
            var lastShipType = LM.Get("Unknown");

            if (km != null)
            {
                systemData = await APIHelper.ESIAPI.GetSystemData("Default", km.solar_system_id);
          
                if (km.victim != null && km.victim.character_id == characterId)
                    lastShipType = km.victim.ship_type_id.ToString();
                else if (km.victim != null)
                {
                    foreach (var attacker in km.attackers)
                    {
                        if (attacker.character_id == characterId)
                        {
                            lastShipType = attacker.ship_type_id.ToString();
                            break;
                        }
                    }
                }
            }

            var lastShip = lastShipType == LM.Get("Unknown") ? null : await APIHelper.ESIAPI.GetTypeId("Default", lastShipType);
            var lastSeenTime = km?.killmail_time.ToString(SettingsManager.Settings.Config.ShortTimeFormat) ?? LM.Get("Unknown");
            var allianceData = await APIHelper.ESIAPI.GetAllianceData("Default", characterData.alliance_id);

            var alliance = allianceData?.name ?? LM.Get("None");
            var allianceTicker = allianceData != null ? $"[{allianceData?.ticker}]" : "";
            var lastSeenSystem = systemData?.name ?? LM.Get("None");
            var lastSeenShip = lastShip?.name ?? LM.Get("None");
            var dangerous = characterStats.dangerRatio > 75 ? LM.Get("Dangerous") : LM.Get("Snuggly");
            var gang = characterStats.gangRatio > 70 ? LM.Get("fleetChance") : LM.Get("soloChance");

            var cynoCount = 0;
            var covertCount = 0;

            foreach (var kill in zkillLosses)
            {
                if (kill.victim.character_id == characterId)
                {
                    foreach (var item in kill.victim.items)
                    {
                        if (item.item_type_id == 21096)
                            cynoCount++;
                        if (item.item_type_id == 28646)
                            covertCount++;
                    }
                }
            }

            var text1 = characterStats.dangerRatio == 0 ? LM.Get("Unavailable") : HelpersAndExtensions.GenerateUnicodePercentage(characterStats.dangerRatio);
            var text2 = characterStats.gangRatio == 0 ? LM.Get("Unavailable") : HelpersAndExtensions.GenerateUnicodePercentage(characterStats.gangRatio);

            var builder = new EmbedBuilder()
                .WithDescription(
                    $"[zKillboard](https://zkillboard.com/character/{characterId}/) / [EVEWho](https://evewho.com/pilot/{HttpUtility.UrlEncode(characterData.name)})")
                .WithColor(new Color(0x4286F4))
                .WithThumbnailUrl($"https://image.eveonline.com/Character/{characterId}_64.jpg")
                .WithAuthor(author =>
                {
                    author
                        .WithName($"{characterData.name}");
                })
                .AddField(LM.Get("Additionaly"), "\u200b")
                .AddField($"{LM.Get("Corporation")}:", $"{corporationData.name}[{corporationData.ticker}]", true)
                .AddField($"{LM.Get("Alliance")}:", $"{alliance}{allianceTicker} ", true)
                .AddField($"{LM.Get("HasBeenSeen")}:", $"{lastSeenSystem}", true)
                .AddField($"{LM.Get("OnShip")}:", $"{lastSeenShip}", true)
                .AddField($"{LM.Get("Seen")}:", $"{lastSeenTime}", true)
                .AddField("\u200b", "\u200b")
                .AddField(LM.Get("CommonCyno"), $"{cynoCount}", true)
                .AddField(LM.Get("CovertCyno"), $"{covertCount}", true)
                .AddField(LM.Get("Dangerous"), $"{text1}{Environment.NewLine}{Environment.NewLine}**{dangerous} {characterStats.dangerRatio}%**", true)
                .AddField(LM.Get("FleetChance2"), $"{text2}{Environment.NewLine}{Environment.NewLine}**{characterStats.gangRatio}% {gang}**", true);

            var embed = builder.Build();

            await APIHelper.DiscordAPI.SendMessageAsync(channel, "", embed).ConfigureAwait(false);
            await LogHelper.LogInfo($"Sending {context.Message.Author} Character Info Request", LogCat.CharSearch).ConfigureAwait(false);

            await Task.CompletedTask;
        }
    }
}
