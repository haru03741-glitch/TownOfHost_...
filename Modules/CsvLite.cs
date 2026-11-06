using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TownOfHostL.Modules;

// 埋め込みの翻訳 CSV (Resources/string.csv) を読むだけの最小の読み込み。
// 決まり: 1 行目が見出し。空行と '#' で始まる行は飛ばす。区切りは ','。値は "..." で囲めて、中の "" は " 1 文字。
// 1 つの値の中で改行はしない (翻訳文の改行は \n と書く)。
public static class CsvLite
{
    public sealed class Row
    {
        public string[] Headers;
        public string[] Values;
        public int Index; // 1 始まりの行番号 (見出しを 1 行目として数える)
        public int ColumnCount => Values.Length;
    }

    public static IEnumerable<Row> Read(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string[] headers = null;
        int index = 0;
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            index++;
            if (headers == null)
            {
                headers = Split(line);
                continue;
            }
            if (line.Length == 0 || line[0] == '#') continue;
            yield return new Row { Headers = headers, Values = Split(line), Index = index };
        }
    }

    // 区切りの ',' は引用符の外にあるものだけ。値が " で始まり " で終わる時だけ外側の " を外し、中の "" を " に戻す
    // (それ以外の崩れた引用符はそのまま残す。元のライブラリと同じ扱い)。
    static string[] Split(string line)
    {
        var values = new List<string>();
        int start = 0;
        bool inQuotes = false;
        for (int i = 0; i <= line.Length; i++)
        {
            if (i < line.Length)
            {
                char c = line[i];
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (c != ',' || inQuotes) continue;
            }
            string raw = line.Substring(start, i - start);
            if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
                raw = raw.Substring(1, raw.Length - 2).Replace("\"\"", "\"");
            values.Add(raw);
            start = i + 1;
        }
        return values.ToArray();
    }
}
