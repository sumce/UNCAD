using System.Linq;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class XstsReportTests
    {
        [Fact]
        public void Build_DeduplicatesSelectedCircuitsAndReportsMissingExpected()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[]
                {
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M1", "B")
                },
                new[]
                {
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M1", "B"),
                    new XstsCircuitRecord("M1", "C")
                });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.Equal(2, machine.SelectedCircuitCount);
            Assert.Equal(3, machine.ExpectedCircuitCount);
            Assert.Equal("C", Assert.Single(machine.MissingCircuits));
            Assert.Equal(1, report.MissingCircuitCount);
        }

        [Fact]
        public void Build_IsCaseInsensitiveAndIgnoresIncompleteRows()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("m1", " a "), new XstsCircuitRecord("", "B") },
                new[] { new XstsCircuitRecord("M1", "A") });

            XstsMachineSummary machine = Assert.Single(report.Machines);
            Assert.Equal("m1", machine.MachineId);
            Assert.Equal(1, machine.SelectedCircuitCount);
            Assert.Empty(machine.MissingCircuits);
        }

        [Fact]
        public void Build_DoesNotAddMachinesThatWereNotSelected()
        {
            XstsReport report = XstsReportBuilder.Build(
                new[] { new XstsCircuitRecord("M1", "A") },
                new[]
                {
                    new XstsCircuitRecord("M1", "A"),
                    new XstsCircuitRecord("M2", "B")
                });

            Assert.Single(report.Machines);
            Assert.Equal("M1", report.Machines[0].MachineId);
        }
    }
}
