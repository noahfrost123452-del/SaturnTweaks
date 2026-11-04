using Xunit;

namespace SaturnTweaks.Tests.Unit;

/// <summary>
/// Registry tests share HKCU with one another and some restore/delete test keys.
/// Keep them in a non-parallel collection so one fixture cannot delete another's
/// test key while that test is applying or verifying a registry tweak.
/// </summary>
[CollectionDefinition("Registry tests", DisableParallelization = true)]
public sealed class RegistryTestsCollection
{
    public const string Name = "Registry tests";
}
