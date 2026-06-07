using Confluent.Kafka;

var kafkaBrokers = Environment.GetEnvironmentVariable("KAFKA_BROKERS") ?? "localhost:9092";

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    var port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "8082");
    options.ListenAnyIP(port);
});

// Singleton Kafka producer
var producerConfig = new ProducerConfig
{
    BootstrapServers = kafkaBrokers,
    MessageTimeoutMs = 10000,
};
var producer = new ProducerBuilder<Null, string>(producerConfig).Build();
builder.Services.AddSingleton<IProducer<Null, string>>(producer);

// Kafka consumer background service
builder.Services.AddSingleton(new KafkaSettings(kafkaBrokers));
builder.Services.AddHostedService<KafkaConsumerService>();

var app = builder.Build();

// ── Health ──────────────────────────────────────────────────────────────────
app.MapGet("/api/events/health", () => Results.Ok(new { status = true }));

// ── Movie Event ─────────────────────────────────────────────────────────────
app.MapPost("/api/events/movie", async (HttpContext ctx, IProducer<Null, string> prod) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    await prod.ProduceAsync("movie-events", new Message<Null, string> { Value = body });
    return Results.Created("/api/events/movie", new { status = "success" });
});

// ── User Event ──────────────────────────────────────────────────────────────
app.MapPost("/api/events/user", async (HttpContext ctx, IProducer<Null, string> prod) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    await prod.ProduceAsync("user-events", new Message<Null, string> { Value = body });
    return Results.Created("/api/events/user", new { status = "success" });
});

// ── Payment Event ────────────────────────────────────────────────────────────
app.MapPost("/api/events/payment", async (HttpContext ctx, IProducer<Null, string> prod) =>
{
    using var reader = new StreamReader(ctx.Request.Body);
    var body = await reader.ReadToEndAsync();
    await prod.ProduceAsync("payment-events", new Message<Null, string> { Value = body });
    return Results.Created("/api/events/payment", new { status = "success" });
});

app.Run();

// ── Settings ─────────────────────────────────────────────────────────────────


// ── Background Kafka Consumer ─────────────────────────────────────────────────
