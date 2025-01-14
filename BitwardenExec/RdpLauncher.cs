using System.Diagnostics;

namespace BitwardenExec
{
    public class RdpLauncher
    {
        /// <summary>
        /// Startet eine RDP-Sitzung mit dem angegebenen Host, User und Password, 
        /// indem eine .rdp-Datei erstellt und cmdkey /add genutzt wird.
        /// Löscht die Credentials wieder, sobald mstsc.exe beendet ist.
        /// </summary>
        /// <param name="host">RDP-Adresse (z. B. "myserver.domain.local")</param>
        /// <param name="username">Domänen-User (z. B. "DOMAIN\\Benutzer")</param>
        /// <param name="password">Passwort</param>
        public async Task StartRdpSessionAsync(string host, string username, string password)
        {
            // 1) Temporäre RDP-Datei erstellen
            string rdpFilePath = Path.Combine(Path.GetTempPath(), $"rdp_{Guid.NewGuid()}.rdp");

            // Wichtig: "prompt for credentials:i:0" -> Keine PW-Abfrage (wird aus Credential Manager geholt).
            // "full address:s:<HOST>" -> Zieladresse
            // "username:s:<USER>" -> Anzeigename im Login (ohne PW)
            // Ggf. zusätzliche RDP-Einstellungen (ColorDepth, etc.) hinzufügen.
            string rdpContent = $@"
full address:s:{host}
username:s:{username}
prompt for credentials:i:0
redirectclipboard:i:1
redirectprinters:i:0
redirectcomports:i:0
redirectsmartcards:i:0
redirectaudio:i:1
audiomode:i:2
";

            await File.WriteAllTextAsync(rdpFilePath, rdpContent);

            // 2) Credentials temporär per cmdkey /add eintragen
            //    Syntax: cmdkey /add:<HOST> /user:<USER> /pass:<PASSWORT>
            //    Beachte: host muss identisch sein mit "full address" oder dem, was MSTSC als Server ansieht.
          //  await RunCmdAsync($"/c cmdkey /add:{host} /user:{username} /pass:{password}");

            // 3) mstsc.exe mit der RDP-Datei starten
            //    Wir warten, bis MSTSC beendet ist -> damit wir anschließend cmdkey /delete ausführen können.
            var mstscProc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "mstsc.exe",
                    Arguments = $"\"{rdpFilePath}\"",
                    UseShellExecute = false
                }
            };

            mstscProc.Start();
            await mstscProc.WaitForExitAsync(); // Warte, bis RDP geschlossen wird

            // 4) Credentials wieder löschen
            //    cmdkey /delete:<HOST>
       //     await RunCmdAsync($"/c cmdkey /delete:{host}");

            // 5) Aufräumen: RDP-Datei entfernen (optional)
            try
            {
                File.Delete(rdpFilePath);
            }
            catch (Exception ex)
            {
                // Logging, falls gewünscht
                Console.WriteLine($"Fehler beim Löschen der .rdp-Datei: {ex.Message}");
            }

            Console.WriteLine("RDP-Sitzung beendet, Credentials aufgeräumt.");
        }

        /// <summary>
        /// Hilfsmethode zum Ausführen eines cmd.exe-Befehls.
        /// </summary>
        private async Task RunCmdAsync(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            await proc.WaitForExitAsync();

            var output = await proc.StandardOutput.ReadToEndAsync();
            var error = await proc.StandardError.ReadToEndAsync();

            if (proc.ExitCode != 0)
            {
                Console.WriteLine($"Fehler bei RunCmd({arguments}): {error}");
            }
        }
    }
}
