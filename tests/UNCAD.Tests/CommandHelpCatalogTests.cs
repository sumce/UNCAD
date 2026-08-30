using System.Linq;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    public class CommandHelpCatalogTests
    {
        [Fact]
        public void EveryRegisteredCommandHasOneHelpEntry()
        {
            Assert.Equal(CommandIds.Registered.Count, CommandHelpCatalog.All.Count);
            Assert.Equal(CommandIds.Registered.Count,
                CommandHelpCatalog.All.Select(entry => entry.Command)
                    .Distinct(System.StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(CommandIds.Registered, command =>
            {
                CommandHelpEntry entry = CommandHelpCatalog.Find(command);
                Assert.NotNull(entry);
                Assert.False(string.IsNullOrWhiteSpace(entry.Usage));
                Assert.False(string.IsNullOrWhiteSpace(entry.Function));
                Assert.False(string.IsNullOrWhiteSpace(entry.Notes));
            });
        }
    }
}
