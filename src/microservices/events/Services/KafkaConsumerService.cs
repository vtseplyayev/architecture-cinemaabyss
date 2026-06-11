using Confluent.Kafka;

namespace CinemaAbyss.Events.Services
{
    public sealed class KafkaConsumerService(
        ILogger<KafkaConsumerService> logger,
        KafkaSettings settings) : BackgroundService
    {
        private static readonly string[] Topics = ["movie-events", "user-events", "payment-events"];

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
            => Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);

        private void ConsumeLoop(CancellationToken ct)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = settings.BootstrapServers,
                GroupId = "events-service-group",
                AutoOffsetReset = AutoOffsetReset.Latest,
                EnableAutoCommit = true,
                SessionTimeoutMs = 10_000,
            };

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var consumer = new ConsumerBuilder<Ignore, string>(config).Build();
                    consumer.Subscribe(Topics);
                    logger.LogInformation("[KAFKA] Consumer started. Subscribed to: {Topics}",
                        string.Join(", ", Topics));

                    while (!ct.IsCancellationRequested)
                    {
                        try
                        {
                            var result = consumer.Consume(TimeSpan.FromSeconds(1));
                            if (result is not null)
                            {
                                logger.LogInformation(
                                    "[EVENT CONSUMED] topic={Topic} partition={Partition} offset={Offset} | {Message}",
                                    result.Topic,
                                    result.Partition.Value,
                                    result.Offset.Value,
                                    result.Message.Value);
                            }
                        }
                        catch (ConsumeException ex)
                        {
                            logger.LogError("[KAFKA] Consume error: {Reason}", ex.Error.Reason);
                        }
                    }

                    consumer.Close();
                    break;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning("[KAFKA] Not ready, retrying in 5 s: {Message}", ex.Message);
                    Thread.Sleep(5_000);
                }
            }
        }
    }
}