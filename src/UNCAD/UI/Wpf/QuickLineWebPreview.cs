using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Media.Media3D;
using UNCAD.Infra;

namespace UNCAD.UI
{
    /// <summary>
    /// Exports the native scene's exact meshes to the isolated, local WebView2
    /// viewer. No browser controller or browser profile is opened inside CAD.
    /// </summary>
    internal static class QuickLineWebPreview
    {
        internal static Task OpenAsync(QuickLine3dScene scene, string title,
            IEnumerable<string> hiddenGroups = null,
            IEnumerable<string> hiddenLabelKinds = null)
            => Task.Run(() => Open(scene, title, hiddenGroups, hiddenLabelKinds));

        internal static void Open(QuickLine3dScene scene, string title,
            IEnumerable<string> hiddenGroups = null,
            IEnumerable<string> hiddenLabelKinds = null)
        {
            string root = Path.GetDirectoryName(typeof(QuickLineWebPreview).Assembly.Location);
            string executable = Path.Combine(root, "UNCAD.Preview.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("WebV 预览组件缺失，请重新安装完整发布包。", executable);
            string directory = Path.Combine(Path.GetTempPath(), "UNCAD", "Preview",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "scene.json");
            try
            {
                CopyTree(Path.Combine(root, "Web", "Preview"), directory);
                File.WriteAllText(path, Serialize(scene, title, hiddenGroups,
                    hiddenLabelKinds), new UTF8Encoding(false));
                using (Process process = Process.Start(new ProcessStartInfo(executable,
                    "\"" + directory + "\"")
                {
                    UseShellExecute = false,
                    WorkingDirectory = root
                }))
                {
                    if (process == null) throw new IOException("无法启动 WebV 预览。");
                }
            }
            catch
            {
                try { Directory.Delete(directory, true); } catch (IOException) { }
                throw;
            }
            Log.Info("[3D] 已启动独立 WebV 预览。");
        }

        private static void CopyTree(string source, string destination)
        {
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException("WebV 页面资源缺失，请重新安装完整发布包。");
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        internal static string Serialize(QuickLine3dScene scene, string title,
            IEnumerable<string> hiddenGroups = null,
            IEnumerable<string> hiddenLabelKinds = null)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            var builders = new Dictionary<
                KeyValuePair<QuickLine3dSceneGroupKind, QuickLine3dViewportControl.MaterialKey>,
                QuickLine3dViewportControl.MeshBuilder>();
            foreach (QuickLine3dPrimitive primitive in scene.Primitives)
            {
                var key = new KeyValuePair<QuickLine3dSceneGroupKind,
                    QuickLine3dViewportControl.MaterialKey>(primitive.Group,
                    new QuickLine3dViewportControl.MaterialKey(primitive.Tint, primitive.Opacity));
                if (!builders.TryGetValue(key, out var builder))
                    builders.Add(key, builder = new QuickLine3dViewportControl.MeshBuilder());
                builder.Append(primitive);
            }
            var meshes = new List<object>();
            foreach (var pair in builders)
            {
                var built = pair.Value.ToBuilt(pair.Key.Value, pair.Key.Key);
                if (built == null) continue;
                MeshGeometry3D mesh = built.Mesh;
                meshes.Add(new
                {
                    group = built.Group.ToString(),
                    color = (built.Key.Tint.R << 16) | (built.Key.Tint.G << 8) | built.Key.Tint.B,
                    opacity = built.Key.Opacity * built.Key.Tint.A / 255.0,
                    positions = mesh.Positions.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray(),
                    normals = mesh.Normals.SelectMany(n => new[] { n.X, n.Y, n.Z }).ToArray(),
                    indices = mesh.TriangleIndices.ToArray()
                });
            }
            return new JavaScriptSerializer { MaxJsonLength = 128 * 1024 * 1024 }.Serialize(new
            {
                version = 1,
                title = title ?? "3D",
                meshes,
                labels = scene.Labels.Select(label => new
                {
                    text = label.Text,
                    position = new[] { label.X, label.Y, label.Z },
                    group = label.Group.ToString(),
                    kind = label.Kind
                }).ToArray(),
                hiddenGroups = (hiddenGroups ?? new[] { "Detail" }).ToArray(),
                hiddenLabelKinds = (hiddenLabelKinds ?? Array.Empty<string>()).ToArray(),
                camera = new
                {
                    orthographic = scene.Orthographic,
                    center = new[] { scene.Centerx, scene.Centery, scene.Centerz },
                    orbit = new[] { scene.Orbitx, scene.Orbitsy, scene.Orbitz },
                    distance = scene.CameraDistance,
                    height = scene.OrthoViewHeight,
                    near = scene.Near,
                    far = scene.Far,
                    diagonal = scene.Diagonal
                }
            });
        }
    }
}
