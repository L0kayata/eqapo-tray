using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace EqApoTray.Services;

public static class EqApoConfig
{
    private static readonly Regex PreampRegex = new(
        @"Preamp:\s*([-\d.]+)\s*dB",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static double Read(string path)
    {
        if (!File.Exists(path)) return 0.0;
        var content = File.ReadAllText(path);
        var m = PreampRegex.Match(content);
        if (!m.Success) return 0.0;
        return double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : 0.0;
    }

    public static void Write(string path, double value)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Equalizer APO 配置文件不存在", path);

        var content = File.ReadAllText(path);
        var newLine = $"Preamp: {value.ToString("F1", CultureInfo.InvariantCulture)} dB";

        content = PreampRegex.IsMatch(content)
            ? PreampRegex.Replace(content, newLine, count: 1)
            : newLine + Environment.NewLine + content;

        File.WriteAllText(path, content);
    }
}
