namespace Moka.DevTools.Hashing;

/// <summary>How a secret key is written: as text (its UTF-8 bytes are the key), hex or Base64.</summary>
public enum KeyFormat
{
	Text,
	Hex,
	Base64,
}
