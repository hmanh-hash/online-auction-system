using BidValidation.Service.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddGrpcReflection(); // cho phép dùng grpcurl / Postman để khám phá service

var app = builder.Build();

app.MapGrpcService<BidValidatorService>();
app.MapGrpcReflectionService();

app.Run();
