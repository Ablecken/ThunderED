﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using ThunderED.Classes;
using ThunderED.Helpers;
using ThunderED.Modules;
using ThunderED.Modules.Sub;
using LogSeverity = ThunderED.Classes.LogSeverity;

namespace ThunderED.API
{
    /// <summary>
    /// Use partial class to implement additional methods
    /// </summary>
    public partial class DiscordAPI: CacheBase
    {
        private DiscordSocketClient Client { get; set; }
        private CommandService Commands { get; set; }

        public bool IsAvailable { get; private set; }

        private readonly List<SocketGuild> _cacheGuilds  = new List<SocketGuild>();

        private void Initialize()
        {
            try
            {
                Client?.Dispose();
            }
            catch
            {
                //ignore
            }

            var config = new DiscordSocketConfig() {GatewayIntents = GatewayIntents.All};

            Client = new DiscordSocketClient(config);
            Commands = new CommandService();
            Commands.AddModuleAsync(typeof(DiscordCommands), null).GetAwaiter().GetResult();
            Client.Log += async message =>
            {
                //await AsyncHelper.RedirectToThreadPool();
                await LogHelper.Log(message.Message, message.Severity.ToSeverity(), LogCat.Discord);
                if (message.Exception != null)
                    await LogHelper.LogEx("Discord Internal Exception", message.Exception, LogCat.Discord);
            };
            Client.UserJoined += Event_UserJoined;
            Client.Ready += async () =>
            {
                _cacheGuilds.Clear();
                _cacheGuilds.AddRange(Client.Guilds.ToList());
                if (!IsAvailable)
                    IsAvailable = true;
                var name = SettingsManager.Settings.Config.BotDiscordName.Length > 31
                    ? SettingsManager.Settings.Config.BotDiscordName.Substring(0, 31)
                    : SettingsManager.Settings.Config.BotDiscordName;
                foreach (var g in _cacheGuilds.Where(g => g.CurrentUser.Nickname != name))
                    await g.CurrentUser.ModifyAsync(x => x.Nickname = name);

                foreach (var guild in _cacheGuilds)
                {
                    await guild.DownloadUsersAsync();
                }
            };
            Client.Connected += async () =>
            {
                //await AsyncHelper.RedirectToThreadPool();
                // await LogHelper.LogInfo("Connected!", LogCat.Discord);
                IsAvailable = _cacheGuilds.Any();
                await Client.SetGameAsync(SettingsManager.Settings.Config.BotDiscordGame);
            };
            Client.Disconnected += async exception =>
            {
                IsAvailable = false;
                //await AsyncHelper.RedirectToThreadPool();
                if (exception != null)
                    await LogHelper.LogEx("Critical disconnection", exception, LogCat.Discord);
                if (HasBadException(exception))
                {
                    await LogHelper.LogWarning("Restarting Discord service...", LogCat.Discord);
                    await Start().ConfigureAwait(false);
                }
            };
        }

        private async Task<bool> Throttle()
        {
            if (IsAvailable) return true;
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(250);
                if (IsAvailable) return true;
            }

            return false;
        }

        private bool HasBadException(Exception ex)
        {
            if (ex == null) return false;
            var type = ex.GetType();
            return type== typeof(SocketException) || type == typeof(OperationCanceledException) || type == typeof(WebSocketException) || type == typeof(TaskCanceledException);
        }

        public SocketSelfUser GetCurrentUser()
        {
            if (!IsAvailable) return null;

            try
            {
                return Client?.CurrentUser;
            }
            catch (Exception ex)
            {
                LogHelper.LogEx(nameof(GetCurrentUser), ex, LogCat.Discord).GetAwaiter().GetResult();
                return null;
            }
        }

        public async Task ReplyMessageAsync(ICommandContext context, string message)
        {
            if (!IsAvailable) return;
            if (!await Throttle()) return;
            await ReplyMessageAsync(context, message, false);
        }

        public async Task<string> GetUserMention(ulong userId)
        {
            if (!IsAvailable) return null;
            if (!await Throttle()) return null;
            try
            {
                return FindUserInGuilds(userId)?.Mention;
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(nameof(GetUserMention), ex, LogCat.Discord);
                return null;
            }
        }

        #region Find for multiple guilds


        private SocketRole FindRoleInGuilds(string roleName, bool caseInsensitive = false)
        {
            if (!IsAvailable) return null;
            foreach (var guild in GetPrioritezedGuildsList())
            {
                var role = guild.Roles.FirstOrDefault(a => a.Name.Equals(roleName, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                if (role != null)
                    return role;
            }

            return null;
        }

        private IMessageChannel FindChannelInGuilds(ulong channelId)
        {
            if (!IsAvailable) return null;
            foreach (var guild in GetPrioritezedGuildsList())
            {
                var channel = guild.Channels.FirstOrDefault(a => a.Id == channelId);
                if (channel != null)
                    return guild.GetTextChannel(channelId);
            }

            return null;
        }

        private SocketUser FindUserInGuilds(ulong userId)
        {
            if (!IsAvailable) return null;
            foreach (var guild in GetPrioritezedGuildsList())
            {
                var user = guild.GetUser(userId);
                if(user != null)
                    return user;
            }

            return null;
        }

        private SocketGuildUser FindGuildUserInGuilds(ulong userId)
        {
            if (!IsAvailable) return null;
            foreach (var guild in GetPrioritezedGuildsList())
            {
                var user = guild.GetUser(userId);
                if (user != null)
                    return user;
            }

            return null;
        }

        private List<SocketGuild> GetPrioritezedGuildsList()
        {
            if (!IsAvailable) return null;
            var g = _cacheGuilds.FirstOrDefault(a => a.Id == SettingsManager.Settings.Config.DiscordGuildId);
            if (g == null) return _cacheGuilds;
            var list = new List<SocketGuild> {g};
            list.AddRange(_cacheGuilds.Where(a => a.Id != SettingsManager.Settings.Config.DiscordGuildId));
            return list;
        }

        #endregion

        public async Task ReplyMessageAsync(ICommandContext context, string message, bool mentionSender)
        {
            if (!IsAvailable) return;
            if (context?.Message == null) return;
            if(!await Throttle()) return;
            if (mentionSender)
            {
                var mention = await GetMentionedUserString(context);
                message = $"{mention}, {message}";
            }

            await ReplyMessageAsync(context, message, null);
        }

        public async Task ReplyMessageAsync(ICommandContext context, IMessageChannel channel, string message, bool mentionSender = false)
        {
            if (!IsAvailable) return;
            if (context == null || channel == null || string.IsNullOrEmpty(message)) return;
            if(!await Throttle()) return;
            if (mentionSender)
            {
                var mention = await GetMentionedUserString(context);
                message = $"{mention}, {message}";
            }

            try
            {
                await channel.SendMessageAsync(message.TrimLengthOrSpace(MAX_MSG_LENGTH));
            }
            catch (HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.InsufficientPermissions)
                    await LogHelper.LogError($"The bot don't have rights to send message to {context.Message.Channel.Id} ({context.Message.Channel.Name}) channel!");
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(nameof(ReplyMessageAsync), ex, LogCat.Discord);
                if (HasBadException(ex))
                {
                    await LogHelper.LogWarning("Restarting Discord service...", LogCat.Discord);
                    await Start().ConfigureAwait(false);
                }
            }
        }

        public async Task ReplyMessageAsync(ICommandContext context, string message, Embed embed)
        {
            if (!IsAvailable) return;
            if (context?.Message == null) return;
            if(!await Throttle()) return;
            try
            {
                await context.Message.Channel.SendMessageAsync(message.TrimLengthOrSpace(MAX_MSG_LENGTH), false, embed).ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.InsufficientPermissions)
                    await LogHelper.LogError($"The bot don't have rights to send message to {context.Message.Channel.Id} ({context.Message.Channel.Name}) channel!");
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(nameof(ReplyMessageAsync), ex, LogCat.Discord);
                if (HasBadException(ex))
                {
                    await LogHelper.LogWarning("Restarting Discord service...", LogCat.Discord);
                    await Start().ConfigureAwait(false);
                }
            }
        }

        public async Task<IUserMessage> SendMessageAsync(ulong channel, string message, Embed embed = null)
        {
            if (!IsAvailable) return null;
            if (!await Throttle()) return null;
            try
            {
                if (channel == 0 || (string.IsNullOrWhiteSpace(message) && embed == null)) return null;
                var ch = GetChannel(channel);
                if (ch == null)
                {
                    await LogHelper.LogWarning($"Discord channel {channel} not found!", LogCat.Discord);
                    return null;
                }

                return await SendMessageAsync(ch, message.TrimLengthOrSpace(MAX_MSG_LENGTH), embed);
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(nameof(SendMessageAsync), ex, LogCat.Discord);
                return null;
            }

        }


        public async Task<IUserMessage> SendMessageAsync(IMessageChannel channel, string message, Embed embed = null)
        {
            if (!IsAvailable) return null;
            if (!await Throttle()) return null;
            if (message == null && embed == null || channel == null) return null;

            try
            {
                return await channel.SendMessageAsync(message.TrimLengthOrSpace(MAX_MSG_LENGTH), false, embed);
            }
            catch (HttpException ex)
            {
                if (ex.DiscordCode == DiscordErrorCode.InsufficientPermissions)
                    await LogHelper.LogError($"The bot don't have rights to send message to {channel.Id} ({channel.Name}) channel!");
                else await LogHelper.LogEx(nameof(SendMessageAsync), ex, LogCat.Discord);
                return null;
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(nameof(SendMessageAsync), ex, LogCat.Discord);
                if (HasBadException(ex))
                {
                    await LogHelper.LogWarning("Restarting Discord service...", LogCat.Discord);
                    await Start().ConfigureAwait(false);
                }

                return null;
            }
        }

        public const int MAX_MSG_LENGTH = 1999;

        public async Task<string> GetMentionedUserString(ICommandContext context)
        {
            if (!IsAvailable) return null;
            if (!await Throttle()) return null;
            try
            {
                var id = context.Message.MentionedUserIds.FirstOrDefault();
                return id == 0 ? context.Message.Author.Mention : (await context.Guild.GetUserAsync(id))?.Mention;
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(nameof(GetMentionedUserString), ex, LogCat.Discord);
                return null;
            }
        }

        private volatile bool _isStarting;

        public async Task Start()
        {
            if(_isStarting) return;
            _isStarting = true;
            try
            {
                if (Client != null)
                {
                    try { await Client.StopAsync(); }
                    catch
                    {
                        // ignored
                    }

                    Client.Dispose();
                }

                Initialize();

                await InstallCommands();
                await Client.LoginAsync(TokenType.Bot, SettingsManager.Settings.Config.BotDiscordToken);
                await Client.StartAsync();
            }
            catch (HttpRequestException ex)
            {
                await LogHelper.LogEx(ex.Message, ex, LogCat.Discord);
                await LogHelper.Log("Probably Discord host is unreachable! Will retry soon", LogSeverity.Critical,
                    LogCat.Discord);
                await Task.Delay(3000);
                await LogHelper.LogWarning("Restarting Discord service...", LogCat.Discord);
                _isStarting = false;
                await Start();
            }
            catch (HttpException ex)
            {
                if (ex.Reason.Contains("401"))
                {
                    await LogHelper.LogError(
                        $"Check your Discord bot Token and make sure it is NOT a Client ID: {ex.Reason}",
                        LogCat.Discord);
                    return;
                }

                await Task.Delay(3000);
                await LogHelper.LogWarning("Restarting Discord service...", LogCat.Discord);
                _isStarting = false;
                await Start();
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(ex.Message, ex, LogCat.Discord);
                await Task.Delay(3000);
                await LogHelper.LogWarning("Restarting Discord service...", LogCat.Discord);
                _isStarting = false;
                await Start();
            }
            finally
            {
                _isStarting = false;
            }
        }

        public async void Stop()
        {
            try
            {
                if(Client != null)
                    await Client.StopAsync();
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(nameof(Stop), ex, LogCat.Discord);
            }
        }

        private async Task InstallCommands()
        {
            Client.MessageReceived += HandleCommand;
            await Commands.AddModulesAsync(Assembly.GetEntryAssembly(), null);
        }

        private async Task HandleCommand(SocketMessage messageParam)
        {
            await Task.Factory.StartNew(async () =>
            {
                if (!(messageParam is SocketUserMessage message)) return;
                if (!await Throttle()) return;

                try
                {
                    if (SettingsManager.Settings.Config.ModuleIRC)
                    {
                        var module = TickManager.GetModule<IRCModule>();
                        module?.SendMessage(message.Channel.Id, message.Author.Id, message.Author.Username,
                            message.Content);
                    }

                    if (SettingsManager.Settings.Config.ModuleTelegram)
                    {
                        var name = GetGuildByChannel(messageParam.Channel.Id)?.GetUser(message.Author.Id)?.Nickname ??
                                   message.Author.Username;
                        TickManager.GetModule<TelegramModule>()
                            ?.SendMessage(message.Channel.Id, message.Author.Id, name, message.Content);
                    }

                    var argPos = 0;

                    if (!(message.HasStringPrefix(SettingsManager.Settings.Config.BotDiscordCommandPrefix,
                              ref argPos) ||
                          message.HasMentionPrefix
                              (Client.CurrentUser, ref argPos))) return;

                    var context = new CommandContext(Client, message);
                    await Commands.ExecuteAsync(context, argPos, null);
                }
                catch (Exception ex)
                {
                    await LogHelper.LogEx(nameof(HandleCommand), ex, LogCat.Discord);
                }
            });
        }

        private SocketGuild GetGuildByChannel(ulong channelId)
        {
            if (!IsAvailable) return null;
            return _cacheGuilds.FirstOrDefault(a=>a.Channels.Any(b=> b.Id == channelId));
        }

        private async Task Event_UserJoined(SocketGuildUser arg)
        {
            //await AsyncHelper.RedirectToThreadPool();
            await Task.Factory.StartNew(async () =>
            {
                if (!await Throttle()) return;
                try
                {
                    if (SettingsManager.Settings.Config.WelcomeMessage)
                    {
                        if (arg.Guild.Id == SettingsManager.Settings.Config.DiscordGuildId)
                        {
                            var channel = SettingsManager.Settings.Config.WelcomeMessageChannelId == 0
                                ? arg.Guild.DefaultChannel
                                : arg.Guild.GetTextChannel(SettingsManager.Settings.Config.WelcomeMessageChannelId);
                            var authurl = ServerPaths.GetAuthPageUrl();
                            if (!string.IsNullOrWhiteSpace(authurl))
                                await APIHelper.DiscordAPI.SendMessageAsync(channel,
                                    LM.Get("welcomeMessage", arg.Mention, authurl,
                                        SettingsManager.Settings.Config.BotDiscordCommandPrefix));
                            else
                                await APIHelper.DiscordAPI.SendMessageAsync(channel,
                                    LM.Get("welcomeAuth", arg.Mention));
                        }
                        else
                        {
                            var channel = arg.Guild.DefaultChannel;
                            var authurl = ServerPaths.GetAuthPageUrl();
                            if (!string.IsNullOrWhiteSpace(authurl))
                                await APIHelper.DiscordAPI.SendMessageAsync(channel,
                                    LM.Get("welcomeMessage", arg.Mention, authurl,
                                        SettingsManager.Settings.Config.BotDiscordCommandPrefix));
                            else
                                await APIHelper.DiscordAPI.SendMessageAsync(channel,
                                    LM.Get("welcomeAuth", arg.Mention));
                        }
                    }
                }
                catch (Exception ex)
                {
                    await LogHelper.LogEx(nameof(Event_UserJoined), ex, LogCat.Discord);
                }
            });
        }

        public async Task<string> IsAdminAccess(ICommandContext context)
        {
            if (!IsAvailable) return null;
            if (!await Throttle()) return null;
            try
            {
                if (context.Guild != null)
                {
                    var roles = new List<IRole>(context.Guild.Roles);
                    var userRoleIDs = (await context.Guild.GetUserAsync(context.User.Id)).RoleIds;
                    var roleMatch = SettingsManager.Settings.Config.DiscordAdminRoles;
                    if ((from role in roleMatch select roles.FirstOrDefault(x => x.Name == role) into tmp where tmp != null select userRoleIDs.FirstOrDefault(x => x == tmp.Id))
                        .All(check => check == 0)) return LM.Get("comRequirePriv");
                }
                else
                {
                    var guild = (await context.Client.GetGuildsAsync()).FirstOrDefault();
                    if (guild == null) return "Error getting guild!";
                    var roles = new List<IRole>(guild.Roles);
                    //var xxx = await guild.GetUsersAsync();
                    var userRoleIDs = (await guild.GetUserAsync(context.User.Id)).RoleIds;
                    var roleMatch = SettingsManager.Settings.Config.DiscordAdminRoles;
                    if ((from role in roleMatch
                            select roles.FirstOrDefault(x => x.Name.Equals(role, StringComparison.OrdinalIgnoreCase))
                            into tmp
                            where tmp != null
                            select userRoleIDs.FirstOrDefault(x => x == tmp.Id))
                        .All(check => check == 0)) return LM.Get("comRequirePriv");
                }

                await Task.CompletedTask;
                return null;
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(nameof(IsAdminAccess), ex, LogCat.Discord);
                return "ERROR";
            }
        }

        #region Cached queries

        private ulong[] _forbiddenPublicChannels;
        private ulong[] _authAllowedChannels;

        internal ulong[] GetConfigForbiddenPublicChannels()
        {
            return _forbiddenPublicChannels ?? (_forbiddenPublicChannels =
                       SettingsManager.Settings.Config.ComForbiddenChannels.ToArray());
        }

        internal ulong[] GetConfigAllowedPublicChannels()
        {
            return SettingsManager.Settings.Config.ComAllowedChannels.ToArray();
        }

        internal ulong[] GetAuthAllowedChannels()
        {
            return _authAllowedChannels ?? (_authAllowedChannels =
                       SettingsManager.Settings.WebAuthModule.ComAuthChannels.ToArray());
        }

        internal override void PurgeCache()
        {
        }

        internal override void ResetCache(string type = null)
        {
            _forbiddenPublicChannels = null;
            _authAllowedChannels = null;
        }

        internal bool IsUserMention(ICommandContext context)
        {
            if (!IsAvailable) return false;
            try
            {
                return context.Message.MentionedUserIds.Count != 0;
            }
            catch (Exception ex)
            {
                LogHelper.LogEx(nameof(IsUserMention), ex, LogCat.Discord).GetAwaiter().GetResult();
                return false;
            }
            
        }

        #endregion

        public IMessageChannel GetChannel(ulong noid)
        {
            if (!IsAvailable) return null;
            try
            {
                return FindChannelInGuilds(noid);
            }
            catch (Exception ex)
            {
                LogHelper.LogEx(nameof(GetChannel), ex, LogCat.Discord).GetAwaiter().GetResult();
                return null;
            }
        }

        public void SubscribeRelay(IDiscordRelayModule m)
        {
            if (!IsAvailable) return;
            if (m == null) return;
            m.RelayMessage += async (message, channel) => { await SendMessageAsync(GetChannel(channel), message); };
        }

        public string GetRoleMention(string role)
        {
            if (!IsAvailable) return null;
            try
            {
                var r = FindRoleInGuilds(role);
                if (r == null || !r.IsMentionable) return null;
                return r.Mention;
            }
            catch (Exception ex)
            {
                LogHelper.LogEx(nameof(GetRoleMention), ex, LogCat.Discord).GetAwaiter().GetResult();
                return null;
            }
        }

        public SocketGuild GetGuild(ulong guildId)
        {
            if (!IsAvailable) return null;
            return _cacheGuilds.FirstOrDefault(a=> a.Id == guildId);
        }

        public SocketRole GetGuildRole(string roleName, bool caseInsensitive = false)
        {
            if (!IsAvailable) return null;
            try
            {
                return FindRoleInGuilds(roleName, caseInsensitive);
            }
            catch (Exception ex)
            {
                LogHelper.LogEx(nameof(GetGuildRole), ex, LogCat.Discord).GetAwaiter().GetResult();
                return null;
            }
        }

        
        public List<string> GetGuildRoleNames(ulong guildId)
        {
            if (!IsAvailable) return null;
            try
            {
                return GetGuild(guildId).Roles.Select(a => a.Name).ToList();
            }
            catch (Exception ex)
            {
                LogHelper.LogEx(nameof(GetGuildRoleNames), ex, LogCat.Discord).GetAwaiter().GetResult();
                return null;
            }
        }

        public SocketRole GetUserRole(SocketGuildUser user, string roleName)
        {
            if (!IsAvailable) return null;
            try
            {
                return user.Roles.FirstOrDefault(x => x.Name == roleName);
            }
            catch (Exception ex)
            {
                LogHelper.LogEx(nameof(GetUserRole), ex, LogCat.Discord).GetAwaiter().GetResult();
                return null;
            }
        }

        public SocketGuildUser GetUser(ulong authorId)
        {
            if (!IsAvailable) return null;
            try
            {
                return FindGuildUserInGuilds(authorId);
            }
            catch (Exception ex)
            {
                LogHelper.LogEx(nameof(GetUser), ex, LogCat.Discord).GetAwaiter().GetResult();
                return null;
            }
        }

        public async Task<bool> AssignRoleToUser(ulong userId, string roleName)
        {
            if (!IsAvailable) return false;
            if (!await Throttle()) return false;

            var discordUser = GetUser(userId);
            if (discordUser == null) return false;
            try
            {
                var role = GetGuildRole(roleName, true);
                if (role == null) return false;
                await discordUser.AddRoleAsync(role);
                return true;
            }
            catch (Exception e)
            {
                await LogHelper.LogEx($"Unable to assign role {roleName} to {discordUser?.Nickname}", e, LogCat.Discord);
                return false;
            }
        }

        
        public async Task<bool> StripUserRole(ulong userId, string roleName)
        {
            if (!IsAvailable) return false;
            if (!await Throttle()) return false;
            try
            {
                var discordUser = GetUser(userId);
                if (discordUser == null) return false;

                var role = GetGuildRole(roleName, true);
                if (role == null) return false;
                await discordUser.RemoveRoleAsync(role);
                return true;
            }
            catch (Exception e)
            {
                await LogHelper.LogEx($"Unable to remove role {roleName} from D:{userId}", e, LogCat.Discord);
                return false;
            }
        }

        public async Task<bool> IsBotPrivateChannel(IMessageChannel contextChannel)
        {
            if (!IsAvailable) return false;
            if (!await Throttle()) return false;
            try
            {
                return contextChannel.GetType() == typeof(SocketDMChannel) && await contextChannel.GetUserAsync(Client.CurrentUser.Id) != null;
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(nameof(IsBotPrivateChannel), ex, LogCat.Discord);
                return false;
            }
        }

        public List<string> GetUserRoleNames(ulong id)
        {
            if (!IsAvailable) return null;
            return GetUser(id)?.Roles.Select(a => a.Name).ToList();
        }

        /*public List<SocketGuildUser> GetUsers(ulong channelId, bool onlineOnly)
        {
            try
            {
                var g = GetGuildByChannel(channelId);
                var users = channelId == 0 ? g.Users.ToList() : g.GetChannel(channelId).Users.ToList();
                return onlineOnly ? users.Where(a => a.Status != UserStatus.Offline).ToList() : users;
            }
            catch (Exception ex)
            {
                LogHelper.LogEx(nameof(GetUsers), ex, LogCat.Discord).GetAwaiter().GetResult();
                return new List<SocketGuildUser>();
            }
        }*/

        public async Task<IList<string>> CheckAndNotifyBadDiscordRoles(IList<string> roles, LogCat category)
        {
            if (!IsAvailable) return null;
            if (!await Throttle()) return null;
            var discordRoles = GetPrioritezedGuildsList().SelectMany(a=> a.Roles).Select(a=> a.Name).Distinct().ToList();
            if(!discordRoles.Any()) return new List<string>();
            if(!roles.Any()) return new List<string>();
            var missing = roles.Except(discordRoles).ToList();
            if (missing.Any())
                await LogHelper.LogWarning($"Unknown roles has been found: {string.Join(',', missing)}!" , category);

            return missing;
        }

        public async Task RemoveMessage(IUserMessage message)
        {
            if (!IsAvailable) return;
            try
            {
                await message.DeleteAsync();
            }
            catch (Exception ex)
            {
                await LogHelper.LogEx(nameof(RemoveMessage), ex, LogCat.Discord);
            }
        }

        public int GetUsersCount()
        {
            if (!IsAvailable) return 0;
            return _cacheGuilds.Sum(a=> a.Users.Count);
        }

        public List<ulong> GetUserIdsFromGuild(ulong guildId)
        {
            if (!IsAvailable) return null;
            return _cacheGuilds.FirstOrDefault(a => a.Id == guildId)?.Users.Select(a=> a.Id).ToList();
        }

        public List<ulong> GetUserIdsFromChannel(ulong guildId, ulong channelId, bool isOnlineOnly)
        {
            if (!IsAvailable) return null;
            if (channelId == 0)
                return _cacheGuilds.FirstOrDefault(a => a.Id == guildId)?.Users.Where(a => !isOnlineOnly || a.Status != UserStatus.Offline).Select(a => a.Id).ToList();

            return _cacheGuilds.FirstOrDefault(a => a.Id == guildId)?.Channels.FirstOrDefault(a => a.Id == channelId)
                ?.Users.Where(a => !isOnlineOnly || a.Status != UserStatus.Offline).Select(a => a.Id).ToList();
        }

        /// <summary>
        /// Return all guild where specified user is present
        /// </summary>
        /// <param name="discordUserId">User Id</param>
        public List<ulong> GetGuildsFromUser(ulong discordUserId)
        {
            return _cacheGuilds.Where(a => a.GetUser(discordUserId) != null).Select(a=> a.Id).ToList();
        }
    }
}
