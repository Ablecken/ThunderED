﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Matrix.Xmpp.MessageArchiving;
using ThunderED.Classes;
using ThunderED.Classes.Entities;
using ThunderED.Classes.Enums;
using ThunderED.Helpers;
using ThunderED.Json;

namespace ThunderED.Modules
{
    public partial class HRMModule
    {
        public async Task<HRMAccessFilter> WebGetAccess(long characterId)
        {
            return await CheckAccess(characterId);
        }

        public async Task<List<WebUserItem>> WebGetUsers(UserStatusEnum userType, HRMAccessFilter filter)
        {
            IOrderedEnumerable<AuthUserEntity> list;
            switch (userType)
            {
                //case UserStatusEnum.Initial:
                //break;
                case UserStatusEnum.Awaiting:
                    list = (await SQLHelper.GetAuthUsers())
                        .Where(a => a.IsPending && IsValidUserForInteraction(filter, a))
                        .OrderBy(a => a.Data.CharacterName);
                    break;
                case UserStatusEnum.Authed:
                    list = (await SQLHelper.GetAuthUsers((int)userType))
                        .Where(a => !a.IsAltChar && IsValidUserForInteraction(filter, a))
                        .OrderBy(a => a.Data.CharacterName);
                    break;
                case UserStatusEnum.Dumped:
                    list = (await SQLHelper.GetAuthUsers((int)userType))
                        .Where(a => IsValidUserForInteraction(filter, a)).OrderBy(a => a.Data.CharacterName);
                    break;
                case UserStatusEnum.Spying:
                    list = (await SQLHelper.GetAuthUsers((int)userType))
                        .Where(a => IsValidUserForInteraction(filter, a)).OrderBy(a => a.Data.CharacterName);
                    break;
                case UserStatusEnum.Alts:
                    list = (await SQLHelper.GetAuthUsers((int)userType))
                        .Where(a => a.IsAltChar && IsValidUserForInteraction(filter, a))
                        .OrderBy(a => a.Data.CharacterName);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(userType), userType, null);
            }


            return list.Select(a => new WebUserItem
            {
                Id = a.CharacterId,
                CharacterName = a.Data.CharacterName,
                CorporationName = a.Data.CorporationName,
                AllianceName = a.Data.AllianceName,
                CorporationTicker = a.Data.CorporationTicker,
                AllianceTicker = a.Data.AllianceTicker,
                RegDate = a.CreateDate,
                IconUrl = $"https://imageserver.eveonline.com/Character/{a.CharacterId}_64.jpg"
            }).ToList();

        }

        public async Task<bool> WebDeleteUser(WebUserItem order)
        {
            var sUser = await SQLHelper.GetAuthUserByCharacterId(order.Id);
            if (sUser == null)
            {
                await LogHelper.LogError($"User {order.Id} not found for delete op");
                return false;
            }

            if (Settings.HRMModule.UseDumpForMembers && !sUser.IsDumped)
            {
                sUser.SetStateDumpster();
                await LogHelper.LogInfo(
                    $"HR moving character {sUser.Data.CharacterName} to dumpster...");
                await SQLHelper.SaveAuthUser(sUser);
            }
            else
            {
                await LogHelper.LogInfo(
                    $"HR deleting character {sUser.Data.CharacterName} auth...");
                await SQLHelper.DeleteAuthDataByCharId(order.Id, true);
            }

            if (sUser.DiscordId > 0)
                await WebAuthModule.UpdateUserRoles(sUser.DiscordId,
                    Settings.WebAuthModule.ExemptDiscordRoles,
                    Settings.WebAuthModule.AuthCheckIgnoreRoles, true);
            return true;
        }

        public async Task<List<JsonClasses.CorporationHistoryEntry>> WebGenerateCorpHistory(long charId)
        {
            var history = (await APIHelper.ESIAPI.GetCharCorpHistory(Reason, charId))
                ?.OrderByDescending(a => a.record_id).ToList();
            if (history == null || history.Count == 0) return null;

            JsonClasses.CorporationHistoryEntry last = null;
            foreach (var entry in history)
            {
                var corp = await APIHelper.ESIAPI.GetCorporationData(Reason, entry.corporation_id);
                entry.CorpName = corp.name;
                entry.IsNpcCorp = corp.creator_id == 1;
                if (last != null)
                {
                    entry.Days = (int)(last.Date - entry.Date).TotalDays;
                }

                entry.CorpTicker = corp.ticker;
                last = entry;
            }

            var l = history.FirstOrDefault();
            if (l != null)
                l.Days = (int)(DateTime.UtcNow - l.Date).TotalDays;
            return history.Where(a=> a.Days > 0).ToList();
        }

        public async Task<List<WebMailHeader>> WebGetMailHeaders(long id, string token)
        {
            var mailHeaders = (await APIHelper.ESIAPI.GetMailHeaders(Reason, id, token, 0, null))?.Result;
            if (mailHeaders == null)
                return null;
            var list = new List<WebMailHeader>();
            foreach (var h in mailHeaders)
            {
                var from = await APIHelper.ESIAPI.GetCharacterData(Reason, h.@from);
                var rcp = await MailModule.GetRecepientNames(Reason, h.recipients, id, token);

                list.Add(new WebMailHeader
                {
                    MailId = h.mail_id,
                    FromName = from?.name ?? LM.Get("Unknown"),
                    ToName = rcp.Length > 0 ? rcp : LM.Get("Unknown"),
                    Subject = h.subject,
                    Date = h.Date
                });
            }

            return list;
        }

        public async Task<List<WebContract>> WebGetCharContracts(long id, string inspectToken)
        {
            var contracts = (await APIHelper.ESIAPI.GetCharacterContracts(Reason, id, inspectToken, null)).Result;
            if (contracts == null) return null;

            var list = new List<WebContract>();
            foreach (var entry in contracts)
            {
                var fromPlace = entry.issuer_id != 0 ? "character" : "corporation";
                var toPlace = !entry.for_corporation ? "character" : "corporation";
                var fromId = entry.issuer_id != 0 ? entry.issuer_id : entry.issuer_corporation_id;
                var toId = entry.acceptor_id;
                var from = entry.issuer_id != 0
                    ? (await APIHelper.ESIAPI.GetCharacterData(Reason, entry.issuer_id))?.name
                    : (await APIHelper.ESIAPI.GetCorporationData(Reason, entry.issuer_corporation_id))?.name;
                var to = entry.for_corporation
                    ? (await APIHelper.ESIAPI.GetCorporationData(Reason, entry.acceptor_id))?.name
                    : (await APIHelper.ESIAPI.GetCharacterData(Reason, entry.acceptor_id))?.name;

                var ch = await APIHelper.ESIAPI.GetCharacterData(Reason, id);
                var itemList = await ContractNotificationsModule.GetContractItemsString(Reason, entry.for_corporation, ch.corporation_id, id, entry.contract_id, inspectToken);

                list.Add(new WebContract
                {
                    Id = entry.contract_id,
                    Type = entry.type,
                    From = from,
                    To = to,
                    FromLink = $"https://zkillboard.com/{fromPlace}/{fromId}/",
                    ToLink = toId > 0 ? $"https://zkillboard.com/{toPlace}/{toId}/" : "-",
                    Status = entry.status,
                    CompleteDate = entry.DateCompleted?.ToString(Settings.Config.ShortTimeFormat) ?? LM.Get("hrmContractInProgress"),
                    Title = entry.title,
                    IncludedItems = itemList[0],
                    AskingItems = itemList[1]
                });
            }

            return list;
        }

        public async Task<List<WebContact>> WebGetCharContacts(long id, string inspectToken, long hrId)
        {
            var contacts = (await APIHelper.ESIAPI.GetCharacterContacts(Reason, id, inspectToken)).Result.OrderByDescending(a => a.standing).ToList();
            List<JsonClasses.Contact> hrContacts = null;

            if (hrId > 0)
            {
                var hrUserInfo = await SQLHelper.GetAuthUserByCharacterId(hrId);
                if (hrUserInfo != null && SettingsManager.HasCharContactsScope(hrUserInfo.Data.PermissionsList))
                {
                    var hrToken = (await APIHelper.ESIAPI.RefreshToken(hrUserInfo.RefreshToken, Settings.WebServerModule.CcpAppClientId, Settings.WebServerModule.CcpAppSecret
                        , $"From {Category} | Char ID: {hrUserInfo.CharacterId} | Char name: {hrUserInfo.Data.CharacterName}"))?.Result;
                    if (!string.IsNullOrEmpty(hrToken))
                    {
                        hrContacts = (await APIHelper.ESIAPI.GetCharacterContacts(Reason, hrId, hrToken)).Result;
                    }
                }
            }

            var list = new List<WebContact>();
            foreach (var entry in contacts)
            {
                string name;
                var color = "transparent";
                var fontColor = "black";
                switch (entry.contact_type)
                {
                    case "character":
                        var c = await APIHelper.ESIAPI.GetCharacterData(Reason, entry.contact_id);
                        name = c?.name;
                        break;
                    case "corporation":
                        var co = await APIHelper.ESIAPI.GetCorporationData(Reason, entry.contact_id);
                        name = co?.name;
                        break;
                    case "alliance":
                        var al = await APIHelper.ESIAPI.GetAllianceData(Reason, entry.contact_id);
                        name = al?.name;
                        break;
                    case "faction":
                        var f = await APIHelper.ESIAPI.GetFactionData(Reason, entry.contact_id);
                        name = f?.name;
                        break;
                    default:
                        name = null;
                        break;
                }

                var hrc = hrContacts?.FirstOrDefault(a => a.contact_type == entry.contact_type && a.contact_id == entry.contact_id)?.standing;
                var hrStand = hrc.HasValue ? hrc.Value.ToString() : "-";
                if (hrc.HasValue)
                {
                    switch (hrc.Value)
                    {
                        case var s when s > 0 && s <= 5:
                            color = "#2B68C6";
                            fontColor = "white";
                            break;
                        case var s when s > 5 && s <= 10:
                            color = "#041B5D";
                            fontColor = "white";
                            break;
                        case var s when s < 0 && s >= -5:
                            color = "#BF4908";
                            fontColor = "white";
                            break;
                        case var s when s < -5 && s >= -10:
                            color = "#8D0808";
                            fontColor = "white";
                            break;
                    }
                }

                list.Add(new WebContact
                {
                    Name = name,
                    Type = entry.contact_type,
                    Blocked = LM.Get(entry.is_blocked? "Yes" : "No"),
                    Stand = entry.standing.ToString("N1"),
                    HrStand = hrStand,
                    ForegroundColor = fontColor,
                    BackgroundColor = color,
                    ZkbLink = $"https://zkillboard.com/{entry.contact_type}/{entry.contact_id}/"
                });
            }

            return list;
        }
    }

    public class WebContact
    {
        public string Name;
        public string HrStand;
        public string ForegroundColor;
        public string BackgroundColor;
        public string Type;
        public string Blocked;
        public string Stand;
        public string ZkbLink;
    }

    public class WebContract
    {
        public long Id;
        public string Type;
        public string FromLink;
        public string ToLink;
        public string Status;
        public string CompleteDate;
        public string Title;
        public string IncludedItems;
        public string AskingItems;
        public string From;
        public string To;
    }

    public class WebMailHeader
    {
        public string Subject;
        public string ToName;
        public string FromName;
        public long MailId;
        public DateTime Date;
    }
}
