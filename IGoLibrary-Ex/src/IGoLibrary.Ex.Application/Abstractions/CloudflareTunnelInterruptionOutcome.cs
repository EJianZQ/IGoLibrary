namespace IGoLibrary.Ex.Application.Abstractions;

public enum CloudflareTunnelInterruptionOutcome
{
    FellBackToLocalNetwork = 0,
    FellBackToLocalNetworkWithPersistenceFailure = 1,
    TunnelModeRetained = 2
}
