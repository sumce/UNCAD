// 3.3.5（D-054 Amendment）：U1U 确认窗左侧 3D 路由预览页——渲染内核从
// HelixToolkit.Wpf 换回 WebView2 + three.js（用户看过 3.3.2/3.3.4 的 Helix
// 预览后现场反馈「这生成的 3D 有严重问题」）。本页是**哑渲染器**：全部
// 场景语义（柱网规划/通道双线偏移/标签锚点）由宿主侧 Core 信封
// （QuickLineOrbitPreviewEnvelope）算好传入，这里只做三件事——
// 画线（透视相机+轨道交互）、叠标签（HTML 层，中文交给 Chromium）、
// 回消息（ready / renderError）。three.js 与 OrbitControls 复用
// QuickLine3D 页已 vendor 的库（宿主把虚拟主机根映射到 Web 父目录）。
import * as THREE from '../QuickLine3D/vendor/three/three.module.min.js';
import { OrbitControls } from '../QuickLine3D/vendor/three/OrbitControls.js';

const el = {
  viewport: document.getElementById('viewport'),
  canvasHost: document.getElementById('canvasHost'),
  loading: document.getElementById('loading'),
  labelLayer: document.getElementById('labelLayer'),
  summary: document.getElementById('summary'),
  legend: document.getElementById('legend'),
  grid: document.getElementById('gridButton'),
  reset: document.getElementById('resetButton')
};

const color = {
  axisX: 0xe06c00, axisY: 0x2e9e5b, axisZ: 0x3b6ed0,
  grid: 0x55606e, tray: 0xe8443c, trayFill: 0xe8443c,
  conduit: 0x2ea8c0, pin: 0x99a7b8
};
const state = {
  renderer: null, scene: null, camera: null, controls: null,
  gridGroup: null, labels: [], diagonal: 1, showGrid: true,
  defaultCamera: null, defaultTarget: null
};

boot();

function boot() {
  try {
    initRenderer();
    bindUi();
    animate();
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.addEventListener('message', event => receive(event.data));
      post({ type: 'ready', schemaVersion: 1 });
    } else {
      // 宿主外（浏览器直开/自检）：允许页面内 window.postMessage 注入信封。
      window.addEventListener('message', event => receive(event.data));
      el.loading.textContent = '等待宿主数据...';
    }
  } catch (error) { fail(error); }
}

function initRenderer() {
  state.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
  state.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  state.renderer.setClearColor(0x0d131a, 1);
  state.renderer.domElement.setAttribute('aria-label', 'U1U 三维路由预览画布');
  el.canvasHost.appendChild(state.renderer.domElement);
  state.scene = new THREE.Scene();
  // CAD 是 Z 朝上：相机 up 跟着立起来（OrbitControls 对任意 up 均可）。
  state.camera = new THREE.PerspectiveCamera(60, 1, 10, 1e9);
  state.camera.up.set(0, 0, 1);
  state.controls = new OrbitControls(state.camera, state.renderer.domElement);
  state.controls.enableDamping = true;
  state.controls.dampingFactor = 0.08;
  state.controls.screenSpacePanning = true;
  state.renderer.domElement.addEventListener('dblclick', resetCamera);
  new ResizeObserver(resize).observe(el.viewport);
  resize();
}

function bindUi() {
  el.grid.addEventListener('click', () => setGridVisible(!state.showGrid));
  el.reset.addEventListener('click', resetCamera);
  window.addEventListener('resize', resize);
}

function receive(raw) {
  try {
    const message = typeof raw === 'string' ? JSON.parse(raw) : raw;
    if (!message || message.type !== 'initialize') return;
    // 宿主载荷形如 {type, schemaVersion, envelope}；宿主外自检可能直接给
    // 信封本体——两形都认。
    renderEnvelope(message.envelope || message);
    el.loading.style.display = 'none';
  } catch (error) { fail(error); }
}

// ---- 场景构建（只消费信封里的现成几何） ----------------------------------

function renderEnvelope(envelope) {
  clearScene();
  state.diagonal = Math.max(Number(envelope.Diagonal) || 1, 1);
  const radius = state.diagonal * 0.0012;

  state.gridGroup = new THREE.Group();
  state.scene.add(state.gridGroup);

  const bounds = measureBounds(envelope);
  addGridLines(envelope, bounds);
  addChannels(envelope, radius);
  addRouteLines(envelope, radius);
  collectLabels(envelope, bounds);
  applyDefaultCamera(bounds);
  setGridVisible(true);
  updateLegend(envelope);
}

function clearScene() {
  while (state.scene.children.length > 0) state.scene.remove(state.scene.children[0]);
  for (const label of state.labels) label.element.remove();
  state.labels = [];
  state.gridGroup = null;
}

function measureBounds(envelope) {
  let minX = Infinity, minY = Infinity, minZ = Infinity;
  let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
  const feed = point => {
    minX = Math.min(minX, point.X); maxX = Math.max(maxX, point.X);
    minY = Math.min(minY, point.Y); maxY = Math.max(maxY, point.Y);
    minZ = Math.min(minZ, point.Z); maxZ = Math.max(maxZ, point.Z);
  };
  for (const segment of envelope.Segments || []) { feed(segment.Start); feed(segment.End); }
  if (!isFinite(minX)) { minX = minY = minZ = 0; maxX = maxY = maxZ = 1000; }
  return {
    centre: new THREE.Vector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2),
    diagonal: Math.max(Math.hypot(maxX - minX, maxY - minY, maxZ - minZ), 1)
  };
}

function addGridLines(envelope) {
  const lines = envelope.ColumnGridLines || [];
  if (lines.length === 0) return;
  const positions = [];
  for (const point of lines) positions.push(point.X, point.Y, point.Z);
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  const material = new THREE.LineBasicMaterial({ color: color.grid, transparent: true, opacity: 0.75 });
  state.gridGroup.add(new THREE.LineSegments(geometry, material));
}

function addChannels(envelope, radius) {
  const railRadius = radius * 0.7;
  for (const segment of envelope.Segments || []) {
    const channel = segment.Channel;
    if (!channel) continue;
    if (channel.Kind === 'tray') {
      // 半透明填充带（双面：从下往上看也有）。
      const fill = new THREE.Mesh(
        fillGeometry(channel),
        new THREE.MeshBasicMaterial({
          color: color.trayFill, transparent: true, opacity: 0.3,
          side: THREE.DoubleSide, depthWrite: false
        }));
      state.scene.add(fill);
      addTube(channel.A1, channel.A2, color.tray, railRadius);
      addTube(channel.B1, channel.B2, color.tray, railRadius);
    } else if (channel.Kind === 'conduit') {
      addTube(channel.A1, channel.A2, color.conduit, railRadius * 0.8);
      addTube(channel.B1, channel.B2, color.conduit, railRadius * 0.8);
    }
  }
}

function fillGeometry(channel) {
  const a1 = vec(channel.A1), a2 = vec(channel.A2);
  const b1 = vec(channel.B1), b2 = vec(channel.B2);
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute([
    a1.x, a1.y, a1.z, a2.x, a2.y, a2.z, b2.x, b2.y, b2.z,
    a1.x, a1.y, a1.z, b2.x, b2.y, b2.z, b1.x, b1.y, b1.z
  ], 3));
  geometry.computeVertexNormals();
  return geometry;
}

function addRouteLines(envelope, radius) {
  for (const segment of envelope.Segments || []) {
    const tint = segment.Axis === 'X' ? color.axisX
      : segment.Axis === 'Y' ? color.axisY : color.axisZ;
    addTube(segment.Start, segment.End, tint, radius);
  }
}

function addTube(from, to, tint, radius) {
  const start = vec(from), end = vec(to);
  const direction = new THREE.Vector3().subVectors(end, start);
  const length = direction.length();
  if (length < 1e-9) return;
  const tube = new THREE.Mesh(
    new THREE.CylinderGeometry(radius, radius, length, 6, 1, false),
    new THREE.MeshBasicMaterial({ color: tint }));
  tube.position.copy(start).addScaledVector(direction, 0.5);
  tube.quaternion.setFromUnitVectors(
    new THREE.Vector3(0, 1, 0), direction.clone().normalize());
  state.scene.add(tube);
}

function collectLabels(envelope) {
  for (const label of envelope.SegmentLabels || []) addLabel(label, 'label-mm');
  for (const tick of envelope.NumericTicks || []) addLabel(tick, 'label-tick');
  for (const letter of envelope.LetterLabels || []) addLabel(letter, 'label-tick');
  for (const pin of envelope.EndpointPins || []) {
    // 端点钉：竖直引线（细管）+ 顶端标签（键与信封标签同形 PascalCase）。
    addTube(
      { x: pin.X, y: pin.Y, z: pin.Z },
      { x: pin.X, y: pin.Y, z: pin.TipZ }, color.pin, state.diagonal * 0.0004);
    addLabel({ Text: pin.Text, X: pin.X, Y: pin.Y, Z: pin.LabelZ }, 'label-pin');
  }
}

function addLabel(label, cssClass) {
  if (!label || !label.Text) return;
  const element = document.createElement('div');
  element.className = 'scene-label ' + cssClass;
  element.textContent = label.Text;
  el.labelLayer.appendChild(element);
  // 信封标签键是 PascalCase（C# 序列化），端点钉内部构造是小写——两路都认。
  const x = label.X !== undefined ? label.X : label.x;
  const y = label.Y !== undefined ? label.Y : label.y;
  const z = label.Z !== undefined ? label.Z : label.z;
  state.labels.push({
    element,
    position: new THREE.Vector3(x, y, z),
    group: cssClass === 'label-tick' ? 'grid' : 'route'
  });
}

// ---- 相机 / 交互 -----------------------------------------------------------

function applyDefaultCamera(bounds) {
  // 与 3.3.2 Helix 默认视角等价：方位 -135°/俯仰 30°（方向向量的 x/y/z 同比）。
  const direction = new THREE.Vector3(-1, -1, 0.57735026919).normalize();
  const distance = bounds.diagonal * 1.6;
  state.defaultCamera = {
    position: bounds.centre.clone().addScaledVector(direction, distance),
    target: bounds.centre.clone()
  };
  state.camera.near = Math.max(bounds.diagonal * 0.001, 0.1);
  state.camera.far = bounds.diagonal * 100;
  state.camera.updateProjectionMatrix();
  resetCamera();
}

function resetCamera() {
  if (!state.defaultCamera) return;
  state.camera.position.copy(state.defaultCamera.position);
  state.controls.target.copy(state.defaultCamera.target);
  state.controls.update();
}

function setGridVisible(visible) {
  state.showGrid = visible;
  if (state.gridGroup) state.gridGroup.visible = visible;
  for (const label of state.labels) {
    if (label.group === 'grid') {
      label.element.style.display = visible ? '' : 'none';
    }
  }
  el.grid.textContent = visible ? '柱网 开' : '柱网 关';
}

function updateLegend(envelope) {
  let legend = '橙X 绿Y 蓝Z　红=桥架(宽) 青=线管';
  if (envelope.AnyTrayWidthAssumedDefault) legend += '　桥架宽未标注按 200 显示';
  el.legend.textContent = legend;
  el.summary.textContent = (envelope.AxisSummary ? envelope.AxisSummary + '　' : '')
    + '左键拖动=旋转 · 滚轮=缩放 · 右键拖动=平移 · 双击=复位';
}

// ---- 帧循环 ----------------------------------------------------------------

function resize() {
  const width = el.viewport.clientWidth || 1;
  const height = el.viewport.clientHeight || 1;
  state.renderer.setSize(width, height, false);
  state.camera.aspect = width / height;
  state.camera.updateProjectionMatrix();
}

function updateLabels() {
  const width = el.viewport.clientWidth || 1;
  const height = el.viewport.clientHeight || 1;
  const visible = new THREE.Vector3();
  for (const label of state.labels) {
    visible.copy(label.position).project(state.camera);
    const onScreen = visible.z > -1 && visible.z < 1
      && visible.x > -1.05 && visible.x < 1.05
      && visible.y > -1.05 && visible.y < 1.05;
    label.element.style.display = onScreen ? '' : 'none';
    if (onScreen) {
      label.element.style.left = ((visible.x * 0.5 + 0.5) * width) + 'px';
      label.element.style.top = ((-visible.y * 0.5 + 0.5) * height) + 'px';
    }
  }
}

function animate() {
  requestAnimationFrame(animate);
  state.controls.update();
  state.renderer.render(state.scene, state.camera);
  updateLabels();
}

function vec(point) { return new THREE.Vector3(point.X, point.Y, point.Z); }

function post(message) {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage(message);
  }
}

function fail(error) {
  el.loading.style.display = 'grid';
  el.loading.textContent = '三维渲染失败：' + (error && error.message ? error.message : error);
  post({ type: 'renderError', schemaVersion: 1, message: String(error) });
}
