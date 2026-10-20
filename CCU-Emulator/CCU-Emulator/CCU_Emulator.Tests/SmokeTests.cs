using Xunit;

namespace CCU_Emulator.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreAssemblyLoads() => Assert.NotNull(typeof(CCU_Emulator.App).Assembly);
}
