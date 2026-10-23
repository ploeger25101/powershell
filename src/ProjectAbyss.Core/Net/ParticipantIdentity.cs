using System;
using System.IO;
using System.Security.Cryptography;
using ProjectAbyss.Core.Data;

namespace ProjectAbyss.Core.Net
{
    /// <summary>
    /// Locally generated ECDSA P-256 key pair. The participant id is derived from the public key, so a client cannot
    /// claim someone else's run slot by sending a chosen name or id: on (re)connect it must sign the host's nonce.
    /// No account, server or personal data is involved.
    /// </summary>
    public sealed class ParticipantIdentity : IDisposable
    {
        private readonly ECDsa key;

        private ParticipantIdentity(ECDsa key)
        {
            this.key = key;
            this.PublicKey = key.ExportSubjectPublicKeyInfo();
            this.ParticipantId = IdFromPublicKey(this.PublicKey);
        }

        public byte[] PublicKey { get; }
        public string ParticipantId { get; }

        public static ParticipantIdentity Create() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

        public static ParticipantIdentity LoadOrCreate(string path)
        {
            if (File.Exists(path))
            {
                var ecdsa = ECDsa.Create();
                ecdsa.ImportPkcs8PrivateKey(File.ReadAllBytes(path), out _);
                return new ParticipantIdentity(ecdsa);
            }
            var created = Create();
            Persistence.AtomicFile.WriteAllBytes(path, created.key.ExportPkcs8PrivateKey());
            return created;
        }

        public static string IdFromPublicKey(byte[] publicKey) => "pa-" + Hashing.Sha256Hex(publicKey).Substring(0, 32);

        public byte[] Sign(byte[] challenge) => this.key.SignData(challenge, HashAlgorithmName.SHA256);

        public static bool Verify(byte[] publicKey, byte[] challenge, byte[] signature)
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
                return ecdsa.VerifyData(challenge, signature, HashAlgorithmName.SHA256);
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        public void Dispose() => this.key.Dispose();
    }
}
