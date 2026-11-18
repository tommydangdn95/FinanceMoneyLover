using System.Diagnostics;
using System.Text;

namespace MoneyLoverAutoInput.Core.Tools;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Chạy tiến trình ngoài (adb, node...) có timeout, đọc stdout/stderr dạng UTF-8.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string file, string arguments, TimeSpan timeout,
        CancellationToken ct = default, IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (env is not null)
        {
            foreach (var (k, v) in env) psi.Environment[k] = v;
        }

        using var p = new Process { StartInfo = psi };
        p.Start();
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await p.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"'{Path.GetFileName(file)} {arguments}' quá {timeout.TotalSeconds:0}s.");
        }

        return new ProcessResult(p.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }
}

/// <summary>Biến môi trường hiệu lực: process → user → machine (app có thể được mở trước khi người dùng cài công cụ).</summary>
public static class EnvVars
{
    public static string? Get(string name)
    {
        foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            var v = System.Environment.GetEnvironmentVariable(name, target);
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return null;
    }

    /// <summary>PATH gộp machine + user + process (bỏ trùng).</summary>
    public static IEnumerable<string> PathDirs()
    {
        var all = new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine }
            .SelectMany(t => (System.Environment.GetEnvironmentVariable("PATH", t) ?? "").Split(';'))
            .Select(s => s.Trim())
            .Where(s => s.Length > 0);
        return all.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static string? FindOnPath(params string[] fileNames)
    {
        foreach (var dir in PathDirs())
        {
            foreach (var f in fileNames)
            {
                try
                {
                    var p = Path.Combine(dir, f);
                    if (File.Exists(p)) return p;
                }
                catch (ArgumentException) { }
            }
        }
        return null;
    }
}
