// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0

using System.Net.WebSockets;

namespace Froststrap.Integrations.AccountManager;

internal sealed class BrowserLoginSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions CommandOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly string[] LoginUrls = ["https://www.roblox.com/"];
    private readonly string _profile = Path.Combine(Paths.Temp, "BrowserLogin", Guid.NewGuid().ToString("N"));
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _commands = new(1);
    private Process? _process;
    private int _commandId;
    private string? _targetId;
    private string? _sessionId;
    private Uri? _endpoint;

    internal string ProfileDirectory => _profile;

    internal async Task StartAsync(string executable, CancellationToken token, bool headless = false)
    {
        if (_process != null) throw new InvalidOperationException("This login session has already started.");
        Directory.CreateDirectory(_profile);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = headless };
        foreach (string argument in new[] { "--user-data-dir=" + _profile, "--remote-debugging-port=0", "--remote-debugging-address=127.0.0.1", "--no-first-run", "--no-default-browser-check", "--disable-notifications", "--disable-extensions", "--disable-background-mode", "about:blank" })
            start.ArgumentList.Add(argument);
        if (headless) start.ArgumentList.Add("--headless=new");
        _process = Process.Start(start) ?? throw new IOException("The sign-in browser could not start.");

        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
        startup.CancelAfter(TimeSpan.FromSeconds(30));
        var endpointFile = Path.Combine(_profile, "DevToolsActivePort");
        Uri? endpoint = null;
        while (endpoint == null)
        {
            startup.Token.ThrowIfCancellationRequested();
            if (_process.HasExited && _process.ExitCode != 0) throw new IOException("The sign-in browser failed to start (exit code " + _process.ExitCode + ").");
            try
            {
                if (File.Exists(endpointFile))
                {
                    string[] lines = await File.ReadAllLinesAsync(endpointFile, startup.Token);
                    if (lines.Length >= 2) endpoint = ParseEndpoint(lines[0], lines[1]);
                }
            }
            catch (IOException) { }
            if (endpoint == null) await Task.Delay(100, startup.Token);
        }
        _endpoint = endpoint;
        _socket.Options.Proxy = null;
        await _socket.ConnectAsync(endpoint, startup.Token);
        var processes = await CommandAsync("SystemInfo.getProcessInfo", null, startup.Token);
        int browserId = processes.GetProperty("processInfo").EnumerateArray().First(process => process.GetProperty("type").GetString() == "browser").GetProperty("id").GetInt32();
        if (browserId != _process.Id)
        {
            var browserProcess = Process.GetProcessById(browserId);
            if (!browserProcess.ProcessName.Equals(Path.GetFileNameWithoutExtension(executable), StringComparison.OrdinalIgnoreCase))
            {
                browserProcess.Dispose();
                throw new IOException("The isolated sign-in browser returned an unexpected process.");
            }
            _process.Dispose();
            _process = browserProcess;
        }
        var targets = await CommandAsync("Target.getTargets", null, startup.Token);
        _targetId = targets.GetProperty("targetInfos").EnumerateArray()
            .First(target => target.GetProperty("type").GetString() == "page").GetProperty("targetId").GetString();
        var attached = await CommandAsync("Target.attachToTarget", new { targetId = _targetId, flatten = true }, startup.Token);
        _sessionId = attached.GetProperty("sessionId").GetString();
    }

    internal static Uri ParseEndpoint(string portText, string path)
    {
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port) || port is < 1 or > 65535 ||
            !path.StartsWith("/devtools/browser/", StringComparison.Ordinal) || path.Length > 128 ||
            path.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '/' and not '-'))
            throw new InvalidDataException("The sign-in browser returned an invalid local endpoint.");
        return new Uri($"ws://127.0.0.1:{port}{path}");
    }

    internal async Task<string?> WaitForLoginAsync(CancellationToken token)
    {
        await CommandAsync("Page.navigate", new { url = "https://www.roblox.com/login" }, token, _sessionId);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (!await IsPageOpenAsync(token)) return null;
            string? cookie = await ReadLoginCookieAsync(token);
            if (cookie != null) return cookie;
            await Task.Delay(1000, token);
        }
    }

    internal async Task<bool> IsPageOpenAsync(CancellationToken token)
    {
        var targets = await CommandAsync("Target.getTargets", null, token);
        return targets.GetProperty("targetInfos").EnumerateArray().Any(target => target.GetProperty("targetId").GetString() == _targetId);
    }

    internal async Task<string?> ReadLoginCookieAsync(CancellationToken token)
    {
        var cookies = await CommandAsync("Network.getCookies", new { urls = LoginUrls }, token, _sessionId);
        return FindLoginCookie(cookies);
    }

    internal static string? FindLoginCookie(JsonElement result)
    {
        foreach (var cookie in result.GetProperty("cookies").EnumerateArray())
        {
            string? domain = cookie.GetProperty("domain").GetString();
            if (cookie.GetProperty("name").GetString() == ".ROBLOSECURITY" &&
                domain is ".roblox.com" or "roblox.com" or "www.roblox.com" &&
                cookie.GetProperty("secure").GetBoolean() && cookie.GetProperty("httpOnly").GetBoolean())
            {
                string? value = cookie.GetProperty("value").GetString();
                if (!string.IsNullOrEmpty(value)) return value;
            }
        }
        return null;
    }

    internal async Task<JsonElement> CommandAsync(string method, object? parameters, CancellationToken token, string? sessionId = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await _commands.WaitAsync(timeout.Token);
        try
        {
            int id = ++_commandId;
            byte[] request = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters ?? new { }, sessionId }, CommandOptions);
            await _socket.SendAsync(request, WebSocketMessageType.Text, true, timeout.Token);
            byte[] buffer = new byte[8192];
            while (true)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult received;
                do
                {
                    received = await _socket.ReceiveAsync(buffer, timeout.Token);
                    if (received.MessageType == WebSocketMessageType.Close) throw new WebSocketException("The sign-in browser closed.");
                    if (received.MessageType != WebSocketMessageType.Text || message.Length + received.Count > 1024 * 1024)
                        throw new InvalidDataException("The sign-in browser returned an invalid response.");
                    await message.WriteAsync(buffer.AsMemory(0, received.Count), timeout.Token);
                } while (!received.EndOfMessage);
                using var document = JsonDocument.Parse(message.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
                var response = document.RootElement;
                if (!response.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue;
                if (response.TryGetProperty("error", out _)) throw new IOException("The browser could not complete the sign-in command: " + method);
                return response.GetProperty("result").Clone();
            }
        }
        finally { _commands.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State == WebSocketState.Open)
        {
            using var close = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try { await CommandAsync("Browser.close", null, close.Token); }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException) { }
        }
        else if (_endpoint != null)
        {
            using var close = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var cleanup = new ClientWebSocket();
            cleanup.Options.Proxy = null;
            try
            {
                await cleanup.ConnectAsync(_endpoint, close.Token);
                await cleanup.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { id = 1, method = "Browser.close" }), WebSocketMessageType.Text, true, close.Token);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException) { }
        }
        _socket.Dispose();
        if (_process != null)
        {
            if (!_process.HasExited)
            {
                _process.CloseMainWindow();
                using var exit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await _process.WaitForExitAsync(exit.Token); }
                catch (OperationCanceledException) { }
            }
            bool closed = _process.HasExited;
            _process.Dispose();
            if (!closed)
            {
                App.Logger.Warn("The isolated sign-in browser is still closing; its temporary profile could not be removed.");
                _commands.Dispose();
                return;
            }
        }
        for (int attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                if (Directory.Exists(_profile)) Directory.Delete(_profile, true);
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == 11) App.Logger.Warn(ex, "The isolated sign-in browser left a locked temporary profile.");
                else await Task.Delay(250);
            }
        }
        _commands.Dispose();
    }
}
