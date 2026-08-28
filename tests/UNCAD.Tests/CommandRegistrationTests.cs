using System.Linq;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class CommandRegistrationTests
    {
        [Fact]
        public void Catalog_ContainsOnlyU1AndRetainedTraditionalCommands()
        {
            Assert.Equal(new[]
            {
                "U1L", "U1R", "U1Q1", "U1Q2", "U1Q4",
                "U1F", "U1U", "U1C", "U1A", "U1S"
            }, CommandIds.Canonical);
            Assert.Equal(new[] { "UNL", "UNR", "UNQ1", "UNQ2", "UNQ4", "UNADD" },
                CommandIds.Legacy);
            Assert.Equal(16, CommandIds.Registered.Count);
            Assert.Equal(16, CommandIds.Registered.Distinct(
                System.StringComparer.OrdinalIgnoreCase).Count());
            Assert.DoesNotContain(CommandIds.Registered, command =>
                command.StartsWith("UNC_", System.StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(CommandIds.Registered, command =>
                command.StartsWith("OPUN", System.StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("UNADDX", CommandIds.Registered);
        }

    }
}
