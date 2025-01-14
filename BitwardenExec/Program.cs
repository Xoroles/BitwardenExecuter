using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace BitwardenExec;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddHostedService(_ => new TeamViewerUrlUpdaterService(TimeSpan.FromMinutes(30)));
        builder.Services.AddHostedService(_ => new RdpUpdaterService(TimeSpan.FromMinutes(30)));
        builder.Services.AddHostedService(_ => new SyncService(TimeSpan.FromMinutes(5)));
        var app = builder.Build();

        var bwSessionToken = Environment.GetEnvironmentVariable("BW_SESSION");
        // Route: GET /start-teamviewer?id=123456789&pw=MeinPasswort
        app.MapGet("/start-teamviewer", async (HttpContext context) =>
        {
            var query = context.Request.Query;
            var item = query["item"].ToString();
            if (string.IsNullOrEmpty(item)) return Results.BadRequest("Bitte ?item=<VaultItemId> angeben.");

            // 2) Per CLI den Eintrag holen: bw get item <item> --session <token>
            var (ok, output, errorMsg) =
                await BitwardenCliWrapper.RunBitwardenCliAsync($"get item \"{item}\" --session {bwSessionToken}");
            if (!ok) return Results.BadRequest($"Fehler bei bw get item: {errorMsg}");

            // 3) JSON parsen: username = data["login"]["username"], password = data["login"]["password"]
            var jsonData = JObject.Parse(output);
            var username = jsonData["login"]?["username"]?.ToString() ?? "";
            var password = jsonData["login"]?["password"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(username))
                return Results.BadRequest("Im Bitwarden-Eintrag ist kein 'username' hinterlegt.");

            // Hier TeamViewer starten
            // Beispiel: "C:\Program Files\TeamViewer\TeamViewer.exe" -i 123456789 --Password "1234"
            var teamViewerPath = @"C:\Program Files (x86)\TeamViewer\TeamViewer.exe";
            if (!File.Exists(teamViewerPath))
            {
                teamViewerPath = @"C:\Program Files\TeamViewer\TeamViewer.exe";
            }

            if (!File.Exists(teamViewerPath))
            {
                return Results.BadRequest($"TeamViewer Installation nicht unter Standard Pfad gefunden");
            }
            // Ggf. Pfad und Parameter anpassen
            var startInfo = new ProcessStartInfo
            {
                FileName = teamViewerPath,
                Arguments = $"-i {username} --Password \"{password}\"",
                UseShellExecute = false
            };
            Process.Start(startInfo);

            return Results.Ok($"TeamViewer mit ID {username} wird gestartet...");
        });

        app.MapGet("/start-rdp", async (HttpContext context) =>
        {
            var item = context.Request.Query["item"].ToString();
            if (string.IsNullOrEmpty(item))
            {
                return Results.BadRequest("Bitte ?item=<NameOderID> angeben.");
            }

            // 1) Bitwarden-CLI: bw get item ...
            var (ok, output, error) =
                await BitwardenCliWrapper.RunBitwardenCliAsync($"get item \"{item}\" --session {bwSessionToken}");
            if (!ok)
            {
                return Results.BadRequest($"Fehler bei bw get item: {error}");
            }

            // 2) JSON parsen
            var data = JObject.Parse(output);

            var username = data["login"]?["username"]?.ToString() ?? "";
            var password = data["login"]?["password"]?.ToString() ?? "";

            var fieldsArray = data["fields"] as JArray;
            var host = "";
            if (fieldsArray != null)
            {
                // Suche im Array nach einem Eintrag mit "name": "host"
                var hostField = fieldsArray.FirstOrDefault(f => string.Equals(f["name"]?.ToString(), "host", StringComparison.InvariantCultureIgnoreCase));
                if (hostField != null)
                {
                    host = hostField["value"]?.ToString() ?? "";
                }
            }

            if (string.IsNullOrEmpty(host))
            {
                return Results.BadRequest("Kein RDP-Host im Item hinterlegt (z.B. in notes).");
            }

            var rdpLauncher = new RdpLauncher();
            _ = rdpLauncher.StartRdpSessionAsync(host, username, password);

            return Results.Ok($"RDP nach {host} gestartet (User: {username}).");
        });


        await app.RunAsync("http://localhost:5000");
    }
}