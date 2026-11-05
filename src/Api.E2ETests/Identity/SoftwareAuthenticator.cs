using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Api.E2ETests.Identity;

/// <summary>
/// A minimal software WebAuthn authenticator (ES256, "none" attestation, user-verifying, synced/backup-eligible),
/// so passkey ceremonies run against the REAL ASP.NET Core Identity verifier over HTTP - no mocked crypto.
/// It produces exactly what a browser returns from <c>credential.toJSON()</c>.
/// </summary>
internal sealed class SoftwareAuthenticator : IDisposable
{
    private const byte UserPresent = 0x01;
    private const byte UserVerified = 0x04;
    private const byte BackupEligible = 0x08;
    private const byte BackedUp = 0x10;
    private const byte AttestedCredentialData = 0x40;

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _origin;
    private uint _signCount;

    public SoftwareAuthenticator(string origin = "http://localhost:3000", bool userVerification = true)
    {
        _origin = origin;
        UserVerification = userVerification;
    }

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

    public string CredentialIdBase64Url => Base64Url.EncodeToString(CredentialId);

    public bool UserVerification { get; set; }

    /// <summary>The <c>user.id</c> from the creation options (returned as <c>userHandle</c> on sign-in).</summary>
    public string? UserHandle { get; private set; }

    /// <summary><c>navigator.credentials.create()</c> for the given creation options JSON.</summary>
    public JsonObject Create(string creationOptionsJson)
    {
        var options = JsonNode.Parse(creationOptionsJson)!.AsObject();
        var rpId = options["rp"]?["id"]?.GetValue<string>() ?? "localhost";
        UserHandle = options["user"]!["id"]!.GetValue<string>();

        var clientData = ClientData("webauthn.create", options["challenge"]!.GetValue<string>());

        var parameters = _key.ExportParameters(false);
        var coseKey = Cbor.Map(
            (Cbor.Int(1), Cbor.Int(2)),             // kty: EC2
            (Cbor.Int(3), Cbor.Int(-7)),            // alg: ES256
            (Cbor.Int(-1), Cbor.Int(1)),            // crv: P-256
            (Cbor.Int(-2), Cbor.Bytes(parameters.Q.X!)),
            (Cbor.Int(-3), Cbor.Bytes(parameters.Q.Y!)));

        var authData = new List<byte>();
        authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        authData.Add((byte)(Flags() | AttestedCredentialData));
        authData.AddRange(Counter(_signCount));
        authData.AddRange(new byte[16]); // AAGUID: none
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)CredentialId.Length);
        authData.AddRange(length);
        authData.AddRange(CredentialId);
        authData.AddRange(coseKey);

        var attestationObject = Cbor.Map(
            (Cbor.Text("fmt"), Cbor.Text("none")),
            (Cbor.Text("attStmt"), Cbor.Map()),
            (Cbor.Text("authData"), Cbor.Bytes([.. authData])));

        return new JsonObject
        {
            ["id"] = CredentialIdBase64Url,
            ["rawId"] = CredentialIdBase64Url,
            ["type"] = "public-key",
            ["authenticatorAttachment"] = "platform",
            ["clientExtensionResults"] = new JsonObject(),
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                ["attestationObject"] = Base64Url.EncodeToString(attestationObject),
                ["transports"] = new JsonArray("internal", "hybrid")
            }
        };
    }

    /// <summary><c>navigator.credentials.get()</c> for the given request options JSON.</summary>
    public JsonObject Get(string requestOptionsJson, byte[]? overrideSignature = null)
    {
        var options = JsonNode.Parse(requestOptionsJson)!.AsObject();
        var rpId = options["rpId"]?.GetValue<string>() ?? "localhost";
        var clientData = ClientData("webauthn.get", options["challenge"]!.GetValue<string>());

        _signCount++;
        var authData = new List<byte>();
        authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        authData.Add(Flags());
        authData.AddRange(Counter(_signCount));

        var signed = authData.Concat(SHA256.HashData(clientData)).ToArray();
        var signature = overrideSignature
                        ?? _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        return new JsonObject
        {
            ["id"] = CredentialIdBase64Url,
            ["rawId"] = CredentialIdBase64Url,
            ["type"] = "public-key",
            ["authenticatorAttachment"] = "platform",
            ["clientExtensionResults"] = new JsonObject(),
            ["response"] = new JsonObject
            {
                ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                ["authenticatorData"] = Base64Url.EncodeToString([.. authData]),
                ["signature"] = Base64Url.EncodeToString(signature),
                ["userHandle"] = UserHandle
            }
        };
    }

    public void Dispose() => _key.Dispose();

    private byte Flags() =>
        (byte)(UserPresent | (UserVerification ? UserVerified : 0) | BackupEligible | BackedUp);

    private byte[] ClientData(string type, string challenge) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
    {
        ["type"] = type,
        ["challenge"] = challenge,
        ["origin"] = _origin,
        ["crossOrigin"] = false
    });

    private static byte[] Counter(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    /// <summary>Just enough CBOR (RFC 8949) for COSE keys and attestation objects.</summary>
    private static class Cbor
    {
        public static byte[] Int(long value) => value >= 0 ? Head(0, (ulong)value) : Head(1, (ulong)(-1 - value));

        public static byte[] Bytes(byte[] value) => [.. Head(2, (ulong)value.Length), .. value];

        public static byte[] Text(string value)
        {
            var utf8 = Encoding.UTF8.GetBytes(value);
            return [.. Head(3, (ulong)utf8.Length), .. utf8];
        }

        public static byte[] Map(params (byte[] Key, byte[] Value)[] entries)
        {
            var result = new List<byte>(Head(5, (ulong)entries.Length));
            foreach (var (key, value) in entries)
            {
                result.AddRange(key);
                result.AddRange(value);
            }

            return [.. result];
        }

        private static byte[] Head(int major, ulong length)
        {
            var type = (byte)(major << 5);
            return length switch
            {
                < 24 => [(byte)(type | (byte)length)],
                <= byte.MaxValue => [(byte)(type | 24), (byte)length],
                <= ushort.MaxValue => [(byte)(type | 25), (byte)(length >> 8), (byte)length],
                _ => [(byte)(type | 26), (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length]
            };
        }
    }
}
