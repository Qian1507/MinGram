// Program.cs — MinGram API
// ASP.NET Core Minimal API: endpoints definieras direkt här, inga controllers.
//
// Starta lokalt:  dotnet run
// Swagger UI:     https://localhost:{port}/swagger
//
// v35 — Azure-konfiguration (görs i portalen, inte i koden):
// 1. CORS: App Service → API → CORS → lägg till din frontend-URL
// 2. Easy Auth: App Service → Authentication → Add identity provider → Microsoft
//    Välj din Entra ID-tenant. Alla anrop kräver nu inloggning.
// 3. App-roller i Entra ID: gå till App registrations → din app → App roles
//    Skapa rollerna Betraktare, Fotograf, Admin.
//    Tilldela dem till dina Entra ID-användare under Enterprise applications.
//
// Bilder lagras som URL:er — ladda upp till Azure Blob Storage och skicka URL:en hit.

using Azure.Storage.Blobs;  
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs.Models;

var builder = WebApplication.CreateBuilder(args);

// Hämta namnet på Storage Account från konfigurationen.
// I Azure sätts detta som en App Setting, t.ex. Storage__AccountName.
var storageAccountName =
    builder.Configuration["Storage:AccountName"]
    ?? throw new InvalidOperationException(
        "Storage:AccountName saknas");

// Hämta containerns namn.
// Om inget värde finns används "bilder" som standard.
var containerName =
    builder.Configuration["Storage:ContainerName"]
    ?? "bilder";

// Skapa en BlobServiceClient med DefaultAzureCredential.
// I Azure använder detta App Service-resursens Managed Identity,
// vilket innebär att vi inte behöver lagra någon Storage Account-nyckel
// eller connection string i applikationen.
var blobServiceClient =
    new BlobServiceClient(
        new Uri($"https://{storageAccountName}.blob.core.windows.net"),
        new DefaultAzureCredential());

// Hämta klienten för den container där bilderna lagras.
// App Service har rollen "Storage Blob Data Contributor"
// och får därför läsa, skapa och radera blobbar via Azure RBAC.
var blobContainerClient =
    blobServiceClient.GetBlobContainerClient(containerName);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS — hanteras primärt i Azure Portal: App Service → API → CORS
// Lägg till din frontend-URL där, så slipper du ändra och redeploya koden.
// Den här koden hanterar CORS lokalt under utveckling.
builder.Services.AddCors(options =>
{
    options.AddPolicy("MinGramPolicy", policy =>
    {
        var origins = builder.Configuration
                             .GetSection("AllowedOrigins")
                             .Get<string[]>() ?? [];
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("MinGramPolicy");

// -------------------------------------------------------
// In-memory datastore med seed-data
// Datan nollställs vid omstart — en riktig app lagrar bilder i Blob Storage
// -------------------------------------------------------

var bilder = new List<Bild>
{
    new(1, "demo.jpg", "Demobild — ersätt med din egen", ["demo", "placeholder"],
        "https://placehold.co/400x300?text=MinGram")
};
var nastaBildId = 2;

// ======================================================
// Bilder
// ======================================================

// Alla roller får se bilder
app.MapGet("/bilder", () => bilder)
   .WithName("HamtaBilder")
   .WithSummary("Hämta alla bilder — alla roller");

app.MapGet("/bilder/{id:int}", (int id) =>
{
    var b = bilder.FirstOrDefault(b => b.Id == id);
    return b is not null ? Results.Ok(b) : Results.NotFound();
})
.WithName("HamtaBild")
.WithSummary("Hämta en specifik bild — alla roller");

// Fotograf och Admin får ladda upp bilder
// Skicka URL:en till bilden — lagra filen i Azure Blob Storage och använd den URL:en här
//Bildfilen lagras i Azure Blob Storage och metadata sparas i minnet.

app.MapPost("/bilder", async (
    HttpRequest req,
    [FromForm] BildUploadRequest input) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf"))
        return Results.StatusCode(403);

    if (input.File is null || input.File.Length == 0)
        return Results.BadRequest("Ingen bild valdes.");

    var taggar = input.Taggar
        .Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(t => t.Trim())
        .ToList();

    var blobName =
        $"{Guid.NewGuid()}-{Path.GetFileName(input.File.FileName)}";

    var blobClient =
        blobContainerClient.GetBlobClient(blobName);

    await using var stream = input.File.OpenReadStream();

    await blobClient.UploadAsync(
        stream,
        overwrite: true);

    //var sasUri = blobClient.GenerateSasUri(
    //    BlobSasPermissions.Read,
    //    DateTimeOffset.UtcNow.AddHours(1));

    // Skapa en User Delegation Key med App Service-resursens Managed Identity.
    // Detta gör att SAS-token kan skapas utan Storage Account Key.
    var startsOn = DateTimeOffset.UtcNow.AddMinutes(-5);
    var expiresOn = DateTimeOffset.UtcNow.AddHours(1);

    var delegationOptions =
     new BlobGetUserDelegationKeyOptions(expiresOn)
     {
         StartsOn = startsOn
     };

    var userDelegationKeyResponse =
        await blobServiceClient.GetUserDelegationKeyAsync(
            delegationOptions);

    var userDelegationKey = userDelegationKeyResponse.Value;

    // Skapa en tidsbegränsad SAS för den uppladdade bilden.
    var sasBuilder = new BlobSasBuilder
    {
        BlobContainerName = containerName,
        BlobName = blobName,
        Resource = "b",
        StartsOn = startsOn,
        ExpiresOn = expiresOn
    };

    sasBuilder.SetPermissions(BlobSasPermissions.Read);

    var sasUri = new UriBuilder(blobClient.Uri)
    {
        Query = sasBuilder
            .ToSasQueryParameters(
                userDelegationKey,
                storageAccountName)
            .ToString()
    }.Uri;

    var bild = new Bild(
        nastaBildId++,
        blobName,
        input.Caption,
        taggar,
        sasUri.ToString());

    bilder.Add(bild);

    return Results.Created(
        $"/bilder/{bild.Id}",
        bild);
})
.WithName("LaddaUppBild")
.WithSummary("Lägg till bild — kräver Fotograf eller Admin")
.DisableAntiforgery(); 

// Fotograf och Admin får uppdatera caption och taggar
app.MapPut("/bilder/{id:int}", (int id, BildUpdate update, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Fotograf")) return Results.StatusCode(403);
    var index = bilder.FindIndex(b => b.Id == id);
    if (index < 0) return Results.NotFound();
    bilder[index] = bilder[index] with
    {
        Caption = update.Caption ?? bilder[index].Caption,
        Taggar  = update.Taggar  ?? bilder[index].Taggar
    };
    return Results.Ok(bilder[index]);
})
.WithName("UppdateraBild")
.WithSummary("Uppdatera bild — kräver Fotograf eller Admin");

// Bara Admin får ta bort bilder — testa med Postman som Betraktare för att se 403
app.MapDelete("/bilder/{id:int}", async (int id, HttpRequest req) =>
{
    if (!HarBehorighet(HamtaRoll(req), "Admin"))
        return Results.StatusCode(403);

    var bild = bilder.FirstOrDefault(b => b.Id == id);

    if (bild is null)
        return Results.NotFound();

    var blobClient =
        blobContainerClient.GetBlobClient(bild.Namn);

    await blobClient.DeleteIfExistsAsync();

    bilder.Remove(bild);

    return Results.NoContent();
})
.WithName("RaderaBild")
.WithSummary("Radera bild — kräver Admin");

app.Run();

// ======================================================
// Rollkontroll
// ======================================================

// Läser rollen ur Easy Auth-headern som Azure injicerar efter inloggning.
// Lokalt (utan Easy Auth): returnerar "Admin" så Swagger fungerar utan inloggning.
string HamtaRoll(HttpRequest request)
{
    var header = request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault();
    if (string.IsNullOrEmpty(header)) return "Admin"; // lokal dev

    try
    {
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(header));
        using var doc = JsonDocument.Parse(json);
        foreach (var claim in doc.RootElement.GetProperty("claims").EnumerateArray())
        {
            if (claim.GetProperty("typ").GetString() == "roles")
                return claim.GetProperty("val").GetString() ?? "Betraktare";
        }
    }
    catch { }

    return "Betraktare"; // okänd roll → minsta behörighet
}

// Kontrollerar om en roll har tillräcklig behörighet.
// Hierarki: Betraktare < Fotograf < Admin
bool HarBehorighet(string roll, string kravRoll) => (roll, kravRoll) switch
{
    (_, "Betraktare")          => true,
    ("Fotograf" or "Admin", "Fotograf") => true,
    ("Admin", "Admin")         => true,
    _                          => false
};

// ======================================================
// Datamodeller
// ======================================================

record Bild(int Id, string Namn, string Caption, List<string> Taggar, string Url);

record NyBild(string Namn, string Caption, List<string>? Taggar, string Url);
record BildUpdate(string? Caption, List<string>? Taggar);

public class BildUploadRequest
{
    public IFormFile File { get; set; } = default!;
    public string Caption { get; set; } = "";
    public string Taggar { get; set; } = "";
}
