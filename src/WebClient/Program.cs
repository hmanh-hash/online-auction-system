using System.Text.Json;

// WebClient: host tĩnh cho index.html + app.js. Có thể mở trực tiếp wwwroot/index.html bằng trình duyệt.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Cấu hình runtime cho front-end (địa chỉ Auction.API mà TRÌNH DUYỆT truy cập được)
app.MapGet("/config.js", (IConfiguration config) =>
{
    var json = JsonSerializer.Serialize(new { apiBaseUrl = config["ApiBaseUrl"] ?? "http://localhost:5100" });
    return Results.Content($"window.APP_CONFIG = {json};", "application/javascript");
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.Run();
