using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using NetatmoThermoSync.Auth;
using NetatmoThermoSync.Models;

namespace NetatmoThermoSync.Api;

public sealed class NetatmoClient : IDisposable
{
    private const string BaseUrl = "https://api.netatmo.com/api";
    private readonly HttpClient _http = new();
    private readonly WebSessionAuth _auth;

    public NetatmoClient(WebSessionAuth auth)
    {
        _auth = auth;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    }

    public void Dispose() => _http.Dispose();

    private async Task<string> SendAsync(string url, CancellationToken cancellationToken, HttpContent? content = null)
    {
        var response = content is not null
            ? await _http.PostAsync(url, content, cancellationToken)
            : await _http.GetAsync(url, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            if (await _auth.TryReauthenticateAsync(cancellationToken))
            {
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _auth.AccessToken);
                response = content is not null
                    ? await _http.PostAsync(url, content, cancellationToken)
                    : await _http.GetAsync(url, cancellationToken);
            }
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new NetatmoException($"API call failed ({response.StatusCode}): {json}");
        }

        return json;
    }

    public async Task<NetatmoResponse<HomesDataBody>> GetHomesDataAsync(CancellationToken cancellationToken = default)
    {
        var json = await SendAsync($"{BaseUrl}/homesdata", cancellationToken);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.NetatmoResponseHomesDataBody)
            ?? throw new NetatmoException("Failed to parse homesdata response.");
    }

    public async Task<NetatmoResponse<HomeStatusBody>> GetHomeStatusAsync(string homeId, CancellationToken cancellationToken = default)
    {
        var json = await SendAsync($"{BaseUrl}/homestatus?home_id={Uri.EscapeDataString(homeId)}", cancellationToken);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.NetatmoResponseHomeStatusBody)
            ?? throw new NetatmoException("Failed to parse homestatus response.");
    }

    public async Task<NetatmoResponse<StationsDataBody>> GetStationsDataAsync(CancellationToken cancellationToken = default)
    {
        var json = await SendAsync($"{BaseUrl}/getstationsdata", cancellationToken);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.NetatmoResponseStationsDataBody)
            ?? throw new NetatmoException("Failed to parse getstationsdata response.");
    }

    public async Task SetRoomThermPointAsync(string homeId, string roomId, double temp, int? endTime = null, CancellationToken cancellationToken = default)
    {
        var parameters = new Dictionary<string, string>
        {
            ["home_id"] = homeId,
            ["room_id"] = roomId,
            ["mode"] = "manual",
            ["temp"] = temp.ToString("F1", CultureInfo.InvariantCulture),
        };

        if (endTime.HasValue)
        {
            parameters["endtime"] = endTime.Value.ToString(CultureInfo.InvariantCulture);
        }

        var content = new FormUrlEncodedContent(parameters);
        await SendAsync($"{BaseUrl}/setroomthermpoint", cancellationToken, content);
    }
}
