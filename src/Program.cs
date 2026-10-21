using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Forms = System.Windows.Forms;

namespace Pulse.Remote;

internal static class Program
{
    public const int Port = 48261;
    public static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PulseRemote");
    public static string Token = "";
    public static CompanionSettings Settings = new();
    public static string[] Addresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && IsLocal(a.Address))
        .Select(a => a.Address.ToString()).Distinct().ToArray();
    internal static bool IsLocal(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        byte[] b = address.GetAddressBytes();
        return b.Length == 4 && (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] >= 16 && b[1] <= 31);
    }
    public static string Url(string address) => $"http://{address}:{Port}/#key={Token}";
    internal static bool AllowedHost(string host) => host.TrimEnd('.').Equals(Settings.HostName, StringComparison.OrdinalIgnoreCase)
        || IPAddress.TryParse(host, out var ip) && IsLocal(ip);
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--start-hwinfo") { HwInfoLauncher.RunElevated(args[1]); return; }
        Directory.CreateDirectory(DataDirectory);
        if (args.Contains("--self-test")) { SelfTest.Run(args.Last()); return; }
        if (args.Contains("--probe"))
        {
            File.WriteAllText(args.Last(), JsonSerializer.Serialize(new SensorService().ReadOnce(), new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        using var mutex = new Mutex(true, @"Local\PulseRemote-48261", out bool first);
        if (!first) { InstanceSignal.Send(); return; }
        Settings = CompanionSettings.Load();
        var keyPath = Path.Combine(DataDirectory, "pairing-key.txt");
        Token = File.Exists(keyPath) ? File.ReadAllText(keyPath).Trim() : "";
        if (Token.Length != 32 || !Token.All(Uri.IsHexDigit))
        {
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            File.WriteAllText(keyPath, Token);
        }
        try
        {
            var builder = WebApplication.CreateSlimBuilder(args);
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls($"http://0.0.0.0:{Port}");
            builder.Services.AddSingleton<SensorService>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<SensorService>());
            builder.Services.AddRateLimiter(o =>
            {
                o.RejectionStatusCode = 429;
                o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(c =>
                    RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            });
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; manifest-src 'self' blob:; connect-src 'self' blob:; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
                if (!IsLocal(context.Connection.RemoteIpAddress)) { context.Response.StatusCode = 403; return; }
                // Allow only this installation's permanent name or local numeric addresses.
                if (!AllowedHost(context.Request.Host.Host))
                { context.Response.StatusCode = 403; return; }
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    string offered = context.Request.Headers["X-Pulse-Key"].ToString();
                    if (offered.Length != Token.Length || !CryptographicOperations.FixedTimeEquals(
                        System.Text.Encoding.UTF8.GetBytes(offered), System.Text.Encoding.UTF8.GetBytes(Token)))
                    { context.Response.StatusCode = 401; return; }
                }
                await next();
            });
            app.UseRateLimiter();
            app.MapGet("/api/snapshot", (SensorService sensors) => Results.Json(sensors.Current with { RefreshSeconds = Settings.RefreshSeconds }));
            app.MapPost("/api/refresh/{seconds:int}", (int seconds) =>
            {
                if (seconds is not (1 or 2 or 5 or 10)) return Results.BadRequest(new { message = "Choose 1, 2, 5, or 10 seconds." });
                lock (Settings) { Settings.RefreshSeconds = seconds; Settings.Save(); }
                return Results.Ok(new { refreshSeconds = Settings.RefreshSeconds });
            });
            foreach (string asset in new[] { "index.html", "style.css", "themes.css", "app.js", "icon.svg", "manifest.webmanifest" })
            {
                string name = asset;
                string contentType = asset.EndsWith(".css") ? "text/css" : asset.EndsWith(".js") ? "text/javascript" :
                    asset.EndsWith(".svg") ? "image/svg+xml" : asset.EndsWith(".webmanifest") ? "application/manifest+json" : "text/html";
                app.MapGet(asset == "index.html" ? "/" : "/" + asset, () =>
                {
                    var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pulse.Remote.wwwroot." + name)!;
                    return Results.Stream(stream, contentType);
                });
            }
            app.StartAsync().GetAwaiter().GetResult();
            using var localName = new LocalNameService(Settings.HostName);
            Forms.Application.SetHighDpiMode(Forms.HighDpiMode.SystemAware);
            Forms.Application.EnableVisualStyles();
            Forms.Application.SetCompatibleTextRenderingDefault(false);
            Forms.Application.Run(new CompanionWindow(app.Services.GetRequiredService<SensorService>(), localName));
            app.StopAsync().GetAwaiter().GetResult();
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(DataDirectory, "last-error.txt"), ex.ToString());
            Forms.MessageBox.Show("Pulse Remote could not start. Another app may be using port 48261.\n\n" + ex.Message, "Pulse Remote");
        }
    }
}
