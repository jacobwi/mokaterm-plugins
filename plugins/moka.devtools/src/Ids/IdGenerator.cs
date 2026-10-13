namespace Moka.DevTools.Ids;

/// <summary>New identifiers in the text form people paste: UUIDs from .NET, ULIDs from <see cref="Ulid"/>.</summary>
public static class IdGenerator
{
	public const int MaxCount = 1000;

	public static IReadOnlyList<string> Generate(IdKind kind, int count, bool upper, bool hyphens, DateTimeOffset now)
	{
		count = Math.Clamp(count, 1, MaxCount);
		if (kind == IdKind.Ulid)
		{
			// Crockford's base32 is written in capitals; lower case reads the same, so it is offered as a choice.
			return [.. Ulid.CreateMany(count, now).Select(ulid => upper ? ulid.ToString() : ulid.ToString().ToLowerInvariant())];
		}

		List<string> ids = new(count);
		for (int index = 0; index < count; index++)
		{
			Guid id = kind == IdKind.UuidV7 ? Guid.CreateVersion7(now) : Guid.NewGuid();
			string text = id.ToString(hyphens ? "D" : "N");
			ids.Add(upper ? text.ToUpperInvariant() : text);
		}

		return ids;
	}
}
