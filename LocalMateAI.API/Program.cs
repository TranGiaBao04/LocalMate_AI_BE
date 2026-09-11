using LocalMateAI.API.Middlewares;
using LocalMateAI.API.Security;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

const string frontendClientPolicy = "FrontendClient";

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

if (allowedOrigins.Length == 0)
{
    throw new InvalidOperationException("At least one CORS allowed origin must be configured.");
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.Configure<PasswordHasherOptions>(options =>
{
    options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
    options.IterationCount = 220_000;
});
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IPasswordHashService, AspNetCorePasswordHashService>();
builder.Services.AddScoped<IMetroStationRepository, MetroStationRepository>();
builder.Services.AddScoped<IPlaceRepository, PlaceRepository>();
builder.Services.AddScoped<IGeoService, GeoService>();
builder.Services.AddScoped<IPlaceQueryService, PlaceQueryService>();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseNetTopologySuite()));
builder.Services.AddCors(options =>
    options.AddPolicy(frontendClientPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()));

var app = builder.Build();

using (var seedScope = app.Services.CreateScope())
{
    var seedDbContext = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DataSeeder.SeedAsync(seedDbContext);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors(frontendClientPolicy);
app.UseAuthorization();
app.MapControllers();

app.Run();
