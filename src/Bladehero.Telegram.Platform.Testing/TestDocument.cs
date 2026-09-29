namespace Bladehero.Telegram.Platform.Testing;

/// <summary>A file a <see cref="TestUser"/> sends in a document album.</summary>
/// <param name="Content">The file's bytes; any will do, but not none.</param>
/// <param name="FileName">The file's name, e.g. <c>march.pdf</c>.</param>
/// <param name="MimeType">
/// The MIME type Telegram reports; when <c>null</c>, it comes from the extension of <paramref name="FileName"/>, as for
/// <see cref="TestUser.SendsDocumentAsync"/>.
/// </param>
public sealed record TestDocument(byte[] Content, string FileName, string? MimeType = null);
