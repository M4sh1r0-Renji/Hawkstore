using Ravenhawk.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Ravenhawk.Services;

public sealed partial class SteamIdentityService
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly string[] CandidatePaths =
    [
        @"C:\Program Files (x86)\Steam\config\loginusers.vdf",
        @"C:\Program Files\Steam\config\loginusers.vdf",
        @"D:\Steam\config\loginusers.vdf",
        @"D:\SteamLibrary\config\loginusers.vdf"
    ];

    public SteamIdentity? FindCurrent()
    {
        foreach (var path in CandidatePaths.Where(File.Exists))
        {
            var identity = Parse(File.ReadAllText(path));
            if (identity is not null) return identity;
        }
        return null;
    }

    public async Task<SteamIdentity> VerifyWithSteamAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var callback = $"http://127.0.0.1:{port}/steam/callback/";
            var realm = $"http://127.0.0.1:{port}/";
            var loginUrl = "https://steamcommunity.com/openid/login?" + string.Join("&", new Dictionary<string, string>
            {
                ["openid.ns"] = "http://specs.openid.net/auth/2.0",
                ["openid.mode"] = "checkid_setup",
                ["openid.return_to"] = callback,
                ["openid.realm"] = realm,
                ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
                ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select"
            }.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
            Process.Start(new ProcessStartInfo(loginUrl) { UseShellExecute = true });

            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(timeout.Token) ?? "";
            string? header;
            do { header = await reader.ReadLineAsync(timeout.Token); } while (!string.IsNullOrEmpty(header));
            var target = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1)
                ?? throw new InvalidDataException(LocalizationService.Get("SteamVerificationFailed"));
            var callbackUri = new Uri(realm.TrimEnd('/') + target);
            var query = HttpUtility.ParseQueryString(callbackUri.Query);
            var claimedId = query["openid.claimed_id"] ?? "";
            var steamId = SteamClaimRegex().Match(claimedId).Groups[1].Value;
            if (string.IsNullOrWhiteSpace(steamId)) throw new InvalidDataException(LocalizationService.Get("SteamVerificationFailed"));

            var verification = new Dictionary<string, string>();
            foreach (var key in query.AllKeys.Where(x => x?.StartsWith("openid.", StringComparison.Ordinal) == true))
                verification[key!] = query[key!] ?? "";
            verification["openid.mode"] = "check_authentication";
            using var response = await _client.PostAsync("https://steamcommunity.com/openid/login", new FormUrlEncodedContent(verification), timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode || !body.Split('\n').Any(x => x.Trim().Equals("is_valid:true", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException(LocalizationService.Get("SteamVerificationFailed"));

            var current = FindCurrent();
            var identity = new SteamIdentity { SteamId = steamId, PersonaName = current?.SteamId == steamId ? current.PersonaName : "" };
            await WriteBrowserResponseAsync(stream, true, timeout.Token);
            return identity;
        }
        catch
        {
            throw;
        }
        finally { listener.Stop(); }
    }

    private static async Task WriteBrowserResponseAsync(NetworkStream stream, bool success, CancellationToken cancellationToken)
    {
        var title = success ? "Steam identity verified" : "Steam verification failed";
        var html = $"<!doctype html><meta charset=\"utf-8\"><title>{title}</title><style>body{{font:18px Segoe UI;background:#1e1e1e;color:#eee;padding:48px}}h1{{color:#2196f3}}</style><h1>{title}</h1><p>You can close this tab and return to Hawkstore.</p>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken);
        await stream.WriteAsync(bytes, cancellationToken);
    }

    private static SteamIdentity? Parse(string text)
    {
        SteamIdentity? fallback = null;
        foreach (Match match in AccountBlockRegex().Matches(text))
        {
            var body = match.Groups[2].Value;
            var persona = PersonaRegex().Match(body).Groups[1].Value;
            var identity = new SteamIdentity { SteamId = match.Groups[1].Value, PersonaName = persona };
            if (MostRecentRegex().IsMatch(body)) return identity;
            fallback ??= identity;
        }
        return fallback;
    }

    [GeneratedRegex("\\\"(7656119[0-9]{10})\\\"\\s*\\{(.*?)\\n\\s*\\}", RegexOptions.Singleline)]
    private static partial Regex AccountBlockRegex();
    [GeneratedRegex("\\\"PersonaName\\\"\\s*\\\"([^\\\"]*)\\\"")]
    private static partial Regex PersonaRegex();
    [GeneratedRegex("\\\"MostRecent\\\"\\s*\\\"1\\\"")]
    private static partial Regex MostRecentRegex();
    [GeneratedRegex("^https?://steamcommunity\\.com/openid/id/(7656119[0-9]{10})$")]
    private static partial Regex SteamClaimRegex();
}
