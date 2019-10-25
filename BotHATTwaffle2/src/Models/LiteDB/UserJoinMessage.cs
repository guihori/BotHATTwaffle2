﻿using System;

namespace BotHATTwaffle2.Models.LiteDB
{
    internal class UserJoinMessage
    {
        public int Id { get; set; }
        public ulong UserId { get; set; }
        public DateTime JoinTime { get; set; }
    }
}