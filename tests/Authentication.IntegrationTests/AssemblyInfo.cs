using Xunit;

// Factories bind configuration through process environment variables, so scenarios run serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
