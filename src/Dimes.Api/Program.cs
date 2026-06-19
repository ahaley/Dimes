using System.Text.Json.Serialization;
using Dimes.Api;
using Dimes.Api.Services;
using Dimes.Infrastructure;
using Dimes.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SQLite by default — "one command up". Override ConnectionStrings:Dimes for Postgres later.
var connectionString = builder.Configuration.GetConnectionString("Dimes")
    ?? "Data Source=dimes.db";

builder.Services.AddDimesPersistence(connectionString);
builder.Services.AddDimesProviders();

// Application services (capture → inbox → promote → lifecycle, commentary, SCM context).
builder.Services.AddScoped<MembershipResolver>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<ObservationService>();
builder.Services.AddScoped<ChangeRequestService>();
builder.Services.AddScoped<CommentaryService>();
builder.Services.AddScoped<ScmService>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services
    .AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

// Apply pending migrations on startup so a fresh self-host install needs no manual DB step.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DimesDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
