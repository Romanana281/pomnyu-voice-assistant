using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Assistant.Infrastructure.GigaChat;

internal static class RussianTrustedCertificates
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "GigaChat", "Certificates");

    private static readonly Lazy<X509Certificate2> Root =
        new(() => X509CertificateLoader.LoadCertificateFromFile(Path.Combine(Directory, "russian_trusted_root_ca.cer")));

    private static readonly Lazy<X509Certificate2> Intermediate =
        new(() => X509CertificateLoader.LoadCertificateFromFile(Path.Combine(Directory, "russian_trusted_sub_ca.cer")));

    public static SocketsHttpHandler CreateHandler() => new()
    {
        SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = Validate },
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    };

    private static bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
            return true;
        if (certificate is null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
            return false;

        using var customChain = new X509Chain();
        customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        customChain.ChainPolicy.CustomTrustStore.Add(Root.Value);
        customChain.ChainPolicy.ExtraStore.Add(Intermediate.Value);
        customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return customChain.Build(new X509Certificate2(certificate));
    }
}
