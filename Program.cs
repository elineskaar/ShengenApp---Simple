using System.Globalization;
using ShengenApp;

// Engelske månedsnavn og punktum som desimaltegn i SVG og CSS, uansett maskinens språk.
CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-GB");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-GB");

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
builder.Services.AddSingleton(new TripStore(Path.Combine(builder.Environment.ContentRootPath, "trips.json")));

var app = builder.Build();

// wwwroot inneholder CSS-filen, Razor Pages lager HTML-en med C#.
app.UseStaticFiles();
app.MapRazorPages();

app.Run();
