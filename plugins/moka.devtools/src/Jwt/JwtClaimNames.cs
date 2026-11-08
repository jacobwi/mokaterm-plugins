namespace Moka.DevTools.Jwt;

/// <summary>What the registered claims of RFC 7519 and the common OpenID Connect ones stand for, in a word or two.</summary>
public static class JwtClaimNames
{
	/// <summary>"Expires" for exp, "Subject" for sub; null for a claim with no registered meaning.</summary>
	public static string? Describe(string name) => name switch
	{
		"iss" => "Issuer",
		"sub" => "Subject",
		"aud" => "Audience",
		"exp" => "Expires",
		"nbf" => "Not before",
		"iat" => "Issued at",
		"jti" => "Token id",
		"auth_time" => "Signed in at",
		"nonce" => "Nonce",
		"azp" => "Authorized party",
		"sid" => "Session id",
		"acr" => "Authentication class",
		"amr" => "Sign-in methods",
		"scope" or "scp" => "Scopes",
		"client_id" => "Client id",
		"roles" => "Roles",
		"groups" => "Groups",
		"name" => "Full name",
		"given_name" => "Given name",
		"family_name" => "Family name",
		"preferred_username" => "User name",
		"email" => "Email",
		"email_verified" => "Email checked",
		"tid" => "Tenant id",
		"oid" => "Object id",
		"cnf" => "Key binding",
		_ => null,
	};
}
