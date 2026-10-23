namespace TestApp;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";
    
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public  string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ExchangeName { get; set; } = "task.events";
    public string RoutingKey { get; set; } = "task.completed";
}