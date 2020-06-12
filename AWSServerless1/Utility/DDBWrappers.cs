using System;
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

namespace AWSServerless1.Utility
{
    class DDBWrappers
    {
        private IAmazonDynamoDB _DDBClient { get; }
        private Func<string, IAmazonApiGatewayManagementApi> _ApiGatewayManagementApiClientFactory { get; }
        private string _UsersRoomsTable { get; }
        private string _RoomsConnectionsTable { get; }
        private string _UsersIndex { get; set; }
        private string _ConnectionsIndex { get; set; }
        public DDBWrappers(IAmazonDynamoDB dDBClient,
                    Func<string, IAmazonApiGatewayManagementApi> apiGatewayManagementApiClientFactory,
                    string usersRoomsTable,
                    string roomsConnectionsTable,
                    string usersIndex,
                    string connectionsIndex)
        {
            _DDBClient = dDBClient;
            _ApiGatewayManagementApiClientFactory = apiGatewayManagementApiClientFactory;
            _UsersRoomsTable = usersRoomsTable;
            _RoomsConnectionsTable = roomsConnectionsTable;
            _UsersIndex = usersIndex;
            _ConnectionsIndex = connectionsIndex;
        }

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

        public async Task<string> GenerateNormalRoom(string user1Id, string user2Id)
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
                    //{":inter", new AttributeValue { S = "Interlocutor" } },
                    {":user2", new AttributeValue { S = user2Id } }
                },
                ProjectionExpression = "TargetId"
            };
            var queryResponse = await _DDBClient.QueryAsync(queryRequest);
            if (queryResponse.Count != 0) return queryResponse.Items[0]["TargetId"].S;
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
                return roomId;
            }
        }

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
    }
}
