using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NPOI.XSSF.UserModel;
using UNCAD.Core.Excel;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class MachineWorkbookSourceTests
    {
        [Fact]
        public async Task Refresh_DownloadsValidatedWorkbookAndFallsBackToCache()
        {
            byte[] workbook = WorkbookBytes();
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string url = "http://127.0.0.1:" + port + "/machine-"
                + Guid.NewGuid().ToString("N") + ".xlsx?sign=test";
            Task server = Task.Run(() =>
            {
                for (int responseIndex = 0; responseIndex < 2; responseIndex++)
                {
                    using (TcpClient client = listener.AcceptTcpClient())
                    using (NetworkStream stream = client.GetStream())
                    {
                        using (var reader = new StreamReader(stream, Encoding.ASCII, false,
                            1024, true))
                        {
                            string line;
                            while (!string.IsNullOrEmpty(line = reader.ReadLine())) { }
                        }
                        byte[] header = Encoding.ASCII.GetBytes(
                            "HTTP/1.1 200 OK\r\nContent-Length: " + workbook.Length
                            + "\r\nContent-Type: application/octet-stream\r\n"
                            + "Connection: close\r\n\r\n");
                        stream.Write(header, 0, header.Length);
                        stream.Write(workbook, 0, workbook.Length);
                    }
                }
            });

            MachineWorkbookSourceResult downloaded = null;
            try
            {
                Assert.False(MachineWorkbookSource.TryGetCachedPath(url, out _));
                downloaded = MachineWorkbookSource.Refresh(url);
                Assert.True(downloaded.Updated);
                Assert.False(downloaded.UsedCachedFallback);
                Assert.True(MachineWorkbookSource.TryGetCachedPath(url, out string cachedPath));
                Assert.Equal(downloaded.LocalPath, cachedPath);
                Assert.Equal("设备A", Assert.Single(
                    ExcelMachineReader.FindRows(downloaded.LocalPath, "NET01")).CircuitName);

                MachineWorkbookSourceResult unchanged = MachineWorkbookSource.Refresh(url);
                Assert.False(unchanged.Updated);
                Assert.False(unchanged.UsedCachedFallback);
                await server;
                listener.Stop();
                MachineWorkbookSourceResult fallback = MachineWorkbookSource.Refresh(url);
                Assert.True(fallback.UsedCachedFallback);
                Assert.Equal(downloaded.LocalPath, fallback.LocalPath);
            }
            finally
            {
                listener.Stop();
                if (downloaded != null && File.Exists(downloaded.LocalPath))
                    File.Delete(downloaded.LocalPath);
            }
        }

        private static byte[] WorkbookBytes()
        {
            var workbook = new XSSFWorkbook();
            var sheet = workbook.CreateSheet("机台数据");
            string[] headers = { "U_区域", "U_机台ID", "回路名称", "U_电缆型号", "U_上游编号",
                "U_配电信息", "U_序号", "U_软管直径", "U_上游类型", "U_设备楼层",
                "U_设备轴位", "U_上游轴位", "U_上游楼层", "U_厂务开关" };
            string[] values = { "LAB", "NET01", "设备A", "ZB-YJVR-3*2.5", "FR01",
                "U220 1P3W 1P20A", "236", "20", "插座盘", "2F", "2/T", "化学实验室",
                "1F", "1P20A" };
            var header = sheet.CreateRow(0);
            var row = sheet.CreateRow(1);
            for (int index = 0; index < headers.Length; index++)
            {
                header.CreateCell(index).SetCellValue(headers[index]);
                row.CreateCell(index).SetCellValue(values[index]);
            }
            using (var stream = new MemoryStream())
            {
                workbook.Write(stream);
                workbook.Close();
                return stream.ToArray();
            }
        }
    }
}
