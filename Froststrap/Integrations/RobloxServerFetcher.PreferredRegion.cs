// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Security.Cryptography;

namespace Froststrap.Integrations;

internal partial class RobloxServerFetcher
{
    internal sealed class RegionDetail
    {
        [JsonPropertyName("server_id")] public string ServerId { get; set; } = "";
        [JsonPropertyName("datacenter_id")] public int DatacenterId { get; set; }
        [JsonPropertyName("city")] public string City { get; set; } = "";
        [JsonPropertyName("country")] public string Country { get; set; } = "";
    }
    private sealed class RegionDetails
    {
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("servers")] public List<RegionDetail> Servers { get; set; } = [];
    }
    private sealed class PublicPage
    {
        [JsonPropertyName("data")] public List<ServerInstance> Servers { get; set; } = [];
        [JsonPropertyName("nextPageCursor")] public string? Cursor { get; set; }
    }
    private async Task<T> ProviderAsync<T>(string query, TimeSpan lifetime, CancellationToken cancellation) where T : class
    {
        if (!App.Settings.Prop.EnableRoValra) throw new InvalidOperationException("Enable RoValra to use region selection.");
        return await Services.RegionRequestCache.GetAsync<T>(_client, new Uri("https://apis.rovalra.com" + query), lifetime, cancellation);
    }
    public async Task<IReadOnlyList<string>> GetPreferredRegionsAsync(CancellationToken cancellation = default)
    {
        var entries = await ProviderAsync<List<DatacenterEntry>>("/v1/datacenters/list", TimeSpan.FromHours(6), cancellation);
        _datacenterEntries = entries;
        _datacenterIdToRegion = entries.SelectMany(entry => entry.DataCenterIds.Select(id => (id, region: BuildRegionKey(entry.Location.City, entry.Location.Country))))
            .GroupBy(entry => entry.id).ToDictionary(group => group.Key, group => group.First().region);
        return _datacenterIdToRegion.Values.Where(region => region != "Unknown").Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static ServerInstance? SelectFullest(IEnumerable<ServerInstance> population, IEnumerable<RegionDetail> locations, string region, IReadOnlyDictionary<int, string> datacenters)
    {
        var ids = locations.Where(location => string.Equals(BuildRegionKey(location.City, location.Country), region, StringComparison.OrdinalIgnoreCase) ||
            datacenters.TryGetValue(location.DatacenterId, out string? mapped) && string.Equals(mapped, region, StringComparison.OrdinalIgnoreCase)).Select(location => location.ServerId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return population.Where(server => Guid.TryParse(server.Id, out _) && server.Playing >= 0 && server.MaxPlayers > 0 && server.Playing < server.MaxPlayers && ids.Contains(server.Id))
            .OrderByDescending(server => server.Playing).ThenBy(server => server.Id, StringComparer.Ordinal).FirstOrDefault();
    }
    public async Task<string> FindFullestPreferredServerAsync(long place, string region, CancellationToken cancellation = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(place);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(55));
        cancellation = deadline.Token;
        await GetPreferredRegionsAsync(cancellation);
        string? cursor = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int page = 0; page < 20; page++)
        {
            var publicUri = new Uri(UrlBuilder.BuildApiUrl("games", $"v1/games/{place}/servers/Public", secure: true) +
                "?sortOrder=Desc&excludeFullGames=true&limit=100&cursor=" + Uri.EscapeDataString(cursor ?? ""));
            var publicPage = await Services.RegionRequestCache.GetAsync<PublicPage>(_client, publicUri, TimeSpan.FromSeconds(60), cancellation);
            var candidates = publicPage.Servers.Where(server => Guid.TryParse(server.Id, out _) && server.MaxPlayers > server.Playing && server.Playing >= 0).ToArray();
            var details = new List<RegionDetail>();
            var unknown = new List<string>();
            foreach (string id in candidates.Select(server => server.Id).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (await Services.RegionRequestCache.ReadAsync<RegionDetail>("server:" + place + ":" + id, TimeSpan.FromHours(6), cancellation) is { } cached) details.Add(cached);
                else unknown.Add(id);
            }
            foreach (var batch in unknown.Order(StringComparer.Ordinal).Chunk(50))
            {
                var locations = await ProviderAsync<RegionDetails>($"/v1/servers/details?place_id={place}&server_ids={string.Join(',', batch)}", TimeSpan.FromSeconds(60), cancellation);
                if (locations.Status != "success") throw new HttpRequestException("RoValra could not locate the requested servers.");
                foreach (var location in locations.Servers.Where(location => batch.Contains(location.ServerId, StringComparer.OrdinalIgnoreCase)))
                {
                    details.Add(location);
                    await Services.RegionRequestCache.StoreAsync("server:" + place + ":" + location.ServerId, location, cancellation);
                }
            }
            var best = SelectFullest(candidates, details, region, _datacenterIdToRegion!);
            if (best != null) return best.Id;
            cursor = publicPage.Cursor;
            if (string.IsNullOrEmpty(cursor) || !seen.Add(cursor)) break;
        }
        throw new HttpRequestException("No available server in " + region + " was found within the 2,000-server search limit. Choose Auto or another region.");
    }
}
