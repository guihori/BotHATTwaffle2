﻿using System;
using System.Collections.Generic;
using BotHATTwaffle2.Services;
using BotHATTwaffle2.src.Handlers;
using Discord.WebSocket;

namespace BotHATTwaffle2.src.Services.Calendar
{
    public class PlaytestEvent
    {
        private const ConsoleColor logColor = ConsoleColor.Magenta;

        private readonly DataService _data;
        private readonly LogHandler _log;
        public bool IsCasual;
        
        public bool IsValid { get; private set; }
        public DateTime? EventEditTime { get; set; } //What is the last time the event was edited?
        public DateTime? StartDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public string Title { get; set; }
        public List<SocketUser> Creators { get; set; }
        public Uri ImageGallery { get; set; }
        public Uri WorkshopLink { get; set; }
        public SocketUser Moderator { get; set; }
        public string Description { get; set; }
        public string ServerLocation { get; set; }
        public List<string> GalleryImages { get; set; }
        public bool CanUseGallery { get; private set; }
        public DateTime? LastEditTime { get; set; }
        public string compPassword { get; set; }

        public PlaytestEvent(DataService data, LogHandler log)
        {
            _log = log;
            _data = data;
            Creators = new List<SocketUser>();
            GalleryImages = new List<string>();
            VoidEvent();
        }

        public void SetGamemode(string input)
        {
            if (input.Contains("comp", StringComparison.OrdinalIgnoreCase))
            {
                IsCasual = false;
                int i = new Random().Next(_data.RootSettings.general.compPasswords.Length);
                compPassword = _data.RootSettings.general.compPasswords[i];

                _ = _log.LogMessage($"Competitive password for `{Title}` is: `{compPassword}`");
            }
            else
                IsCasual = true;
        }

        /// <summary>
        /// Checks the required values on the test event to see if it can be used
        /// </summary>
        /// <returns>True if valid, false otherwise.</returns>
        public bool TestValid()
        {
            if (EventEditTime != null && StartDateTime != null && EndDateTime != null && Title != null &&
                Creators.Count > 0 && ImageGallery != null && WorkshopLink != null && Moderator != null &&
                Description != null && ServerLocation != null)
            {
                //Can we use the gallery images?
                if (GalleryImages.Count > 1)
                {
                    CanUseGallery = true;

                    if (_data.RootSettings.program_settings.debug)
                        _ = _log.LogMessage("Can use image gallery for test event", false, color: logColor);
                }

                if (_data.RootSettings.program_settings.debug)
                    _ = _log.LogMessage($"Test event is valid!\n{ToString()}", false, color: logColor);

                IsValid = true;

                return true;
            }

            if (_data.RootSettings.program_settings.debug)
                _ = _log.LogMessage($"Test even is not valid!\n{ToString()}", false, color: logColor);

            IsValid = false;

            return false;
        }

        /// <summary>
        /// Essentially resets this object for next use. Could dispose and make a new one
        /// but where is the fun in that?
        /// </summary>
        public void VoidEvent()
        {
            if (_data.RootSettings.program_settings.debug)
                _ = _log.LogMessage("Voiding test event", false, color: logColor);

            IsValid = false;
            CanUseGallery = false;
            EventEditTime = null;
            StartDateTime = null;
            EndDateTime = null;
            Title = null;
            Creators.Clear();
            Creators.TrimExcess();
            GalleryImages.Clear();
            GalleryImages.TrimExcess();
            ImageGallery = null;
            WorkshopLink = null;
            IsCasual = true;
            Moderator = null;
            Description = null;
            ServerLocation = null;
            compPassword = null;
        }

        public override string ToString()
        {
            return "eventValid: " + IsValid
                                  + "\neventEditTime: " + EventEditTime
                                  + "\ndateTime: " + StartDateTime
                                  + "\nEndDateTime: " + EndDateTime
                                  + "\ntitle: " + Title
                                  + "\nimageGallery: " + ImageGallery
                                  + "\nworkshopLink: " + WorkshopLink
                                  + "\nisCasual: " + IsCasual
                                  + "\nmoderator: " + Moderator
                                  + "\ndescription: " + Description
                                  + "\nserverLocation: " + ServerLocation
                                  + "\ncreators: " + string.Join(", ", Creators);
        }
    }
}