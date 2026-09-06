using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using UNCAD.Infra;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class StartupSplashFormTests
    {
        [Theory]
        [InlineData("acad", true, true)]
        [InlineData("acad", false, false)]
        [InlineData("accoreconsole", true, false)]
        [InlineData("AcCoreConsole.exe", true, false)]
        public void StartupUi_IsLimitedToInteractiveFullAutoCad(
            string processName, bool userInteractive, bool expected)
        {
            Assert.Equal(expected, StartupSplashForm.SupportsHost(processName, userInteractive));
        }

        [Fact]
        public void Splash_UsesWebViewWithNativeFallbackAtRequestedScreenBounds()
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var bounds = new Rectangle(12, 34, 800, 600);
                    using (var form = new StartupSplashForm(bounds))
                    {
                        Assert.Equal(bounds, form.Bounds);
                        Assert.Equal(FormBorderStyle.None, form.FormBorderStyle);
                        Assert.True(form.TopMost);
                        Assert.Empty(FindAll<WebBrowser>(form));
                        Assert.Single(FindAll<WebView2>(form));
                        StartupSplashCanvas canvas = Assert.Single(
                            FindAll<StartupSplashCanvas>(form));
                        Assert.Equal(2400, canvas.ParticleCount);
                    }
                }
                catch (Exception ex) { failure = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (failure != null) throw failure;
        }

        [Fact]
        public void EmbeddedWebAnimation_IsHostTimedAndIncludesVersion()
        {
            string html = StartupSplashForm.LoadSplashHtml();

            Assert.Equal(5000, StartupSplashForm.AutoCloseMilliseconds);
            Assert.Contains("id=\"particleCanvas\"", html);
            Assert.Contains("id=\"loaderContainer\"", html);
            Assert.Contains("class=\"win-loader\"", html);
            Assert.Contains("UNCAD Pro", html);
            Assert.Contains("v" + ProductMetadata.VersionText, html);
            Assert.DoesNotContain("{{VERSION}}", html);
            Assert.DoesNotContain("window.close", html);
        }

        private static List<T> FindAll<T>(Control root) where T : Control
        {
            var result = new List<T>();
            foreach (Control child in root.Controls)
            {
                if (child is T match) result.Add(match);
                result.AddRange(FindAll<T>(child));
            }
            return result;
        }
    }
}
