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
  ref: document.getElementById('refButton'),
  detail: document.getElementById('detailButton'),
  reset: document.getElementById('resetButton')
};

const color = {
  axisX: 0xe06c00, axisY: 0x2e9e5b, axisZ: 0x3b6ed0,
  grid: 0x55606e, tray: 0xf2574c, trayFill: 0xf2574c,
  conduit: 0x2ea8c0, pin: 0x99a7b8, reference: 0xe0a94a
};
// 3.3.7：参照物实体模型配色（Lambert 材质吃光照，才有立体感）。
// 两轮视觉实测调优：深底上深灰读作线框→楼板/高架板提亮；设备盒/母线
// 塔再提亮+自发光（第二轮它们被雾洗进背景）；板边线用中性银灰。
const refColor = {
  floor: 0x7d93a6, floorEdge: 0xaab6c2,
  steel: 0xa5b2c3, grating: 0x94a884, raised: 0x8fadc0,
  device: 0xd9dfe6, deviceEdge: 0x39424e, deviceGlow: 0x30363d,
  busGlass: 0xe8b45f, busEdge: 0x7a5426,
  column: 0x5d6977, columnGlow: 0x181d24,
  ground: 0x39424e,
  epoxy: 0x5f9e6e, epoxyEdge: 0x9fd3ab,
  waffle: 0x8497a8
};
const state = {
  renderer: null, scene: null, camera: null, controls: null,
  gridGroup: null, refGroup: null, detailGroup: null, labels: [], diagonal: 1,
  showGrid: true, showReferenceLevels: true, showDetail: false,
  refLevelCount: 0, baseFloorZ: 0,
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
  el.ref.addEventListener('click', () => setReferenceVisible(!state.showReferenceLevels));
  if (el.detail) {
    el.detail.addEventListener('click', () => setDetailVisible(!state.showDetail));
  }
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
  // 3.3.7 第二轮实测：管线半径 0.0012 档在 20m+ 场景被建筑模型+雾衬得
  // 快看不见——加粗到 0.0019（模型要醒目，路由更是主角）。
  const radius = state.diagonal * 0.0019;

  state.gridGroup = new THREE.Group();
  state.scene.add(state.gridGroup);

  // 3.3.7：参照物用 Lambert 实体材质——两盏定位光（路由管线/线框是
  // Basic 材质不受影响），楼板/钢梁/设备盒才有明暗立体感。
  const hemi = new THREE.HemisphereLight(0xe8eef5, 0x141a21, 0.95);
  state.scene.add(hemi);
  const sun = new THREE.DirectionalLight(0xffffff, 0.65);
  sun.position.set(-0.55, -0.75, 1);
  state.scene.add(sun);

  const bounds = measureBounds(envelope);
  addGridLines(envelope, bounds);
  addReferenceLevels(envelope, bounds, radius);
  addChannels(envelope, radius);
  addRouteLines(envelope, radius);
  collectLabels(envelope, bounds);
  const viewBounds = boundsWithReferenceTop(bounds, envelope);
  // 轻雾：叠层远近有层次，远处淡入背景色——第二轮实测收紧（near 拉远、
  // far 放宽），别把近地设备/管线洗进背景。
  state.scene.fog = new THREE.Fog(0x0d131a,
    viewBounds.diagonal * 2.4, viewBounds.diagonal * 9);
  applyDefaultCamera(viewBounds);
  setGridVisible(true);
  setReferenceVisible(true);
  // 三轮用户口径：工字钢梁阵/葡萄架格栅默认不显示（「结构」开关按需开）。
  setDetailVisible(false);
  updateLegend(envelope);
}

function clearScene() {
  while (state.scene.children.length > 0) state.scene.remove(state.scene.children[0]);
  for (const label of state.labels) label.element.remove();
  state.labels = [];
  state.gridGroup = null;
  state.refGroup = null;
  state.detailGroup = null;
  state.refLevelCount = 0;
  state.baseFloorZ = 0;
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
    diagonal: Math.max(Math.hypot(maxX - minX, maxY - minY, maxZ - minZ), 1),
    minZ, maxZ
  };
}

// 3.3.7：参照物把模型顶到 15m+——相机取景要把参照层高度算进去，否则
// 整栋楼被挤到画面上半区、下半截空屏（首轮实测）。
function boundsWithReferenceTop(bounds, envelope) {
  const base = Number(envelope.BaseFloorZ) || 0;
  let refTop = 0;
  let refBottom = 0;
  for (const level of envelope.ReferenceLevels || []) {
    if (!level) continue;
    const z = level.Z - base;
    if (z > refTop) refTop = z;
    if (z < refBottom) refBottom = z;
  }
  const minZ = Math.min(bounds.minZ, refBottom);
  const maxZ = Math.max(bounds.maxZ, refTop + bounds.diagonal * 0.03);
  if (maxZ <= bounds.maxZ && minZ >= bounds.minZ) return bounds;
  const diagonal = Math.max(bounds.diagonal,
    Math.hypot(bounds.diagonal, maxZ - minZ));
  return {
    centre: new THREE.Vector3(bounds.centre.x, bounds.centre.y,
      (minZ + maxZ) / 2),
    diagonal,
    minZ, maxZ
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

// 3.3.7：参照物——按类型画**简化建筑模型**（用户原话「你完全可以自己
// 做一个模型 包括每层的地面 等 你就光弄个线条」）。类型来自 U1SET
// UNC_PREVIEW_REF_LEVELS 第三段（信封 Kind）：floor=楼板、steel=工字钢
// 梁阵、grating=葡萄架格栅、raised=高架地板薄板、device=设备盒（沿底边
// 排开）、bus=母线塔（右侧两柱+分层横臂）；frame/未知=3.3.6 水平线框。
// 高度来自清单（准确）；平面位置无数据——板材横贯包络、设备/母线沿边
// 示意布置（预览是展示件）。
function addReferenceLevels(envelope, bounds, radius) {
  const raw = envelope.ReferenceLevels || [];
  // 三轮用户口径：机台所在楼层（「本层=2F地面」）——整栋楼按本层重定位，
  // 本层地面=场景 0（上游就在本层），设备/母线已由信封锚定解析成绝对
  // 标高，这里统一减基准；标签显示本层起算高度。
  const base = Number(envelope.BaseFloorZ) || 0;
  state.baseFloorZ = base;
  const levels = raw
    .filter(level => level && level.Label && level.Z > 0)
    .map(level => ({ Label: level.Label, Z: level.Z - base, Kind: level.Kind }));
  state.refLevelCount = levels.length;
  if (levels.length === 0) return;
  state.refGroup = new THREE.Group();
  state.scene.add(state.refGroup);
  // 三轮用户口径：空间按推算渲染——量出回路占多大的地，扩到整跨柱网再
  // 每侧外扩一跨（无柱网退化为 6% 边距）。
  const extent = horizontalExtent(envelope, bounds.diagonal * 0.06);
  const diag = bounds.diagonal;
  const tubeRadius = radius * 0.9;
  const devices = [];
  const buses = [];
  let modelTopZ = 0;
  let modelBottomZ = 0;
  let cornerFlip = 0;
  for (const level of levels) {
    const kind = String(level.Kind || 'frame').toLowerCase();
    if (kind !== 'device') {
      if (level.Z > modelTopZ) modelTopZ = level.Z;
      if (level.Z < modelBottomZ) modelBottomZ = level.Z;
    }
    // 板/梁/格栅的角标签左右交替锚——全锚同一角会在屏幕上叠成一摞
    //（第二轮实测）。
    const anchorRight = cornerFlip++ % 2 === 1;
    if (kind === 'floor') addSlab(level, extent, diag, refColor.floor, refColor.floorEdge, Math.max(220, diag * 0.005), anchorRight);
    else if (kind === 'epoxy') addEpoxySlab(level, extent, diag, anchorRight);
    else if (kind === 'waffle') addWafflePlate(level, extent, diag, anchorRight, envelope);
    else if (kind === 'raised') addSlab(level, extent, diag, refColor.raised, refColor.floorEdge, Math.max(140, diag * 0.003), anchorRight);
    else if (kind === 'steel') addSteelBeams(level, extent, diag, anchorRight);
    else if (kind === 'grating') addGrating(level, extent, diag, anchorRight);
    else if (kind === 'device') devices.push(level);
    else if (kind === 'bus') buses.push(level);
    else addRefFrame(level, extent, tubeRadius);
  }
  addDevices(devices, extent, diag);
  // 母线=半透明玻璃层（用户口径：告诉用户这个高度是几层母线——锚定本层
  // 后正好在本层地面上方 3660/4220/4900）。
  addBusGlassLevels(buses, extent, diag);
  // 结构柱按柱网交点（每 ~4.8m 一个柱位，用户口径）落地到最高参照层。
  if (modelTopZ > 0) addGridColumns(envelope, extent, diag, modelTopZ);
  // 零平面铺板：有「本层」基准时本层楼板条目已经铺在 0，不再重复铺；
  // 没有基准（1F 绝对标高口径）时自动铺 1F 地面。
  if (base === 0) addGroundPlate(extent, diag, '1F地面');
}

// 环氧地坪：绿色泽面（Phong 高光表达树脂光泽）。
function addEpoxySlab(level, extent, diag, anchorRight) {
  const thickness = Math.max(180, diag * 0.004);
  state.refGroup.add(boxMesh(
    extent.W, extent.D, thickness,
    extent.minX + extent.W / 2, extent.minY + extent.D / 2,
    level.Z - thickness / 2,
    refColor.epoxy,
    { transparent: true, opacity: 0.5, edgeTint: refColor.epoxyEdge,
      phong: true, shininess: 90, specular: 0xa8c8b4 }));
  addCornerRefLabel(level, extent, level.Z, anchorRight);
}

// 华夫孔洞板：真圆孔穿透的板（ShapeGeometry 挖孔）。用户口径：**每 4 个
// 柱位围成的格间里 9 个圆孔（3×3）**——孔位按柱网分间布，无柱网时退化为
// 均匀阵列兜底。
function addWafflePlate(level, extent, diag, anchorRight, envelope) {
  const shape = new THREE.Shape();
  shape.moveTo(extent.minX, extent.minY);
  shape.lineTo(extent.maxX, extent.minY);
  shape.lineTo(extent.maxX, extent.maxY);
  shape.lineTo(extent.minX, extent.maxY);
  shape.closePath();
  const axes = gridAxes(envelope, diag);
  if (axes.xs.length >= 2 && axes.ys.length >= 2) {
    for (let i = 0; i + 1 < axes.xs.length; i++) {
      const x0 = Math.max(axes.xs[i], extent.minX);
      const x1 = Math.min(axes.xs[i + 1], extent.maxX);
      for (let j = 0; j + 1 < axes.ys.length; j++) {
        const y0 = Math.max(axes.ys[j], extent.minY);
        const y1 = Math.min(axes.ys[j + 1], extent.maxY);
        const spanX = x1 - x0;
        const spanY = y1 - y0;
        if (spanX <= 0 || spanY <= 0) continue;
        const radius = Math.min(spanX, spanY) / 3 * 0.5 * 0.72;
        for (let a = 0; a < 3; a++) {
          for (let b = 0; b < 3; b++) {
            const hole = new THREE.Path();
            hole.absarc(x0 + spanX * (a + 0.5) / 3,
              y0 + spanY * (b + 0.5) / 3, radius, 0, Math.PI * 2, true);
            shape.holes.push(hole);
          }
        }
      }
    }
  } else {
    const span = Math.min(extent.W, extent.D);
    const pitch = clamp(span / 7, 900, 2400);
    const radius = pitch * 0.36;
    const inset = pitch * 0.8;
    for (let cx = extent.minX + inset; cx <= extent.maxX - inset; cx += pitch) {
      for (let cy = extent.minY + inset; cy <= extent.maxY - inset; cy += pitch) {
        const hole = new THREE.Path();
        hole.absarc(cx, cy, radius, 0, Math.PI * 2, true);
        shape.holes.push(hole);
      }
    }
  }
  const plate = new THREE.Mesh(
    new THREE.ShapeGeometry(shape),
    new THREE.MeshLambertMaterial({
      color: refColor.waffle, transparent: true, opacity: 0.42,
      depthWrite: false, side: THREE.DoubleSide
    }));
  plate.position.z = level.Z;
  state.refGroup.add(plate);
  addCornerRefLabel(level, extent, level.Z, anchorRight);
}

// 母线玻璃层：横贯包络的半透明琥珀玻璃板，每层一块+标签。
function addBusGlassLevels(levels, extent, diag) {
  if (levels.length === 0) return;
  let flip = 0;
  const thickness = Math.max(70, diag * 0.0018);
  for (const level of levels.slice().sort((a, b) => a.Z - b.Z)) {
    state.refGroup.add(boxMesh(
      extent.W, extent.D, thickness,
      extent.minX + extent.W / 2, extent.minY + extent.D / 2,
      level.Z - thickness / 2,
      refColor.busGlass,
      { transparent: true, opacity: 0.16, edgeTint: refColor.busEdge,
        phong: true, shininess: 120, specular: 0xd9c9a0 }));
    addCornerRefLabel(level, extent, level.Z, flip++ % 2 === 1);
  }
}

// 柱网两向轴线集合（X 向线集 + Y 向线集，升序）——结构柱与华夫分间共用。
function gridAxes(envelope, diag) {
  const lines = envelope.ColumnGridLines || [];
  const eps = Math.max(diag * 0.0005, 1);
  const xs = [];
  const ys = [];
  for (let index = 0; index + 1 < lines.length; index += 2) {
    const a = lines[index];
    const b = lines[index + 1];
    if (!a || !b) continue;
    if (Math.abs(a.X - b.X) < eps) pushUnique(xs, a.X, eps);
    else if (Math.abs(a.Y - b.Y) < eps) pushUnique(ys, a.Y, eps);
  }
  xs.sort((p, q) => p - q);
  ys.sort((p, q) => p - q);
  return { xs, ys, eps };
}

// 结构柱：柱网两向线交点（每 ~4.8m 一个柱位），包络内的交点落地生根。
function addGridColumns(envelope, extent, diag, topZ) {
  const axes = gridAxes(envelope, diag);
  const xs = axes.xs;
  const ys = axes.ys;
  const eps = axes.eps;
  if (xs.length === 0 || ys.length === 0) return;
  const size = Math.max(350, diag * 0.014);
  let count = 0;
  for (const x of xs) {
    for (const y of ys) {
      if (x < extent.minX - eps || x > extent.maxX + eps
          || y < extent.minY - eps || y > extent.maxY + eps) continue;
      if (++count > 400) return;
      state.refGroup.add(boxMesh(size, size, topZ, x, y, topZ / 2,
        refColor.column,
        { transparent: true, opacity: 0.95, emissive: refColor.columnGlow }));
    }
  }
}

function pushUnique(list, value, eps) {
  for (const existing of list) {
    if (Math.abs(existing - value) < eps) return;
  }
  list.push(value);
}

// 零平面铺板：无「本层」基准时自动铺 1F 地面（归零口径：最低线段端点
// Z=0 即 1F 地面）。
function addGroundPlate(extent, diag, label) {
  const thickness = Math.max(60, diag * 0.0015);
  state.refGroup.add(boxMesh(
    extent.W, extent.D, thickness,
    extent.minX + extent.W / 2, extent.minY + extent.D / 2, -thickness / 2,
    refColor.ground,
    { transparent: true, opacity: 0.5, edges: false, emissive: 0x10141a }));
  addLabel({ Text: label + ' 0',
    X: extent.minX - extent.margin * 0.35,
    Y: extent.minY - extent.margin * 0.35, Z: 0 }, 'label-ref');
}

// 楼板/高架地板：横贯包络的半透明板（顶面=标高 Z），双面+边线。
// 首轮视觉实测：0.3 透明度在深底上读作空线框——提到 0.45 并提亮。
function addSlab(level, extent, diag, tint, edgeTint, thickness, anchorRight) {
  state.refGroup.add(boxMesh(
    extent.W, extent.D, thickness,
    extent.minX + extent.W / 2, extent.minY + extent.D / 2,
    level.Z - thickness / 2,
    tint, { transparent: true, opacity: 0.45, edgeTint }));
  addCornerRefLabel(level, extent, level.Z, anchorRight);
}

// 工字钢梁阵：等距工字截面梁横贯 X（上翼缘+腹板+下翼缘，顶面=标高 Z）。
function addSteelBeams(level, extent, diag, anchorRight) {
  const z = level.Z;
  const group = ensureDetailGroup();
  const height = Math.max(400, diag * 0.014);
  const flange = Math.max(220, height * 0.6);
  const flangeT = Math.max(55, height * 0.18);
  const webT = Math.max(70, height * 0.14);
  const count = clamp(Math.round(extent.D / (diag * 0.16)), 3, 8);
  const cx = extent.minX + extent.W / 2;
  for (let index = 0; index < count; index++) {
    const y = extent.minY + extent.D * (index + 0.5) / count;
    group.add(boxMesh(extent.W, flange, flangeT, cx, y, z - flangeT / 2, refColor.steel));
    group.add(boxMesh(extent.W, webT, height - 2 * flangeT, cx, y,
      z - flangeT - (height - 2 * flangeT) / 2, refColor.steel, { edges: false }));
    group.add(boxMesh(extent.W, flange, flangeT, cx, y, z - height + flangeT / 2, refColor.steel, { edges: false }));
  }
  addCornerRefLabel(level, extent, z, anchorRight, 'detail');
}

// 葡萄架格栅：细条阵列横贯 X（顶面=标高 Z），镂空感。
function addGrating(level, extent, diag, anchorRight) {
  const z = level.Z;
  const group = ensureDetailGroup();
  const barH = Math.max(140, diag * 0.005);
  const barW = Math.max(110, diag * 0.004);
  const count = clamp(Math.round(extent.D / (diag * 0.04)), 8, 20);
  const cx = extent.minX + extent.W / 2;
  for (let index = 0; index < count; index++) {
    const y = extent.minY + extent.D * (index + 0.5) / count;
    group.add(boxMesh(extent.W, barW, barH, cx, y, z - barH / 2,
      refColor.grating));
  }
  addCornerRefLabel(level, extent, z, anchorRight, 'detail');
}

// 设备盒（I-L盘/插座盘…）：从场景地面立到标高 Z 的实体盒，沿底边排开
// （平面位置无数据，示意；高度准确）。首轮实测盒体过小读不出——放大。
function addDevices(levels, extent, diag) {
  if (levels.length === 0) return;
  const footX = Math.max(1000, diag * 0.042);
  const footY = footX * 0.7;
  for (let index = 0; index < levels.length; index++) {
    const level = levels[index];
    const x = extent.minX + extent.W * (index + 1) / (levels.length + 1);
    const y = extent.minY + extent.margin * 0.22 + footY / 2;
    state.refGroup.add(boxMesh(footX, footY, level.Z, x, y, level.Z / 2,
      refColor.device,
      { edgeTint: refColor.deviceEdge, emissive: refColor.deviceGlow }));
    addRefLabel(level, x, y, level.Z + diag * 0.014);
  }
}

// 3.3.6 兼容：无类型/未知类型的水平线框（琥珀色四边+角标签）。
function addRefFrame(level, extent, tubeRadius) {
  const z = level.Z;
  addTubeTo(state.refGroup, { x: extent.minX, y: extent.minY, z }, { x: extent.maxX, y: extent.minY, z }, color.reference, tubeRadius);
  addTubeTo(state.refGroup, { x: extent.maxX, y: extent.minY, z }, { x: extent.maxX, y: extent.maxY, z }, color.reference, tubeRadius);
  addTubeTo(state.refGroup, { x: extent.maxX, y: extent.maxY, z }, { x: extent.minX, y: extent.maxY, z }, color.reference, tubeRadius);
  addTubeTo(state.refGroup, { x: extent.minX, y: extent.maxY, z }, { x: extent.minX, y: extent.minY, z }, color.reference, tubeRadius);
  addRefLabel(level, extent.minX - extent.margin * 0.35,
    extent.minY - extent.margin * 0.35, z);
}

function addRefLabel(level, x, y, z, group) {
  addLabel({ Text: level.Label + ' ' + level.Z, X: x, Y: y, Z: z },
    'label-ref', group);
}

// 板/梁/格栅的角标签：左右角交替锚（同角全叠在屏幕一摞）。
function addCornerRefLabel(level, extent, z, anchorRight, group) {
  addRefLabel(level,
    anchorRight ? extent.maxX + extent.margin * 0.2
                : extent.minX - extent.margin * 0.35,
    extent.minY - extent.margin * 0.35, z, group);
}

// Lambert 盒子（可选边线）；透明盒双面且不写深度，避免挡死路由。
function boxMesh(sizeX, sizeY, sizeZ, cx, cy, cz, tint, options) {
  const settings = options || {};
  const common = {
    color: tint,
    transparent: !!settings.transparent,
    opacity: settings.opacity === undefined ? 1 : settings.opacity,
    depthWrite: !settings.transparent,
    side: settings.transparent ? THREE.DoubleSide : THREE.FrontSide
  };
  const material = settings.phong
    ? new THREE.MeshPhongMaterial(Object.assign(common, {
        shininess: settings.shininess === undefined ? 60 : settings.shininess,
        specular: settings.specular === undefined ? 0x9aa8b8 : settings.specular
      }))
    : new THREE.MeshLambertMaterial(common);
  if (settings.emissive) material.emissive.setHex(settings.emissive);
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(sizeX, sizeY, sizeZ), material);
  mesh.position.set(cx, cy, cz);
  if (settings.edges !== false) {
    mesh.add(new THREE.LineSegments(
      new THREE.EdgesGeometry(mesh.geometry),
      new THREE.LineBasicMaterial({
        color: settings.edgeTint === undefined ? tint : settings.edgeTint,
        transparent: true, opacity: 0.85
      })));
  }
  return mesh;
}

function clamp(value, low, high) {
  return Math.min(high, Math.max(low, value));
}

// 水平包络——三轮用户口径「分析回路会占用多大的空间 然后渲染出来更大的
// 空间」：有柱网时把回路footprint 扩到整跨柱网、每侧再外扩一跨（结构的
// 房间感来自柱网而不是百分比边距）；无柱网退化为 6% 边距。
function horizontalExtent(envelope, margin) {
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
  for (const point of collectHorizontalBounds(envelope)) {
    minX = Math.min(minX, point[0]); maxX = Math.max(maxX, point[0]);
    minY = Math.min(minY, point[1]); maxY = Math.max(maxY, point[1]);
  }
  const axes = gridAxes(envelope, margin / 0.06);
  const xs = axes.xs;
  const ys = axes.ys;
  if (xs.length >= 2 && ys.length >= 2) {
    const gap = values => {
      let best = 0;
      for (let i = 1; i < values.length; i++) {
        best = Math.max(best, values[i] - values[i - 1]);
      }
      return best;
    };
    const bayX = gap(xs) || margin;
    const bayY = gap(ys) || margin;
    const snapOut = (values, lo, hi, bay) => {
      let below = values[0];
      let above = values[values.length - 1];
      for (const value of values) {
        if (value <= lo) below = value;
        if (value >= hi && above > value) above = value;
      }
      // 兜底：回路超出柱网两端时也要保证至少一跨的裕量。
      return [Math.min(below - bay, lo - bay), Math.max(above + bay, hi + bay)];
    };
    [minX, maxX] = snapOut(xs, minX, maxX, bayX);
    [minY, maxY] = snapOut(ys, minY, maxY, bayY);
    return { minX, minY, maxX, maxY, margin, W: maxX - minX, D: maxY - minY };
  }
  minX -= margin; minY -= margin; maxX += margin; maxY += margin;
  return { minX, minY, maxX, maxY, margin, W: maxX - minX, D: maxY - minY };
}

function collectHorizontalBounds(envelope) {
  const points = [];
  const feed = point => points.push([point.X, point.Y]);
  for (const segment of envelope.Segments || []) {
    feed(segment.Start); feed(segment.End);
    const channel = segment.Channel;
    if (channel) { feed(channel.A1); feed(channel.A2); feed(channel.B1); feed(channel.B2); }
  }
  if (points.length === 0) points.push([0, 0], [1000, 1000]);
  return points;
}

// 带目标组的细管（参照层要走 refGroup 才能整组开关）。
function addTubeTo(group, from, to, tint, radius) {
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
  group.add(tube);
}

// 结构细节组（工字钢梁阵+葡萄架格栅）：默认隐藏，走独立「结构」开关；
// 仍挂在 refGroup 之下，参照整组关闭时随组消失。
function ensureDetailGroup() {
  if (!state.detailGroup) {
    state.detailGroup = new THREE.Group();
    state.detailGroup.visible = state.showDetail;
    state.refGroup.add(state.detailGroup);
  }
  return state.detailGroup;
}

function setDetailVisible(visible) {
  state.showDetail = visible;
  if (state.detailGroup) state.detailGroup.visible = visible;
  if (el.detail) {
    el.detail.textContent = visible ? '结构 开' : '结构 关';
    el.detail.style.display = state.detailGroup ? '' : 'none';
  }
  for (const label of state.labels) {
    if (label.group === 'detail') {
      label.element.style.display =
        visible && state.showReferenceLevels ? '' : 'none';
    }
  }
}

function setReferenceVisible(visible) {
  state.showReferenceLevels = visible;
  if (state.refGroup) state.refGroup.visible = visible;
  for (const label of state.labels) {
    if (label.group === 'ref') label.element.style.display = visible ? '' : 'none';
    if (label.group === 'detail') {
      label.element.style.display =
        visible && state.showDetail ? '' : 'none';
    }
  }
  el.ref.textContent = visible ? '参照 开' : '参照 关';
  el.ref.style.display = state.refLevelCount > 0 ? '' : 'none';
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

function addLabel(label, cssClass, groupOverride) {
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
    group: groupOverride
      || (cssClass === 'label-tick' ? 'grid'
        : cssClass === 'label-ref' ? 'ref' : 'route')
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
  // 3.3.7：参照物按实际出现的类型标注（楼板/钢梁/格栅/高架板/设备/母线/
  // 线框），没有的不占位。
  const kinds = new Set((envelope.ReferenceLevels || [])
    .map(level => String((level && level.Kind) || 'frame').toLowerCase()));
  const refParts = [];
  if (kinds.has('floor')) refParts.push('灰=楼板');
  if (kinds.has('epoxy')) refParts.push('绿=环氧地坪');
  if (kinds.has('waffle')) refParts.push('华夫=圆孔板');
  if (kinds.has('steel')) refParts.push('银=钢梁');
  if (kinds.has('grating')) refParts.push('架=葡萄架');
  if (kinds.has('raised')) refParts.push('蓝灰=高架板');
  if (kinds.has('device')) refParts.push('白=设备');
  if (kinds.has('bus')) refParts.push('琥珀玻璃=母线层');
  if (kinds.has('frame')) refParts.push('黄=标高线');
  if (refParts.length > 0) legend += '　参照: ' + refParts.join(' ');
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
    // 开关状态必须在这里再判一遍：本函数每帧重写 display，3.3.5 起它把
    // 柱网/参照开关藏掉的标签下一帧又放回来（本轮顺手修复）。
    let shown = onScreen;
    if (shown && label.group === 'grid') shown = state.showGrid;
    else if (shown && label.group === 'ref') shown = state.showReferenceLevels;
    else if (shown && label.group === 'detail') {
      shown = state.showReferenceLevels && state.showDetail;
    }
    label.element.style.display = shown ? '' : 'none';
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
