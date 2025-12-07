using System;
using System.IO;
using System.Threading.Tasks;
using IGoLibrary.Core.Interfaces;

namespace IGoLibrary.Mac.Services
{
    public class FileStorageService : IStorageService
    {
        private readonly string _cookieFilePath;

        public FileStorageService()
        {
            // macOS 下使用 ApplicationData 目录
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appDirectory = Path.Combine(appDataPath, "IGoLibrary");

            // 确保目录存在
            if (!Directory.Exists(appDirectory))
            {
                Directory.CreateDirectory(appDirectory);
            }

            _cookieFilePath = Path.Combine(appDirectory, "cookie.txt");
        }

        public async Task SaveCookieAsync(string cookie)
        {
            try
            {
                await File.WriteAllTextAsync(_cookieFilePath, cookie);
            }
            catch (Exception ex)
            {
                throw new IOException($"保存 Cookie 失败: {ex.Message}", ex);
            }
        }

        public async Task<string?> LoadCookieAsync()
        {
            try
            {
                if (!File.Exists(_cookieFilePath))
                {
                    return null;
                }

                var cookie = await File.ReadAllTextAsync(_cookieFilePath);
                return string.IsNullOrWhiteSpace(cookie) ? null : cookie;
            }
            catch (Exception ex)
            {
                throw new IOException($"读取 Cookie 失败: {ex.Message}", ex);
            }
        }
    }
}
