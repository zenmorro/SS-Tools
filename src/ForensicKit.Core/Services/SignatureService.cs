using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ForensicKit.Core.Models;

namespace ForensicKit.Core.Services;

public interface ISignatureService
{
    SignatureInfo Inspect(string filePath);
}

/// <summary>
/// Reads the Authenticode certificate embedded in an executable (if any) so the UI
/// can show the signer to the user before launch. This performs a best-effort read
/// of the embedded certificate; it does not, and must not, alter system trust.
/// </summary>
public sealed class SignatureService : ISignatureService
{
    public SignatureInfo Inspect(string filePath)
    {
        if (!File.Exists(filePath))
            return new SignatureInfo(false, null, null, null, null, "File not found.");

        try
        {
            // Throws if the file has no embedded signature.
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));

            var now = DateTime.Now;
            var expired = now < cert.NotBefore || now > cert.NotAfter;
            var status = expired
                ? "Signed, but the certificate is outside its validity window."
                : "Signed. Certificate present and within its validity window.";

            return new SignatureInfo(
                IsSigned: true,
                Subject: cert.GetNameInfo(X509NameType.SimpleName, false),
                Issuer: cert.GetNameInfo(X509NameType.SimpleName, true),
                NotBefore: cert.NotBefore,
                NotAfter: cert.NotAfter,
                StatusMessage: status);
        }
        catch (CryptographicException)
        {
            return new SignatureInfo(false, null, null, null, null,
                "Not digitally signed (no embedded Authenticode certificate).");
        }
        catch (Exception ex)
        {
            return new SignatureInfo(false, null, null, null, null,
                $"Could not read signature: {ex.Message}");
        }
    }
}
