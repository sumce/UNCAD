using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.0.0.17 机台身份守卫（现场报告：同事反馈“U1F 会计算复制过来之前的
    /// 预留米数”）。旧 frameinfo_json 记录与 CABLE_INFO 建议值属于「图框原来
    /// 的那台机」；U1F 改选别的机台（复制改号/换机台重定义）时必须整体丢弃，
    /// 同机台三个场景（重跑 U1F / 同机台第二只框 / 跨文件搬场）不受影响。
    /// </summary>
    public sealed class FrameRedefineGuardTests
    {
        private static ExistingFillIdentity Identity(string machine, string device)
            => new ExistingFillIdentity { MachineId = machine, DeviceName = device };

        private static MachineRow Machine(string machine, string circuit)
            => new MachineRow { MachineId = machine, CircuitName = circuit };

        [Fact]
        public void RedefinedMachine_DropsHintAndRecord()
        {
            // 同事报告的场景：复制框（物理身份=源机台 A）跑 U1F 选了新机台 B
            // ——源框的 3000 建议值不得预填进 B 的清单确认窗，旧记录不得进 B 的 JSON。
            double? hint = 3000d;
            var previous = new FrameInfoJsonRecord
            {
                MachineId = "MQ-01", DeviceName = "A-1", DeviceReserveMm = "3000"
            };
            FrameInfoJsonRecord result = FrameRedefineGuard.Apply(
                Identity("MQ-01", "A-1"), previous, Machine("MQ-02", "B-1"),
                ref hint, out string note);
            Assert.Null(result);
            Assert.Null(hint);
            Assert.Contains("MQ-01", note);   // 点名原机台，丢弃不是静默的
            Assert.Contains("MQ-02", note);
        }

        [Fact]
        public void SameMachine_KeepsRecordAndHint()
        {
            // 同机台第二只框 / 重跑 U1F：建议值与记录保留（3000 本来就是这台的）。
            double? hint = 3000d;
            var previous = new FrameInfoJsonRecord
            {
                MachineId = "MQ-01", DeviceName = "A-1", DeviceReserveMm = "3000"
            };
            FrameInfoJsonRecord result = FrameRedefineGuard.Apply(
                Identity("MQ-01", "A-1"), previous, Machine("MQ-01", "A-1"),
                ref hint, out string note);
            Assert.Same(previous, result);
            Assert.Equal(3000d, hint);
            Assert.Equal("", note);
        }

        [Fact]
        public void SameMachine_GhostRecordIsDropped()
        {
            // 物理身份与所选机台一致，但记录指向别的机台（幽灵残留）：丢弃，
            // 不能让 A 机的记录给 A 机的框写 B 机的历史。
            double? hint = null;
            var ghost = new FrameInfoJsonRecord { MachineId = "MQ-09" };
            FrameInfoJsonRecord result = FrameRedefineGuard.Apply(
                Identity("MQ-01", "A-1"), ghost, Machine("MQ-01", "A-1"),
                ref hint, out string note);
            Assert.Null(result);
            Assert.Contains("MQ-09", note);
        }

        [Fact]
        public void FreshFrame_WithoutIdentity_KeepsEverything()
        {
            // 全新图框没有物理身份：外来记录早已被句柄校验拒绝，守卫不得再动什么。
            double? hint = null;
            var previous = new FrameInfoJsonRecord
            {
                MachineId = "MQ-01", DeviceReserveMm = "3000"
            };
            FrameInfoJsonRecord result = FrameRedefineGuard.Apply(
                null, previous, Machine("MQ-01", "A-1"), ref hint, out string note);
            Assert.Same(previous, result);
            Assert.Equal("", note);
        }

        [Fact]
        public void LegacyRecord_WithoutDeviceName_CountsAsMatch()
        {
            // v1 旧记录只有机台ID没有设备名：按机台匹配，不因缺字段误丢。
            double? hint = null;
            var legacy = new FrameInfoJsonRecord { MachineId = "MQ-01" };
            FrameInfoJsonRecord result = FrameRedefineGuard.Apply(
                Identity("MQ-01", "A-1"), legacy, Machine("MQ-01", "A-1"),
                ref hint, out string note);
            Assert.Same(legacy, result);
            Assert.Equal("", note);
        }

        [Fact]
        public void RedefinedMachine_NoteNamesSourceEvenWithoutRecord()
        {
            // 复制框外来 JSON 被拒（记录=null）但建议值在：改选机台仍要丢建议值
            // 并提示按新机台重填（这正是 2026-11-12 现场 18:23 那次 U1F 的形态）。
            double? hint = 3000d;
            FrameInfoJsonRecord result = FrameRedefineGuard.Apply(
                Identity("MQ-01", "A-1"), null, Machine("MQ-02", "B-1"),
                ref hint, out string note);
            Assert.Null(result);
            Assert.Null(hint);
            Assert.Contains("重新定义", note);
        }
    }
}
