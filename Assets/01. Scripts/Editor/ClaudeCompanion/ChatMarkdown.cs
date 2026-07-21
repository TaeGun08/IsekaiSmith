using System.Collections.Generic;
using System.Text.RegularExpressions;

// Deliberately not a full CommonMark implementation - Unity's IMGUI rich text only understands
// a handful of inline tags (<b>, <i>, <color>), so this only covers what Claude's CLI output
// actually uses in practice: fenced code blocks, **bold**, `inline code`, "- "/"* " bullets, and
// (2026-07-16) an [[image: path]] marker so a generated/saved file can render inline in the
// chat instead of only living on disk.
public static class ChatMarkdown
{
    public enum SegmentKind
    {
        Text,
        Code,
        Image,
    }

    public readonly struct Segment
    {
        public readonly SegmentKind Kind;
        // Body text for Text/Code; the referenced file path for Image.
        public readonly string Text;

        public Segment(SegmentKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }
    }

    private static readonly Regex CombinedBlock = new Regex(
        "```[a-zA-Z0-9]*\r?\n?(?<code>.*?)```" + "|" + @"\[\[image:\s*(?<image>[^\]]+?)\s*\]\]",
        RegexOptions.Singleline);
    private static readonly Regex Bold = new Regex(@"\*\*(.+?)\*\*");
    private static readonly Regex InlineCode = new Regex(@"`([^`\n]+?)`");
    private static readonly Regex BulletLine = new Regex(@"^[ \t]*[-*][ \t]+", RegexOptions.Multiline);

    // Splits raw text into plain-text/fenced-code-block/image-marker segments so the caller can
    // render each appropriately (code in a non-rich-text monospace-ish block so markdown
    // conversion can't mangle it, images as an actual Image element instead of raw text).
    public static List<Segment> Split(string raw)
    {
        List<Segment> segments = new List<Segment>();
        int lastEnd = 0;
        foreach (Match match in CombinedBlock.Matches(raw))
        {
            if (match.Index > lastEnd)
            {
                segments.Add(new Segment(SegmentKind.Text, raw.Substring(lastEnd, match.Index - lastEnd)));
            }
            if (match.Groups["code"].Success)
            {
                segments.Add(new Segment(SegmentKind.Code, match.Groups["code"].Value.Trim('\n', '\r')));
            }
            else if (match.Groups["image"].Success)
            {
                segments.Add(new Segment(SegmentKind.Image, match.Groups["image"].Value.Trim()));
            }
            lastEnd = match.Index + match.Length;
        }
        if (lastEnd < raw.Length)
        {
            segments.Add(new Segment(SegmentKind.Text, raw.Substring(lastEnd)));
        }
        if (segments.Count == 0)
        {
            segments.Add(new Segment(SegmentKind.Text, raw));
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
