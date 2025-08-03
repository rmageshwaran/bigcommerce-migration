using Xunit;

namespace BigCommerce.Migration.UnitTests;

/// <summary>
/// Collection definition to disable parallel execution for test classes that have isolation issues
/// when running in parallel with other tests. These tests pass individually but fail when run
/// in parallel due to shared state, race conditions, or resource contention.
/// 
/// Test classes marked with [Collection("NonParallelCollection")] will run sequentially.
/// </summary>
[CollectionDefinition("NonParallelCollection", DisableParallelization = true)]
public class NonParallelCollectionDefinition
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}