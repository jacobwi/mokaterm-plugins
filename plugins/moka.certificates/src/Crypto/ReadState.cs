namespace Moka.Certificates.Crypto;

/// <summary>What one <see cref="CertificateReader.Read"/> collects as it goes.</summary>
internal sealed class ReadState
{
	private readonly List<MaterialItem> _items = [];
	private readonly List<ReadNote> _notes = [];
	private bool _capped;

	public ReadState(string? password)
	{
		// An empty password field means no password, except where a format distinguishes the two (PFX tries both).
		Password = string.IsNullOrEmpty(password) ? null : password;
		GivenPassword = password;
	}

	public string? Password { get; }

	/// <summary>The password exactly as given, empty included.</summary>
	public string? GivenPassword { get; }

	public bool Full => _items.Count >= CertificateReader.MaxObjects;

	public void Add(MaterialItem item)
	{
		if (Full)
		{
			item.Dispose();
			NoteFull();
			return;
		}

		_items.Add(item);
	}

	/// <summary>Says once that the input held more than was read.</summary>
	public void NoteFull()
	{
		if (!_capped)
		{
			_capped = true;
			Note(NoteLevel.Warning, $"Only the first {CertificateReader.MaxObjects} objects were read.");
		}
	}

	public void Note(NoteLevel level, string message) => _notes.Add(new ReadNote(level, message));

	public MaterialSet ToSet() => new([.. _items], [.. _notes]);
}
