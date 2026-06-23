using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using EduGuardAgent.Security;

namespace EduGuardAgent.Services;

internal sealed class LocalCertificateAuthority : IDisposable
{
    private const string CaPassword = "EduGuard-Local-CA-v1";
    private static readonly string CertDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Config.AgentDataDir,
        "certs");

    private static readonly string RootCaPath = Path.Combine(CertDir, "eduguard-root.pfx");

    private readonly Dictionary<string, X509Certificate2> _leafCerts = new(StringComparer.OrdinalIgnoreCase);
    private X509Certificate2? _rootCa;
    public string? LastError { get; private set; }

    public bool EnsureReady()
    {
        LastError = null;
        try
        {
            Directory.CreateDirectory(CertDir);
            _rootCa = LoadOrCreateRootCa();
            InstallRootToTrustStore(_rootCa);
            AuditLog.Write("EduGuard root certificate installed to Windows trust store.");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public void SyncBlockedHosts(IEnumerable<string> hosts)
    {
        _leafCerts.Clear();

        foreach (var host in hosts.SelectMany(UrlBlocklistStore.ExpandHostEntries).Distinct(StringComparer.OrdinalIgnoreCase))
            _leafCerts[host] = GetOrCreateLeafCertificate(host);
    }

    public X509Certificate2? SelectServerCertificate(string? hostName)
    {
        if (_rootCa is null)
            return null;

        if (string.IsNullOrWhiteSpace(hostName))
            return _leafCerts.Values.FirstOrDefault();

        if (_leafCerts.TryGetValue(hostName, out var exact))
            return exact;

        if (hostName.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            && _leafCerts.TryGetValue(hostName[4..], out var bare))
            return bare;

        return GetOrCreateLeafCertificate(hostName);
    }

    private X509Certificate2 LoadOrCreateRootCa()
    {
        if (File.Exists(RootCaPath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                RootCaPath,
                CaPassword,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
        }

        using var rsa = RSA.Create(4096);
        var request = new CertificateRequest(
            "CN=EduGuard Supervision Root",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, true, 1, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(10);
        using var root = request.CreateSelfSigned(notBefore, notAfter);
        var exported = root.Export(X509ContentType.Pfx, CaPassword);
        File.WriteAllBytes(RootCaPath, exported);

        return X509CertificateLoader.LoadPkcs12(
            exported,
            CaPassword,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
    }

    private X509Certificate2 GetOrCreateLeafCertificate(string host)
    {
        if (_leafCerts.TryGetValue(host, out var cached))
            return cached;

        var safeName = host.Replace('*', '_');
        var leafPath = Path.Combine(CertDir, $"{safeName}.pfx");
        if (File.Exists(leafPath))
        {
            var loaded = X509CertificateLoader.LoadPkcs12FromFile(
                leafPath,
                CaPassword,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
            _leafCerts[host] = loaded;
            return loaded;
        }

        if (_rootCa is null)
            throw new InvalidOperationException("Root CA is not initialized.");

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN={host}"),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                false));
        request.CertificateExtensions.Add(
            BuildSanExtension(host));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(2);
        using var created = request.Create(_rootCa, notBefore, notAfter, RandomNumberGenerator.GetBytes(16));
        var withKey = created.CopyWithPrivateKey(rsa);
        var pfx = withKey.Export(X509ContentType.Pfx, CaPassword);
        File.WriteAllBytes(leafPath, pfx);

        var cert = X509CertificateLoader.LoadPkcs12(
            pfx,
            CaPassword,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
        _leafCerts[host] = cert;
        return cert;
    }

    private static X509Extension BuildSanExtension(string host)
    {
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(host);
        if (!host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            sanBuilder.AddDnsName($"www.{host}");

        return sanBuilder.Build();
    }

    private static void InstallRootToTrustStore(X509Certificate2 rootCa)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        var exists = store.Certificates
            .Cast<X509Certificate2>()
            .Any(c => string.Equals(c.Thumbprint, rootCa.Thumbprint, StringComparison.OrdinalIgnoreCase));

        if (!exists)
            store.Add(rootCa);
    }

    public void Dispose()
    {
        foreach (var cert in _leafCerts.Values)
            cert.Dispose();

        _leafCerts.Clear();
        _rootCa?.Dispose();
        _rootCa = null;
    }
}
