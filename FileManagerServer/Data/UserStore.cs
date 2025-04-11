using System.Xml;
using FileManagerServer.Models;
using Newtonsoft.Json;
using Formatting = Newtonsoft.Json.Formatting;

namespace FileManagerServer.Data
{
    public static class UserStore
    {
        private static readonly string BasePath = Path.Combine(Directory.GetCurrentDirectory(), "UserData");
        private static readonly string UsersFile = Path.Combine(BasePath, "users.json");
        private static readonly object LockObject = new object();

        static UserStore()
        {
            lock (LockObject)
            {
                if (!Directory.Exists(BasePath))
                    Directory.CreateDirectory(BasePath);
                if (!File.Exists(UsersFile))
                    SaveUsers(new List<User>());
            }
        }

        public static List<User> LoadUsers()
        {
            lock (LockObject)
            {
                try
                {
                    if (!File.Exists(UsersFile))
                        return new List<User>();

                    var json = File.ReadAllText(UsersFile);
                    var users = JsonConvert.DeserializeObject<List<User>>(json);
                    return users ?? new List<User>();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading users: {ex.Message}");
                    return new List<User>();
                }
            }
        }

        public static void SaveUsers(List<User> users)
        {
            lock (LockObject)
            {
                try
                {
                    var json = JsonConvert.SerializeObject(users, Formatting.Indented);
                    File.WriteAllText(UsersFile, json);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving users: {ex.Message}");
                    throw;
                }
            }
        }

        public static string GetUserSpaceDirectory(string username, string spaceName)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(spaceName))
                throw new ArgumentException("Username and spaceName cannot be empty.");

            var path = Path.Combine(BasePath, username, spaceName);
            lock (LockObject)
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            return path;
        }

        public static bool CanUploadFile(string username, string spaceName, long fileSize)
        {
            if (fileSize < 0)
                return false;

            var users = LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null)
                return false;

            var space = user.Spaces.FirstOrDefault(s => s.Name == spaceName);
            if (space == null)
                return false;

            long totalUsedStorage = user.Spaces.Sum(s => s.UsedStorage);
            long limit = user.IsPremium ? 6_000_000_000 : 2_000_000_000;
            return totalUsedStorage + fileSize <= limit;
        }

        public static void UpdateStorage(string username, string spaceName, long fileSize)
        {
            if (fileSize < 0)
                throw new ArgumentException("File size cannot be negative.");

            var users = LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null)
                throw new InvalidOperationException($"User '{username}' not found.");

            var space = user.Spaces.FirstOrDefault(s => s.Name == spaceName);
            if (space == null)
                throw new InvalidOperationException($"Space '{spaceName}' not found for user '{username}'.");

            lock (LockObject)
            {
                space.UsedStorage += fileSize;
                SaveUsers(users);
            }
        }
        public static bool IsUserBlocked(string username)
        {
            var users = LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);

            if (user == null || !user.IsBlocked)
                return false;

            if (user.BlockedUntil.HasValue)
            {
                return DateTime.UtcNow < user.BlockedUntil.Value;
            }

            return true;
        }

        public static string GetUserRootDirectory(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username cannot be empty.");

            var path = Path.Combine(BasePath, username);
            lock (LockObject)
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }
            return path;
        }
    }
}