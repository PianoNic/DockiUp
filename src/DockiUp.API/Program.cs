using DockiUp.Application.Models;
using DockiUp.Application.Queries;
using DockiUp.Application.Services;
using DockiUp.Domain.Enums;
using DockiUp.Domain.Models;
using DockiUp.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add authentication services
var configuration = builder.Configuration;
var issuer = configuration["Jwt:Issuer"];
var audience = configuration["Jwt:Audience"];
var secretKey = configuration["JWT_SECRET_KEY"];

if (string.IsNullOrEmpty(secretKey))
{
    throw new InvalidOperationException("JWT secret key is not set in environment variables.");
}

builder.Services.AddAuthorization();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
        };
    });

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddSpaStaticFiles(spaStaticFilesOptions => { spaStaticFilesOptions.RootPath = "wwwroot/browser"; });

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(AppQuery).Assembly));

// Add the WebhookSecretService with scoped lifetime
builder.Services.AddScoped<IWebhookSecretService, WebhookSecretService>();

// Configure the DbContext with a connection string.
builder.Services.AddDbContext<DockiUpDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DockiUpDatabase"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DockiUpDatabase")),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)
    ));

// Configure SystemPaths
builder.Services.Configure<SystemPaths>(builder.Configuration.GetSection("SystemPaths"));

// Register Swagger services
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "DockiUp API",
        Version = "v1",
        Description = "API for the DockiUp document management system"
    });

    // Add JWT Authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. \r\n\r\n Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\nExample: \"Bearer 1safsfsdfdfd\"",
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement {
        {
            new OpenApiSecurityScheme {
                Reference = new OpenApiReference {
                    Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// Add CORS policy to allow all origins, methods, and headers
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(corsBuilder =>
        {
            corsBuilder.WithOrigins("http://localhost:4200");
            corsBuilder.WithExposedHeaders("Content-Disposition");
            corsBuilder.AllowAnyHeader();
            corsBuilder.AllowAnyMethod();
            corsBuilder.AllowCredentials();
            if (!builder.Environment.IsProduction())
            {
                corsBuilder.WithExposedHeaders("X-Impersonate");
            }
        });
    });
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        // Get the database context
        var dbContext = services.GetRequiredService<DockiUpDbContext>();

        // Apply any pending migrations
        logger.LogInformation("Applying migrations...");
        dbContext.Database.Migrate();
        logger.LogInformation("Migrations applied successfully");

        // Seed data using the integrated method
        logger.LogInformation("Seeding database...");

        var adminUser = new User
        {
            Username = "admin",
            Email = "admin@dockiup.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
            UserRole = UserRole.Admin,
            UserSettings = new UserSettings
            {
                PreferredColorScheme = ColorScheme.System
            }
        };

        // Check if the user already exists
        var existingUser = dbContext.Users
            .FirstOrDefault(u => u.Username == adminUser.Username || u.Email == adminUser.Email);

        if (existingUser == null)
        {
            dbContext.Users.Add(adminUser);
            dbContext.SaveChanges();
            logger.LogInformation("Admin user created successfully");
        }
        else
        {
            logger.LogInformation("Admin user already exists");
        }

        logger.LogInformation("Database seeding completed");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred during database migration or seeding");
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "DockiUp API V1");
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();

if (!app.Environment.IsDevelopment())
{
    app.UseSpaStaticFiles();
}

// Ensure frontend routes work
app.UseRouting();
app.UseAuthorization();
app.UseAuthentication();
app.UseCors();
app.MapControllers();

// Serve Angular Frontend in Production
if (!app.Environment.IsDevelopment())
{
    app.UseSpa(spa =>
    {
        spa.Options.SourcePath = "wwwroot";
    });
}

app.Run();