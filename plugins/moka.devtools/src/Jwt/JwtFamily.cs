namespace Moka.DevTools.Jwt;

/// <summary>What kind of key an algorithm checks with, which decides the verify box the view shows.</summary>
public enum JwtFamily
{
	/// <summary>alg none: unsigned, nothing to check.</summary>
	None,

	/// <summary>HS256, HS384, HS512: a shared secret.</summary>
	Hmac,

	/// <summary>RS*, PS*, ES*: a public key, a certificate or a JWK.</summary>
	PublicKey,

	/// <summary>Anything this tool does not check (EdDSA, ES256K, a missing alg).</summary>
	Unsupported,
}
