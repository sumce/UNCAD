using System.Linq;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class CommandRegistrationTests
    {
        [Fact]
        public void Catalog_ContainsTwentyTwoCanonicalAndTenLegacyCommands()
        {
            Assert.Equal(22, CommandIds.Canonical.Count);
            Assert.Equal(10, CommandIds.Legacy.Count);
            Assert.Equal(32, CommandIds.Registered.Count);
            Assert.Equal(32, CommandIds.Registered.Distinct(
                System.StringComparer.OrdinalIgnoreCase).Count());
            Assert.DoesNotContain("UNC_F", CommandIds.Registered);
            Assert.Contains("UNC_FILL", CommandIds.Registered);
        }

    }
}
