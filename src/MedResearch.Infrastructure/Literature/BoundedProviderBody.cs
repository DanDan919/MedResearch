using System.Text;

namespace MedResearch.Infrastructure.Literature;

public sealed class ProviderResponseTooLargeException : Exception
{
    public ProviderResponseTooLargeException() : base("Provider response exceeded the configured byte limit.") { }
}

public static class BoundedProviderBody
{
    public static async Task<string> ReadAsync(HttpContent content, int maximumBytes, TimeSpan timeout, CancellationToken cancellationToken, TimeProvider? timeProvider = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBytes is < 1 or > 10_000_000 || timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (content.Headers.ContentLength > maximumBytes) throw new ProviderResponseTooLargeException();
        using var deadline = new CancellationTokenSource(timeout, timeProvider ?? TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            using var stream = await content.ReadAsStreamAsync(linked.Token).WaitAsync(linked.Token);
            using var memory = new MemoryStream();
            var buffer = new byte[Math.Min(8192, maximumBytes + 1)];
            while (true)
            {
                // Read at most one byte beyond the cap, including streams without Content-Length.
                var count = Math.Min(buffer.Length, maximumBytes - (int)memory.Length + 1);
                var read = await stream.ReadAsync(buffer.AsMemory(0, count), linked.Token).AsTask().WaitAsync(linked.Token);
                if (read == 0) break;
                if (memory.Length + read > maximumBytes) throw new ProviderResponseTooLargeException();
                memory.Write(buffer, 0, read);
            }
            return Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Provider response body read timed out.");
        }
    }
}
