using System.Globalization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShengenApp.Pages;

public sealed record TimelineDay(bool InSchengen, bool IsMonthStart);

public sealed record TimelineLabel(double Percent, string Text);

public sealed class IndexModel(TripStore store) : PageModel
{
    public DateOnly Today { get; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly WindowStart => SchengenDays.WindowStart(Today);
    public int Used { get; private set; }
    public int Left => Math.Max(0, SchengenDays.AllowedDays - Used);
    public DateOnly? StayUntil { get; private set; }
    public DateOnly? NextReturn { get; private set; }
    public TripCheck? PastOverstay { get; private set; }
    public TripCheck? PlannedOverstay { get; private set; }
    public List<TripCheck> Upcoming { get; private set; } = [];
    public List<TimelineDay> Timeline { get; } = [];
    public List<TimelineLabel> TripLabels { get; } = [];
    public List<TimelineLabel> MonthLabels { get; } = [];

    public string Status => PastOverstay is not null ? "Overstayed"
        : PlannedOverstay is not null ? "Check your plans"
        : Used >= SchengenDays.AllowedDays ? "No days left"
        : "You're fine";

    public void OnGet()
    {
        var trips = store.Load();
        var days = new SchengenDays(trips);
        var checks = trips.Select(days.Check).ToList();

        Used = days.UsedOn(Today);
        StayUntil = days.LastDayIfEntering(Today);
        NextReturn = days.NextDayReturns(Today);
        PastOverstay = checks.FirstOrDefault(check => check.FirstDayOverLimit <= Today);
        PlannedOverstay = checks.FirstOrDefault(check => check.FirstDayOverLimit > Today);
        Upcoming = checks.Where(check => check.Trip.End >= Today).ToList();

        BuildTimeline(trips, days);
    }

    // Én stolpe per dag i 180-dagersvinduet, med etiketter for reiser og måneder.
    private void BuildTimeline(List<Trip> trips, SchengenDays days)
    {
        double PercentOf(DateOnly day) =>
            (day.DayNumber - WindowStart.DayNumber) * 100.0 / SchengenDays.WindowDays;

        for (var day = WindowStart; day <= Today; day = day.AddDays(1))
        {
            Timeline.Add(new TimelineDay(days.Contains(day), day.Day == 1));
            if (day.Day == 1)
            {
                MonthLabels.Add(new TimelineLabel(PercentOf(day), day.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant()));
            }
        }

        foreach (var trip in trips.Where(trip => trip.Country.IsSchengen && trip.End >= WindowStart && trip.Start <= Today))
        {
            var start = trip.Start < WindowStart ? WindowStart : trip.Start;
            var end = trip.End > Today ? Today : trip.End;
            TripLabels.Add(new TimelineLabel(PercentOf(start), $"{trip.Country.Code} {end.DayNumber - start.DayNumber + 1}D"));
        }
    }
}
