using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShengenApp.Pages;

public sealed class TripsModel(TripStore store) : PageModel
{
    private static readonly string[] DateFormats = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"];

    // Skjemaet for å legge til en reise
    [BindProperty]
    public string CountryCode { get; set; } = "";

    [BindProperty]
    public string? Arrival { get; set; }

    [BindProperty]
    public string? Departure { get; set; }

    [BindProperty]
    public string? Notes { get; set; }

    // Første måned som vises i kalenderen.
    [BindProperty]
    public DateOnly? Month { get; set; }

    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? ArrivalDate => ParseDate(Arrival);
    public DateOnly? DepartureDate => ParseDate(Departure);
    public DateOnly FirstMonth => Month ?? FirstOfMonth(ArrivalDate ?? Today);
    public int? TripDays => ArrivalDate is DateOnly a && DepartureDate is DateOnly d && d >= a ? d.DayNumber - a.DayNumber + 1 : null;
    public HashSet<DateOnly> BookedDays { get; } = [];
    public string? Preview { get; private set; }
    public bool PreviewIsBad { get; private set; }
    public string? ErrorMessage { get; private set; }

    // Reiselistene
    public List<TripCheck> Upcoming { get; private set; } = [];
    public List<TripCheck> Previous { get; private set; } = [];

    public void OnGet() => Prepare();

    // Kalenderen er vanlige knapper: hvert klikk sender skjemaet og C# regner ut på nytt.
    public IActionResult OnPostUpdate(string? pick, int shift, bool clear)
    {
        if (clear)
        {
            Arrival = null;
            Departure = null;
        }

        if (ParseDate(pick) is DateOnly day)
        {
            if (ArrivalDate is not DateOnly arrival || DepartureDate is not null || day < arrival)
            {
                Arrival = FormatDate(day);
                Departure = null;
            }
            else
            {
                Departure = FormatDate(day);
            }
        }

        Month = FirstMonth.AddMonths(shift);

        // Uten dette ville skjemafeltene vist de innsendte verdiene i stedet for de nye.
        ModelState.Clear();
        Prepare();
        return Page();
    }

    public IActionResult OnPostSave()
    {
        if (string.IsNullOrEmpty(CountryCode))
        {
            ErrorMessage = "Choose a country.";
        }
        else if (ArrivalDate is null || DepartureDate is null)
        {
            ErrorMessage = "Pick an arrival and a departure day (dd.mm.yyyy).";
        }
        else if (DepartureDate < ArrivalDate)
        {
            ErrorMessage = "Departure can't be before arrival.";
        }

        if (ErrorMessage is not null)
        {
            Prepare();
            return Page();
        }

        var country = Countries.Find(CountryCode);
        var notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        store.Add(new Trip(Guid.NewGuid(), country.Code, ArrivalDate!.Value, DepartureDate!.Value, notes));
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(Guid id)
    {
        store.Delete(id);
        return RedirectToPage();
    }

    private void Prepare()
    {
        var trips = store.Load();
        var days = new SchengenDays(trips);
        var checks = trips.Select(days.Check).ToList();

        Upcoming = checks.Where(check => check.Trip.End >= Today).ToList();
        Previous = checks.Where(check => check.Trip.End < Today).OrderByDescending(check => check.Trip.Start).ToList();

        foreach (var trip in trips)
        {
            for (var day = trip.Start; day <= trip.End; day = day.AddDays(1))
            {
                BookedDays.Add(day);
            }
        }

        BuildPreview(trips);
    }

    // Linjen under kalenderen: sier om landet er i Schengen/EU, og hvor mange dager du har igjen.
    private void BuildPreview(List<Trip> trips)
    {
        if (string.IsNullOrEmpty(CountryCode))
        {
            return;
        }

        var country = Countries.Find(CountryCode);
        if (ArrivalDate is not DateOnly arrival || DepartureDate is not DateOnly departure || departure < arrival)
        {
            Preview = country.Description;
            return;
        }

        var candidate = new Trip(Guid.Empty, country.Code, arrival, departure, null);
        if (!country.IsSchengen)
        {
            Preview = $"{country.Name} isn't in Schengen ({country.Label}), so these {candidate.Days} days don't count.";
            return;
        }

        var days = new SchengenDays(trips);
        days.Add(candidate);
        if (days.Check(candidate).FirstDayOverLimit is DateOnly overLimit)
        {
            Preview = $"This trip takes you over 90 days on {FormatDate(overLimit)}.";
            PreviewIsBad = true;
        }
        else
        {
            Preview = $"You'd have {SchengenDays.AllowedDays - days.UsedOn(departure)} of 90 days left on {FormatDate(departure)}.";
        }
    }

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string FormatDate(DateOnly day) => day.ToString("dd.MM.yyyy");

    private static DateOnly FirstOfMonth(DateOnly day) => new(day.Year, day.Month, 1);
}
