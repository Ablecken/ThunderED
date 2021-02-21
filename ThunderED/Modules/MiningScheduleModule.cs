﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ThunderED.Classes;
using ThunderED.Classes.Enums;
using ThunderED.Helpers;
using ThunderED.Json;
using ThunderED.Modules.Sub;

namespace ThunderED.Modules
{
    public class MiningScheduleModule: AppModuleBase
    {
        public override LogCat Category => LogCat.MiningSchedule;

        protected readonly Dictionary<string, Dictionary<string, List<long>>> ParsedFeedMembersLists = new Dictionary<string, Dictionary<string, List<long>>>();
        protected readonly Dictionary<string, Dictionary<string, List<long>>> ParsedAccessMembersLists = new Dictionary<string, Dictionary<string, List<long>>>();

        public override async Task Initialize()
        {
            if (WebServerModule.WebModuleConnectors.ContainsKey(Reason))
                WebServerModule.WebModuleConnectors.Remove(Reason);
            WebServerModule.WebModuleConnectors.Add(Reason, ProcessRequest);

            ParsedFeedMembersLists.Clear();
            ParsedAccessMembersLists.Clear();

            var data = new Dictionary<string, List<object>> { { "general", Settings.MiningScheduleModule.AccessEntities } };
            await ParseMixedDataArray(data, MixedParseModeEnum.Member, ParsedAccessMembersLists);
            data = new Dictionary<string, List<object>> { { "general", Settings.MiningScheduleModule.FeedEntities } };
            await ParseMixedDataArray(data, MixedParseModeEnum.Member, ParsedFeedMembersLists);
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
                if (!query.Contains("&state=ml"))
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



        #region Access checks

        public static bool HasViewAccess(in JsonClasses.CharacterData data)
        {
            if (data == null) return false;
            return HasViewAccess(data.character_id, data.corporation_id, data.alliance_id ?? 0);
        }

        public static bool HasViewAccess(WebAuthUserData data)
        {
            if (data == null) return false;
            return HasViewAccess(data.Id, data.CorpId, data.AllianceId);
        }

        private static bool HasViewAccess(long id, long corpId, long allianceId)
        {
            if (!SettingsManager.Settings.Config.ModuleMiningSchedule) return false;
            var module = TickManager.GetModule<MiningScheduleModule>();

            return module.GetAccessAllCharacterIds().Contains(id) ||
                   module.GetAccessAllCorporationIds().Contains(corpId) || (allianceId > 0 &&
                                                                            module.GetAccessAllAllianceIds().Contains(allianceId));
        }

        public List<long> GetAccessAllCharacterIds()
        {
            return ParsedAccessMembersLists.Where(a => a.Value.ContainsKey("character")).SelectMany(a => a.Value["character"]).Distinct().Where(a => a > 0).ToList();
        }
        public List<long> GetAccessAllCorporationIds()
        {
            return ParsedAccessMembersLists.Where(a => a.Value.ContainsKey("corporation")).SelectMany(a => a.Value["corporation"]).Distinct().Where(a => a > 0).ToList();
        }

        public List<long> GetAccessAllAllianceIds()
        {
            return ParsedAccessMembersLists.Where(a => a.Value.ContainsKey("alliance")).SelectMany(a => a.Value["alliance"]).Distinct().Where(a => a > 0).ToList();
        }
        #endregion

        #region Feed/Auth checks

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

            return module.GetFeedAllCharacterIds().Contains(id) ||
                   module.GetFeedAllCorporationIds().Contains(corpId) || (allianceId > 0 &&
                                                                            module.GetFeedAllAllianceIds().Contains(allianceId));
        }

        public List<long> GetFeedAllCharacterIds()
        {
            return ParsedFeedMembersLists.Where(a => a.Value.ContainsKey("character")).SelectMany(a => a.Value["character"]).Distinct().Where(a => a > 0).ToList();
        }
        public List<long> GetFeedAllCorporationIds()
        {
            return ParsedFeedMembersLists.Where(a => a.Value.ContainsKey("corporation")).SelectMany(a => a.Value["corporation"]).Distinct().Where(a => a > 0).ToList();
        }

        public List<long> GetFeedAllAllianceIds()
        {
            return ParsedFeedMembersLists.Where(a => a.Value.ContainsKey("alliance")).SelectMany(a => a.Value["alliance"]).Distinct().Where(a => a > 0).ToList();
        }
        #endregion
    }
}
