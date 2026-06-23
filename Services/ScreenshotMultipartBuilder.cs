using System.Net.Http;
using System.Text;

namespace EduGuardAgent.Services;

internal static class ScreenshotTriggers
{
    public const string Scheduled = "scheduled";
    public const string OnCommand = "on_command";
}

internal static class ScreenshotMultipartBuilder
{
    public static ByteArrayContent Build(
        byte[] jpegBytes,
        DateTime capturedAtUtc,
        string trigger,
        string? focusedWindow)
    {
        // Alphanumeric boundary without quotes — strict multipart validators (Hono/Lovable)
        // reject Content-Type headers like: multipart/form-data; boundary="----..."
        // which .NET's MultipartFormDataContent emits by default.
        var boundary = "EduGuard" + Guid.NewGuid().ToString("N");
        using var stream = new MemoryStream();

        void Write(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        void WriteField(string name, string value)
        {
            Write($"--{boundary}\r\n");
            Write($"Content-Disposition: form-data; name=\"{name}\"\r\n\r\n");
            Write(value);
            Write("\r\n");
        }

        WriteField("kind", "screenshot");
        WriteField(
            "captured_at",
            capturedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        WriteField("trigger", trigger);

        if (!string.IsNullOrWhiteSpace(focusedWindow))
            WriteField("focused_window", Truncate(focusedWindow, 500));

        Write($"--{boundary}\r\n");
        Write("Content-Disposition: form-data; name=\"file\"; filename=\"screenshot.jpg\"\r\n");
        Write("Content-Type: image/jpeg\r\n\r\n");
        stream.Write(jpegBytes, 0, jpegBytes.Length);
        Write("\r\n");
        Write($"--{boundary}--\r\n");

        var body = stream.ToArray();
        var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation(
            "Content-Type",
            $"multipart/form-data; boundary={boundary}");
        return content;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
