using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using FileManagerServer.Data;
using FileManagerServer.Models;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http.Headers;
using OtpNet;

namespace FileManagerServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FileManagerController : ControllerBase
    {
        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterModel registerModel)
        {
            var users = UserStore.LoadUsers();

            // Проверка существования пользователя
            if (users.Any(u => u.Username == registerModel.Username))
                return BadRequest("Пользователь уже существует");

            // Создаем нового пользователя с дефолтными значениями
            var newUser = new User
            {
                Username = registerModel.Username,
                Password = registerModel.Password,
                IsPremium = false, // По умолчанию не премиум
                IsAdmin = false,   // По умолчанию не админ
                IsBlocked = false, // По умолчанию не заблокирован
                Spaces = new List<Space> { new Space { Name = "Private", IsPublic = false } },
                Subscribers = new List<string>(), // Инициализация списка подписчиков
                Subscriptions = new List<string>()
            };

            users.Add(newUser);
            UserStore.SaveUsers(users);
            UserStore.GetUserSpaceDirectory(newUser.Username, "Private");

            return Ok("Регистрация успешна");
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginModel loginModel)
        {
            var users = UserStore.LoadUsers();
            var existingUser = users.FirstOrDefault(u => u.Username == loginModel.Username && u.Password == loginModel.Password);

            if (existingUser == null)
                return Unauthorized("Неверный логин или пароль");

            if (existingUser.IsBlocked)
            {
                var message = existingUser.BlockedUntil.HasValue ?
                    $"Ваш аккаунт заблокирован до {existingUser.BlockedUntil.Value.ToLocalTime()} по причине: {existingUser.BlockReason}" :
                    $"Ваш аккаунт заблокирован по причине: {existingUser.BlockReason}";
                return Unauthorized(message);
            }

            Console.WriteLine($"Login for {loginModel.Username}, 2FA: {existingUser.TwoFactorEnabled}");

            if (existingUser.TwoFactorEnabled)
            {
                return Ok(new
                {
                    Requires2FA = true,
                    Message = "Требуется двухфакторная аутентификация"
                });
            }

            return Ok(new
            {
                existingUser.Username,
                existingUser.IsAdmin,
                existingUser.IsPremium
            });
        }

        [HttpPost("login-with-2fa")]
        public IActionResult LoginWith2FA([FromBody] LoginWith2FAModel model)
        {
            var users = UserStore.LoadUsers();
            var existingUser = users.FirstOrDefault(u => u.Username == model.Username && u.Password == model.Password);

            if (existingUser == null)
                return Unauthorized("Неверный логин или пароль");

            if (existingUser.IsBlocked)
            {
                var message = existingUser.BlockedUntil.HasValue ?
                    $"Ваш аккаунт заблокирован до {existingUser.BlockedUntil.Value.ToLocalTime()} по причине: {existingUser.BlockReason}" :
                    $"Ваш аккаунт заблокирован по причине: {existingUser.BlockReason}";
                return Unauthorized(message);
            }

            if (!existingUser.TwoFactorEnabled)
                return BadRequest("2FA is not enabled for this account");

            // Verify TOTP or backup code
            if (existingUser.BackupCodes.Contains(model.TwoFACode))
            {
                existingUser.BackupCodes.Remove(model.TwoFACode);
            }
            else
            {
                var totp = new Totp(Base32Encoding.ToBytes(existingUser.TwoFactorSecret));
                long timeStepMatched;
                if (!totp.VerifyTotp(model.TwoFACode, out timeStepMatched,
                    new VerificationWindow(1, 1)))
                {
                    return Unauthorized("Неверный код двухфакторной аутентификации");
                }
            }

            UserStore.SaveUsers(users);
            return Ok(new
            {
                existingUser.Username,
                existingUser.IsAdmin,
                existingUser.IsPremium
            });
        }

        [HttpGet("2fa-status")]
        public IActionResult Get2FAStatus([FromQuery] string username)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");

            return Ok(new
            {
                Enabled = user.TwoFactorEnabled
            });
        }

        private string RandomCode(int length)
        {
            var random = new Random();
            return string.Join("", Enumerable.Range(0, length)
                .Select(_ => random.Next(0, 10).ToString()));
        }

        [HttpPost("create-space")]
        public IActionResult CreateSpace([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] bool isPublic)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null) return Unauthorized();

            // Проверяем, не превышает ли пользователь лимит пространств (если есть такой лимит)
            if (user.Spaces.Count >= 10) // Пример: максимум 10 пространств
                return BadRequest("Достигнут лимит пространств. Удалите одно из существующих.");

            if (user.Spaces.Any(s => s.Name == spaceName))
                return BadRequest("Пространство с таким именем уже существует");

            user.Spaces.Add(new Space { Name = spaceName, IsPublic = isPublic });
            UserStore.SaveUsers(users);
            UserStore.GetUserSpaceDirectory(username, spaceName);
            return Ok($"Пространство '{spaceName}' создано");
        }

        private bool CheckStorageLimit(User user, long additionalSize)
        {
            long totalUsed = user.Spaces.Sum(s => s.UsedStorage);
            long limit = user.IsPremium ? 6_000_000_000 : 2_000_000_000;
            return (totalUsed + additionalSize) <= limit;
        }

        [HttpGet("userinfo")]
        public IActionResult GetUserInfo([FromQuery] string username)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);

            // Проверка существования пользователя
            if (user == null)
                return Unauthorized("User not found");

            // Проверка блокировки
            if (user.IsBlocked)
            {
                var message = user.BlockedUntil.HasValue ?
                    $"Account blocked until {user.BlockedUntil.Value.ToLocalTime()}. Reason: {user.BlockReason}" :
                    $"Account blocked. Reason: {user.BlockReason}";
                return Unauthorized(message);
            }

            // Формируем ответ
            var response = new
            {
                user.Username,
                user.IsPremium,
                user.IsAdmin,
                UsedStorage = user.Spaces.Sum(s => s.UsedStorage),
                StorageLimit = user.IsPremium ? 6_000_000_000 : 2_000_000_000,
                Spaces = user.Spaces.Select(s => new
                {
                    s.Name,
                    s.IsPublic,
                    s.UsedStorage,
                    FileCount = s.Files.Count
                }),
                AccountStatus = user.IsBlocked ? "Blocked" : "Active",
                BlockReason = user.IsBlocked ? user.BlockReason : null,
                BlockedUntil = user.IsBlocked ? user.BlockedUntil : null
            };

            return Ok(response);
        }

        [HttpPost("upload")]
        [RequestSizeLimit(100_000_000)]
        public async Task<IActionResult> UploadFile([FromForm] IFormFile file, [FromQuery] string username, [FromQuery] string spaceName)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var space = user.Spaces.FirstOrDefault(s => s.Name == spaceName);
            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);

            // Декодируем имя файла для корректной обработки русских символов
            var fileName = WebUtility.UrlDecode(file.FileName);
            var filePath = Path.Combine(userDir, fileName);

            bool fileExists = System.IO.File.Exists(filePath);
            long existingFileSize = 0;
            FileMetadata existingMetadata = null;

            if (fileExists)
            {
                var fileInfo = new FileInfo(filePath);
                existingFileSize = fileInfo.Length;
                existingMetadata = space.Files.FirstOrDefault(f => f.Name == fileName);
            }

            long newTotalSize = user.Spaces.Sum(s => s.UsedStorage) - existingFileSize + file.Length;
            long storageLimit = user.IsPremium ? 6_000_000_000 : 2_000_000_000;

            if (newTotalSize > storageLimit)
            {
                double limitInGB = storageLimit / 1_000_000_000.0;
                return BadRequest($"Превышен лимит хранилища ({limitInGB} ГБ). Удалите файлы или купите Premium.");
            }

            try
            {
                if (Path.GetExtension(fileName).ToLower() == ".mp4")
                {
                    var tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".mp4");
                    using (var stream = new FileStream(tempFilePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }
                    var optimizedFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "_optimized.mp4");
                    var ffmpegProcess = new System.Diagnostics.Process
                    {
                        StartInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = $"-i \"{tempFilePath}\" -movflags faststart -c:v libx264 -c:a aac -b:a 128k \"{optimizedFilePath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };
                    ffmpegProcess.Start();
                    ffmpegProcess.WaitForExit();

                    if (ffmpegProcess.ExitCode != 0)
                    {
                        System.IO.File.Delete(tempFilePath);
                        return BadRequest("Ошибка обработки MP4 файла");
                    }

                    if (fileExists)
                    {
                        System.IO.File.Delete(filePath);
                    }

                    System.IO.File.Move(optimizedFilePath, filePath);
                    System.IO.File.Delete(tempFilePath);
                    var optimizedFileSize = new FileInfo(filePath).Length;
                    space.UsedStorage = space.UsedStorage - existingFileSize + optimizedFileSize;

                    if (existingMetadata != null)
                    {
                        existingMetadata.Size = optimizedFileSize;
                        existingMetadata.Modified = DateTime.UtcNow;
                    }
                    else
                    {
                        space.Files.Add(new FileMetadata
                        {
                            Name = fileName,
                            Size = optimizedFileSize,
                            Modified = DateTime.UtcNow,
                            IsPublic = false,
                            Path = fileName
                        });
                    }
                }
                else
                {
                    if (fileExists)
                        System.IO.File.Delete(filePath);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    space.UsedStorage = space.UsedStorage - existingFileSize + file.Length;

                    if (existingMetadata != null)
                    {
                        existingMetadata.Size = file.Length;
                        existingMetadata.Modified = DateTime.UtcNow;
                    }
                    else
                    {
                        space.Files.Add(new FileMetadata
                        {
                            Name = fileName,
                            Size = file.Length,
                            Modified = DateTime.UtcNow,
                            IsPublic = false,
                            Path = fileName
                        });
                    }
                }

                UserStore.SaveUsers(users);
                return Ok(fileExists ? "Файл заменен" : "Файл загружен");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка при загрузке файла: {ex.Message}");
            }
        }

        [HttpPost("copy-file")]
        public IActionResult CopyFile([FromBody] FileMoveRequest request)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == request.Username);
            if (user == null || !user.Spaces.Any(s => s.Name == request.FromSpace) || !user.Spaces.Any(s => s.Name == request.ToSpace))
                return Unauthorized();

            var fromSpace = user.Spaces.FirstOrDefault(s => s.Name == request.FromSpace);
            var toSpace = user.Spaces.FirstOrDefault(s => s.Name == request.ToSpace);
            var fromPath = Path.Combine(UserStore.GetUserSpaceDirectory(request.Username, request.FromSpace), request.Filename);
            var toPath = Path.Combine(UserStore.GetUserSpaceDirectory(request.Username, request.ToSpace), request.Filename);

            if (!System.IO.File.Exists(fromPath))
                return NotFound("Source file not found");

            if (!CheckStorageLimit(user, new FileInfo(fromPath).Length))
                return BadRequest("Not enough storage space in target space");

            System.IO.File.Copy(fromPath, toPath, true);
            toSpace.UsedStorage += new FileInfo(fromPath).Length;
            UserStore.SaveUsers(users);
            return Ok("File copied successfully");
        }

        [HttpPost("move-file")]
        public IActionResult MoveFile([FromBody] FileMoveRequest request)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == request.Username);
            if (user == null || !user.Spaces.Any(s => s.Name == request.FromSpace) || !user.Spaces.Any(s => s.Name == request.ToSpace))
                return Unauthorized();

            var fromSpace = user.Spaces.First(s => s.Name == request.FromSpace);
            var toSpace = user.Spaces.First(s => s.Name == request.ToSpace);
            var fromPath = Path.Combine(UserStore.GetUserSpaceDirectory(request.Username, request.FromSpace), request.OldFilePath);
            var toPath = Path.Combine(UserStore.GetUserSpaceDirectory(request.Username, request.ToSpace), request.NewFilePath);

            if (!System.IO.File.Exists(fromPath))
                return NotFound("Source file not found");

            // Проверка лимита хранилища, если перемещение между пространствами
            if (request.FromSpace != request.ToSpace)
            {
                var fileSize = new FileInfo(fromPath).Length;
                var existingSize = System.IO.File.Exists(toPath) ? new FileInfo(toPath).Length : 0;
                if (!CheckStorageLimit(user, fileSize - existingSize))
                    return BadRequest("Not enough storage space in target space");
            }

            // Создаем целевую директорию, если её нет
            var toDir = Path.GetDirectoryName(toPath);
            if (!string.IsNullOrEmpty(toDir) && !Directory.Exists(toDir))
            {
                Directory.CreateDirectory(toDir);
            }

            // Если файл уже существует в целевой локации, удаляем его
            if (System.IO.File.Exists(toPath))
                System.IO.File.Delete(toPath);

            // Перемещаем файл
            System.IO.File.Move(fromPath, toPath);

            // Обновляем метаданные и хранилище
            var fileSizeMoved = new FileInfo(toPath).Length;
            var fileMetadata = fromSpace.Files.FirstOrDefault(f => f.Path == request.OldFilePath);
            if (fileMetadata != null)
            {
                if (request.FromSpace == request.ToSpace)
                {
                    // Перемещение внутри пространства: обновляем только путь
                    fileMetadata.Path = request.NewFilePath;
                }
                else
                {
                    // Перемещение между пространствами: удаляем из старого и добавляем в новое
                    fromSpace.Files.Remove(fileMetadata);
                    fromSpace.UsedStorage -= fileSizeMoved;
                    toSpace.Files.Add(new FileMetadata
                    {
                        Name = Path.GetFileName(request.NewFilePath),
                        Size = fileSizeMoved,
                        Modified = DateTime.UtcNow,
                        IsPublic = fileMetadata.IsPublic,
                        Path = request.NewFilePath
                    });
                    toSpace.UsedStorage += fileSizeMoved;
                }
            }

            UserStore.SaveUsers(users);
            return Ok("File moved successfully");
        }

        [HttpGet("search-content")]
        public IActionResult SearchContent([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string query)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);
            if (!Directory.Exists(userDir))
                return NotFound("Space directory not found");

            var textFileExtensions = new[] { ".txt", ".json", ".xml", ".csv", ".md", ".html", ".css", ".js" };
            var matchingFiles = new List<object>();

            try
            {
                var files = Directory.GetFiles(userDir)
                    .Where(file => textFileExtensions.Contains(Path.GetExtension(file).ToLower()))
                    .Select(filePath =>
                    {
                        var fileInfo = new FileInfo(filePath);
                        try
                        {
                            var content = System.IO.File.ReadAllText(filePath, Encoding.UTF8).ToLower();
                            if (content.Contains(query.ToLower()))
                            {
                                return new
                                {
                                    Name = Path.GetFileName(filePath),
                                    Size = fileInfo.Length,
                                    Modified = fileInfo.LastWriteTimeUtc
                                };
                            }
                            return null;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error reading file {filePath}: {ex.Message}");
                            return null;
                        }
                    })
                    .Where(file => file != null)
                    .ToList();

                matchingFiles.AddRange(files);
                return Ok(matchingFiles);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error searching files: {ex.Message}");
            }
        }

        [HttpGet("files")]
        public IActionResult GetFiles([FromQuery] string username, [FromQuery] string spaceName)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var space = user.Spaces.First(s => s.Name == spaceName);
            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);

            // Получаем все файлы
            var actualFiles = Directory.GetFiles(userDir, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .ToList();

            // Получаем все папки
            var actualFolders = Directory.GetDirectories(userDir, "*", SearchOption.AllDirectories)
                .Select(d => new DirectoryInfo(d))
                .ToList();

            // Обновляем метаданные файлов
            foreach (var file in actualFiles)
            {
                var relativePath = Path.GetRelativePath(userDir, file.FullName);
                var metadata = space.Files.FirstOrDefault(f => f.Path == relativePath);
                if (metadata == null)
                {
                    space.Files.Add(new FileMetadata
                    {
                        Name = file.Name,
                        Size = file.Length,
                        Modified = file.LastWriteTimeUtc,
                        IsPublic = false,
                        Path = relativePath
                    });
                }
                else
                {
                    metadata.Size = file.Length;
                    metadata.Modified = file.LastWriteTimeUtc;
                }
            }

            // Удаляем метаданные для несуществующих файлов
            space.Files.RemoveAll(f => !actualFiles.Any(af => Path.GetRelativePath(userDir, af.FullName) == f.Path));

            UserStore.SaveUsers(users);

            // Формируем результат: файлы и папки
            var result = new List<object>();

            // Добавляем файлы
            result.AddRange(space.Files.Select(f => new
            {
                Type = "file",
                Name = f.Name,
                Size = f.Size,
                Modified = f.Modified,
                IsPublic = f.IsPublic,
                Path = f.Path
            }));

            // Добавляем папки
            result.AddRange(actualFolders.Select(d => new
            {
                Type = "folder",
                Name = d.Name,
                Size = 0L, // Папки не имеют размера в данном случае
                Modified = d.LastWriteTimeUtc,
                IsPublic = false, // Можно добавить логику для публичности папок
                Path = Path.GetRelativePath(userDir, d.FullName)
            }));

            return Ok(result);
        }

        [HttpPost("rename-folder")]
        public IActionResult RenameFolder([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string oldFolderPath, [FromQuery] string newFolderPath)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);
            var oldFullPath = Path.Combine(userDir, oldFolderPath);
            var newFullPath = Path.Combine(userDir, newFolderPath);

            if (!Directory.Exists(oldFullPath))
                return NotFound("Folder not found");

            if (Directory.Exists(newFullPath))
                return BadRequest("Folder with new name already exists");

            try
            {
                Directory.Move(oldFullPath, newFullPath);
                var space = user.Spaces.First(s => s.Name == spaceName);
                foreach (var file in space.Files.Where(f => f.Path.StartsWith(oldFolderPath)))
                {
                    file.Path = Path.Combine(newFolderPath, file.Path.Substring(oldFolderPath.Length + 1));
                }
                UserStore.SaveUsers(users);
                return Ok("Folder renamed successfully");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error renaming folder: {ex.Message}");
            }
        }

        [HttpPost("delete-folder")]
        public IActionResult DeleteFolder([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string folderPath)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);
            var fullPath = Path.Combine(userDir, folderPath);

            if (!Directory.Exists(fullPath))
                return NotFound("Folder not found");

            try
            {
                var totalSize = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
                Directory.Delete(fullPath, true);
                var space = user.Spaces.First(s => s.Name == spaceName);
                space.Files.RemoveAll(f => f.Path.StartsWith(folderPath));
                space.UsedStorage -= totalSize;
                UserStore.SaveUsers(users);
                return Ok("Folder deleted successfully");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error deleting folder: {ex.Message}");
            }
        }

        [HttpGet("download")]
        public IActionResult DownloadFile([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string filename)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);
            var filePath = Path.Combine(userDir, filename);
            if (!System.IO.File.Exists(filePath))
                return NotFound();

            var fileInfo = new FileInfo(filePath);
            long fileLength = fileInfo.Length;
            string contentType = Path.GetExtension(filename).ToLower() == ".mp4" ? "video/mp4" : "application/octet-stream";

            string rangeHeader = Request.Headers["Range"];
            if (!string.IsNullOrEmpty(rangeHeader))
            {
                var range = rangeHeader.Replace("bytes=", "").Split('-');
                long start = long.Parse(range[0]);
                long end = range[1].Length > 0 ? long.Parse(range[1]) : fileLength - 1;

                if (start < 0 || start >= fileLength || end < start || end >= fileLength)
                    return StatusCode(416, "Requested Range Not Satisfiable");

                Response.StatusCode = 206;
                Response.Headers.Add("Content-Range", $"bytes {start}-{end}/{fileLength}");
                Response.Headers.Add("Accept-Ranges", "bytes");
                Response.Headers.Add("Content-Length", (end - start + 1).ToString());

                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    fileStream.Seek(start, SeekOrigin.Begin);
                    var buffer = new byte[end - start + 1];
                    fileStream.Read(buffer, 0, buffer.Length);
                    return File(buffer, contentType, enableRangeProcessing: true);
                }
            }
            else
            {
                Response.Headers.Add("Accept-Ranges", "bytes");
                Response.Headers.Add("Content-Length", fileLength.ToString());
                return PhysicalFile(filePath, contentType, filename, enableRangeProcessing: true);
            }
        }

        [HttpGet("preview")]
        public IActionResult PreviewFile([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string filename)
        {
            // Декодируем имя файла из URL
            var decodedFilename = WebUtility.UrlDecode(filename);

            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);
            var filePath = Path.Combine(userDir, decodedFilename); // Используем декодированное имя
            if (!System.IO.File.Exists(filePath))
                return NotFound();

            var fileInfo = new FileInfo(filePath);
            string contentType = GetContentType(decodedFilename);

            // Для изображений и видео возвращаем встроенный просмотр
            if (contentType.StartsWith("image/") || contentType.StartsWith("video/") || contentType.StartsWith("audio/"))
            {
                // Правильное формирование Content-Disposition для UTF-8 имен
                var contentDisposition = new ContentDispositionHeaderValue("inline")
                {
                    FileNameStar = decodedFilename,
                    FileName = Uri.EscapeDataString(decodedFilename) // fallback для старых клиентов
                };
                Response.Headers.Add("Content-Disposition", contentDisposition.ToString());

                return PhysicalFile(filePath, contentType, enableRangeProcessing: true);
            }

            // Для текстовых файлов возвращаем содержимое
            if (contentType.StartsWith("text/") ||
                decodedFilename.EndsWith(".txt") ||
                decodedFilename.EndsWith(".json") ||
                decodedFilename.EndsWith(".xml"))
            {
                return Content(System.IO.File.ReadAllText(filePath), contentType);
            }

            // Для остальных типов предлагаем скачать
            return RedirectToAction("DownloadFile", new
            {
                username,
                spaceName,
                filename = WebUtility.UrlEncode(decodedFilename)
            });
        }

        private string GetContentType(string filename)
        {
            string extension = Path.GetExtension(filename).ToLower();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mov" => "video/quicktime",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".rar" => "application/x-rar-compressed",
                ".7z" => "application/x-7z-compressed",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".txt" => "text/plain",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                _ => "application/octet-stream"
            };
        }

        [HttpGet("share-link")]
        public IActionResult GetShareLink([FromQuery] string username, [FromQuery] string spaceName)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null) return Unauthorized();

            var space = user.Spaces.FirstOrDefault(s => s.Name == spaceName);
            if (space == null || !space.IsPublic)
                return BadRequest("Пространство не найдено или не является публичным");

            var link = $"https://mircord.online/api/filemanager/public?username={username}&space={spaceName}";
            return Ok(new { Link = link });
        }

        [HttpGet("public")]
        public IActionResult GetPublicFiles([FromQuery] string username, [FromQuery] string space)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");

            var spaceObj = user.Spaces.FirstOrDefault(s => s.Name == space && s.IsPublic);
            if (spaceObj == null) return Unauthorized("Пространство не публичное или не существует");

            var userDir = UserStore.GetUserSpaceDirectory(username, space);
            var files = Directory.GetFiles(userDir).Select(Path.GetFileName).ToList();

            var htmlContent = new System.Text.StringBuilder();
            htmlContent.AppendLine("<!DOCTYPE html>");
            htmlContent.AppendLine("<html lang='en'>");
            htmlContent.AppendLine("<head>");
            htmlContent.AppendLine("<meta charset='UTF-8'>");
            htmlContent.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            htmlContent.AppendLine("<title>Public Files - MelanCloud</title>");
            htmlContent.AppendLine("<link href='https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&display=swap' rel='stylesheet'>");
            htmlContent.AppendLine("<style>");
            htmlContent.AppendLine(":root { --bg-color: #121212; --surface-color: #1E1E1E; --primary-color: #FFD700; --primary-hover: #FFC107; --text-primary: #E0E0E0; --text-secondary: #A0A0A0; --border-color: #333333; --card-bg: #252525; --progress-bg: #333333; }");
            htmlContent.AppendLine("body { font-family: 'Inter', sans-serif; margin: 0; padding: 20px; background-color: var(--bg-color); color: var(--text-primary); line-height: 1.6; }");
            htmlContent.AppendLine(".container { max-width: 1200px; margin: 0 auto; padding: 20px; }");
            htmlContent.AppendLine("header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 30px; padding-bottom: 20px; border-bottom: 1px solid var(--border-color); }");
            htmlContent.AppendLine("h1 { color: var(--primary-color); margin: 0; font-weight: 600; }");
            htmlContent.AppendLine(".logo { display: flex; align-items: center; gap: 10px; font-size: 24px; font-weight: 600; }");
            htmlContent.AppendLine(".btn { background-color: var(--primary-color); color: #1a1a1a; padding: 12px 24px; border: none; border-radius: 8px; cursor: pointer; font-weight: 500; font-size: 16px; transition: all 0.3s ease; display: inline-flex; align-items: center; gap: 8px; }");
            htmlContent.AppendLine(".btn:hover { background-color: var(--primary-hover); transform: translateY(-2px); box-shadow: 0 4px 12px rgba(255, 215, 0, 0.2); }");
            htmlContent.AppendLine(".file-list { display: grid; grid-template-columns: repeat(auto-fill, minmax(300px, 1fr)); gap: 15px; margin-top: 30px; }");
            htmlContent.AppendLine(".file-card { background-color: var(--card-bg); border-radius: 10px; padding: 20px; border: 1px solid var(--border-color); transition: all 0.3s ease; }");
            htmlContent.AppendLine(".file-card:hover { transform: translateY(-3px); box-shadow: 0 6px 16px rgba(0, 0, 0, 0.2); border-color: var(--primary-color); }");
            htmlContent.AppendLine(".file-name { font-weight: 500; margin-bottom: 8px; word-break: break-all; }");
            htmlContent.AppendLine(".file-actions { display: flex; gap: 10px; margin-top: 15px; }");
            htmlContent.AppendLine(".action-btn { background-color: var(--surface-color); color: var(--text-primary); border: 1px solid var(--border-color); padding: 8px 12px; border-radius: 6px; cursor: pointer; font-size: 14px; transition: all 0.2s ease; }");
            htmlContent.AppendLine(".action-btn:hover { background-color: var(--border-color); }");
            htmlContent.AppendLine("#progressContainer { margin: 30px 0; display: none; background-color: var(--surface-color); padding: 20px; border-radius: 10px; text-align: center; }");
            htmlContent.AppendLine("#progressBar { width: 100%; height: 10px; margin: 10px 0; background-color: var(--progress-bg); border-radius: 5px; }");
            htmlContent.AppendLine("#progressBar::-webkit-progress-bar { background-color: var(--progress-bg); border-radius: 5px; }");
            htmlContent.AppendLine("#progressBar::-webkit-progress-value { background-color: var(--primary-color); border-radius: 5px; }");
            htmlContent.AppendLine(".file-icon { font-size: 24px; margin-right: 10px; color: var(--primary-color); }");
            htmlContent.AppendLine(".user-info { font-size: 14px; color: var(--text-secondary); margin-top: 10px; }");
            htmlContent.AppendLine("</style>");
            htmlContent.AppendLine("</head>");
            htmlContent.AppendLine("<body>");
            htmlContent.AppendLine("<div class='container'>");
            htmlContent.AppendLine("<header>");
            htmlContent.AppendLine("<div class='logo'>");
            htmlContent.AppendLine("<span>☁</span>");
            htmlContent.AppendLine($"<h1>Files in {space} <small style='color: var(--text-secondary); font-size: 16px;'>(by {username})</small></h1>");
            htmlContent.AppendLine("</div>");
            htmlContent.AppendLine("<div>");
            htmlContent.AppendLine("<button class='btn' onclick='downloadAll()'>");
            htmlContent.AppendLine("<span>⬇</span> Download All as ZIP");
            htmlContent.AppendLine("</button>");
            htmlContent.AppendLine("</div>");
            htmlContent.AppendLine("</header>");
            htmlContent.AppendLine($"<div class='user-info'>Subscribers: {user.Subscribers.Count} | Subscriptions: {user.Subscriptions.Count}</div>");
            htmlContent.AppendLine("<div id='progressContainer'>");
            htmlContent.AppendLine("<p>Creating and downloading archive...</p>");
            htmlContent.AppendLine("<progress id='progressBar' value='0' max='100'></progress>");
            htmlContent.AppendLine("<p id='progressText'>0%</p>");
            htmlContent.AppendLine("</div>");
            htmlContent.AppendLine("<div class='file-list'>");

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file).ToLower();
                var icon = GetFileIcon(extension);

                htmlContent.AppendLine("<div class='file-card'>");
                htmlContent.AppendLine($"<div class='file-icon'>{icon}</div>");
                htmlContent.AppendLine($"<div class='file-name'>{file}</div>");
                htmlContent.AppendLine("<div class='file-actions'>");
                htmlContent.AppendLine($"<button class='action-btn' onclick=\"downloadFile('{file}')\">Download</button>");
                htmlContent.AppendLine($"<button class='action-btn' onclick=\"previewFile('{file}')\">Preview</button>");
                htmlContent.AppendLine("</div>");
                htmlContent.AppendLine("</div>");
            }

            htmlContent.AppendLine("</div>");
            htmlContent.AppendLine("</div>");
            htmlContent.AppendLine("<script>");
            htmlContent.AppendLine("function downloadFile(filename) {");
            htmlContent.AppendLine($"  window.open(`/api/filemanager/download?username={username}&spaceName={space}&filename=${{encodeURIComponent(filename)}}`, '_blank');");
            htmlContent.AppendLine("}");
            htmlContent.AppendLine("function previewFile(filename) {");
            htmlContent.AppendLine($"  window.open(`/api/filemanager/preview?username={username}&spaceName={space}&filename=${{encodeURIComponent(filename)}}`, '_blank');");
            htmlContent.AppendLine("}");
            htmlContent.AppendLine("async function downloadAll() {");
            htmlContent.AppendLine($"  const response = await fetch('/api/filemanager/download-all?username={username}&space={space}', {{ method: 'GET' }});");
            htmlContent.AppendLine("  if (!response.ok) {");
            htmlContent.AppendLine("    alert('Error creating archive: ' + await response.text());");
            htmlContent.AppendLine("    progressContainer.style.display = 'none';");
            htmlContent.AppendLine("    return;");
            htmlContent.AppendLine("  }");
            htmlContent.AppendLine("  const contentLength = response.headers.get('Content-Length');");
            htmlContent.AppendLine("  const total = contentLength ? parseInt(contentLength, 10) : 0;");
            htmlContent.AppendLine("  const reader = response.body.getReader();");
            htmlContent.AppendLine("  let received = 0;");
            htmlContent.AppendLine("  const chunks = [];");
            htmlContent.AppendLine("  while (true) {");
            htmlContent.AppendLine("    const { done, value } = await reader.read();");
            htmlContent.AppendLine("    if (done) break;");
            htmlContent.AppendLine("    chunks.push(value);");
            htmlContent.AppendLine("    received += value.length;");
            htmlContent.AppendLine("    if (total > 0) {");
            htmlContent.AppendLine("      const progress = Math.min((received / total) * 100, 100);");
            htmlContent.AppendLine("      progressBar.value = progress;");
            htmlContent.AppendLine("      progressText.textContent = `${Math.round(progress)}%`;");
            htmlContent.AppendLine("    }");
            htmlContent.AppendLine("  }");
            htmlContent.AppendLine("  const blob = new Blob(chunks);");
            htmlContent.AppendLine("  const url = window.URL.createObjectURL(blob);");
            htmlContent.AppendLine("  const a = document.createElement('a');");
            htmlContent.AppendLine("  a.href = url;");
            htmlContent.AppendLine($"  a.download = '{space}_archive.zip';");
            htmlContent.AppendLine("  document.body.appendChild(a);");
            htmlContent.AppendLine("  a.click();");
            htmlContent.AppendLine("  document.body.removeChild(a);");
            htmlContent.AppendLine("  window.URL.revokeObjectURL(url);");
            htmlContent.AppendLine("  progressContainer.style.display = 'none';");
            htmlContent.AppendLine("}");
            htmlContent.AppendLine("</script>");
            htmlContent.AppendLine("</body>");
            htmlContent.AppendLine("</html>");

            return Content(htmlContent.ToString(), "text/html");
        }

        private string GetFileIcon(string extension)
        {
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".gif":
                    return "🖼️";
                case ".mp4":
                case ".mov":
                case ".avi":
                    return "🎬";
                case ".mp3":
                case ".wav":
                case ".flac":
                    return "🎵";
                case ".pdf":
                    return "📄";
                case ".zip":
                case ".rar":
                case ".7z":
                    return "🗄️";
                case ".doc":
                case ".docx":
                    return "📝";
                case ".xls":
                case ".xlsx":
                    return "📊";
                case ".ppt":
                case ".pptx":
                    return "📑";
                case ".txt":
                    return "📄";
                case ".exe":
                case ".msi":
                    return "⚙️";
                case ".html":
                case ".htm":
                    return "🌐";
                case ".css":
                    return "🎨";
                case ".js":
                    return "📜";
                default:
                    return "📁";
            }
        }

        [HttpGet("download-all")]
        public IActionResult DownloadAll([FromQuery] string username, [FromQuery] string space)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null) return Unauthorized();

            var spaceObj = user.Spaces.FirstOrDefault(s => s.Name == space && s.IsPublic);
            if (spaceObj == null) return Unauthorized("Пространство не публичное или не существует");

            var userDir = UserStore.GetUserSpaceDirectory(username, space);
            var files = Directory.GetFiles(userDir).Select(Path.GetFileName).ToList();
            if (!files.Any()) return BadRequest("Нет файлов для архивации");

            using (var memoryStream = new MemoryStream())
            {
                using (var archive = SharpCompress.Archives.Zip.ZipArchive.Create())
                {
                    foreach (var file in files)
                    {
                        var filePath = Path.Combine(userDir, file);
                        archive.AddEntry(file, new FileStream(filePath, FileMode.Open, FileAccess.Read));
                    }
                    archive.SaveTo(memoryStream, new SharpCompress.Writers.WriterOptions(CompressionType.Deflate));
                }

                memoryStream.Position = 0;
                Response.Headers.Add("Content-Length", memoryStream.Length.ToString());
                return File(memoryStream.ToArray(), "application/zip", $"{space}_archive.zip");
            }
        }

        [HttpPost("delete-file")]
        public IActionResult DeleteFile([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string filename)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var space = user.Spaces.FirstOrDefault(s => s.Name == spaceName);
            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);
            var filePath = Path.Combine(userDir, filename);

            if (!System.IO.File.Exists(filePath))
                return NotFound();

            var fileSize = new FileInfo(filePath).Length;
            System.IO.File.Delete(filePath);
            space.UsedStorage -= fileSize;

            var metadata = space.Files.FirstOrDefault(f => f.Path == filename);
            if (metadata != null)
                space.Files.Remove(metadata);

            UserStore.SaveUsers(users);
            return Ok("Файл удален");
        }

        [HttpPost("rename-space")]
        public IActionResult RenameSpace([FromQuery] string username, [FromQuery] string oldSpaceName, [FromQuery] string newSpaceName)
        {
            // Input validation
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(oldSpaceName) || string.IsNullOrWhiteSpace(newSpaceName))
                return BadRequest("Username, oldSpaceName, and newSpaceName cannot be empty.");

            if (oldSpaceName == newSpaceName)
                return BadRequest("Новое имя должно отличаться от старого.");

            // Load users
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null)
            {
                Console.WriteLine($"User '{username}' not found.");
                return Unauthorized();
            }

            // Find the space to rename
            var space = user.Spaces.FirstOrDefault(s => s.Name == oldSpaceName);
            if (space == null)
            {
                Console.WriteLine($"Space '{oldSpaceName}' not found for user '{username}'.");
                return BadRequest("Пространство не найдено");
            }

            // Check if the new name is already taken
            if (user.Spaces.Any(s => s.Name == newSpaceName))
            {
                Console.WriteLine($"Space name '{newSpaceName}' is already taken for user '{username}'.");
                return BadRequest("Новое имя уже занято");
            }

            // Get the directories
            var oldDir = UserStore.GetUserSpaceDirectory(username, oldSpaceName);
            var newDir = Path.Combine(Path.GetDirectoryName(oldDir)!, newSpaceName); // Compute new directory path without creating it

            // Check if the old directory exists
            if (!Directory.Exists(oldDir))
            {
                Console.WriteLine($"Directory for space '{oldSpaceName}' does not exist at '{oldDir}'.");
                return BadRequest("Директория пространства не найдена");
            }

            // Check if the new directory already exists (shouldn't, but just in case)
            if (Directory.Exists(newDir))
            {
                Console.WriteLine($"Directory for new space name '{newSpaceName}' already exists at '{newDir}'.");
                return BadRequest("Директория с новым именем уже существует");
            }

            try
            {
                // Rename the directory
                Console.WriteLine($"Renaming directory from '{oldDir}' to '{newDir}'...");
                Directory.Move(oldDir, newDir);
                Console.WriteLine("Directory renamed successfully.");

                // Update the space name in memory
                var oldName = space.Name;
                space.Name = newSpaceName;
                Console.WriteLine($"Space name updated in memory from '{oldName}' to '{space.Name}'.");

                // Save the updated users list
                Console.WriteLine("Saving users to JSON...");
                UserStore.SaveUsers(users);
                Console.WriteLine("Users saved successfully.");

                // Verify the change by reloading the users
                var reloadedUsers = UserStore.LoadUsers();
                var reloadedUser = reloadedUsers.FirstOrDefault(u => u.Username == username);
                var reloadedSpace = reloadedUser?.Spaces.FirstOrDefault(s => s.Name == newSpaceName);
                if (reloadedSpace == null)
                {
                    Console.WriteLine($"Failed to verify rename: Space '{newSpaceName}' not found after reload.");
                    // Rollback directory rename
                    Directory.Move(newDir, oldDir);
                    return BadRequest("Ошибка при сохранении изменений: пространство не обновлено в данных.");
                }

                Console.WriteLine($"Verified: Space renamed to '{reloadedSpace.Name}' in JSON.");
                return Ok($"Пространство переименовано в '{newSpaceName}'");
            }
            catch (Exception ex)
            {
                // Log the error and attempt to rollback
                Console.WriteLine($"Error during rename operation: {ex.Message}");
                if (Directory.Exists(newDir) && !Directory.Exists(oldDir))
                {
                    try
                    {
                        Directory.Move(newDir, oldDir);
                        Console.WriteLine("Rolled back directory rename.");
                    }
                    catch (Exception rollbackEx)
                    {
                        Console.WriteLine($"Failed to rollback directory rename: {rollbackEx.Message}");
                    }
                }
                return BadRequest($"Ошибка при переименовании пространства: {ex.Message}");
            }
        }

        [HttpPost("delete-space")]
        public IActionResult DeleteSpace([FromQuery] string username, [FromQuery] string spaceName)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null) return Unauthorized();

            var space = user.Spaces.FirstOrDefault(s => s.Name == spaceName);
            if (space == null) return BadRequest("Пространство не найдено");
            if (space.Name == "Private") return BadRequest("Нельзя удалить приватное пространство");

            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);
            Directory.Delete(userDir, true);
            user.Spaces.Remove(space);
            UserStore.SaveUsers(users);
            return Ok("Пространство удалено");
        }

        [HttpPost("toggle-space-public")]
        public IActionResult ToggleSpacePublic([FromQuery] string username, [FromQuery] string spaceName)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null) return Unauthorized();

            var space = user.Spaces.FirstOrDefault(s => s.Name == spaceName);
            if (space == null) return BadRequest("Пространство не найдено");

            space.IsPublic = !space.IsPublic;
            UserStore.SaveUsers(users);
            return Ok($"Пространство теперь {(space.IsPublic ? "публичное" : "приватное")}");
        }

        [HttpPost("rename-file")]
        public IActionResult RenameFile([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string oldFileName, [FromQuery] string newFileName)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);
            var oldFilePath = Path.Combine(userDir, oldFileName);
            var newFilePath = Path.Combine(userDir, newFileName);

            if (!System.IO.File.Exists(oldFilePath))
                return NotFound("File not found");

            if (System.IO.File.Exists(newFilePath))
                return BadRequest("File with new name already exists");

            try
            {
                System.IO.File.Move(oldFilePath, newFilePath);
                return Ok("File renamed successfully");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error renaming file: {ex.Message}");
            }
        }

        [HttpPost("create-folder")]
        public IActionResult CreateFolder([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string folderPath)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);
            var fullPath = Path.Combine(userDir, folderPath);

            if (Directory.Exists(fullPath))
                return BadRequest("Folder already exists");

            try
            {
                Directory.CreateDirectory(fullPath);
                return Ok("Folder created successfully");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error creating folder: {ex.Message}");
            }
        }

        [HttpPost("toggle-file-public")]
        public IActionResult ToggleFilePublic([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string filename)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var space = user.Spaces.First(s => s.Name == spaceName);
            var filePath = Path.Combine(UserStore.GetUserSpaceDirectory(username, spaceName), filename);
            if (!System.IO.File.Exists(filePath))
                return NotFound();

            var metadata = space.Files.FirstOrDefault(f => f.Path == filename);
            if (metadata == null)
            {
                metadata = new FileMetadata
                {
                    Name = Path.GetFileName(filename),
                    Size = new FileInfo(filePath).Length,
                    Modified = DateTime.UtcNow,
                    IsPublic = false,
                    Path = filename
                };
                space.Files.Add(metadata);
            }

            metadata.IsPublic = !metadata.IsPublic;
            UserStore.SaveUsers(users);
            return Ok($"Файл теперь {(metadata.IsPublic ? "публичный" : "приватный")}");
        }

        [HttpPost("save-text-file")]
        public IActionResult SaveTextFile([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string filename, [FromBody] string content)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var filePath = Path.Combine(UserStore.GetUserSpaceDirectory(username, spaceName), filename);
            if (!System.IO.File.Exists(filePath))
                return NotFound();

            try
            {
                System.IO.File.WriteAllText(filePath, content);
                return Ok("File saved successfully");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error saving file: {ex.Message}");
            }
        }

        [HttpPost("create-archive")]
        public IActionResult CreateArchive([FromQuery] string username, [FromQuery] string spaceName, [FromBody] List<string> filenames, [FromQuery] string archiveName, [FromQuery] string password = null)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);
            var archivePath = Path.Combine(userDir, $"{archiveName}.zip");

            using (var memoryStream = new MemoryStream())
            {
                using (var archive = SharpCompress.Archives.Zip.ZipArchive.Create())
                {
                    foreach (var filename in filenames)
                    {
                        var filePath = Path.Combine(userDir, filename);
                        if (System.IO.File.Exists(filePath))
                            archive.AddEntry(filename, new FileStream(filePath, FileMode.Open, FileAccess.Read));
                    }
                    var options = new SharpCompress.Writers.WriterOptions(CompressionType.Deflate)
                    {
                        ArchiveEncoding = new SharpCompress.Common.ArchiveEncoding
                        {
                            Password = password != null ? Encoding.UTF8 : null // Fix: Use Encoding.UTF8 for password encoding
                        }
                    };
                    archive.SaveTo(memoryStream, options);
                }

                System.IO.File.WriteAllBytes(archivePath, memoryStream.ToArray());
            }

            var space = user.Spaces.First(s => s.Name == spaceName);
            space.UsedStorage += new FileInfo(archivePath).Length;
            UserStore.SaveUsers(users);
            return Ok("Archive created successfully");
        }

        [HttpPost("extract-archive")]
        public IActionResult ExtractArchive([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string filename)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);
            var archivePath = Path.Combine(userDir, filename);

            if (!System.IO.File.Exists(archivePath))
                return NotFound();

            using (var archive = SharpCompress.Archives.Zip.ZipArchive.Open(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    var extractPath = Path.Combine(userDir, entry.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(extractPath));
                    entry.WriteToFile(extractPath);
                }
            }

            var space = user.Spaces.First(s => s.Name == spaceName);
            space.UsedStorage += Directory.GetFiles(userDir, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
            UserStore.SaveUsers(users);
            return Ok("Archive extracted successfully");
        }

        [HttpGet("api-docs")]
        public IActionResult GetApiDocs()
        {
            var docs = @"
    MelanCloud API Documentation
    
    Authentication:
    - Add header: X-API-Key: your_api_key
    
    Endpoints:
    GET /api/filemanager/files?username={username}&spaceName={spaceName}
    POST /api/filemanager/upload?username={username}&spaceName={spaceName}
    GET /api/filemanager/download?username={username}&spaceName={spaceName}&filename={filename}
    DELETE /api/filemanager/delete-file?username={username}&spaceName={spaceName}&filename={filename}
    
    Full documentation available at: https://github.com/melancloud/api-docs
    ";

            return Content(docs, "text/plain");
        }

        [HttpGet("export-file-list")]
        public IActionResult ExportFileList([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string format = "json")
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var space = user.Spaces.First(s => s.Name == spaceName);
            var files = space.Files;

            if (format.ToLower() == "csv")
            {
                var csv = new StringBuilder();
                csv.AppendLine("Name,Size (bytes),Modified,Path");
                foreach (var file in files)
                {
                    csv.AppendLine($"\"{file.Name}\",{file.Size},{file.Modified:yyyy-MM-dd HH:mm:ss},\"{file.Path}\"");
                }
                return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"{spaceName}_file_list.csv");
            }
            else
            {
                return Ok(files);
            }
        }

        [HttpPost("import-file-list")]
        public IActionResult ImportFileList([FromQuery] string username, [FromQuery] string spaceName, [FromForm] IFormFile file)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var space = user.Spaces.First(s => s.Name == spaceName);

            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            try
            {
                using (var reader = new StreamReader(file.OpenReadStream()))
                {
                    if (file.FileName.EndsWith(".csv"))
                    {
                        // Skip header
                        reader.ReadLine();
                        while (!reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            var values = line.Split(',');

                            var fileMetadata = new FileMetadata
                            {
                                Name = values[0].Trim('"'),
                                Size = long.Parse(values[1]),
                                Modified = DateTime.Parse(values[2]),
                                Path = values[3].Trim('"')
                            };

                            space.Files.Add(fileMetadata);
                        }
                    }
                    else if (file.FileName.EndsWith(".json"))
                    {
                        var json = reader.ReadToEnd();
                        var files = JsonConvert.DeserializeObject<List<FileMetadata>>(json);
                        space.Files.AddRange(files);
                    }
                    else
                    {
                        return BadRequest("Unsupported file format");
                    }
                }

                UserStore.SaveUsers(users);
                return Ok("File list imported successfully");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error importing file list: {ex.Message}");
            }
        }

        [HttpGet("notification-settings")]
        public IActionResult GetNotificationSettings([FromQuery] string username)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null) return Unauthorized();

            return Ok(user.Notifications);
        }

        [HttpPost("notification-settings")]
        public IActionResult UpdateNotificationSettings([FromQuery] string username, [FromBody] NotificationSettings settings)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null) return Unauthorized();

            user.Notifications = settings;
            UserStore.SaveUsers(users);
            return Ok("Notification settings updated");
        }

        [HttpGet("admin/users")]
        public IActionResult GetAllUsers([FromQuery] string adminUsername)
        {
            var users = UserStore.LoadUsers();
            var admin = users.FirstOrDefault(u => u.Username == adminUsername);
            if (admin == null || !admin.IsAdmin)
                return Unauthorized("Требуются права администратора");

            // Возвращаем список пользователей без паролей
            var result = users.Select(u => new {
                u.Username,
                u.IsPremium,
                u.IsAdmin,
                u.IsBlocked,
                u.BlockedUntil,
                u.BlockReason,
                SpacesCount = u.Spaces.Count,
                TotalStorage = u.Spaces.Sum(s => s.UsedStorage)
            });

            return Ok(result);
        }

        [HttpPost("admin/block-user")]
        public IActionResult BlockUser(
            [FromQuery] string adminUsername,
            [FromQuery] string usernameToBlock,
            [FromQuery] string reason,
            [FromQuery] int? blockDays = null)
        {
            var users = UserStore.LoadUsers();
            var admin = users.FirstOrDefault(u => u.Username == adminUsername);
            if (admin == null || !admin.IsAdmin)
                return Unauthorized("Требуются права администратора");

            var userToBlock = users.FirstOrDefault(u => u.Username == usernameToBlock);
            if (userToBlock == null)
                return NotFound("Пользователь не найден");

            if (userToBlock.IsAdmin)
                return BadRequest("Нельзя заблокировать другого администратора");

            userToBlock.IsBlocked = true;
            userToBlock.BlockReason = reason;
            userToBlock.BlockedUntil = blockDays.HasValue ?
                DateTime.UtcNow.AddDays(blockDays.Value) : null;

            UserStore.SaveUsers(users);
            return Ok($"Пользователь {usernameToBlock} заблокирован");
        }

        [HttpPost("admin/unblock-user")]
        public IActionResult UnblockUser(
            [FromQuery] string adminUsername,
            [FromQuery] string usernameToUnblock)
        {
            var users = UserStore.LoadUsers();
            var admin = users.FirstOrDefault(u => u.Username == adminUsername);
            if (admin == null || !admin.IsAdmin)
                return Unauthorized("Требуются права администратора");

            var userToUnblock = users.FirstOrDefault(u => u.Username == usernameToUnblock);
            if (userToUnblock == null)
                return NotFound("Пользователь не найден");

            userToUnblock.IsBlocked = false;
            userToUnblock.BlockReason = null;
            userToUnblock.BlockedUntil = null;

            UserStore.SaveUsers(users);
            return Ok($"Пользователь {usernameToUnblock} разблокирован");
        }

        [HttpPost("admin/set-admin")]
        public IActionResult SetAdmin(
            [FromQuery] string adminUsername,
            [FromQuery] string username,
            [FromQuery] bool isAdmin)
        {
            var users = UserStore.LoadUsers();
            var admin = users.FirstOrDefault(u => u.Username == adminUsername);
            if (admin == null || !admin.IsAdmin)
                return Unauthorized("Требуются права администратора");

            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null)
                return NotFound("Пользователь не найден");

            user.IsAdmin = isAdmin;
            UserStore.SaveUsers(users);
            return Ok($"Права администратора для {username} {(isAdmin ? "установлены" : "сняты")}");
        }

        [HttpPost("admin/set-premium")]
        public IActionResult SetPremiumStatus([FromQuery] string adminUsername, [FromQuery] string username, [FromQuery] bool isPremium)
        {
            var users = UserStore.LoadUsers();
            var admin = users.FirstOrDefault(u => u.Username == adminUsername);
            if (admin == null || !admin.IsAdmin)
                return Unauthorized("Admin rights required");

            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null)
                return NotFound("User not found");

            user.IsPremium = isPremium;
            UserStore.SaveUsers(users);

            return Ok($"Premium status {(isPremium ? "granted" : "revoked")} for {username}");
        }

        // 2FA Endpoints
        [HttpPost("enable-2fa")]
        public IActionResult EnableTwoFactorAuth([FromQuery] string username)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null) return Unauthorized();

            // Generate a secure TOTP secret (20 bytes = 160 bits, standard for TOTP)
            var secretKeyBytes = KeyGeneration.GenerateRandomKey(20);
            var secretKeyBase32 = Base32Encoding.ToString(secretKeyBytes);

            // Generate backup codes (8-digit codes)
            var backupCodes = Enumerable.Range(0, 5)
                .Select(_ => RandomCode(8)) // Generate 8-digit codes
                .ToList();

            user.TwoFactorSecret = secretKeyBase32;
            user.TwoFactorEnabled = true;
            user.BackupCodes = backupCodes;
            UserStore.SaveUsers(users);

            // Return the secret key and backup codes for the user to save
            return Ok(new
            {
                SecretKey = secretKeyBase32,
                BackupCodes = user.BackupCodes,
                // Provide the otpauth URI for QR code generation
                OtpAuthUri = $"otpauth://totp/MelanCloud:{username}?secret={secretKeyBase32}&issuer=MelanCloud"
            });
        }

        [HttpPost("verify-2fa")]
        public IActionResult VerifyTwoFactorAuth([FromQuery] string username, [FromQuery] string code)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null) return Unauthorized();

            if (!user.TwoFactorEnabled || string.IsNullOrEmpty(user.TwoFactorSecret))
                return BadRequest("2FA not enabled for this user");

            // Check if it's a backup code
            if (user.BackupCodes.Contains(code))
            {
                user.BackupCodes.Remove(code);
                UserStore.SaveUsers(users);
                return Ok(new { Message = "2FA verification successful using backup code" });
            }

            // Verify TOTP code
            var totp = new Totp(Base32Encoding.ToBytes(user.TwoFactorSecret));
            long timeStepMatched;
            bool isValid = totp.VerifyTotp(code, out timeStepMatched,
                new VerificationWindow(1, 1)); // Allow 30 seconds before/after

            if (isValid)
            {
                UserStore.SaveUsers(users);
                return Ok(new { Message = "2FA verification successful" });
            }

            return BadRequest("Invalid 2FA code");
        }

        [HttpPost("disable-2fa")]
        public IActionResult DisableTwoFactorAuth([FromQuery] string username)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null) return Unauthorized();

            if (!user.TwoFactorEnabled)
                return BadRequest("2FA is not enabled");

            user.TwoFactorEnabled = false;
            user.TwoFactorSecret = null;
            user.BackupCodes.Clear();
            UserStore.SaveUsers(users);

            return Ok("2FA disabled successfully");
        }

        // Recycle Bin Endpoints
        [HttpPost("delete-to-recycle")]
        public IActionResult DeleteToRecycleBin([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string filename)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (user == null || !user.Spaces.Any(s => s.Name == spaceName))
                return Unauthorized();

            var space = user.Spaces.FirstOrDefault(s => s.Name == spaceName);
            var userDir = UserStore.GetUserSpaceDirectory(username, spaceName);
            var filePath = Path.Combine(userDir, filename);

            if (!System.IO.File.Exists(filePath))
                return NotFound();

            // Создаем папку для корзины, если её нет
            var recycleDir = Path.Combine(UserStore.GetUserRootDirectory(username), "RecycleBin");
            if (!Directory.Exists(recycleDir))
                Directory.CreateDirectory(recycleDir);

            // Генерируем уникальное имя для файла в корзине
            var recycleFileName = $"{DateTime.UtcNow.Ticks}_{filename}";
            var recyclePath = Path.Combine(recycleDir, recycleFileName);

            // Перемещаем файл в корзину
            System.IO.File.Move(filePath, recyclePath);

            // Добавляем запись в корзину
            user.RecycleBin.Files.Add(new RecycledFile
            {
                OriginalPath = filename,
                RecyclePath = recycleFileName,
                DeletedDate = DateTime.UtcNow,
                Size = new FileInfo(recyclePath).Length,
                SpaceName = spaceName
            });

            // Обновляем метаданные и хранилище
            var metadata = space.Files.FirstOrDefault(f => f.Path == filename);
            if (metadata != null)
            {
                space.UsedStorage -= metadata.Size;
                space.Files.Remove(metadata);
            }

            UserStore.SaveUsers(users);
            return Ok("File moved to recycle bin");
        }

        [HttpGet("recycle-bin")]
        public IActionResult GetRecycleBin([FromQuery] string username)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");

            return Ok(user.RecycleBin);
        }

        [HttpPost("restore-from-recycle")]
        public IActionResult RestoreFromRecycleBin([FromQuery] string username, [FromQuery] string recycleFileName)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");

            var recycledFile = user.RecycleBin.Files.FirstOrDefault(f => f.RecyclePath == recycleFileName);
            if (recycledFile == null)
                return NotFound("File not found in recycle bin");

            // Проверяем, существует ли еще пространство
            var space = user.Spaces.FirstOrDefault(s => s.Name == recycledFile.SpaceName);
            if (space == null)
                return BadRequest("Original space no longer exists");

            var recycleDir = Path.Combine(UserStore.GetUserRootDirectory(username), "RecycleBin");
            var recyclePath = Path.Combine(recycleDir, recycleFileName);
            var userDir = UserStore.GetUserSpaceDirectory(username, recycledFile.SpaceName);
            var restorePath = Path.Combine(userDir, recycledFile.OriginalPath);

            if (!System.IO.File.Exists(recyclePath))
                return NotFound("File not found in recycle bin storage");

            // Проверяем, не существует ли уже файл с таким именем
            if (System.IO.File.Exists(restorePath))
                return BadRequest("File with this name already exists in the target location");

            // Восстанавливаем файл
            System.IO.File.Move(recyclePath, restorePath);

            // Обновляем метаданные
            space.Files.Add(new FileMetadata
            {
                Name = Path.GetFileName(recycledFile.OriginalPath),
                Size = recycledFile.Size,
                Modified = DateTime.UtcNow,
                IsPublic = false,
                Path = recycledFile.OriginalPath
            });
            space.UsedStorage += recycledFile.Size;

            // Удаляем запись из корзины
            user.RecycleBin.Files.Remove(recycledFile);

            UserStore.SaveUsers(users);
            return Ok("File restored successfully");
        }

        [HttpPost("empty-recycle-bin")]
        public IActionResult EmptyRecycleBin([FromQuery] string username)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");

            var recycleDir = Path.Combine(UserStore.GetUserRootDirectory(username), "RecycleBin");

            if (Directory.Exists(recycleDir))
            {
                Directory.Delete(recycleDir, true);
                Directory.CreateDirectory(recycleDir);
            }

            user.RecycleBin.Files.Clear();
            UserStore.SaveUsers(users);
            return Ok("Recycle bin emptied successfully");
        }

        // Scheduled task to clean old files from recycle bins
        public static void CleanOldRecycledFiles()
        {
            var users = UserStore.LoadUsers();
            foreach (var user in users)
            {
                var recycleDir = Path.Combine(UserStore.GetUserRootDirectory(user.Username), "RecycleBin");
                if (!Directory.Exists(recycleDir)) continue;

                var filesToDelete = user.RecycleBin.Files
                    .Where(f => (DateTime.UtcNow - f.DeletedDate).TotalDays > user.RecycleBin.RetentionDays)
                    .ToList();

                foreach (var file in filesToDelete)
                {
                    var filePath = Path.Combine(recycleDir, file.RecyclePath);
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                    user.RecycleBin.Files.Remove(file);
                }
            }
            UserStore.SaveUsers(users);
        }

        private User GetUser(string username)
        {
            var users = UserStore.LoadUsers();
            return users.FirstOrDefault(u => u.Username == username && !UserStore.IsUserBlocked(username));
        }

        // Публикация файла на площадку
        [HttpPost("publish-file")]
        public IActionResult PublishFile([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string filename, [FromBody] string description)
        {
            var users = UserStore.LoadUsers();
            var user = GetUser(username);
            if (user == null) return Unauthorized("User not found or blocked");

            var space = user.Spaces.FirstOrDefault(s => s.Name == spaceName);
            var file = space?.Files.FirstOrDefault(f => f.Path == filename);
            if (file == null) return NotFound("File not found");

            // Проверяем, не опубликован ли файл уже
            if (PublicFileStore.GetAll().Any(pf => pf.Username == username && pf.SpaceName == spaceName && pf.FilePath == filename))
                return BadRequest("File is already published");

            var filePath = Path.Combine(UserStore.GetUserSpaceDirectory(username, spaceName), filename);
            if (!System.IO.File.Exists(filePath)) return NotFound("File not found on server");

            file.IsPublic = true; // Делаем файл публичным в пространстве
            var fileInfo = new FileInfo(filePath);
            var publicFile = new PublicFile
            {
                Username = username,
                SpaceName = spaceName,
                FilePath = filename,
                Description = description,
                FileSize = fileInfo.Length,
                ContentType = GetContentType(filename) // Используем существующую функцию
            };
            PublicFileStore.GetAll().Add(publicFile);
            PublicFileStore.SavePublicFiles();
            UserStore.SaveUsers(users);
            return Ok("File published to marketplace");
        }

        // Удаление файла с площадки
        [HttpPost("unpublish-file")]
        public IActionResult UnpublishFile([FromQuery] string username, [FromQuery] string spaceName, [FromQuery] string filename)
        {
            var users = UserStore.LoadUsers();
            var user = GetUser(username);
            if (user == null) return Unauthorized("User not found or blocked");

            var publicFile = PublicFileStore.GetAll().FirstOrDefault(pf => pf.Username == username && pf.SpaceName == spaceName && pf.FilePath == filename);
            if (publicFile == null) return NotFound("File not found in marketplace");

            var space = user.Spaces.FirstOrDefault(s => s.Name == spaceName);
            var file = space?.Files.FirstOrDefault(f => f.Path == filename);
            if (file != null) file.IsPublic = false; // Убираем публичность

            PublicFileStore.GetAll().Remove(publicFile);
            PublicFileStore.SavePublicFiles();
            UserStore.SaveUsers(users);
            return Ok("File removed from marketplace");
        }

        // Просмотр всех публичных файлов
        [HttpGet("public-files")]
        public IActionResult GetPublicFiles([FromQuery] string sortBy = "date", [FromQuery] bool descending = true)
        {
            var publicFiles = PublicFileStore.GetAll();
            if (!publicFiles.Any())
                return Ok(new List<object>());

            var sortedFiles = sortBy.ToLower() switch
            {
                "downloads" => descending ? publicFiles.OrderByDescending(f => f.DownloadCount) : publicFiles.OrderBy(f => f.DownloadCount),
                "likes" => descending ? publicFiles.OrderByDescending(f => f.Likes.Count) : publicFiles.OrderBy(f => f.Likes.Count),
                "date" => descending ? publicFiles.OrderByDescending(f => f.UploadedDate) : publicFiles.OrderBy(f => f.UploadedDate),
                _ => descending ? publicFiles.OrderByDescending(f => f.UploadedDate) : publicFiles.OrderBy(f => f.UploadedDate)
            };

            var users = UserStore.LoadUsers();
            var result = sortedFiles.Select(f =>
            {
                var user = users.FirstOrDefault(u => u.Username == f.Username);
                var filePath = Path.Combine(UserStore.GetUserSpaceDirectory(f.Username, f.SpaceName), f.FilePath);
                var fileExists = System.IO.File.Exists(filePath);
                var isLiked = f.Likes.Contains(HttpContext.User.Identity.Name);

                return new
                {
                    Id = f.Id,
                    Username = f.Username,
                    SpaceName = f.SpaceName,
                    FilePath = f.FilePath,
                    Description = f.Description,
                    UploadedDate = f.UploadedDate,
                    DownloadCount = f.DownloadCount,
                    Likes = f.Likes.Count,
                    IsLiked = isLiked,
                    Comments = f.Comments.Select(c => new
                    {
                        Author = c.Username,
                        Text = c.Text,
                        Date = c.Date
                    }),
                    SubscribersCount = user?.Subscribers.Count ?? 0,
                    FileSize = f.FileSize,
                    ContentType = f.ContentType,
                    FileExists = fileExists
                };
            }).Where(f => f.FileExists);

            return Ok(result);
        }

        [HttpGet("public-file-details")]
        public IActionResult GetPublicFileDetails([FromQuery] string fileId, [FromQuery] string username)
        {
            var publicFile = PublicFileStore.GetById(fileId);
            if (publicFile == null)
                return NotFound("File not found");

            var user = UserStore.LoadUsers().FirstOrDefault(u => u.Username == publicFile.Username);
            var filePath = Path.Combine(UserStore.GetUserSpaceDirectory(publicFile.Username, publicFile.SpaceName), publicFile.FilePath);

            if (!System.IO.File.Exists(filePath))
                return NotFound("File not found on disk");

            var isLiked = publicFile.Likes.Contains(username);

            var result = new
            {
                Id = publicFile.Id,
                Username = publicFile.Username,
                SpaceName = publicFile.SpaceName,
                FilePath = publicFile.FilePath,
                Description = publicFile.Description,
                UploadedDate = publicFile.UploadedDate,
                DownloadCount = publicFile.DownloadCount,
                Likes = publicFile.Likes.Count,
                LikedBy = publicFile.Likes,
                IsLiked = isLiked,
                Comments = publicFile.Comments.Select(c => new
                {
                    Id = c.Id,
                    Author = c.Username,
                    Text = c.Text,
                    Date = c.Date
                }),
                SubscribersCount = user?.Subscribers.Count ?? 0,
                FileSize = publicFile.FileSize,
                ContentType = publicFile.ContentType
            };

            return Ok(result);
        }

        [HttpGet("public-preview")]
        public IActionResult GetPublicPreview([FromQuery] string fileId)
        {
            var publicFile = PublicFileStore.GetById(fileId);
            if (publicFile == null)
                return NotFound();

            var filePath = Path.Combine(UserStore.GetUserSpaceDirectory(publicFile.Username, publicFile.SpaceName), publicFile.FilePath);

            if (!System.IO.File.Exists(filePath))
                return NotFound();

            var extension = Path.GetExtension(filePath).ToLower();
            var contentType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".mov" => "video/quicktime",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".flac" => "audio/flac",
                ".txt" => "text/plain",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".csv" => "text/csv",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };

            if (new[] { ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".webm", ".mov", ".mp3", ".wav", ".flac" }.Contains(extension))
            {
                return PhysicalFile(filePath, contentType);
            }
            else if (new[] { ".txt", ".json", ".xml", ".csv", ".pdf" }.Contains(extension))
            {
                return PhysicalFile(filePath, contentType);
            }

            return BadRequest("Preview not available for this file type");
        }

        // Скачивание файла с площадки (с учетом статистики)
        [HttpGet("marketplace/download")]
        public IActionResult DownloadMarketplaceFile([FromQuery] string fileId)
        {
            var publicFile = PublicFileStore.GetAll().FirstOrDefault(f => f.Id == fileId);
            if (publicFile == null) return NotFound("File not found in marketplace");

            publicFile.DownloadCount++;
            PublicFileStore.SavePublicFiles();

            return RedirectToAction("DownloadFile", new
            {
                username = publicFile.Username,
                spaceName = publicFile.SpaceName,
                filename = publicFile.FilePath
            });
        }

        // Добавление/удаление лайка
        [HttpPost("toggle-like")]
        public IActionResult ToggleLike([FromQuery] string username, [FromQuery] string fileId)
        {
            var user = GetUser(username);
            if (user == null) return Unauthorized("User not found or blocked");

            var publicFile = PublicFileStore.GetById(fileId);
            if (publicFile == null) return NotFound("File not found in marketplace");

            if (publicFile.Likes.Contains(username))
            {
                publicFile.Likes.Remove(username);
            }
            else
            {
                publicFile.Likes.Add(username);
            }
            PublicFileStore.SavePublicFiles();
            return Ok(new { Message = "Like updated", LikesCount = publicFile.Likes.Count, LikedBy = publicFile.Likes });
        }

        // Добавление комментария
        [HttpPost("add-comment")]
        public IActionResult AddComment([FromQuery] string username, [FromQuery] string fileId, [FromBody] string commentText)
        {
            var user = GetUser(username);
            if (user == null) return Unauthorized("User not found or blocked");

            var publicFile = PublicFileStore.GetById(fileId);
            if (publicFile == null) return NotFound("File not found in marketplace");

            var comment = new Comment
            {
                Id = Guid.NewGuid().ToString(),
                Username = username,
                Text = commentText,
                Date = DateTime.UtcNow
            };
            publicFile.Comments.Add(comment);
            PublicFileStore.SavePublicFiles();
            return Ok(new
            {
                Message = "Comment added",
                Comment = new
                {
                    Id = Guid.NewGuid().ToString(),
                    Author = comment.Username,
                    Text = comment.Text,
                    Date = comment.Date
                }
            });
        }

        // Получение статистики файла
        [HttpGet("marketplace/file-stats")]
        public IActionResult GetFileStats([FromQuery] string fileId)
        {
            var publicFile = PublicFileStore.GetAll().FirstOrDefault(f => f.Id == fileId);
            if (publicFile == null) return NotFound("File not found in marketplace");

            return Ok(new
            {
                publicFile.Username,
                publicFile.SpaceName,
                publicFile.FilePath,
                publicFile.Description,
                publicFile.UploadedDate,
                publicFile.DownloadCount,
                LikesCount = publicFile.Likes.Count,
                Comments = publicFile.Comments
            });
        }

        // Получение списка опубликованных файлов пользователя
        [HttpGet("my-published-files")]
        public IActionResult GetMyPublishedFiles([FromQuery] string username)
        {
            var user = GetUser(username);
            if (user == null) return Unauthorized("User not found or blocked");

            var myFiles = PublicFileStore.GetAll().Where(f => f.Username == username);
            return Ok(myFiles.Select(f => new
            {
                f.Id,
                f.SpaceName,
                f.FilePath,
                f.Description,
                f.UploadedDate,
                f.DownloadCount,
                LikesCount = f.Likes.Count,
                CommentCount = f.Comments.Count
            }));
        }

        [HttpPost("subscribe")]
        public IActionResult Subscribe([FromQuery] string username, [FromQuery] string targetUsername)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            var targetUser = users.FirstOrDefault(u => u.Username == targetUsername);

            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (targetUser == null || UserStore.IsUserBlocked(targetUsername))
                return NotFound("Target user not found or blocked");
            if (username == targetUsername)
                return BadRequest("Cannot subscribe to yourself");
            if (user.Subscriptions.Contains(targetUsername))
                return BadRequest("Already subscribed");

            user.Subscriptions.Add(targetUsername);
            targetUser.Subscribers.Add(username);

            UserStore.SaveUsers(users);
            return Ok($"Subscribed to {targetUsername}");
        }

        [HttpPost("unsubscribe")]
        public IActionResult Unsubscribe([FromQuery] string username, [FromQuery] string targetUsername)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);
            var targetUser = users.FirstOrDefault(u => u.Username == targetUsername);

            if (user == null || UserStore.IsUserBlocked(username))
                return Unauthorized("User not found or blocked");
            if (targetUser == null || UserStore.IsUserBlocked(targetUsername))
                return NotFound("Target user not found or blocked");
            if (!user.Subscriptions.Contains(targetUsername))
                return BadRequest("Not subscribed to this user");

            user.Subscriptions.Remove(targetUsername);
            targetUser.Subscribers.Remove(username);

            UserStore.SaveUsers(users);
            return Ok($"Unsubscribed from {targetUsername}");
        }

        [HttpGet("profile")]
        public IActionResult GetProfile([FromQuery] string username, [FromQuery] string? viewerUsername = null)
        {
            var users = UserStore.LoadUsers();
            var user = users.FirstOrDefault(u => u.Username == username);

            if (user == null || UserStore.IsUserBlocked(username))
                return NotFound("User not found or blocked");

            var publicFiles = PublicFileStore.GetAll()
                .Where(f => f.Username == username)
                .Select(f => new
                {
                    f.Id,
                    f.SpaceName,
                    f.FilePath,
                    f.Description,
                    f.UploadedDate,
                    f.DownloadCount,
                    LikesCount = f.Likes.Count,
                    CommentCount = f.Comments.Count,
                    f.FileSize,
                    f.ContentType
                }).ToList();

            var response = new
            {
                user.Username,
                user.IsPremium,
                SubscribersCount = user.Subscribers.Count,
                SubscriptionsCount = user.Subscriptions.Count,
                PublicFiles = publicFiles,
                IsSubscribed = viewerUsername != null && users.FirstOrDefault(u => u.Username == viewerUsername)?.Subscriptions.Contains(username) == true
            };

            return Ok(response);
        }

        [HttpPost("delete-comment")]
        public IActionResult DeleteComment([FromQuery] string username, [FromQuery] string fileId, [FromQuery] string commentId)
        {
            var users = UserStore.LoadUsers();
            var user = GetUser(username);
            if (user == null) return Unauthorized("User not found or blocked");

            var publicFile = PublicFileStore.GetById(fileId);
            if (publicFile == null) return NotFound("File not found in marketplace");

            var comment = publicFile.Comments.FirstOrDefault(c => c.Id == commentId);
            if (comment == null) return NotFound("Comment not found");

            // Проверяем, что пользователь является автором комментария или владельцем файла
            if (comment.Username != username && publicFile.Username != username)
                return Unauthorized("You can only delete your own comments");

            publicFile.Comments.Remove(comment);
            PublicFileStore.SavePublicFiles();
            return Ok("Comment deleted successfully");
        }

        [HttpPost("delete-public-file")]
        public IActionResult DeletePublicFile([FromQuery] string username, [FromQuery] string fileId)
        {
            var users = UserStore.LoadUsers();
            var user = GetUser(username);
            if (user == null) return Unauthorized("User not found or blocked");

            var publicFile = PublicFileStore.GetById(fileId);
            if (publicFile == null) return NotFound("File not found in marketplace");

            // Проверяем, что пользователь является владельцем файла
            if (publicFile.Username != username)
                return Unauthorized("You can only delete your own published files");

            // Удаляем файл из публичного списка
            PublicFileStore.GetAll().Remove(publicFile);
            PublicFileStore.SavePublicFiles();

            // Делаем файл приватным в пространстве пользователя
            var space = user.Spaces.FirstOrDefault(s => s.Name == publicFile.SpaceName);
            if (space != null)
            {
                var file = space.Files.FirstOrDefault(f => f.Path == publicFile.FilePath);
                if (file != null) file.IsPublic = false;
            }

            UserStore.SaveUsers(users);
            return Ok("Public file deleted successfully");
        }
    }
    public class FileMoveRequest
    {
        public string Username { get; set; }
        public string FromSpace { get; set; }
        public string ToSpace { get; set; }
        public string Filename { get; set; }
        public string OldFilePath { get; set; } // Старый путь файла (включает папки, если есть)
        public string NewFilePath { get; set; } // Новый путь файла (включает папки, если есть)

    }
}