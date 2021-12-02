﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dasync.Collections;
using Discord.WebSocket;

using Microsoft.EntityFrameworkCore.Metadata.Internal;

using ThunderED.Classes;
using ThunderED.Classes.Enums;
using ThunderED.Helpers;
using ThunderED.Json;
using ThunderED.Modules.OnDemand;

namespace ThunderED.Modules
{
    public partial class WebAuthModule
    {
        public class RoleSearchResult
        {
            public string GroupName;
            public WebAuthGroup Group;
            public List<SocketRole> UpdatedRoles = new List<SocketRole>();
            public List<string> ValidManualAssignmentRoles = new List<string>();
            public bool IsConnectionError;
            public bool TokenMustBeDeleted;
        }

        internal static async Task UpdateAuthUserRolesFromDiscord(List<string> exemptRoles, List<string> authCheckIgnoreRoles, bool useParallel)
        {
            //check main
            await UpdateAuthUserRolesFromDiscordInternal(SettingsManager.Settings.Config.DiscordGuildId, exemptRoles,
                authCheckIgnoreRoles, useParallel);

            if(SettingsManager.Settings.WebAuthModule.AuxiliaryDiscordGuildIds != null)
                foreach (var guildId in SettingsManager.Settings.WebAuthModule.AuxiliaryDiscordGuildIds)
                {
                    await UpdateAuthUserRolesFromDiscordInternal(guildId, exemptRoles,
                        authCheckIgnoreRoles, useParallel);
                }
        }

        private static async Task UpdateAuthUserRolesFromDiscordInternal(ulong discordGuildId, List<string> exemptRoles,
            List<string> authCheckIgnoreRoles, bool useParallel)
        {
            var discordGuild = APIHelper.DiscordAPI.GetGuild(discordGuildId);
            if(discordGuild == null) return;

            await AuthInfoLog($"Guild ({discordGuild.IsConnected}) has {discordGuild.DownloadedMemberCount} members in cache before update");
            await discordGuild.DownloadUsersAsync();
            await AuthInfoLog($"Guild ({discordGuild.IsConnected}) has {discordGuild.DownloadedMemberCount} members in cache after update");

            if (SettingsManager.Settings.CommandsConfig.EnableRoleManagementCommands && DiscordRolesManagementModule.AvailableRoleNames.Any())
            {
                authCheckIgnoreRoles = authCheckIgnoreRoles.ToList();
                authCheckIgnoreRoles.AddRange(DiscordRolesManagementModule.AvailableRoleNames);
            }
            var idList = APIHelper.DiscordAPI.GetUserIdsFromGuild(discordGuildId);

            //get only discord users which are not present in DB
            var idFromDb = await DbHelper.GetUserDiscordIdsForAuthCheck(int.MaxValue);
            idList = idList.Except(idFromDb).ToList();

            if (useParallel)
            {
                await idList.ParallelForEachAsync(async id =>
                {
                    //make sure we only check not authed users present in Discord group
                    var user = await DbHelper.GetAuthUserByDiscordId(id, true);
                    if (user != null && user.AuthState == (int)UserStatusEnum.Authed) return;
                    //check user roles
                    await UpdateUserRoles(id, exemptRoles, authCheckIgnoreRoles, false, discordGuild);
                }, SettingsManager.MaxConcurrentThreads);
            }
            else
            {
                foreach (var id in idList)
                {
                    var user = await DbHelper.GetAuthUserByDiscordId(id, true);
                    if (user != null && user.AuthState == (int)UserStatusEnum.Authed) return;
                    //check user roles
                    await UpdateUserRoles(id, exemptRoles, authCheckIgnoreRoles, false, discordGuild);
                }
            }
        }

        internal static async Task UpdateAllUserRoles(List<string> exemptRoles, List<string> authCheckIgnoreRoles, bool manual = false)
        {
            if (SettingsManager.Settings.CommandsConfig.EnableRoleManagementCommands && DiscordRolesManagementModule.AvailableRoleNames.Any())
            {
                authCheckIgnoreRoles = authCheckIgnoreRoles.ToList();
                authCheckIgnoreRoles.AddRange(DiscordRolesManagementModule.AvailableRoleNames);
            }
            
            var ids = await DbHelper.GetUserDiscordIdsForAuthCheck(manual ? int.MaxValue : 100);

            await AuthInfoLog($"Fetched {ids?.Count} users to check within this pass", true);
            if (ids == null || !ids.Any()) return;

            var list = new List<ulong>(SettingsManager.Settings.WebAuthModule.AuxiliaryDiscordGuildIds);
            list.Insert(0, SettingsManager.Settings.Config.DiscordGuildId);

            foreach (var guildId in list)
            {
                await AuthInfoLog($"Checking {guildId} Discord guild...");
                var discordGuild = APIHelper.DiscordAPI.GetGuild(guildId);
                if (discordGuild == null)
                {
                    await LogHelper.LogWarning(
                        $"Discord guild {guildId} is null!");
                    continue;
                }

                await AuthInfoLog(
                    $"Guild ({discordGuild.IsConnected}) has {discordGuild.DownloadedMemberCount} members in cache before update");
                await discordGuild.DownloadUsersAsync();
                await AuthInfoLog(
                    $"Guild ({discordGuild.IsConnected}) has {discordGuild.DownloadedMemberCount} members in cache after update");

                await ids.ParallelForEachAsync(async id =>
                {
                    await UpdateUserRoles(id, exemptRoles, authCheckIgnoreRoles, false, discordGuild);
                    await DbHelper.SetAuthUserLastCheck(id, DateTime.Now);
                }, SettingsManager.MaxConcurrentThreads);
            }
        }

        public static async Task<string> UpdateUserRoles(ulong discordUserId, List<string> exemptRoles, List<string> authCheckIgnoreRoles,
            bool forceRemove = false, SocketGuild discordGuild = null)
        {
            try
            {
                discordGuild ??= APIHelper.DiscordAPI.GetGuild(SettingsManager.Settings.Config.DiscordGuildId);
                var u = discordGuild.GetUser(discordUserId);
                var currentUser = APIHelper.DiscordAPI.GetCurrentUser();

                if (u != null && (u.Id == currentUser.Id || u.IsBot || u.Roles.Any(r => exemptRoles.Contains(r.Name))))
                {
                    if(discordGuild.Id == SettingsManager.Settings.Config.DiscordGuildId)
                        await AuthInfoLog(discordUserId, "[RUPD] User is bot or have an exempt role. Skipping roles update...", true);
                    return null;
                }

                if (u == null && (discordUserId == currentUser.Id))
                {
                    if(discordGuild.Id == SettingsManager.Settings.Config.DiscordGuildId)
                        await AuthInfoLog(discordUserId, "[RUPD] Discord user not found. Skipping roles update...", true);
                    return null;
                }

                var grps = SettingsManager.Settings.WebAuthModule.GetEnabledAuthGroups();
                var authUser = await DbHelper.GetAuthUserByDiscordId(discordUserId, true);
                if (authUser != null)
                {
                    if (u == null && !authUser.HasToken && authUser.AuthState != (int)UserStatusEnum.Authed)
                    {
                        await DbHelper.DeleteAuthDataByDiscordId(discordUserId);
                        await LogHelper.LogInfo(
                            $"User {authUser.DataView.CharacterName}[{discordUserId}] has been removed from DB (no token, no auth, not present in discord)");
                        return null;
                    }

                    if (!string.IsNullOrEmpty(authUser.GroupName) && grps.ContainsKey(authUser.GroupName) &&
                        grps[authUser.GroupName].SkipDiscordAuthPage)
                    {
                        await AuthInfoLog(authUser, "[RUPD] Discord-less auth found. Skipping roles update...", true);
                        return null;
                    }

                    //get data
                    var characterData = await APIHelper.ESIAPI.GetCharacterData("authCheck", authUser.CharacterId, true);
                    //skip bad requests
                    if (characterData == null)
                    {
                        await AuthInfoLog(authUser, "[RUPD] Character data is null. Skipping due to bad request.", true);
                        return null;
                    }

                    if (authUser.DataView.CorporationId != characterData.corporation_id || authUser.DataView.AllianceId != (characterData.alliance_id ?? 0))
                    {
                        await authUser.UpdateData(characterData);
                        await DbHelper.SaveAuthUser(authUser);
                    }

                    var remroles = new List<SocketRole>();

                    await AuthInfoLog(characterData, $"[RUPD] PRE CHARID: {authUser.CharacterId} DID: {discordUserId} AUTH: {authUser.AuthState} GRP: {authUser.GroupName} TOKEN: {!string.IsNullOrEmpty(authUser.GetGeneralTokenString())}", true);
                    var result = authUser.AuthState == (int)UserStatusEnum.Dumped ? new RoleSearchResult() : await GetRoleGroup(characterData, discordUserId, discordGuild.Id, authUser.GetGeneralTokenString());
                    if (result.IsConnectionError)
                    {
                        await AuthWarningLog(characterData, "[RUPD] Connection error while searching for group! Skipping roles update.");
                        return null;
                    }

                    if (result.TokenMustBeDeleted)
                    {
                        await LogHelper.LogWarning(
                            $"Invalid token detected for {authUser.DataView.CharacterName}. It will be removed.");
                        await DbHelper.DeleteToken(authUser.CharacterId, TokenEnum.General);
                    }
                    await AuthInfoLog(characterData, $"[RUPD] GRPFETCH GROUP: {result.GroupName} ROLES: {(result.UpdatedRoles == null || !result.UpdatedRoles.Any() ? "null" : string.Join(',', result.UpdatedRoles.Where(a=> !a.Name.StartsWith("@")).Select(a=> a.Name)))} MANUAL: {(result.ValidManualAssignmentRoles == null || !result.ValidManualAssignmentRoles.Any() ? "null" : string.Join(',', result.ValidManualAssignmentRoles))}", true);

                    var isMovingToDump = string.IsNullOrEmpty(result.GroupName) && authUser.AuthState == (int)UserStatusEnum.Authed;
                    var isAuthed = !string.IsNullOrEmpty(result.GroupName);
                    await AuthInfoLog(characterData, $"[RUPD] TODUMP: {isMovingToDump}  ISAUTHED: {isAuthed} FORCED: {forceRemove}", true);

                    var changed = false;
                    if (!isMovingToDump)
                    {
                        if (!string.IsNullOrEmpty(result.GroupName) && !string.IsNullOrEmpty(authUser.GroupName) && result.GroupName != authUser.GroupName)
                        {
                            var oldGroup = GetGroupByName(authUser.GroupName).Value;
                            if (oldGroup != null && (oldGroup.UpgradeGroupNames.ContainsCaseInsensitive(result.GroupName) || oldGroup.DowngradeGroupNames.ContainsCaseInsensitive(result.GroupName)))
                            {
                                await AuthInfoLog(characterData,
                                    $"[RUPD] Character has been transferred from {authUser.GroupName} to {result.GroupName} group");
                                authUser.GroupName = result.GroupName;
                                await DbHelper.SaveAuthUser(authUser);
                            }
                        }
                    }

                    //move to dumpster
                    if (forceRemove || isMovingToDump && authUser.AuthState != (int)UserStatusEnum.Dumped)
                    {
                        if (SettingsManager.Settings.Config.ModuleHRM && SettingsManager.Settings.HRMModule.UseDumpForMembers)
                        {
                            await AuthInfoLog(characterData, $"[RUPD] Character is being moved into dumpster [F:{forceRemove}]...");
                            authUser.SetStateDumpster();
                            if(!forceRemove)
                                authUser.GroupName = null;
                            await authUser.UpdateData();
                            await DbHelper.SaveAuthUser(authUser);
                        }
                        else
                        {
                            await AuthInfoLog(characterData, $"[RUPD] {authUser.DataView.CharacterName}({authUser.CharacterId}) is no longer validated for `{authUser.GroupName}` group and will be deleted!");
                            await DbHelper.DeleteAuthUser(authUser.CharacterId);
                        }
                    }
                    //skip if we don't have discord user (discord-less auth)
                    if (u == null)
                    {
                        await AuthInfoLog(characterData, $"[RUPD] Skipping roles check as discord user is null", true);
                        return null;
                    }

                    var initialUserRoles = new List<SocketRole>(u.Roles);
                    var invalidRoles = initialUserRoles.Where(a => result.UpdatedRoles.FirstOrDefault(b => b.Id == a.Id) == null && !a.Name.StartsWith("@everyone"));
                    foreach (var invalidRole in invalidRoles)
                    {
                        //if role is not ignored
                        if (!authCheckIgnoreRoles.Contains(invalidRole.Name))
                        {
                            // if role is in valid roles and char is not authed
                            if (isAuthed && !result.ValidManualAssignmentRoles.Contains(invalidRole.Name) || !isAuthed)
                            {
                                remroles.Add(invalidRole);
                                changed = true;
                            }
                        }
                    }

                    //remove already assigned roles and mark changed if we have at least one new role to add
                    result.UpdatedRoles.RemoveAll(role => initialUserRoles.FirstOrDefault(a => a.Id == role.Id) != null);
                    changed = changed || result.UpdatedRoles.Count > 0;

                    if (changed)
                    {
                        var actuallyDone = false;
                        if (result.UpdatedRoles.Count > 0)
                        {
                            try
                            {
                                result.UpdatedRoles =
                                    result.UpdatedRoles.Where(a => !a.Name.Equals("@everyone")).ToList();
                                await u.AddRolesAsync(result.UpdatedRoles);
                                actuallyDone = true;
                            }
                            catch(Exception ex)
                            {
                                await LogHelper.LogEx(ex, LogCat.AuthWeb);
                                await AuthWarningLog(characterData, $"[RUPD] Failed to add {string.Join(", ", result.UpdatedRoles.Select(a=> a.Name))} roles to {characterData.name} ({u.Username})!");
                            }
                        }

                        if (remroles.Count > 0)
                        {
                            try
                            {
                                await u.RemoveRolesAsync(remroles);
                                actuallyDone = true;
                            }
                            catch
                            {
                                await AuthWarningLog(characterData, $"[RUPD] Failed to remove {string.Join(", ", remroles.Select(a=> a.Name))} roles from {characterData.name} ({u.Username})!");
                            }
                        }

                        if (actuallyDone)
                        {
                            var stripped = remroles.Count > 0 ? $" {LM.Get("authStripped")}: {string.Join(", ", remroles.Select(a => a.Name))}" : null;
                            var added = result.UpdatedRoles.Count > 0 ? $" {LM.Get("authAddedRoles")}: {string.Join(", ", result.UpdatedRoles.Select(a => a.Name))}" : null;
                            if (SettingsManager.Settings.WebAuthModule.AuthReportChannel != 0)
                            {
                                var channel = discordGuild.GetTextChannel(SettingsManager.Settings.WebAuthModule.AuthReportChannel);
                                if(SettingsManager.Settings.WebAuthModule.AuthReportChannel > 0 && channel == null)
                                    await LogHelper.LogWarning($"Discord channel {SettingsManager.Settings.WebAuthModule.AuthReportChannel} not found!", LogCat.Discord);
                                else await APIHelper.DiscordAPI.SendMessageAsync(channel, $"{LM.Get("renewingRoles")} {characterData.name} ({u.Username}){stripped}{added}");
                            }

                            await AuthInfoLog(characterData, $"[RUPD] Adjusting roles for {characterData.name} ({u.Username}) {stripped}{added}");
                        }
                    }

                    var eveName = characterData.name;

                    if ((SettingsManager.Settings.WebAuthModule.EnforceCorpTickers || SettingsManager.Settings.WebAuthModule.EnforceCharName || SettingsManager.Settings.WebAuthModule.EnforceAllianceTickers) 
                        && !TickManager.IsESIUnreachable && !u.Roles.Select(a=> a.Name).ContainsAnyFromList(authCheckIgnoreRoles))
                    {
                        string alliancePart = null;
                        if (SettingsManager.Settings.WebAuthModule.EnforceAllianceTickers && characterData.alliance_id.HasValue)
                        {
                            var ad = await APIHelper.ESIAPI.GetAllianceData("authCheck", characterData.alliance_id.Value, true);
                            if (characterData.alliance_id.HasValue && ad == null)
                            {
                                //esi fuckup
                            }
                            else alliancePart = ad != null ? $"[{ad.ticker}] " : null;

                        }
                        string corpPart = null;
                        if (SettingsManager.Settings.WebAuthModule.EnforceCorpTickers)
                        {
                            if (!SettingsManager.Settings.WebAuthModule.EnforceSingleTickerPerUser || string.IsNullOrEmpty(alliancePart))
                            {
                                var ad = await APIHelper.ESIAPI.GetCorporationData("authCheck", characterData.corporation_id, true);
                                if (ad == null)
                                {
                                    //esi fuckup
                                }
                                else corpPart = ad != null ? $"[{ad.ticker}] " : null;
                            }
                        }

                        var nickname = $"{alliancePart}{corpPart}{(SettingsManager.Settings.WebAuthModule.EnforceCharName ? eveName : u.Username)}";
                        nickname = nickname.Length > 31
                            ? nickname.Substring(0, 31)
                            : nickname;

                        if (nickname != u.Nickname && !string.IsNullOrWhiteSpace(u.Nickname) || string.IsNullOrWhiteSpace(u.Nickname) && u.Username != nickname)
                        {
                            await AuthInfoLog(characterData, $"[RUPD] Trying to change name of {u.Nickname} to {nickname}");
                            try
                            {
                                await u.ModifyAsync(x => x.Nickname = nickname);
                            }
                            catch
                            {
                                await LogHelper.LogError($"[RUPD] Name change failed, probably due to insufficient rights", LogCat.AuthCheck);
                            }
                        }
                    }

                    return isAuthed ? result.GroupName : null;
                }


                await AuthInfoLog(discordUserId, "[RUPD] Auth user not found. Checking live discord user...", true);


                //auth user not found
                if (u == null) return null;
                var rroles = new List<SocketRole>();
                var rolesOrig = new List<SocketRole>(u.Roles);
                foreach (var role in rolesOrig)
                {
                    if (exemptRoles.FirstOrDefault(x => x == role.Name) == null)
                        rroles.Add(role);
                }

                rolesOrig.Remove(u.Roles.FirstOrDefault(x => x.Name == "@everyone"));
                rroles.Remove(u.Roles.FirstOrDefault(x => x.Name == "@everyone"));

                var hasBeenChanged = false;

                if (rroles != rolesOrig)
                {
                    foreach (var exempt in rroles)
                    {
                        if (exemptRoles.FirstOrDefault(x => x == exempt.Name) == null && !authCheckIgnoreRoles.Contains(exempt.Name))
                            hasBeenChanged = true;
                    }
                }

                if (hasBeenChanged)
                {
                    try
                    {
                        var channel = discordGuild.GetTextChannel(SettingsManager.Settings.WebAuthModule.AuthReportChannel);
                        if(channel != null)
                            await APIHelper.DiscordAPI.SendMessageAsync(channel, $"{LM.Get("resettingRoles")} {u.Username}");
                        var roles = rroles.Where(a => !exemptRoles.Contains(a.Name) && !authCheckIgnoreRoles.Contains(a.Name)).ToList();
                        if (roles.Any())
                        {
                            await AuthInfoLog(discordUserId, $"[RUPD] Resetting roles for {u.Username}: {string.Join(',', roles.Select(a=> a.Name))}");
                            await u.RemoveRolesAsync(roles);
                        }
                    }
                    catch (Exception ex)
                    {
                        await LogHelper.LogEx($"Error removing roles: {ex.Message}", ex, LogCat.AuthCheck);
                    }
                }

                return null;

            }
            catch (Exception ex)
            {
                await LogHelper.LogEx($"Fatal Error: {ex.Message}", ex, LogCat.AuthCheck);
                return null;
            }
        }

        private static async Task UpdateResultRolesWithTitles(SocketGuild discordGuild, List<AuthRoleEntity> roleEntities, RoleSearchResult result, JsonClasses.CharacterData ch, string uToken)
        {
            //process titles in priority
            //TODO titles and general mix?

            var titleEntity = roleEntities.FirstOrDefault(a => a.Titles.Any());
            if (titleEntity != null)
            {
                if (string.IsNullOrEmpty(uToken))
                {
                    await AuthWarningLog(ch,
                        $"User has no ESI token but is being checked against group with Titles! Titles require `esi-characters.read_titles.v1` permissions!");
                    return;
                }

                var userTitles = (await APIHelper.ESIAPI.GetCharacterTitles("AuthCheck", ch.character_id, uToken))?.Select(a=> a.name).ToList();
                if (userTitles != null && userTitles.Any())
                {

                    foreach (var roleTitle in titleEntity.Titles.Values)
                    {
                        if (!roleTitle.TitleNames.ContainsAnyFromList(userTitles)) continue;
                        foreach (var roleName in roleTitle.DiscordRoles)
                        {
                            var role = APIHelper.DiscordAPI.GetGuildRole(roleName);
                            if (role != null && !result.UpdatedRoles.Contains(role))
                                result.UpdatedRoles.Add(role);
                        }
                    }
                }
                else
                {
                    var forEmpty = titleEntity.Titles.FirstOrDefault(a => a.Value.TitleNames.Count == 0 && a.Value.DiscordRoles.Any()).Value;
                    if (forEmpty != null)
                    {
                        foreach (var roleName in forEmpty.DiscordRoles)
                        {
                            var role = APIHelper.DiscordAPI.GetGuildRole(roleName);
                            if (role != null && !result.UpdatedRoles.Contains(role))
                                result.UpdatedRoles.Add(role);
                        }
                    }
                }
            }
            else
            {
                var foundRoles = roleEntities.SelectMany(a => a.DiscordRoles).Distinct();
                var aRoles = discordGuild.Roles.Where(a => foundRoles.Contains(a.Name) && !result.UpdatedRoles.Contains(a));

                foreach (var role in aRoles)
                {
                    if (!result.UpdatedRoles.Contains(role))
                        result.UpdatedRoles.Add(role);
                }
            }
        }

        private static KeyValuePair<string, WebAuthGroup> GetGroupByName(string name)
        {
            if(string.IsNullOrEmpty(name)) 
                return new KeyValuePair<string, WebAuthGroup>();
            var trimmedName = name.Trim();
            return SettingsManager.Settings.WebAuthModule.AuthGroups.FirstOrDefault(a => a.Value.IsEnabled && a.Key.Trim().Equals(trimmedName,StringComparison.OrdinalIgnoreCase));
        }

        public static async Task<RoleSearchResult> GetRoleGroup(JsonClasses.CharacterData characterData, ulong discordUserId, ulong guildId, string refreshToken = null)
        {
            var result = new RoleSearchResult();
            var discordGuildList = APIHelper.DiscordAPI.GetGuildsFromUser(discordUserId);
            //TODO multi guild reg - need to update database

            var mainDiscordGuild = APIHelper.DiscordAPI.GetGuild(guildId);
            var mainDiscordUser = mainDiscordGuild.GetUser(discordUserId);

            try
            {
                if (characterData == null)
                {
                    await AuthWarningLog(discordUserId, "[RG] Aborted due to character data is null");
                    result.IsConnectionError = true;
                    return result;
                }

                if (mainDiscordUser != null)
                    result.UpdatedRoles.Add(mainDiscordUser.Roles.FirstOrDefault(x => x.Name == "@everyone"));


                var groupsToCheck = new Dictionary<string, WebAuthGroup>();
                var authData = await DbHelper.GetAuthUser(characterData.character_id);

                #region Select groups to check
                if (!string.IsNullOrEmpty(authData?.GroupName))
                {
                    await AuthInfoLog(characterData, $"[RG] Has group name {authData.GroupName}", true);

                    //check specified group for roles
                    var (groupName, group) = GetGroupByName(authData.GroupName);
                    if (group != null)
                    {
                        //process upgrade groups first
                        if (group.UpgradeGroupNames.Any())
                        {
                            await AuthInfoLog(characterData, $"[RG] Adding upgrade groups: {string.Join(',', group.UpgradeGroupNames)}", true);

                            foreach (var item in group.UpgradeGroupNames)
                            {
                                var (key, value) = GetGroupByName(item);
                                if (value != null)
                                    groupsToCheck.Add(key, value);
                            }
                        }

                        //add root group
                        groupsToCheck.Add(groupName, group);

                        //add downgrade groups (checked last)
                        if (authData.AuthState == (int)UserStatusEnum.Authed && group.DowngradeGroupNames.Any())
                        {
                            await AuthInfoLog(characterData, $"[RG] Adding downgrade groups: {string.Join(',', group.DowngradeGroupNames)}", true);
                            foreach (var item in group.DowngradeGroupNames)
                            {
                                var (key, value) = GetGroupByName(item);
                                if (value != null)
                                    groupsToCheck.Add(key, value);
                            }
                        }
                    }else
                        await AuthWarningLog(characterData, "[RG] Specified group not found!", true);


                }
                else //no auth group specifies - fresh general auth
                {
                    //check only GENERAL auth groups for roles
                    //non-general group auth should have group name supplied
                    foreach (var (key, value) in SettingsManager.Settings.WebAuthModule.GetEnabledAuthGroups().Where(a => !a.Value.ESICustomAuthRoles.Any() && !a.Value.BindToMainCharacter))
                    {
                        groupsToCheck.Add(key, value);
                    }
                    await AuthInfoLog(characterData, $"[RG] No group were specified, selected for search: {string.Join(',', groupsToCheck.Keys)}!", true);
                }
                #endregion

               // string groupName = null;

                //refresh token
                var tq = string.IsNullOrEmpty(refreshToken) ? null : await APIHelper.ESIAPI.RefreshToken(refreshToken, SettingsManager.Settings.WebServerModule.CcpAppClientId,
                    SettingsManager.Settings.WebServerModule.CcpAppSecret, $"From WebAuth | Char ID: {characterData?.character_id} | Char name: {characterData?.name}");

                var uToken = tq?.Result;
                if (tq != null)
                {
                    if (tq.Data.IsFailed || tq.Data.IsNoConnection)
                    {
                        if (!tq.Data.IsNotValid)
                        {
                            result.IsConnectionError = true;
                            await AuthWarningLog(characterData, $"[RG] {characterData.name} Connection error while fetching token!");
                            return result;
                        }
                    }
                }
                

                await AuthInfoLog(characterData, $"[RG] PRE TOCHECK: {string.Join(',', groupsToCheck.Keys)} CHARID: {characterData.character_id} DID: {authData?.DiscordId} AUTH: {authData?.AuthState} GRP: {authData?.GroupName}", true);
                //TODO multi impl
                var foundGroup = await GetAuthGroupByCharacter(groupsToCheck, characterData);

                if (foundGroup != null)
                {
                    await AuthInfoLog(characterData, $"[RG] Group found: {foundGroup.GroupName} Roles: {string.Join(',', foundGroup.RoleEntities.SelectMany(a=> a.DiscordRoles))} Titles: {string.Join(',', foundGroup.RoleEntities.SelectMany(a=> a.Titles))}!", true);

                    //bad token
                    if (tq != null && tq.Data.IsNotValid)
                    {
                        if (foundGroup.Group.RemoveTokenIfTokenIsInvalid)
                        {
                            await AuthWarningLog(characterData, $"[RG] User {characterData.name} token is no more valid. Token will be omitted from auth data.");
                            result.TokenMustBeDeleted = true;
                        }

                        if (foundGroup.Group.RemoveAuthIfTokenIsInvalid)
                        {
                            await AuthWarningLog(characterData, $"[RG] User {characterData.name} token is no more valid. Authentication will be declined.");
                            return result;
                        }
                    }

                    //var useDiscordGuildId = foundGroup.DiscordGuildId == 0 ? mainDiscordGuild
                    await UpdateResultRolesWithTitles(mainDiscordGuild, foundGroup.RoleEntities, result, characterData, uToken);
                    result.ValidManualAssignmentRoles.AddRange(foundGroup.Group.ManualAssignmentRoles.Where(a => !result.ValidManualAssignmentRoles.Contains(a)));
                    result.GroupName = foundGroup.GroupName;
                    result.Group = foundGroup.Group;
                }else 
                    await AuthInfoLog(characterData, $"[RG] Group not found", true);

                return result;
            }
            catch(Exception ex)
            {
                await LogHelper.LogError($"EXCEPTION: {ex.Message} CHARACTER: {characterData.character_id} [{characterData?.name}][{characterData?.corporation_id}]", LogCat.AuthCheck);
                throw;
            }
        }

    }
}
