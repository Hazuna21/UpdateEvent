using System.Net.Http;
using System.Text.Json;

namespace HazunaV2;

// Mengambil katalog gift dari API TikTools (layanan pihak ketiga, butuh API key)
public static class TikToolsClient
{
    private const string Url = "https://api.tik.tools/webcast/gift_info";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<(List<LiveGiftInfo>? Gifts, string Error)> FetchGiftsAsync(string apiKey)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Url);
            request.Headers.Add("x-api-key", apiKey.Trim());

            using var response = await Http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return (null, $"HTTP {(int)response.StatusCode}: {Shorten(body)}");

            using var doc = JsonDocument.Parse(body);

            if (!TryGetGifts(doc.RootElement, out var gifts))
                return (null, "Format respons tidak dikenali: " + Shorten(body));

            var list = new List<LiveGiftInfo>();
            foreach (var g in gifts.EnumerateArray())
            {
                if (g.ValueKind != JsonValueKind.Object) continue;

                var name = Str(g, "name") ?? "";
                if (name.Length == 0) continue;

                long id = Num(g, "id") ?? 0;
                long diamonds = Num(g, "diamond_count") ?? Num(g, "diamondCount") ?? 0;
                var icon = Str(g, "icon") ?? Str(g, "icon_url") ?? Str(g, "image");

                list.Add(new LiveGiftInfo(id, name.Trim(), diamonds, icon));
            }

            return (list, "");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static bool TryGetGifts(JsonElement root, out JsonElement gifts)
    {
        gifts = default;

        if (root.ValueKind != JsonValueKind.Object) return false;

        if (root.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("gifts", out var inData) &&
            inData.ValueKind == JsonValueKind.Array)
        {
            gifts = inData;
            return true;
        }

        if (root.TryGetProperty("gifts", out var direct) && direct.ValueKind == JsonValueKind.Array)
        {
            gifts = direct;
            return true;
        }

        return false;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    private static string Shorten(string text) =>
        text.Length <= 300 ? text : text[..300] + "...";
}