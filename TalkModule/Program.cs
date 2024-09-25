using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

class Program
{
    private static string _inputQueueName = "broadcast_queue";
    private static string _outputQueueName = "f-list-output-queue";
    private static string _discordQueueName = "f-list-discord-queue"; // New queue for Discord module

    static void Main(string[] args)
    {
        Console.WriteLine("TalkModule is running...");

        var factory = new ConnectionFactory() { HostName = "rabbitmq", Port = 5672 };
        IConnection connection = null;
        IModel channel = null;
        int maxRetries = 5;

        // Retry logic for connecting to RabbitMQ
        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                connection = factory.CreateConnection();
                channel = connection.CreateModel();
                
                // Declare the broadcast_queue before binding it
                channel.QueueDeclare(queue: _inputQueueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

                // Declare the output and discord queues
                channel.QueueDeclare(queue: _outputQueueName, durable: false, exclusive: false, autoDelete: false, arguments: null);
                channel.QueueDeclare(queue: _discordQueueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

                // Declare the exchange and bind the queue to it
                channel.ExchangeDeclare(exchange: "broadcast_exchange", type: ExchangeType.Fanout);
                channel.QueueBind(queue: _inputQueueName, exchange: "broadcast_exchange", routingKey: "");
                
                break; // Exit loop if connection is successful
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Attempt {retry + 1} - Failed to connect to RabbitMQ: {ex.Message}");
                if (retry == maxRetries - 1) throw; // Re-throw the exception if all retries fail
                System.Threading.Thread.Sleep(5000); // Wait 5 seconds before retrying
            }
        }

        // Event handler for processing received messages
        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var messageJson = Encoding.UTF8.GetString(body);
            Console.WriteLine($"Received message: {messageJson}");

            // Deserialize the message
            var brokerMessage = JsonSerializer.Deserialize<BrokerMessage>(messageJson);

            // Check if it's a !talk command
            if (brokerMessage.Command == "!talk")
            {
                Console.WriteLine("Processing !talk command...");
                brokerMessage.Type = "MSG"; // Change the command to MSG
                brokerMessage.Sender = "R03";
                brokerMessage.Content = brokerMessage.Content.Trim(); // Trim any leading/trailing spaces

                // Serialize the modified message back to JSON
                var outputMessageJson = JsonSerializer.Serialize(brokerMessage);

                // Publish to output queue for F-list
                var outputBody = Encoding.UTF8.GetBytes(outputMessageJson);
                channel.BasicPublish(exchange: "", routingKey: _outputQueueName, basicProperties: null, body: outputBody);
                Console.WriteLine($"Published message to output queue: {outputMessageJson}");

                // Publish to discord-output-queue for Discord
                channel.BasicPublish(exchange: "", routingKey: _discordQueueName, basicProperties: null, body: outputBody);
                Console.WriteLine($"Published message to Discord queue: {outputMessageJson}");
            }
        };

        channel.BasicConsume(queue: _inputQueueName, autoAck: true, consumer: consumer);

        // Keep the application running
        while (true)
        {
            System.Threading.Thread.Sleep(1000); // Sleep for 1 second
        }
    }
}

public class BrokerMessage
{
    public string Type { get; set; }     // Type of the message (e.g., PRI, MSG)
    public string Command { get; set; }  // Command (e.g., !talk), or empty if no command
    public string Sender { get; set; }   // The name of the sender
    public string Content { get; set; }  // The content of the message
}
