using IGoLibrary.Core.Data;

namespace IGoLibrary.Core.Interfaces
{
    public interface ISessionService
    {
        string? Cookie { get; set; }
        Library? CurrentLibrary { get; set; }
        string? QueryLibInfoSyntax { get; set; }
        bool IsAuthenticated { get; }
    }
}
