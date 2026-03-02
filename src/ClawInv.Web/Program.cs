using ClawInv.Web.Data;
using ClawInv.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

var dbPath = builder.Configuration["ClawInv:DbPath"] ?? "data/clawinv.db";
Directory.CreateDirectory(Path.GetDirectoryName(dbPath) ?? ".");

var connString = $"Data Source={dbPath}";

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(connString));

builder.Services.AddSingleton<UniverseRegenerator>();
builder.Services.AddSingleton<NavService>();
builder.Services.AddSingleton<NavLookupService>();
builder.Services.AddScoped<RecommendationEngine>();
builder.Services.AddHostedService<ScheduledJobsService>();

var app = builder.Build();

// Ensure schema exists (simple start; can be replaced with migrations).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Patch older SQLite DBs created before certain columns existed.
SchemaUpgrader.Upgrade(connString);

// Seed defaults (universe settings + strategy configs).
await SeedData.EnsureSeededAsync(app.Services, CancellationToken.None);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
