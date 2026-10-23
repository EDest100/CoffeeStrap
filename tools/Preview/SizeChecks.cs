// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0

using System.Reflection;
using System.Text.Json;
using Froststrap;
using Froststrap.Integrations.AccountManager;

internal static partial class Program
{
    private static void VerifySizeChanges()
    {
        Check(BrowserLoginSession.ParseEndpoint("45555", "/devtools/browser/ab12-cd34").Host == "127.0.0.1", "browser discovery always uses the isolated loopback endpoint");
        foreach (var invalid in new[] { ("0", "/devtools/browser/abc"), ("65536", "/devtools/browser/abc"), ("1234", "//example.com/abc"), ("1234", "/devtools/browser/../abc"), ("1234", "/devtools/browser/abc?host=remote") })
        {
            bool rejected = false;
            try { BrowserLoginSession.ParseEndpoint(invalid.Item1, invalid.Item2); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid browser endpoint is rejected: " + invalid.Item1 + invalid.Item2);
        }
        foreach (string domain in new[] { ".roblox.com", "roblox.com", "www.roblox.com", "evilroblox.com", "roblox.com.example.net", "example.net" })
        {
            using var data = JsonDocument.Parse(JsonSerializer.Serialize(new { cookies = new[] { new { name = ".ROBLOSECURITY", value = "synthetic-login-fixture", domain, secure = true, httpOnly = true } } }));
            Check((BrowserLoginSession.FindLoginCookie(data.RootElement) != null) == (domain is ".roblox.com" or "roblox.com" or "www.roblox.com"), "sign-in cookie domain is matched exactly: " + domain);
        }
        foreach (var flags in new[] { (false, true), (true, false), (false, false) })
        {
            using var data = JsonDocument.Parse(JsonSerializer.Serialize(new { cookies = new[] { new { name = ".ROBLOSECURITY", value = "synthetic-login-fixture", domain = ".roblox.com", secure = flags.Item1, httpOnly = flags.Item2 } } }));
            Check(BrowserLoginSession.FindLoginCookie(data.RootElement) == null, "sign-in requires both Secure and HttpOnly cookie attributes: " + flags);
        }
        var verifier = typeof(RemoteDataManager).GetMethod("VerifyEd25519", BindingFlags.Static | BindingFlags.NonPublic)!;
        byte[] key = Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        byte[] signature = Convert.FromHexString("e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");
        Check((bool)verifier.Invoke(null, [Array.Empty<byte>(), signature, key])!, "production remote-config verifier accepts the RFC 8032 signature");
        Check(!(bool)verifier.Invoke(null, [new byte[] { 1 }, signature, key])!, "production remote-config verifier rejects a changed message");
        Check(!(bool)verifier.Invoke(null, [Array.Empty<byte>(), signature, new byte[31]])!, "production remote-config verifier rejects the wrong key length");
        Check(!(bool)verifier.Invoke(null, [Array.Empty<byte>(), new byte[63], key])!, "production remote-config verifier rejects the wrong signature length");
        var dependencies = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CoffeeStrap.Preview.deps.json")));
        var names = dependencies.RootElement.GetProperty("libraries").EnumerateObject().Select(item => item.Name).ToArray();
        foreach (string removed in new[] { "PuppeteerSharp/", "PuppeteerExtraSharp/", "WebDriverBiDi/", "HotAvalonia/", "Avalonia.X11/", "Avalonia.Native/", "Avalonia.Wayland/", "NWayland/" })
            Check(!names.Any(name => name.StartsWith(removed, StringComparison.Ordinal)), "Windows release dependency graph excludes " + removed);
    }

    private static void ProbeBrowserLogin()
    {
        VerifySizeChanges();
        var browsers = new[] { @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"C:\Program Files\Google\Chrome\Application\chrome.exe" }.Where(File.Exists).ToArray();
        Check(browsers.Length > 0, "native browser probe has an installed Chromium browser available");
        foreach (string executable in browsers) ProbeBrowserLogin(executable);
    }

    private static void ProbeBrowserLogin(string executable)
    {
        string profile = "";
        var probe = Task.Run(async () =>
        {
            await using (var browser = new BrowserLoginSession())
            {
                profile = browser.ProfileDirectory;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await browser.StartAsync(executable, timeout.Token, headless: true);
                Check(await browser.IsPageOpenAsync(timeout.Token), "native isolated browser opens and attaches to its own page");
                Check(await browser.ReadLoginCookieAsync(timeout.Token) == null, "new browser profile has no inherited Roblox credentials");
                var targets = await browser.CommandAsync("Target.getTargets", null, timeout.Token);
                string target = targets.GetProperty("targetInfos").EnumerateArray().First(item => item.GetProperty("type").GetString() == "page").GetProperty("targetId").GetString()!;
                var session = await browser.CommandAsync("Target.attachToTarget", new { targetId = target, flatten = true }, timeout.Token);
                string sessionId = session.GetProperty("sessionId").GetString()!;
                await browser.CommandAsync("Network.setCookie", new { name = ".ROBLOSECURITY", value = "synthetic-coffeestrap-test-not-a-token", domain = ".roblox.com", path = "/", secure = true, httpOnly = true }, timeout.Token, sessionId);
                Check(await browser.ReadLoginCookieAsync(timeout.Token) == "synthetic-coffeestrap-test-not-a-token", "production sign-in reader retrieves only the synthetic Secure HttpOnly cookie");
                bool invalidCommand = false;
                try { await browser.CommandAsync("CoffeeStrap.nonexistentCommand", null, timeout.Token); }
                catch (IOException) { invalidCommand = true; }
                Check(invalidCommand, "native browser protocol errors propagate as sign-in failures");
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                bool cancellation = false;
                try { await browser.ReadLoginCookieAsync(cancelled.Token); }
                catch (OperationCanceledException) { cancellation = true; }
                Check(cancellation, "cancelled sign-in commands stop before socket access");
                await browser.CommandAsync("Target.closeTarget", new { targetId = target }, timeout.Token);
            }
        });
        WaitFor(() => probe.IsCompleted, 60000);
        probe.GetAwaiter().GetResult();
        Check(!Directory.Exists(profile), "owned native browser exits gracefully and removes its temporary synthetic profile");
        File.WriteAllText(Path.Combine(_output, "browser-login-probe-" + Path.GetFileNameWithoutExtension(executable) + ".json"), JsonSerializer.Serialize(new { browser = Path.GetFileName(executable), isolatedProfile = true, realAccountUsed = false, robloxVisited = false, memoryAccess = false, browserDownload = false, profileRemoved = !Directory.Exists(profile) }));
    }
}
