using System;
using System.Linq;

namespace MFAWPF.Services.RinoPackages;

internal sealed class RinoPackageSigningPublicKey
{
	public string KeyId { get; }

	public ReadOnlyMemory<byte> PublicKeyBytes { get; }

	public RinoPackageSigningPublicKey(string keyId, ReadOnlySpan<byte> publicKeyBytes)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(keyId, "keyId");
		if (keyId.Length > 128 || keyId.Any((char character) => !char.IsAsciiLetterOrDigit(character) && character != '.' && character != '_' && character != ':' && character != '-'))
		{
			throw new ArgumentException("Signing key ID is invalid.", "keyId");
		}
		if (publicKeyBytes.Length != 32)
		{
			throw new ArgumentException("Ed25519 public keys must contain exactly 32 bytes.", "publicKeyBytes");
		}
		KeyId = keyId;
		PublicKeyBytes = publicKeyBytes.ToArray();
	}
}
