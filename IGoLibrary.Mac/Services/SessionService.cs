using IGoLibrary.Core.Interfaces;
using IGoLibrary.Core.Data;

namespace IGoLibrary.Mac.Services
{
    public class SessionService : ISessionService
    {
        private string? _cookie;
        private Library? _currentLibrary;
        private string? _queryLibInfoSyntax;

        public string? Cookie
        {
            get => _cookie;
            set => _cookie = value;
        }

        public Library? CurrentLibrary
        {
            get => _currentLibrary;
            set => _currentLibrary = value;
        }

        public string? QueryLibInfoSyntax
        {
            get => _queryLibInfoSyntax;
            set => _queryLibInfoSyntax = value;
        }

        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_cookie);
    }
}
