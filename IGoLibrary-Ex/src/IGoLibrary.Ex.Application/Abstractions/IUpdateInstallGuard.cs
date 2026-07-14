namespace IGoLibrary.Ex.Application.Abstractions;

public interface IUpdateInstallGuard
{
    IReadOnlyList<string> GetBlockingTaskNames();
}
