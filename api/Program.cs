using Microsoft.EntityFrameworkCore;
using BubbleSplash.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// --- Services ---

// EF Core + PostgreSQL
builder.Services.AddDbContext<BubbleSplashDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Controllers
builder.Services.AddControllers();

// OpenAPI / Swagger
builder.Services.AddOpenApi();

// CORS — allow the frontend (adjust origins for production)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                ?? ["http://localhost:5500", "http://127.0.0.1:5500"])
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// --- Middleware Pipeline ---

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");

app.MapControllers();

// Auto-migrate database on startup (development convenience)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BubbleSplashDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
