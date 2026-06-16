using IGoLibrary.Ex.Application.Updates;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IAppVersionProvider
{
    ReleaseVersion CurrentVersion { get; }

    string CurrentVersionText { get; }
}
