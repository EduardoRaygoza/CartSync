using CartSync.SyncApi.Domain;
using CartSync.SyncApi.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<CanonicalReducer>();
builder.Services.AddSingleton<InMemoryHouseholdStore>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["ClientOrigin"] ?? "http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();
app.UseCors();
app.MapControllers();
app.Run();

public partial class Program;
