using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public class SourceDocumentationTests
    {
        [Fact]
        public void FillWorkflow_DocumentsEveryBusinessStageAndCableInvariant()
        {
            string feature = File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Fill", "FillFeature.cs"));
            // 2.6.4 起 BOQ 输出独立成阶段7（CAD 提交之后再写），阶段数随之增加。
            for (int stage = 1; stage <= 7; stage++)
                Assert.Contains("阶段" + stage + "：", feature);
            Assert.Contains("Machine.Cable remains the source-device value", feature);

            string standard = File.ReadAllText(RepoFile("docs", "DEVELOPMENT.md"));
            Assert.Contains("## 注释硬性要求", standard);
            Assert.Contains("状态分离、回滚、缓存、兼容和异常分支", standard);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            for (int depth = 0; depth < 8; depth++)
            {
                string candidate = Path.Combine(root,
                    Path.Combine(parts));
                if (File.Exists(candidate)) return candidate;
                DirectoryInfo parent = Directory.GetParent(root);
                if (parent == null) break;
                root = parent.FullName;
            }
            throw new FileNotFoundException("Repository file not found: "
                + Path.Combine(parts));
        }
    }
}
