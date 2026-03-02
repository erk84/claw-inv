using ClawInv.Web.Data;
using ClawInv.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

var dbPath = builder.Configuration["ClawInv:DbPath"] ?? "data/clawinv.db";
Directory.CreateDirectory(Path.GetDirectoryName(dbPath) ?? ".");

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddScoped<UniverseRegenerator>();
builder.Services.AddHostedService<ScheduledJobsService>();

var app = builder.Build();

// Ensure schema exists (simple start; can be replaced with migrations).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

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
