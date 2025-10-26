namespace DragonDen.ModManager.Utils;

public class HTMLUtils
{
    public static string HtmlToDisplay(string html)
    {
        var s = html ?? "";
        s = s.Replace("\r", "");

        s = System.Text.RegularExpressions.Regex.Replace(s, @"<\s*br\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        s = System.Text.RegularExpressions.Regex.Replace(s, @"<\s*/\s*p\s*>", "\n\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<\s*p\b[^>]*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        s = System.Text.RegularExpressions.Regex.Replace(s, @"<\s*li\b[^>]*>", "• ", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<\s*/\s*li\s*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<\s*/\s*ul\s*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<\s*/\s*ol\s*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<\s*ul\b[^>]*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        s = System.Text.RegularExpressions.Regex.Replace(s, @"<\s*ol\b[^>]*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        s = System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");

        s = System.Net.WebUtility.HtmlDecode(s);

        s = System.Text.RegularExpressions.Regex.Replace(s, @"\n{3,}", "\n\n").Trim();

        return s;
    }
}