namespace IGoLibrary.Ex.Tests;

public sealed class ArchitectureClosureTests
{
    [Fact]
    public void ProductionCode_DoesNotContainRetiredBusinessNames()
    {
        var sourceRoot = GetRepositoryRoot().FullName;
        var files = EnumerateProductionFiles(sourceRoot)
            .Where(path => !path.EndsWith("SqliteSettingsRepository.cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var retiredNames = new[]
        {
            "CookieExpiry",
            "GrabMode",
            "RefreshMode",
            "TaskAlertService",
            "ProtocolTemplateSet",
            "ToastEnabled",
            "RetryCount",
            "ApiTimeoutSeconds",
            "TrackedSeat",
            "RequestPolicySettings",
            "LocalAlertChannelSettings",
            "ThemeSettings",
            "TraceIntGraphQlTemplateSet"
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var retiredName in retiredNames)
            {
                Assert.DoesNotContain(retiredName, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void DomainProject_DoesNotReferenceApplicationInfrastructureOrDesktopConcepts()
    {
        var domainRoot = Path.Combine(GetRepositoryRoot().FullName, "src", "IGoLibrary.Ex.Domain");
        var bannedTerms = new[]
        {
            "IGoLibrary.Ex.Application",
            "IGoLibrary.Ex.Infrastructure",
            "IGoLibrary.Ex.Desktop",
            "Avalonia",
            "Toast",
            "Window",
            "GraphQL",
            "GraphQl",
            "SQLite",
            "Sqlite",
            "OperatingSystem",
            "Smtp",
            "SMTP",
            "Telegram",
            "AppSettings"
        };

        foreach (var file in Directory.EnumerateFiles(domainRoot, "*.*", SearchOption.AllDirectories)
                     .Where(IsSourceFile))
        {
            var text = File.ReadAllText(file);
            foreach (var term in bannedTerms)
            {
                Assert.DoesNotContain(term, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void StateMachineTypes_DoNotInjectServicesOrRuntimeAdapters()
    {
        var appServicesRoot = Path.Combine(GetRepositoryRoot().FullName, "src", "IGoLibrary.Ex.Application", "Services");
        var stateMachineFiles = new[]
        {
            Path.Combine(appServicesRoot, "GrabSeatStateMachine.cs"),
            Path.Combine(appServicesRoot, "GlobalLeakStateMachine.cs"),
            Path.Combine(appServicesRoot, "OccupySeatStateMachine.cs")
        };
        var bannedTerms = new[]
        {
            "ISettingsService",
            "ITraceIntApiClient",
            "IActivityLogService",
            "ICoordinatorEventPublisher",
            "AppRuntimeState",
            "ICoordinatorRuntime"
        };

        foreach (var file in stateMachineFiles)
        {
            var text = File.ReadAllText(file);
            foreach (var term in bannedTerms)
            {
                Assert.DoesNotContain(term, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void ApplicationProject_DoesNotReferenceDesktopOrInfrastructureImplementations()
    {
        var applicationRoot = Path.Combine(GetRepositoryRoot().FullName, "src", "IGoLibrary.Ex.Application");
        var bannedTerms = new[]
        {
            "Avalonia",
            "Toast",
            "Window",
            "SQLite",
            "Sqlite",
            "IGoLibrary.Ex.Desktop",
            "IGoLibrary.Ex.Infrastructure"
        };

        foreach (var file in Directory.EnumerateFiles(applicationRoot, "*.*", SearchOption.AllDirectories)
                     .Where(IsSourceFile))
        {
            var text = File.ReadAllText(file);
            foreach (var term in bannedTerms)
            {
                Assert.DoesNotContain(term, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void TraceIntApiClient_DoesNotParseBusinessJsonDirectly()
    {
        var file = Path.Combine(
            GetRepositoryRoot().FullName,
            "src",
            "IGoLibrary.Ex.Infrastructure",
            "Api",
            "TraceIntApiClient.cs");
        var text = File.ReadAllText(file);

        Assert.DoesNotContain("JsonDocument", text, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonElement", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowViewModelShellFiles_StaySmallAndDoNotContainPageBusinessCommands()
    {
        var viewModelRoot = Path.Combine(GetRepositoryRoot().FullName, "src", "IGoLibrary.Ex.Desktop", "ViewModels");
        var files = Directory.EnumerateFiles(viewModelRoot, "MainWindowViewModel*.cs", SearchOption.TopDirectoryOnly)
            .ToArray();
        var lineCount = files.Sum(file => File.ReadLines(file).Count());

        Assert.True(
            lineCount <= 600,
            $"MainWindowViewModel*.cs should keep shell responsibilities and stay below 600 lines; actual line count: {lineCount}.");

        var bannedPageCommandTerms = new[]
        {
            "AuthenticateFrom",
            "LoadLibraries",
            "StartGrab",
            "StartOccupy",
            "SendTest",
            "SaveProtocol",
            "GetCookieFromLink",
            "ValidateManualCookie"
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("[RelayCommand]", text, StringComparison.Ordinal);
            foreach (var term in bannedPageCommandTerms)
            {
                Assert.DoesNotContain(term, text, StringComparison.Ordinal);
            }
        }
    }

    private static IEnumerable<string> EnumerateProductionFiles(string repositoryRoot)
    {
        var srcRoot = Path.Combine(repositoryRoot, "src");
        return Directory.EnumerateFiles(srcRoot, "*.*", SearchOption.AllDirectories)
            .Where(IsSourceFile)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSourceFile(string path)
    {
        return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase);
    }

    private static DirectoryInfo GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
