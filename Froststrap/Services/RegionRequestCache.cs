// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Security.Cryptography;

namespace Froststrap.Services;

internal static class RegionRequestCache
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private sealed record RequestState(DateTimeOffset NextRequest, DateTimeOffset RetryAfter);
    internal static string Folder => Path.Combine(Paths.Cache, "RoValra");
    internal static string CacheFile(string key) => Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
    internal static async Task<T?> ReadAsync<T>(string key, TimeSpan age, CancellationToken cancellation)
    {
        string file = CacheFile(key);
        if (!File.Exists(file) || DateTime.UtcNow - File.GetLastWriteTimeUtc(file) >= age) return default;
        try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(file, cancellation)); }
        catch (JsonException) { return default; }
    }
    internal static async Task StoreAsync<T>(string key, T value, CancellationToken cancellation)
    {
        Directory.CreateDirectory(Folder);
        string file = CacheFile(key), temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value), cancellation); File.Move(temporary, file, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static async Task<T> GetAsync<T>(HttpClient client, Uri uri, TimeSpan age, CancellationToken cancellation) where T : class
    {
        await Gate.WaitAsync(cancellation);
        try
        {
            Directory.CreateDirectory(Folder);
            await using var processLock = await LockAsync(cancellation);
            string key = uri.AbsoluteUri;
            if (await ReadAsync<T>(key, age, cancellation) is { } cached) return cached;
            string stateKey = "requests:" + uri.Host;
            var state = await ReadAsync<RequestState>(stateKey, TimeSpan.FromDays(1), cancellation) ?? new(default, default);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var now = DateTimeOffset.UtcNow;
                if (state.RetryAfter > now)
                {
                    var wait = state.RetryAfter - now;
                    if (wait > TimeSpan.FromSeconds(5)) throw Limited(uri.Host, state.RetryAfter);
                    await Task.Delay(wait, cancellation);
                }
                var spacing = state.NextRequest - DateTimeOffset.UtcNow;
                if (spacing > TimeSpan.Zero) await Task.Delay(spacing, cancellation);
                using var response = await client.GetAsync(uri, cancellation);
                var next = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(uri.Host == "apis.rovalra.com" ? 300 : 2000);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retry = response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow + (response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60));
                    state = new(next, retry > DateTimeOffset.UtcNow ? retry : DateTimeOffset.UtcNow.AddSeconds(1));
                    await StoreAsync(stateKey, state, CancellationToken.None);
                    if (attempt == 0 && state.RetryAfter - DateTimeOffset.UtcNow <= TimeSpan.FromSeconds(5)) continue;
                    throw Limited(uri.Host, state.RetryAfter);
                }
                await StoreAsync(stateKey, new RequestState(next, default), CancellationToken.None);
                response.EnsureSuccessStatusCode();
                T result = JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(cancellation)) ?? throw new JsonException("Empty server response from " + uri.Host);
                await StoreAsync(key, result, cancellation);
                foreach (var old in new DirectoryInfo(Folder).GetFiles("*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(4096)) old.Delete();
                return result;
            }
            throw Limited(uri.Host, state.RetryAfter);
        }
        finally { Gate.Release(); }
    }
    private static HttpRequestException Limited(string host, DateTimeOffset until) => new($"{host} is limiting server requests. Retry in {Math.Max(1, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalSeconds))} seconds; your selected region is saved.", null, HttpStatusCode.TooManyRequests);
    private static async Task<FileStream> LockAsync(CancellationToken cancellation)
    {
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(Folder, "requests.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { await Task.Delay(100, cancellation); }
        }
    }
}
