﻿using System;
using BotHATTwaffle2.Services;
using BotHATTwaffle2.src.Handlers;
using BotHATTwaffle2.src.Services.Calendar;
using Discord;

namespace BotHATTwaffle2.src.Services.Playtesting
{
    public class AnnouncementMessage
    {
        private const ConsoleColor logColor = ConsoleColor.Magenta;
        private static int lastImageIndex;
        private readonly GoogleCalendar _calendar;
        private readonly DataService _data;
        private readonly LogHandler _log;
        private readonly Random _random;

        public AnnouncementMessage(GoogleCalendar calendar, DataService data, Random random, LogHandler log)
        {
            _log = log;
            _calendar = calendar;
            _data = data;
            _random = random;
        }

        public Embed CreatePlaytestEmbed(string password = null)
        {
            if (_data.RootSettings.program_settings.debug)
                _ = _log.LogMessage("Creating Playtest Embed", false, color: logColor);

            var testEvent = _calendar.GetTestEvent();

            //What type of test
            var testType = "Casual";
            if (!testEvent.IsCasual)
                testType = "Competitive";

            //If more than 1 creator, randomly change between them for their index on the thumbnail
            var creatorIndex = 0;
            var creatorSpelling = "Creator";
            var creatorProfile =
                $"[{testEvent.Creators[0].Username}](https://discordapp.com/users/{testEvent.Creators[0].Id})";
            if (testEvent.Creators.Count > 1)
            {
                if (_data.RootSettings.program_settings.debug)
                    _ = _log.LogMessage("Multiple Test Creators found for embed", false, color: logColor);

                creatorIndex = _random.Next(0, testEvent.Creators.Count + 1);
                creatorSpelling = "Creators";

                for (var i = 1; i < testEvent.Creators.Count; i++)
                    creatorProfile +=
                        $"\n[{testEvent.Creators[i].Username}](https://discordapp.com/users/{testEvent.Creators[i].Id})";
            }

            if (_data.RootSettings.program_settings.debug)
                _ = _log.LogMessage(
                    $"Creators string\n{creatorProfile}\nUsing creator index {creatorIndex} of {testEvent.Creators.Count}",
                    false, color: logColor);

            //Timezone information
            var utcTime = testEvent.StartDateTime.GetValueOrDefault().ToUniversalTime();
            var est = TimeZoneInfo
                .ConvertTimeFromUtc(utcTime, TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"))
                .ToString("ddd HH:mm");
            var pst = TimeZoneInfo
                .ConvertTimeFromUtc(utcTime, TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time"))
                .ToString("ddd HH:mm");
            var gmt = TimeZoneInfo.ConvertTimeFromUtc(utcTime, TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"))
                .ToString("ddd HH:mm");

            //Figure out how far away from start we are
            string countdownString = null;
            var countdown = testEvent.StartDateTime.GetValueOrDefault().Subtract(DateTime.Now);
            if (testEvent.StartDateTime.GetValueOrDefault().CompareTo(DateTime.Now) < 0)
                countdownString = $"Started: {countdown:h\'H \'m\'M\'} ago!";
            else
                countdownString = countdown.ToString("d'D 'h'H 'm'M'").TrimStart(' ', 'D', 'H', '0');

            //What image should be displayed
            var embedImageUrl = _data.RootSettings.general.fallbackTestImageURL;
            if (testEvent.CanUseGallery)
            {
                var randomIndex = _random.Next(testEvent.GalleryImages.Count + 1);
                while (lastImageIndex == randomIndex) randomIndex = _random.Next(testEvent.GalleryImages.Count + 1);

                if (_data.RootSettings.program_settings.debug)
                    _ = _log.LogMessage($"Using random gallery index {randomIndex} of {testEvent.GalleryImages.Count}",
                        false, color: logColor);

                lastImageIndex = randomIndex;
                embedImageUrl = testEvent.GalleryImages[randomIndex];
            }

            //Setup the basic embed
            var playtestEmbed = new EmbedBuilder()
                .WithAuthor($"{testEvent.Title} | {testType}")
                .WithTitle("Workshop Link")
                .WithUrl(testEvent.WorkshopLink.ToString())
                .WithImageUrl(embedImageUrl)
                .WithThumbnailUrl(testEvent.Creators[creatorIndex].GetAvatarUrl())
                .WithDescription(testEvent.Description)
                .WithColor(new Color(243, 128, 72));

            playtestEmbed.AddField("Test Starts In", countdownString, true);
            playtestEmbed.AddField(creatorSpelling, creatorProfile, true);
            playtestEmbed.AddField("Moderator",
                $"[{testEvent.Moderator.Username}](https://discordapp.com/users/{testEvent.Moderator.Id})", true);
            playtestEmbed.AddField("Connect to",
                $"`{testEvent.ServerLocation}; password {_data.RootSettings.general.casualPassword}`");
            playtestEmbed.AddField("Information", $"[Screenshots]({testEvent.ImageGallery}) | " +
                                                  "[Testing Information](https://www.tophattwaffle.com/playtesting)");
            playtestEmbed.AddField("When",
                $"{testEvent.StartDateTime.GetValueOrDefault():MMMM ddd d, HH:mm} | {est} EST | {pst} PST | {gmt} GMT");

            return playtestEmbed.Build();
        }
    }
}