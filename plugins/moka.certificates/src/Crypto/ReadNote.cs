namespace Moka.Certificates.Crypto;

/// <summary>Something the reader noticed and could not turn into an object: a locked key, a format it does not read, a damaged block.</summary>
public sealed record ReadNote(NoteLevel Level, string Message);
