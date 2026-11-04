using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// WebView2 启动动画 + 原生粒子兜底（3.0 WPF 版，D-038，P6 瞬时提示：
    /// 无交互、无边框全屏）。只换皮不改时序：5 秒自动关闭、WebView2 初始化
    /// 2.5 秒超时回退原生动画、点击/按键跳过，全部与旧 StartupSplashForm
    /// 一致；关闭只走 Close()，不依赖 DialogResult。
    /// </summary>
    public sealed class StartupSplashWindow : Window
    {
        internal const int AutoCloseMilliseconds = 5000;
        private const int WebInitializationTimeoutMilliseconds = 2500;
        /// <summary>
        /// 网页动画接管的最后期限（原生 Logo 于 1450ms 开始淡入）。就绪晚于它
        /// 就保留原生动画播到底——中途换手会让网页动画从 0 帧重播，用户看到的
        /// 就是"启动画面闪一下"（2026-11-04 用户报告）。
        /// </summary>
        internal const int WebTakeoverDeadlineMilliseconds = 1200;
        private const string HtmlResourceName = "UNCAD.Assets.StartupSplash.html";

        private readonly DispatcherTimer _timer =
            new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly StartupSplashCanvas _canvas = new StartupSplashCanvas();
        private readonly System.Drawing.Rectangle _screenBounds;
        private Microsoft.Web.WebView2.Wpf.WebView2 _webView;
        private bool _webStartupPending;
        private bool _webStartupAbandoned;
        private bool _webAnimationRunning;
        private bool _closed;

        internal static bool SupportsHost(string processName, bool userInteractive)
            => userInteractive
                && !string.Equals(processName, "accoreconsole",
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(processName, "accoreconsole.exe",
                    StringComparison.OrdinalIgnoreCase);

        /// <param name="screenBounds">要覆盖的屏幕区域（像素）；空矩形覆盖主屏。</param>
        public StartupSplashWindow(System.Drawing.Rectangle screenBounds)
        {
            _screenBounds = screenBounds;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Topmost = true;
            Background = Brushes.White;
            Title = "UNCAD";

            var layout = new Grid();
            layout.Children.Add(_canvas);
            TryCreateWebView(layout);
            Content = layout;

            _canvas.MouseDown += (sender, args) => Close();
            MouseDown += (sender, args) => Close();
            PreviewKeyDown += (sender, args) => Close();
            ContentRendered += OnShown;
            _timer.Tick += OnTick;
            Closed += OnClosed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Bootstrap 传进来的是 WinForms 像素矩形；WPF 窗口用 DIP，
            // 除以当前显示器缩放后按像素精确覆盖同一区域。
            double scaleX = 1.0, scaleY = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                scaleX = source.CompositionTarget.TransformToDevice.M11;
                scaleY = source.CompositionTarget.TransformToDevice.M22;
            }
            System.Drawing.Rectangle bounds = _screenBounds.IsEmpty
                ? new System.Drawing.Rectangle(0, 0,
                    (int)Math.Round(SystemParameters.PrimaryScreenWidth * scaleX),
                    (int)Math.Round(SystemParameters.PrimaryScreenHeight * scaleY))
                : _screenBounds;
            Left = bounds.X / scaleX;
            Top = bounds.Y / scaleY;
            Width = bounds.Width / scaleX;
            Height = bounds.Height / scaleY;
        }

        private void OnShown(object sender, EventArgs args)
        {
            _clock.Restart();
            _timer.Start();
            InitializeWebViewAsync();
        }

        private void OnTick(object sender, EventArgs args)
        {
            int elapsed = (int)_clock.ElapsedMilliseconds;
            if (elapsed >= AutoCloseMilliseconds)
            {
                Close();
                return;
            }
            if (_webAnimationRunning) return;
            if (_webStartupPending && elapsed >= WebInitializationTimeoutMilliseconds)
                UseNativeFallback("WebView2 启动动画初始化超时", "超过 2.5 秒");

            _canvas.ElapsedMilliseconds = elapsed;
            _canvas.InvalidateVisual();
        }

        private void TryCreateWebView(Grid layout)
        {
            try
            {
                _webView = new Microsoft.Web.WebView2.Wpf.WebView2
                {
                    Visibility = Visibility.Collapsed,
                    DefaultBackgroundColor = System.Drawing.Color.White
                };
                layout.Children.Add(_webView);
                _webStartupPending = true;
            }
            catch (Exception ex)
            {
                Log.Warn("WebView2 启动动画控件创建失败，使用原生动画: " + ex.Message);
                _webView = null;
            }
        }

        private async void InitializeWebViewAsync()
        {
            if (_webView == null) return;
            try
            {
                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "UNCAD", "WebView2");
                CoreWebView2Environment environment =
                    await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                if (_closed || _webStartupAbandoned) return;
                await _webView.EnsureCoreWebView2Async(environment);
                if (_closed || _webStartupAbandoned) return;

                CoreWebView2 core = _webView.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.WebMessageReceived += OnWebMessageReceived;
                core.ProcessFailed += OnWebProcessFailed;
                await core.AddScriptToExecuteOnDocumentCreatedAsync(
                    "addEventListener('pointerdown',()=>chrome.webview.postMessage('close'),true);"
                    + "addEventListener('keydown',()=>chrome.webview.postMessage('close'),true);");
                _webView.NavigationCompleted += OnWebNavigationCompleted;
                _webView.NavigateToString(LoadSplashHtml());
            }
            catch (Exception ex)
            {
                if (_closed || _webStartupAbandoned) return;
                UseNativeFallback("WebView2 启动动画初始化失败", ex.Message);
            }
        }

        private void OnWebNavigationCompleted(object sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!args.IsSuccess)
            {
                UseNativeFallback("WebView2 启动动画加载失败", args.WebErrorStatus.ToString());
                return;
            }
            StartWebAnimation();
        }

        private void OnWebMessageReceived(object sender,
            CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                string message = args.TryGetWebMessageAsString();
                if (string.Equals(message, "close", StringComparison.Ordinal))
                {
                    Close();
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("WebView2 启动动画消息读取失败: " + ex.Message);
            }
        }

        private void OnWebProcessFailed(object sender,
            CoreWebView2ProcessFailedEventArgs args)
            => UseNativeFallback("WebView2 启动动画进程异常", args.ProcessFailedKind.ToString());

        private void StartWebAnimation()
        {
            if (_closed || _webStartupAbandoned || _webView == null) return;
            int elapsed = (int)_clock.ElapsedMilliseconds;
            if (elapsed >= WebTakeoverDeadlineMilliseconds)
            {
                // 换手过晚：原生动画已播到粒子汇聚/Logo 淡入阶段，此刻切成
                // 网页动画会从 0 帧重播一遍——这就是"启动画面闪一下"。保留
                // 原生动画到底（与超时回退同语义），不再接管。
                _webStartupPending = false;
                _webStartupAbandoned = true;
                Log.Info("WebView2 启动动画就绪过晚（" + elapsed
                    + " ms ≥ " + WebTakeoverDeadlineMilliseconds
                    + " ms），保留原生动画避免切换闪烁");
                return;
            }
            _webStartupPending = false;
            _webAnimationRunning = true;
            _webView.Visibility = Visibility.Visible;
            // 换手完成：停掉底层原生粒子绘制，避免边缘露出与重复重绘。
            _canvas.Visibility = Visibility.Collapsed;
            _webView.Focus();
        }

        private void UseNativeFallback(string reason, string detail)
        {
            if (_closed || _webStartupAbandoned) return;
            _webStartupPending = false;
            _webStartupAbandoned = true;
            _webAnimationRunning = false;
            if (_webView != null) _webView.Visibility = Visibility.Collapsed;
            _canvas.InvalidateVisual();
            Log.Warn(reason + "，使用原生动画: " + detail);
        }

        internal static string LoadSplashHtml()
        {
            Assembly assembly = typeof(StartupSplashWindow).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(HtmlResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("启动动画 HTML 资源不存在: "
                        + HtmlResourceName);
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                    return reader.ReadToEnd().Replace(
                        "{{VERSION}}", ProductMetadata.VersionText);
            }
        }

        private void OnClosed(object sender, EventArgs args)
        {
            _closed = true;
            _webStartupAbandoned = true;
            _timer.Stop();
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                _webView.CoreWebView2.ProcessFailed -= OnWebProcessFailed;
            }
            if (_webView != null)
            {
                _webView.NavigationCompleted -= OnWebNavigationCompleted;
                _webView.Dispose();
                _webView = null;
            }
        }
    }

    /// <summary>
    /// 原生粒子启动动画（WPF 版）：粒子从全屏边缘汇聚成品牌标志，再浮出
    /// “UNSIAO”字标与产品说明。粒子布局与旧 GDI+ 版完全一致（同一采样步长、
    /// 同一随机种子），只是渲染 API 从 GDI+ 换成 DrawingContext。
    /// </summary>
    public sealed class StartupSplashCanvas : System.Windows.FrameworkElement
    {
        private const string LogoResourceName = "UNCAD.Assets.unsiao-mark.png";
        private const int MaximumParticles = 2400;
        private readonly BitmapFrame _logo;
        private readonly List<Particle> _particles;

        public StartupSplashCanvas()
        {
            _logo = LoadLogo();
            _particles = BuildParticles(_logo);
        }

        public int ElapsedMilliseconds { get; set; }

        internal int ParticleCount => _particles.Count;

        protected override void OnRender(DrawingContext graphics)
        {
            base.OnRender(graphics);
            double width = ActualWidth;
            double height = ActualHeight;
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            double logoHeight = Math.Max(96d, Math.Min(width, height) * 0.18d);
            double logoWidth = logoHeight * _logo.PixelWidth / _logo.PixelHeight;
            var centeredLogo = new Rect(
                (width - logoWidth) / 2d,
                (height - logoHeight) / 2d,
                logoWidth,
                logoHeight);

            double wordSize = logoHeight * 0.27d;
            var wordFont = new Typeface(new FontFamily(UiTheme.BrandFontFamily),
                FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            double wordWidth = MeasureWord(wordFont, wordSize, "UNSIAO", pixelsPerDip);
            double gap = logoHeight * 0.14d;
            double groupWidth = logoWidth + gap + wordWidth;
            var finalLogo = new Rect(
                (width - groupWidth) / 2d,
                centeredLogo.Top,
                logoWidth,
                logoHeight);
            double shift = Ease(Progress(ElapsedMilliseconds, 1700, 400));
            var visibleLogo = new Rect(
                Lerp(centeredLogo.Left, finalLogo.Left, shift),
                centeredLogo.Top,
                logoWidth,
                logoHeight);

            DrawParticles(graphics, centeredLogo);
            DrawImage(graphics, _logo, visibleLogo,
                Ease(Progress(ElapsedMilliseconds, 1450, 350)));
            DrawWordmark(graphics, wordFont, wordSize, finalLogo.Right + gap,
                finalLogo.Top + (logoHeight - wordSize) / 2d, pixelsPerDip);
            DrawProductCaption(graphics, finalLogo.Left,
                finalLogo.Bottom + logoHeight * 0.18d, groupWidth, logoHeight,
                pixelsPerDip);
        }

        private void DrawParticles(DrawingContext graphics, Rect logoBounds)
        {
            double fade = 1d - Ease(Progress(ElapsedMilliseconds, 1450, 450));
            if (fade <= 0d) return;

            int alpha = Math.Max(0, Math.Min(220, (int)(220d * fade)));
            var brush = new SolidColorBrush(Color.FromArgb((byte)alpha, 10, 10, 10));
            brush.Freeze();
            double centerX = ActualWidth / 2d;
            double centerY = ActualHeight / 2d;
            double radius = Math.Sqrt(ActualWidth * ActualWidth
                + ActualHeight * ActualHeight) * 0.72d;
            double sizeScale = Math.Sqrt(Math.Max(0.5d, logoBounds.Height / 180d));
            foreach (Particle particle in _particles)
            {
                double progress = Progress(ElapsedMilliseconds,
                    particle.DelayMilliseconds, particle.DurationMilliseconds);
                if (progress <= 0d) continue;
                double eased = Ease(progress);
                double targetX = logoBounds.Left + particle.Target.X * logoBounds.Width;
                double targetY = logoBounds.Top + particle.Target.Y * logoBounds.Height;
                double startX = centerX + particle.StartDirection.X * radius;
                double startY = centerY + particle.StartDirection.Y * radius;
                double bendX = -(targetY - startY) * particle.Bend;
                double bendY = (targetX - startX) * particle.Bend;
                double remaining = 1d - eased;
                double middle = 2d * remaining * eased;
                double x = remaining * remaining * startX
                    + middle * ((startX + targetX) / 2d + bendX)
                    + eased * eased * targetX;
                double y = remaining * remaining * startY
                    + middle * ((startY + targetY) / 2d + bendY)
                    + eased * eased * targetY;
                double size = particle.Size * sizeScale * Math.Min(1d, progress * 4d);
                graphics.DrawRectangle(brush, null,
                    new Rect(x - size / 2d, y - size / 2d, size, size));
            }
        }

        private void DrawWordmark(DrawingContext graphics, Typeface font, double fontSize,
            double left, double top, double pixelsPerDip)
        {
            const string word = "UNSIAO";
            double x = left;
            for (int index = 0; index < word.Length; index++)
            {
                string letter = word[index].ToString();
                double progress = Ease(Progress(ElapsedMilliseconds, 1850 + index * 70, 480));
                double letterWidth = MeasureWord(font, fontSize, letter, pixelsPerDip);
                if (progress > 0d)
                {
                    var brush = new SolidColorBrush(Color.FromArgb(
                        (byte)(255 * progress), 10, 10, 10));
                    brush.Freeze();
                    var text = MakeText(font, fontSize, letter, brush, pixelsPerDip);
                    graphics.DrawText(text,
                        new System.Windows.Point(x + (1d - progress) * fontSize * 0.9d,
                            top + (1d - progress) * fontSize * 0.15d));
                }
                x += letterWidth - fontSize * 0.08d;
            }
        }

        private static void DrawImage(DrawingContext graphics, ImageSource image,
            Rect bounds, double alpha)
        {
            if (alpha <= 0d) return;
            graphics.PushOpacity(Math.Min(1d, alpha));
            graphics.DrawImage(image, bounds);
            graphics.Pop();
        }

        private void DrawProductCaption(DrawingContext graphics, double left, double top,
            double width, double logoHeight, double pixelsPerDip)
        {
            double progress = Ease(Progress(ElapsedMilliseconds, 2250, 450));
            if (progress <= 0d) return;
            double fontSize = Math.Max(14d, logoHeight * 0.10d);
            var font = new Typeface(new FontFamily(UiTheme.BrandFontFamily),
                FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var brush = new SolidColorBrush(Color.FromArgb(
                (byte)(150 * progress), 70, 78, 86));
            brush.Freeze();
            var text = MakeText(font, fontSize,
                "UNCAD Pro · v" + ProductMetadata.VersionText, brush, pixelsPerDip);
            double textX = left + (width - text.Width) / 2d;
            graphics.DrawText(text, new System.Windows.Point(textX, top));
        }

        private static FormattedText MakeText(Typeface font, double fontSize,
            string text, Brush brush, double pixelsPerDip)
            => new FormattedText(text,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, font, fontSize, brush, pixelsPerDip);

        private static double MeasureWord(Typeface font, double fontSize, string text,
            double pixelsPerDip)
            => MakeText(font, fontSize, text, Brushes.Black, pixelsPerDip).Width;

        private static List<Particle> BuildParticles(BitmapFrame logo)
        {
            int logoWidth = logo.PixelWidth;
            int logoHeight = logo.PixelHeight;
            var pixels = new byte[logoWidth * logoHeight * 4];
            var converted = new FormatConvertedBitmap(logo, PixelFormats.Bgra32, null, 0);
            converted.CopyPixels(pixels, logoWidth * 4, 0);
            bool IsOpaque(int x, int y) => pixels[(y * logoWidth + x) * 4 + 3] >= 128;

            var targets = new List<System.Windows.Point>();
            const int sampleStep = 8;
            for (int y = sampleStep / 2; y < logoHeight; y += sampleStep)
            {
                for (int x = sampleStep / 2; x < logoWidth; x += sampleStep)
                {
                    if (!IsOpaque(x, y)) continue;
                    targets.Add(new System.Windows.Point(
                        x / (double)logoWidth, y / (double)logoHeight));
                }
            }

            var random = new Random(2402);
            for (int index = 0; index < Math.Min(MaximumParticles, targets.Count); index++)
            {
                int swap = random.Next(index, targets.Count);
                System.Windows.Point value = targets[index];
                targets[index] = targets[swap];
                targets[swap] = value;
            }
            if (targets.Count > MaximumParticles)
                targets.RemoveRange(MaximumParticles, targets.Count - MaximumParticles);

            var particles = new List<Particle>(targets.Count);
            foreach (System.Windows.Point target in targets)
            {
                double angle = random.NextDouble() * Math.PI * 2.0;
                particles.Add(new Particle(
                    target,
                    new System.Windows.Point(Math.Cos(angle), Math.Sin(angle)),
                    70 + (int)(target.X * 360) + random.Next(0, 90),
                    780 + random.Next(0, 260),
                    2.0f + (float)random.NextDouble() * 1.2f,
                    (float)(random.NextDouble() - 0.5) * 0.32f));
            }
            return particles;
        }

        private static BitmapFrame LoadLogo()
        {
            Assembly assembly = typeof(StartupSplashCanvas).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(LogoResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("启动动画品牌资源不存在: "
                        + LogoResourceName);
                // OnLoad：解码进内存，stream 关闭后仍可反复绘制。
                return BitmapDecoder.Create(stream, BitmapCreateOptions.None,
                    BitmapCacheOption.OnLoad).Frames[0];
            }
        }

        private static double Progress(int elapsed, int start, int duration)
            => Math.Max(0d, Math.Min(1d, (elapsed - start) / (double)duration));

        private static double Ease(double value)
        {
            double remaining = 1d - value;
            return 1d - remaining * remaining * remaining * remaining;
        }

        private static double Lerp(double start, double end, double progress)
            => start + (end - start) * progress;

        private readonly struct Particle
        {
            public Particle(System.Windows.Point target, System.Windows.Point startDirection,
                int delayMilliseconds, int durationMilliseconds, double size, double bend)
            {
                Target = target;
                StartDirection = startDirection;
                DelayMilliseconds = delayMilliseconds;
                DurationMilliseconds = durationMilliseconds;
                Size = size;
                Bend = bend;
            }

            public System.Windows.Point Target { get; }
            public System.Windows.Point StartDirection { get; }
            public int DelayMilliseconds { get; }
            public int DurationMilliseconds { get; }
            public double Size { get; }
            public double Bend { get; }
        }
    }
}
