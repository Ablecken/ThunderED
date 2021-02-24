﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Text;
using System.Threading.Tasks;
using Matrix.Xmpp.MessageArchiving;
using ThunderED.API;
using ThunderED.Classes;
using ThunderED.Classes.Enums;
using ThunderED.Helpers;
using ThunderED.Json;
using ThunderED.Modules.Sub;
using ThunderED.Thd;

namespace ThunderED.Modules
{
    public class MiningScheduleModule: AppModuleBase
    {
        public override LogCat Category => LogCat.MiningSchedule;

        protected Dictionary<string, List<long>> ParsedAuthAccessMembersLists = new Dictionary<string,List<long>>();
        protected Dictionary<string, List<long>> ParsedExtrViewAccessMembersLists = new Dictionary<string, List<long>>();
        protected Dictionary<string, List<long>> ParsedLedgerViewAccessMembersLists = new Dictionary<string, List<long>>();
        
        protected Dictionary<string, Dictionary<string, List<long>>> ParsedExtrEntitiesLists = new Dictionary<string, Dictionary<string, List<long>>>();
        protected Dictionary<string, Dictionary<string, List<long>>> ParsedLedgerEntitiesLists = new Dictionary<string, Dictionary<string, List<long>>>();
        private static readonly object UpdateLock = new object();

        public override async Task Initialize()
        {
            if (WebServerModule.WebModuleConnectors.ContainsKey(Reason))
                WebServerModule.WebModuleConnectors.Remove(Reason);
            WebServerModule.WebModuleConnectors.Add(Reason, ProcessRequest);

            ParsedAuthAccessMembersLists.Clear();
            ParsedExtrViewAccessMembersLists.Clear();
            ParsedLedgerViewAccessMembersLists.Clear();

            //auth access
            ParsedAuthAccessMembersLists = await ParseMixedDataArray(Settings.MiningScheduleModule.AuthAccessEntities, MixedParseModeEnum.Member);

            //extractions
            //view access
            ParsedExtrViewAccessMembersLists = await ParseMixedDataArray(Settings.MiningScheduleModule.Extractions.ViewAccessEntities, MixedParseModeEnum.Member);
            
            lock (UpdateLock)
                ParsedExtrEntitiesLists.Clear();
            //parse data
            foreach (var (key, value) in Settings.MiningScheduleModule.Extractions.ComplexAccess)
            {
                var aData = await ParseMemberDataArray(value.Entities
                    .Where(a => (a is long i && i != 0) || (a is string s && s != string.Empty)).ToList());
                lock (UpdateLock)
                    ParsedExtrEntitiesLists.Add(key, aData);
            }

            //ledger
            //view access
            ParsedLedgerViewAccessMembersLists = await ParseMixedDataArray(Settings.MiningScheduleModule.Ledger.ViewAccessEntities, MixedParseModeEnum.Member);
            lock (UpdateLock)
                ParsedLedgerEntitiesLists.Clear();
            //parse data
            foreach (var (key, value) in Settings.MiningScheduleModule.Ledger.ComplexAccess)
            {
                var aData = await ParseMemberDataArray(value.Entities
                    .Where(a => (a is long i && i != 0) || (a is string s && s != string.Empty)).ToList());
                lock (UpdateLock)
                    ParsedLedgerEntitiesLists.Add(key, aData);
            }

        }

        public override Task Run(object prm)
        {
            return base.Run(prm);
        }

        public async Task<WebQueryResult> ProcessRequest(string query, CallbackTypeEnum type, string ip, WebAuthUserData data)
        {
            if (!Settings.Config.ModuleMiningSchedule)
                return WebQueryResult.False;

            try
            {
                RunningRequestCount++;
                if (!query.Contains("&state=ms"))
                    return WebQueryResult.False;

                var prms = query.TrimStart('?').Split('&');
                var code = prms[0].Split('=')[1];

                var result = await WebAuthModule.GetCharacterIdFromCode(code,
                    Settings.WebServerModule.CcpAppClientId, Settings.WebServerModule.CcpAppSecret);
                if (result == null)
                    return WebQueryResult.EsiFailure;

                var characterId = result[0];
                var numericCharId = Convert.ToInt64(characterId);

                if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(result[1]))
                {
                    await LogHelper.LogWarning("Bad or outdated feed request!", Category);
                    var r = WebQueryResult.BadRequestToSystemAuth;
                    r.Message1 = LM.Get("authTokenBodyFail");
                    r.Message2 = LM.Get("authTokenBadRequest");
                    return r;
                }

                var rChar = await APIHelper.ESIAPI.GetCharacterData(Reason, characterId, true);
                if(rChar == null)
                    return WebQueryResult.EsiFailure;

                if (!HasAuthAccess(rChar))
                {
                    await LogHelper.LogWarning($"Unauthorized feed request from {characterId}", Category);
                    var r = WebQueryResult.BadRequestToSystemAuth;
                    r.Message1 = LM.Get("authTokenBodyFail");
                    return r;
                }

                await DbHelper.UpdateToken(result[1], numericCharId, TokenEnum.MiningSchedule);
                await LogHelper.LogInfo($"Feed added for character: {characterId}", Category);

                var res = WebQueryResult.FeedAuthSuccess;
                res.Message1 = LM.Get("msAuthSuccessHeader");
                res.Message2 = LM.Get("msAuthSuccessBody");
                return res;

            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(ex.Message, ex, Category);
                return WebQueryResult.False;
            }
            finally
            {
                RunningRequestCount--;
            }

        }

        public async Task<WebMiningExtractionResult> GetExtractions(MiningComplexAccessGroup accessGroup, WebAuthUserData user)
        {
            var result = new WebMiningExtractionResult();
            var tokens = await DbHelper.GetTokens(TokenEnum.MiningSchedule);

            var processedCorps = new List<long>();

            foreach (var token in tokens)
            {
                var r = await APIHelper.ESIAPI.RefreshToken(token.Token, Settings.WebServerModule.CcpAppClientId,
                    Settings.WebServerModule.CcpAppSecret);
                if (r == null || r.Data.IsFailed)
                {
                    await LogHelper.LogWarning($"Failed to refresh mining token from {token.CharacterId}");
                    if (r?.Data.IsNotValid ?? false)
                    {
                        await DbHelper.DeleteToken(token.CharacterId, TokenEnum.MiningSchedule);
                        await LogHelper.LogWarning($"Mining token from {token.CharacterId} is no longer valid and will be deleted!");
                    }
                    continue;
                }

                var rChar = await APIHelper.ESIAPI.GetCharacterData(Reason, token.CharacterId, true);
                if (rChar == null)
                {
                    await LogHelper.LogWarning($"Failed to refresh character {token.CharacterId}");
                    continue;
                }
                var corp = await APIHelper.ESIAPI.GetCorporationData(Reason, rChar.corporation_id);
                if (corp == null)
                {
                    await LogHelper.LogWarning($"Failed to refresh corp {rChar.corporation_id}");
                    continue;
                }

                //check access to only own corp
                if(accessGroup != null && accessGroup.CanManageOwnCorporation && rChar.corporation_id != user.CorpId)
                    continue;

                if(processedCorps.Contains(rChar.corporation_id))
                    continue;

                processedCorps.Add(rChar.corporation_id);
                result.Corporations.Add(corp.name);

                var extr = await APIHelper.ESIAPI.GetCorpMiningExtractions(Reason, rChar.corporation_id, r.Result);
                var innerList = new List<WebMiningExtraction>();

                foreach (var e in extr)
                {
                    var structure =
                        await APIHelper.ESIAPI.GetUniverseStructureData(Reason, e.structure_id, r.Result);

                    //check structures from access list
                    if (accessGroup != null && !accessGroup.CanManageOwnCorporation && accessGroup.StructureNames.Any())
                    {
                        if (structure == null)
                        {
                            await LogHelper.LogWarning(
                                $"Has accessGroup for user {user.Name} and can't identify structure. It will be skipped.", Category);
                            continue;
                        }
                        if(!accessGroup.StructureNames.Any(a=> structure.name.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
                            continue;
                    }

                    //var moon = await APIHelper.ESIAPI.GetMoon(Reason, e.moon_id);
                    var item = new WebMiningExtraction
                    {
                        ExtractionStartTime = e.extraction_start_time.ToEveTime(),
                        ChunkArrivalTime = e.chunk_arrival_time.ToEveTime(),
                        NaturalDecayTime = e.natural_decay_time.ToEveTime(),
                        //MoonId = e.moon_id,
                        StructureId = e.structure_id,
                        StructureName = structure?.name ?? LM.Get("Unknown"),
                        //MoonName = moon?.name ?? LM.Get("Unknown"),
                        CorporationName = corp.name,
                        
                    };
                    item.Remains = item.ChunkArrivalTime.GetRemains(LM.Get("timerRemains"));

                    var notify = await DbHelper.GetMiningNotification(e.structure_id, item.NaturalDecayTime);
                    if (notify != null)
                    {
                        item.OreComposition = notify.OreComposition;
                        item.Operator = notify.Operator;
                    }

                    innerList.Add(item);
                }
                result.Extractions.AddRange(innerList);
            }

            result.Corporations = result.Corporations.OrderBy(a => a).ToList();
            result.Extractions = result.Extractions.OrderBy(a => a.ChunkArrivalTime).ToList();
            return result;
        }

        public class WebMiningExtractionResult
        {
            public List<WebMiningExtraction> Extractions { get; set; } = new List<WebMiningExtraction>();
            public List<string> Corporations { get; set; } = new List<string>();
        }

        public static async Task UpdateNotificationFromFeed(string composition, long structureId, DateTime date, string op)
        {
            if(!SettingsManager.Settings.Config.ModuleMiningSchedule) return;

            await DbHelper.UpdateMiningNotification(new ThdMiningNotification
            {
                CitadelId = structureId, Operator = op, OreComposition = composition, Date = date
            });

        }

        public async Task<List<WebMiningLedger>> GetLedgers(MiningComplexAccessGroup accessGroup, WebAuthUserData user)
        {
            var tokens = await DbHelper.GetTokens(TokenEnum.MiningSchedule);

            var processedCorps = new List<long>();
            var list = new List<WebMiningLedger>();

            foreach (var token in tokens)
            {
                var r = await APIHelper.ESIAPI.RefreshToken(token.Token, Settings.WebServerModule.CcpAppClientId,
                    Settings.WebServerModule.CcpAppSecret);
                if (r == null || r.Data.IsFailed)
                {
                    await LogHelper.LogWarning($"Failed to refresh mining token from {token.CharacterId}", Category);
                    if (r?.Data.IsNotValid ?? false)
                    {
                        await DbHelper.DeleteToken(token.CharacterId, TokenEnum.MiningSchedule);
                        await LogHelper.LogWarning($"Mining token from {token.CharacterId} is no longer valid and will be deleted!", Category);
                    }
                    continue;
                }

                var rChar = await APIHelper.ESIAPI.GetCharacterData(Reason, token.CharacterId, true);
                if (rChar == null)
                {
                    await LogHelper.LogWarning($"Failed to refresh character {token.CharacterId}", Category);
                    continue;
                }
                var corp = await APIHelper.ESIAPI.GetCorporationData(Reason, rChar.corporation_id);
                if (corp == null)
                {
                    await LogHelper.LogWarning($"Failed to refresh corp {rChar.corporation_id}", Category);
                    continue;
                }
                if (processedCorps.Contains(rChar.corporation_id))
                    continue;

                //check access to only own corp
                if (accessGroup != null && accessGroup.CanManageOwnCorporation && rChar.corporation_id != user.CorpId)
                    continue;

                processedCorps.Add(rChar.corporation_id);

                var ledgers = await APIHelper.ESIAPI.GetCorpMiningLedgers(Reason, rChar.corporation_id, r.Result);
                var innerList = new List<WebMiningLedger>();

                foreach (var ledger in ledgers)
                {
                    var structure =
                        await APIHelper.ESIAPI.GetUniverseStructureData(Reason, ledger.observer_id, r.Result);

                    //check structures from access list
                    if (accessGroup != null && !accessGroup.CanManageOwnCorporation && accessGroup.StructureNames.Any())
                    {
                        if (structure == null)
                        {
                            await LogHelper.LogWarning(
                                $"Has accessGroup for user {user.Name} and can't identify structure. It will be skipped.", Category);
                            continue;
                        }
                        if (!accessGroup.StructureNames.Any(a => structure.name.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
                            continue;
                    }

                    var item = new WebMiningLedger
                    {
                        CorporationName = corp.name,
                        CorporationId = rChar.corporation_id,
                        StructureName = structure?.name ?? LM.Get("Unknown"),
                        StructureId = ledger.observer_id,
                        Date = ledger.last_updated,
                        FeederId = token.CharacterId
                    };

                    innerList.Add(item);
                }
                list.AddRange(innerList);
            }

            return list;
        }

        #region Extractions Access checks
        /// <summary>
        /// Admin access to observer all extractions operations
        /// </summary>
        public static bool HasObserverExtrViewAccess(in JsonClasses.CharacterData data)
        {
            if (data == null) return false;
            var module = TickManager.GetModule<MiningScheduleModule>();
            return HasViewAccess(data.character_id, data.corporation_id, data.alliance_id ?? 0, module.ParsedExtrViewAccessMembersLists);
        }
        /// <summary>
        /// Admin access to observer all extractions operations
        /// </summary>
        public static bool HasObserverExtrViewAccess(WebAuthUserData data)
        {
            if (data == null) return false;
            var module = TickManager.GetModule<MiningScheduleModule>();
            return HasViewAccess(data.Id, data.CorpId, data.AllianceId, module.ParsedExtrViewAccessMembersLists);
        }

        public static bool HasExtrEditAccess(in JsonClasses.CharacterData data, out string groupName)
        {
            groupName = null;
            if (data == null) return false;
            var module = TickManager.GetModule<MiningScheduleModule>();
            return HasViewAccess(data.character_id, data.corporation_id, data.alliance_id ?? 0, module.ParsedExtrEntitiesLists, out groupName);
        }

        public static bool HasExtrEditAccess(WebAuthUserData data, out string groupName)
        {
            groupName = null;
            if (data == null) return false;
            var module = TickManager.GetModule<MiningScheduleModule>();
            return HasViewAccess(data.Id, data.CorpId, data.AllianceId, module.ParsedExtrEntitiesLists, out groupName);
        }

        /// <summary>
        /// Global access flag to extractions operations
        /// </summary>
        public static bool HasCommonExtrViewAccess(in JsonClasses.CharacterData data)
        {
            if (data == null) return false;
            return HasObserverExtrViewAccess(data) || HasExtrEditAccess(data, out _);
        }
        /// <summary>
        /// Global access flag to extractions operations
        /// </summary>
        public static bool HasCommonExtrViewAccess(in WebAuthUserData data)
        {
            if (data == null) return false;
            return HasObserverExtrViewAccess(data) || HasExtrEditAccess(data, out _);
        }
        #endregion

        #region Ledger Access checks

        /// <summary>
        /// Admin access to observer all ledger operations
        /// </summary>
        public static bool HasObserverLedgerViewAccess(in JsonClasses.CharacterData data)
        {
            if (data == null) return false;
            var module = TickManager.GetModule<MiningScheduleModule>();
            return HasViewAccess(data.character_id, data.corporation_id, data.alliance_id ?? 0, module.ParsedLedgerViewAccessMembersLists);
        }

        /// <summary>
        /// Admin access to observer all ledger operations
        /// </summary>
        public static bool HasObserverLedgerViewAccess(WebAuthUserData data)
        {
            if (data == null) return false;
            var module = TickManager.GetModule<MiningScheduleModule>();
            return HasViewAccess(data.Id, data.CorpId, data.AllianceId, module.ParsedLedgerViewAccessMembersLists);
        }

        public static bool HasLedgerEditAccess(in JsonClasses.CharacterData data, out string groupName)
        {
            groupName = null;
            if (data == null) return false;
            var module = TickManager.GetModule<MiningScheduleModule>();
            return HasViewAccess(data.character_id, data.corporation_id, data.alliance_id ?? 0, module.ParsedLedgerEntitiesLists, out groupName);
        }

        public static bool HasLedgerEditAccess(WebAuthUserData data, out string groupName)
        {
            groupName = null;
            if (data == null) return false;
            var module = TickManager.GetModule<MiningScheduleModule>();
            return HasViewAccess(data.Id, data.CorpId, data.AllianceId, module.ParsedLedgerEntitiesLists, out groupName);
        }
        /// <summary>
        /// Global access flag to ledger operations
        /// </summary>
        public static bool HasCommonLedgerViewAccess(in JsonClasses.CharacterData data)
        {
            if (data == null) return false;
            return HasObserverLedgerViewAccess(data) || HasLedgerEditAccess(data, out _);
        }
        /// <summary>
        /// Global access flag to ledger operations
        /// </summary>
        public static bool HasCommonLedgerViewAccess(in WebAuthUserData data)
        {
            if (data == null) return false;
            return HasObserverLedgerViewAccess(data) || HasLedgerEditAccess(data, out _);
        }
        #endregion

        #region Auth checks

        public static bool HasAuthAccess(in JsonClasses.CharacterData data)
        {
            if (data == null) return false;
            return HasAuthAccess(data.character_id, data.corporation_id, data.alliance_id ?? 0);
        }

        public static bool HasAuthAccess(WebAuthUserData data)
        {
            if (data == null) return false;
            return HasAuthAccess(data.Id, data.CorpId, data.AllianceId);
        }

        private static bool HasAuthAccess(long id, long corpId, long allianceId)
        {
            if (!SettingsManager.Settings.Config.ModuleMiningSchedule) return false;
            var module = TickManager.GetModule<MiningScheduleModule>();

            return module.GetAccessAllCharacterIds(module.ParsedAuthAccessMembersLists).Contains(id) ||
                   module.GetAccessAllCorporationIds(module.ParsedAuthAccessMembersLists).Contains(corpId) || (allianceId > 0 &&
                                                                            module.GetAccessAllAllianceIds(module.ParsedAuthAccessMembersLists).Contains(allianceId));
        }

        /// <summary>
        /// Security check for access
        /// </summary>
        public static bool HasViewAccess(in JsonClasses.CharacterData data)
        {
            if (data == null) return false;
            return HasObserverExtrViewAccess(data) || HasObserverLedgerViewAccess(data) || HasExtrEditAccess(data, out _) || HasLedgerEditAccess(data, out _);
        }

        /// <summary>
        /// Security check for access
        /// </summary>
        public static bool HasViewAccess(WebAuthUserData data)
        {
            if (data == null) return false;
            return HasObserverExtrViewAccess(data) || HasObserverLedgerViewAccess(data) || HasExtrEditAccess(data, out _) || HasLedgerEditAccess(data, out _);
        }

        #endregion

        #region General access functions

        private static bool HasViewAccess(long id, long corpId, long allianceId, Dictionary<string, List<long>> dic)
        {
            if (!SettingsManager.Settings.Config.ModuleMiningSchedule) return false;
            return dic["character"].Contains(id) || dic["corporation"].Contains(corpId) || (allianceId > 0 && dic["alliance"].Contains(corpId));
        }

        private static bool HasViewAccess(long id, long corpId, long allianceId, Dictionary<string, Dictionary<string, List<long>>> dic, out string groupName)
        {
            groupName = null;
            if (!SettingsManager.Settings.Config.ModuleMiningSchedule) return false;
            //var module = TickManager.GetModule<MiningScheduleModule>();

            groupName = dic.FirstOrDefault(a => a.Value.FirstOrDefault(b=> b.Key == "character" && b.Value.Contains(id)).Key != null).Key;
            if (!string.IsNullOrEmpty(groupName))
                return true;
            groupName = dic.FirstOrDefault(a => a.Value.FirstOrDefault(b => b.Key == "corporation" && b.Value.Contains(corpId)).Key != null).Key;
            if (!string.IsNullOrEmpty(groupName))
                return true;
            if (allianceId > 0)
            {
                groupName = dic.FirstOrDefault(a => a.Value.FirstOrDefault(b => b.Key == "alliance" && b.Value.Contains(allianceId)).Key != null).Key;
                if (!string.IsNullOrEmpty(groupName))
                    return true;
            }

            return false;
        }

        public List<long> GetAccessAllCharacterIds(Dictionary<string, List<long>> dic)
        {
            return dic.FirstOrDefault(a => a.Key == "character").Value.Distinct().Where(a => a > 0).ToList();
        }
        public List<long> GetAccessAllCorporationIds(Dictionary<string, List<long>> dic)
        {
            return dic.FirstOrDefault(a => a.Key == "corporation").Value.Distinct().Where(a => a > 0).ToList();
        }

        public List<long> GetAccessAllAllianceIds(Dictionary<string, List<long>> dic)
        {
            return dic.FirstOrDefault(a => a.Key == "alliance").Value.Distinct().Where(a => a > 0).ToList();
        }
        #endregion

        public async Task<List<WebMiningLedgerEntry>> GetLedgerEntries(long ledgerStructureId, long charId)
        {
            var token = await DbHelper.GetToken(charId, TokenEnum.MiningSchedule);

            var r = await APIHelper.ESIAPI.RefreshToken(token, Settings.WebServerModule.CcpAppClientId,
                Settings.WebServerModule.CcpAppSecret);
            if (r == null || r.Data.IsFailed)
            {
                await LogHelper.LogWarning($"Failed to refresh mining token from {charId}", Category);
                if (r?.Data.IsNotValid ?? false)
                {
                    await DbHelper.DeleteToken(charId, TokenEnum.MiningSchedule);
                    await LogHelper.LogWarning($"Mining token from {charId} is no longer valid and will be deleted!", Category);
                }
                return null;
            }

            var rChar = await APIHelper.ESIAPI.GetCharacterData(Reason, charId, true);
            if (rChar == null)
            {
                await LogHelper.LogWarning($"Failed to refresh character {charId}", Category);
                return null;
            }
            var corp = await APIHelper.ESIAPI.GetCorporationData(Reason, rChar.corporation_id);
            if (corp == null)
            {
                await LogHelper.LogWarning($"Failed to refresh corp {rChar.corporation_id}", Category);
                return null;
            }

            var entries = await APIHelper.ESIAPI.GetCorpMiningLedgerEntries(Reason, rChar.corporation_id, ledgerStructureId, r.Result);
            var list = new List<WebMiningLedgerEntry>();

            var maxDate = entries.Max(a => a.last_updated);
            var lowDate = maxDate.AddDays(-3);
            entries = entries.Where(a => a.last_updated <= maxDate && a.last_updated >= lowDate).ToList();

            //group by character and ore type
            entries = entries.GroupByMany(new [] { "character_id", "type_id" }).Select(
                g =>
                {
                    var list = g.Items.Cast<MiningLedgerEntryJson>();
                    return new MiningLedgerEntryJson
                    {
                        character_id = list.First().character_id,
                        quantity = list.Sum(s => s.quantity),
                        last_updated = list.First().last_updated,
                        recorded_corporation_id = list.First().recorded_corporation_id,
                        type_id = list.First().type_id
                    };
                }).ToList();

            var oreIds = entries.Select(a => a.type_id).Distinct().ToList();
            var prices = await APIHelper.ESIAPI.GetFuzzPrice(Reason, oreIds);

            foreach (var entry in entries)
            {
                var ch = await APIHelper.ESIAPI.GetCharacterData(Reason, entry.character_id);
                var c = await APIHelper.ESIAPI.GetCorporationData(Reason, entry.recorded_corporation_id);
                var ore = await APIHelper.ESIAPI.GetTypeId(Reason, entry.type_id);
                var price = prices.FirstOrDefault(a => a.Id == entry.type_id)?.Sell ?? 0;

                list.Add(new WebMiningLedgerEntry
                {
                    CharacterName = ch?.name ?? LM.Get("Unknown"),
                    CorporationTicker = c?.ticker,
                    OreName = ore?.name ?? LM.Get("Unknown"),
                    OreId = entry.type_id,
                    Quantity = entry.quantity,
                    Price = price * entry.quantity,
                });
            }

            return list.OrderByDescending(a=> a.Quantity).ToList();
        }

    }
}
