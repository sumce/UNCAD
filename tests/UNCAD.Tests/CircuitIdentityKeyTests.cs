using System;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public class CircuitIdentityKeyTests
    {
        private static MachineRow Row(string machine, string circuit, string upstream)
        {
            return new MachineRow { MachineId = machine, CircuitName = circuit, Fr = upstream };
        }

        [Fact]
        public void Of_StartsWithVersionedPrefix()
        {
            string key = CircuitIdentityKey.Of(Row("MPILT01", "插座1", "3U-UPS9D1-PP-01-LP-01"));
            Assert.StartsWith("ck1:", key);
            Assert.Equal("ck1:".Length + 24, key.Length);
        }

        [Fact]
        public void Of_IsStableAcrossWidthCaseAndWhitespace()
        {
            string a = CircuitIdentityKey.Of(Row("MPILT01", "插座 1", "3U-UPS9D1-PP-01-LP-01"));
            string b = CircuitIdentityKey.Of(Row("ｍｐｉｌｔ０１", " 插座1 ", "3Ｕ-ＵＰＳ9Ｄ1-ＰＰ-01-ＬＰ-01"));
            Assert.Equal(a, b);
        }

        [Fact]
        public void Of_IgnoresUpstreamTrailingAnnotation()
        {
            string a = CircuitIdentityKey.Of(Row("MPBAC07", "THC", "2U-UPS5D1-PP-02"));
            string b = CircuitIdentityKey.Of(Row("MPBAC07", "THC", "2U-UPS5D1-PP-02 40-42(41/Ab)"));
            Assert.Equal(a, b);
        }

        [Fact]
        public void Of_DistinguishesSameNameByUpstream()
        {
            string a = CircuitIdentityKey.Of(Row("MWCRD02", "ECS", "3N-11B1A-PP-02"));
            string b = CircuitIdentityKey.Of(Row("MWCRD02", "ECS", "3N-31D2A-PP-03"));
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Of_DistinguishesCircuitNameParentheses()
        {
            // “AC Rack” 和 “AC Rack (630A)” 是同一机台下两条不同回路（在线档案实据）。
            string a = CircuitIdentityKey.Of(Row("MEDPC01", "AC Rack", "2N-31F1A-PP-01"));
            string b = CircuitIdentityKey.Of(Row("MEDPC01", "AC Rack (630A)", "2N-31F1A-PP-01"));
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Of_EmptyUpstreamStillProducesKey()
        {
            string key = CircuitIdentityKey.Of(Row("M01", "LP", ""));
            Assert.StartsWith("ck1:", key);
        }

        [Fact]
        public void Of_EmptyUpstreamDiffersFromFilledUpstream()
        {
            string a = CircuitIdentityKey.Of(Row("M01", "LP", ""));
            string b = CircuitIdentityKey.Of(Row("M01", "LP", "2N-31D2A-PP-01"));
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Of_NullRowThrows()
        {
            Assert.Throws<ArgumentNullException>(() => CircuitIdentityKey.Of(null));
        }

        [Fact]
        public void FindDuplicateNotes_ReportsSameKeyRowsWithRowNumbers()
        {
            var notes = CircuitIdentityKey.FindDuplicateNotes(new[]
            {
                (12, new MachineRow { MachineId = "M01", CircuitName = "ECS", Fr = "3N-11B1A-PP-02" }),
                (13, new MachineRow { MachineId = "M01", CircuitName = "ECS", Fr = "3N-11B1A-PP-02（51/Ac）" }),
                (14, new MachineRow { MachineId = "M01", CircuitName = "ECS", Fr = "3N-31D2A-PP-03" }),
                (15, new MachineRow { MachineId = "M02", CircuitName = "ECS", Fr = "3N-11B1A-PP-02" })
            });

            string note = Assert.Single(notes);
            Assert.Contains("M01", note);
            Assert.Contains("ECS", note);
            Assert.Contains("2 行", note);
            Assert.Contains("12", note);
            Assert.Contains("13", note);
        }

        [Fact]
        public void FindDuplicateNotes_EmptyWhenAllDistinct()
        {
            var notes = CircuitIdentityKey.FindDuplicateNotes(new[]
            {
                (10, new MachineRow { MachineId = "M01", CircuitName = "ECS", Fr = "3N-A-PP-01" }),
                (11, new MachineRow { MachineId = "M01", CircuitName = "ECS", Fr = "3N-B-PP-01" })
            });

            Assert.Empty(notes);
        }
    }
}
