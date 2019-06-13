﻿using System;
using BotHATTwaffle2.Services;
using BotHATTwaffle2.src.Services.Playtesting;
using Discord.WebSocket;
using FluentScheduler;

namespace BotHATTwaffle2.src.Handlers
{
    internal class ScheduleHandler
    {
        private const ConsoleColor logColor = ConsoleColor.DarkMagenta;
        private readonly DiscordSocketClient _client;
        private readonly DataService _data;
        private readonly LogHandler _log;
        private readonly PlaytestService _playtestService;

        public ScheduleHandler(DataService data, DiscordSocketClient client, LogHandler log, PlaytestService playtestService)
        {
            Console.WriteLine("Setting up ScheduleHandler...");
            _playtestService = playtestService;
            _log = log;
            _data = data;
            _client = client;

            //Fluent Scheduler init and events
            JobManager.Initialize(new Registry());

            JobManager.JobStart += FluentJobStart;
            JobManager.JobEnd += FluentJobEnd;
            JobManager.JobException += FluentJobException;
        }

        public void RemoveAllJobs()
        {
            _ = _log.LogMessage("Removing all scheduled jobs", false, color: ConsoleColor.Red);
            JobManager.RemoveAllJobs();
        }

        public void AddRequiredJobs()
        {
            _ = _log.LogMessage("Adding required scheduled jobs...", false, color: logColor);

            //Add schedule for playtest information
            JobManager.AddJob(async () => await _playtestService.PostOrUpdateAnnouncement(), s => s
                .WithName("[PostOrUpdateAnnouncement]").ToRunEvery(60).Seconds());

            //Reattach to the old announcement message quickly
            JobManager.AddJob(async () => await _playtestService.TryAttachPreviousAnnounceMessage(), s => s
                .WithName("[TryAttachPreviousAnnounceMessage]").ToRunOnceIn(5).Seconds());

            //Display what jobs we have scheduled
            foreach (var allSchedule in JobManager.AllSchedules)
            {
                _ = _log.LogMessage($"{allSchedule.Name} runs at: {allSchedule.NextRun}", 
                    false, color: logColor);
            }
        }

        private void FluentJobStart(JobStartInfo info)
        {
            if (_data.RootSettings.program_settings.debug)
                _ = _log.LogMessage($"FLUENT JOB STARTED:{info.Name}", false, color: logColor);
        }

        private void FluentJobEnd(JobEndInfo info)
        {
            if (_data.RootSettings.program_settings.debug)
                _ = _log.LogMessage($"FLUENT JOB ENDED:{info.Name}", false, color: logColor);
        }

        private void FluentJobException(JobExceptionInfo info)
        {
            if (_data.RootSettings.program_settings.debug)
                _ = _log.LogMessage($"FLUENT JOB EXCEPTION:\n{info.Exception}", false, color: logColor);
        }
    }
}