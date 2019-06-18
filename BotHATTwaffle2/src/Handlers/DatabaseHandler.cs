﻿using System;
using System.Collections.Generic;
using BotHATTwaffle2.Models.LiteDB;
using BotHATTwaffle2.Services;
using BotHATTwaffle2.src.Handlers;
using BotHATTwaffle2.src.Models.LiteDB;
using Discord;
using LiteDB;

namespace BotHATTwaffle2.Handlers
{
    internal class DatabaseHandler
    {
        private const string DBPATH = @"MasterDB.db";
        private const string COLLECTION_ANNOUNCEMENT = "announcement";
        private const string COLLECTION_SERVERS = "servers";
        private const string COLLECTION_USER_JOIN = "userJoin";
        private const ConsoleColor LOG_COLOR = ConsoleColor.DarkYellow;
        private static LogHandler _log;
        private static DataService _data;

        public static void SetHandlers(LogHandler log, DataService data)
        {
            _data = data;
            _log = log;
        }

        /// <summary>
        ///     Stores the provided announce message in the database.
        ///     Creates if it does not exist.
        /// </summary>
        /// <param name="message">Message to store</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool StoreAnnouncement(IUserMessage message, DateTime eventEditTime)
        {
            try
            {
                using (var db = new LiteDatabase(DBPATH))
                {
                    //Grab our collection
                    var announcement = db.GetCollection<AnnounceMessage>(COLLECTION_ANNOUNCEMENT);

                    var foundMessage = announcement.FindOne(Query.EQ("_id", 1));

                    //If not null, we need to remove the old record first.
                    if (foundMessage != null)
                    {
                        if (_data.RSettings.ProgramSettings.Debug)
                            _ = _log.LogMessage("Old record found, deleting", false, color: LOG_COLOR);

                        announcement.Delete(1);
                    }

                    if (_data.RSettings.ProgramSettings.Debug)
                        _ = _log.LogMessage("Adding new record..." +
                                            $"\n{message.Id} at {eventEditTime}", false, color: LOG_COLOR);

                    //Insert new entry with ID of 1, and our values.
                    announcement.Insert(new AnnounceMessage
                    {
                        Id = 1,
                        AnnouncementDateTime = eventEditTime,
                        AnnouncementId = message.Id
                    });
                }
            }
            catch (Exception e)
            {
                //TODO: Don't actually know what exceptions can happen here, catch all for now.
                _ = _log.LogMessage("Something happened storing announcement message\n" +
                                    $"{e}", false, color: ConsoleColor.Red);
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Gets the stored announcement message from the DB.
        /// </summary>
        /// <returns>Found announcement message or null</returns>
        public static AnnounceMessage GetAnnouncementMessage()
        {
            AnnounceMessage foundMessage = null;
            try
            {
                using (var db = new LiteDatabase(DBPATH))
                {
                    //Grab our collection
                    var announcement = db.GetCollection<AnnounceMessage>(COLLECTION_ANNOUNCEMENT);

                    foundMessage = announcement.FindOne(Query.EQ("_id", 1));
                }
            }
            catch (Exception e)
            {
                //TODO: Don't actually know what exceptions can happen here, catch all for now.
                _ = _log.LogMessage("Something happened getting announcement message\n" +
                                    $"{e}", false, color: ConsoleColor.Red);
                return foundMessage;
            }

            return foundMessage;
        }

        /// <summary>
        ///     Gets a specific test server from the database based on the ID.
        /// </summary>
        /// <param name="serverId">Server ID to get</param>
        /// <returns>Server object if found, null otherwise</returns>
        public static Server GetTestServer(string serverId)
        {
            Server foundServer = null;
            try
            {
                using (var db = new LiteDatabase(DBPATH))
                {
                    //Grab our collection
                    var servers = db.GetCollection<Server>(COLLECTION_SERVERS);

                    foundServer = servers.FindOne(Query.EQ("ServerId", serverId));
                }

                if (_data.RSettings.ProgramSettings.Debug && foundServer != null)
                    _ = _log.LogMessage(foundServer.ToString(), false, color: LOG_COLOR);
            }
            catch (Exception e)
            {
                //TODO: Don't actually know what exceptions can happen here, catch all for now.
                _ = _log.LogMessage("Something happened getting test server\n" +
                                    $"{e}", false, color: ConsoleColor.Red);
                return foundServer;
            }

            return foundServer;
        }

        /// <summary>
        ///     Removes a server object from the database based on the ID.
        /// </summary>
        /// <param name="serverId">Server ID to remove</param>
        /// <returns>True if the server was removed, false otherwise.</returns>
        public static bool RemoveTestServer(string serverId)
        {
            var foundServer = GetTestServer(serverId);

            if (foundServer == null)
            {
                if (_data.RSettings.ProgramSettings.Debug)
                    _ = _log.LogMessage("No server found, so cannot remove anything", false, color: LOG_COLOR);
                return false;
            }

            try
            {
                using (var db = new LiteDatabase(DBPATH))
                {
                    //Grab our collection
                    var servers = db.GetCollection<Server>(COLLECTION_SERVERS);

                    servers.Delete(foundServer.Id);
                }

                if (_data.RSettings.ProgramSettings.Debug)
                    _ = _log.LogMessage(foundServer.ToString(), false, color: LOG_COLOR);
            }
            catch (Exception e)
            {
                //TODO: Don't actually know what exceptions can happen here, catch all for now.
                _ = _log.LogMessage("Something happened removing test server\n" +
                                    $"{e}", false, color: ConsoleColor.Red);
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Returns a IEnumerable of server objects containing all test servers in the database.
        /// </summary>
        /// <returns>IEnumerable of servers</returns>
        public static IEnumerable<Server> GetAllTestServers()
        {
            IEnumerable<Server> foundServers = null;
            try
            {
                using (var db = new LiteDatabase(DBPATH))
                {
                    //Grab our collection
                    var serverCol = db.GetCollection<Server>(COLLECTION_SERVERS);

                    foundServers = serverCol.FindAll();
                }
            }
            catch (Exception e)
            {
                //TODO: Don't actually know what exceptions can happen here, catch all for now.
                _ = _log.LogMessage("Something happened getting all test servers\n" +
                                    $"{e}", false, color: ConsoleColor.Red);
                return foundServers;
            }

            return foundServers;
        }

        /// <summary>
        ///     Adds a server object to the database.
        /// </summary>
        /// <param name="server">Server to add to the database</param>
        /// <returns>True if server was added, false otherwise</returns>
        public static bool AddTestServer(Server server)
        {
            if (GetTestServer(server.ServerId) != null)
            {
                if (_data.RSettings.ProgramSettings.Debug)
                    _ = _log.LogMessage("Unable to add test server since one was found.", false, color: LOG_COLOR);
                //We found an entry under the same name as this server.
                return false;
            }

            try
            {
                using (var db = new LiteDatabase(DBPATH))
                {
                    //Grab our collection
                    var servers = db.GetCollection<Server>(COLLECTION_SERVERS);
                    servers.EnsureIndex(x => x.ServerId);

                    if (_data.RSettings.ProgramSettings.Debug)
                        _ = _log.LogMessage("Inserting new server into DB", false, color: LOG_COLOR);
                }
            }
            catch (Exception e)
            {
                //TODO: Don't actually know what exceptions can happen here, catch all for now.
                _ = _log.LogMessage("Something happened adding test server\n" +
                                    $"{e}", false, color: ConsoleColor.Red);
            }

            return true;
        }

        /// <summary>
        ///     Adds a user join to the database to be processed once the bot reloads.
        /// </summary>
        /// <param name="userId">User ID to add</param>
        public static void AddJoinedUser(ulong userId)
        {
            try
            {
                using (var db = new LiteDatabase(DBPATH))
                {
                    db.DropCollection(COLLECTION_USER_JOIN);

                    var userJoins = db.GetCollection<UserJoinMessage>(COLLECTION_USER_JOIN);

                    userJoins.EnsureIndex(x => x.UserId);

                    if (_data.RSettings.ProgramSettings.Debug)
                        _ = _log.LogMessage("Inserting new user join into DB", false, color: LOG_COLOR);

                    userJoins.Insert(new UserJoinMessage
                    {
                        UserId = userId,
                        JoinTime = DateTime.Now
                    });
                }
            }
            catch (Exception e)
            {
                //TODO: Don't actually know what exceptions can happen here, catch all for now.
                _ = _log.LogMessage("Something happened adding user join\n" +
                                    $"{e}", false, color: ConsoleColor.Red);
            }
        }

        /// <summary>
        ///     Removes a user join from the database.
        /// </summary>
        /// <param name="userId">User ID to remove</param>
        public static void RemoveJoinedUser(ulong userId)
        {
            try
            {
                using (var db = new LiteDatabase(DBPATH))
                {
                    var userJoins = db.GetCollection<UserJoinMessage>(COLLECTION_USER_JOIN);
                    userJoins.EnsureIndex(x => x.UserId);

                    if (_data.RSettings.ProgramSettings.Debug)
                        _ = _log.LogMessage("Deleting new user join from DB", false, color: LOG_COLOR);

                    //Have to cast the user ID to a long
                    userJoins.Delete(Query.EQ("UserId", (long) userId));
                }
            }
            catch (Exception e)
            {
                //TODO: Don't actually know what exceptions can happen here, catch all for now.
                _ = _log.LogMessage("Something happened removing user join\n" +
                                    $"{e}", false, color: ConsoleColor.Red);
            }
        }

        /// <summary>
        ///     Gets all user joins from the database, used on restart.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<UserJoinMessage> GetAllUserJoins()
        {
            IEnumerable<UserJoinMessage> foundUsers = null;
            try
            {
                using (var db = new LiteDatabase(DBPATH))
                {
                    //Grab our collection
                    var userJoins = db.GetCollection<UserJoinMessage>(COLLECTION_USER_JOIN);

                    foundUsers = userJoins.FindAll();
                }
            }
            catch (Exception e)
            {
                //TODO: Don't actually know what exceptions can happen here, catch all for now.
                _ = _log.LogMessage("Something happened getting all user joins\n" +
                                    $"{e}", false, color: ConsoleColor.Red);
                return foundUsers;
            }

            return foundUsers;
        }
    }
}