using IGoLibrary.Core.Interfaces;
using IGoLibrary.Core.Data;

namespace IGoLibrary.ConsoleTest
{
    /// <summary>
    /// 模拟通知服务 - 仅输出到控制台
    /// </summary>
    public class MockNotificationService : INotificationService
    {
        public void ShowSuccess(string title, string message)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[SUCCESS] {title}: {message}");
            Console.ResetColor();
        }

        public void ShowError(string title, string message)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {title}: {message}");
            Console.ResetColor();
        }

        public void ShowWarning(string title, string message)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[WARNING] {title}: {message}");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// 模拟存储服务 - 仅在内存中存取
    /// </summary>
    public class MockStorageService : IStorageService
    {
        private string? _cookieInMemory;

        public Task SaveCookieAsync(string cookie)
        {
            _cookieInMemory = cookie;
            Console.WriteLine($"[STORAGE] Cookie 已保存到内存: {cookie.Substring(0, Math.Min(50, cookie.Length))}...");
            return Task.CompletedTask;
        }

        public Task<string?> LoadCookieAsync()
        {
            if (_cookieInMemory != null)
            {
                Console.WriteLine($"[STORAGE] 从内存加载 Cookie: {_cookieInMemory.Substring(0, Math.Min(50, _cookieInMemory.Length))}...");
            }
            else
            {
                Console.WriteLine("[STORAGE] 内存中无 Cookie");
            }
            return Task.FromResult(_cookieInMemory);
        }
    }

    /// <summary>
    /// 模拟会话服务 - 内存中管理状态
    /// </summary>
    public class MockSessionService : ISessionService
    {
        private string? _cookie;
        private Library? _currentLibrary;
        private string? _queryLibInfoSyntax;

        public string? Cookie
        {
            get => _cookie;
            set
            {
                _cookie = value;
                Console.WriteLine($"[SESSION] Cookie 已设置");
            }
        }

        public Library? CurrentLibrary
        {
            get => _currentLibrary;
            set
            {
                _currentLibrary = value;
                if (value != null)
                {
                    Console.WriteLine($"[SESSION] 当前图书馆: {value.Name}");
                }
            }
        }

        public string? QueryLibInfoSyntax
        {
            get => _queryLibInfoSyntax;
            set => _queryLibInfoSyntax = value;
        }

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_cookie);
    }
}
