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
        /// DynamoDB table used to store the open connection ids. More advanced use cases could store logged on user map to their connection id to implement direct message chatting.
        /// </summary>
        string UsersTable { get; }
        string ConnectionsIndex { get; }

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
            UsersTable = System.Environment.GetEnvironmentVariable("TABLE_NAME");
            ConnectionsIndex = System.Environment.GetEnvironmentVariable("INDEX_NAME");

            this.ApiGatewayManagementApiClientFactory = (Func<string, AmazonApiGatewayManagementApiClient>)((endpoint) => 
            {
                return new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig
                {
                    ServiceURL = endpoint
                });
            });
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
            this.UsersTable = usersTable;
        }

        public async Task<APIGatewayProxyResponse> OnConnectHandler(APIGatewayProxyRequest request, ILambdaContext context)
        {
            try
            {
                var connectionId = request.RequestContext.ConnectionId;
                context.Logger.LogLine($"ConnectionId: {connectionId}");

                var ddbRequest = new PutItemRequest
                {
                    TableName = UsersTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        {RoomIdField, new AttributeValue { S = TempRoomName } },
                        {ConnectionIdField, new AttributeValue { S = connectionId}}
                    }
                };

                await DDBClient.PutItemAsync(ddbRequest);

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

                //var ddbRequest = new UpdateItemRequest
                //{
                //    TableName = ConnectionMappingTable,
                //    Key = new Dictionary<string, AttributeValue>
                //    {
                //        {RoomIdField, new AttributeValue { S = TempRoomName } },
                //        {ConnectionIdField, new AttributeValue{ S = connectionId}}
                //    },
                //    UpdateExpression = "SET roomId = :ri, userId = :ui",
                //    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                //    {
                //         {":ri", new AttributeValue{ S = doc.RoomID}},
                //         {":ui", new AttributeValue{ S = doc.UserID}}
                //    }
                //};

                var ddbRequest = new PutItemRequest
                {
                    TableName = UsersTable,
                    Item = new Dictionary<string, AttributeValue>
                    {
                        { RoomIdField, new AttributeValue { S = doc.RoomID } },
                        { ConnectionIdField, new AttributeValue { S = connectionId}},
                        { UserIdField, new AttributeValue { S = doc.UserID }}
                    }
                };

                await DDBClient.PutItemAsync(ddbRequest);

                //await DDBClient.UpdateItemAsync(ddbRequest);

                JoinResponse responseMsg = new JoinResponse()
                {
                    RoomID = doc.RoomID,
                    UserID = doc.UserID
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
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = $"Failed to connect: {e.Message}"
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

                //// The body will look something like this: {"message":"sendmessage", "data":"What are you doing?"}
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

                //var getItemRequest = new GetItemRequest
                //{
                //    TableName = ConnectionMappingTable,
                //    Key = new Dictionary<string, AttributeValue>
                //    {
                //        {ConnectionIdField, new AttributeValue{ S = connectionId}}
                //    },
                //    ProjectionExpression = $"{RoomIdField}, {UserIdField}"
                //};

                //var resItem = await DDBClient.GetItemAsync(getItemRequest);

                ChatMessageResponse chatMsg = new ChatMessageResponse
                {
                    Message = messageRequest.Message,
                    Date = DateTime.UtcNow.ToShortTimeString(),
                    Author = messageRequest.UserID
                };

                //var data = dataProperty.GetString();
                string data = JsonSerializer.Serialize(chatMsg);
                var stream = new MemoryStream(UTF8Encoding.UTF8.GetBytes(data));

                // List all of the current connections. In a more advanced use case the table could be used to grab a group of connection ids for a chat group.
                //var scanRequest = new ScanRequest
                //{
                //    TableName = ConnectionMappingTable,
                //    ProjectionExpression = ConnectionIdField,

                //};

                var queryRequest = new QueryRequest
                {
                    TableName = UsersTable,
                    KeyConditionExpression = "roomId = :ri",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue> {
                        {":ri", new AttributeValue { S =  messageRequest.RoomID }}
                     },
                    ProjectionExpression = ConnectionIdField
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
                        ConnectionId = item[ConnectionIdField].S,
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
                                TableName = UsersTable,
                                Key = new Dictionary<string, AttributeValue>
                                {
                                    {ConnectionIdField, new AttributeValue {S = postConnectionRequest.ConnectionId}}
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

                var queryRequest = new QueryRequest
                {
                    TableName = UsersTable,
                    IndexName = ConnectionsIndex,
                    KeyConditionExpression = "connectionId = :ci",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue> {
                        {":ci", new AttributeValue { S =  connectionId }}
                     },
                    ProjectionExpression = $"{ConnectionIdField}, {RoomIdField}"
                };

                var queryResponse = await DDBClient.QueryAsync(queryRequest);

                foreach(var item in queryResponse.Items)
                {
                    var ddbRequest = new DeleteItemRequest
                    {
                        TableName = UsersTable,
                        Key = new Dictionary<string, AttributeValue>
                    {
                        { RoomIdField, new AttributeValue { S = item[RoomIdField].S } },
                        { ConnectionIdField, new AttributeValue { S = item[ConnectionIdField].S } }
                    }
                    };

                    await DDBClient.DeleteItemAsync(ddbRequest);
                }

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