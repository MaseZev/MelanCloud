using System.ComponentModel.DataAnnotations;

namespace FileManagerServer.Models
{
    public class Space
    {
        public string Name { get; set; }
        public bool IsPublic { get; set; }
        public long UsedStorage { get; set; }
        public List<FileMetadata> Files { get; set; } = new List<FileMetadata>();
    }

    public class NotificationSettings
    {
        public bool EmailNotifications { get; set; }
        public bool BrowserNotifications { get; set; }
        public bool OnUpload { get; set; }
        public bool OnDownload { get; set; }
        public bool OnSpaceFull { get; set; }
    }

    public class User
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool IsPremium { get; set; } = false;
        public bool IsAdmin { get; set; } = false;
        public bool IsBlocked { get; set; } = false;
        public DateTime? BlockedUntil { get; set; }
        public string BlockReason { get; set; }
        public List<Space> Spaces { get; set; } = new List<Space>();
        public NotificationSettings Notifications { get; set; } = new NotificationSettings();
        public bool TwoFactorEnabled { get; set; }
        public string TwoFactorSecret { get; set; }
        public List<string> BackupCodes { get; set; } = new List<string>();
        public RecycleBin RecycleBin { get; set; } = new RecycleBin();

        // Add these new properties
        public List<string> Subscribers { get; set; } = new List<string>();
        public List<string> Subscriptions { get; set; } = new List<string>();
    }

    public class RecycleBin
    {
        public List<RecycledFile> Files { get; set; } = new List<RecycledFile>();
        public int RetentionDays { get; set; } = 30;
    }

    public class RecycledFile
    {
        public string OriginalPath { get; set; }
        public string RecyclePath { get; set; }
        public DateTime DeletedDate { get; set; }
        public long Size { get; set; }
        public string SpaceName { get; set; }
    }

    public class LoginModel
    {
        [Required(ErrorMessage = "Username is required")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; }
    }

    public class RegisterModel
    {
        [Required(ErrorMessage = "Username is required")]
        [StringLength(20, MinimumLength = 4, ErrorMessage = "Username must be between 4 and 20 characters")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters")]
        public string Password { get; set; }
    }
}