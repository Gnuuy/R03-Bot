using System.Text;
using System.Text.Json;
using Discord;
using Discord.WebSocket;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

class Program
{
    private static string _queueName = "f-list-discord-queue";
    private static string _discordToken = "MTIzMTU5MzMwODQyMjE0NDA5NQ.GEIVWR.cI7zNwgXZepjuhIoviipPyvw_X_tPgnqCgNcwE";
    private static ulong _channelId = 1254621583754924052; // Replace with your Discord channel ID
    
  private static DiscordSocketClient _client;
  
      static async Task Main(string[] args)
    {
        // Initialize the Discord client
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages // Specify only the necessary intents
        };
        _client = new DiscordSocketClient(config);
        _client.Log += LogAsync;

        // Log in to Discord
        await _client.LoginAsync(TokenType.Bot, _discordToken);
        await _client.StartAsync();

        // Ensure the bot is connected before continuing
        await Task.Delay(1000);

        var testMessageObject = new BrokerMessage
        {
            Sender = "SYSTEM",
            Content = "This is a test message to confirm Discord connection."
        };

        // Test sending a message directly to Discord to confirm the connection
        //await ForwardMessageToDiscord(testMessageObject);

        // Start consuming messages from RabbitMQ
        ConsumeMessagesFromRabbitMQ();

        // Keep the bot running
        await Task.Delay(-1);
    }

    private static void ConsumeMessagesFromRabbitMQ()
    {
        var factory = new ConnectionFactory() { HostName = "rabbitmq", Port = 5672 };
        RabbitMQ.Client.IConnection connection = null;
        IModel channel = null;
        int maxRetries = 5;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                connection = factory.CreateConnection();
                channel = connection.CreateModel();
                channel.QueueDeclare(queue: _queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);
                Console.WriteLine("Connected to RabbitMQ and declared queue."); // Confirm connection
                break; // Exit loop if connection is successful
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Attempt {retry + 1} - Failed to connect to RabbitMQ: {ex.Message}");
                if (retry == maxRetries - 1) throw; // Re-throw the exception if all retries fail
                Thread.Sleep(5000); // Wait 5 seconds before retrying
            }
        }

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var messageJson = Encoding.UTF8.GetString(body);
            Console.WriteLine($"Received from RabbitMQ: {messageJson}"); // Debugging line

            try
            {
                // Deserialize the JSON message
                var messageObject = JsonSerializer.Deserialize<BrokerMessage>(messageJson);

                // Forward the message to Discord
                await ForwardMessageToDiscord(messageObject);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing message: {ex.Message}");
            }
        };

        channel.BasicConsume(queue: _queueName, autoAck: true, consumer: consumer);

        Console.WriteLine("Listening for messages from RabbitMQ...");
    }

    private static async Task ForwardMessageToDiscord(BrokerMessage messageObject)
    {
        try
        {
            var channel = _client.GetChannel(_channelId) as IMessageChannel;
            if (channel != null)
            {
                string formattedMessage;

                // Check if the message starts with /me
                if (messageObject.Content.StartsWith("/me ", StringComparison.OrdinalIgnoreCase))
                {
                    // Remove "/me " from the beginning of the message
                    var content = messageObject.Content.Substring(4).Trim();

                    // Format the message as "_[CharacterName] does a dance_"
                    formattedMessage = $"_[{messageObject.Sender}] {content}_";
                }
                else
                {
                    // Standard message formatting as "[CharacterName]: does a dance"
                    formattedMessage = $"[{messageObject.Sender}]: {messageObject.Content}";
                }

                // Send the message to Discord
                await channel.SendMessageAsync(formattedMessage);
                Console.WriteLine($"Message sent to Discord: {formattedMessage}");
            }
            else
            {
                Console.WriteLine("Failed to send message to Discord. Channel not found.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending message to Discord: {ex.Message}");
        }
    }

    private static Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }
}

// Define the MessageObject class to represent the structure of the RabbitMQ message
public class BrokerMessage
{
    public string Type { get; set; }     // Type of the message (e.g., PRI, MSG)
    public string Command { get; set; }  // Command (e.g., !talk), or empty if no command
    public string Sender { get; set; }   // The name of the sender
    public string Content { get; set; }  // The content of the message
}