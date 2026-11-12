using System.Text;

namespace HazunaV2;

// Memecah teks per bahasa (Jepang / Inggris / Indonesia) supaya tiap bagian dibaca dengan suara bahasanya.
public static class TtsLang
{
    private static readonly HashSet<string> English = new(StringComparer.OrdinalIgnoreCase)
    {
        "the","is","are","was","were","you","your","i","im","me","my","we","they","he","she","it","its",
        "and","or","but","to","of","in","on","at","for","with","that","this","these","those","what","how",
        "why","when","where","who","can","could","do","does","did","will","would","have","has","had",
        "not","no","yes","so","very","really","hello","hi","hey","thanks","thank","good","great","nice",
        "love","like","please","welcome","stream","today","just","about","from","be","been","get","got"
    };

    private static readonly HashSet<string> Indonesian = new(StringComparer.OrdinalIgnoreCase)
    {
        "yang","dan","di","ke","dari","ini","itu","aku","kamu","saya","gue","lu","apa","nggak","gak","ga",
        "udah","sudah","ada","mau","bisa","dengan","untuk","juga","aja","kok","deh","sih","banget","halo",
        "hai","terima","kasih","selamat","berkata","sama","lagi","ya","tidak","bukan","akan","sedang","dia",
        "kita","kami","mereka","nih","dong","yuk","ayo","bang","kak","gimana","bagaimana","kenapa","siapa"
    };

    private enum Kind { Neutral, Japanese, Other }

    private static Kind KindOf(char c)
    {
        if ((c >= '\u3040' && c <= '\u30FF') ||      // hiragana, katakana, tanda panjang
            (c >= '\u4E00' && c <= '\u9FFF') ||      // kanji
            (c >= '\u3400' && c <= '\u4DBF') ||
            (c >= '\u3000' && c <= '\u303F') ||      // tanda baca Jepang
            (c >= '\uFF01' && c <= '\uFF60') ||      // huruf dan tanda baca lebar penuh
            (c >= '\uFF66' && c <= '\uFF9F'))        // katakana setengah lebar
            return Kind.Japanese;

        if (char.IsLetter(c)) return Kind.Other;
        return Kind.Neutral;                           // spasi, angka, tanda baca ASCII
    }

    // Mengembalikan potongan berurutan: ("ja" | "en" | "id", teks)
    public static List<(string Lang, string Text)> Split(string text)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var sb = new StringBuilder();
        Kind current = Kind.Neutral;

        void Flush()
        {
            var s = sb.ToString().Trim();
            sb.Clear();
            if (s.Length == 0) return;
            string lang = current == Kind.Japanese ? "ja" : DetectLatin(s);

            // Gabungkan dengan potongan sebelumnya kalau bahasanya sama
            if (result.Count > 0 && result[^1].Item1 == lang)
                result[^1] = (lang, result[^1].Item2 + " " + s);
            else
                result.Add((lang, s));
        }

        foreach (var ch in text)
        {
            var k = KindOf(ch);
            if (k != Kind.Neutral && current != Kind.Neutral && k != current)
            {
                Flush();
                current = k;
            }
            else if (current == Kind.Neutral && k != Kind.Neutral)
            {
                current = k;
            }
            sb.Append(ch);
        }
        Flush();
        return result;
    }

    // Inggris atau Indonesia untuk teks huruf Latin (kata-kata umum menentukan)
    private static string DetectLatin(string s)
    {
        int en = 0, id = 0;
        foreach (var w in s.Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '\'', '"', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (English.Contains(w)) en++;
            if (Indonesian.Contains(w)) id++;
        }
        return en > id ? "en" : "id";
    }
}
