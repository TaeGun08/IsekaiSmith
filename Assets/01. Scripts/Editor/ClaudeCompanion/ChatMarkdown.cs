using System.Collections.Generic;
using System.Text.RegularExpressions;

// Deliberately not a full CommonMark implementation - Unity's IMGUI rich text only understands
// a handful of inline tags (<b>, <i>, <color>), so this only covers what Claude's CLI output
// actually uses in practice: fenced code blocks, **bold**, `inline code`, and "- "/"* " bullets.
public static class ChatMarkdown
{
    public readonly struct Segment
    {
        public readonly bool IsCode;
        public readonly string Text;

        public Segment(bool isCode, string text)
        {
            IsCode = isCode;
            Text = text;
        }
    }

    private static readonly Regex FencedCodeBlock = new Regex("```[a-zA-Z0-9]*\r?\n?(.*?)```", RegexOptions.Singleline);
    private static readonly Regex Bold = new Regex(@"\*\*(.+?)\*\*");
    private static readonly Regex InlineCode = new Regex(@"`([^`\n]+?)`");
    private static readonly Regex BulletLine = new Regex(@"^[ \t]*[-*][ \t]+", RegexOptions.Multiline);

    // Splits raw text into alternating plain-text and fenced-code-block segments so the
    // caller can render code in its own non-rich-text, monospace-ish block instead of running
    // markdown/rich-text conversion over it (which would mangle real code).
    public static List<Segment> Split(string raw)
    {
        List<Segment> segments = new List<Segment>();
        int lastEnd = 0;
        foreach (Match match in FencedCodeBlock.Matches(raw))
        {
            if (match.Index > lastEnd)
            {
                segments.Add(new Segment(false, raw.Substring(lastEnd, match.Index - lastEnd)));
            }
            segments.Add(new Segment(true, match.Groups[1].Value.Trim('\n', '\r')));
            lastEnd = match.Index + match.Length;
        }
        if (lastEnd < raw.Length)
        {
            segments.Add(new Segment(false, raw.Substring(lastEnd)));
        }
        if (segments.Count == 0)
        {
            segments.Add(new Segment(false, raw));
        }
        return segments;
    }

    public static string ToRichText(string text)
    {
        // Escape Unity's own rich-text delimiters before inserting real tags below, so stray
        // '<'/'>' in Claude's output (HTML snippets, generics like List<string>) can't be
        // misparsed as formatting or break the tags this method adds.
        text = text.Replace("<", "＜").Replace(">", "＞");
        text = Bold.Replace(text, "<b>$1</b>");
        text = InlineCode.Replace(text, "<color=#E0B15A>$1</color>");
        text = BulletLine.Replace(text, "• ");
        return text;
    }
}
