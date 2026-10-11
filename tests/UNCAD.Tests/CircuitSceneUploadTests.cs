using System.Collections.Generic;
using System.IO;
using UNCAD.Core.Fill;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1F/U1U 的**等轴测（3D）场景**上传契约。
    ///
    /// 用户要求：「U1U/U1F 自动上传 3D 数据到云端」，网页端「ISO 图」要能在线看等轴测视图。
    /// 关键点：
    /// 1) 场景与清单**同一次回路提交**一起上传（同一个 payload），不是另开一条链路；
    /// 2) 场景必须与客户端 U1L3D 预览是**同一份 envelope**（同一套构建逻辑），
    ///    否则网页看到的 3D 和客户端看到的不是一回事；
    /// 3) 图框内没有带毫米标注的 U1L 线段时**不发 scene 字段**（不是错误，也不发空串）。
    /// </summary>
    public sealed class CircuitSceneUploadTests
    {
        private const string Machine = "MDIDA03";
        private const string Circuit = "MDIDA03-1";

        [Fact]
        public void SubmitCircuit_uploads_scene_together_with_the_inventory()
        {
            ClientSubmissionRequest captured = null;
            var uploader = new ClientSubmissionUploader(() => Credentials(),
                request =>
                {
                    captured = request;
                    return new ClientSubmissionHttpResult(200, "{\"ok\":true,\"circuitId\":7}");
                });

            string scene = "{\"scene\":{\"projectionMode\":\"Isometric\",\"nodes\":[],\"segments\":[]}}";
            ClientSubmissionOutcome outcome = uploader.SubmitCircuit(Machine, Circuit, "U1U",
                "1A2B", new List<TableFillRow>(), scene);

            Assert.Equal(ClientSubmissionStatus.Accepted, outcome.Status);
            Assert.Equal("/api/client/submissions/circuit", captured.Path);
            Assert.Contains("\"scene\"", captured.JsonBody);
            Assert.Contains("projectionMode", captured.JsonBody);
            // 清单与场景在同一个请求里（同一次 U1U 上传）
            Assert.Contains("\"inventory\"", captured.JsonBody);
            Assert.Contains("\"circuitName\":\"" + Circuit + "\"", captured.JsonBody);
        }

        [Fact]
        public void SubmitCircuit_sends_a_null_scene_when_the_frame_has_no_u1l_routes()
        {
            ClientSubmissionRequest captured = null;
            var uploader = new ClientSubmissionUploader(() => Credentials(),
                request =>
                {
                    captured = request;
                    return new ClientSubmissionHttpResult(200, "{\"ok\":true,\"circuitId\":7}");
                });

            // 空串/null 都必须表达成"没有 3D 数据"（scene 为 JSON null）：
            // 服务端据此区分"没有等轴测数据"与"传了一个空场景"。
            uploader.SubmitCircuit(Machine, Circuit, "U1U", "1A2B",
                new List<TableFillRow>(), null);
            Assert.Null(SceneValue(captured.JsonBody));

            uploader.SubmitCircuit(Machine, Circuit, "U1U", "1A2B",
                new List<TableFillRow>(), "   ");
            Assert.Null(SceneValue(captured.JsonBody));
        }

        /// <summary>取 payload 里的 scene 值（net48 下用 JavaScriptSerializer，客户端与之一致）。</summary>
        private static object SceneValue(string json)
        {
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var map = (Dictionary<string, object>)serializer.DeserializeObject(json);
            Assert.True(map.ContainsKey("scene"),
                "scene 字段必须存在（没有 3D 数据时为 null）");
            return map["scene"];
        }

        [Fact]
        public void Feature_builds_the_scene_with_the_same_builder_as_the_u1l3d_preview()
        {
            string fill = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillFeature.cs"));
            string batch = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs"));
            string preview = File.ReadAllText(Repo("src", "UNCAD", "Features", "Unl",
                "QuickLine3dPreviewFeature.cs"));

            // 场景由 U1L3D 那个类构建（同一套 BuildScene/BuildEnvelope），FillFeature 不自己另写一套
            Assert.Contains("QuickLine3dPreviewFeature.TryBuildSceneJson(", fill);
            Assert.DoesNotContain("BuildEnvelope(", fill);
            Assert.Contains("internal static bool TryBuildSceneJson(CadContext ctx",
                preview);
            Assert.Contains("BuildRegionEnvelope(ctx, region", preview);
            // U1L3D 本地预览与上传共用同一个构建入口
            Assert.Contains("BuildRegionEnvelope(ctx, region,", preview);

            // 场景在 CAD 线程上构建后随清单一起上传；批量 U1U 同样带上场景
            Assert.Contains("SubmitCircuit(machineId, circuitName,", fill);
            Assert.Contains("captured, sceneJson)", fill);
            Assert.Contains("FillFeature.UploadCircuitSubmission(ctx, plan.Machine, plan.Rows, true,",
                batch);
        }

        [Fact]
        public void Scene_size_limit_is_documented_and_enforced_before_upload()
        {
            string fill = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillFeature.cs"));
            Assert.Contains("MaximumSceneChars", fill);
            Assert.Contains("if (sceneJson.Length > MaximumSceneChars)", fill);
            // 超限/构建失败/无 U1L 都只记日志，清单照常上传
            Assert.Contains("构建等轴测场景失败（已忽略，清单照常上传）", fill);
            Assert.Contains("未上传等轴测场景（", fill);
        }

        private static ClientSubmissionCredentials Credentials()
            => new ClientSubmissionCredentials
            {
                Account = "devuser",
                DeviceId = "8f14e45f-ceea-467a-9575-1b0e0f0d9c3a",
                Token = "qN3fZk9Wx2LpRt7YvBc4MdE6GhJ1Ks8UoA5TiPnQrZe"
            };

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            var all = new string[parts.Length + 1];
            all[0] = root;
            System.Array.Copy(parts, 0, all, 1, parts.Length);
            return Path.Combine(all);
        }
    }
}
