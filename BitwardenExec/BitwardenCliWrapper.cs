using System.Diagnostics;

namespace BitwardenExec;

internal class BitwardenCliWrapper
{
    /// <summary>
    ///     Führt einen bw-CLI-Befehl aus und gibt (success, stdout, stderr) zurück.
    /// </summary>
    public static async Task<(bool ok, string stdout, string stderr)> RunBitwardenCliAsync(string arguments,
        string? stdinContent = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "bw",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinContent != null,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = new Process();
        proc.StartInfo = psi;
        proc.Start();

        if (stdinContent != null)
        {
            await proc.StandardInput.WriteAsync(stdinContent);
            proc.StandardInput.Close();
        }

        var output = await proc.StandardOutput.ReadToEndAsync();
        var error = await proc.StandardError.ReadToEndAsync();

        await proc.WaitForExitAsync();
        var success = proc.ExitCode == 0;
        return (success, output, error);
    }
}