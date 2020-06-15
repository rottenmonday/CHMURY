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
using AWSServerless1.Utility;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace AWSServerless1
{
    public class Functions
    {
        public const string ConnectionIdField = "connectionId";
        public const string RoomIdField = "roomId";
        public const string UserIdField = "userId";
        public const string TempRoomName = "Purgatory";
        /// <summary>
        /// Number of previous message to load at a time
        /// </summary>
        public const int MessageLoadingLimit = 5;

        /// <summary>
        /// DynamoDB table used to store the open connection ids. More advanced use cases could store logged on user map to their connection id to implement direct message chatting.
        /// </summary>
        string UsersRoomsTable { get; }
        string RoomsConnectionsTable { get; }
        string UsersIndex { get; }
        string ConnectionsIndex { get;  }
        string MessagesTable { get; }
        //string ConnectionsIndex { get; }
        DDBWrappers DDBUtils { get; set; }

        /// <summary>
        /// DynamoDB service client used to store and retieve connection information from the ConnectionMappingTable
        /// </summary>
        IAmazonDynamoDB DDBClient { get; }

        /// <summary>
        /// Factory func to create the AmazonApiGatewayManagementApiClient. This is needed to created per endpoint of the a connection. It is a factory to make it easy for tests
        /// to moq the creation.
        /// </summary>
        Func<string, IAmazonApiGatewayManagementApi> ApiGatewayManagementApiClientFactory { get; }


        /// <summary>
        /// Default constructor that Lambda will invoke.
        /// </summary>
        public Functions()
        {
            DDBClient = new AmazonDynamoDBClient();

            // Grab the name of the DynamoDB from the environment variable setup in the CloudFormation template serverless.template
            UsersRoomsTable = System.Environment.GetEnvironmentVariable("USERS_ROOMS_TABLE_NAME");
            RoomsConnectionsTable = System.Environment.GetEnvironmentVariable("ROOMS_CONNECTIONS_TABLE_NAME");
            UsersIndex = System.Environment.GetEnvironmentVariable("USERS_INDEX_NAME");
            ConnectionsIndex = System.Environment.GetEnvironmentVariable("CONNECTIONS_INDEX_NAME");
            MessagesTable = System.Environment.GetEnvironmentVariable("MESSAGES_TABLE_NAME");

            this.ApiGatewayManagementApiClientFactory = (Func<string, AmazonApiGatewayManagementApiClient>)((endpoint) => 
            {
                return new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig
                {
                    ServiceURL = endpoint
                });
            });
            DDBUtils = new DDBWrappers(DDBClient, ApiGatewayManagementApiClientFactory,
                                        UsersRoomsTable, RoomsConnectionsTable, MessagesTable, UsersIndex, ConnectionsIndex);
        }

        /// <summary>
        /// Constructor used for testing allow tests to pass in moq versions of the service clients.
        /// </summary>
        /// <param name="ddbClient"></param>
        /// <param name="apiGatewayManagementApiClientFactory"></param>
        /// <param name="connectionMappingTable"></param>
        public Functions(IAmazonDynamoDB ddbClient, Func<string, IAmazonApiGatewayManagementApi> apiGatewayManagementApiClientFactory, string usersTable)
        {
            this.DDBClient = ddbClient;
            this.ApiGatewayManagementApiClientFactory = apiGatewayManagementApiClientFactory;
            this.UsersRoomsTable = usersTable;
        }

        public async Task<APIGatewayProxyResponse> OnConnectHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = "Connected."
                };
            }
            catch (Exception e)
            {
                context.Logger.LogLine("Error connecting: " + e.Message);
                context.Logger.LogLine(e.StackTrace);
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = $"Failed to connect: {e.Message}"
                };
            }
        }

        public async Task<APIGatewayProxyResponse> AddRoomHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                var connectionId = request.RequestContext.ConnectionId;
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                };
                AddRoomRequest doc = JsonSerializer.Deserialize<AddRoomRequest>(request.Body, options);
                doc.OtherUsers.Add(doc.UserId);
                string roomId = await DDBUtils.AddCustomRoom(doc.OtherUsers, doc.RoomName, connectionId);
                roomId = "room-" + roomId;
                AddRoomResponse responseMsg = new AddRoomResponse()
                {
                    Success = true,
                    RoomId = roomId,
                    RoomName = doc.RoomName
                };

                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = JsonSerializer.Serialize(responseMsg)
                };
            }

            catch (Exception e)
            {
                context.Logger.LogLine("Error adding room: " + e.Message);
                context.Logger.LogLine(e.StackTrace);
                AddRoomResponse responseMsg = new AddRoomResponse()
                {
                    Success = false
                };
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = JsonSerializer.Serialize(responseMsg)
                };
            }
        }

        public async Task<APIGatewayProxyResponse> JoinHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                var connectionId = request.RequestContext.ConnectionId;
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                };
                JoinRequest doc = JsonSerializer.Deserialize<JoinRequest>(request.Body, options);

                string roomId = await DDBUtils.GenerateNormalRoom(doc.User1ID, doc.User2ID, connectionId);

                JoinResponse responseMsg = new JoinResponse()
                {
                    RoomName = "Conversation with " + doc.User2ID,
                    RoomID = roomId,
                    Success = true
                };

                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = JsonSerializer.Serialize(responseMsg)
                };
            }
            catch (Exception e)
            {
                context.Logger.LogLine("Error connecting: " + e.Message);
                context.Logger.LogLine(e.StackTrace);
                JoinResponse responseMsg = new JoinResponse()
                {
                    Success = false
                };
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = JsonSerializer.Serialize(responseMsg)
                };
            }
        }

        public async Task<APIGatewayProxyResponse> LoginHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                var connectionId = request.RequestContext.ConnectionId;
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                };
                LoginRequest logRequest = JsonSerializer.Deserialize<LoginRequest>(request.Body, options);
                var ddbRequest = new PutItemRequest
                {
                    TableName = UsersRoomsTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        { "NodeId", new AttributeValue { S = "user-" + logRequest.UserID } },
                        { "TargetId", new AttributeValue { S = "user-" + logRequest.UserID }},
                        { "UserId", new AttributeValue { S = logRequest.UserID }}
                    }
                };

                await DDBUtils.AddUserToHisRooms(logRequest.UserID, connectionId);
                await DDBClient.PutItemAsync(ddbRequest);

                var customRoomsInfo = await DDBUtils.GetUserCustomRooms(logRequest.UserID);

                LoginResponse responseMsg = new LoginResponse()
                {
                    Success = true,
                    Users = await DDBUtils.GetAllUsers(),
                    CustomRoomsNames = customRoomsInfo.Item1,
                    CustomRoomsIds = customRoomsInfo.Item2
                };

                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.OK,
                    Body = JsonSerializer.Serialize(responseMsg)
                };
            }
            catch (Exception e)
            {
                context.Logger.LogLine("Error logging in: " + e.Message);
                context.Logger.LogLine(e.StackTrace);
                LoginResponse responseMsg = new LoginResponse()
                {
                    Success = false
                };
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Body = JsonSerializer.Serialize(responseMsg)
                };
            }
        }

        public async Task<APIGatewayProxyResponse> SendMessageHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                // Construct the API Gateway endpoint that incoming message will be broadcasted to.
                var domainName = request.RequestContext.DomainName;
                var stage = request.RequestContext.Stage;
                var endpoint = $"https://{domainName}/{stage}";
                var connectionId = request.RequestContext.ConnectionId;
                context.Logger.LogLine($"API Gateway management endpoint: {endpoint}");

                JsonDocument message = JsonDocument.Parse(request.Body);

                // Grab the data from the JSON body which is the message to broadcasted.
                JsonElement dataProperty;
                if (!message.RootElement.TryGetProperty("data", out dataProperty))
                {
                    context.Logger.LogLine("Failed to find data element in JSON document");
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = (int)HttpStatusCode.BadRequest
                    };
                }
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                };
                ChatMessageRequest messageRequest = JsonSerializer.Deserialize<ChatMessageRequest>(dataProperty.ToString(), options);
                string date = (DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds.ToString();
                await DDBUtils.PutMessage(messageRequest.Message, messageRequest.RoomID, messageRequest.UserID, date);
                ChatMessageResponse chatMsg = new ChatMessageResponse
                {
                    Success = true,
                    Message = messageRequest.Message,
                    Date = date,
                    Author = messageRequest.UserID,
                    RoomId = messageRequest.RoomID
                };

                string data = JsonSerializer.Serialize(chatMsg);
                var stream = new MemoryStream(UTF8Encoding.UTF8.GetBytes(data));

                var queryRequest = new QueryRequest
                {
                    TableName = RoomsConnectionsTable,
                    KeyConditionExpression = "RoomId = :ri",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue> {
                        {":ri", new AttributeValue { S =  messageRequest.RoomID }}
                     },
                    ProjectionExpression = "RoomId, ConnectionId"
                };

                var queryResponse = await DDBClient.QueryAsync(queryRequest);

                // Construct the IAmazonApiGatewayManagementApi which will be used to send the message to.
                var apiClient = ApiGatewayManagementApiClientFactory(endpoint);

                // Loop through all of the connections and broadcast the message out to the connections.
                var count = 0;
                foreach (var item in queryResponse.Items)
                {
                    var postConnectionRequest = new PostToConnectionRequest
                    {
                        ConnectionId = item["ConnectionId"].S,
                        Data = stream
                    };

                    try
                    {
                        context.Logger.LogLine($"Post to connection {count}: {postConnectionRequest.ConnectionId}");
                        stream.Position = 0;
                        await apiClient.PostToConnectionAsync(postConnectionRequest);
                        count++;
                    }
                    catch (AmazonServiceException e)
                    {
                        // API Gateway returns a status of 410 GONE then the connection is no
                        // longer available. If this happens, delete the identifier
                        // from our DynamoDB table.
                        if (e.StatusCode == HttpStatusCode.Gone)
                        {
                            var ddbDeleteRequest = new DeleteItemRequest
                            {
                                TableName = RoomsConnectionsTable,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    {"RoomId", new AttributeValue {S = item["RoomId"].S } },
                                    {"ConnectionId", new AttributeValue {S = postConnectionRequest.ConnectionId}}
                                }
                            };

                            context.Logger.LogLine($"Deleting gone connection: {postConnectionRequest.ConnectionId}");
                            await DDBClient.DeleteItemAsync(ddbDeleteRequest);
                        }
                        else
                        {
                            context.Logger.LogLine($"Error posting message to {postConnectionRequest.ConnectionId}: {e.Message}");
                            context.Logger.LogLine(e.StackTrace);
                        }
                    }
                }

                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.OK,
                    Body = "Data sent to " + count + " connection" + (count == 1 ? "" : "s")
                };
            }
            catch (Exception e)
            {
                context.Logger.LogLine("Error disconnecting: " + e.Message);
                context.Logger.LogLine(e.StackTrace);
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Body = $"Failed to send message: {e.Message}"
                };
            }
        }

        public async Task<APIGatewayProxyResponse> GetMessagesHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            
            try
            {
                var connectionId = request.RequestContext.ConnectionId;
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                };
                GetMessagesRequest getMessagesRequest = JsonSerializer.Deserialize<GetMessagesRequest>(request.Body, options);

                var queryResponse = await DDBUtils.GetMessages(getMessagesRequest.RoomID, 
                                                               getMessagesRequest.TimeStamp,
                                                               MessageLoadingLimit);
                GetMessagesResponse getMessagesResponse = new GetMessagesResponse()
                {
                    Messages = queryResponse.Messages,
                    Dates = queryResponse.Dates,
                    Users = queryResponse.UserNames,
                    Success = true
                };

                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.OK,
                    Body = JsonSerializer.Serialize(getMessagesResponse)
                };
            }
            catch (Exception e)
            {
                context.Logger.LogLine("Error logging in: " + e.Message);
                context.Logger.LogLine(e.StackTrace);
                GetMessagesResponse responseMsg = new GetMessagesResponse()
                {
                    Success = false
                };
                return new APIGatewayProxyResponse
                {
                    StatusCode = (int)HttpStatusCode.InternalServerError,
                    Body = JsonSerializer.Serialize(responseMsg)
                };
            }
        }

        public async Task<APIGatewayProxyResponse> HelloHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                var connectionId = request.RequestContext.ConnectionId;
                // Construct the API Gateway endpoint that incoming message will be broadcasted to.
                var domainName = request.RequestContext.DomainName;
                var stage = request.RequestContext.Stage;
                var endpoint = $"https://{domainName}/{stage}";
                var apiClient = ApiGatewayManagementApiClientFactory(endpoint);
                string message = $"Hello, {connectionId}! Here's a date for you: {DateTime.Now.ToShortDateString()}";
                MemoryStream stream = new MemoryStream(UTF8Encoding.UTF8.GetBytes(message));
                PostToConnectionRequest postConnectionRequest = new PostToConnectionRequest
                {
                    ConnectionId = connectionId,
                    Data = stream
                };
                stream.Position = 0;
                await apiClient.PostToConnectionAsync(postConnectionRequest);
                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = $"Successfully sent Hello message!"
                };
            }
            catch (Exception e)
            {
                context.Logger.LogLine("Error sending hello: " + e.Message);
                context.Logger.LogLine(e.StackTrace);
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = $"Failed to send message: {e.Message}"
                };
            }
        }

        public async Task<APIGatewayProxyResponse> ByeHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = $"Hello, ! Date for you is: NONE."
                };
            }
            catch (Exception e)
            {
                context.Logger.LogLine("Error sending hello: " + e.Message);
                context.Logger.LogLine(e.StackTrace);
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = $"Failed to send message: {e.Message}"
                };
            }
        }

        public async Task<APIGatewayProxyResponse> OnDisconnectHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                var connectionId = request.RequestContext.ConnectionId;
                context.Logger.LogLine($"ConnectionId: {connectionId}");

                await DDBUtils.RemoveConnectionFromTable(connectionId);

                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = "Disconnected."
                };
            }
            catch (Exception e)
            {
                context.Logger.LogLine("Error disconnecting: " + e.Message);
                context.Logger.LogLine(e.StackTrace);
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = $"Failed to disconnect: {e.Message}"
                };
            }
        }
    }
}