﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ThunderED.Helpers;
using ThunderED.Thd;

namespace ThunderED
{
    public static partial class SQLHelper
    {
        #region OLD
        //"1.0.0","1.0.1","1.0.7", "1.0.8", "1.1.3", "1.1.4", "1.1.5", "1.1.6", "1.1.8", "1.2.2","1.2.6", "1.2.7", "1.2.8", "1.2.10", "1.2.14", "1.2.15", "1.2.16","1.2.19","1.3.1", "1.3.2", "1.3.4", "1.3.10", "1.3.16", "1.4.2", 
        private static readonly string[] MajorVersionUpdates = new[]
        {
            "1.4.5", "1.5.4", "2.0.1", "2.0.2", "2.0.3", "2.0.4", "2.0.5", "2.0.6", "2.0.7", "2.0.9", "2.0.10",
            "2.0.15", "2.0.16", "2.0.18", "2.0.19", "2.0.20", "2.1.0", "2.1.1", "2.1.2"
        };

        public static async Task<bool> Upgrade()
        {
            var version = await Query<string>("cache_data", "data", "name", "version") ??
                          await Query<string>("cacheData", "data", "name", "version");
            var isNew = string.IsNullOrEmpty(version) || SettingsManager.IsNew;

            var vDbVersion = isNew ? new Version(Program.VERSION) : new Version(version);

            try
            {
                var firstUpdate = new Version(MajorVersionUpdates[0]);
                var isSqlite = SettingsManager.Settings.Database.DatabaseProvider.Equals("sqlite",
                    StringComparison.OrdinalIgnoreCase);
                if (vDbVersion < firstUpdate)
                {
                    await LogHelper.LogError(
                        "Your database version is below the required minimum for an upgrade. You have to do clean install without the ability to migrate your data. Consult GitHub WIKI or reach @panthernet#1659 on Discord group for assistance.");
                    return false;
                }

                foreach (var update in MajorVersionUpdates)
                {
                    var v = new Version(update);
                    if (vDbVersion >= v) continue;

                    switch (update)
                    {
                        #region OLD

                        /* case "1.0.1":
                             await RunCommand("DELETE FROM cacheData where name='version'");
                             await RunCommand("CREATE UNIQUE INDEX cacheData_name_uindex ON cacheData (name)");
                             await RunCommand("CREATE TABLE `killFeedCache` ( `type` text NOT NULL, `id` text NOT NULL, `lastId` TEXT)");
                             await RunCommand("CREATE UNIQUE INDEX killFeedCache_type_id_uindex ON killFeedCache (type, id)");
                             await RunCommand("delete from cache");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.0.7":
                             await RunCommand("CREATE TABLE `timersAuth` ( `id` text UNIQUE PRIMARY KEY NOT NULL, `time` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP);");
                             await RunCommand(
                                 "CREATE TABLE `timers` ( `id` INTEGER PRIMARY KEY NOT NULL, `timerType` int NOT NULL, `timerStage` int NOT NULL,`timerLocation` text NOT NULL, `timerOwner` text NOT NULL, `timerET` timestamp NOT NULL,`timerNotes` text, `timerChar` text NOT NULL, `announce` int NOT NULL DEFAULT 0);");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.0.8":
                             await RunCommand("ALTER TABLE refreshTokens ADD mail TEXT NULL;");
                             await RunCommand("CREATE TABLE `mail` ( `id` text UNIQUE PRIMARY KEY NOT NULL, `mailId` int DEFAULT 0);");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.1.3":
                             await RunCommand("CREATE TABLE `fleetup` ( `id` text UNIQUE PRIMARY KEY NOT NULL, `announce` int NOT NULL DEFAULT 0);");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.1.4":
                             await RunCommand("DROP TABLE notificationsList;");
                             await RunCommand("DROP TABLE notifications;");
                             await RunCommand("CREATE TABLE `notificationsList` ( groupName TEXT NOT NULL, filterName TEXT NOT NULL,`id` int NOT NULL, `time` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP);");                            
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.1.5":
                             await RunCommand("CREATE TABLE `incursions` ( `constId` int UNIQUE PRIMARY KEY NOT NULL, `time` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP);");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.1.6": 
                             await RunCommand("CREATE TABLE `nullCampaigns` ( `groupKey` text NOT NULL, `campaignId` INTEGER NOT NULL, `time` timestamp NOT NULL, `data` TEXT NOT NULL, `lastAnnounce` INTEGER NOT NULL DEFAULT 0);");
                             await RunCommand("CREATE INDEX nullCampaigns_groupKey_uindex ON nullCampaigns (groupKey);");
                             await RunCommand("CREATE UNIQUE INDEX nullCampaigns_groupKey_campaignId_uindex ON nullCampaigns (groupKey, campaignId);");

                             //https://www.fuzzwork.co.uk/dump/latest/
                             if(await RunScript(Path.Combine(SettingsManager.RootDirectory, "Content", "SQL", "1.1.6.sql")))
                                 await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             else await LogHelper.LogError($"Upgrade to DB version {update} FAILED! Script not found!");
                             break;
                         case "1.1.8":
                             await RunCommand(
                                 "CREATE TABLE `userTokens` ( `characterID` INT UNIQUE NOT NULL, `characterName` TEXT NOT NULL, `discordUserId` INT NOT NULL DEFAULT 0, `refreshToken` TEXT NOT NULL, `groupName` TEXT NOT NULL DEFAULT 'DEFAULT', `permissions` TEXT NOT NULL, `authState` INT NOT NULL DEFAULT 0);");
                             await LogHelper.LogWarning("Step 1 finished...");
                             await RunCommand("DELETE FROM `pendingUsers`;");
                             await RunCommand("CREATE UNIQUE INDEX ux_pendingUsers_characterID ON `pendingUsers`(`characterID`);;");
                             await LogHelper.LogWarning("Step 2 finished...");
                             await RunCommand("ALTER TABLE `pendingUsers` ADD COLUMN `discordID` INT NOT NULL DEFAULT 0;");
                             await LogHelper.LogWarning("Step 3 finished...");
                             await RunCommand("CREATE TABLE `hrmAuth` ( `id` text UNIQUE PRIMARY KEY NOT NULL, `time` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP, `code` TEXT NOT NULL);");
                             await LogHelper.LogWarning("Step 4 finished...");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.2.2":
                             await BackupDatabase();
                             await RunCommand("CREATE TABLE invTypes(typeID INTEGER PRIMARY KEY NOT NULL,groupID INTEGER,typeName VARCHAR(100),description TEXT,mass FLOAT,volume FLOAT,capacity FLOAT,portionSize INTEGER,raceID INTEGER,basePrice DECIMAL(19,4),published BOOLEAN,marketGroupID INTEGER,iconID INTEGER,soundID INTEGER,graphicID INTEGER);");
                             await RunCommand("CREATE INDEX ix_invTypes_groupID ON invTypes (groupID);");
                             await RunCommand("CREATE TABLE mapConstellations(regionID INTEGER,constellationID INTEGER PRIMARY KEY NOT NULL,constellationName VARCHAR(100),x FLOAT,y FLOAT,z FLOAT,xMin FLOAT,xMax FLOAT,yMin FLOAT,yMax FLOAT,zMin FLOAT,zMax FLOAT,factionID INTEGER,radius FLOAT);");
                             await RunCommand("CREATE TABLE mapRegions(regionID INTEGER PRIMARY KEY NOT NULL,regionName VARCHAR(100),x FLOAT,y FLOAT,z FLOAT,xMin FLOAT,xMax FLOAT,yMin FLOAT,yMax FLOAT,zMin FLOAT,zMax FLOAT,factionID INTEGER,radius FLOAT);");
                             await RunCommand("CREATE TABLE invGroups(groupID INTEGER PRIMARY KEY NOT NULL,categoryID INTEGER,groupName VARCHAR(100),iconID INTEGER,useBasePrice BOOLEAN,anchored BOOLEAN,anchorable BOOLEAN,fittableNonSingleton BOOLEAN,published BOOLEAN);");
                             await RunCommand("CREATE INDEX ix_invGroups_categoryID ON invGroups (categoryID);");
                             await RunCommand("DELETE FROM `cache`;");

                             if (!await CopyTableDataFromDefault("invTypes", "invGroups", "mapConstellations", "mapRegions", "mapSolarSystems"))
                             {
                                 await RestoreDatabase();
                                 return false;
                             }

                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.2.6":
                             await BackupDatabase();
                             await RunCommand("ALTER TABLE `refreshTokens` ADD COLUMN `ctoken` TEXT;");
                             await RunCommand("CREATE TABLE contracts(`characterID` INTEGER PRIMARY KEY NOT NULL,`type` INTEGER NOT NULL,`data` TEXT NOT NULL);");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.2.7":
                             await BackupDatabase();
                             await RunCommand("CREATE TABLE contracts(`characterID` INTEGER PRIMARY KEY NOT NULL,`type` INTEGER NOT NULL,`data` TEXT NOT NULL);");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.2.8":
                             await BackupDatabase();
                             await RunCommand("DROP TABLE `contracts`;");
                             await RunCommand("CREATE TABLE contracts(`characterID` INTEGER PRIMARY KEY NOT NULL,`data` TEXT, `corpdata` TEXT);");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.2.10":
                             await BackupDatabase();
                             await RunCommand("CREATE TABLE standAuth(`characterID` INTEGER PRIMARY KEY NOT NULL, `token` TEXT, `personalStands` TEXT, `corpStands` TEXT, `allianceStands` TEXT);");
                             break;
                         case "1.2.14":
                             await BackupDatabase();
                             await LogHelper.LogWarning("Upgrading DB! Please wait...");
                             var users = await GetAuthUsersEx();
                             var tokens = await UserTokensGetAllEntriesEx();
                             await RunCommand("DROP TABLE `authUsers`;");
                             await RunCommand("CREATE TABLE authUsers(`Id` INTEGER PRIMARY KEY NOT NULL, `characterID` INTEGER NOT NULL, `discordID` INTEGER, `groupName` TEXT, `refreshToken` TEXT, `authState` INTEGER NOT NULL DEFAULT 0, `data` TEXT);");
                             await RunCommand("CREATE INDEX ix_authUsers_characterID ON authUsers (characterID);");
                             await RunCommand("CREATE INDEX ix_authUsers_discordID ON authUsers (discordID);");
                             await users.ParallelForEachAsync(async user =>
                             {
                                 var t = tokens.FirstOrDefault(a => a.CharacterId == user.CharacterId);
                                 user.AuthState = t?.AuthState ?? (user.IsActive ? 2 : 0);
                                 user.GroupName = user.Group;
                                 user.DiscordId = user.DiscordId == 0 ? (t?.DiscordUserId ?? 0) : user.DiscordId;
                                 user.RefreshToken = t?.RefreshToken;
                                 user.Data.Permissions = t?.Permissions;
                                 user.Data.CharacterName = user.EveName;

                                 var cData = await APIHelper.ESIAPI.GetCharacterData("DB_UPGRADE", user.CharacterId);
                                 if (cData != null)
                                 {
                                     var corp = await APIHelper.ESIAPI.GetCorporationData("DB_UPGRADE", cData.corporation_id);
                                     user.Data.CorporationName = corp?.name;
                                     user.Data.CorporationTicker = corp?.ticker;
                                     user.Data.CorporationId = cData.corporation_id;
                                     var ally = cData.alliance_id.HasValue ? await APIHelper.ESIAPI.GetAllianceData("DB_UPGRADE", cData.alliance_id) : null;
                                     user.Data.AllianceName = ally?.name;
                                     user.Data.AllianceTicker = ally?.ticker;
                                     user.Data.AllianceId = cData.alliance_id ?? 0;
                                 }
                             }, 10);

                             var cUsers = new ConcurrentBag<AuthUserEntity>(users);
                             var lTokens = tokens.Where(a => users.All(b => b.CharacterId != a.CharacterId));
                             await lTokens.ParallelForEachAsync(async token =>
                             {
                                 var item = new AuthUserEntity
                                 {
                                     CharacterId = token.CharacterId,
                                     DiscordId = token.DiscordUserId,
                                     GroupName = token.GroupName,
                                     AuthState = token.AuthState,
                                     RefreshToken = token.RefreshToken,
                                     Data = {CharacterName = token.CharacterName, Permissions = token.Permissions}
                                 };
                                 var cData = await APIHelper.ESIAPI.GetCharacterData("DB_UPGRADE", token.CharacterId);
                                 if (cData != null)
                                 {
                                     var corp = await APIHelper.ESIAPI.GetCorporationData("DB_UPGRADE", cData.corporation_id);
                                     item.Data.CorporationName = corp?.name;
                                     item.Data.CorporationId = cData.corporation_id;
                                     item.Data.CorporationTicker = corp?.ticker;
                                     var ally = cData.alliance_id.HasValue ? await APIHelper.ESIAPI.GetAllianceData("DB_UPGRADE", cData.alliance_id) : null;
                                     item.Data.AllianceName = ally?.name;
                                     item.Data.AllianceId = cData.alliance_id ?? 0;
                                     item.Data.AllianceTicker = ally?.ticker;
                                 }
                                 cUsers.Add(item);
                             }, 10);


                             var oUsers = cUsers.ToList();
                             oUsers.ToList().Select(a => a.DiscordId).Distinct().ToList().ForEach(item =>
                             {
                                 if(item == 0) return;
                                 var l = oUsers.Where(a => a.DiscordId == item).ToList();
                                 if (l.Count > 1)
                                 {
                                     var pending = l.Where(a => a.IsPending).ToList();
                                     if (pending.Count == l.Count)
                                     {
                                         l.Remove(pending[0]);
                                         oUsers.Remove(pending[0]);
                                         pending.RemoveAt(0);
                                     }

                                     pending.ForEach(d =>
                                     {
                                         l.Remove(d);
                                         oUsers.Remove(d);
                                     });
                                     if (l.Count > 1)
                                     {
                                         l.RemoveAt(0);
                                         l.ForEach(d => { oUsers.Remove(d); });                                        
                                     }
                                 }

                             });

                             foreach (var a in oUsers)
                             {
                                 a.Id = 0;
                                 await SaveAuthUserEx(a, true);
                             }

                             await RunCommand("DROP TABLE `userTokens`;");
                             await LogHelper.LogWarning("Step 1 finished...");

                             //text fixes
                             await RunCommand("DROP TABLE `hrmAuth`;");
                             await RunCommand("CREATE TABLE `hrmAuth` ( `id` int UNIQUE PRIMARY KEY NOT NULL, `time` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP, `code` TEXT NOT NULL);");
                             await RunCommand("DROP TABLE `fleetup`;");
                             await RunCommand("CREATE TABLE `fleetup` ( `id` int UNIQUE PRIMARY KEY NOT NULL, `announce` int NOT NULL DEFAULT 0);");
                             await RunCommand("DROP TABLE `mail`;");
                             await RunCommand("CREATE TABLE `mail` ( `id` int UNIQUE PRIMARY KEY NOT NULL, `mailId` int DEFAULT 0);");
                             await RunCommand("DROP TABLE `timersAuth`;");
                             await RunCommand("CREATE TABLE `timersAuth` ( `id` int UNIQUE PRIMARY KEY NOT NULL, `time` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP);");
                             await LogHelper.LogWarning("Step 2 finished...");

                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;

                         case "1.2.15":
                             if (SettingsManager.Settings.Database.DatabaseProvider == "sqlite")
                             {
                                 await RunCommand("drop table killFeedCache;");

                                 await RunCommand("alter table authUsers rename to auth_users;");
                                 await RunCommand("alter table cacheData rename to cache_data;");
                                 await RunCommand("alter table hrmAuth rename to hrm_auth;");
                                 await RunCommand("alter table invGroups rename to inv_groups;");
                                 await RunCommand("alter table invTypes rename to inv_types;");
                                 await RunCommand("alter table mapConstellations rename to map_constellations;");
                                 await RunCommand("alter table mapRegions rename to map_regions;");
                                 await RunCommand("alter table mapSolarSystems rename to map_solar_systems;");
                                 await RunCommand("alter table notificationsList rename to notifications_list;");
                                 await RunCommand("alter table nullCampaigns rename to null_campaigns;");
                                 await RunCommand("alter table pendingUsers rename to pending_users;");
                                 await RunCommand("alter table refreshTokens rename to refresh_tokens;");
                                 await RunCommand("alter table standAuth rename to stand_auth;");
                                 await RunCommand("alter table timersAuth rename to timers_auth;");
                             }
                             if (SettingsManager.Settings.Database.DatabaseProvider == "mysql")
                             {
                                 await RunCommand("drop table killfeedcache;");

                                 await RunCommand("alter table authusers rename to auth_users;");
                                 await RunCommand("alter table cachedata rename to cache_data;");
                                 await RunCommand("alter table hrmauth rename to hrm_auth;");
                                 await RunCommand("alter table invgroups rename to inv_groups;");
                                 await RunCommand("alter table invtypes rename to inv_types;");
                                 await RunCommand("alter table mapconstellations rename to map_constellations;");
                                 await RunCommand("alter table mapregions rename to map_regions;");
                                 await RunCommand("alter table mapsolarsystems rename to map_solar_systems;");
                                 await RunCommand("alter table notificationslist rename to notifications_list;");
                                 await RunCommand("alter table nullcampaigns rename to null_campaigns;");
                                 await RunCommand("alter table pendingusers rename to pending_users;");
                                 await RunCommand("alter table refreshtokens rename to refresh_tokens;");
                                 await RunCommand("alter table standauth rename to stand_auth;");
                                 await RunCommand("alter table timersauth rename to timers_auth;");

                                 if(!string.IsNullOrEmpty(SettingsManager.Settings.Database.DatabaseName))
                                     await RunCommand($"ALTER DATABASE `{SettingsManager.Settings.Database.DatabaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
                             }

                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.2.16":
                             await BackupDatabase();

                             var pUsers = await GetPendingUsersEx();

                             await RunCommand("drop table pending_users;");
                             await RunCommand("ALTER TABLE `auth_users` ADD COLUMN `reg_code` TEXT;");
                             await RunCommand("ALTER TABLE `auth_users` ADD COLUMN `reg_date` timestamp;");

                             foreach (var user in pUsers.Where(a=> a.Active))
                             {
                                 var dbentry = await GetAuthUserByCharacterId(user.CharacterId);
                                 if (dbentry != null)
                                 {
                                     dbentry.RegCode = user.AuthString;
                                     dbentry.CreateDate = user.CreateDate;
                                     await SaveAuthUser(dbentry);
                                 }
                                 else
                                 {
                                     var au = new AuthUserEntity
                                     {
                                         CharacterId = user.CharacterId,
                                         DiscordId = 0,
                                         RegCode = user.AuthString,
                                         AuthState = 0,
                                         CreateDate = user.CreateDate,
                                         Data = new AuthUserData()
                                     };
                                     await au.UpdateData();
                                     await SaveAuthUser(au);
                                 }
                             }

                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                             //MYSQL HAS BEEN ADDED HERE
                         case "1.2.19":
                             await Delete("notifications_list", "id", 999990000);
                             break;
                         case "1.3.1":
                             await RunCommand("ALTER TABLE `auth_users` ADD COLUMN `dump_date` timestamp NULL;");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.3.2":
                             if(SettingsManager.Settings.Database.DatabaseProvider == "sqlite")
                                 await RunCommand("CREATE TABLE `sovIndexTracker` ( `groupName` TEXT UNIQUE NOT NULL, `data` TEXT NOT NULL);");
                             else
                                 await RunCommand("CREATE TABLE `sovIndexTracker` ( `groupName` VARCHAR(100) UNIQUE NOT NULL, `data` TEXT NOT NULL);");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.3.4":
                             await RunCommand("ALTER TABLE `auth_users` ADD COLUMN `main_character_id` bigint NULL;");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.3.10":
                             await RunCommand("CREATE TABLE `web_editor_auth` ( `id` int UNIQUE PRIMARY KEY NOT NULL, `code` TEXT NOT NULL, `time` timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP);");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.3.16":
                             await RunCommand("ALTER TABLE `refresh_tokens` ADD COLUMN `indtoken` bigint NULL;");
                             await RunCommand("CREATE TABLE `industry_jobs` (`character_id` bigint UNIQUE NOT NULL, `personal_jobs` TEXT NULL, `corporate_jobs` TEXT NULL);");
                             await RunCommand("DELETE FROM `contracts`;");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;
                         case "1.4.2":
                             await RunCommand("ALTER TABLE `auth_users` ADD COLUMN `last_check` timestamp NULL;");
                             await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                             break;*/

                        #endregion

                        #region Actual Upgrades

                        case "1.4.5":
                            await RunCommand("ALTER TABLE `auth_users` ADD COLUMN `ip` text NULL;");
                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                            break;
                        case "1.5.4":
                            await RunCommand("create unique index timers_id_uindex on timers(id);");
                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                            break;
                        case "2.0.1":
                            await BackupDatabase();
                            if (SettingsManager.Settings.Database.DatabaseProvider.Equals("sqlite",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                await RunCommand(
                                    @"create table tokens (id integer not null constraint tokens_pk primary key autoincrement,	token text not null,	type int not null,	character_id integer not null);");
                                await RunCommand(@"create index tokens_character_id_index on tokens (character_id);");
                                await RunCommand(
                                    @"create unique index tokens_character_id_type_uindex on tokens (character_id, type);");
                                await RunCommand(@"create unique index tokens_id_uindex on tokens (id);");
                            }
                            else
                            {
                                await RunCommand(
                                    @"create table tokens(id int key auto_increment,	token text not null, type int not null,	character_id int not null);");
                                await RunCommand(@"create index tokens_character_id_index on tokens (character_id);");
                                await RunCommand(
                                    @"create unique index tokens_character_id_type_uindex on tokens (character_id, type);");
                                await RunCommand(@"create unique index tokens_id_uindex on tokens (id);");
                            }

                            await LogHelper.LogWarning("Step 1 finished...");

                            //notifications
                            var tokens = (await SelectData("select id,token from refresh_tokens"))
                                .Where(a => !string.IsNullOrEmpty((string) a[1]))
                                .ToDictionary(a => Convert.ToInt64(a[0]), a => (string) a[1]);
                            foreach (var (key, value) in tokens)
                            {
                                //await DbHelper.UpdateToken(value, key, TokenEnum.Notification);
                                await RunCommand(
                                    @$"insert into tokens(token, type, character_id) values('{value}', {(int) TokenEnum.Notification}, {key});");
                            }

                            //contracts
                            tokens.Clear();
                            tokens = (await SelectData("select id,ctoken from refresh_tokens"))
                                .Where(a => !string.IsNullOrEmpty((string) a[1]))
                                .ToDictionary(a => Convert.ToInt64(a[0]), a => (string) a[1]);
                            foreach (var (key, value) in tokens)
                            {
                                //await DbHelper.UpdateToken(value, key, TokenEnum.Contract);
                                await RunCommand(
                                    @$"insert into tokens(token, type, character_id) values('{value}', {(int) TokenEnum.Contract}, {key});");
                            }

                            //mail
                            tokens.Clear();
                            tokens = (await SelectData("select id,mail from refresh_tokens"))
                                .Where(a => !string.IsNullOrEmpty((string) a[1]))
                                .ToDictionary(a => Convert.ToInt64(a[0]), a => (string) a[1]);
                            foreach (var (key, value) in tokens)
                            {
                                //await DbHelper.UpdateToken(value, key, TokenEnum.Mail);
                                await RunCommand(
                                    @$"insert into tokens(token, type, character_id) values('{value}', {(int) TokenEnum.Mail}, {key});");
                            }

                            //industry
                            tokens.Clear();
                            tokens = (await SelectData("select id,indtoken from refresh_tokens"))
                                .Where(a => !string.IsNullOrEmpty((string) a[1]))
                                .ToDictionary(a => Convert.ToInt64(a[0]), a => (string) a[1]);
                            foreach (var (key, value) in tokens)
                            {
                                //await DbHelper.UpdateToken(value, key, TokenEnum.Industry);
                                await RunCommand(
                                    @$"insert into tokens(token, type, character_id) values('{value}', {(int) TokenEnum.Industry}, {key});");
                            }

                            //general
                            tokens.Clear();
                            var data = (await SelectData("select characterID,refreshToken from auth_users"))
                                .Where(a => !string.IsNullOrEmpty((string) a[1]));
                            foreach (var d in data)
                            {
                                var key = Convert.ToInt64(d[0]);
                                var value = (string) d[1];
                                //await DbHelper.UpdateToken(value, key, TokenEnum.General);
                                await RunCommand(
                                    @$"insert into tokens(token, type, character_id) values('{value}', {(int) TokenEnum.General}, {key});");
                            }

                            await LogHelper.LogWarning("Step 2 finished...");
                            await RunCommand(@"drop table refresh_tokens;");
                            await LogHelper.LogWarning("Step 3 finished...");


                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                            break;
                        case "2.0.2":
                            await BackupDatabase();
                            if (SettingsManager.Settings.Database.DatabaseProvider.Equals("sqlite",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                await RunCommand(
                                    @"create table mining_notifications(citadel_id int not null constraint mining_notifications_pk primary key, ore_composition text not null, operator text not null,date timestamp not null);");
                                await RunCommand(
                                    "create unique index mining_notifications_citadel_id_uindex on mining_notifications(citadel_id);");
                            }
                            else
                            {
                                await RunCommand(
                                    @"create table mining_notifications(citadel_id int key, ore_composition text not null, operator text not null,date timestamp not null);");
                                await RunCommand(
                                    "create unique index mining_notifications_citadel_id_uindex on mining_notifications(citadel_id);");

                            }

                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                            break;
                        case "2.0.3":
                            await BackupDatabase();
                            if (SettingsManager.Settings.Database.DatabaseProvider.Equals("sqlite",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                await RunCommand(
                                    "create table mining_ledger(id integer not null constraint mining_ledger_pk primary key autoincrement, citadel_id integer not null, date timestamp,ore_json text);");
                            }
                            else
                            {
                                await RunCommand(
                                    "create table mining_ledger(bigint int not null key auto_increment,citadel_id bigint not null, date timestamp,ore_json text);");
                                await RunCommand(
                                    "ALTER TABLE `mining_notifications` CHANGE COLUMN `citadel_id` `citadel_id` BIGINT NOT NULL;");
                                await RunCommand(
                                    "ALTER TABLE `tokens` CHANGE COLUMN `id` `id` BIGINT NOT NULL;");
                            }

                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                            break;
                        case "2.0.4":
                            await BackupDatabase();
                            if (SettingsManager.Settings.Database.DatabaseProvider.Equals("sqlite",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                await RunCommand(
                                    "create table moon_table(id integer not null constraint moon_table_pk primary key autoincrement, ore_id integer not null,ore_quantity real not null,system_id integer not null,planet_id integer not null,moon_id integer not null,region_id integer not null, ore_name text not null, moon_name text not null);");
                            }
                            else
                            {
                                await RunCommand(
                                    "create table moon_table(id bigint not null key auto_increment,ore_id bigint not null,ore_quantity double not null,system_id bigint not null,planet_id bigint not null,moon_id bigint not null,region_id bigint not null, ore_name text not null, moon_name text not null);");
                            }

                            await RunCommand("create unique index moon_table_id_uindex on moon_table(id);");
                            await RunCommand("create index moon_table_ore_id_index on moon_table(ore_id);");
                            await RunCommand("create index moon_table_system_id_index on moon_table(system_id);");
                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");

                            break;
                        case "2.0.5":
                            await BackupDatabase();

                            if (SettingsManager.Settings.Database.DatabaseProvider.Equals("sqlite",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                await RunCommand(
                                    "create table storage_console(id integer not null constraint storage_console_pk primary key autoincrement, name text not null,value numeric not null);");
                                await RunCommand(
                                    "create unique index storage_console_id_uindex on storage_console(id);");
                                await RunCommand(
                                    "create unique index storage_console_name_uindex on storage_console(name);");
                            }
                            else
                            {
                                await RunCommand(
                                    "create table storage_console(id bigint not null key auto_increment, name text not null,value numeric not null);");
                                await RunCommand(
                                    "create unique index storage_console_id_uindex on storage_console(id);");
                                await RunCommand(
                                    "create unique index storage_console_name_uindex on storage_console(name(255) ASC);");
                            }

                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");

                            break;
                        case "2.0.6":
                            await BackupDatabase();
                            if (SettingsManager.Settings.Database.DatabaseProvider.Equals("sqlite",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                await RunCommand(
                                    "create table inv_custom_scheme(id integer not null, item_id integer not null,quantity int default 0 not null);");
                                await RunCommand(
                                    "create unique index inv_custom_scheme_id_item_id_uindex on inv_custom_scheme(id, item_id);");
                                await RunCommand(
                                    "create index inv_custom_scheme_item_id_index on inv_custom_scheme(item_id);");
                            }
                            else
                            {
                                await RunCommand(
                                    "create table inv_custom_scheme(id bigint not null, item_id bigint not null,quantity int default 0 not null);");
                                await RunCommand(
                                    "create unique index inv_custom_scheme_id_item_id_uindex on inv_custom_scheme(id, item_id);");
                                await RunCommand(
                                    "create index inv_custom_scheme_item_id_index on inv_custom_scheme(item_id);");
                            }

                            await RunCommand(@"
insert into inv_custom_scheme (id, item_id, quantity) values (45510, 16634, 20);
insert into inv_custom_scheme (id, item_id, quantity) values (45510, 16640, 20);
insert into inv_custom_scheme (id, item_id, quantity) values (45510, 16642, 10);
insert into inv_custom_scheme (id, item_id, quantity) values (45510, 16650, 22);

insert into inv_custom_scheme (id, item_id, quantity) values (46312, 16634, 23);
insert into inv_custom_scheme (id, item_id, quantity) values (46312, 16640, 23);
insert into inv_custom_scheme (id, item_id, quantity) values (46312, 16642, 12);
insert into inv_custom_scheme (id, item_id, quantity) values (46312, 16650, 25);

insert into inv_custom_scheme (id, item_id, quantity) values (46313, 16634, 40);
insert into inv_custom_scheme (id, item_id, quantity) values (46313, 16640, 40);
insert into inv_custom_scheme (id, item_id, quantity) values (46313, 16642, 20);
insert into inv_custom_scheme (id, item_id, quantity) values (46313, 16650, 44);

insert into inv_custom_scheme (id, item_id, quantity) values (45513, 16636, 20); 
insert into inv_custom_scheme (id, item_id, quantity) values (45513, 16638, 20); 
insert into inv_custom_scheme (id, item_id, quantity) values (45513, 16643, 10); 
insert into inv_custom_scheme (id, item_id, quantity) values (45513, 16653, 22); 

insert into inv_custom_scheme (id, item_id, quantity) values (46318, 16636, 23); 
insert into inv_custom_scheme (id, item_id, quantity) values (46318, 16638, 23); 
insert into inv_custom_scheme (id, item_id, quantity) values (46318, 16643, 12); 
insert into inv_custom_scheme (id, item_id, quantity) values (46318, 16653, 25); 

insert into inv_custom_scheme (id, item_id, quantity) values (46319, 16636, 40); 
insert into inv_custom_scheme (id, item_id, quantity) values (46319, 16638, 40); 
insert into inv_custom_scheme (id, item_id, quantity) values (46319, 16643, 20); 
insert into inv_custom_scheme (id, item_id, quantity) values (46319, 16653, 44); 

insert into inv_custom_scheme (id, item_id, quantity) values (45511, 16635, 20); 
insert into inv_custom_scheme (id, item_id, quantity) values (45511, 16637, 20); 
insert into inv_custom_scheme (id, item_id, quantity) values (45511, 16641, 10); 
insert into inv_custom_scheme (id, item_id, quantity) values (45511, 16651, 22); 

insert into inv_custom_scheme (id, item_id, quantity) values (46314, 16635, 23); 
insert into inv_custom_scheme (id, item_id, quantity) values (46314, 16637, 23); 
insert into inv_custom_scheme (id, item_id, quantity) values (46314, 16641, 12); 
insert into inv_custom_scheme (id, item_id, quantity) values (46314, 16651, 25); 

insert into inv_custom_scheme (id, item_id, quantity) values (46315, 16635, 40); 
insert into inv_custom_scheme (id, item_id, quantity) values (46315, 16637, 40); 
insert into inv_custom_scheme (id, item_id, quantity) values (46315, 16641, 20); 
insert into inv_custom_scheme (id, item_id, quantity) values (46315, 16651, 44); 

insert into inv_custom_scheme (id, item_id, quantity) values (45512, 16633, 20); 
insert into inv_custom_scheme (id, item_id, quantity) values (45512, 16639, 20); 
insert into inv_custom_scheme (id, item_id, quantity) values (45512, 16644, 10); 
insert into inv_custom_scheme (id, item_id, quantity) values (45512, 16652, 22); 

insert into inv_custom_scheme (id, item_id, quantity) values (46316, 16633, 23); 
insert into inv_custom_scheme (id, item_id, quantity) values (46316, 16639, 23); 
insert into inv_custom_scheme (id, item_id, quantity) values (46316, 16644, 12); 
insert into inv_custom_scheme (id, item_id, quantity) values (46316, 16652, 25); 

insert into inv_custom_scheme (id, item_id, quantity) values (46317, 16633, 40); 
insert into inv_custom_scheme (id, item_id, quantity) values (46317, 16639, 40); 
insert into inv_custom_scheme (id, item_id, quantity) values (46317, 16644, 20); 
insert into inv_custom_scheme (id, item_id, quantity) values (46317, 16652, 44); 

insert into inv_custom_scheme (id, item_id, quantity) values (45502, 16634, 15); 
insert into inv_custom_scheme (id, item_id, quantity) values (45502, 16640, 10); 
insert into inv_custom_scheme (id, item_id, quantity) values (45502, 16649, 50); 

insert into inv_custom_scheme (id, item_id, quantity) values (46304, 16634, 17); 
insert into inv_custom_scheme (id, item_id, quantity) values (46304, 16640, 12); 
insert into inv_custom_scheme (id, item_id, quantity) values (46304, 16649, 58); 

insert into inv_custom_scheme (id, item_id, quantity) values (46305, 16634, 30); 
insert into inv_custom_scheme (id, item_id, quantity) values (46305, 16640, 20); 
insert into inv_custom_scheme (id, item_id, quantity) values (46305, 16649, 100);

insert into inv_custom_scheme (id, item_id, quantity) values (45503, 16636, 15); 
insert into inv_custom_scheme (id, item_id, quantity) values (45503, 16638, 10); 
insert into inv_custom_scheme (id, item_id, quantity) values (45503, 16648, 50); 

insert into inv_custom_scheme (id, item_id, quantity) values (46306, 16636, 17); 
insert into inv_custom_scheme (id, item_id, quantity) values (46306, 16638, 12); 
insert into inv_custom_scheme (id, item_id, quantity) values (46306, 16648, 58); 

insert into inv_custom_scheme (id, item_id, quantity) values (46307, 16636, 30); 
insert into inv_custom_scheme (id, item_id, quantity) values (46307, 16638, 20); 
insert into inv_custom_scheme (id, item_id, quantity) values (46307, 16648, 100);

insert into inv_custom_scheme (id, item_id, quantity) values (45504, 16633, 15); 
insert into inv_custom_scheme (id, item_id, quantity) values (45504, 16639, 10); 
insert into inv_custom_scheme (id, item_id, quantity) values (45504, 16647, 50); 

insert into inv_custom_scheme (id, item_id, quantity) values (46308, 16633, 17); 
insert into inv_custom_scheme (id, item_id, quantity) values (46308, 16639, 12); 
insert into inv_custom_scheme (id, item_id, quantity) values (46308, 16647, 58); 

insert into inv_custom_scheme (id, item_id, quantity) values (46309, 16633, 30); 
insert into inv_custom_scheme (id, item_id, quantity) values (46309, 16639, 20); 
insert into inv_custom_scheme (id, item_id, quantity) values (46309, 16647, 100);

insert into inv_custom_scheme (id, item_id, quantity) values (45506, 16635, 15); 
insert into inv_custom_scheme (id, item_id, quantity) values (45506, 16637, 10); 
insert into inv_custom_scheme (id, item_id, quantity) values (45506, 16646, 50); 

insert into inv_custom_scheme (id, item_id, quantity) values (46310, 16635, 15); 
insert into inv_custom_scheme (id, item_id, quantity) values (46310, 16637, 10); 
insert into inv_custom_scheme (id, item_id, quantity) values (46310, 16646, 50); 

insert into inv_custom_scheme (id, item_id, quantity) values (46311, 16635, 15); 
insert into inv_custom_scheme (id, item_id, quantity) values (46311, 16637, 10); 
insert into inv_custom_scheme (id, item_id, quantity) values (46311, 16646, 50); 

");

                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                            break;
                        case "2.0.7":
                            if (!SettingsManager.Settings.Database.DatabaseProvider.Equals("sqlite",
                                    StringComparison.OrdinalIgnoreCase))
                                await RunCommand("alter table `tokens` modify `id` BIGINT AUTO_INCREMENT;");
                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                            break;
                        case "2.0.8":
                        case "2.0.9":
                            await BackupDatabase();
                            try
                            {
                                await RunCommand("drop table `timers_auth`;");
                            }
                            catch
                            {
                            }

                            try
                            {
                                await RunCommand("drop table `web_editor_auth`;");
                            }
                            catch
                            {
                            }

                            try
                            {
                                await RunCommand("drop table `hrm_auth`;");
                            }
                            catch
                            {
                            }

                            try
                            {
                                await RunCommand("drop table `fleetup`;");
                            }
                            catch
                            {
                            }

                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                            break;
                        case "2.0.10":
                            await BackupDatabase();
                            await RunCommand("alter table mining_ledger add stats text; ");
                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                            break;
                        case "2.0.15":
                            await BackupDatabase();
                            await RunCommand(
                                $"alter table tokens add roles {(SettingsManager.Settings.Database.DatabaseProvider.Equals("sqlite", StringComparison.OrdinalIgnoreCase) ? "integer" : "BIGINT")}; ");
                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                            break;
                        case "2.0.16":
                            await BackupDatabase();

                            await RunCommand($"alter table tokens add scopes TEXT; ");

                            if (isSqlite)
                            {
                                await RunCommand(
                                    $"create table cache_data_dg_tmp(	name TEXT,	data TEXT, constraint cache_data_pk primary key (name) );");
                                await RunCommand(
                                    $"insert into cache_data_dg_tmp(name, data) select name, data from cache_data;");
                                await RunCommand($"drop table cache_data;");
                                await RunCommand($"alter table cache_data_dg_tmp rename to cache_data;");
                                await RunCommand($"create unique index cacheData_name_uindex on cache_data(name); ");

                                await RunCommand(
                                    "create table cache_dg_tmp (	type text not null,	id text not null,	lastAccess timestamp default CURRENT_TIMESTAMP not null,	lastUpdate timestamp default CURRENT_TIMESTAMP not null,	text text not null,	days integer default 1 not null,	constraint cache_pk		primary key (type, id));");
                                await RunCommand(
                                    "insert into cache_dg_tmp(type, id, lastAccess, lastUpdate, text, days) select type, id, lastAccess, lastUpdate, text, days from cache;");
                                await RunCommand("drop table cache;");
                                await RunCommand("alter table cache_dg_tmp rename to cache;");
                                await RunCommand("create index typeIdIndex on cache(type, id); ");
                            }
                            else
                            {
                                await RunCommand(
                                    "alter table cache add constraint cache_pk primary key(type(256), id(256)); ");
                                await RunCommand("alter table cache_data drop key cacheData_name_uindex;");
                                await RunCommand(
                                    "alter table cache_data add constraint cacheData_name_uindex primary key(name(256)); ");
                            }

                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                            break;

                        case "2.0.18":
                            await BackupDatabase();

                            var stands = await DbHelper.GetAllAuthStands();
                            foreach (var item in stands)
                                await DbHelper.UpdateToken(item.Token, item.CharacterId, TokenEnum.AuthStandings);

                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                            break;
                        case "2.0.19":
                            await BackupDatabase();

                            if (isSqlite)
                            {
                                await RunCommand(
                                    "create table null_campaigns_dg_tmp ( groupKey text not null, campaignId INTEGER not null, time timestamp not null, data TEXT not null, lastAnnounce INTEGER default 0 not null, constraint null_campaigns_pk  primary key(groupKey, campaignId))");
                                await RunCommand(
                                    "insert into null_campaigns_dg_tmp(groupKey, campaignId, time, data, lastAnnounce) select groupKey, campaignId, time, data, lastAnnounce from null_campaigns;");
                                await RunCommand("drop table null_campaigns;");
                                await RunCommand("alter table null_campaigns_dg_tmp rename to null_campaigns;");
                                await RunCommand(
                                    "create unique index nullCampaigns_groupKey_campaignId_uindex on null_campaigns(groupKey, campaignId); create index nullCampaigns_groupKey_uindex on null_campaigns(groupKey);");
                            }
                            else
                            {
                                await RunCommand(
                                    "alter table null_campaigns add constraint null_campaigns_pk primary key(groupKey(256), campaignId); ");
                            }

                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");

                            break;
                        case "2.0.20":

                            await BackupDatabase();
                            if (isSqlite)
                            {
                                await RunCommand(
                                    "create table notifications_list_dg_tmp (groupName TEXT not null, filterName TEXT not null, id int not null, time timestamp default CURRENT_TIMESTAMP not null, constraint notifications_list_pk primary key(groupName, filterName));");
                                await RunCommand(
                                    "insert into notifications_list_dg_tmp(groupName, filterName, id, time) select groupName, filterName, id, time from notifications_list;");
                                await RunCommand("drop table notifications_list;");
                                await RunCommand("alter table notifications_list_dg_tmp rename to notifications_list;");
                            }
                            else
                            {
                                await RunCommand(
                                    "alter table notifications_list add constraint notifications_list_pk primary key(groupName(256), filterName(256)); ");

                            }

                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");

                            break;
                        case "2.1.0":
                        {
                            await BackupDatabase();
                            await RunCommand("alter table moon_table add region_name text;");
                            await RunCommand("alter table moon_table add system_name text;");
                            await RunCommand("alter table moon_table add planet_name text;");
                            await RunCommand("alter table moon_table add notes text;");

                            await using var db = new ThunderedDbContext();
                            var regions = db.StarRegions.ToList();

                            foreach (var moon in db.MoonTable.ToList())
                            {
                                moon.RegionName = regions.FirstOrDefault(a => a.RegionId == moon.RegionId)?.RegionName;
                                var breakIndex = moon.MoonName.LastIndexOf('-');
                                var fPart = moon.MoonName.Substring(0, breakIndex).Trim().Split(' ');
                                moon.PlanetName = fPart[1];
                                moon.SystemName = fPart[0];
                                moon.MoonName = moon.MoonName
                                    .Substring(breakIndex + 1, moon.MoonName.Length - (breakIndex + 1)).Trim();
                            }

                            await db.SaveChangesAsync();
                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                        }
                            break;
                        case "2.1.1":
                        {
                            await BackupDatabase();

                            await using var db = new ThunderedDbContext();
                            foreach (var item in db.MoonTable)
                                item.MoonName = item.MoonName.TrimEnd('*');
                            await db.SaveChangesAsync();

                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                        }
                            break;
                        case "2.1.2":
                        {
                            await RunCommand("alter table mining_ledger add refine_eff int default 0 not null;");
                            await RunCommand("alter table mining_ledger add payment_settings text;");
                            await RunCommand("alter table mining_ledger add payment_data text;");
                            await RunCommand("alter table mining_ledger add ledger_data text;");
                            await RunCommand("alter table mining_ledger add closed int default 0 not null;");
                            await UpdateDatabaseVersion(update);
                            await LogHelper.LogWarning($"Upgrade to DB version {update} is complete!");
                        }
                            break;

                        #endregion

                        default:
                            continue;
                    }
                }

                //update version in DB
                /*InsertOrUpdate("cache_data", new Dictionary<string, object>
                {
                    { "name", "version"},
                    { "data", Program.VERSION}
                }).GetAwaiter().GetResult();*/


                return true;
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx("Upgrade", ex, LogCat.Database);
                return false;
            }
            finally
            {
                SettingsManager.IsNew = false;
            }
        }
        #endregion

        #region Upgrade logic
        private const string MIN_SUPPORTED_UPG_VERSION = "1.5.4";

        public static async Task<bool> UpgradeV2()
        {
            await using var db = new ThunderedDbContext();

            var version = await DbHelper.GetCacheDataEntry("version");
            var isNew = version == null || SettingsManager.IsNew;
            var vDbVersion = isNew ? new Version(Program.VERSION) : new Version(version.Data);

            //database is up to date
            if (!isNew && new Version(Program.VERSION) == new Version(version.Data))
                return true;

            try
            {
                var firstUpdate = new Version(MIN_SUPPORTED_UPG_VERSION);
                var isSqlite = SettingsManager.Settings.Database.DatabaseProvider.Equals("sqlite",
                    StringComparison.OrdinalIgnoreCase);
                if (vDbVersion < firstUpdate)
                {
                    await LogHelper.LogError(
                        "Your database version is below the required minimum for an upgrade. You have to do clean install without the ability to migrate your data. Consult GitHub WIKI or reach @panthernet#1659 on Discord group for assistance.");
                    return false;
                }

                var upgradeList = new List<Tuple<Version, MethodInfo, string>>();
                foreach (var methodInfo in typeof(SQLHelper).GetMethods(BindingFlags.Static|BindingFlags.NonPublic))
                {
                    var attr = (DBUpgradeAttribute) methodInfo.GetCustomAttribute(typeof(DBUpgradeAttribute));
                    if (attr == null) continue;
                    if (attr.VersionNumber > vDbVersion)
                        upgradeList.Add(new Tuple<Version, MethodInfo, string>(attr.VersionNumber, methodInfo, attr.Version));
                }

                upgradeList = upgradeList.OrderBy(a => a.Item1).ToList();
                foreach (var item in upgradeList)
                {
                    var result = await (Task<bool>) item.Item2.Invoke(null, new object[] {isSqlite, item.Item3});
                    if (!result)
                    {
                        await LogHelper.LogWarning("Database upgrade has been stopped due to error!",  LogCat.Database);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx("UpgradeV2", ex, LogCat.Database);
                return false;
            }
            finally
            {
                SettingsManager.IsNew = false;
            }
        }
        #endregion

        #region TOOLS

        private static async Task BackupDatabase(string bkFile = null)
        {
            if (SettingsManager.Settings.Database.DatabaseProvider != "sqlite") return;
            try
            {
                bkFile ??= $"{SettingsManager.DatabaseFilePath}.bk";
                if (File.Exists(bkFile))
                    File.Delete(bkFile);
                using (var source = new SqliteConnection($"Data Source = {SettingsManager.DatabaseFilePath};"))
                using (var target = new SqliteConnection($"Data Source = {bkFile};"))
                {
                    await source.OpenAsync();
                    await target.OpenAsync();
                    source.BackupDatabase(target);
                }
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx("DbBackup", ex, LogCat.Database);

            }
        }

        private static async Task UpdateDatabaseVersion(string version, ThunderedDbContext db = null)
        {
            await DbHelper.UpdateCacheDataEntry("version", version, db);
        }

        private static async Task<bool> UpgradeWrapper(string version, Func<ThunderedDbContext, Task> method)
        {
            if (method == null)
                throw new Exception($"Upgrade method not found!");

            //await BackupDatabase();
            await using var db = new ThunderedDbContext();
            var t = await db.Database.BeginTransactionAsync();
            try
            {
                await method.Invoke(db);
                await db.SaveChangesAsync();
                await UpdateDatabaseVersion(version, db);
                await t.CommitAsync();
                await LogHelper.LogWarning($"Upgrade to DB version {version} is complete!", LogCat.Database);
                return true;
            }
            catch(Exception ex)
            {
                await t.RollbackAsync();
                await LogHelper.LogError($"Upgrade to DB version {version} FAILED!", LogCat.Database);
                await LogHelper.LogEx(ex, LogCat.Database);
                return false;
            }
        }
        #endregion

        #region UPGRADES

        [DBUpgrade("2.1.4")]
        private static async Task<bool> UpgradeV214(bool isSQLite, string version)
        {
            return await UpgradeWrapper(version, async db =>
            {
                if (isSQLite)
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "create table cache_data_dg_tmp(name TEXT not null constraint cache_data_pk primary key, data TEXT);");
                    await db.Database.ExecuteSqlRawAsync(
                        "insert into cache_data_dg_tmp(name, data) select name, data from cache_data;");
                    await db.Database.ExecuteSqlRawAsync("drop table cache_data;");
                    await db.Database.ExecuteSqlRawAsync(
                        "alter table cache_data_dg_tmp rename to cache_data;");
                    await db.Database.ExecuteSqlRawAsync(
                        "create unique index cache_data_name_uindex on cache_data(name);");
                }
                else
                {
                    var list = await db.CacheData.Where(a => a.Equals("version")).ToListAsync();
                    foreach (var item in list)
                        db.CacheData.Remove(item);
                    await db.SaveChangesAsync();
                    await db.Database.ExecuteSqlRawAsync(
                        "create unique index cache_data_name_uindex on cache_data(name(256));");
                }
            });
        }

        [DBUpgrade("2.1.5")]
        private static async Task<bool> UpgradeV215(bool isSQLite, string version)
        {
            return await UpgradeWrapper(version, async db =>
            {
                if (isSQLite)
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "create table fits ( id integer not null constraint fits_pk primary key  autoincrement, `name` text not null, ship_name text not null, group_name text   not null, fit_text   text   not null, skill_data text   not null );");
                }
                else
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "create table fits ( id integer auto_increment, constraint fits_pk primary key (id), `name` text not null, ship_name text not null, group_name text   not null, fit_text   text   not null, skill_data text   not null );");
                }

                await db.Database.ExecuteSqlRawAsync("create unique index fits_id_uindex     on fits (id);");
            });
        }

        [DBUpgrade("2.1.6")]
        private static async Task<bool> UpgradeV216(bool isSQLite, string version)
        {
            return await UpgradeWrapper(version, async db =>
            {
                await db.Database.ExecuteSqlRawAsync("alter table auth_users add alliance_id bigint;");
                await db.Database.ExecuteSqlRawAsync(
                    "alter table auth_users add corporation_id bigint default 0 not null;");
                await db.Database.ExecuteSqlRawAsync("alter table auth_users add character_name text;");

                foreach (var user in db.Users)
                {
                    user.UnpackData();
                    if (user.DataView != null)
                    {
                        user.CorporationId = user.DataView.CorporationId;
                        user.AllianceId = user.DataView.AllianceId;
                        user.CharacterName = user.DataView.CharacterName;
                    }
                }
            });
        }

        [DBUpgrade("2.1.8")]
        private static async Task<bool> UpgradeV218(bool isSQLite, string version)
        {
            return await UpgradeWrapper(version, async db =>
            {
                if (isSQLite)
                {
                    await db.Database.ExecuteSqlRawAsync("create table history_notifications(id bigint  not null constraint history_notifications_pk primary key,type      text     not null,sender_id bigint  not null,sender_type  text      not null,receive_date timestamp not null, data      text     not null);");
                    await db.Database.ExecuteSqlRawAsync("create index history_notifications_sender_id_date_index   on history_notifications (sender_id, receive_date);");
                    await db.Database.ExecuteSqlRawAsync("create index history_notifications_sender_id_index    on history_notifications (sender_id);");
                    await db.Database.ExecuteSqlRawAsync("create index history_notifications_type_date_index    on history_notifications (type, receive_date);");
                    await db.Database.ExecuteSqlRawAsync("create index history_notifications_type_index    on history_notifications (type);");

                    await db.Database.ExecuteSqlRawAsync("create table history_mail(    id           bigint    not null        constraint history_mail_pk            primary key,    sender_id    bigint    not null,    subject      text,    receive_date timestamp not null,    body         text,    labels       text);");
                    await db.Database.ExecuteSqlRawAsync("create index history_mail_labels_index    on history_mail (labels);");
                    await db.Database.ExecuteSqlRawAsync("create index history_mail_sender_id_index    on history_mail (sender_id);");
                    
                    await db.Database.ExecuteSqlRawAsync("create table history_mail_rcp(    id       INTEGER not null        constraint history_mail_rcp_pk            primary key autoincrement,    mail_id  bigint  not null,    rcp_id   bigint  not null,    rcp_type text    not null);");
                    await db.Database.ExecuteSqlRawAsync("create unique index history_mail_rcp_id_uindex    on history_mail_rcp (id);");
                    await db.Database.ExecuteSqlRawAsync("create index history_mail_rcp_mail_id_index    on history_mail_rcp (mail_id);");
                    await db.Database.ExecuteSqlRawAsync("create index history_mail_rcp_rcp_id_index    on history_mail_rcp (rcp_id);");
                    
                    await db.Database.ExecuteSqlRawAsync("create table history_mail_list(    id   bigint not null        constraint history_mail_list_pk            primary key,    name text   not null);");
                    await db.Database.ExecuteSqlRawAsync("create unique index history_mail_list_id_uindex    on history_mail_list (id);");

                }
                else
                {
                    await db.Database.ExecuteSqlRawAsync("create table history_notifications(    id        bigint  not null primary key,    type      text     not null,    sender_id bigint  not null,sender_type  text      not null,receive_date timestamp not null,    data      text     not null);");
                    await db.Database.ExecuteSqlRawAsync("create index history_notifications_sender_id_date_index    on history_notifications (sender_id, receive_date);");
                    await db.Database.ExecuteSqlRawAsync("create index history_notifications_sender_id_index    on history_notifications (sender_id);");
                    await db.Database.ExecuteSqlRawAsync("create index history_notifications_type_date_index    on history_notifications (type(255), receive_date);");
                    await db.Database.ExecuteSqlRawAsync("create index history_notifications_type_index    on history_notifications (type(255));");

                    await db.Database.ExecuteSqlRawAsync("create table history_mail(    id           bigint    not null            primary key,    sender_id    bigint    not null,    subject      text,    receive_date timestamp not null,    body         text,    labels       text);");
                    await db.Database.ExecuteSqlRawAsync("create index history_mail_labels_index    on history_mail (labels(512));");
                    await db.Database.ExecuteSqlRawAsync("create index history_mail_sender_id_index    on history_mail (sender_id);");
                    
                    await db.Database.ExecuteSqlRawAsync("create table history_mail_rcp(id  bigint not null  primary key auto_increment,    mail_id  bigint  not null,    rcp_id   bigint  not null,    rcp_type text    not null);");
                    await db.Database.ExecuteSqlRawAsync("create unique index history_mail_rcp_id_uindex    on history_mail_rcp (id);");
                    await db.Database.ExecuteSqlRawAsync("create index history_mail_rcp_mail_id_index    on history_mail_rcp (mail_id);");
                    await db.Database.ExecuteSqlRawAsync("create index history_mail_rcp_rcp_id_index    on history_mail_rcp (rcp_id);");
                    
                    await db.Database.ExecuteSqlRawAsync("create table history_mail_list(id   bigint not null primary key,    name text   not null);");
                    await db.Database.ExecuteSqlRawAsync("create unique index history_mail_list_id_uindex    on history_mail_list (id);");

                }

                await db.Database.ExecuteSqlRawAsync("create unique index history_mail_rcp_mail_id_rcp_id_uindex on history_mail_rcp (mail_id, rcp_id);");

                await db.Database.ExecuteSqlRawAsync("alter table history_notifications add sender_snapshot text not null;");
                await db.Database.ExecuteSqlRawAsync("alter table history_notifications add sender_corporation_id bigint not null;");
                await db.Database.ExecuteSqlRawAsync("alter table history_notifications add sender_alliance_id bigint;");
                await db.Database.ExecuteSqlRawAsync("alter table history_mail_rcp add rcp_snapshot text;");

                await db.Database.ExecuteSqlRawAsync("alter table history_mail add sender_snapshot text;");
                await db.Database.ExecuteSqlRawAsync("alter table history_mail add sender_corporation_id bigint;");
                await db.Database.ExecuteSqlRawAsync("alter table history_mail add sender_alliance_id bigint;");

                await db.Database.ExecuteSqlRawAsync("alter table history_notifications add feeder_id bigint not null;");
            });
        }

        [DBUpgrade("2.3.1")]
        private static async Task<bool> UpgradeV231(bool isSQLite, string version)
        {
            return await UpgradeWrapper(version, async db =>
            {
                if (isSQLite)
                {
                    await db.Database.ExecuteSqlRawAsync(
                        "create table discord_groups(id integer not null constraint discord_groups_pk primary key autoincrement, `name` text not null, directorCharacterId bigint null, discordRole text not null);");
                }
                else
                {
                    // ugh
                }
            });
        }

        #endregion
    }
}
