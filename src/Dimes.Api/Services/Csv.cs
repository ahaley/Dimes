using System.Text;

namespace Dimes.Api.Services;

/// <summary>RFC 4180 CSV rendering, kept in one place because both of its concerns are quiet
/// correctness traps rather than formatting taste.
///
/// The first is escaping: a change's title is free text and will contain commas, quotes and the
/// occasional newline, all of which corrupt the row if emitted raw.
///
/// The second is formula injection, and it is the reason this is a shared helper rather than a
/// string.Join at the call site. Titles and descriptions reach Dimes from an SDK embedded in
/// arbitrary host apps and from LLM synthesis — untrusted text by construction — and a spreadsheet
/// evaluates any cell that opens with an operator, so an exported title of <c>=cmd|'…'!A1</c> is
/// live code the moment a human double-clicks the file.</summary>
public static class Csv
{
    /// <summary>Characters that make a spreadsheet treat the cell as a formula rather than text.
    /// Tab and CR qualify because Excel strips leading whitespace before deciding.</summary>
    private static readonly char[] FormulaLeaders = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>Render one field: neutralize a leading formula character, then quote per RFC 4180.
    /// Order matters — the guard has to run before quoting so the apostrophe lands inside the quotes
    /// and is seen by the spreadsheet, not by the CSV parser.</summary>
    public static string Field(string? value)
    {
        var s = value ?? string.Empty;

        // A leading apostrophe is the conventional mitigation: spreadsheets read it as "this cell is
        // text" and don't display it. It does mean a title that legitimately opens with '-' shows up
        // as '- … in the sheet — accepted, because the alternative is executing it.
        if (s.Length > 0 && FormulaLeaders.Contains(s[0]))
        {
            s = "'" + s;
        }

        // Leading/trailing whitespace is quoted too: unquoted, a parser is free to trim it, which
        // silently rewrites the data.
        var mustQuote = s.Contains('"') || s.Contains(',') || s.Contains('\n') || s.Contains('\r')
            || (s.Length > 0 && (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1])));

        return mustQuote ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }

    /// <summary>Append one escaped row. CRLF per RFC 4180, independent of the host platform, so the
    /// bytes a Windows and a Linux install produce for the same board are identical.</summary>
    public static void AppendRow(StringBuilder sb, params string?[] fields)
    {
        sb.Append(string.Join(',', fields.Select(Field))).Append("\r\n");
    }
}
