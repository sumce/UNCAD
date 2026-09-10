using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>WebView2 startup animation with a native fallback. Click or press any key to skip.</summary>
    internal sealed class StartupSplashForm : Form
    {
        internal const int AutoCloseMilliseconds = 5000;
        private const int WebInitializationTimeoutMilliseconds = 2500;
        private const string HtmlResourceName = "UNCAD.Assets.StartupSplash.html";
        private readonly Timer _timer = new Timer { Interval = 16 };
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly StartupSplashCanvas _canvas = new StartupSplashCanvas();
        private WebView2 _webView;
        private bool _webStartupPending;
        private bool _webStartupAbandoned;
        private bool _webAnimationRunning;

        internal static bool SupportsHost(string processName, bool userInteractive)
            => userInteractive
                && !string.Equals(processName, "accoreconsole",
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(processName, "accoreconsole.exe",
                    StringComparison.OrdinalIgnoreCase);

        public StartupSplashForm(Rectangle screenBounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Bounds = screenBounds.IsEmpty ? Screen.PrimaryScreen.Bounds : screenBounds;
            BackColor = Color.White;
            KeyPreview = true;

            _canvas.Dock = DockStyle.Fill;
            _canvas.MouseDown += (sender, args) => Close();
            Controls.Add(_canvas);
            TryCreateWebView();

            KeyDown += (sender, args) => Close();
            MouseDown += (sender, args) => Close();
            Shown += OnShown;
            _timer.Tick += OnTick;
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
            _canvas.Invalidate();
        }

        private void TryCreateWebView()
        {
            try
            {
                _webView = new WebView2
                {
                    Dock = DockStyle.Fill,
                    Visible = false,
                    DefaultBackgroundColor = Color.White
                };
                Controls.Add(_webView);
                _webView.BringToFront();
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
                if (IsDisposed || Disposing || _webStartupAbandoned) return;
                await _webView.EnsureCoreWebView2Async(environment);
                if (IsDisposed || Disposing || _webStartupAbandoned) return;

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
                if (IsDisposed || Disposing || _webStartupAbandoned) return;
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

        private void OnWebProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs args)
            => UseNativeFallback("WebView2 启动动画进程异常", args.ProcessFailedKind.ToString());

        private void StartWebAnimation()
        {
            if (IsDisposed || Disposing || _webStartupAbandoned || _webView == null) return;
            _webStartupPending = false;
            _webAnimationRunning = true;
            _webView.Visible = true;
            _webView.BringToFront();
            _webView.Focus();
        }

        private void UseNativeFallback(string reason, string detail)
        {
            if (IsDisposed || Disposing || _webStartupAbandoned) return;
            _webStartupPending = false;
            _webStartupAbandoned = true;
            _webAnimationRunning = false;
            if (_webView != null) _webView.Visible = false;
            _canvas.Invalidate();
            Log.Warn(reason + "，使用原生动画: " + detail);
        }

        internal static string LoadSplashHtml()
        {
            Assembly assembly = typeof(StartupSplashForm).Assembly;
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

        protected override void OnFormClosed(FormClosedEventArgs args)
        {
            _webStartupAbandoned = true;
            _timer.Stop();
            _timer.Dispose();
            if (_webView?.CoreWebView2 != null)
            {
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                _webView.CoreWebView2.ProcessFailed -= OnWebProcessFailed;
            }
            if (_webView != null)
            {
                _webView.NavigationCompleted -= OnWebNavigationCompleted;
                _webView.Dispose();
            }
            base.OnFormClosed(args);
        }
    }

    internal sealed class StartupSplashCanvas : Control
    {
        private const string LogoResourceName = "UNCAD.Assets.unsiao-mark.png";
        private const int MaximumParticles = 2400;
        private readonly Bitmap _logo;
        private readonly List<Particle> _particles;
        private readonly RectangleF[] _particleRectangles;

        public StartupSplashCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Color.White;
            _logo = LoadLogo();
            _particles = BuildParticles(_logo);
            _particleRectangles = new RectangleF[_particles.Count];
        }

        public int ElapsedMilliseconds { get; set; }
        internal int ParticleCount => _particles.Count;

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            Graphics graphics = args.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float logoHeight = Math.Max(96f, Math.Min(ClientSize.Width, ClientSize.Height) * 0.18f);
            float logoWidth = logoHeight * _logo.Width / _logo.Height;
            var centeredLogo = new RectangleF(
                (ClientSize.Width - logoWidth) / 2f,
                (ClientSize.Height - logoHeight) / 2f,
                logoWidth,
                logoHeight);

            using (var wordFont = new Font(UiTheme.BrandFontFamily, logoHeight * 0.27f,
                FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF wordSize = graphics.MeasureString("UNSIAO", wordFont);
                float gap = logoHeight * 0.14f;
                float groupWidth = logoWidth + gap + wordSize.Width;
                var finalLogo = new RectangleF(
                    (ClientSize.Width - groupWidth) / 2f,
                    centeredLogo.Top,
                    logoWidth,
                    logoHeight);
                float shift = Ease(Progress(ElapsedMilliseconds, 1700, 400));
                var visibleLogo = new RectangleF(
                    Lerp(centeredLogo.Left, finalLogo.Left, shift),
                    centeredLogo.Top,
                    logoWidth,
                    logoHeight);

                DrawParticles(graphics, centeredLogo);
                DrawImage(graphics, _logo, visibleLogo,
                    Ease(Progress(ElapsedMilliseconds, 1450, 350)));
                DrawWordmark(graphics, wordFont, finalLogo.Right + gap,
                    finalLogo.Top + (logoHeight - wordSize.Height) / 2f);
                DrawProductCaption(graphics, finalLogo.Left,
                    finalLogo.Bottom + logoHeight * 0.18f, groupWidth, logoHeight);
            }
        }

        private void DrawParticles(Graphics graphics, RectangleF logoBounds)
        {
            float fade = 1f - Ease(Progress(ElapsedMilliseconds, 1450, 450));
            if (fade <= 0f) return;

            int alpha = Math.Max(0, Math.Min(220, (int)(220f * fade)));
            using (var brush = new SolidBrush(Color.FromArgb(alpha, 10, 10, 10)))
            {
                float centerX = ClientSize.Width / 2f;
                float centerY = ClientSize.Height / 2f;
                float radius = (float)Math.Sqrt(ClientSize.Width * ClientSize.Width
                    + ClientSize.Height * ClientSize.Height) * 0.72f;
                float sizeScale = (float)Math.Sqrt(Math.Max(0.5f,
                    logoBounds.Height / 180f));
                for (int index = 0; index < _particles.Count; index++)
                {
                    Particle particle = _particles[index];
                    float progress = Progress(ElapsedMilliseconds,
                        particle.DelayMilliseconds, particle.DurationMilliseconds);
                    if (progress <= 0f)
                    {
                        _particleRectangles[index] = RectangleF.Empty;
                        continue;
                    }
                    float eased = Ease(progress);
                    float targetX = logoBounds.Left + particle.Target.X * logoBounds.Width;
                    float targetY = logoBounds.Top + particle.Target.Y * logoBounds.Height;
                    float startX = centerX + particle.StartDirection.X * radius;
                    float startY = centerY + particle.StartDirection.Y * radius;
                    float bendX = -(targetY - startY) * particle.Bend;
                    float bendY = (targetX - startX) * particle.Bend;
                    float remaining = 1f - eased;
                    float middle = 2f * remaining * eased;
                    float x = remaining * remaining * startX
                        + middle * ((startX + targetX) / 2f + bendX)
                        + eased * eased * targetX;
                    float y = remaining * remaining * startY
                        + middle * ((startY + targetY) / 2f + bendY)
                        + eased * eased * targetY;
                    float size = particle.Size * sizeScale * Math.Min(1f, progress * 4f);
                    _particleRectangles[index] = new RectangleF(
                        x - size / 2f, y - size / 2f, size, size);
                }
                graphics.FillRectangles(brush, _particleRectangles);
            }
        }

        private void DrawWordmark(Graphics graphics, Font font, float left, float top)
        {
            const string word = "UNSIAO";
            float x = left;
            for (int index = 0; index < word.Length; index++)
            {
                string letter = word[index].ToString();
                float progress = Ease(Progress(ElapsedMilliseconds, 1850 + index * 70, 480));
                SizeF size = graphics.MeasureString(letter, font);
                if (progress > 0f)
                {
                    using (var brush = new SolidBrush(Color.FromArgb((int)(255 * progress),
                        10, 10, 10)))
                    {
                        graphics.DrawString(letter, font, brush,
                            x + (1f - progress) * font.Size * 0.9f,
                            top + (1f - progress) * font.Size * 0.15f);
                    }
                }
                x += size.Width - font.Size * 0.08f;
            }
        }

        private static void DrawImage(Graphics graphics, Image image, RectangleF bounds, float alpha)
        {
            if (alpha <= 0f) return;
            using (var attributes = new ImageAttributes())
            {
                var matrix = new ColorMatrix { Matrix33 = Math.Min(1f, alpha) };
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default,
                    ColorAdjustType.Bitmap);
                graphics.DrawImage(image, Rectangle.Round(bounds), 0, 0,
                    image.Width, image.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        private void DrawProductCaption(Graphics graphics, float left, float top,
            float width, float logoHeight)
        {
            float progress = Ease(Progress(ElapsedMilliseconds, 2250, 450));
            if (progress <= 0f) return;
            using (var font = new Font(UiTheme.BrandFontFamily, Math.Max(14f, logoHeight * 0.10f),
                FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color.FromArgb((int)(150 * progress),
                70, 78, 86)))
            using (var format = new StringFormat { Alignment = StringAlignment.Center })
            {
                graphics.DrawString("UNCAD Pro · v" + ProductMetadata.VersionText,
                    font, brush, new RectangleF(left, top, width, font.Height * 1.5f), format);
            }
        }

        private static List<Particle> BuildParticles(Bitmap logo)
        {
            var targets = new List<PointF>();
            const int sampleStep = 8;
            for (int y = sampleStep / 2; y < logo.Height; y += sampleStep)
            {
                for (int x = sampleStep / 2; x < logo.Width; x += sampleStep)
                {
                    if (logo.GetPixel(x, y).A < 128) continue;
                    targets.Add(new PointF(x / (float)logo.Width, y / (float)logo.Height));
                }
            }

            var random = new Random(2402);
            for (int index = 0; index < Math.Min(MaximumParticles, targets.Count); index++)
            {
                int swap = random.Next(index, targets.Count);
                PointF value = targets[index];
                targets[index] = targets[swap];
                targets[swap] = value;
            }
            if (targets.Count > MaximumParticles)
                targets.RemoveRange(MaximumParticles, targets.Count - MaximumParticles);

            var particles = new List<Particle>(targets.Count);
            foreach (PointF target in targets)
            {
                double angle = random.NextDouble() * Math.PI * 2.0;
                particles.Add(new Particle(
                    target,
                    new PointF((float)Math.Cos(angle), (float)Math.Sin(angle)),
                    70 + (int)(target.X * 360) + random.Next(0, 90),
                    780 + random.Next(0, 260),
                    2.0f + (float)random.NextDouble() * 1.2f,
                    (float)(random.NextDouble() - 0.5) * 0.32f));
            }
            return particles;
        }

        private static Bitmap LoadLogo()
        {
            Assembly assembly = typeof(StartupSplashCanvas).Assembly;
            using (var stream = assembly.GetManifestResourceStream(LogoResourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException("启动动画品牌资源不存在: "
                        + LogoResourceName);
                using (var source = new Bitmap(stream)) return new Bitmap(source);
            }
        }

        private static float Progress(int elapsed, int start, int duration)
            => Math.Max(0f, Math.Min(1f, (elapsed - start) / (float)duration));

        private static float Ease(float value)
        {
            float remaining = 1f - value;
            return 1f - remaining * remaining * remaining * remaining;
        }

        private static float Lerp(float start, float end, float progress)
            => start + (end - start) * progress;

        protected override void Dispose(bool disposing)
        {
            if (disposing) _logo.Dispose();
            base.Dispose(disposing);
        }

        private readonly struct Particle
        {
            public Particle(PointF target, PointF startDirection, int delayMilliseconds,
                int durationMilliseconds, float size, float bend)
            {
                Target = target;
                StartDirection = startDirection;
                DelayMilliseconds = delayMilliseconds;
                DurationMilliseconds = durationMilliseconds;
                Size = size;
                Bend = bend;
            }

            public PointF Target { get; }
            public PointF StartDirection { get; }
            public int DelayMilliseconds { get; }
            public int DurationMilliseconds { get; }
            public float Size { get; }
            public float Bend { get; }
        }
    }
}
