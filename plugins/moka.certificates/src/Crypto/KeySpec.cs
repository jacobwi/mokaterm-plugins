namespace Moka.Certificates.Crypto;

/// <summary>A new key: its algorithm, and the size or curve that goes with it.</summary>
public sealed record KeySpec(KeyAlgorithm Algorithm, RsaKeySize RsaSize = RsaKeySize.Bits3072, EllipticCurve Curve = EllipticCurve.P256);
