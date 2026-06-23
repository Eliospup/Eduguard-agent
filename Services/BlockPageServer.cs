using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EduGuardAgent.Services;

internal sealed class BlockPageServer : IDisposable
{
    private readonly LocalCertificateAuthority _ca = new();
    private IHost? _host;

    public bool IsRunning { get; private set; }
    public string? LastError { get; private set; }
    public string? CertificateError => _ca.LastError;

    public void Start(IReadOnlyCollection<string> blockedHosts)
    {
        Stop();
        LastError = null;

        if (blockedHosts.Count == 0)
            return;

        try
        {
            if (!_ca.EnsureReady())
            {
                LastError = _ca.LastError ?? "Certificate authority setup failed.";
                return;
            }

            _ca.SyncBlockedHosts(blockedHosts);

            _host = Host.CreateDefaultBuilder()
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(ConfigureKestrel);
                    webBuilder.Configure(app => app.Run(HandleRequestAsync));
                })
                .Build();

            _host.Start();
            IsRunning = true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            IsRunning = false;
        }
    }

    private void ConfigureKestrel(KestrelServerOptions options)
    {
        options.AddServerHeader = false;

        options.Listen(IPAddress.Loopback, 80);
        options.Listen(IPAddress.IPv6Loopback, 80);

        void ConfigureHttps(ListenOptions listen)
        {
            listen.UseHttps(https =>
            {
                https.ServerCertificateSelector = (_, name) =>
                    _ca.SelectServerCertificate(name);
            });
        }

        options.Listen(IPAddress.Loopback, 443, ConfigureHttps);
        options.Listen(IPAddress.IPv6Loopback, 443, ConfigureHttps);
    }

    private static async Task HandleRequestAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(host)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host == "127.0.0.1"
            || host == "::1")
        {
            host = "blocked site";
        }

        var html = BlockPageHtml.Build(host);
        var bytes = Encoding.UTF8.GetBytes(html);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes);
    }

    public void Stop(TimeSpan timeout = default)
    {
        if (timeout == default)
            timeout = TimeSpan.FromSeconds(2);

        if (_host is not null)
        {
            var host = _host;
            _host = null;
            IsRunning = false;

            try
            {
                host.StopAsync().WaitAsync(timeout).GetAwaiter().GetResult();
            }
            catch
            {
                // Best effort shutdown — abandon a stuck Kestrel host.
            }

            try
            {
                host.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }
        }
        else
        {
            IsRunning = false;
        }
    }

    public void Dispose()
    {
        Stop();
        _ca.Dispose();
    }
}
