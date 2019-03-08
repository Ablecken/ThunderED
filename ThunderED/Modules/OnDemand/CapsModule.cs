﻿using System;
using System.Collections.Async;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using Newtonsoft.Json;
using ThunderED.API;
using ThunderED.Classes;
using ThunderED.Classes.Entities;
using ThunderED.Classes.Enums;
using ThunderED.Helpers;
using ThunderED.Json;

namespace ThunderED.Modules.OnDemand
{
    public class CapsModule: AppModuleBase
    {
        public override LogCat Category => LogCat.Caps;

        private bool _isWhoRunning;

        public class ShipsData
        {
            public Dictionary<string, ShipDataGroup> Groups = new Dictionary<string, ShipDataGroup>();
        }

        public class ShipDataGroup
        {
            public string FormatDescription;
            public string FormatTemplate;
            public List<SkillRequirement> CommonSkills  = new List<SkillRequirement>();
            public Dictionary<string, List<SkillRequirement>> ShipSkills  = new Dictionary<string, List<SkillRequirement>>();
            public Dictionary<string, ShipSpecialCounter> SpecialCounters = new Dictionary<string, ShipSpecialCounter>();
        }

        public class ShipSpecialCounter
        {
            public long Id;
            public int Rank;
            public bool IsVisible;
            [JsonIgnore]
            public string Name;
            [JsonIgnore]
            public int Count;
        }

        public class ShipResult
        {
            public string Text;
            public int Count;
            public string Name;
        }

        public async Task ProcessWhoCommand(ICommandContext context, string commandText)
        {
            if (_isWhoRunning)
            {
                await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("capsCommandRunning"));
                return;
            }

            if (!File.Exists(SettingsManager.FileShipsData))
            {
                if(!File.Exists(SettingsManager.FileShipsDataDefault))
                {
                    await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("errFileNotFound", SettingsManager.FileShipsData));
                    return;
                }
                File.Copy(SettingsManager.FileShipsDataDefault, SettingsManager.FileShipsData);
            }

            if(context.User.IsBot || string.IsNullOrEmpty(commandText))
                return;
            try
            {
                _isWhoRunning = true;
                var parts = commandText.Split(' ');
                var command = parts[0];
                var mod = parts.Length > 1 ? parts[1] : null;

                if ((!command.Equals("who", StringComparison.OrdinalIgnoreCase) && !command.Equals("all", StringComparison.OrdinalIgnoreCase) || (mod != null && !mod.Equals("online", StringComparison.OrdinalIgnoreCase)
                                                                                                                                                  && !mod.Equals("o", StringComparison.OrdinalIgnoreCase))))
                {
                    await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("helpCaps", SettingsManager.Settings.Config.BotDiscordCommandPrefix, "caps"));
                    return;
                }

                var isAll = command.Equals("all", StringComparison.OrdinalIgnoreCase);
                var isOnlineOnly = mod != null && (mod.Equals("online", StringComparison.OrdinalIgnoreCase) || mod.Equals("o", StringComparison.OrdinalIgnoreCase));


                var usersToCheck = new List<AuthUserEntity>();
                if (!isAll)
                {
                    foreach (var user in APIHelper.DiscordAPI.GetUsers(context.Channel.Id, isOnlineOnly))
                    {
                        var item = await SQLHelper.GetAuthUserByDiscordId(user.Id);
                        if (item != null && !string.IsNullOrEmpty(item.Data.Permissions) && SettingsManager.HasCharSkillsScope(item.Data.PermissionsList))
                            usersToCheck.Add(item);
                    }
                }
                else
                {
                    if (isOnlineOnly)
                    {
                        var dusers = APIHelper.DiscordAPI.GetUsers(0, true).Select(a=> a.Id);
                        usersToCheck = (await SQLHelper.GetAuthUsers((int) UserStatusEnum.Authed)).Where(item => dusers.Contains(item.DiscordId) &&
                                !string.IsNullOrEmpty(item.Data.Permissions) && SettingsManager.HasCharSkillsScope(item.Data.PermissionsList))
                            .ToList();
                    }
                    else
                        usersToCheck = (await SQLHelper.GetAuthUsers((int) UserStatusEnum.Authed)).Where(item =>
                                !string.IsNullOrEmpty(item.Data.Permissions) && SettingsManager.HasCharSkillsScope(item.Data.PermissionsList))
                            .ToList();
                }

                if (!usersToCheck.Any())
                {
                    await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("capsNoUsersFound"));
                    return;
                }

                var data = JsonConvert.DeserializeObject<ShipsData>(File.ReadAllText(SettingsManager.FileShipsData));
                if (data == null)
                {                
                    await APIHelper.DiscordAPI.ReplyMessageAsync(context, LM.Get("errFileContainsInvalidData"));
                    return;
                }

                var dataDic = new Dictionary<string, List<ShipResult>>();
                var counts = new Dictionary<string, int>();

                var usersData = new ConcurrentBag<ShipUserData>();
                await usersToCheck.ParallelForEachAsync(async userEntity =>
                {
                    var token = await APIHelper.ESIAPI.RefreshToken(userEntity.RefreshToken, SettingsManager.Settings.WebServerModule.CcpAppClientId,
                        SettingsManager.Settings.WebServerModule.CcpAppSecret);
                    if (token == null)
                    {
                        await LogHelper.LogWarning($"Character {userEntity.Data.CharacterName}({userEntity.CharacterId}) has invalid token!");
                        return;
                    }
                    var skills = await APIHelper.ESIAPI.GetCharSkills(Reason, userEntity.CharacterId, token);
                    if (skills?.skills == null || skills.skills.Count == 0) return;
                    usersData.Add(new ShipUserData
                    {
                        User = userEntity,
                        Skills = skills
                    });
                }, 8);

                foreach (var pair in data.Groups)
                {
                    var groupName = pair.Key;
                    var group = pair.Value;
                    counts.Add(groupName, 0);
                    dataDic.Add(groupName, new List<ShipResult>());
                    
                    var shCountDic = new Dictionary<string, int>();
                    foreach (var skill in @group.ShipSkills)
                        shCountDic.Add(skill.Key, 0);

                    var useSiege = group.FormatTemplate.Contains("{ST1}");
                    var useWeapon = group.FormatTemplate.Contains("{ST2}");


                    foreach (var user in usersData.Where(user => group.CommonSkills.All(skill =>
                    {
                        var s = user.Skills.skills.FirstOrDefault(a => a.skill_id == skill.Id);
                        if (s == null) return false;
                        return s.trained_skill_level >= skill.Minumim;
                    })))
                    {
                        var st1 = !useSiege ? null : (group.CommonSkills.Where(a => a.SkillType == SkillType.Siege).All(skill =>
                        {
                            var s = user.Skills.skills.FirstOrDefault(a => a.skill_id == skill.Id);
                            if (s == null) return false;
                            return s.trained_skill_level >= skill.T2;
                        })
                            ? "T2"
                            : "T1");

                        var userCounted = false;
                        foreach (var valuePair in @group.ShipSkills)
                        {
                            var shipName = valuePair.Key;
                            var shipSkills = valuePair.Value;

                            var canDrive = shipSkills.All(skill =>
                            {
                                var s = user.Skills.skills.FirstOrDefault(a => a.skill_id == skill.Id);
                                if (s == null) return false;
                                if (skill.SkillType == SkillType.Weapon && skill.T2 > 0) return true;
                                return s.trained_skill_level >= skill.Minumim;
                            });
                            if (!canDrive) continue;

                            var st2 = !useWeapon ? null : (shipSkills.Where(a => a.SkillType == SkillType.Weapon).Any(skill =>
                            {
                                var s = user.Skills.skills.FirstOrDefault(a => a.skill_id == skill.Id);
                                if (s == null) return false;
                                return s.trained_skill_level >= skill.T2;
                            })
                                ? "T2"
                                : "T1");
                            if (!userCounted)
                            {
                                counts[groupName] = counts[groupName] + 1;
                                userCounted = true;
                            }

                            var tmplt = group.FormatTemplate.Replace("{NAME}", user.User.Data.CharacterName)
                                .Replace("{ST1}", st1)
                                .Replace("{ST2}", st2);
                            for (int i = 1; i < 99; i++)
                            {
                                if(!tmplt.Contains($"{{SP{i}}}"))
                                    break;
                                var spId = group.SpecialCounters.Values.ToList()[i-1].Id;
                                var value = user.Skills.skills.FirstOrDefault(a => a.skill_id == spId)?.trained_skill_level ?? 0;
                                tmplt = tmplt.Replace($"{{SP{i}}}", value.ToString());
                            }
                            var result = new ShipResult
                            {
                                Name = shipName,
                                Text = tmplt
                            };
                            var sd = dataDic[groupName].FirstOrDefault(a => a.Name == result.Name);
                            if (sd != null)
                                sd.Text += $", {result.Text}";
                            else dataDic[groupName].Add(result);
                            shCountDic[shipName] = shCountDic[shipName] + 1;
                        }
                    }

                    foreach (var counter in @group.SpecialCounters.Where(a=> a.Value.IsVisible))
                    {
                        counter.Value.Count = usersData.Count(user =>
                            user.Skills.skills.FirstOrDefault(a => a.skill_id == counter.Value.Id && a.trained_skill_level >= counter.Value.Rank) != null);
                    }

                    //add empty ships
                    foreach (var valuePair in shCountDic.Where(a=> a.Value == 0))
                    {
                        dataDic[groupName].Add(new ShipResult
                        {
                            Name = valuePair.Key,
                            Text = LM.Get("None")
                        });
                    }
                }

                var sb = new StringBuilder();
                foreach (var pair in dataDic)
                {
                    sb.AppendLine($"**{pair.Key}** ({counts[pair.Key]} {LM.Get("of")} {usersData.Count})");
                    sb.AppendLine($"*Format: {data.Groups[pair.Key].FormatDescription}*");
                    sb.AppendLine($"```");
                    var maxNameLen = pair.Value.GroupBy(a=> a.Name).Max(a => a.Key.Length);

                    foreach (var group in pair.Value.GroupBy(a=> a.Name))
                    {
                        var text = string.Join(", ", group.Select(a=>a.Text));
                        sb.AppendLine($"{group.Key.FixedLength(maxNameLen)} - {(string.IsNullOrEmpty(text) ? LM.Get("None") : text)}");
                    }

                    if (data.Groups[pair.Key].SpecialCounters.Any(a => a.Value.IsVisible))
                    {
                        sb.AppendLine($"");
                        sb.AppendLine($"Specials");
                    }

                    foreach (var counter in data.Groups[pair.Key].SpecialCounters.Where(a=> a.Value.IsVisible))
                    {
                        sb.AppendLine($"{counter.Key}: {counter.Value.Count} {LM.Get("of")} {usersData.Count}");
                    }
                    sb.AppendLine($"```");
                }


                foreach (var str in sb.ToString().SplitBy(5990))
                {                  
                    await APIHelper.DiscordAPI.ReplyMessageAsync(context, $".\n{str}");
                }

               /* var sb = new StringBuilder();
                sb.AppendLine(".");
                sb.AppendLine($"**Dreads** ({dreadCount} {LM.Get("of")} {totalUsers})");
                sb.AppendLine($"*Format: NAME (CALIBRATION) [SIEGE,WEAPON]*");
                sb.AppendLine($"```");
                foreach (var pair in dredsDic)
                {
                    sb.AppendLine($"{pair.Key} - {(string.IsNullOrEmpty(pair.Value) ? LM.Get("None") : pair.Value.Substring(0, pair.Value.Length - 2))}");
                }
                sb.AppendLine($"```");

                if (split)
                {
                    await APIHelper.DiscordAPI.ReplyMessageAsync(context, sb.ToString());
                    sb = new StringBuilder();
                    sb.AppendLine(".");
                }

                sb.AppendLine($"**Carriers** ({carrierCount} {LM.Get("of")} {totalUsers})");
                sb.AppendLine($"*Format: NAME (CALIBRATION) [FIGHTERS]*");
                sb.AppendLine($"```");
                foreach (var pair in carrierDic)
                {
                    sb.AppendLine($"{pair.Key} - {(string.IsNullOrEmpty(pair.Value) ? LM.Get("None") : pair.Value.Substring(0, pair.Value.Length - 2))}");
                }
                sb.AppendLine($"```");

                if (split)
                {
                    await APIHelper.DiscordAPI.ReplyMessageAsync(context, sb.ToString());
                    sb = new StringBuilder();
                    sb.AppendLine(".");
                }

                sb.AppendLine($"**Logi** ({logiCount} {LM.Get("of")} {totalUsers})");
                sb.AppendLine($"*Format: NAME (CALIBRATION) [SIEGE]*");
                sb.AppendLine($"```");
                foreach (var pair in logiDic)
                {
                    sb.AppendLine($"{pair.Key} - {(string.IsNullOrEmpty(pair.Value) ? LM.Get("None") : pair.Value.Substring(0, pair.Value.Length - 2))}");
                }
                sb.AppendLine($"```");

                if (split)
                {
                    await APIHelper.DiscordAPI.ReplyMessageAsync(context, sb.ToString());
                    sb = new StringBuilder();
                    sb.AppendLine(".");
                }

                sb.AppendLine($"**{LM.Get("capsCalibr5")}**: {c5count} {LM.Get("of")} {totalUsers}");
                await APIHelper.DiscordAPI.ReplyMessageAsync(context, sb.ToString());
                */
            }
            finally
            {
                _isWhoRunning = false;
            }
        }
    }

    public class ShipUserData
    {
        public AuthUserEntity User;
        public JsonClasses.SkillsData Skills;
    }


    public class SkillRequirement
    {
        public string SkillName { get; set; }
        public long Id { get; set; }
        public int Minumim { get; set; }
        public int T2 { get; set; }
        public SkillType SkillType { get; set; }

        public SkillRequirement(long id, string name, int minimum = 1, SkillType type = SkillType.General, int t2 = 0)
        {
            SkillName = name;
            Id = id;
            Minumim = minimum;
            T2 = t2;
            SkillType = type;
        }

    }

    public enum SkillType
    {
        General,
        Siege,
        Weapon
    }

}
