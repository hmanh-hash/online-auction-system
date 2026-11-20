using System.Text.Json.Serialization;
using Auction.API.Data;
using Auction.API.Grpc;
using Auction.API.Hubs;
using Auction.API.Messaging;
using Auction.API.Services;
using Auction.Contracts.Protos;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- MVC + Swagger
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---------- PostgreSQL / EF Core
builder.Services.AddDbContext<AuctionDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("AuctionDb")));

// ---------- gRPC client -> BidValidation.Service
builder.Services.AddGrpcClient<BidValidator.BidValidatorClient>(o =>
    o.Address = new Uri(builder.Configuration["GrpcServices:BidValidation"] ?? "http://localhost:5200"));
builder.Services.AddScoped<BidValidationClient>();

// ---------- RabbitMQ
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.AddSingleton<RabbitMqConnectionProvider>();
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
builder.Services.AddHostedService<AuctionEventsSignalRConsumer>();

// ---------- SignalR
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ---------- Domain services + scheduler
builder.Services.Configure<AuctionLifecycleOptions>(builder.Configuration.GetSection(AuctionLifecycleOptions.SectionName));
builder.Services.AddScoped<AuctionService>();
builder.Services.AddHostedService<AuctionLifecycleWorker>();

// ---------- CORS (SignalR cần AllowCredentials => không dùng AllowAnyOrigin)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (allowedOrigins.Length == 0 || allowedOrigins.Contains("*"))
        p.SetIsOriginAllowed(_ => true); // dev: cho phép cả file:// và mọi origin
    else
        p.WithOrigins(allowedOrigins);
    p.AllowAnyHeader().AllowAnyMethod().AllowCredentials();
}));

var app = builder.Build();

// Tự động migrate DB khi khởi động (trước khi các BackgroundService chạy)
await app.Services.MigrateDatabaseAsync<AuctionDbContext>();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy", time = DateTime.UtcNow })).ExcludeFromDescription();
app.MapControllers();
app.MapHub<AuctionHub>("/hubs/auction");

app.Run();
