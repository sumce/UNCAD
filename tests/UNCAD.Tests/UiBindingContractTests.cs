using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 2026-11-04 修复：WPF 数据绑定只解析 public 属性。多个结果窗的标题/
    /// 汇总/页脚用 internal 表达式属性，却在 XAML 里经
    /// {Binding X, RelativeSource={RelativeSource AncestorType=ui:FluentWindow}}
    /// 绑定——绑定静默失败，文字渲染为空。本契约扫描 src/UNCAD/UI/Wpf 的
    /// 全部 XAML：凡经 FluentWindow AncestorType 绑定的属性，必须在同名
    /// .xaml.cs 中以 public 声明，且不得以 internal 声明回流。
    /// </summary>
    public sealed class UiBindingContractTests
    {
        private static readonly Regex AncestorBinding = new Regex(
            @"\{Binding\s+(?:Path=)?(?<property>\w+)\s*,\s*RelativeSource=\{RelativeSource\s+AncestorType=[A-Za-z_:]+FluentWindow\s*\}");

        [Fact]
        public void AncestorFluentWindowBindings_AreDeclaredPublicInCodeBehind()
        {
            string wpfDirectory = Repo("src", "UNCAD", "UI", "Wpf");
            List<string> violations = new List<string>();
            foreach (string xamlPath in Directory.GetFiles(wpfDirectory, "*.xaml"))
            {
                string codePath = Path.ChangeExtension(xamlPath, ".xaml.cs");
                string fileName = Path.GetFileName(xamlPath);
                string xaml = File.ReadAllText(xamlPath);
                foreach (string property in AncestorBindingPropertyNames(xaml))
                {
                    if (!File.Exists(codePath))
                    {
                        violations.Add(fileName + " → " + property
                            + "（找不到同名 .xaml.cs）");
                        continue;
                    }
                    string code = File.ReadAllText(codePath);
                    if (!PublicDeclaration(property).IsMatch(code))
                        violations.Add(fileName + " → " + property
                            + "（同名 .xaml.cs 缺少 public 声明，WPF 绑定只解析 public 属性）");
                    if (InternalDeclaration(property).IsMatch(code))
                        violations.Add(fileName + " → " + property
                            + "（同名 .xaml.cs 仍存在 internal 声明，绑定会静默失败）");
                }
            }
            Assert.True(violations.Count == 0,
                "以下 AncestorType=FluentWindow 绑定的属性未以 public 声明："
                + Environment.NewLine + string.Join(Environment.NewLine, violations));
        }

        [Fact]
        public void SpotCheck_KeyWindowsExposeBoundPropertiesAsPublic()
        {
            string xsts = File.ReadAllText(Repo("src", "UNCAD", "UI", "Wpf",
                "XstsWindow.xaml.cs"));
            Assert.Contains("public string Heading", xsts);

            string about = File.ReadAllText(Repo("src", "UNCAD", "UI", "Wpf",
                "AboutWindow.xaml.cs"));
            Assert.Contains("public string Footer", about);
        }

        private static List<string> AncestorBindingPropertyNames(string xaml)
        {
            List<string> names = new List<string>();
            foreach (Match match in AncestorBinding.Matches(xaml))
                names.Add(match.Groups["property"].Value);
            return names.Distinct().ToList();
        }

        private static Regex PublicDeclaration(string property)
        {
            // 容忍表达式属性（=>）与 get 访问器属性（{）。
            return new Regex(@"\bpublic\s+[\w<>,\.\[\]\? ]+\s+" + property
                + @"\b\s*(=>|\{)");
        }

        private static Regex InternalDeclaration(string property)
        {
            return new Regex(@"\binternal\s+[\w<>,\.\[\]\? ]+\s+" + property
                + @"\b\s*(=>|\{)");
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new string[] { root }.Concat(parts).ToArray());
        }
    }
}
