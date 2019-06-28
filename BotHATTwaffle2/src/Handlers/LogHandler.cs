﻿using System;
using System.Threading.Tasks;
using BotHATTwaffle2.Services;
using Discord;
using Discord.WebSocket;

namespace BotHATTwaffle2.Handlers
{
    public class LogHandler
    {
        private readonly DiscordSocketClient _client;
        private readonly DataService _dataService;

        public LogHandler(DataService data, DiscordSocketClient client)
        {
            Console.WriteLine("Setting up LogHandler...");

            _dataService = data;
            _client = client;

            _client.Log += LogEventHandler;
        }

        private Task LogEventHandler(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        public async Task LogMessage(string msg, bool channel = true, bool console = true, bool alert = false,
            ConsoleColor color = ConsoleColor.White)
        {
            if (alert)
                msg = _dataService.AlertUser.Mention + "\n" + msg;

            if (msg.Length > 1950)
                msg = msg.Substring(0, 1950);

            if (channel)
                await _dataService.LogChannel.SendMessageAsync(msg);


            if (console)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(msg.Replace("```","") + "\n");
                Console.ResetColor();
            }
        }
    }
}