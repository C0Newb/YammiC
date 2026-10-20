namespace CNCO.YccuSdk;

internal static class ByteExtensions
{
    internal static string ToHexString(this byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));

    /// <summary>Hex of the first <paramref name="max"/> bytes plus the total size, so logging a multi-kilobyte image chunk stays one short line.</summary>
    internal static string ToHexPreview(this byte[] bytes, int max = 48) =>
        bytes.Length <= max ? bytes.ToHexString() : $"{bytes.Take(max).ToArray().ToHexString()} ... ({bytes.Length}B total)";

    /// <summary>
    /// Awaits [task], returning false (and a default value) if it did not complete within [timeout].
    /// </summary>
    internal static async Task<(bool Completed, T? Value)> WithTimeoutAsync<T>(this Task<T> task, TimeSpan timeout)
    {
        try
        {
            return (true, await task.WaitAsync(timeout).ConfigureAwait(false));
        }
        catch (TimeoutException)
        {
            return (false, default);
        }
    }
}
