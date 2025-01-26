using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;

namespace BitwardenExec
{
    internal class WebApiHostedService : BackgroundService
    {
        private readonly string[] _args;
        private IHost? _webHost;

        public WebApiHostedService(string[] args)
        {
            this._args = args;
        }

        //public override async Task StopAsync(CancellationToken stoppingToken)
        //{
        //    if (this._webHost != null)
        //    {
        //        try
        //        {
        //            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        //            cts.CancelAfter(TimeSpan.FromSeconds(10)); // 10 Sekunden Timeout
        //            await _webHost.StopAsync(cts.Token);
        //        }
        //        catch (OperationCanceledException)
        //        {
        //            // Logge den Timeout-Fehler oder führe andere Aktionen aus
        //            Console.WriteLine("StopAsync wurde durch einen Timeout abgebrochen.");
        //        }
        //        finally
        //        {
        //            _webHost.Dispose();
        //        }
        //        this._webHost.Dispose();
        //    }

        //    await base.StopAsync(stoppingToken);
        //}

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(this._args);
            builder.Services.AddHostedService(_ => new TeamViewerUrlUpdaterService(TimeSpan.FromMinutes(30)));
            builder.Services.AddHostedService(_ => new RdpUpdaterService(TimeSpan.FromMinutes(30)));
            builder.Services.AddHostedService(_ => new SyncService(TimeSpan.FromMinutes(5)));
            WebApplication app = builder.Build();

            string? bwSessionToken = Environment.GetEnvironmentVariable("BW_SESSION");

            // Route: GET /start-teamviewer?id=123456789&pw=MeinPasswort
            app.MapGet("/start-teamviewer", async (HttpContext context) =>
            {
                IQueryCollection query = context.Request.Query;
                var item = query["item"].ToString();

                if (string.IsNullOrEmpty(item))
                {
                    return Results.BadRequest("Bitte ?item=<VaultItemId> angeben.");
                }

                // 2) Per CLI den Eintrag holen: bw get item <item> --session <token>
                (bool ok, string output, string errorMsg) = await BitwardenCliWrapper.RunBitwardenCliAsync($"get item \"{item}\" --session {bwSessionToken}");

                if (!ok)
                {
                    return Results.BadRequest($"Fehler bei bw get item: {errorMsg}");
                }

                // 3) JSON parsen: username = data["login"]["username"], password = data["login"]["password"]
                JObject jsonData = JObject.Parse(output);
                string username = jsonData["login"]?["username"]?.ToString() ?? "";
                string password = jsonData["login"]?["password"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(username))
                {
                    return Results.BadRequest("Im Bitwarden-Eintrag ist kein 'username' hinterlegt.");
                }

                // Hier TeamViewer starten
                // Beispiel: "C:\Program Files\TeamViewer\TeamViewer.exe" -i 123456789 --Password "1234"
                var teamViewerPath = @"C:\Program Files (x86)\TeamViewer\TeamViewer.exe";

                if (!File.Exists(teamViewerPath))
                {
                    teamViewerPath = @"C:\Program Files\TeamViewer\TeamViewer.exe";
                }

                if (!File.Exists(teamViewerPath))
                {
                    return Results.BadRequest("TeamViewer Installation nicht unter Standard Pfad gefunden");
                }

                // Ggf. Pfad und Parameter anpassen
                var startInfo = new ProcessStartInfo
                {
                    FileName = teamViewerPath,
                    Arguments = $"-i {username} --Password \"{password}\"",
                    UseShellExecute = false,
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
                (bool ok, string output, string error) = await BitwardenCliWrapper.RunBitwardenCliAsync($"get item \"{item}\" --session {bwSessionToken}");

                if (!ok)
                {
                    return Results.BadRequest($"Fehler bei bw get item: {error}");
                }

                // 2) JSON parsen
                JObject data = JObject.Parse(output);

                string username = data["login"]?["username"]?.ToString() ?? "";
                string password = data["login"]?["password"]?.ToString() ?? "";

                var fieldsArray = data["fields"] as JArray;
                var host = "";

                if (fieldsArray != null)
                {
                    // Suche im Array nach einem Eintrag mit "name": "host"
                    JToken? hostField = fieldsArray.FirstOrDefault(f => string.Equals(f["name"]?.ToString(), "host", StringComparison.InvariantCultureIgnoreCase));

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
            app.Urls.Add("http://localhost:5000");
            this._webHost = app;

            await app.StartAsync(stoppingToken);
        }
    }
}