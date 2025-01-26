using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;

namespace BitwardenExec;

internal class RdpUpdaterService : BackgroundService
{
    // Wenn die URL fehlt, fügen wir z.B. "teamviewerapi://control?item=<ItemID>" hinzu
    private const string URL_SCHEME_PREFIX = "http://127.0.0.1:5000/start-rdp?item=";
    private readonly string? _bwSessionToken;
    private readonly TimeSpan _interval;

    private readonly Regex _rdpNameRegex = new("(RDP|Remote Desktop)", RegexOptions.IgnoreCase);

    public RdpUpdaterService(TimeSpan interval)
    {
        _interval = interval;
        _bwSessionToken = Environment.GetEnvironmentVariable("BW_SESSION");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Solange nicht abgebrochen
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1) Alle Items holen
                var (ok, output, errorMsg) =
                  await BitwardenCliWrapper.RunBitwardenCliAsync($"list items --session {_bwSessionToken}");
                if (!ok)
                {
                    Console.WriteLine($"[RdpUpdaterService] Fehler bei bw list items: {errorMsg}");
                }
                else
                {
                    await ProcessItemsAsync(output);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RdpUpdaterService] Exception: {ex}");
            }

            // 2) Warten bis zum nächsten Durchlauf
            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessItemsAsync(string itemsJson)
    {
        // JSON parsen (Liste von Items)
        var items = JArray.Parse(itemsJson);

        foreach (var itemObj in items)
        {
            // Nur Logins interessant?
            if (itemObj["login"] == null)
            {
                continue;

            }

            var itemId = itemObj["id"]?.ToString() ?? "";
            var name = itemObj["name"]?.ToString() ?? "";

            // Prüfen, ob name "TV" oder "TeamViewer" enthält
            if (!_rdpNameRegex.IsMatch(name))
            {
                continue;

            }

            // 3) Custom Field "host" vorhanden?
            //    In Bitwarden-CLI-JSON liegt das unter itemNode["fields"] als Array
            //    (oder "fields": null falls keine custom fields).
            bool hasHostField = false;
            var fieldsArr = itemObj["fields"] as JArray;
            if (fieldsArr != null)
            {
                // Suche im Array nach einem Eintrag mit "name": "host"
                var hostField = fieldsArr.FirstOrDefault(f => string.Equals(f["name"]?.ToString(), "host", StringComparison.InvariantCultureIgnoreCase));
                if (hostField != null)
                {
                    var hostValue = hostField["value"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(hostValue))
                    {
                        hasHostField = true;
                    }
                }
            }

            if (!hasHostField)
            {
                continue;
            }

            // Schauen, ob URL schon drinsteht
            var uris = itemObj["login"]?["uris"] as JArray;
            if (uris == null)
            {
                // Falls null, legen wir ne leere Array an
                uris = new JArray();
                itemObj["login"]!["uris"] = uris;
            }

            // Zieldatei
            var desiredUrl = $"{URL_SCHEME_PREFIX}{itemId}";
            var alreadyThere = uris.Any(u => (string?)u["uri"] == desiredUrl);
            if (alreadyThere)
            // Nichts tun
            {
                continue;
            }

            Console.WriteLine($"Füge URL '{desiredUrl}' zu Item '{name}' ({itemId}) hinzu...");

            // URL anhängen
            var newUriEntry = new JObject { ["uri"] = desiredUrl };
            uris.Add(newUriEntry);

            // Item zurückschreiben
            var updatedJson = itemObj.ToString();


            var (editOk, _, editErr) =
                await BitwardenCliWrapper.RunBitwardenCliAsync(
                    $"edit item {itemId} --session {_bwSessionToken}", Convert.ToBase64String(Encoding.UTF8.GetBytes(updatedJson)));
            if (!editOk)
            {
                Console.WriteLine($"Fehler beim Editieren von {itemId}: {editErr}");
            }
            else
            {
                Console.WriteLine($"Item {itemId} aktualisiert.");
            }
        }
    }
}