namespace Bladehero.Telegram.Platform.Testing;

// The MIME type Telegram gives a document, worked out from its name. Only common extensions are known; any other is
// application/octet-stream.
internal static class MimeTypes
{
    public static string Of(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream",
        };
}
