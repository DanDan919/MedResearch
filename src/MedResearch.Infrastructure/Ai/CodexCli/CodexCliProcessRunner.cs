using System.ComponentModel;
using System.Diagnostics;

namespace MedResearch.Infrastructure.Ai.CodexCli;

public interface ICodexCliProcessRunner
{
    Task<CodexCliProcessResult> RunAsync(
        CodexCliProcessRequest request,
        CancellationToken cancellationToken);
}

public sealed record CodexCliProcessRequest(
    string ExecutablePath,
    IReadOnlyCollection<string> Arguments,
    string WorkingDirectory,
    string StandardInput,
    TimeSpan Timeout);

public sealed record CodexCliProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public enum CodexCliProcessFailureKind
{
    NotInstalled,
    TimedOut
}

public sealed class CodexCliProcessException : Exception
{
    public CodexCliProcessException(CodexCliProcessFailureKind kind, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public CodexCliProcessFailureKind Kind { get; }
}

public sealed class CodexCliProcessRunner : ICodexCliProcessRunner
{
    public async Task<CodexCliProcessResult> RunAsync(
        CodexCliProcessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);
        Directory.CreateDirectory(request.WorkingDirectory);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(request)
        };
        RemoveProjectSecretsFromChildEnvironment(process.StartInfo);

        try
        {
            if (!process.Start())
            {
                throw new CodexCliProcessException(
                    CodexCliProcessFailureKind.NotInstalled,
                    "Codex CLI process could not be started.");
            }
        }
        catch (Win32Exception exception)
        {
            throw new CodexCliProcessException(
                CodexCliProcessFailureKind.NotInstalled,
                "Codex CLI executable was not found or could not be started.",
                exception);
        }

        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(request.Timeout);

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();

            await process.WaitForExitAsync(timeoutCancellation.Token);
            await Task.WhenAll(standardOutputTask, standardErrorTask);

            return new CodexCliProcessResult(
                process.ExitCode,
                await standardOutputTask,
                await standardErrorTask);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            throw new CodexCliProcessException(
                CodexCliProcessFailureKind.TimedOut,
                "Codex CLI process timed out.");
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }
        catch
        {
            KillProcessTree(process);
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(CodexCliProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void RemoveProjectSecretsFromChildEnvironment(ProcessStartInfo startInfo)
    {
        foreach (var name in new[]
        {
            "AI__ApiKey",
            "OPENAI_API_KEY",
            "PubMed__ApiKey",
            "ConnectionStrings__MedResearch",
            "MEDRESEARCH_LIVE_E2E_CONNECTION_STRING",
            "MEDRESEARCH_LIVE_E2E_DATABASE_ACK"
        })
        {
            startInfo.Environment.Remove(name);
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }
}
