using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShengenApp;

// En reise lagres med landkode og datoer. Både innreise- og utreisedagen teller.
public sealed record Trip(Guid Id, string CountryCode, DateOnly Start, DateOnly End, string? Notes)
{
    [JsonIgnore]
    public Country Country => Countries.Find(CountryCode);

    [JsonIgnore]
    public int Days => End.DayNumber - Start.DayNumber + 1;
}

public sealed record Country(string Code, string Name, bool IsSchengen, bool IsEu)
{
    public string Label => (IsSchengen, IsEu) switch
    {
        (true, true) => "Schengen + EU",
        (true, false) => "Schengen, not EU",
        (false, true) => "EU, not Schengen",
        _ => "Outside Schengen + EU",
    };

    public string Description => (IsSchengen, IsEu) switch
    {
        (true, true) => $"{Name} is in both Schengen and the EU. Counts toward the 90/180 rule.",
        (true, false) => $"{Name} is in Schengen, but not the EU. Counts toward the 90/180 rule.",
        (false, true) => $"{Name} is in the EU, but not Schengen. Does not count toward the 90/180 rule.",
        _ => $"{Name} is in neither Schengen nor the EU. Does not count toward the 90/180 rule.",
    };
}

public static class Countries
{
    public static readonly Country Other = new("--", "Other country", false, false);

    public static readonly List<Country> All = new List<Country>
    {
        // EU-land som også er med i Schengen
        new("AT", "Austria", true, true),
        new("BE", "Belgium", true, true),
        new("BG", "Bulgaria", true, true),
        new("HR", "Croatia", true, true),
        new("CZ", "Czechia", true, true),
        new("DK", "Denmark", true, true),
        new("EE", "Estonia", true, true),
        new("FI", "Finland", true, true),
        new("FR", "France", true, true),
        new("DE", "Germany", true, true),
        new("GR", "Greece", true, true),
        new("HU", "Hungary", true, true),
        new("IT", "Italy", true, true),
        new("LV", "Latvia", true, true),
        new("LT", "Lithuania", true, true),
        new("LU", "Luxembourg", true, true),
        new("MT", "Malta", true, true),
        new("NL", "Netherlands", true, true),
        new("PL", "Poland", true, true),
        new("PT", "Portugal", true, true),
        new("RO", "Romania", true, true),
        new("SK", "Slovakia", true, true),
        new("SI", "Slovenia", true, true),
        new("ES", "Spain", true, true),
        new("SE", "Sweden", true, true),

        // EU-land utenfor Schengen
        new("IE", "Ireland", false, true),
        new("CY", "Cyprus", false, true),

        // Schengen-land utenfor EU
        new("IS", "Iceland", true, false),
        new("LI", "Liechtenstein", true, false),
        new("NO", "Norway", true, false),
        new("CH", "Switzerland", true, false),

        // Vanlige reisemål i Europa som står utenfor begge
        new("GB", "United Kingdom", false, false),
        new("AL", "Albania", false, false),
        new("BA", "Bosnia and Herzegovina", false, false),
        new("GE", "Georgia", false, false),
        new("MD", "Moldova", false, false),
        new("ME", "Montenegro", false, false),
        new("MK", "North Macedonia", false, false),
        new("RS", "Serbia", false, false),
        new("TR", "Turkey", false, false),
        new("UA", "Ukraine", false, false),
    }.OrderBy(country => country.Name).ToList();

    public static Country Find(string? code) =>
        All.FirstOrDefault(country => country.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) ?? Other;
}

public sealed record TripCheck(Trip Trip, int MaxUsed, DateOnly? FirstDayOverLimit);

// Alle dager brukt i Schengen. Et sett gjør at overlappende reiser bare telles én gang.
public sealed class SchengenDays
{
    public const int AllowedDays = 90;
    public const int WindowDays = 180;

    private readonly HashSet<DateOnly> _days = [];

    public SchengenDays(IEnumerable<Trip> trips)
    {
        foreach (var trip in trips)
        {
            Add(trip);
        }
    }

    public void Add(Trip trip)
    {
        if (!trip.Country.IsSchengen)
        {
            return;
        }

        for (var day = trip.Start; day <= trip.End; day = day.AddDays(1))
        {
            _days.Add(day);
        }
    }

    public bool Contains(DateOnly day) => _days.Contains(day);

    // Vinduet er dagen selv pluss de 179 dagene før.
    public static DateOnly WindowStart(DateOnly day) => day.AddDays(-(WindowDays - 1));

    public int UsedOn(DateOnly day) => CountInWindow(_days, day);

    // Sjekker hver dag på reisen mot sitt eget rullerende 180-dagersvindu.
    public TripCheck Check(Trip trip)
    {
        if (!trip.Country.IsSchengen)
        {
            return new TripCheck(trip, 0, null);
        }

        var maxUsed = 0;
        DateOnly? firstDayOverLimit = null;
        for (var day = trip.Start; day <= trip.End; day = day.AddDays(1))
        {
            var used = UsedOn(day);
            maxUsed = Math.Max(maxUsed, used);
            if (used > AllowedDays && firstDayOverLimit is null)
            {
                firstDayOverLimit = day;
            }
        }

        return new TripCheck(trip, maxUsed, firstDayOverLimit);
    }

    // Siste dag man kan bli hvis man reiser inn på en gitt dag og blir sammenhengende.
    public DateOnly? LastDayIfEntering(DateOnly entry)
    {
        var simulated = new HashSet<DateOnly>(_days);
        DateOnly? lastDay = null;
        for (var day = entry; day <= entry.AddDays(AllowedDays); day = day.AddDays(1))
        {
            simulated.Add(day);
            if (CountInWindow(simulated, day) > AllowedDays)
            {
                break;
            }

            lastDay = day;
        }

        return lastDay;
    }

    // Neste dag en brukt dag faller ut av vinduet og blir ledig igjen.
    public DateOnly? NextDayReturns(DateOnly today) =>
        _days.Where(day => day >= WindowStart(today) && day <= today)
            .Order()
            .Select(day => (DateOnly?)day.AddDays(WindowDays))
            .FirstOrDefault();

    private static int CountInWindow(HashSet<DateOnly> days, DateOnly day)
    {
        var count = 0;
        for (var current = WindowStart(day); current <= day; current = current.AddDays(1))
        {
            if (days.Contains(current))
            {
                count++;
            }
        }

        return count;
    }
}

// Reisene lagres i en enkel JSON-fil i prosjektmappen.
public sealed class TripStore(string path)
{
    private readonly Lock _fileLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<Trip> Load()
    {
        lock (_fileLock)
        {
            var trips = File.Exists(path)
                ? JsonSerializer.Deserialize<List<Trip>>(File.ReadAllText(path)) ?? []
                : [];
            return trips.OrderBy(trip => trip.Start).ToList();
        }
    }

    public void Add(Trip trip) => Change(trips => trips.Add(trip));

    public void Delete(Guid id) => Change(trips => trips.RemoveAll(trip => trip.Id == id));

    private void Change(Action<List<Trip>> change)
    {
        lock (_fileLock)
        {
            var trips = Load();
            change(trips);
            File.WriteAllText(path, JsonSerializer.Serialize(trips, JsonOptions));
        }
    }
}
