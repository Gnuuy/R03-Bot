using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

class Program
{
    private static readonly HttpClient _httpClient = new HttpClient();
    private static readonly ClientWebSocket _webSocket = new ClientWebSocket();
    private static string _fListApiBaseUrl = "https://www.f-list.net/json/getApiTicket.php";
    private static string _fListChatUrl = "wss://chat.f-list.net/chat2";
    private static string _fListTicket;
    private static string _username = "GloryboxBot";
    private static string _password = "cR0mDtQoL%&K4iywB8Zj";
    private static string _characterName = "R03"; // The character the bot should use
    private static string _roomCode = "ADH-ab8003be6279bd0ed6ef";
    private static string _roomCode2 = "ADH-dd98e961dd337ff7a639";
    private static DateTime _lastCommandTime = DateTime.MinValue;   
    
    private static readonly string[] _moderatorNames = { "FKLR-R03" };
    
    // RabbitMQ setup
    private static IConnection _rabbitConnection;
    private static IModel _rabbitChannel;
    private static string _discordQueueName = "f-list-discord-queue";
    private static string _talkQueueName = "broadcast_queue"; // For the TalkModule
    private static string _outputQueueName = "f-list-output-queue";
    
    static async Task Main(string[] args)
    {
        // Set up RabbitMQ connection
        var factory = new ConnectionFactory() { HostName = "rabbitmq", Port = 5672 };
        int maxRetries = 5;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                _rabbitConnection = factory.CreateConnection();
                _rabbitChannel = _rabbitConnection.CreateModel();
                
                // Declare queues
                _rabbitChannel.QueueDeclare(queue: _discordQueueName, durable: false, exclusive: false, autoDelete: false, arguments: null);
                _rabbitChannel.QueueDeclare(queue: _talkQueueName, durable: false, exclusive: false, autoDelete: false, arguments: null);
                _rabbitChannel.QueueDeclare(queue: _outputQueueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

                Console.WriteLine($"Successfully declared queues: {_discordQueueName}, {_talkQueueName}, {_outputQueueName}");
                break; // Exit loop if connection is successful
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Attempt {retry + 1} - Failed to connect to RabbitMQ: {ex.Message}");
                if (retry == maxRetries - 1) throw; // Re-throw the exception if all retries fail
                Thread.Sleep(5000); // Wait 5 seconds before retrying
            }
        }

        // Set up a consumer for the output queue (to get messages from TalkModule)
        try
        {
            var outputConsumer = new EventingBasicConsumer(_rabbitChannel);
            outputConsumer.Received += async (model, ea) =>
            {
                Console.WriteLine($"[Output Queue] Consumer triggered");

                var body = ea.Body.ToArray();
                var messageJson = Encoding.UTF8.GetString(body);
                Console.WriteLine($"[Output Queue] Received message: {messageJson}"); // Log the message received from output queue
                var brokerMessage = JsonSerializer.Deserialize<BrokerMessage>(messageJson);

                if (brokerMessage.Type.Equals("MSG", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[Output Queue] Processing message: {brokerMessage.Content}"); // Log the message processing
                    await SendMessageToRoom(brokerMessage.Content);  // Send message back to F-List room
                }
            };

            _rabbitChannel.BasicConsume(queue: _outputQueueName, autoAck: true, consumer: outputConsumer);
            Console.WriteLine($"[Output Queue] Consumer set up and listening");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Output Queue] Error setting up consumer: {ex.Message}");
        }
        
        _fListTicket = await GetTicket();

        if (!string.IsNullOrEmpty(_fListTicket))
        {
            Console.WriteLine($"Successfully obtained ticket: {_fListTicket}");
            await ConnectToWebSocket();
        }
        else
        {
            Console.WriteLine("Failed to obtain ticket.");
        }

        // Keep the program running
        Console.WriteLine("Messenger is running... Press [enter] to exit.");
        Console.ReadLine();

        // Close RabbitMQ connection
        _rabbitChannel.Close();
        _rabbitConnection.Close();
    }

    private static async Task<string> GetTicket()
    {
        var requestBody = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("account", _username),
            new KeyValuePair<string, string>("password", _password),
            new KeyValuePair<string, string>("no_characters", "true"),
            new KeyValuePair<string, string>("no_bookmarks", "true"),
            new KeyValuePair<string, string>("no_friends", "true")
        });

        try
        {
            var response = await _httpClient.PostAsync(_fListApiBaseUrl, requestBody);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(content);

            // Extract the ticket from the response
            if (jsonResponse.TryGetProperty("ticket", out var ticketElement))
            {
                return ticketElement.GetString();
            }
            else
            {
                Console.WriteLine("Failed to find the ticket in the response.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }

        return null;
    }

    private static async Task ConnectToWebSocket()
    {
        try
        {
            // Connect to the WebSocket server
            await _webSocket.ConnectAsync(new Uri(_fListChatUrl), CancellationToken.None);
            Console.WriteLine("WebSocket connection established.");

            // Send the IDN message to authenticate the bot
            var idnMessage = new
            {
                method = "ticket",
                account = _username,
                ticket = _fListTicket,
                character = _characterName,
                cname = "R03_bot",  // Replace with your client name
                cversion = "1.0"  // Replace with your client version
            };

            string idnMessageString = "IDN " + JsonSerializer.Serialize(idnMessage);
            await SendMessageAsync(idnMessageString);

            // Join the specified room
            var joinRoomMessage = new
            {
                channel = _roomCode
            };

            var joinRoomMessage2 = new
            {
                channel = _roomCode2
            };
            
            string joinRoomMessageString = "JCH " + JsonSerializer.Serialize(joinRoomMessage);
            await SendMessageAsync(joinRoomMessageString);

            string joinRoomMessageString2 = "JCH " + JsonSerializer.Serialize(joinRoomMessage2);
            await SendMessageAsync(joinRoomMessageString2);
            
            // Listen for messages
            await ReceiveMessagesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WebSocket error: {ex.Message}");
        }
        finally
        {
            _webSocket.Dispose();
            Console.WriteLine("WebSocket connection closed.");
        }
    }

    private static async Task SendMessageAsync(string message)
    {
        // Calculate the time elapsed since the last command was sent
        var timeSinceLastCommand = DateTime.Now - _lastCommandTime;

        // If less than a second has passed, wait for the remaining time
        if (timeSinceLastCommand.TotalSeconds < 1)
        {
            var delay = 1000 - (int)timeSinceLastCommand.TotalMilliseconds;
            await Task.Delay(delay);
        }

        var encodedMessage = Encoding.UTF8.GetBytes(message);
        var buffer = new ArraySegment<byte>(encodedMessage, 0, encodedMessage.Length);
        await _webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);

        // Update the last command time
        _lastCommandTime = DateTime.Now;
    }

    private static async Task ReceiveMessagesAsync()
    {
        var buffer = new byte[1024 * 4];
        while (_webSocket.State == WebSocketState.Open)
        {
            var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var receivedMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);

                try
                {
                    // Extract the message type (first three characters)
                    var messageType = receivedMessage.Substring(0, 3);
                    
                    // Ignore and do not process FLN, NLN, STA, PIN messages
                    if (messageType == "FLN" || messageType == "NLN" || messageType == "STA" || messageType == "PIN")
                    {
                        continue; // Skip processing for these message types
                    }

                    // Parse the JSON message for other types
                    var jsonMessage = JsonSerializer.Deserialize<JsonElement>(receivedMessage.Substring(4));

                    // Check if the message is a private message (e.g., "PRI" type)
                    if (messageType == "PRI")
                    {
                        // Extract character name and message content
                        var characterName = jsonMessage.GetProperty("character").GetString();
                        var messageContent = jsonMessage.GetProperty("message").GetString();

                        // Extract command if the message starts with '!'
                        string command = string.Empty;
                        if (messageContent.StartsWith("!"))
                        {
                            var commandEndIndex = messageContent.IndexOf(' ') > 0 ? messageContent.IndexOf(' ') : messageContent.Length;
                            command = messageContent.Substring(0, commandEndIndex).Trim();
                            messageContent = messageContent.Substring(commandEndIndex).Trim();
                        }

                        // Create the BrokerMessage object
                        var brokerMessage = new BrokerMessage
                        {
                            Type = messageType,      // PRI in this case
                            Command = command,       // The command (if any), or empty string
                            Sender = characterName,  // The sender's name
                            Content = messageContent // The content of the message
                        };

                        // Serialize the object to JSON
                        var jsonString = JsonSerializer.Serialize(brokerMessage);

                        // Publish the JSON string to RabbitMQ
                        PublishMessageToRabbitMQ(_talkQueueName, jsonString);
                    }

                    // Check if the message is a channel message (e.g., "MSG" type)
                    if (messageType == "MSG")
                    {
                        // Extract character name and message content
                        var characterName = jsonMessage.GetProperty("character").GetString();
                        var messageContent = jsonMessage.GetProperty("message").GetString();

                        // Create the BrokerMessage object
                        var brokerMessage = new BrokerMessage
                        {
                            Type = messageType,      // MSG in this case
                            Command = string.Empty,  // No command
                            Sender = characterName,  // The sender's name
                            Content = messageContent // The content of the message
                        };

                        // Serialize the object to JSON
                        var jsonString = JsonSerializer.Serialize(brokerMessage);

                        // Publish the JSON string to RabbitMQ
                        PublishMessageToRabbitMQ(_discordQueueName, jsonString);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing message: {ex.Message}");
                }
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
            }
        }
    }

    private static void PublishMessageToRabbitMQ(string queueName, string jsonString)
    {
        try
        {
            Console.WriteLine($"Publishing to RabbitMQ queue: {queueName} - Message: {jsonString}"); // Debugging line
            var body = Encoding.UTF8.GetBytes(jsonString);
            _rabbitChannel.BasicPublish(exchange: "", routingKey: queueName, basicProperties: null, body: body);
            Console.WriteLine("Message successfully published to RabbitMQ."); // Confirm successful publishing
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to publish message to RabbitMQ: {ex.Message}");
        }
    }
    
    private static async Task SendMessageToRoom(string message)
    {
        string formattedMessage = $"MSG {JsonSerializer.Serialize(new { channel = _roomCode, message = message })}";
        await SendMessageAsync(formattedMessage);
    }
}

public class BrokerMessage
{
    public string Type { get; set; }     // Type of the message (e.g., PRI, MSG)
    public string Command { get; set; }  // Command (e.g., !talk), or empty if no command
    public string Sender { get; set; }   // The name of the sender
    public string Content { get; set; }  // The content of the message
}
