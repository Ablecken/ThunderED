﻿using System;
using System.Collections.Async;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.WebSocket;
using ThunderED.API;
using ThunderED.Classes;
using ThunderED.Helpers;
using ThunderED.Modules.OnDemand;

namespace ThunderED.Modules
{
    public partial class WebAuthModule
    {
        public class RoleSearchResult
        {
            public string GroupName;
            public List<SocketRole> UpdatedRoles = new List<SocketRole>();
            public List<string> ValidManualAssignmentRoles = new List<string>();
        }

        internal static async Task UpdateAllUserRoles(List<string> exemptRoles, List<string> authCheckIgnoreRoles)
        {
            var discordGuild = APIHelper.DiscordAPI.GetGuild();
            var discordUsers = discordGuild.Users;
            var dids = discordUsers.Select(a => a.Id).ToList();

            if (SettingsManager.Settings.CommandsConfig.EnableRoleManagementCommands)
            {
                authCheckIgnoreRoles = authCheckIgnoreRoles.ToList();
                authCheckIgnoreRoles.AddRange(DiscordRolesManagementModule.AvailableRoleNames);
            }

            await dids.ParallelForEachAsync(async id =>
            {
                await UpdateUserRoles(id, exemptRoles, authCheckIgnoreRoles, false); 
            }, 8);

            await UpdateDBUserRoles(exemptRoles, authCheckIgnoreRoles, dids);
        }

        private static async Task UpdateDBUserRoles(List<string> exemptRoles, List<string> authCheckIgnoreRoles, IEnumerable<ulong> dids)
        {
            var ids = (await SQLHelper.GetAuthUsers(2)).Where(a=> !a.MainCharacterId.HasValue).Select(a=> a.DiscordId);
            var x = ids.FirstOrDefault(a => a == 268473315843112960);
            await ids.Where(a => !dids.Contains(a)).ParallelForEachAsync(async id =>
            {
                await UpdateUserRoles(id, exemptRoles, authCheckIgnoreRoles, false); 
            }, 8);
        }

        public static async Task<string> UpdateUserRoles(ulong discordUserId, List<string> exemptRoles, List<string> authCheckIgnoreRoles, bool isManualAuth)
        {
            try
            {
                var discordGuild = APIHelper.DiscordAPI.GetGuild();
                var u = discordGuild.GetUser(discordUserId);

                if (u != null && (u.Id == APIHelper.DiscordAPI.Client.CurrentUser.Id || u.IsBot || u.Roles.Any(r => exemptRoles.Contains(r.Name))))
                    return null;
                if(u == null && (discordUserId == APIHelper.DiscordAPI.Client.CurrentUser.Id))
                    return null;

               // await LogHelper.LogInfo($"Running Auth Check on {u.Username}", LogCat.AuthCheck, false);

                var authUser = await SQLHelper.GetAuthUserByDiscordId(discordUserId);

                if (authUser != null)
                {
                    //get data
                    var characterData = await APIHelper.ESIAPI.GetCharacterData("authCheck", authUser.CharacterId, true);
                    //skip bad requests
                    if(characterData == null) return null;

                    if (authUser.Data.CorporationId != characterData.corporation_id || authUser.Data.AllianceId != characterData.alliance_id)
                    {
                        await authUser.UpdateData(characterData);
                        await SQLHelper.SaveAuthUser(authUser);
                    }

                    var remroles = new List<SocketRole>();
                    var result = await GetRoleGroup(authUser.CharacterId, discordUserId, isManualAuth);
                    var isMovingToDump = string.IsNullOrEmpty(result.GroupName) && authUser.IsAuthed;
                    //skip dumped
                    //if (authUser.IsSpying) return null;
                    if (!isMovingToDump)
                    {
                        var group = SettingsManager.Settings.WebAuthModule.AuthGroups.FirstOrDefault(a => a.Key == result.GroupName);
                        isMovingToDump = group.Value == null || (group.Value.IsEmpty() && authUser.GroupName != group.Key);
                    }

                    var changed = false;
                    var isAuthed = result.UpdatedRoles.Count > 1;


                    if (isMovingToDump && !authUser.IsDumped)
                    {
                        if (SettingsManager.Settings.Config.ModuleHRM && SettingsManager.Settings.HRMModule.UseDumpForMembers)
                        {
                            await LogHelper.LogInfo($"{authUser.Data.CharacterName}({authUser.CharacterId}) is being moved into dumpster...", LogCat.AuthCheck);
                            authUser.SetStateDumpster();
                            await authUser.UpdateData();
                            await SQLHelper.SaveAuthUser(authUser);
                        }
                        else
                        {
                            await SQLHelper.DeleteAuthDataByCharId(authUser.CharacterId);
                        }
                    }
                    if (u == null) return null;


                    var initialUserRoles = new List<SocketRole>(u.Roles);
                    var invalidRoles = initialUserRoles.Where(a => result.UpdatedRoles.FirstOrDefault(b => b.Id == a.Id) == null);
                    foreach (var invalidRole in invalidRoles)
                    {
                        //if role is not ignored and not in valid roles while char is authed
                        if (!authCheckIgnoreRoles.Contains(invalidRole.Name) && !(isAuthed && result.ValidManualAssignmentRoles.Contains(invalidRole.Name)))
                        {
                            remroles.Add(invalidRole);
                            changed = true;
                        }
                    }

                    //mark changed if we have at least one new role to add
                    changed = changed || result.UpdatedRoles.Any(role => initialUserRoles.FirstOrDefault(x => x.Id == role.Id) == null);
    

                    if (changed)
                    {
                        result.UpdatedRoles.Remove(u.Roles.FirstOrDefault(x => x.Name == "@everyone"));

                        var actuallyDone = false;
                        if (result.UpdatedRoles.Count > 0)
                        {
                            try
                            {
                                await u.AddRolesAsync(result.UpdatedRoles);
                                actuallyDone = true;
                            }
                            catch
                            {
                                await LogHelper.LogWarning($"Failed to add {string.Join(',', result.UpdatedRoles.Select(a=> a.Name))} roles to {characterData.name} ({u.Username})!", LogCat.AuthCheck);
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
                                await LogHelper.LogWarning($"Failed to remove {string.Join(',', remroles.Select(a=> a.Name))} roles from {characterData.name} ({u.Username})!", LogCat.AuthCheck);
                            }
                        }

                        if (actuallyDone)
                        {
                            var stripped = remroles.Count > 0 ? $" {LM.Get("authStripped")}: {string.Join(',', remroles.Select(a => a.Name))}" : null;
                            var added = result.UpdatedRoles.Count > 0 ? $" {LM.Get("authAddedRoles")}: {string.Join(',', result.UpdatedRoles.Select(a => a.Name))}" : null;
                            if (SettingsManager.Settings.WebAuthModule.AuthReportChannel != 0)
                            {
                                var channel = discordGuild.GetTextChannel(SettingsManager.Settings.WebAuthModule.AuthReportChannel);
                                if(SettingsManager.Settings.WebAuthModule.AuthReportChannel > 0 && channel == null)
                                    await LogHelper.LogWarning($"Discord channel {SettingsManager.Settings.WebAuthModule.AuthReportChannel} not found!", LogCat.Discord);
                                else await APIHelper.DiscordAPI.SendMessageAsync(channel, $"{LM.Get("renewingRoles")} {characterData.name} ({u.Username}){stripped}{added}");
                            }

                            await LogHelper.LogInfo($"Adjusting roles for {characterData.name} ({u.Username}) {stripped}{added}", LogCat.AuthCheck);
                        }
                    }

                    var eveName = characterData.name;

                    if (SettingsManager.Settings.WebAuthModule.EnforceCorpTickers || SettingsManager.Settings.WebAuthModule.EnforceCharName || SettingsManager.Settings.WebAuthModule.EnforceAllianceTickers)
                    {
                        string alliancePart = null;
                        if (SettingsManager.Settings.WebAuthModule.EnforceAllianceTickers && characterData.alliance_id.HasValue)
                        {
                            var ad = await APIHelper.ESIAPI.GetAllianceData("authCheck", characterData.alliance_id.Value, true);
                            alliancePart = ad != null ? $"[{ad.ticker}] " : null;
                        }
                        string corpPart = null;
                        if (SettingsManager.Settings.WebAuthModule.EnforceCorpTickers)
                        {
                            var ad = await APIHelper.ESIAPI.GetCorporationData("authCheck", characterData.corporation_id, true);
                            corpPart = ad != null ? $"[{ad.ticker}] " : null;
                        }

                        var nickname = $"{alliancePart}{corpPart}{(SettingsManager.Settings.WebAuthModule.EnforceCharName ? eveName : u.Username)}";
                        nickname = nickname.Length > 31
                            ? nickname.Substring(0, 31)
                            : nickname;

                        if (nickname != u.Nickname && !string.IsNullOrWhiteSpace(u.Nickname) || string.IsNullOrWhiteSpace(u.Nickname) && u.Username != nickname)
                        {
                            await LogHelper.LogInfo($"Trying to change name of {u.Nickname} to {nickname}", LogCat.AuthCheck);
                            try
                            {
                                await u.ModifyAsync(x => x.Nickname = nickname);
                            }
                            catch
                            {
                                await LogHelper.LogError($"Name change failed, probably due to insufficient rights", LogCat.AuthCheck);
                            }
                        }
                    }

                    return isAuthed && !string.IsNullOrEmpty(result.GroupName) ? result.GroupName : null;
                }
                else
                {
                    if (u == null) return null;
                    var rroles = new List<SocketRole>();
                    var rolesOrig = new List<SocketRole>(u.Roles);
                    foreach (var rrole in rolesOrig)
                    {
                        var exemptRole = exemptRoles.FirstOrDefault(x => x == rrole.Name);
                        if (exemptRole == null)
                        {
                            rroles.Add(rrole);
                        }
                    }

                    rolesOrig.Remove(u.Roles.FirstOrDefault(x => x.Name == "@everyone"));
                    rroles.Remove(u.Roles.FirstOrDefault(x => x.Name == "@everyone"));

                    bool rchanged = false;

                    if (rroles != rolesOrig)
                    {
                        foreach (var exempt in rroles)
                        {
                            if (exemptRoles.FirstOrDefault(x => x == exempt.Name) == null && !authCheckIgnoreRoles.Contains(exempt.Name))
                                rchanged = true;
                        }
                    }

                    if (rchanged)
                    {
                        try
                        {
                            var channel = discordGuild.GetTextChannel(SettingsManager.Settings.WebAuthModule.AuthReportChannel);
                            if(channel != null)
                                await APIHelper.DiscordAPI.SendMessageAsync(channel, $"{LM.Get("resettingRoles")} {u.Username}");
                            await LogHelper.LogInfo($"Resetting roles for {u.Username}", LogCat.AuthCheck);
                            var trueRroles = rroles.Where(a => !exemptRoles.Contains(a.Name) && !authCheckIgnoreRoles.Contains(a.Name));
                            await u.RemoveRolesAsync(trueRroles);
                        }
                        catch (Exception ex)
                        {
                            await LogHelper.LogEx($"Error removing roles: {ex.Message}", ex, LogCat.AuthCheck);
                        }
                    }

                    return null;
                }
                
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx($"Fatal Error: {ex.Message}", ex, LogCat.AuthCheck);
                return null;
            }
        }

        public static async Task<RoleSearchResult> GetRoleGroup(long characterID, ulong discordUserId, bool isManualAuth = false)
        {
            var result = new RoleSearchResult();
            var discordGuild = APIHelper.DiscordAPI.GetGuild();
            var u = discordGuild.GetUser(discordUserId);
            var characterData = await APIHelper.ESIAPI.GetCharacterData("authCheck", characterID, true);

            try
            {
                if (characterData == null)
                    return result;

                if (u != null)
                    result.UpdatedRoles.Add(u.Roles.FirstOrDefault(x => x.Name == "@everyone"));

                #region Get personalized foundList

                var groupsToCheck = new List<WebAuthGroup>();
                var authData = await SQLHelper.GetAuthUserByCharacterId(characterID);

                if (!string.IsNullOrEmpty(authData?.GroupName))
                {
                    //check specified group for roles
                    var group = SettingsManager.Settings.WebAuthModule.AuthGroups.FirstOrDefault(a => a.Key == authData.GroupName).Value;
                    if (group != null)
                        groupsToCheck.Add(group);
                }

                if (!groupsToCheck.Any())
                {
                    //check only GENERAL auth groups for roles
                    //non-general group auth should have group name supplied
                    groupsToCheck.AddRange(SettingsManager.Settings.WebAuthModule.AuthGroups.Values.Where(a => !a.ESICustomAuthRoles.Any() && a.StandingsAuth == null));
                }

                #endregion

                string groupName = null;
                var hasAuth = false;

                // Check for Character Roles
                var authResultCharacter = await GetAuthGroupByCharacterId(groupsToCheck, characterID);
                if (authResultCharacter != null)
                {
                    var aRoles = discordGuild.Roles.Where(a => authResultCharacter.RoleEntity.DiscordRoles.Contains(a.Name) && !result.UpdatedRoles.Contains(a)).ToList();
                    if (aRoles.Count > 0)
                        result.UpdatedRoles.AddRange(aRoles);
                    result.ValidManualAssignmentRoles.AddRange(authResultCharacter.Group.ManualAssignmentRoles.Where(a => !result.ValidManualAssignmentRoles.Contains(a)));
                    groupName = SettingsManager.Settings.WebAuthModule.AuthGroups.FirstOrDefault(a => a.Value == authResultCharacter.Group).Key;
                    hasAuth = true;
                    groupsToCheck.Clear();
                    groupsToCheck.Add(authResultCharacter.Group);
                }

                if (authResultCharacter == null || (authResultCharacter.Group != null && !authResultCharacter.Group.UseStrictAuthenticationMode))
                {
                    // Check for Corporation Roles
                    var authResultCorporation = await GetAuthGroupByCorpId(groupsToCheck, characterData.corporation_id);
                    if (authResultCorporation != null)
                    {
                        var aRoles = discordGuild.Roles.Where(a => authResultCorporation.RoleEntity.DiscordRoles.Contains(a.Name) && !result.UpdatedRoles.Contains(a)).ToList();
                        if (aRoles.Count > 0)
                            result.UpdatedRoles.AddRange(aRoles);
                        result.ValidManualAssignmentRoles.AddRange(authResultCorporation.Group.ManualAssignmentRoles.Where(a => !result.ValidManualAssignmentRoles.Contains(a)));
                        groupName = SettingsManager.Settings.WebAuthModule.AuthGroups.FirstOrDefault(a => a.Value == authResultCorporation.Group).Key;
                        hasAuth = true;
                        groupsToCheck.Clear();
                        groupsToCheck.Add(authResultCorporation.Group);
                    }

                    var group = authResultCharacter?.Group ?? authResultCorporation?.Group;

                    if (group == null || !group.UseStrictAuthenticationMode)
                    {
                        // Check for Alliance Roles
                        var authResultAlliance = await GetAuthGroupByAllyId(groupsToCheck, characterData.alliance_id ?? 0);
                        if (authResultAlliance != null)
                        {
                            var aRoles = discordGuild.Roles.Where(a => authResultAlliance.RoleEntity.DiscordRoles.Contains(a.Name) && !result.UpdatedRoles.Contains(a)).ToList();
                            if (aRoles.Count > 0)
                                result.UpdatedRoles.AddRange(aRoles);
                            result.ValidManualAssignmentRoles.AddRange(authResultAlliance.Group.ManualAssignmentRoles.Where(a => !result.ValidManualAssignmentRoles.Contains(a)));
                            groupName = SettingsManager.Settings.WebAuthModule.AuthGroups.FirstOrDefault(a => a.Value == authResultAlliance.Group).Key;
                            hasAuth = true;
                        }
                    }
                }

                if (!hasAuth)
                {
                    result.UpdatedRoles = result.UpdatedRoles.Distinct().ToList();
                    result.ValidManualAssignmentRoles = result.ValidManualAssignmentRoles.Distinct().ToList();
                    //search for personal stands
                    var grList = groupsToCheck.Where(a => a.StandingsAuth != null).ToList();
                    if (grList.Count > 0)
                    {
                        var ar = await GetAuthGroupByCharacterId(groupsToCheck, characterID);
                        if (ar != null)
                        {
                            var aRoles = discordGuild.Roles.Where(a => ar.RoleEntity.DiscordRoles.Contains(a.Name)).ToList();
                            if (aRoles.Count > 0)
                                result.UpdatedRoles.AddRange(aRoles);
                            result.ValidManualAssignmentRoles.AddRange(ar.Group.ManualAssignmentRoles);
                            groupName = SettingsManager.Settings.WebAuthModule.AuthGroups.FirstOrDefault(a => a.Value == ar.Group).Key;

                        }
                    }
                }

                if (!hasAuth && (isManualAuth || !string.IsNullOrEmpty(authData?.GroupName)))
                {
                    var token = await SQLHelper.GetAuthUserByCharacterId(characterID);
                    if (token != null && !string.IsNullOrEmpty(token.GroupName) && SettingsManager.Settings.WebAuthModule.AuthGroups.ContainsKey(token.GroupName))
                    {
                        var group = SettingsManager.Settings.WebAuthModule.AuthGroups[token.GroupName];
                        if ((!group.AllowedAlliances.Any() || group.AllowedAlliances.Values.All(a => a.Id.All(b => b == 0))) &&
                            (!group.AllowedCorporations.Any() || group.AllowedCorporations.Values.All(a => a.Id.All(b => b == 0))) &&
                            (!group.AllowedCharacters.Any() || group.AllowedCharacters.Values.Any(a => a.Id.All(b => b == 0)))
                            && group.StandingsAuth == null)
                        {
                            groupName = token.GroupName;
                            var l = group.AllowedCorporations.SelectMany(a => a.Value.DiscordRoles);
                            var aRoles = discordGuild.Roles.Where(a => l.Contains(a.Name)).ToList();
                            result.UpdatedRoles.AddRange(aRoles);

                            l = group.AllowedAlliances.SelectMany(a => a.Value.DiscordRoles);
                            aRoles = discordGuild.Roles.Where(a => l.Contains(a.Name)).ToList();
                            result.UpdatedRoles.AddRange(aRoles);
                        }
                    }

                    //ordinary guest
                    if (string.IsNullOrEmpty(groupName))
                    {
                        var grp = SettingsManager.Settings.WebAuthModule.AuthGroups.FirstOrDefault(a =>
                            a.Value.AllowedAlliances.Values.All(b => b.Id.All(c => c == 0)) && a.Value.AllowedCorporations.Values.All(b => b.Id.All(c => c == 0)));
                        if (grp.Value != null)
                        {
                            groupName = grp.Key;
                            var l = grp.Value.AllowedCorporations.SelectMany(a => a.Value.DiscordRoles);
                            var aRoles = discordGuild.Roles.Where(a => l.Contains(a.Name)).ToList();
                            result.UpdatedRoles.AddRange(aRoles);

                            l = grp.Value.AllowedAlliances.SelectMany(a => a.Value.DiscordRoles);
                            aRoles = discordGuild.Roles.Where(a => l.Contains(a.Name)).ToList();
                            result.UpdatedRoles.AddRange(aRoles);
                        }
                    }
                }

                result.UpdatedRoles = result.UpdatedRoles.Distinct().ToList();
                result.GroupName = groupName;
                return result;
            }
            catch(Exception ex)
            {
                await LogHelper.LogError($"EXCEPTION: {ex.Message} CHARACTER: {characterID} [{characterData?.name}][{characterData?.corporation_id}]", LogCat.AuthCheck);
                throw;
            }
        }

    }
}
