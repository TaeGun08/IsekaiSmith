public class ChatMessage
{
    public readonly string Role;
    public readonly string Text;

    // True for app-generated notices (e.g. "AI 전환됨") that aren't part of the actual AI
    // conversation - rendered as a centered pill instead of a left/right speech bubble, see
    // AiCompanionWindow.AddChatBubble.
    public readonly bool IsSystemNotice;

    public ChatMessage(string role, string text, bool isSystemNotice = false)
    {
        Role = role;
        Text = text;
        IsSystemNotice = isSystemNotice;
    }

    public static ChatMessage SystemNotice(string text)
    {
        return new ChatMessage("System", text, true);
    }
}
