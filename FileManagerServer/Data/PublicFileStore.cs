using FileManagerServer.Models;
using Newtonsoft.Json;

namespace FileManagerServer.Data
{
    public static class PublicFileStore
    {
        private static readonly string FilePath = Path.Combine(Directory.GetCurrentDirectory(), "public_files.json");
        private static List<PublicFile> PublicFiles = LoadPublicFiles();

        public static List<PublicFile> GetAll() => PublicFiles;

        public static PublicFile GetById(string id) => PublicFiles.FirstOrDefault(f => f.Id == id);

        public static void SavePublicFiles()
        {
            var json = JsonConvert.SerializeObject(PublicFiles, Formatting.Indented);
            File.WriteAllText(FilePath, json);
        }

        private static List<PublicFile> LoadPublicFiles()
        {
            if (!File.Exists(FilePath)) return new List<PublicFile>();
            var json = File.ReadAllText(FilePath);
            return JsonConvert.DeserializeObject<List<PublicFile>>(json) ?? new List<PublicFile>();
        }
    }
}