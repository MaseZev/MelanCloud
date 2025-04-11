// VoiceChannel.cs
namespace FileManagerServer.Models
{
    public class VoiceChannel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Creator { get; set; }
        public List<string> Participants { get; set; } = new List<string>();
        public DateTime CreatedAt { get; set; }
    }
}