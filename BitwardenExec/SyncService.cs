using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;

namespace BitwardenExec;

internal class SyncService : BackgroundService
{
    private readonly TimeSpan _interval;
    private readonly string? _bwSessionToken;

    public SyncService(TimeSpan interval)
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
                  await BitwardenCliWrapper.RunBitwardenCliAsync($"sync --session {_bwSessionToken}");
                if (!ok)
                {
                    Console.WriteLine($"[SyncService] Fehler bei bw list items: {errorMsg}");
                }
                else
                {
                     Console.WriteLine($"[SyncService] Sync erfolgreich: {output}");
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
}