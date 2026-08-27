using System;
using System.IO;
using UNCAD.Core.Report;
using UNCAD.Core.Stat;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>Excel 报表导出测试：生成文件是合法 xlsx（ZIP 头 + 可再次打开）。</summary>
    public class StatExcelReporterTests
    {
        [Fact]
        public void WriteToFile_ProducesValidXlsx()
        {
            var result = new CableStatResult();
            result.CableFormatted.Add("1.2");
            result.CableSum = 1.2;
            var b = new BridgeStat { Spec = "桥架200*100", MmPerGrid = 250.0 };
            b.Grids.Add(10.0);
            result.Bridges.Add(b);
            var c = new ConduitStat { Spec = "⌀20线管" };
            c.LengthsMm.Add(2000.0);
            c.LengthsMm.Add(2500.0);
            result.Conduits.Add(c);

            string path = Path.Combine(Path.GetTempPath(), "uncad_test_" + Guid.NewGuid().ToString("N") + ".xlsx");
            try
            {
                StatExcelReporter.WriteToFile(result, path);

                Assert.True(File.Exists(path));
                var bytes = File.ReadAllBytes(path);
                // xlsx = ZIP，必须以 PK 开头
                Assert.True(bytes.Length > 2);
                Assert.True(bytes[0] == (byte)'P' && bytes[1] == (byte)'K', "xlsx 必须以 PK (ZIP) 头开头");

                // 可再次打开解析（不抛异常即合法）
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    var wb = new NPOI.XSSF.UserModel.XSSFWorkbook(fs);
                    Assert.NotNull(wb.GetSheet("统计"));
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
