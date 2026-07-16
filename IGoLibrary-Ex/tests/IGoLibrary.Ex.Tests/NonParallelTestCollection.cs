namespace IGoLibrary.Ex.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NonParallelTestCollection
{
    public const string Name = "NonParallel";
}
