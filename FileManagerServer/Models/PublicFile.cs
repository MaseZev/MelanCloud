namespace FileManagerServer.Models
{
    public class PublicFile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Username { get; set; }
        public string SpaceName { get; set; }
        public string FilePath { get; set; }
        public string Description { get; set; }
        public DateTime UploadedDate { get; set; } = DateTime.UtcNow;
        public long DownloadCount { get; set; }
        public List<string> Likes { get; set; } = new List<string>();
        public List<Comment> Comments { get; set; } = new List<Comment>();
        public long FileSize { get; set; } // Размер файла в байтах
        public string ContentType { get; set; } // MIME-тип файла для предпросмотра
    }

    public class Comment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Username { get; set; }
        public string Text { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;
    }
}