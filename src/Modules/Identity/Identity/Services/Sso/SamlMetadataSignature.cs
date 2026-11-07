using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace Identity.Services.Sso;

public enum MetadataSignature
{
    /// <summary>The metadata carries no XML signature (still accepted, see <see cref="SamlMetadataSignature"/>).</summary>
    NotSigned = 0,

    /// <summary>Signed, and the signature verifies against one of the pinned certificates.</summary>
    Valid = 1,

    /// <summary>Signed, but the signature is broken, made by another key, or not the shape we accept.</summary>
    Invalid = 2
}

/// <summary>
/// Verifies the (optional) enveloped XML signature of IdP metadata against the certificates ALREADY PINNED on the
/// connection. This is what lets a rollover through (the old key signs the metadata that announces the new key) while a
/// hijacked metadata host cannot swap the keys of a federation that publishes SIGNED metadata.
/// <para>
/// Honest limits: UNSIGNED metadata is accepted, because many IdPs publish none, and then the entity-id check does
/// NOT protect against a hijacked or compromised metadata host (it can serve the right entity id with attacker keys).
/// Prefer an IdP that signs its metadata, or leave the metadata URL empty and rotate certificates by hand.
/// </para>
/// Accepted shape: exactly one <c>ds:Signature</c>, a direct child of the root element, with exactly one reference to
/// the root (<c>URI=""</c> or <c>#ID</c>) and only enveloped-signature / canonicalization transforms (no XSLT, no
/// XPath), which closes the usual XML-signature-wrapping tricks.
/// </summary>
public static class SamlMetadataSignature
{
    public static MetadataSignature Verify(string xml, IEnumerable<string> pinnedCertificates)
    {
        XmlDocument document;
        try
        {
            document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document.Load(reader);
        }
        catch (XmlException)
        {
            return MetadataSignature.Invalid;
        }

        var signatures = document.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        if (signatures.Count == 0)
            return MetadataSignature.NotSigned;

        var root = document.DocumentElement;
        if (root is null || signatures.Count != 1 || signatures[0]!.ParentNode != root)
            return MetadataSignature.Invalid;

        try
        {
            var signed = new SignedXml(document);
            signed.LoadXml((XmlElement)signatures[0]!);
            if (signed.SignedInfo?.References.Count != 1)
                return MetadataSignature.Invalid;

            var reference = (Reference)signed.SignedInfo.References[0]!;
            var id = root.GetAttribute("ID");
            var targetsRoot = reference.Uri == string.Empty || (id.Length > 0 && reference.Uri == "#" + id);
            if (!targetsRoot || !OnlySafeTransforms(reference.TransformChain))
                return MetadataSignature.Invalid;

            foreach (var pinned in pinnedCertificates)
            {
                using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(pinned));
                if (signed.CheckSignature(certificate, verifySignatureOnly: true))
                    return MetadataSignature.Valid;
            }

            return MetadataSignature.Invalid;
        }
        catch (Exception ex) when (ex is CryptographicException or XmlException or FormatException or ArgumentException
                                       or InvalidCastException or NullReferenceException)
        {
            return MetadataSignature.Invalid;
        }
    }

    private static bool OnlySafeTransforms(TransformChain chain)
    {
        for (var i = 0; i < chain.Count; i++)
        {
            if (chain[i] is not (XmlDsigEnvelopedSignatureTransform or XmlDsigExcC14NTransform or XmlDsigC14NTransform))
                return false;
        }

        return true;
    }
}
