namespace FileManagerServer.Models
{
    public class FileMetadata
    {
        public string Name { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public bool IsPublic { get; set; }
        public string Path { get; set; }
    }
}