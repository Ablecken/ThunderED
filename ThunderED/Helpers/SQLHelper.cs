﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ThunderED.Classes;
using ThunderED.Classes.Entities;
using ThunderED.Json;
using ThunderED.Json.Internal;
using ThunderED.Providers;

namespace ThunderED.Helpers
{
    public static partial class SQLHelper
    {
        public static IDatabasePovider Provider { get; set; }

        //SQLite Query
        #region SQLiteQuery

        internal static async Task<T> SQLiteDataQuery<T>(string table, string field, string whereField, object whereData)
        {
            return await Provider?.SQLiteDataQuery<T>(table, field, whereField, whereData);
        }

        internal static async Task<T> SQLiteDataQuery<T>(string table, string field, Dictionary<string, object> where)
        {
            return await Provider?.SQLiteDataQuery<T>(table, field, where);
        }

        internal static async Task<List<T>> SQLiteDataQueryList<T>(string table, string field, string whereField, object whereData)
        {
            return await Provider?.SQLiteDataQueryList<T>(table, field, whereField, whereData);
        }

        internal static async Task<List<T>> SQLiteDataQueryList<T>(string table, string field, Dictionary<string, object> where)
        {
            return await Provider?.SQLiteDataQueryList<T>(table, field, where);
        }

        #endregion
        
        //SQLite Update
        #region SQLiteUpdate

        internal static async Task SQLiteDataUpdate(string table, string setField, object setData, string whereField, object whereData)
        {
            await Provider?.SQLiteDataUpdate(table, setField, setData, whereField, whereData);
        }

        internal static async Task SQLiteDataUpdate(string table, string setField, object setData, Dictionary<string, object> where)
        {
            await Provider?.SQLiteDataUpdate(table, setField, setData, where);

        }

        internal static async Task SQLiteDataInsertOrUpdate(string table, Dictionary<string, object> values)
        {
            await Provider?.SQLiteDataInsertOrUpdate(table, values);
        }

        internal static async Task SQLiteDataInsert(string table, Dictionary<string, object> values)
        {
            await Provider?.SQLiteDataInsert(table, values);
        }
        #endregion

        //SQLite Delete
        #region SQLiteDelete
        internal static async Task SQLiteDataDelete(string table, string whereField = null, object whereValue = null)
        {
            await Provider?.SQLiteDataDelete(table, whereField, whereValue);
        }

        internal static async Task SQLiteDataDelete(string table, Dictionary<string, object> where)
        {
            await Provider?.SQLiteDataDelete(table, where);
        }
        #endregion

        internal static async Task SQLiteDataInsertOrUpdateTokens(string notifyToken, string userId, string mailToken, string contractsToken)
        {
            await Provider?.SQLiteDataInsertOrUpdateTokens(notifyToken, userId, mailToken, contractsToken);
        }

        internal static async Task<IList<IDictionary<string, object>>> GetAuthUser(ulong uId, bool order = false)
        {
            return await Provider?.GetAuthUser(uId, order);
        }

        internal static async Task<PendingUserEntity> GetPendingUser(string remainder)
        {
            var res = await SelectData("pendingUsers", new[] {"*"}, new Dictionary<string, object> {{"authString", remainder}});
            return res.Select(item => new PendingUserEntity
            {
                Id = Convert.ToInt64(item[0]),
                CharacterId = Convert.ToInt64(item[1]),
                CorporationId = Convert.ToInt64(item[2]),
                AllianceId = Convert.ToInt64(item[3]),
                Groups = Convert.ToString(item[4]),
                AuthString = Convert.ToString(item[5]),
                Active = (string)item[6] == "1",
                CreateDate = Convert.ToDateTime(item[7]),
                DiscordId = Convert.ToInt64(item[8]),
            }).ToList().FirstOrDefault();
        }

        internal static async Task RunCommand(string query2, bool silent = false)
        {
            await Provider?.RunCommand(query2, silent);
        }

        internal static async Task<List<PendingUserEntity>> GetPendingUsers()
        {
            return (await SelectData("pendingUsers", new[] {"*"})).Select(item => new PendingUserEntity
            {
                Id = Convert.ToInt64(item[0]),
                CharacterId = Convert.ToInt64(item[1]),
                CorporationId = Convert.ToInt64(item[2]),
                AllianceId = Convert.ToInt64(item[3]),
                Groups = Convert.ToString(item[4]),
                AuthString = Convert.ToString(item[5]),
                Active = item[6] == "1",
                CreateDate = Convert.ToDateTime(item[7]),
                DiscordId = Convert.ToInt64(item[8]),
            }).ToList();
        }

        internal static async Task<T> SQLiteDataSelectCache<T>(object whereValue, int maxDays)
            where T: class
        {
            return await Provider?.SQLiteDataSelectCache<T>(whereValue, maxDays);
        }

        internal static async Task SQLiteDataUpdateCache<T>(T data, object id, int days = 1) 
            where T : class
        {
            await Provider?.SQLiteDataUpdateCache(data, id, days);
        }

        internal static async Task SQLiteDataPurgeCache()
        {
            await Provider?.SQLiteDataPurgeCache();
        }

        public static string LoadProvider()
        {
            var prov = SettingsManager.Settings.Config.DatabaseProvider;
            switch (prov)
            {
                case "sqlite":
                    Provider = new SqliteDatabaseProvider();
                    break;
                default:
                    LogHelper.LogInfo("Using default sqlite provider!").GetAwaiter().GetResult();
                    Provider = new SqliteDatabaseProvider();
                    break;
                //  return $"[CRITICAL] Unknown database provider {prov}!";

            }
            //upgrade database
            if (!SQLHelper.Upgrade().GetAwaiter().GetResult())
            {
                return "[CRITICAL] Failed to upgrade DB to latest version!";
            }

            return null;
        }

        public static async Task<List<TimerItem>> SQLiteDataSelectTimers()
        {
            return await Provider?.SQLiteDataSelectTimers();
        }

        public static async Task CleanupNotificationsList()
        {
            await Provider?.CleanupNotificationsList();
        }

        public static async Task SQLiteDataDeleteWhereIn(string table, string field, List<long> list, bool not)
        {
            await Provider?.SQLiteDataDeleteWhereIn(table, field, list, not);

        }

        private static async Task<bool> RunScript(string file)
        {
            return await Provider?.RunScript(file);
        }

        public static async Task<List<JsonClasses.SystemName>> GetSystemsByConstellation(long constellationId)
        {
            return (await SelectData("mapSolarSystems", new[] {"solarSystemID", "constellationID", "regionID", "solarSystemName", "security"}, new Dictionary<string, object>
            {
                {"constellationID", constellationId}
            })).Select(item => new JsonClasses.SystemName
            {
                system_id = Convert.ToInt64(item[0]),
                constellation_id = Convert.ToInt64(item[1]),
                DB_RegionId = Convert.ToInt64(item[2]),
                name = Convert.ToString(item[3]),
                security_status = (float)Convert.ToDouble(item[4]),
            }).ToList();
        }

        public static async Task<List<JsonClasses.SystemName>> GetSystemsByRegion(long regionId)
        {
            return (await SelectData("mapSolarSystems", new[] {"solarSystemID", "constellationID", "regionID", "solarSystemName", "security"}, new Dictionary<string, object>
            {
                {"regionID", regionId}
            })).Select(item => new JsonClasses.SystemName
            {
                system_id = Convert.ToInt64(item[0]),
                constellation_id = Convert.ToInt64(item[1]),
                DB_RegionId = Convert.ToInt64(item[2]),
                name = Convert.ToString(item[3]),
                security_status = (float)Convert.ToDouble(item[4]),
            }).ToList();
        }

        public static async Task<List<object[]>> SelectData(string table, string[] fields, Dictionary<string, object> where = null)
        {
            return await Provider?.SelectData(table, fields, where);
        }

        public static async Task<bool> IsEntryExists(string table, Dictionary<string, object> where)
        {
            return await Provider?.IsEntryExists(table, where);
        }

        public static async Task<List<JsonClasses.NullCampaignItem>> GetNullCampaigns(string group)
        {
            return (await SelectData("nullCampaigns", new[] {"data", "lastAnnounce" }, new Dictionary<string, object>{{"groupKey", group}}))
                .Select(item =>
                {                    
                    var i = new JsonClasses.NullCampaignItem().FromJson((string) item[0]);
                    i.LastAnnounce = Convert.ToInt64(item[1]);
                    return i;
                }).ToList();
        }

        public static async Task<JsonClasses.SystemName> GetSystemById(long id)
        {
            return (await SelectData("mapSolarSystems", new[] {"solarSystemID", "constellationID", "regionID", "solarSystemName", "security"}, new Dictionary<string, object>
            {
                {"solarSystemID", id}
            })).Select(item => new JsonClasses.SystemName
            {
                system_id = Convert.ToInt64(item[0]),
                constellation_id = Convert.ToInt64(item[1]),
                DB_RegionId = Convert.ToInt64(item[2]),
                name = Convert.ToString(item[3]),
                security_status = (float)Convert.ToDouble(item[4]),
            }).FirstOrDefault();
        }

        internal static async Task<JsonClasses.RegionData> GetRegionById(long id)
        {
            return (await SelectData("mapRegions", new[] {"regionID", "regionName"}, new Dictionary<string, object>
            {
                {"regionID", id}
            })).Select(item => new JsonClasses.RegionData
            {
                DB_id = Convert.ToInt64(item[0]),
                name = Convert.ToString(item[1]),
            }).FirstOrDefault();
        }

        internal static async Task<JsonClasses.ConstellationData> GetConstellationById(long id)
        {
            return (await SelectData("mapConstellations", new[] {"regionID", "constellationID","constellationName"}, new Dictionary<string, object>
            {
                {"constellationID", id}
            })).Select(item => new JsonClasses.ConstellationData
            {
                region_id = Convert.ToInt64(item[0]),
                constellation_id = Convert.ToInt64(item[1]),
                name = Convert.ToString(item[2]),
            }).FirstOrDefault();
        }

        
        internal static async Task<JsonClasses.Type_id> GetTypeId(long id)
        {
            return (await SelectData("invTypes", new[] {"typeID", "groupID","typeName", "description", "mass", "volume"}, new Dictionary<string, object>
            {
                {"typeID", id}
            })).Select(item => new JsonClasses.Type_id
            {
                type_id = Convert.ToInt64(item[0]),
                group_id = Convert.ToInt64(item[1]),
                name = Convert.ToString(item[2]),
                description = Convert.ToString(item[3]),
                mass = (float)Convert.ToDouble(item[4]),
                volume = (float)Convert.ToDouble(item[5])
            }).FirstOrDefault();
        }

        
        internal static async Task<JsonClasses.invGroup> GetInvGroup(long id)
        {
            return (await SelectData("invGroups", new[] {"groupID", "categoryID","groupName"}, new Dictionary<string, object>
            {
                {"groupID", id}
            })).Select(item => new JsonClasses.invGroup
            {
                groupId = Convert.ToInt64(item[0]),
                categoryId = Convert.ToInt64(item[1]),
                groupName = Convert.ToString(item[2]),
            }).FirstOrDefault();
        }

        #region pendingUsers table
        public static async Task<bool> PendingUsersIsEntryActive(long characterId)
        {
            return !string.IsNullOrEmpty(await SQLiteDataQuery<string>("pendingUsers", "characterID", "characterID", characterId.ToString()));
        }

        public static async Task<bool> PendingUsersIsEntryActive(string code)
        {
            return !string.IsNullOrEmpty(await SQLiteDataQuery<string>("pendingUsers", "characterID", "authString", code)) && 
                 await SQLiteDataQuery<string>("pendingUsers", "active", "authString", code) == "1";
        }

        public static async Task<string> PendingUsersGetCode(long characterId)
        {
            return await SQLiteDataQuery<string>("pendingUsers", "authString", "characterID", characterId.ToString());
        }

        public static async Task PendingUsersSetCode(string code, ulong discordId)
        {
            await SQLiteDataUpdate("pendingUsers", "discordID", discordId, "authString", code);
        }

        public static async Task<ulong> PendingUsersGetDiscordId(string code)
        {
            return await SQLiteDataQuery<ulong>("pendingUsers", "discordID", "authString", code);
        }

        
        public static async Task<long> PendingUsersGetCharacterId(string code)
        {
            return await SQLiteDataQuery<long>("pendingUsers", "characterID", "authString", code);

        }
        
        #endregion

        #region userTokens table

        public static async Task<string> UserTokensGetGroupName(long characterId)
        {
            return await SQLiteDataQuery<string>("userTokens", "groupName", "characterID", characterId);
        }
        public static async Task<string> UserTokensGetGroupName(string code)
        {
            var characterId = await SQLiteDataQuery<string>("pendingUsers", "characterID", "authString", code);
            return await UserTokensGetGroupName(Convert.ToInt64(characterId));
        }

        public static async Task<string> UserTokensGetName(long characterId)
        {
            return await SQLiteDataQuery<string>("userTokens", "characterName", "characterID", characterId);
        }

        public static async Task<string> UserTokensGetName(string code)
        {
            var characterId = await SQLiteDataQuery<string>("pendingUsers", "characterID", "authString", code);
            return await UserTokensGetName(Convert.ToInt64(characterId));
        }

        public static async Task<bool> UserTokensIsAuthed(long characterId)
        {
            return await SQLiteDataQuery<int>("userTokens", "authState", "characterID", characterId) == 2;
        }

        public static async Task<bool> UserTokensIsConfirmed(long characterId)
        {
            return await SQLiteDataQuery<int>("userTokens", "authState", "characterID", characterId) == 1;
        }

        public static async Task<bool> UserTokensIsConfirmed(string code)
        {
            var characterId = await SQLiteDataQuery<string>("pendingUsers", "characterID", "authString", code);
            return await UserTokensIsConfirmed(Convert.ToInt64(characterId));
        }

        public static async Task<bool> UserTokensIsPending(long characterId)
        {
            return await SQLiteDataQuery<int>("userTokens", "authState", "characterID", characterId) == 0;
        }

        public static async Task<bool> UserTokensIsPending(string code )
        {
            var characterId = await SQLiteDataQuery<string>("pendingUsers", "characterID", "authString", code);
            return await UserTokensIsPending(Convert.ToInt64(characterId));
        }

        public static async Task<bool> UserTokensExists(long characterId)
        {
            return await SQLiteDataQuery<long>("userTokens", "characterID", "characterID", characterId) != 0;
        }


        public static async Task<bool> UserTokensExists(string code)
        {
            var characterId = await SQLiteDataQuery<string>("pendingUsers", "characterID", "authString", code);
            return await UserTokensExists(Convert.ToInt64(characterId));

        }

        public static async Task UserTokensSetDiscordId(string code, ulong authorId)
        {
            var characterId = await SQLiteDataQuery<string>("pendingUsers", "characterID", "authString", code);

            await SQLiteDataUpdate("userTokens", "discordUserId", authorId, "characterID", Convert.ToInt64(characterId));
        }

        public static async Task<List<object[]>> UserTokensGetConfirmedDataList()
        {
            return await SelectData("userTokens", new[] {"characterID", "characterName", "discordUserId", "groupName"}, new Dictionary<string, object>
            {
                {"authState", 1}
            });
        }

        public static async Task<string> GetRefreshTokenForContracts(long charId)
        {
            return await SQLHelper.SQLiteDataQuery<string>("refreshTokens", "ctoken", "id", charId);
        }

        public static async Task UserTokensSetAuthState(long characterId, int value)
        {
            await SQLiteDataUpdate("userTokens", "authState", value, "characterID", characterId);
        }

        public static async Task UserTokensSetAuthState(string code, int value)
        {
            var characterId = await SQLiteDataQuery<string>("pendingUsers", "characterID", "authString", code);
            await SQLiteDataUpdate("userTokens", "authState", value, "characterID", Convert.ToInt64(characterId));
        }
        
        public static async Task<bool> UserTokensHasDiscordId(string code)
        {
            var characterId = await SQLiteDataQuery<string>("pendingUsers", "characterID", "authString", code);
            return await SQLiteDataQuery<ulong>("userTokens", "discordUserId", "characterID", characterId) != 0;
        }

        public static async Task<List<UserTokenEntity>> UserTokensGetAllEntries(Dictionary<string, object> where = null)
        {
            var data = await SelectData("userTokens", new[] {"characterID", "characterName", "discordUserId", "refreshToken", "groupName", "permissions", "authState"}, where);
            var list = new List<UserTokenEntity>();
            data.ForEach(d =>
            {
                list.Add(new UserTokenEntity
                {
                    CharacterId = Convert.ToInt64(d[0]),
                    CharacterName = d[1].ToString(),
                    DiscordUserId = Convert.ToUInt64(d[2]),
                    RefreshToken = d[3].ToString(),
                    GroupName = d[4].ToString(),
                    Permissions = d[5].ToString(),
                    AuthState = Convert.ToInt32(d[6]),
                });
            });
            return list;
        }
        #endregion

        public static async Task<UserTokenEntity> UserTokensGetEntry(long inspectCharId)
        {
            return (await UserTokensGetAllEntries(new Dictionary<string, object> {{"characterID", inspectCharId}})).FirstOrDefault();
        }

        public static async Task DeleteAuthUsers(string discordId)
        {
            await SQLiteDataDelete("authUsers", "discordID", discordId);
        }

        public static async Task InvalidatePendingUser(string remainder)
        {
            await SQLiteDataUpdate("pendingUsers", "active", "0", "authString", remainder);
        }

        public static async Task DeleteAuthDataByCharId(long characterID)
        {
            await SQLiteDataDelete("userTokens", "characterID", characterID);
            await SQLiteDataDelete("pendingUsers", "characterID", characterID.ToString());
            await SQLiteDataDelete("authUsers", "characterID", characterID.ToString());
        }

        public static async Task<List<JsonClasses.Contract>> LoadContracts(long characterID, bool isCorp)
        {
            var data = (string)(await SelectData("contracts", new [] {"data"}, new Dictionary<string, object> {{"type", isCorp ? 0 : 1}, {"characterID", characterID}}))?.FirstOrDefault()?.FirstOrDefault();
            return string.IsNullOrEmpty(data) ? null : JsonConvert.DeserializeObject<List<JsonClasses.Contract>>(data).OrderByDescending(a=> a.contract_id).ToList();
        }

        public static async Task SaveContracts(long characterID, List<JsonClasses.Contract> data, bool isCorp)
        {
            var result = JsonConvert.SerializeObject(data);
            await SQLiteDataInsertOrUpdate("contracts", new Dictionary<string, object>
            {
                {"characterID", characterID},
                {"type", isCorp ? 0 : 1},
                {"data", result}
            });
        }


    }
}
