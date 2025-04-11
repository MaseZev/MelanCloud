// VoiceChannelStore.cs
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FileManagerServer.Models;

namespace FileManagerServer.Data
{
    public class VoiceChannelStore
    {
        private const string ChannelsFile = "voice_channels.json";
        private static readonly object _lock = new object();

        public static List<VoiceChannel> LoadChannels()
        {
            lock (_lock)
            {
                if (!File.Exists(ChannelsFile))
                    return new List<VoiceChannel>();

                var json = File.ReadAllText(ChannelsFile);
                return JsonSerializer.Deserialize<List<VoiceChannel>>(json) ?? new List<VoiceChannel>();
            }
        }

        public static void SaveChannels(List<VoiceChannel> channels)
        {
            lock (_lock)
            {
                var json = JsonSerializer.Serialize(channels, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ChannelsFile, json);
            }
        }
    }
}