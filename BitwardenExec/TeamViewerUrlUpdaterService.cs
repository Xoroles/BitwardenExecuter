using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace BitwardenExec;

internal class TeamViewerUrlUpdaterService : BackgroundService
{
    // Wenn die URL fehlt, fügen wir z.B. "teamviewerapi://control?item=<ItemID>" hinzu
    private const string URL_SCHEME_PREFIX = "http://127.0.0.1:5000/start-teamviewer?item=";
    private readonly string? _bwSessionToken;
    private readonly Regex _digitRegex = new("^[0-9]{9,10}$");
    private readonly TimeSpan _interval;

    // Beispiel: Wir wollen nur Items updaten, die im Namen "TV" oder "TeamViewer" enthalten
    //           und bei denen der username (ID) genau 9 oder 10 Ziffern aufweist.
    private readonly Regex _tvNameRegex = new("(TV|TeamViewer)", RegexOptions.IgnoreCase);

    public TeamViewerUrlUpdaterService(TimeSpan interval)
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
                    Console.WriteLine($"[TeamViewerUrlUpdaterService] Fehler bei bw list items: {errorMsg}");
                }
                else
                {
                    await ProcessItemsAsync(output);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TeamViewerUrlUpdaterService] Exception: {ex}");
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
            if (!_tvNameRegex.IsMatch(name))
            {
                continue;
            }

            // username/ID aus dem login
            var username = itemObj["login"]?["username"]?.ToString() ?? "";
            // username muss 9-10 Ziffern sein:
            if (!_digitRegex.IsMatch(username))
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