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
                if (term == "Window")
                {
                    Assert.DoesNotMatch(@"\bWindow\b", text);
                    continue;
                }

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
                if (term == "Window")
                {
                    Assert.DoesNotMatch(@"\bWindow\b", text);
                    continue;
                }

                Assert.DoesNotContain(term, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void UpdaterCoreProject_UsesOnlyBclAndDoesNotReferenceProductionOrUiLayers()
    {
        var coreRoot = Path.Combine(GetRepositoryRoot().FullName, "src", "IGoLibrary.Ex.Updater.Core");
        var projectText = File.ReadAllText(Path.Combine(coreRoot, "IGoLibrary.Ex.Updater.Core.csproj"));
        Assert.DoesNotContain("ProjectReference", projectText, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference", projectText, StringComparison.Ordinal);

        var bannedTerms = new[]
        {
            "using IGoLibrary.Ex.Domain",
            "using IGoLibrary.Ex.Application",
            "using IGoLibrary.Ex.Infrastructure",
            "using IGoLibrary.Ex.Desktop",
            "Avalonia",
            "System.Windows.Forms"
        };
        foreach (var file in Directory.EnumerateFiles(coreRoot, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (var term in bannedTerms)
            {
                Assert.DoesNotContain(term, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void WindowsUpdaterProject_ReferencesOnlyUpdaterCore()
    {
        var updaterProject = Path.Combine(
            GetRepositoryRoot().FullName,
            "src",
            "IGoLibrary.Ex.Updater",
            "IGoLibrary.Ex.Updater.csproj");
        var text = File.ReadAllText(updaterProject);

        Assert.Contains("IGoLibrary.Ex.Updater.Core", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IGoLibrary.Ex.Domain", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IGoLibrary.Ex.Application", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IGoLibrary.Ex.Infrastructure", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IGoLibrary.Ex.Desktop", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PackageReference", text, StringComparison.Ordinal);
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
    public void TraceIntApiProductionFiles_DoNotHardCodeProtocolUrls()
    {
        var apiRoot = Path.Combine(
            GetRepositoryRoot().FullName,
            "src",
            "IGoLibrary.Ex.Infrastructure",
            "Api");
        var files = Directory.EnumerateFiles(apiRoot, "TraceInt*.cs", SearchOption.AllDirectories)
            .ToArray();

        Assert.NotEmpty(files);
        Assert.Contains(files, static path => Path.GetFileName(path) == "TraceIntApiClient.cs");
        Assert.Contains(files, static path => Path.GetFileName(path) == "TraceIntRemoteCheckInApiClient.cs");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("http://", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ws://", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("wss://", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void MainWindowViewModelShellFiles_StaySmallAndDoNotContainPageBusinessCommands()
    {
        var shellViewModelRoot = Path.Combine(GetRepositoryRoot().FullName, "src", "IGoLibrary.Ex.Desktop", "ViewModels", "Shell");
        var files = Directory.EnumerateFiles(shellViewModelRoot, "MainWindowViewModel*.cs", SearchOption.TopDirectoryOnly)
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

    [Fact]
    public void MigratedMainWindowWorkflowFacadeFiles_DoNotOwnObservableStateOrCommands()
    {
        var viewModelRoot = Path.Combine(GetRepositoryRoot().FullName, "src", "IGoLibrary.Ex.Desktop", "ViewModels");
        var facadeFiles = new[]
        {
            Path.Combine("Shell", "MainWindowWorkflowViewModel.CoordinatorStatusLogs.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.Navigation.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.Notifications.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.ProtocolTemplates.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.UpdatesLinks.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.GlobalLeak.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.Grab.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.TomorrowReservation.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.SeatSelection.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.Venue.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.LanCookieRelay.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.ReservationOccupyAutoRelease.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.Session.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.HomeDashboard.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.Settings.cs"),
            Path.Combine("Shell", "MainWindowWorkflowViewModel.Theme.cs")
        };

        foreach (var fileName in facadeFiles)
        {
            var text = File.ReadAllText(Path.Combine(viewModelRoot, fileName));
            Assert.DoesNotContain("[ObservableProperty]", text, StringComparison.Ordinal);
            Assert.DoesNotContain("[RelayCommand]", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ExtractedPageViewModels_DoNotReferenceMainWindowWorkflowViewModel()
    {
        var viewModelRoot = Path.Combine(GetRepositoryRoot().FullName, "src", "IGoLibrary.Ex.Desktop", "ViewModels");
        var extractedFiles = new[]
        {
            Path.Combine("Components", "ActivityLogPanelViewModel.cs"),
            Path.Combine("Components", "GlobalLeakLibraryPriorityItemViewModel.cs"),
            Path.Combine("Components", "GlobalLeakLibrarySelectionViewModel.cs"),
            Path.Combine("Shell", "ShellNavigationViewModel.cs"),
            Path.Combine("Pages", "NotificationSettingsViewModel.Settings.cs"),
            Path.Combine("Pages", "ProtocolTemplatesViewModel.cs"),
            Path.Combine("Pages", "UpdateLinksViewModel.cs"),
            Path.Combine("Pages", "GlobalLeakPageViewModel.cs"),
            Path.Combine("Pages", "GrabPageViewModel.cs"),
            Path.Combine("Pages", "TomorrowReservationPageViewModel.cs"),
            Path.Combine("Components", "MultiSeatSelectionViewModel.cs"),
            Path.Combine("Components", "MultiSeatSelectionViewModel.Labels.cs"),
            Path.Combine("Pages", "AccountVenueViewModel.cs"),
            Path.Combine("Pages", "LanCookieRelayViewModel.cs"),
            Path.Combine("Pages", "RemoteCheckInPageViewModel.cs"),
            Path.Combine("Pages", "RemoteCheckInPageViewModel.Authorization.cs"),
            Path.Combine("Pages", "RemoteCheckInPageViewModel.Profile.cs"),
            Path.Combine("Pages", "RemoteCheckInPageViewModel.Sign.cs"),
            Path.Combine("Pages", "OccupyPageViewModel.cs"),
            Path.Combine("Pages", "SessionViewModel.cs"),
            Path.Combine("Pages", "HomeDashboardViewModel.cs"),
            Path.Combine("Pages", "SystemSettingsViewModel.cs"),
            Path.Combine("Pages", "StorageSettingsViewModel.cs")
        };

        foreach (var fileName in extractedFiles)
        {
            var text = File.ReadAllText(Path.Combine(viewModelRoot, fileName));
            Assert.DoesNotContain("MainWindowWorkflowViewModel", text, StringComparison.Ordinal);
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
