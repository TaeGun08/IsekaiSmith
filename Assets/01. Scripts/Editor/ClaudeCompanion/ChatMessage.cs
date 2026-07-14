public class ChatMessage
{
    public readonly string Role;
    public readonly string Text;

    public ChatMessage(string role, string text)
    {
        Role = role;
        Text = text;
    }
}
