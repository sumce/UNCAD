using System.Linq;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class CommandRegistrationTests
    {
        [Fact]
        public void Catalog_UsesGenerationAndBatchUpdateWithoutSubmitCommand()
        {
            Assert.Equal(21, CommandIds.Canonical.Count);
            Assert.Equal(10, CommandIds.Legacy.Count);
            Assert.Equal(31, CommandIds.Registered.Count);
            Assert.Equal(31, CommandIds.Registered.Distinct(
                System.StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains("UNC_F", CommandIds.Registered);
            Assert.Contains("UNC_UPDATE", CommandIds.Registered);
            Assert.DoesNotContain("UNC_SUBMIT", CommandIds.Registered);
            Assert.DoesNotContain("UNC_FILL", CommandIds.Registered);
            Assert.DoesNotContain("UNC_FILL_UPDATE", CommandIds.Registered);
        }

    }
}
