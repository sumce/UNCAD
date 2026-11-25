using System;
using System.IO;
using UNCAD.Core.IO;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class AtomicFileWriterTests
    {
        [Fact]
        public void Replace_TargetMissing_MovesTemporaryIntoPlace()
        {
            string folder = NewFolder();
            string target = Path.Combine(folder, "target.txt");
            string temporary = Path.Combine(folder, "temporary.txt");
            try
            {
                File.WriteAllText(temporary, "new");
                AtomicFileWriter.Replace(target, temporary);

                Assert.False(File.Exists(temporary));
                Assert.Equal("new", File.ReadAllText(target));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void Replace_TargetExists_ReplacesAndRemovesTransientBackup()
        {
            string folder = NewFolder();
            string target = Path.Combine(folder, "target.txt");
            string temporary = Path.Combine(folder, "temporary.txt");
            try
            {
                File.WriteAllText(target, "old");
                File.WriteAllText(temporary, "new");
                AtomicFileWriter.Replace(target, temporary);

                Assert.Equal("new", File.ReadAllText(target));
                Assert.False(File.Exists(temporary));
                Assert.Empty(Directory.GetFiles(folder, "*.uncad-replace-*.bak"));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void Replace_TargetMissing_AllowsCallerToRejectTheConflict()
        {
            string folder = NewFolder();
            string target = Path.Combine(folder, "target.txt");
            string temporary = Path.Combine(folder, "temporary.txt");
            try
            {
                File.WriteAllText(temporary, "new");
                Assert.Throws<InvalidOperationException>(() =>
                    AtomicFileWriter.Replace(target, temporary,
                        () => throw new InvalidOperationException("target disappeared")));

                Assert.False(File.Exists(target));
                Assert.True(File.Exists(temporary));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void Replace_TargetExists_DoesNotRunFallbackVerifierOnSuccess()
        {
            string folder = NewFolder();
            string target = Path.Combine(folder, "target.txt");
            string temporary = Path.Combine(folder, "temporary.txt");
            try
            {
                File.WriteAllText(target, "old");
                File.WriteAllText(temporary, "new");
                bool verified = false;
                AtomicFileWriter.Replace(target, temporary, null,
                    () => verified = true);

                Assert.False(verified, "成功替换不应进入降级路径");
            }
            finally { Directory.Delete(folder, true); }
        }

        private static string NewFolder()
        {
            string path = Path.Combine(Path.GetTempPath(),
                "uncad_atomic_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
