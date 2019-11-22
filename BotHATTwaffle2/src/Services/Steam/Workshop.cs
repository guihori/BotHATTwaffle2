﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BotHATTwaffle2.Handlers;
using BotHATTwaffle2.Models.JSON.Steam;
using BotHATTwaffle2.Util;
using bsp_pakfile;
using Discord;
using Discord.WebSocket;
using ICSharpCode.SharpZipLib.Zip;
using Newtonsoft.Json;
using Color = Discord.Color;
using ImageFormat = Pfim.ImageFormat;
using ZipFile = System.IO.Compression.ZipFile;

namespace BotHATTwaffle2.Services.Steam
{
    public class Workshop
    {
        private readonly DataService _dataService;
        private readonly LogHandler _log;

        public Workshop(DataService dataService, LogHandler log)
        {
            _dataService = dataService;
            _log = log;
        }

        public async Task<EmbedBuilder> HandleWorkshopEmbeds(SocketMessage message,
            string images = null, string testType = null, string inputId = null)
        {
            // Cut down the message to grab just the first URL
            var regMatch = Regex.Match(message.Content,
                @"\b((https?|ftp|file)://|(www|ftp)\.)(steamcommunity)[-A-Z0-9+&@#/%?=~_|$!:,.;]*[A-Z0-9+&@#/%=~_|$]",
                RegexOptions.IgnoreCase);
            var workshopLink = regMatch.ToString();
            
            //Get the initial response from the workshop API
            var workshopId = GeneralUtil.GetWorkshopIdFromFqdn(workshopLink);
            var steamApi = new SteamAPI(_dataService, _log);
            var workshopJsonItem = await steamApi.GetWorkshopItem(workshopId);

            if (workshopJsonItem == null)
                return null;

            var workshopJsonAuthor = await steamApi.GetWorkshopAuthor(workshopJsonItem);

            if (string.IsNullOrWhiteSpace(workshopLink))
                workshopLink =
                    $"https://steamcommunity.com/sharedfiles/filedetails/?id={workshopJsonItem.response.publishedfiledetails[0].publishedfileid}";

            // Finally we can build the embed after too many HTTP requests
            var workshopItemEmbed = new EmbedBuilder()
                .WithAuthor($"{workshopJsonItem.response.publishedfiledetails[0].title}",
                    workshopJsonAuthor.response.players[0].avatar, workshopLink)
                .WithTitle($"Creator: {workshopJsonAuthor.response.players[0].personaname}")
                .WithUrl(workshopJsonAuthor.response.players[0].profileurl)
                .WithImageUrl(workshopJsonItem.response.publishedfiledetails[0].preview_url)
                .WithColor(new Color(71, 126, 159));


            //Try to get games, if null don't embed a game field
            var wsGames = await steamApi.GetWorkshopGames();
            var gameId = wsGames?.applist.apps.SingleOrDefault(x =>
                x.appid == workshopJsonItem.response.publishedfiledetails[0].creator_app_id);

            if (gameId != null)
                workshopItemEmbed.AddField("Game", gameId.name, true);


            // Add every other field now
            // Get tags from Json object
            var tags = string.Join(", ",
                workshopJsonItem.response.publishedfiledetails[0].tags.Select(x => x.tag));

            if (!string.IsNullOrWhiteSpace(tags))
                workshopItemEmbed.AddField("Tags", tags, true);

            // If test type is null or empty, it will not be included in the embed (bot only)
            if (!string.IsNullOrEmpty(testType)) workshopItemEmbed.AddField("Test Type", testType);

            var shortDescription = Regex.Replace(workshopJsonItem.response.publishedfiledetails[0].description,
                @"\t|\n|\r", " ");

            if (!string.IsNullOrWhiteSpace(shortDescription))
                workshopItemEmbed.AddField("Description",
                    shortDescription.Length > 497 ? shortDescription.Substring(0, 497) + "..." : shortDescription);

            // If images is null or empty, it will not be included in the embed (bot only)
            if (!string.IsNullOrEmpty(images)) workshopItemEmbed.AddField("Links", images);

            return workshopItemEmbed;
        }

        public async Task SendWorkshopEmbed(SocketMessage message)
        {
            await message.Channel.TriggerTypingAsync();
            //If the invoking message has an embed, do nothing.
            await Task.Delay(2000);
            var refreshedMessage = await _dataService.GetSocketMessage(message.Channel, message.Id);
            if (refreshedMessage.Embeds.Count > 0)
                return;

            var embed = await HandleWorkshopEmbeds(message);

            if (embed != null)
                await message.Channel.SendMessageAsync(embed: embed.Build());
        }
    }
}