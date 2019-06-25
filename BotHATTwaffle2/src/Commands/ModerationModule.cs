﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BotHATTwaffle2.Handlers;
using BotHATTwaffle2.Models.LiteDB;
using BotHATTwaffle2.Services;
using BotHATTwaffle2.Services.Calendar;
using BotHATTwaffle2.Services.Playtesting;
using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Discord.WebSocket;
using FluentScheduler;

namespace BotHATTwaffle2.Commands
{
    public class ModerationModule : InteractiveBase
    {
        private readonly GoogleCalendar _calendar;
        private readonly DiscordSocketClient _client;
        private readonly DataService _data;
        private readonly LogHandler _log;
        private readonly PlaytestService _playtestService;
        private readonly InteractiveService _interactive;
        private const ConsoleColor LOG_COLOR = ConsoleColor.DarkRed;
        private static readonly Dictionary<ulong, string> ServerDictionary = new Dictionary<ulong, string>();
        private static PlaytestCommandInfo _playtestCommandInfo;
        private readonly ReservationService _reservationService;

        public ModerationModule(DataService data, DiscordSocketClient client, LogHandler log, GoogleCalendar calendar,
            PlaytestService playtestService, InteractiveService interactive, ReservationService reservationService)
        {
            _playtestService = playtestService;
            _calendar = calendar;
            _data = data;
            _client = client;
            _log = log;
            _interactive = interactive;
            _reservationService = reservationService;
        }

        [Command("Mute")]
        [Summary("Mutes a user.")]
        [Remarks("Mutes a user for a specified reason and duration. When picking a duration" +
                 "you may leave off any unit of time. For example `>Mute [user] 1D5H [reason]` will mute for 1 day 5 hours. " +
                 "Alternatively, if you don't specify a unit of time, minutes is assumed. `>Mute [user] 120 [reason]` will mute for 2 hours.\n\n" +
                 "A mute may be extended on a currently muted user if you start the mute reason with `e`. For example `>Mute [user] 1D e User keeps being difficult` " +
                 "will mute the user for 1 addational day, on top of their existing mute.")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.KickMembers)]
        public async Task MuteAsync([Summary("User to mute")]SocketGuildUser user,
            [Summary("Length to mute for, in `%D%H%M%S` format")]TimeSpan muteLength,
            [Summary("Reason the user has been muted")][Remainder]string reason)
        {
            double duration = muteLength.TotalMinutes;

            //Variables used if we are extending a mute.
            double oldMuteTime = 0;
            DateTime muteStartTime = DateTime.Now;

            if (user.Roles.Contains(_data.AdminRole))
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithAuthor("Only mortals can be muted")
                    .WithDescription($"As a result, {_data.AdminRole.Mention} are immune.")
                    .WithColor(new Color(165,55,55))
                    .Build());
                return;
            }

            if (reason.StartsWith("e ", StringComparison.OrdinalIgnoreCase))
            {
                //Get the old mute, and make sure it exists before removing it. Also need some data from it.
                var oldMute = DatabaseHandler.GetActiveMute(user.Id);

                if (oldMute != null)
                {
                    //Set vars for next mute
                    oldMuteTime = oldMute.Duration;
                    muteStartTime = oldMute.MuteTime;

                    //Unmute inside the DB
                    var result = DatabaseHandler.UnmuteUser(user.Id);

                    //Remove old mute from job manager
                    JobManager.RemoveJob($"[UnmuteUser_{user.Id}]");

                    reason = "Extended from previous mute: " + reason.Substring(reason.IndexOf(' '));
                }
            }

            var added = DatabaseHandler.AddMute(new Mute
            {
                UserId = user.Id,
                Username = user.Username,
                Reason = reason,
                Duration = duration + oldMuteTime,
                MuteTime = muteStartTime,
                ModeratorId = Context.User.Id,
                Expired = false
            });

            if (added)
            {
                try
                {
                    await user.AddRoleAsync(_data.MuteRole);
                    
                    JobManager.AddJob(async () => await _data.UnmuteUser(user.Id), s => s
                        .WithName($"[UnmuteUser_{user.Id}]").ToRunOnceAt(DateTime.Now.Add(muteLength)));
                }
                catch
                {
                    await ReplyAsync("Failed to apply mute role, did the user leave the server?");
                    return;
                }

                string formatted = null;

                if (muteLength.Days != 0)
                    formatted += muteLength.Days == 1 ? $"{muteLength.Days} Day," : $"{muteLength.Days} Days,";

                if (muteLength.Hours != 0)
                    formatted += muteLength.Hours == 1 ? $" {muteLength.Hours} Hour," : $" {muteLength.Hours} Hours,";

                if (muteLength.Minutes != 0)
                    formatted += muteLength.Minutes == 1 ? $" {muteLength.Minutes} Minute," : $" {muteLength.Minutes} Minutes,";

                if (muteLength.Seconds != 0)
                    formatted += muteLength.Seconds == 1 ? $" {muteLength.Seconds} Second" : $" {muteLength.Seconds} Seconds";

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithAuthor($"{user.Username} Muted")
                    .WithDescription($"`{Context.User}` muted you for `{formatted.Trim().TrimEnd(',')}` because `{reason}`")
                    .WithColor(new Color(165, 55, 55))
                    .Build());

                await _log.LogMessage(
                    $"`{Context.User}` muted `{user.Username}` for `{formatted.Trim().TrimEnd(',')}` because `{reason}`",color:LOG_COLOR);

                try
                {
                    await user.SendMessageAsync(embed: new EmbedBuilder()
                        .WithAuthor("You have been muted")
                        .WithDescription($"`{Context.User}` muted you for `{formatted.Trim().TrimEnd(',')}` because `{reason}`")
                        .WithColor(new Color(165,55,55))
                        .Build());
                }
                catch
                {
                    //Can't DM then
                }
            }
            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithAuthor($"Unable to mute {user.Username}")
                    .WithDescription($"I could not mute `{user.Username}` because they are already muted.")
                    .WithColor(new Color(165,55,55))
                    .Build());
            }
        }

        [Command("Unmute")]
        [Summary("Unmutes a user.")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.KickMembers)]
        public async Task UnmuteAsync([Summary("User to unmute")]SocketGuildUser user)
        {
            var result = await _data.UnmuteUser(user.Id);

            if (result)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithAuthor($"{user.Username}")
                    .WithDescription($"`{user.Username}` has been unmuted by `{Context.User.Username}`.")
                    .WithColor(new Color(165, 55, 55))
                    .Build());

                await _log.LogMessage($"`{user.Username}` has been unmuted by `{Context.User.Username}`.");

                //Remove the scheduled job, because we are manually unmuting.
                JobManager.RemoveJob($"[UnmuteUser_{user.Id}]");
                
                try
                {
                    await user.SendMessageAsync(embed: new EmbedBuilder()
                        .WithAuthor($"Unmuted!")
                        .WithDescription($"You have been unmuted.")
                        .WithColor(new Color(165, 55, 55))
                        .Build());
                }
                catch
                {
                    //Try to DM them
                }
            }
            else
            {
                await ReplyAsync($"Failed to unmute `{user.Username}`");
            }
        }

        [Command("Mutes")]
        [Alias("MuteHistory")]
        [Summary("Shows active mutes or mute history for a specific user.")]
        [Remarks("If no parameters are provided, all active mutes for the server are shown." +
                 "\nIf a user is specific, the mute history for that user will be shown. A paged reply will be returned, " +
                 "along with a text file to let you see extended mute histories.")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.KickMembers)]
        public async Task MutesAsync([Summary("User for which to get mute history")][Optional]SocketGuildUser user)
        {
            string fullListing = "";

            var embed = new EmbedBuilder();
            //If null, get all the active mutes on the server.
            if (user == null)
            {
                embed.WithAuthor("Active Mutes in Server").WithColor(new Color(165,55,55));

                var allMutes = DatabaseHandler.GetAllActiveUserMutes();
                foreach (var mute in allMutes)
                {
                    embed.AddField(mute.Username,$"ID: `{mute.UserId}`\nMute Time: `{mute.MuteTime}`\nDuration: `{TimeSpan.FromMinutes(mute.Duration).ToString()}`\nReason: `{mute.Reason}`\nMuting Mod ID: `{mute.ModeratorId}`");
                }

                if (allMutes.ToArray().Length == 0)
                {
                    embed.WithColor(55, 165, 55);
                    embed.AddField("No active mutes found","I'm so proud of this community.");
                }
            }
            else
            {
                var allMutes = DatabaseHandler.GetAllUserMutes(user.Id);

                embed.WithAuthor($"All Mutes for {user.Username} - {user.Id}").WithColor(new Color(165, 55, 55));

                if (allMutes.Count() >= 5)
                {
                    //Create string to text file to send along with the embed
                    foreach (var muteFull in allMutes.Reverse())
                    {
                        fullListing += muteFull.ToString() + "\n------------------------\n";
                    }

                    //Send the text file before the interactive embed
                    Directory.CreateDirectory("Mutes");
                    File.WriteAllText($"Mutes\\AllMutes_{user.Id}.txt", fullListing);
                    await Context.Channel.SendFileAsync($"Mutes\\AllMutes_{user.Id}.txt");

                    //Paged reply
                    var lists = new List<string>();
                    var pageList = new PaginatedMessage
                    {
                        Title = $"All Mutes for {user.Username} - {user.Id}",
                        Color = new Color(165, 55, 55)
                    };
                    pageList.Options.DisplayInformationIcon = false;
                    pageList.Options.JumpDisplayOptions = JumpDisplayOptions.Never;

                    //Build the pages for the interactive embed
                    int counter = 0;
                    fullListing = null;
                    foreach (var mutePage in allMutes.Reverse())
                    {
                        fullListing += $"**{mutePage.MuteTime.ToString()}**" +
                                       $"\nDuration: `{TimeSpan.FromMinutes(mutePage.Duration).ToString()}`" +
                                       $"\nReason: `{mutePage.Reason}`" +
                                       $"\nMuting Mod ID: `{mutePage.ModeratorId}`\n\n";

                        counter++;
                        if (counter >= 5)
                        {
                            lists.Add(fullListing);
                            fullListing = null;
                            counter = 0;
                        }

                    }
                    //Add any left overs to the pages
                    lists.Add(fullListing);

                    //Send the page
                    pageList.Pages = lists;
                    await PagedReplyAsync(pageList);
                    return;
                }

                foreach (var mute in allMutes.Reverse())
                {
                    fullListing += $"**{mute.MuteTime.ToString()}**" +
                                   $"\nDuration: `{TimeSpan.FromMinutes(mute.Duration).ToString()}`" +
                                   $"\nReason: `{mute.Reason}`" +
                                   $"\nMuting Mod ID: `{mute.ModeratorId}`\n\n";
                }

                embed.WithDescription(fullListing);

                if (allMutes.ToArray().Length == 0)
                {
                    embed.WithColor(55, 165, 55);
                    embed.AddField($"No active mutes found for {user.Username}", "I'm so proud of this user.");
                }
            }
            
            await ReplyAsync(embed: embed.Build());
        }

        [Command("Playtest", RunMode = RunMode.Async)]
        [Alias("p")]
        [Summary("Handles playtesting functions.")]
        [Remarks("This command contains many sub commands for various playtesting functions. For this command to work, a playtest event must be active. " +
                 "Command syntax is `>p [subcommand]`, for example `>p post`\n\n" +
                 "`pre` / `prestart` - Pre-start the playtest. Required before a playtest can go live. Always run this before running `start`.\n" +
                 "`start` - Starts the playtest, including recording the demo file.\n" +
                 "`post` - Run when the gameplay portion of the playtest is complete. This will reload the map and get postgame " +
                 "features enabled on the test server. It will also handle downloading the demo and giving it to the creators.\n" +
                 "`p` / `pause` - Pauses a live test.\n" +
                 "`u` / `unpause` - Unpauses a live test.\n" +
                 "`s` / `scramble` - Scrambles teams on test server. This command will restart the test. Don't run it after running `start`\n" +
                 "`k` / `kick` - Kicks a player from the playtest.\n" +
                 "`end` - Officially ends a playtest which allows community server reservations.")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.KickMembers)]
        public async Task PlaytestAsync([Summary("Playtesting Sub-command")]string command)
        {
            //Reload the last used playtest if the current event is null
            if (_playtestCommandInfo == null)
                _playtestCommandInfo = DatabaseHandler.GetPlaytestCommandInfo();


            //Make sure we have a valid event, if not, abort.
            if (!_calendar.GetTestEventNoUpdate().IsValid)
            {
                await ReplyAsync("This command requires a valid playtest event.");
                return;
            }

            //Setup a few variables we'll need later
            string config = _calendar.GetTestEventNoUpdate().IsCasual
                ? _data.RSettings.General.CasualConfig
                : _data.RSettings.General.CompConfig;

            switch (command.ToLower())
            {
                case "prestart":
                case "pre":

                    //Store test information for later use. Will be written to the DB.
                    string gameMode = _calendar.GetTestEventNoUpdate().IsCasual ? "casual" : "comp";
                    string mentions = null;
                    _calendar.GetTestEventNoUpdate().Creators.ForEach(x => mentions += $"{x.Mention} ");
                    _playtestCommandInfo = new PlaytestCommandInfo
                    {
                        Id = 1, //Only storing 1 of these in the DB at a time, so hard code to 1.
                        Mode = gameMode,
                        DemoName = $"{_calendar.GetTestEventNoUpdate().StartDateTime:MM_dd_yyyy}" +
                                   $"_{_calendar.GetTestEventNoUpdate().Title.Substring(0, _calendar.GetTestEventNoUpdate().Title.IndexOf(' '))}" +
                                   $"_{gameMode}",
                        WorkshopId = _data.GetWorkshopIdFromFqdn(_calendar.GetTestEventNoUpdate().WorkshopLink.ToString()),
                        ServerAddress = _calendar.GetTestEventNoUpdate().ServerLocation,
                        Title = _calendar.GetTestEventNoUpdate().Title,
                        ThumbNailImage = _calendar.GetTestEventNoUpdate().CanUseGallery ? _calendar.GetTestEventNoUpdate().GalleryImages[0] : _data.RSettings.General.FallbackTestImageUrl,
                        ImageAlbum = _calendar.GetTestEventNoUpdate().ImageGallery.ToString(),
                        CreatorMentions = mentions,
                        StartDateTime = _calendar.GetTestEventNoUpdate().StartDateTime.Value
                    };

                    //Write to the DB so we can restore this info next boot
                    DatabaseHandler.StorePlaytestCommandInfo(_playtestCommandInfo);

                    await ReplyAsync($"Pre-start playtest of **{_playtestCommandInfo.Title}**" +
                                     $"\nOn **{_playtestCommandInfo.ServerAddress}**" +
                                     $"\nWith config of **{config}**" +
                                     $"\nWorkshop ID **{_playtestCommandInfo.WorkshopId}**");

                    await _data.RconCommand(_playtestCommandInfo.ServerAddress, $"exec {config}");
                    await Task.Delay(1000);
                    await _data.RconCommand(_playtestCommandInfo.ServerAddress, $"host_workshop_map {_playtestCommandInfo.WorkshopId}");
                    break;

                case "start":
                    await ReplyAsync($"Start playtest of **{_playtestCommandInfo.Title}**" +
                                     $"\nOn **{_playtestCommandInfo.ServerAddress}**" +
                                     $"\nWith config of **{config}**" +
                                     $"\nWorkshop ID **{_playtestCommandInfo.WorkshopId}**" +
                                     $"\nDemo Name **{_playtestCommandInfo.DemoName}**");

                    await _data.RconCommand(_playtestCommandInfo.ServerAddress, $"exec {config}");
                    await Task.Delay(3000);
                    await _data.RconCommand(_playtestCommandInfo.ServerAddress, $"tv_record {_playtestCommandInfo.DemoName}; say Recording {_playtestCommandInfo.DemoName}");
                    await Task.Delay(1000);
                    await _data.RconCommand(_playtestCommandInfo.ServerAddress, $"say Playtest of {_playtestCommandInfo.Title} is live! Be respectful and GLHF!");
                    await Task.Delay(1000);
                    await _data.RconCommand(_playtestCommandInfo.ServerAddress, $"say Playtest of {_playtestCommandInfo.Title} is live! Be respectful and GLHF!");
                    await Task.Delay(1000);
                    await _data.RconCommand(_playtestCommandInfo.ServerAddress, $"say Playtest of {_playtestCommandInfo.Title} is live! Be respectful and GLHF!");
                    break;

                case "post":
                    //This is fired and forgotten. All error handling will be done in the method itself.
                    await ReplyAsync($"Post playtest of **{_playtestCommandInfo.Title}**" +
                                     $"\nOn **{_playtestCommandInfo.ServerAddress}**" +
                                     $"\nWorkshop ID **{_playtestCommandInfo.WorkshopId}**" +
                                     $"\nDemo Name **{_playtestCommandInfo.DemoName}**");

                    PlaytestPostTasks(_playtestCommandInfo);
                    break;

                case "pause":
                case "p":
                    await _data.RconCommand(_playtestCommandInfo.ServerAddress,
                        @"mp_pause_match;say Pausing Match!;say Pausing Match!;say Pausing Match!;say Pausing Match!");
                    await ReplyAsync($"```Pausing playtest on {_playtestCommandInfo.ServerAddress}!```");
                    break;

                case "unpause":
                case "u":
                    await _data.RconCommand(_playtestCommandInfo.ServerAddress,
                        @"mp_unpause_match;say Unpausing Match!;say Unpausing Match!;say Unpausing Match!;say Unpausing Match!");
                    await ReplyAsync($"```Unpausing playtest on {_playtestCommandInfo.ServerAddress}!```");
                    break;

                case "scramble":
                case "s":
                    await _data.RconCommand(_playtestCommandInfo.ServerAddress, "mp_scrambleteams 1" +
                                                                           ";say Scrambling Teams!;say Scrambling Teams!;say Scrambling Teams!;say Scrambling Teams!");
                    await ReplyAsync($"```Scrambling teams on {_playtestCommandInfo.ServerAddress}!```");
                    break;

                case "kick":
                case "k":
                    var kick = new KickUserRcon(Context, _interactive, _data, _log);
                    await kick.KickPlaytestUser(_playtestCommandInfo.ServerAddress);
                    break;

                case "end":
                    //Allow manual enabling of community reservations
                    _reservationService.AllowReservations();
                    await ReplyAsync("```Community servers may now be reserved.```");

                    break;
                default:
                    await ReplyAsync("Invalid action, please consult the help document for this command.");
                    break;
            }
        }

        /// <summary>
        /// Handles post playtest tasks.
        /// </summary>
        /// <param name="playtestCommandInfo"></param>
        internal async void PlaytestPostTasks(PlaytestCommandInfo playtestCommandInfo)
        {
            await _data.RconCommand(playtestCommandInfo.ServerAddress, $"host_workshop_map {playtestCommandInfo.WorkshopId}");
            await Task.Delay(15000); //Wait for map to change
            await _data.RconCommand(playtestCommandInfo.ServerAddress,
                $"sv_cheats 1; bot_stop 1;sv_voiceenable 0;exec {_data.RSettings.General.PostgameConfig};" +
                $"say Please join the level testing voice channel for feedback!;" +
                $"say Please join the level testing voice channel for feedback!;" +
                $"say Please join the level testing voice channel for feedback!;" +
                $"say Please join the level testing voice channel for feedback!;" +
                $"say Please join the level testing voice channel for feedback!");

            DownloadHandler.DownloadPlaytestDemo(playtestCommandInfo);

            const string demoUrl = "http://demos.tophattwaffle.com";

            var embed = new EmbedBuilder()
                .WithAuthor($"Download playtest demo for {playtestCommandInfo.Title}",_data.Guild.IconUrl, demoUrl)
                .WithThumbnailUrl(playtestCommandInfo.ThumbNailImage)
                .WithColor(new Color(243,128,72))
                .WithDescription($"[Download Demo Here]({demoUrl}) | [Map Images]({playtestCommandInfo.ImageAlbum}) | [Playtesting Information](https://www.tophattwaffle.com/playtesting/)");

            await _data.TestingChannel.SendMessageAsync(playtestCommandInfo.CreatorMentions, embed: embed.Build());
        }

        [Command("Active")]
        [Summary("Grants a user the Active Memeber role.")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.KickMembers)]
        public async Task ActiveAsync([Summary("User to give role to.")]SocketGuildUser user)
        {
            await _log.LogMessage($"{user} has been given {_data.ActiveRole.Mention} by {Context.User}");
            await ReplyAsync(embed: new EmbedBuilder()
                .WithAuthor($"{user.Username} is now an active member!")
                .WithDescription($"The {_data.ActiveRole.Mention} is given to users who are active and helpful in our community. " +
                                 $"Thanks for contributing!")
                .WithColor(new Color(241, 196, 15))
                .Build());
            await user.AddRoleAsync(_data.ActiveRole);
        }

        [Command("CompetitiveTester")]
        [Summary("Grants a user the Competitive Tester role.")]
        [Alias("comp")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.KickMembers)]
        public async Task CompetitiveTesterAsync([Summary("User to give role to")]SocketGuildUser user)
        {

            if (user.Roles.Contains(_data.CompetitiveTesterRole))
            {
                await Context.Message.DeleteAsync();
                await user.RemoveRoleAsync(_data.CompetitiveTesterRole);
                await _log.LogMessage($"{user} has {_data.CompetitiveTesterRole} removed by {Context.User}");
            }
            else
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithAuthor($"{user.Username} is now a Competitive Tester!")
                    .WithDescription($"The {_data.CompetitiveTesterRole.Mention} is given to users who contribute positively to the playtesting service. " +
                                     $"Such as attending tests, giving valid feedback, and making smart plays.")
                    .WithColor(new Color(52, 152, 219))
                    .Build());

                await user.AddRoleAsync(_data.CompetitiveTesterRole);
                await _log.LogMessage($"{user} has been given {_data.CompetitiveTesterRole} by {Context.User}");
            }
        }

        [Command("Invite")]
        [Summary("Invites a user to a competitive level test.")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.KickMembers)]
        public async Task CompInviteAsync([Summary("User to invite")]SocketGuildUser user)
        {
            await Context.Message.DeleteAsync();

            //Do nothing if a test is not valid.
            if (!_calendar.GetTestEventNoUpdate().IsValid)
            {
                await ReplyAsync("There is no valid test that I can invite that user to.");
                return;
            }

            await _log.LogMessage($"{user} has been invite to the competitive test of {_calendar.GetTestEventNoUpdate().Title} by {Context.User}");

            try
            {
                await user.SendMessageAsync($"You've been invited to join __**{_calendar.GetTestEventNoUpdate().Title}**__!\n" +
                                            $"Open Counter-Strike Global Offensive and paste the following into console to join:" +
                                            $"```connect {_calendar.GetTestEventNoUpdate().ServerLocation}; password {_calendar.GetTestEventNoUpdate().CompPassword}```");
            }
            catch
            {
                await ReplyAsync("I attempted to DM that user connection information, but they don't allow DMs.");
            }
        }

        [Command("rcon")]
        [Alias("r")]
        [RequireUserPermission(GuildPermission.KickMembers)]
        [RequireContext(ContextType.Guild)]
        [Summary("Sends RCON commands to a server.")]
        [Remarks("Sends RCON commands to a test server.\n" +
                 "You can use `>rcon auto` to automatically use the next playtest server.\n" +
                 "You can specify a server be specified before commands are sent.\n" +
                 "Set a server using `>rcon set [serverId]\n" +
                 "Then commands can be sent as normal without a server ID:\n" +
                 "Example: `>r sv_cheats 1`\n" +
                 "Provide no parameters to see what server you're current sending to.")]
        public async Task RconAsync([Summary("Rcon command to send")][Remainder][Optional]string command)
        {
            string targetServer = null;
            if (command == null)
            {
                if (ServerDictionary.ContainsKey(Context.User.Id))
                    targetServer = ServerDictionary[Context.User.Id];
                else
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithAuthor($"RCON commands sent by {Context.User}", _data.Guild.IconUrl)
                        .WithDescription($"will be sent using `Auto mode`. Which is the active playtest server, if there is one.")
                        .WithColor(new Color(55, 165, 55)).Build());
                    return;
                }

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithAuthor($"RCON commands sent by {Context.User}", _data.Guild.IconUrl)
                    .WithDescription($"will be sent to `{targetServer}`")
                    .WithColor(new Color(55, 165, 55)).Build());
                return;
            }

            //Set server mode
            if (command.StartsWith("set", StringComparison.OrdinalIgnoreCase))
            {
                var server = DatabaseHandler.GetTestServer(command.Substring(3).Trim());

                if (server == null)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithAuthor($"Cannot set RCON server", _data.Guild.IconUrl)
                        .WithDescription($"No server found with the name {command.Substring(3).Trim()}")
                        .WithColor(new Color(165, 55, 55)).Build());
                    return;
                }

                //Dictionary contains user already, remove them.
                if(ServerDictionary.ContainsKey(Context.User.Id))
                {
                    ServerDictionary.Remove(Context.User.Id);
                }
                ServerDictionary.Add(Context.User.Id, command.Substring(3).Trim());
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithAuthor($"RCON commands sent by {Context.User}", _data.Guild.IconUrl)
                    .WithDescription($"will be sent to `{ServerDictionary[Context.User.Id]}`")
                    .WithColor(new Color(55, 165, 55)).Build());
                return;
            }

            //Set user's mode to Auto, which is really just removing a user from the dictionary
            if (command.StartsWith("auto", StringComparison.OrdinalIgnoreCase))
            {
                if (ServerDictionary.ContainsKey(Context.User.Id))
                {
                    ServerDictionary.Remove(Context.User.Id);
                }
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithAuthor($"RCON commands sent by {Context.User}", _data.Guild.IconUrl)
                    .WithDescription($"will be sent using `Auto mode`. Which is the active playtest server, if there is one.")
                    .WithColor(new Color(55, 165, 55)).Build());
                return;
            }

            //In auto mode
            if (!ServerDictionary.ContainsKey(Context.User.Id))
            {
                if (_calendar.GetTestEventNoUpdate().IsValid)
                {
                    //There is a playtest event, get the server ID from the test event
                    string serverAddress = _calendar.GetTestEventNoUpdate().ServerLocation;
                    targetServer = serverAddress.Substring(0, serverAddress.IndexOf('.'));
                }
                else
                {
                    //No playtest event, we cannot do anything.
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithAuthor("No playtest server found.", _data.Guild.IconUrl)
                        .WithDescription("Set your target server using `>rcon set [serverId]`.")
                        .WithColor(new Color(55, 165, 55)).Build());
                    return;
                }
            }
            else
                //User has a server set manually.
                targetServer = ServerDictionary[Context.User.Id];

            //Quick kick feature
            if (command.StartsWith("kick", StringComparison.OrdinalIgnoreCase))
            {
                var kick = new KickUserRcon(Context, _interactive, _data, _log);
                await kick.KickPlaytestUser(targetServer);
                return;
            }

            var reply = await _data.RconCommand(targetServer, command);

            await ReplyAsync(embed: new EmbedBuilder()
                .WithAuthor($"Command sent to {targetServer}", _data.Guild.IconUrl)
                .WithDescription($"```{(string.IsNullOrWhiteSpace(reply) ? $"{command} was sent, but provided no reply." : reply)}```")
                .WithColor(new Color(55, 165, 55)).Build());
        }

        [Command("ClearReservation")]
        [Alias("cr")]
        [Summary("Clears a server reservation.")]
        [Remarks("Clears all server reservations manually. Can be used if users are abusing the reservation system.\n" +
                 "If a server code is provided, just that server reservation will be removed.")]
        [RequireContext(ContextType.Guild)]
        [RequireUserPermission(GuildPermission.KickMembers)]
        public async Task ClearReservationAsync([Summary("ID of test server to clear")][Optional]string serverId)
        {
            if (serverId != null)
            {
                var reservation = DatabaseHandler.GetServerReservation(serverId);

                if (reservation != null)
                {
                    await ReplyAsync(embed: _reservationService.ReleaseServer(reservation.UserId,
                        "A moderator has cleared your reservation."));

                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithAuthor($"{DatabaseHandler.GetTestServer(serverId).Address} has been released.", _data.Guild.IconUrl)
                        .WithColor(new Color(55, 165, 55)).Build());
                    return;
                }
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithAuthor("No server reservation found to release", _data.Guild.IconUrl)
                    .WithColor(new Color(165, 55, 55)).Build());
            }
            await ReplyAsync(embed: new EmbedBuilder()
                .WithAuthor("Clearing all reservations", _data.Guild.IconUrl)
                .WithColor(new Color(165, 55, 55)).Build());

            await _reservationService.ClearAllServerReservations();
        }

        [Command("TestServer")]
        [Alias("ts")]
        [RequireUserPermission(GuildPermission.Administrator)]
        [Summary("Command for manipulating test servers.")]
        [Remarks("`>TestServer get [ServerCode / all]`\n" +
        "`>TestServer remove [ServerCode]`\n\n" +
        "Adding a server requires the information provided with each variable on a new line after the invoking command." +
        "`>TestServer add\n" +
        "[ServerId]\n" +
        "[Description]\n" +
        "[Address]\n" +
        "[RconPassword]\n" +
        "[FtpUser]\n" +
        "[FtpPassword]\n" +
        "[FtpPath]\n" +
        "[FtpType]`\n\n" +
        "Getting a single test server will reply with the required information to re-add the server into the database. " +
        "This is useful when editing servers.")]
        public async Task TestServerAsync(string action, [Remainder]string values = null)
        {
            //Add server
            if (action.StartsWith("a", StringComparison.OrdinalIgnoreCase))
            {
                //Need command values, abort if we don't have them.
                if (values == null)
                {
                    await ReplyAsync("No command provided");
                    return;
                }

                string[] serverValues = values.Split("\n");

                //Make sure all the data is present, as all values are required
                if (serverValues.Length != 8)
                {
                    await ReplyAsync("Adding a server requires all 8 server values.");
                    await Context.Message.DeleteAsync();
                    return;
                }

                //Validate FTP type before entry
                switch (serverValues[7])
                {
                    case "ftp":
                        break;
                    case "sftp":
                        break;
                    case "ftps":
                        break;
                    default:
                        await ReplyAsync("Invalid FTP type. Please provide `ftp`, `ftps`, or `sftp` and try again." +
                                         "\nYour message was deleted as it may have contained a password.");
                        await Context.Message.DeleteAsync();
                        return;
                }

                if (DatabaseHandler.AddTestServer(new Server()
                {
                    ServerId = serverValues[0],
                    Description = serverValues[1],
                    Address = serverValues[2],
                    RconPassword = serverValues[3],
                    FtpUser = serverValues[4],
                    FtpPassword = serverValues[5],
                    FtpPath = serverValues[6],
                    FtpType = serverValues[7].ToLower()
                }))
                {
                    await ReplyAsync("Server added!\nI deleted your message since it had passwords in it.");
                    await Context.Message.DeleteAsync();
                    return;
                }

                await ReplyAsync("Issue adding server, does it already exist?\nI deleted your message since it had passwords in it.");
                await Context.Message.DeleteAsync();
            }
            //Get server
            else if (action.StartsWith("g"))
            {
                string reply = $"No server found with server code {values}";
                if (values != null && !values.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    var testServer = DatabaseHandler.GetTestServer(values);

                    if (testServer != null)
                        reply = $"Found the following server:\n{testServer.ToString()}\n\n" +
                                $"Use the following command to re-add this server to the database.\n" +
                                $"```" +
                                $">TestServer add" +
                                $"\n{testServer.ServerId}" +
                                $"\n{testServer.Description}" +
                                $"\n{testServer.Address}" +
                                $"\n{testServer.RconPassword}" +
                                $"\n{testServer.FtpUser}" +
                                $"\n{testServer.FtpPassword}" +
                                $"\n{testServer.FtpPath}" +
                                $"\n{testServer.FtpType}" +
                                $"```";

                    await _data.AlertUser.SendMessageAsync(reply);
                    
                }
                //Get all servers instead
                else
                {
                    var testServers = DatabaseHandler.GetAllTestServers();

                    if (testServers != null)
                    {
                        reply = null;
                        foreach (var testServer in testServers)
                        {
                            reply += "```" + testServer + "```";
                        }
                    }
                    else
                        reply = "Could not get all servers because the request returned null.";

                    await _data.AlertUser.SendMessageAsync(reply);
                }

                await ReplyAsync($"Server information contains passwords, as a result I have DM'd it to {_data.AlertUser}.");
            }
            //Remove server
            else if (action.StartsWith("r"))
            {
                if (DatabaseHandler.RemoveTestServer(values))
                {
                    await ReplyAsync($"Server with the ID: `{values}` was removed.");
                }
                else
                {
                    await ReplyAsync($"Could not remove a server with the ID of: `{values}`. It likely does not exist in the DB.");
                }
            }
        }

        [Command("ForceAnnounce")]
        [Alias("fa")]
        [RequireUserPermission(GuildPermission.KickMembers)]
        [Summary("Allows manual announcing of a playtest. This command mentions the playtester role.")]
        public async Task ForceAnnounceAsync()
        {
            await _playtestService.PlaytestStartingInTask();
        }

        [Command("SkipAnnounce")]
        [Alias("sa")]
        [RequireUserPermission(GuildPermission.KickMembers)]
        [Summary("Toggles the next playtest announcement.")]
        [Remarks("Toggles if the next playtest announcement happens. This will allow you to prevent the 1 hour, or starting " +
                 "playtest announcement messages from happening. Server setup tasks are still preformed, just the message is skipped. " +
                 "After server setup tasks run, the flag is reset. Meaning if you disable the 1 hour announcement, the starting announcement " +
                 "will still go out unless you disable it after the 1 hour announcement would have gone out.")]
        public async Task SkipAnnounceAsync()
        {
            //Toggle the announcement state
            _playtestService.PlaytestStartAlert = !_playtestService.PlaytestStartAlert;

            await ReplyAsync(embed: new EmbedBuilder()
                .WithAuthor($"Next Playtest Alert is: {_playtestService.PlaytestStartAlert}")
                .WithColor(_playtestService.PlaytestStartAlert ? new Color(55,165,55) : new Color(165,55,55))
                .Build());
        }

        [Command("Debug")]
        [RequireUserPermission(GuildPermission.Administrator)]
        [Summary("Debug settings.")]
        [Remarks("View or change the debug flag." +
                 "\n`>debug [true/false/reload]` to set the flag, or reload settings from the settings file.")]
        public async Task DebugAsync(string status = null)
        {
            if (status == null)
            {
                await Context.Channel.SendMessageAsync(
                    $"Current debug status is: `{_data.RSettings.ProgramSettings.Debug}`");
            }
            else if (status.StartsWith("t", StringComparison.OrdinalIgnoreCase))
            {
                _data.RSettings.ProgramSettings.Debug = true;
                await _data.UpdateRolesAndUsers();
                await Context.Channel.SendMessageAsync(
                    $"Changed debug status to: `{_data.RSettings.ProgramSettings.Debug}`");
            }
            else if (status.StartsWith("f", StringComparison.OrdinalIgnoreCase))
            {
                _data.RSettings.ProgramSettings.Debug = false;
                await _data.UpdateRolesAndUsers();
                await Context.Channel.SendMessageAsync(
                    $"Changed debug status to: `{_data.RSettings.ProgramSettings.Debug}`");
            }
            else if (status.StartsWith("r", StringComparison.OrdinalIgnoreCase))
            {
                await _data.DeserializeConfig();
                await Context.Channel.SendMessageAsync(
                    $"Deserializing configuration...");
            }
        }
    }
}