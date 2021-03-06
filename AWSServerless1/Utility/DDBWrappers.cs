﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;

using Amazon.Runtime;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;

using AWSServerless1.Models.OutMessages;
using AWSServerless1.Models.InMessages;
using System.Runtime.CompilerServices;
using Amazon.DynamoDBv2.DocumentModel;

namespace AWSServerless1.Utility
{
    class DDBWrappers
    {
        private IAmazonDynamoDB _DDBClient { get; }
        private Func<string, IAmazonApiGatewayManagementApi> _ApiGatewayManagementApiClientFactory { get; }
        private string _UsersRoomsTable { get; }
        private string _RoomsConnectionsTable { get; }
        private string _MessagesTable { get; }
        private string _UsersIndex { get; set; }
        private string _ConnectionsIndex { get; set; }
        
        public DDBWrappers(IAmazonDynamoDB dDBClient,
                    Func<string, IAmazonApiGatewayManagementApi> apiGatewayManagementApiClientFactory,
                    string usersRoomsTable,
                    string roomsConnectionsTable,
                    string messagesTable,
                    string usersIndex,
                    string connectionsIndex)
        {
            _DDBClient = dDBClient;
            _ApiGatewayManagementApiClientFactory = apiGatewayManagementApiClientFactory;
            _UsersRoomsTable = usersRoomsTable;
            _RoomsConnectionsTable = roomsConnectionsTable;
            _MessagesTable = messagesTable;
            _UsersIndex = usersIndex;
            _ConnectionsIndex = connectionsIndex;
        }

        /// <summary>
        /// Gets all users from UsersRoomsTable DDB
        /// </summary>
        /// <returns>List of usernames</returns>
        public async Task<List<string>> GetAllUsers()
        {
            var scanRequest = new ScanRequest
            {
                TableName = _UsersRoomsTable,
                IndexName = _UsersIndex,
                ProjectionExpression = "UserId"
            };
            List<string> retVal = new List<string>();
            var scanResponse = await _DDBClient.ScanAsync(scanRequest);
            foreach(var item in scanResponse.Items)
            {
                retVal.Add(item["UserId"].S);
            }
            return retVal;
        }

        /// <summary>
        /// Gets all custom rooms to which user belongs to
        /// </summary>
        /// <param name="userId">Id of user</param>
        /// <returns>First list is a list of room names, second list is a list of ids</returns>
        public async Task<(List<string>, List<string>)> GetUserCustomRooms(string userId)
        {
            var queryRequest = new QueryRequest
            {
                TableName = _UsersRoomsTable,
                KeyConditionExpression = $"NodeId = :partitionkeyval AND begins_with (TargetId, :sortkeyval)",
                FilterExpression = "Custom = :t",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    {":partitionkeyval", new AttributeValue { S = $"user-{userId}"} },
                    {":sortkeyval", new AttributeValue { S = $"room"} },
                    {":t", new AttributeValue { BOOL = true } }
                },
                ProjectionExpression = "RoomId, TargetId"
            };
            List<string> retNames = new List<string>();
            List<string> retIds = new List<string>();
            var queryResponse = await _DDBClient.QueryAsync(queryRequest);
            foreach (var item in queryResponse.Items)
            {
                retNames.Add(item["RoomId"].S);
                retIds.Add(item["TargetId"].S);
            }
            return (retNames, retIds);
        }

        /// <summary>
        /// Adds a custom room with a given name and with users given
        /// </summary>
        /// <param name="users">Users to be added to room</param>
        /// <param name="roomName">Name to be given to the room</param>
        /// <param name="connectionId">Identifier of the room creator, so he can be automatically added to room</param>
        /// <returns>Room id</returns>
        public async Task<string> AddCustomRoom(List<string> users, string roomName, string connectionId)
        {
            string roomId = Guid.NewGuid().ToString();
            var putRequest = new PutItemRequest
            {
                TableName = _UsersRoomsTable,
                Item = new Dictionary<string, AttributeValue>
                {
                    { "NodeId", new AttributeValue { S = "room-" + roomId} },
                    { "TargetId", new AttributeValue { S = "room-" + roomId} }
                }
            };

            await _DDBClient.PutItemAsync(putRequest);

            putRequest = new PutItemRequest
            {
                TableName = _RoomsConnectionsTable,
                Item = new Dictionary<string, AttributeValue>
                {
                    { "RoomId", new AttributeValue { S = "room-" + roomId } },
                    { "ConnectionId", new AttributeValue { S = connectionId } }
                }
            };

            await _DDBClient.PutItemAsync(putRequest);

            foreach (var user in users)
            {
                putRequest = new PutItemRequest
                {
                    TableName = _UsersRoomsTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        { "NodeId", new AttributeValue { S = "user-" + user} },
                        { "TargetId", new AttributeValue { S = "room-" + roomId} },
                        { "Custom", new AttributeValue { BOOL = true} },
                        { "RoomId", new AttributeValue { S = roomName } }
                    }
                };
                await _DDBClient.PutItemAsync(putRequest);
            }
            return roomId;
        }

        /// <summary>
        /// Generates a normal (1 on 1) room. If such room exists, it just passes its id back to user
        /// and adds both users to connections table. Otherwise, new room has to be created.
        /// </summary>
        /// <param name="user1Id">User who wants to start a conversation</param>
        /// <param name="user2Id">Interlocutor</param>
        /// <param name="connectionId">Connection of user who wants to join. If he's not yet connected he will be</param>
        /// <returns>Id of room</returns>
        public async Task<string> GenerateNormalRoom(string user1Id, string user2Id, string connectionId)
        {
            var queryRequest = new QueryRequest
            {
                TableName = _UsersRoomsTable,
                KeyConditionExpression = $"NodeId = :partitionkeyval AND begins_with (TargetId, :sortkeyval)",
                FilterExpression = "Custom = :predicate1 AND attribute_exists (Interlocutor) AND Interlocutor = :user2",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    {":partitionkeyval", new AttributeValue { S = $"user-{user1Id}"} },
                    {":sortkeyval", new AttributeValue { S = $"room"} },
                    {":predicate1", new AttributeValue { BOOL = false } },
                    {":user2", new AttributeValue { S = user2Id } }
                },
                ProjectionExpression = "TargetId"
            };
            var queryResponse = await _DDBClient.QueryAsync(queryRequest);
            if (queryResponse.Count != 0)
            {
                var roomId = queryResponse.Items[0]["TargetId"].S;
                queryRequest = new QueryRequest
                {
                    TableName = _RoomsConnectionsTable,
                    KeyConditionExpression = $"RoomId = :partitionkeyval AND ConnectionId = :sortkeyval",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        {":partitionkeyval", new AttributeValue { S = roomId } },
                        {":sortkeyval", new AttributeValue { S = connectionId } }
                    },
                    ProjectionExpression = "RoomId"
                };
                queryResponse = await _DDBClient.QueryAsync(queryRequest);
                if (queryResponse.Count != 0) return roomId;
                else
                {
                    var putRequest = new PutItemRequest
                    {
                        TableName = _RoomsConnectionsTable,
                        Item = new Dictionary<string, AttributeValue>
                        {
                            { "RoomId", new AttributeValue { S = roomId} },
                            { "ConnectionId", new AttributeValue { S = connectionId } }
                        }
                    };

                    await _DDBClient.PutItemAsync(putRequest);
                    return roomId;
                }
            }
                
            else
            {
                string roomId = Guid.NewGuid().ToString();
                var putRequest = new PutItemRequest
                {
                    TableName = _UsersRoomsTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        { "NodeId", new AttributeValue { S = "room-" + roomId} },
                        { "TargetId", new AttributeValue { S = "room-" + roomId} }
                    }
                };
                await _DDBClient.PutItemAsync(putRequest);

                putRequest = new PutItemRequest
                {
                    TableName = _UsersRoomsTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        { "NodeId", new AttributeValue { S = "user-" + user1Id} },
                        { "TargetId", new AttributeValue { S = "room-" + roomId} },
                        { "Custom", new AttributeValue { BOOL = false} },
                        { "Interlocutor", new AttributeValue { S = user2Id } }
                    }
                };
                await _DDBClient.PutItemAsync(putRequest);

                putRequest = new PutItemRequest
                {
                    TableName = _UsersRoomsTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        { "NodeId", new AttributeValue { S = "user-" + user2Id} },
                        { "TargetId", new AttributeValue { S = "room-" + roomId} },
                        { "Custom", new AttributeValue { BOOL = false} },
                        { "Interlocutor", new AttributeValue { S = user1Id } }
                    }
                };

                await _DDBClient.PutItemAsync(putRequest);

                putRequest = new PutItemRequest
                {
                    TableName = _RoomsConnectionsTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        { "RoomId", new AttributeValue { S = "room-" + roomId} },
                        { "ConnectionId", new AttributeValue { S = connectionId } }
                    }
                };

                await _DDBClient.PutItemAsync(putRequest);

                return $"room-{roomId}";
            }
        }

        /// <summary>
        /// Add user connection to all rooms he belongs to (when logging in)
        /// </summary>
        /// <param name="userId">User who just logged in</param>
        /// <param name="connectionId">Connection id of this user</param>
        public async Task AddUserToHisRooms(string userId, string connectionId)
        {
            // retrieve rooms ids
            var queryRequest = new QueryRequest
            {
                TableName = _UsersRoomsTable,
                KeyConditionExpression = $"NodeId = :partitionkeyval AND begins_with (TargetId, :sortkeyval)",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    {":partitionkeyval", new AttributeValue { S = $"user-{userId}"} },
                    {":sortkeyval", new AttributeValue { S = $"room"} }
                },
                ProjectionExpression = "TargetId"
            };
            var queryResponse = await _DDBClient.QueryAsync(queryRequest);
            // add user to rooms
            foreach(var item in queryResponse.Items)
            {
                var putRequest = new PutItemRequest
                {
                    TableName = _RoomsConnectionsTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        { "RoomId", new AttributeValue { S = item["TargetId"].S } },
                        { "ConnectionId", new AttributeValue { S = connectionId } }
                    }
                };
                await _DDBClient.PutItemAsync(putRequest);
            }

        }

        /// <summary>
        /// Remove a connection from all rooms it belongs to
        /// </summary>
        /// <param name="connectionId">Connection id</param>
        public async Task RemoveConnectionFromTable(string connectionId)
        {
            var queryRequest = new QueryRequest
            {
                TableName = _RoomsConnectionsTable,
                IndexName = _ConnectionsIndex,
                KeyConditionExpression = "ConnectionId = :ci",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> {
                    {":ci", new AttributeValue { S =  connectionId }}
                 },
                ProjectionExpression = "ConnectionId, RoomId"
            };

            var queryResponse = await _DDBClient.QueryAsync(queryRequest);

            foreach (var item in queryResponse.Items)
            {
                var ddbRequest = new DeleteItemRequest
                {
                    TableName = _RoomsConnectionsTable,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "RoomId", new AttributeValue { S = item["RoomId"].S } },
                        { "ConnectionId", new AttributeValue { S = item["ConnectionId"].S } }
                    }
                };

                await _DDBClient.DeleteItemAsync(ddbRequest);
            }
        }

        /// <summary>
        /// Adds a record to the DynamoDBB MessagesTable containing the message, the room id, 
        /// the user id and the date measured in a UNIX Epoch time system.
        /// </summary>
        /// <param name="message">The message</param>
        /// <param name="roomId">The room in which the message was sent</param>
        /// <param name="user">Author of the message</param>
        /// <returns></returns>
        public async Task PutMessage(string message, string roomId, string user, string date)
        {
            var putRequest = new PutItemRequest
            {
                TableName = _MessagesTable,
                Item = new Dictionary<string, AttributeValue>
                {
                    { "RoomId", new AttributeValue { S = $"{roomId}"} },
                    { "DateId", new AttributeValue { N = date} },
                    { "UserId", new AttributeValue { S = user} },
                    { "Message", new AttributeValue { S = message} }

                }
            };
            await _DDBClient.PutItemAsync(putRequest);
        }

        /// <summary>
        /// Gets limit messages from the given room before the given timeStamp.
        /// </summary>
        /// <param name="roomId">The room id</param>
        /// <param name="timeStamp"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        public async Task<Messsages> GetMessages(string roomId, string timeStamp, int limit = 5)
        {
            var queryRequest = new QueryRequest
            {
                TableName = _MessagesTable,
                KeyConditionExpression = $"RoomId = :partitionkeyval AND DateId < :dateval",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    {":partitionkeyval", new AttributeValue { S = $"{roomId}"} },
                    {":dateval", new AttributeValue { N = timeStamp } }
                },
                ProjectionExpression = "Message, DateId, UserId",
                ScanIndexForward = false,
                Limit = limit
            };
            Messsages retMessages = new Messsages()
            {
                Dates = new List<string>(),
                Messages = new List<string>(),
                UserNames = new List<string>()
            };
            var queryResponse = await _DDBClient.QueryAsync(queryRequest);
            foreach (var item in queryResponse.Items)
            {
                retMessages.Messages.Add(item["Message"].S);
                retMessages.UserNames.Add(item["UserId"].S);
                retMessages.Dates.Add(item["DateId"].N);
            }
            return retMessages;
        }

        
    }
}
