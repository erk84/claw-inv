using ClawInv.Web.Data;
using ClawInv.Web.Data.Entities;
using ClawInv.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace ClawInv.Web.Pages;

public sealed class UniverseModel(AppDbContext db, UniverseRegenerator regenerator) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string LastRegeneratedText { get; set; } = "-";

    public int UniverseFundCount { get; set; }

    public sealed class InputModel
    {
        public int RatingLimit { get; set; } = 3;
        public double TotalFeeLimit { get; set; } = 2.0;
        public int RiskLimit { get; set; } = 0;
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        var s = await db.UniverseSettings.SingleOrDefaultAsync(x => x.Key == "default", ct) ?? new UniverseSettings();
        Input = new InputModel
        {
            RatingLimit = s.RatingLimit,
            TotalFeeLimit = s.TotalFeeLimit,
            RiskLimit = s.RiskLimit,
        };
        LastRegeneratedText = s.LastRegeneratedAtUtc?.ToString("u") ?? "-";
        UniverseFundCount = s.UniverseFundCount;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var s = await db.UniverseSettings.SingleOrDefaultAsync(x => x.Key == "default", ct);
        if (s is null)
        {
            s = new UniverseSettings { Key = "default" };
            db.UniverseSettings.Add(s);
        }

        s.RatingLimit = Input.RatingLimit;
        s.TotalFeeLimit = Input.TotalFeeLimit;
        s.RiskLimit = Input.RiskLimit;
        await db.SaveChangesAsync(ct);

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRegenerateAsync(CancellationToken ct)
    {
        await OnPostAsync(ct);
        await regenerator.RegenerateAsync(ct);
        return RedirectToPage();
    }
}
