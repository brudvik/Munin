using Xunit;

namespace Munin.Core.Tests;

/// <summary>
/// Collection definition to force tests to run sequentially.
/// </summary>
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class SequentialCollectionDefinition
{
}
