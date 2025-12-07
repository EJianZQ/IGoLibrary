namespace IGoLibrary.Core.Interfaces
{
    public interface IStorageService
    {
        Task SaveCookieAsync(string cookie);
        Task<string?> LoadCookieAsync();
    }
}
