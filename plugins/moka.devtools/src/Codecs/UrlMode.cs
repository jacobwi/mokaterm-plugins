namespace Moka.DevTools.Codecs;

public enum UrlMode
{
	/// <summary>One query value or path segment: everything but letters, digits and <c>- . _ ~</c> is escaped.</summary>
	Component,

	/// <summary>A whole address: the characters that give a URL its structure stay, the way JavaScript's encodeURI keeps them.</summary>
	WholeUrl,
}
