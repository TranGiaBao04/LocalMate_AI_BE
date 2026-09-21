using System.Text.Json.Nodes;
using FluentValidation;
using LocalMateAI.API;
using LocalMateAI.API.Middlewares;
using LocalMateAI.Application.Commands;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Trips;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using LocalMateAI.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Serilog;

try
{
    DotNetEnv.Env.TraversePath().Load();
}
catch (FileNotFoundException)
{
}

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

var jwtConfiguration = builder.Configuration.GetSection(JwtOptions.SectionName);
var configuredJwtOptions = jwtConfiguration.Get<JwtOptions>() ?? new JwtOptions();
var jwtValidationResult = new JwtOptionsValidator().Validate(
    Options.DefaultName,
    configuredJwtOptions);

if (jwtValidationResult.Failed)
{
    throw new OptionsValidationException(
        JwtOptions.SectionName,
        typeof(JwtOptions),
        jwtValidationResult.Failures);
}

JwtOptionsValidator.TryDecodeSigningKey(
    configuredJwtOptions.SigningKey,
    out var jwtSigningKeyBytes);
var jwtSigningKey = new SymmetricSecurityKey(jwtSigningKeyBytes);

var googleAuthConfiguration = builder.Configuration.GetSection(GoogleAuthOptions.SectionName);
var configuredGoogleAuthOptions =
    googleAuthConfiguration.Get<GoogleAuthOptions>() ?? new GoogleAuthOptions();
var googleAuthValidationResult = new GoogleAuthOptionsValidator().Validate(
    Options.DefaultName,
    configuredGoogleAuthOptions);

if (googleAuthValidationResult.Failed)
{
    throw new OptionsValidationException(
        GoogleAuthOptions.SectionName,
        typeof(GoogleAuthOptions),
        googleAuthValidationResult.Failures);
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter a JWT access token using the Bearer scheme."
    });
    options.OperationFilter<TripLibraryAuthorizationOperationFilter>();
    options.MapType<FeedbackQuickTag>(() => new OpenApiSchema
    {
        Type = JsonSchemaType.String,
        Enum =
        [
            JsonValue.Create(nameof(FeedbackQuickTag.Suitable)),
            JsonValue.Create(nameof(FeedbackQuickTag.NotSuitable)),
            JsonValue.Create(nameof(FeedbackQuickTag.TooDense)),
            JsonValue.Create(nameof(FeedbackQuickTag.TooFewStops)),
            JsonValue.Create(nameof(FeedbackQuickTag.TooExpensive)),
            JsonValue.Create(nameof(FeedbackQuickTag.TooFar)),
            JsonValue.Create(nameof(FeedbackQuickTag.PreferenceMismatch))
        ]
    });
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.Configure<JwtOptions>(jwtConfiguration);
builder.Services.Configure<GoogleAuthOptions>(googleAuthConfiguration);
builder.Services.Configure<PasswordHasherOptions>(options =>
{
    options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
    options.IterationCount = 220_000;
});
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IExternalLoginRepository, ExternalLoginRepository>();
builder.Services.AddScoped<ITagRepository, TagRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<ITagService, TagService>();
builder.Services.AddScoped<IPasswordHashService, AspNetCorePasswordHashService>();
builder.Services.AddScoped<IAccessTokenService, JwtAccessTokenService>();
builder.Services.AddScoped<IGoogleIdentityTokenValidator, GoogleIdentityTokenValidator>();
builder.Services.AddScoped<IMetroStationRepository, MetroStationRepository>();
builder.Services.AddScoped<IPlaceRepository, PlaceRepository>();
builder.Services.AddScoped<ITripRepository, TripRepository>();
builder.Services.AddScoped<IItineraryItemRepository, ItineraryItemRepository>();
builder.Services.AddScoped<IFeedbackRepository, FeedbackRepository>();
builder.Services.AddScoped<IGeoService, GeoService>();
builder.Services.AddScoped<IPlaceQueryService, PlaceQueryService>();
builder.Services.AddScoped<IAdminPlaceService, AdminPlaceService>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();
builder.Services.AddScoped<ICuratedItineraryRepository, CuratedItineraryRepository>();
builder.Services.AddScoped<ICuratedItineraryService, CuratedItineraryService>();
builder.Services.AddValidatorsFromAssemblyContaining<TripRequestValidator>();
builder.Services.AddScoped<ITripCriteriaNormalizationService, TripCriteriaNormalizationService>();
builder.Services.AddScoped<ITripOriginResolverService, TripOriginResolverService>();
builder.Services.AddScoped<ITripFeasibilityService, TripFeasibilityService>();
builder.Services.AddScoped<IMetroClusterMatchingService, MetroClusterMatchingService>();
builder.Services.AddScoped<ICandidateFilterService, CandidateFilterService>();
builder.Services.AddScoped<ITagSimilarityScorer, TagSimilarityScorer>();
builder.Services.AddScoped<IAlternativePlaceFinder, AlternativePlaceFinder>();
builder.Services.AddScoped<IItineraryTimelineRecalculator, ItineraryTimelineRecalculator>();
builder.Services.AddScoped<ITripMatchingService, TripMatchingService>();
builder.Services.AddScoped<IHeuristicFallbackEngine, HeuristicFallbackEngine>();
builder.Services.AddScoped<ITripService, TripService>();
builder.Services.AddScoped<ITripAlternativesService, TripAlternativesService>();
builder.Services.AddScoped<ITripItemReplacementService, TripItemReplacementService>();
builder.Services.AddScoped<IFinalizeTripCommand, FinalizeTripCommand>();
builder.Services.AddScoped<IForkTripCommand, ForkTripCommand>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();

// Register Application Services for Google Maps & Navigation (BE-60, BE-61, BE-62, BE-63)
builder.Services.AddSingleton<IGoogleMapsUrlBuilderService, GoogleMapsUrlBuilderService>();
builder.Services.AddSingleton<ICoordinatesValidationService, CoordinatesValidationService>();
builder.Services.AddScoped<IRouteEstimateService, RouteEstimateService>();
builder.Services.AddScoped<IMetroWalkingRouter, MetroWalkingRouter>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions => npgsqlOptions.UseNetTopologySuite()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtSigningKey,
            ValidateIssuer = true,
            ValidIssuer = configuredJwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = configuredJwtOptions.Audience,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidTypes = ["JWT"],
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
    options.AddPolicy(frontendClientPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()));

var app = builder.Build();

using (var seedScope = app.Services.CreateScope())
{
    var seedDbContext = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await seedDbContext.Database.MigrateAsync();
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
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TripActionGuardMiddleware>();
app.MapControllers();

app.Run();
