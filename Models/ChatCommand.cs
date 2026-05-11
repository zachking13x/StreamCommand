namespace StreamCommand.Models;

public class ChatCommand
{
    public string Trigger   { get; set; } = "";   // e.g. "!discord"
    public string Response  { get; set; } = "";   // what the bot sends back
    public bool   IsEnabled { get; set; } = true;
}
