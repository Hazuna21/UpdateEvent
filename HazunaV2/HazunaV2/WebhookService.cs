using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace HazunaV2;

// Mengirim permintaan HTTP (webhook) saat sebuah aturan dijalankan.
// Placeholder: {user} {gift} {count} {index} {effect}
public static class WebhookService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<string> SendAsync(string url, string method, string body, IDictionary<string, string> vars)
    {
        string target = Fill(url ?? "", vars, FillMode.Url).Trim();
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("URL webhook harus diawali http:// atau https://");

        bool get = string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase);
        using var req = new HttpRequestMessage(get ? HttpMethod.Get : HttpMethod.Post, uri);

        if (!get)
        {
            string trimmed = (body ?? "").TrimStart();
            bool json = trimmed.StartsWith('{') || trimmed.StartsWith('[');
            string text = Fill(body ?? "", vars, json ? FillMode.Json : FillMode.Plain);
            req.Content = new StringContent(text, Encoding.UTF8, json ? "application/json" : "text/plain");
        }

        using var resp = await Http.SendAsync(req);
        string status = $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException("Server menjawab " + status);
        return status;
    }

    private enum FillMode { Plain, Url, Json }

    private static string Fill(string template, IDictionary<string, string> vars, FillMode mode)
    {
        foreach (var kv in vars)
        {
            string value = mode switch
            {
                FillMode.Url => Uri.EscapeDataString(kv.Value),
                FillMode.Json => JsonEncodedText.Encode(kv.Value).ToString(),
                _ => kv.Value
            };
            template = template.Replace("{" + kv.Key + "}", value, StringComparison.OrdinalIgnoreCase);
        }
        return template;
    }
}
