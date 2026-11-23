using System.Collections.Generic;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class XLayoutFilterListTests
    {
        private static XLayoutFrameItem Item(string machine, string circuit,
            string handle = "h")
            => new XLayoutFrameItem(machine, circuit,
                new FrameRectangle(handle, 0, 0, 100, 100), handle);

        [Fact]
        public void MachineAndCircuitEntry_MatchesOnlyThatCircuit()
        {
            var entry = new XLayoutFilterEntry("MTPWM04", "Scrubber - 2");
            Assert.True(XLayoutFilterList.Matches(entry,
                Item("MTPWM04", "Scrubber - 2")));
            Assert.False(XLayoutFilterList.Matches(entry,
                Item("MTPWM04", "LP")));
            Assert.False(XLayoutFilterList.Matches(entry,
                Item("MTPWM05", "Scrubber - 2")));
        }

        [Fact]
        public void MachineLevelEntry_MatchesEveryCircuitOfThatMachine()
        {
            var entry = new XLayoutFilterEntry("MTPWM04", "");
            Assert.True(entry.IsMachineLevel);
            Assert.True(XLayoutFilterList.Matches(entry, Item("MTPWM04", "LP")));
            Assert.True(XLayoutFilterList.Matches(entry, Item("MTPWM04", "PM")));
            Assert.False(XLayoutFilterList.Matches(entry, Item("MTPWM05", "LP")));
        }

        [Fact]
        public void Matching_IgnoresInternalWhitespacePerD021()
        {
            var entry = new XLayoutFilterEntry("M Q-01", "设备 A");
            Assert.True(XLayoutFilterList.Matches(entry, Item("MQ-01", "设备A")));
            Assert.True(XLayoutFilterList.Matches(entry, Item(" mq-01 ", " 设备A ")));
        }

        [Fact]
        public void IsMatched_TrueWhenAnyEntryHits()
        {
            var list = new XLayoutFilterList(new[]
            {
                new XLayoutFilterEntry("M1", "A"),
                new XLayoutFilterEntry("M2", "")
            }, "list.xlsx");
            Assert.True(list.IsMatched(Item("M1", "A")));
            Assert.True(list.IsMatched(Item("M2", "anything")));
            Assert.False(list.IsMatched(Item("M1", "B")));
            Assert.False(list.IsMatched(Item("M3", "A")));
        }

        [Fact]
        public void MatchAll_KeepsListOrderAndCollectsEveryHit()
        {
            var list = new XLayoutFilterList(new[]
            {
                new XLayoutFilterEntry("M1", "A"),
                new XLayoutFilterEntry("M9", "Ghost"),
                new XLayoutFilterEntry("M2", "")
            }, "list.csv");
            var items = new List<XLayoutFrameItem>
            {
                Item("M1", "A", "h1"),
                Item("M1", "A", "h1d"),
                Item("M2", "X", "h2"),
                Item("M3", "A", "h3")
            };

            IReadOnlyList<XLayoutFilterMatch> matches = list.MatchAll(items);

            Assert.Equal(3, matches.Count);
            Assert.Equal("M1", matches[0].Entry.MachineId);
            Assert.Equal(2, matches[0].Items.Count);
            Assert.False(matches[1].Found);
            Assert.True(matches[2].Found);
            Assert.Single(matches[2].Items);
        }
    }
}
