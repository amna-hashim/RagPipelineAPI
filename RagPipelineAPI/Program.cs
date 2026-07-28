using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RagPipeline;
using SimpleChatbot.Infrastructure;
using SimpleChatbot.Interfaces;
using SimpleChatbot.Services;
using System.Net.Http.Headers;
using System.Text;

try
{
    var builder = WebApplication.CreateBuilder(args);

    var config = new ConfigurationBuilder()
  .AddUserSecrets<Program>()
  .Build();

    var connectionString = builder.Configuration["ConnectionStrings:ChatDb"] ?? throw new InvalidOperationException("ConnectionStrings:ChatDb not found in user secrets.");

    var ghPat = config["GH_PAT"]
    ?? throw new InvalidOperationException("GH_PAT not found in user secrets.");

    var ghEndpoint = builder.Configuration["GitHubModels:Endpoint"] ?? throw new InvalidOperationException("GitHubModels:Endpoint not found in appsettings.json.");
    var ghEmbeddingModel = builder.Configuration["GitHubModels:EmbeddingModel"] ?? throw new InvalidOperationException("GitHubModels:EmbeddingModel not found in appsettings.json.");
    var ghChatModel = builder.Configuration["GitHubModels:ChatModel"] ?? throw new InvalidOperationException("GitHubModels:ChatModel not found in appsettings.json.");


    var JwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not found in user secrets.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(JwtKey))
            };
        });

    builder.Services.AddRagPipeline(builder.Configuration);

    builder.Services.AddDbContext<ChatDBContext>(options =>
    options.UseSqlServer(connectionString));

    builder.Services.AddScoped<IChatService, ChatService>();
    builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
    builder.Services.AddScoped<IMessageRepository, MessageRepository>();

    builder.Services.AddHttpClient(nameof(GitHubModelsEmbeddingService), client =>
    {
        client.BaseAddress = new Uri(ghEndpoint);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ghPat);
    });

    // Factory that creates the service with the named client and model string
    builder.Services.AddScoped<IEmbeddingService>(sp =>
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient(nameof(GitHubModelsEmbeddingService));
        return new GitHubModelsEmbeddingService(httpClient, ghEmbeddingModel);
    });

    builder.Services.AddHttpClient(nameof(OpenAiChatCompletionService), client =>
    {
        client.BaseAddress = new Uri(ghEndpoint);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ghPat);
    });


    builder.Services.AddScoped<IChatCompletionService>(sp =>
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient(nameof(OpenAiChatCompletionService));
        return new OpenAiChatCompletionService(httpClient, ghChatModel);
    });

    builder.Services.AddAuthorization();

    builder.Services.AddControllers();

    // Swagger
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var app = builder.Build();

    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.ContentType = "application/json";
            var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
            var ex = exceptionFeature?.Error;

            var (statusCode, message) = ex switch
            {
                UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
                InvalidOperationException => (StatusCodes.Status404NotFound, ex.Message),
                _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
            };

            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(new { error = message });
        });
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // Serve static files from wwwroot and treat index.html as the default document
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseAuthentication();   // populates HttpContext.User
    app.UseAuthorization();    // enforces [Authorize] attributes

    app.MapControllers();  // required so routes actually get wired up

    // If no controller route matches, serve the SPA/default index.html
    app.MapFallbackToFile("index.html");
    app.Run();
}
catch(Exception ex)
{
    // Don't swallow startup exceptions: write to the error stream and rethrow so the host fails fast.
    Console.Error.WriteLine(ex);
    throw;
}
