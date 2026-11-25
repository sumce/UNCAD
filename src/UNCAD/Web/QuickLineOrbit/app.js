// 3.8.0.5：U1U 确认窗左侧 3D 路由预览页（WebView2 + three.js）。
// 哑渲染器：全部场景语义（楼层/柱网/轴位/通道偏移/标签锚点/柜体）由宿主侧
// Core 信封 QuickLineOrbitPreviewEnvelope 算好传入，页面只负责画线、叠标签、
// 回消息。页面自带 vendor 的 three.js，不访问网络（CSP default-src 'self'）。
import * as THREE from './vendor/three/three.module.min.js';
import { OrbitControls } from './vendor/three/OrbitControls.js';

const el = {
  viewport: document.getElementById('viewport'),
  canvasHost: document.getElementById('canvasHost'),
  labelLayer: document.getElementById('labelLayer'),
  loading: document.getElementById('loading'),
  issues: document.getElementById('issues'),
  summary: document.getElementById('summary'),
  legend: document.getElementById('legend'),
  grid: document.getElementById('gridButton'),
  reset: document.getElementById('resetButton')
};

const color = {
  route: 0xff8a1e,
  grid: 0x55606e, floor: 0x7d93a6,
  tray: 0xf2574c, trayFill: 0xf2574c,
  conduit: 0x2ea8c0, pin: 0x99a7b8,
  cabinet: 0xc9d3de, cabinetEdge: 0x5d6977,
  hose: 0x5fc98a
};

const state = {
  renderer: null, scene: null, camera: null, controls: null,
  gridGroup: null, routeGroup: null, referenceGroup: null,
  labels: [], diagonal: 1,
  showGrid: true, hasGrid: false,
  defaultCamera: null, defaultTarget: null
};

boot();

function boot() {
  try {
    initRenderer();
    bindUi();
    animate();
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.addEventListener('message',
        event => receive(event.data));
      post({ type: 'ready', schemaVersion: 1 });
    } else {
      // 宿主外（浏览器直开/自检）：允许 window.postMessage 注入信封。
      window.addEventListener('message', event => receive(event.data));
      el.loading.textContent = '等待宿主数据...';
    }
  } catch (error) { fail(error); }
}

function initRenderer() {
  state.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
  state.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  state.renderer.setClearColor(0x0d131a, 1);
  el.canvasHost.appendChild(state.renderer.domElement);
  state.scene = new THREE.Scene();
  // CAD 是 Z 朝上。
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
    if (!message) return;
    if (message.type === 'resetCamera') { resetCamera(); return; }
    if (message.type === 'setColumnGrid') {
      setGridVisible(!!message.enabled);
      return;
    }
    if (message.type !== 'initialize') return;
    renderEnvelope(message.envelope);
  } catch (error) { fail(error); }
}

// ---- 场景构建（只消费信封里的现成几何） ----------------------------------

function renderEnvelope(envelope) {
  clearScene();
  const segments = (envelope && envelope.Segments) || [];
  if (!envelope || segments.length === 0) {
    state.hasGrid = false;
    setGridVisible(state.showGrid);
    el.issues.textContent = '';
    el.summary.textContent = '';
    el.legend.textContent = '';
    el.loading.textContent = '该图框没有可预览的带毫米标注 U1L 路由';
    el.loading.style.display = 'grid';
    return;
  }
  state.diagonal = Math.max(Number(envelope.Diagonal) || 1, 1);
  // 3.8.0.8：线路加粗（现场「清晰的回路」）——细管在楼板网格里不够醒目。
  const radius = state.diagonal * 0.0028;
  const labelHeight = state.diagonal * 0.016;

  state.referenceGroup = new THREE.Group();
  state.gridGroup = new THREE.Group();
  state.routeGroup = new THREE.Group();
  state.scene.add(state.referenceGroup);
  state.scene.add(state.gridGroup);
  state.scene.add(state.routeGroup);

  // 柜体是实体盒，给两盏灯；管线/线框是 Basic 材质不受影响。
  state.scene.add(new THREE.HemisphereLight(0xe8eef5, 0x141a21, 0.95));
  const sun = new THREE.DirectionalLight(0xffffff, 0.65);
  sun.position.set(-0.55, -0.75, 1);
  state.scene.add(sun);

  // 3.8.0.6：取景按线路本身（段/柜/钉/软管），不按整块楼板——否则几十米
  // 楼板把 30 米的线路压成中间一小撮，标签全挤在一起。
  const bounds = measureRouteBounds(envelope);
  addFloors(envelope);
  addColumnGrid(envelope);
  addChannels(envelope, radius);
  addRouteLines(envelope, radius);
  addHose(envelope, radius);
  addCabinet(envelope, radius);
  addPinsAndDirections(envelope, radius, labelHeight);
  collectLabels(envelope);

  const viewBounds = bounds;
  state.scene.fog = new THREE.Fog(0x0d131a,
    viewBounds.diagonal * 2.4, viewBounds.diagonal * 9);
  applyDefaultCamera(viewBounds);
  state.hasGrid = (envelope.Floors || []).length > 0;
  setGridVisible(state.showGrid);
  updateInfo(envelope);
  el.loading.style.display = 'none';
  // 首帧已建好场景（WebGL 上下文在 boot 里创建，失败会走 renderError）。
  // 回报段数/标签给宿主做运行时自检（真机不再只凭「ready」判断页面活了）。
  post({
    type: 'rendered', schemaVersion: 1,
    segments: segments.length, labels: state.labels.length
  });
}

function clearScene() {
  while (state.scene.children.length > 0)
    state.scene.remove(state.scene.children[0]);
  for (const label of state.labels) label.element.remove();
  state.labels = [];
  state.gridGroup = null;
  state.routeGroup = null;
  state.referenceGroup = null;
  state.hasGrid = false;
  el.loading.style.display = 'grid';
}

function measureRouteBounds(envelope) {
  let minX = Infinity, minY = Infinity, minZ = Infinity;
  let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
  const feed = (x, y, z) => {
    if (!Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(z)) return;
    minX = Math.min(minX, x); maxX = Math.max(maxX, x);
    minY = Math.min(minY, y); maxY = Math.max(maxY, y);
    minZ = Math.min(minZ, z); maxZ = Math.max(maxZ, z);
  };
  for (const segment of envelope.Segments || []) {
    feed(segment.Start.X, segment.Start.Y, segment.Start.Z);
    feed(segment.End.X, segment.End.Y, segment.End.Z);
  }
  for (const pin of envelope.EndpointPins || [])
    feed(pin.X, pin.Y, pin.LabelZ);
  const cabinet = envelope.Cabinet;
  if (cabinet) {
    const topZ = Number(cabinet.BaseZ) + Number(cabinet.Height);
    feed(cabinet.CenterX - cabinet.SizeX / 2,
      cabinet.CenterY - cabinet.SizeY / 2,
      cabinet.MountingFloorZ != null ? cabinet.MountingFloorZ : cabinet.BaseZ);
    feed(cabinet.CenterX + cabinet.SizeX / 2,
      cabinet.CenterY + cabinet.SizeY / 2, topZ);
  }
  if (envelope.Hose) {
    feed(envelope.Hose.Start.X, envelope.Hose.Start.Y, envelope.Hose.Start.Z);
    feed(envelope.Hose.End.X, envelope.Hose.End.Y, envelope.Hose.End.Z);
  }
  if (!isFinite(minX)) { minX = minY = minZ = 0; maxX = maxY = maxZ = 1000; }
  return {
    centre: new THREE.Vector3((minX + maxX) / 2, (minY + maxY) / 2,
      (minZ + maxZ) / 2),
    diagonal: Math.max(Math.hypot(maxX - minX, maxY - minY, maxZ - minZ), 1),
    minZ, maxZ
  };
}

function measureBounds(envelope) {
  let minX = Infinity, minY = Infinity, minZ = Infinity;
  let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
  const feed = (x, y, z) => {
    // 防御：缺字段/NaN 不参与取景（一个 undefined 会让整台相机变 NaN，整屏黑）。
    if (!Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(z)) return;
    minX = Math.min(minX, x); maxX = Math.max(maxX, x);
    minY = Math.min(minY, y); maxY = Math.max(maxY, y);
    minZ = Math.min(minZ, z); maxZ = Math.max(maxZ, z);
  };
  for (const segment of envelope.Segments || []) {
    feed(segment.Start.X, segment.Start.Y, segment.Start.Z);
    feed(segment.End.X, segment.End.Y, segment.End.Z);
  }
  for (const floor of envelope.Floors || []) {
    feed(floor.MinX, floor.MinY, floor.Z);
    feed(floor.MaxX, floor.MaxY, floor.Z);
  }
  for (const line of envelope.ColumnGridLines || [])
    feed(line.X, line.Y, line.Z);
  for (const pin of envelope.EndpointPins || [])
    feed(pin.X, pin.Y, pin.LabelZ);
  const cabinet = envelope.Cabinet;
  if (cabinet) {
    const topZ = Number(cabinet.BaseZ) + Number(cabinet.Height);
    feed(cabinet.CenterX - cabinet.SizeX / 2,
      cabinet.CenterY - cabinet.SizeY / 2,
      cabinet.MountingFloorZ != null ? cabinet.MountingFloorZ : cabinet.BaseZ);
    feed(cabinet.CenterX + cabinet.SizeX / 2,
      cabinet.CenterY + cabinet.SizeY / 2, topZ);
  }
  if (envelope.Hose) {
    feed(envelope.Hose.Start.X, envelope.Hose.Start.Y, envelope.Hose.Start.Z);
    feed(envelope.Hose.End.X, envelope.Hose.End.Y, envelope.Hose.End.Z);
  }
  if (!isFinite(minX)) { minX = minY = minZ = 0; maxX = maxY = maxZ = 1000; }
  return {
    centre: new THREE.Vector3((minX + maxX) / 2, (minY + maxY) / 2,
      (minZ + maxZ) / 2),
    diagonal: Math.max(Math.hypot(maxX - minX, maxY - minY, maxZ - minZ), 1),
    minZ, maxZ
  };
}

function addFloors(envelope) {
  for (const floor of envelope.Floors || []) {
    // 线框：只画边框 + 柱网（半透明面会挡住另一侧的线路）。
    const corners = [
      [floor.MinX, floor.MinY], [floor.MaxX, floor.MinY],
      [floor.MaxX, floor.MaxY], [floor.MinX, floor.MaxY]
    ];
    const positions = [];
    for (let i = 0; i < 4; i++) {
      const a = corners[i], b = corners[(i + 1) % 4];
      positions.push(a[0], a[1], floor.Z, b[0], b[1], floor.Z);
    }
    const outline = new THREE.BufferGeometry();
    outline.setAttribute('position',
      new THREE.Float32BufferAttribute(positions, 3));
    state.referenceGroup.add(new THREE.LineSegments(outline,
      new THREE.LineBasicMaterial({ color: color.floor, transparent: true,
        opacity: 0.85 })));
    const grid = (floor.GridLines || []);
    if (grid.length >= 2) {
      const gridPositions = [];
      for (let i = 0; i + 1 < grid.length; i += 2) {
        gridPositions.push(grid[i].X, grid[i].Y, grid[i].Z,
          grid[i + 1].X, grid[i + 1].Y, grid[i + 1].Z);
      }
      const geometry = new THREE.BufferGeometry();
      geometry.setAttribute('position',
        new THREE.Float32BufferAttribute(gridPositions, 3));
      state.referenceGroup.add(new THREE.LineSegments(geometry,
        new THREE.LineBasicMaterial({ color: color.grid, transparent: true,
          opacity: 0.55 })));
    }
    const width = floor.MaxX - floor.MinX;
    const depth = floor.MaxY - floor.MinY;
    addLabel({
      Text: floor.Label + '  ' + floor.ElevationText,
      X: floor.MinX + width * 0.02,
      Y: floor.MaxY + depth * 0.03,
      Z: floor.Z
    }, 'label-floor', 'reference', 'floor');
  }
}

function addColumnGrid(envelope) {
  const lines = envelope.ColumnGridLines || [];
  if (lines.length >= 2) {
    const positions = [];
    for (let i = 0; i + 1 < lines.length; i += 2) {
      positions.push(lines[i].X, lines[i].Y, lines[i].Z,
        lines[i + 1].X, lines[i + 1].Y, lines[i + 1].Z);
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position',
      new THREE.Float32BufferAttribute(positions, 3));
    state.gridGroup.add(new THREE.LineSegments(geometry,
      new THREE.LineBasicMaterial({ color: color.grid, transparent: true,
        opacity: 0.75 })));
  }
  // 3.8.0.8（用户口径「每个方格交叉点都加一个轴位」）：网格交点铺坐标，
  // 如「55/C」；边标/平面浮标都不再画。
  addGridIntersectionLabels(envelope);
}

// 每个柱网交点一个坐标牌（数字/字母），铺满整块楼层平面；数量过大时按步长
// 抽稀到约 900 个。轴位坐标由平面内刻度/字母（NumericTicks/LetterLabels）外推：
// 每个柱网线一个轴号、间距 4.8 m，方向与刻度一致。
function addGridIntersectionLabels(envelope) {
  const ticks = envelope.NumericTicks || [];
  const letters = envelope.LetterLabels || [];
  const lines = envelope.ColumnGridLines || [];
  if (ticks.length < 2 || letters.length < 2 || lines.length < 4) return;
  const xs = [], ys = [];
  const xsSeen = new Set(), ysSeen = new Set();
  for (let i = 0; i + 1 < lines.length; i += 2) {
    const a = lines[i], b = lines[i + 1];
    if (Math.abs(a.X - b.X) < 1e-3) {
      if (!xsSeen.has(a.X)) { xsSeen.add(a.X); xs.push(a.X); }
    } else if (Math.abs(a.Y - b.Y) < 1e-3) {
      if (!ysSeen.has(a.Y)) { ysSeen.add(a.Y); ys.push(a.Y); }
    }
  }
  xs.sort((left, right) => left - right);
  ys.sort((left, right) => left - right);
  if (xs.length === 0 || ys.length === 0) return;
  const numericIsX = ticks.every(tick => Math.abs(tick.Y - ticks[0].Y) < 1.0);
  const tick0 = ticks[0];
  const tick1 = ticks[1];
  const tickPos = value => Number(numericIsX ? value.X : value.Y);
  const tickNumber = value => parseInt(value.Text, 10);
  const tickSpan = tickPos(tick1) - tickPos(tick0);
  const tickStep = tickNumber(tick1) - tickNumber(tick0);
  if (!Number.isFinite(tickSpan) || Math.abs(tickSpan) < 1.0 || tickStep === 0)
    return;
  const numberSign = Math.sign(tickStep) * Math.sign(tickSpan);
  const pitch = Math.abs(tickSpan / Math.abs(tickStep));
  const letterPosition = value => Number(numericIsX ? value.Y : value.X);
  const letter0 = letters[0];
  const letter1 = letters[1];
  const letterSpan = letterPosition(letter1) - letterPosition(letter0);
  const letterStep = letterIndex(letter1.Text) - letterIndex(letter0.Text);
  if (!Number.isFinite(letterSpan) || Math.abs(letterSpan) < 1.0
    || letterStep === 0) return;
  const letterSign = Math.sign(letterStep) * Math.sign(letterSpan);
  const numericLines = numericIsX ? xs : ys;
  const letterLines = numericIsX ? ys : xs;
  const total = numericLines.length * letterLines.length;
  const stride = total > 900 ? Math.ceil(Math.sqrt(total / 900)) : 1;
  const baseNumber = tickNumber(tick0);
  const basePos = tickPos(tick0);
  const baseLetter = letterIndex(letter0.Text);
  const baseLetterPos = letterPosition(letter0);
  for (let nx = 0; nx < numericLines.length; nx += stride) {
    const numericPos = numericLines[nx];
    const number = Math.round(baseNumber
      + (numericPos - basePos) / pitch * numberSign);
    if (number < 1) continue;
    for (let ly = 0; ly < letterLines.length; ly += stride) {
      const letterPos = letterLines[ly];
      const index = Math.round(baseLetter
        + (letterPos - baseLetterPos) / pitch * letterSign);
      const letter = lettersFromIndex(index);
      if (!letter) continue;
      const x = numericIsX ? numericPos : letterPos;
      const y = numericIsX ? letterPos : numericPos;
      const element = document.createElement('div');
      element.className = 'scene-label label-grid';
      element.textContent = number + '/' + letter;
      el.labelLayer.appendChild(element);
      const entry = {
        element,
        position: new THREE.Vector3(x, y, 0),
        group: 'grid',
        direction: null,
        side: 'above',
        kind: 'axis',
        lattice: true
      };
      measureLabel(entry);
      state.labels.push(entry);
    }
  }
}

// 26 进制双射：1=A … 26=Z、27=AA。
function letterIndex(text) {
  let index = 0;
  for (const character of String(text || '').toUpperCase()) {
    const digit = character.charCodeAt(0) - 64;
    if (digit < 1 || digit > 26) return 0;
    index = index * 26 + digit;
  }
  return index;
}

function lettersFromIndex(index) {
  if (!Number.isFinite(index) || index < 1) return null;
  let remaining = index;
  let result = '';
  while (remaining > 0) {
    const digit = (remaining - 1) % 26;
    result = String.fromCharCode(65 + digit) + result;
    remaining = (remaining - 1) / 26;
  }
  return result;
}

function addChannels(envelope, radius) {
  const railRadius = radius * 0.7;
  for (const segment of envelope.Segments || []) {
    const channel = segment.Channel;
    if (!channel) continue;
    if (channel.Kind === 'tray') {
      const fill = new THREE.Mesh(fillGeometry(channel),
        new THREE.MeshBasicMaterial({
          color: color.trayFill, transparent: true, opacity: 0.3,
          side: THREE.DoubleSide, depthWrite: false
        }));
      state.routeGroup.add(fill);
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
  // 电缆就是一色（现场「你加XYZ干嘛」）：不再按等测轴 X/Y/Z 换色。
  for (const segment of envelope.Segments || [])
    addTube(segment.Start, segment.End, color.route, radius);
}

function addHose(envelope, radius) {
  const hose = envelope.Hose;
  if (!hose) return;
  addTube(hose.Start, hose.End, color.hose, radius * 1.1);
  if (hose.Label) {
    addLineLabel(hose.Label,
      (hose.Start.X + hose.End.X) / 2, (hose.Start.Y + hose.End.Y) / 2,
      (hose.Start.Z + hose.End.Z) / 2,
      hose.End.X - hose.Start.X, hose.End.Y - hose.Start.Y,
      hose.End.Z - hose.Start.Z, 'label-mm', 'above');
  }
}

function addCabinet(envelope, radius) {
  const cabinet = envelope.Cabinet;
  if (!cabinet) return;
  const box = new THREE.Mesh(
    new THREE.BoxGeometry(cabinet.SizeX, cabinet.SizeY, cabinet.Height),
    new THREE.MeshLambertMaterial({ color: color.cabinet }));
  box.position.set(cabinet.CenterX, cabinet.CenterY,
    cabinet.BaseZ + cabinet.Height / 2);
  state.referenceGroup.add(box);
  const edges = new THREE.LineSegments(
    new THREE.EdgesGeometry(box.geometry),
    new THREE.LineBasicMaterial({ color: color.cabinetEdge }));
  edges.position.copy(box.position);
  state.referenceGroup.add(edges);
  if (cabinet.Kind === 'SocketPanel' && cabinet.MountingFloorZ != null
    && cabinet.BaseZ - cabinet.MountingFloorZ > 1) {
    addTube({ X: cabinet.CenterX, Y: cabinet.CenterY, Z: cabinet.MountingFloorZ },
      { X: cabinet.CenterX, Y: cabinet.CenterY, Z: cabinet.BaseZ },
      color.cabinet, Math.max(radius * 0.4, state.diagonal * 0.0004));
  }
  const connection = cabinet.Connection;
  if (connection) {
    addTube(connection, {
      X: cabinet.CenterX,
      Y: cabinet.CenterY,
      Z: Math.max(cabinet.BaseZ + cabinet.Height * 0.15,
        Math.min(cabinet.TopZ - cabinet.Height * 0.15, connection.Z))
    }, color.cabinetEdge, radius * 0.5);
  }
  const note = cabinet.Label.startsWith('插座') ? ' · 离地 1 m'
    : cabinet.Label.startsWith('I-Line') ? ' · 落地' : '';
  addLabel({
    Text: cabinet.Label + note,
    X: cabinet.CenterX, Y: cabinet.CenterY,
    Z: cabinet.BaseZ + cabinet.Height * 0.5
  }, 'label-cabinet', 'reference', 'cabinet');
}

// 端点轴位牌落点：该轴号线与字母线的交点（网格夹角）；找不到退回端点。
function axisCorner(envelope, axisText) {
  const match = /^\s*(\d{1,6})\s*[\/\-\s]\s*([A-Za-z]{1,4})\s*$/.exec(axisText || '');
  if (!match) return null;
  const ticks = envelope.NumericTicks || [];
  const letters = envelope.LetterLabels || [];
  if (ticks.length === 0 || letters.length === 0) return null;
  const numericIsX = ticks.every(tick => Math.abs(tick.Y - ticks[0].Y) < 1.0);
  const tick = ticks.find(item => String(item.Text).trim() === match[1]);
  const letter = letters.find(item =>
    String(item.Text).trim().toUpperCase() === match[2].toUpperCase());
  if (!tick || !letter) return null;
  return numericIsX
    ? { X: tick.X, Y: letter.Y, Z: 0 }
    : { X: letter.X, Y: tick.Y, Z: 0 };
}

function addPinsAndDirections(envelope, radius, labelHeight) {
  const pins = envelope.EndpointPins || [];
  // 未确认方向时没有「上游端/设备端」牌子，保留轴位钉文字兜底。
  if (!envelope.DirectionResolved) {
    for (const pin of pins)
      addLabel({ Text: pin.Text, X: pin.X, Y: pin.Y, Z: pin.LabelZ },
        'label-pin', 'route', 'pin');
    return;
  }
  const upstream = envelope.UpstreamEndpoint;
  const downstream = envelope.DownstreamEndpoint;
  if (upstream) {
    let lift = labelHeight * 1.6;
    if (envelope.Cabinet) {
      const topZ = Number(envelope.Cabinet.BaseZ) + Number(envelope.Cabinet.Height);
      lift = Math.max(lift, topZ - upstream.Z + labelHeight * 1.2);
    }
    const axis = pins.length > 0 ? pins[0].Text : '';
    const corner = axisCorner(envelope, axis);
    if (corner) {
      addTube(corner, upstream, color.pin, state.diagonal * 0.00035);
      lift = labelHeight * 2.2;
    }
    addLabel({
      Text: '上游端' + (axis ? '  ' + axis : ''),
      X: corner ? corner.X : upstream.X,
      Y: corner ? corner.Y : upstream.Y,
      Z: corner ? corner.Z + lift : upstream.Z + lift
    }, 'label-direction', 'route', 'direction');
  }
  if (downstream) {
    const axis = pins.length > 1 ? pins[1].Text : '';
    const corner = axisCorner(envelope, axis);
    if (corner)
      addTube(corner, downstream, color.pin, state.diagonal * 0.00035);
    addLabel({
      Text: '设备端' + (axis ? '  ' + axis : ''),
      X: corner ? corner.X : downstream.X,
      Y: corner ? corner.Y : downstream.Y,
      Z: corner ? corner.Z + labelHeight * 2.2
        : downstream.Z + labelHeight * 1.6
    }, 'label-direction', 'route', 'direction');
  }
}

function collectLabels(envelope) {
  for (const label of envelope.SegmentLabels || [])
    addLineLabel(metricText(label.Text), label.X, label.Y, label.Z,
      label.DirectionX, label.DirectionY, label.DirectionZ,
      'label-mm', 'above');
  for (const label of envelope.RunLabels || [])
    addLineLabel(label.Text, label.X, label.Y, label.Z,
      label.DirectionX, label.DirectionY, label.DirectionZ,
      'label-run', 'below');
}

function addLabel(label, cssClass, group, kind) {
  if (!label || !label.Text) return;
  const element = document.createElement('div');
  element.className = 'scene-label ' + cssClass;
  element.textContent = label.Text;
  el.labelLayer.appendChild(element);
  const entry = {
    element,
    position: new THREE.Vector3(label.X, label.Y, label.Z),
    group: group || 'route',
    direction: null,
    side: 'above',
    kind: kind || 'route'
  };
  measureLabel(entry);
  state.labels.push(entry);
}

// 3.8.0.2：文字贴在线上——锚点沿线中点，按线段在屏幕上的方向排字，
// 固定在线旁（米数/软管在上侧，桥架/线管整趟在下侧）。
function addLineLabel(text, x, y, z, dx, dy, dz, cssClass, side) {
  if (!text) return;
  const element = document.createElement('div');
  element.className = 'scene-label ' + cssClass;
  element.textContent = text;
  el.labelLayer.appendChild(element);
  const direction = new THREE.Vector3(dx, dy, dz);
  const entry = {
    element,
    position: new THREE.Vector3(x, y, z),
    group: 'route',
    direction: direction.lengthSq() > 1e-12 ? direction.normalize() : null,
    side: side,
    kind: 'line'
  };
  measureLabel(entry);
  state.labels.push(entry);
}

// 标签只量一次（文字不变）：visibility:hidden 下取布局尺寸，再收起。
function measureLabel(entry) {
  const element = entry.element;
  element.style.visibility = 'hidden';
  element.style.display = '';
  entry.width = element.offsetWidth || Math.max(20, element.textContent.length * 8);
  entry.height = element.offsetHeight || 18;
  element.style.display = 'none';
  element.style.visibility = '';
}

// 「8000mm」→「8 m」（与 WPF 预览同口径；不是纯毫米数的文字原样返回）。
const MILLIMETRE_TEXT = /^\s*([0-9]+(?:\.[0-9]+)?)\s*mm\s*$/i;
function metricText(text) {
  const match = MILLIMETRE_TEXT.exec(text || '');
  if (!match) return text;
  const metres = Number(match[1]) / 1000;
  return (Math.round(metres * 100) / 100).toString() + ' m';
}

// ---- 相机 / 交互 -----------------------------------------------------------

function applyDefaultCamera(bounds) {
  const direction = new THREE.Vector3(-1, -1, 0.57735026919).normalize();
  // 3.8.0.6：按线路对角线 1.3 倍取景（线路铺满窗格，楼板自然延伸出画外）。
  const distance = bounds.diagonal * 1.3;
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
    if (label.group === 'grid')
      label.element.style.display = visible ? '' : 'none';
  }
  el.grid.textContent = visible ? '轴位 开' : '轴位 关';
  el.grid.style.display = state.hasGrid ? '' : 'none';
  el.reset.style.display = state.routeGroup ? '' : 'none';
}

function updateInfo(envelope) {
  const issues = envelope.RouteIssues || [];
  const shown = issues.slice(0, 3).map(text => '⚠ ' + text);
  if (issues.length > 3)
    shown.push('…另有 ' + (issues.length - 3) + ' 项，详见命令行输出');
  el.issues.textContent = shown.join('\n');
  const summaryParts = [];
  if (envelope.RecognitionSummary) summaryParts.push(envelope.RecognitionSummary);
  if (envelope.AxisSummary) summaryParts.push(envelope.AxisSummary);
  summaryParts.push('左键拖动=旋转 · 滚轮=缩放 · 右键拖动=平移 · 双击=复位');
  el.summary.textContent = summaryParts.join('　');
  let legend = '橙=电缆　红=桥架 青=线管';
  if (envelope.Hose) legend += '　绿管=软管(示意)';
  if ((envelope.Floors || []).length > 0) legend += '　灰框=楼层平面';
  if (envelope.DirectionResolved) legend += '　琥珀=上游端 绿=设备端';
  if (envelope.AnyTrayWidthAssumedDefault) legend += '　桥架宽未标注按 200 显示';
  el.legend.textContent = legend;
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
  const projected = new THREE.Vector3();
  const ahead = new THREE.Vector3();
  const entries = [];
  for (const label of state.labels) {
    projected.copy(label.position).project(state.camera);
    const onScreen = projected.z > -1 && projected.z < 1
      && projected.x > -1.08 && projected.x < 1.08
      && projected.y > -1.08 && projected.y < 1.08;
    if (!onScreen || (label.group === 'grid' && !state.showGrid)) {
      label.element.style.display = 'none';
      continue;
    }
    const x = (projected.x * 0.5 + 0.5) * width;
    const y = (-projected.y * 0.5 + 0.5) * height;
    let angle = 0;
    if (label.direction) {
      ahead.copy(label.position).add(label.direction).project(state.camera);
      const dx = (ahead.x - projected.x) * width;
      const dy = (-ahead.y + projected.y) * height;
      if (Math.hypot(dx, dy) > 0.5) {
        angle = Math.atan2(dy, dx);
        if (angle > Math.PI / 2) angle -= Math.PI;
        if (angle < -Math.PI / 2) angle += Math.PI;
      }
    }
    entries.push({ label, x, y, angle });
  }
  // 3.8.0.6：同屏避让——线路标签（米数/整趟/软管）优先，其次端点/柜/楼层，
  // 轴位边标最低（挤在一起就隐藏，不再堆成一坨）。
  entries.sort((left, right) =>
    labelPriority(left.label.kind) - labelPriority(right.label.kind));
  const placed = [];
  const lattice = [];
  for (const entry of entries) {
    if (entry.label.lattice) { lattice.push(entry); continue; }
    const label = entry.label;
    const candidates = [];
    if (label.kind === 'line') {
      const flip = label.side === 'below' ? 'above' : 'below';
      for (const side of [label.side, flip])
        for (const offset of [15, 27]) candidates.push({ side, offset });
    } else if (label.kind === 'axis') {
      candidates.push({ dy: 0 });
    } else {
      candidates.push({ dy: 0 }, { dy: -14 }, { dy: 14 }, { dy: -28 });
    }
    let chosen = null;
    for (const candidate of candidates) {
      const point = candidatePoint(entry, candidate);
      const box = boxAt(point.x, point.y, label, entry.angle);
      if (!placed.some(other => overlaps(box, other))) {
        chosen = { point, box };
        break;
      }
    }
    if (!chosen) {
      if (label.kind === 'axis') {
        label.element.style.display = 'none';
        continue;
      }
      // 线路/端点/柜/楼层：挤不下也照常显示在首选位置（信息优先）。
      const point = candidatePoint(entry, candidates[0]);
      chosen = { point, box: boxAt(point.x, point.y, label, entry.angle) };
    }
    placed.push(chosen.box);
    label.element.style.display = '';
    label.element.style.left = chosen.point.x + 'px';
    label.element.style.top = chosen.point.y + 'px';
    label.element.style.transform = entry.angle === 0
      ? 'translate(-50%, -50%)'
      : 'translate(-50%, -50%) rotate(' + entry.angle + 'rad)';
  }
  // 网格交点坐标最后铺：屏幕格子哈希去重（O(n)），已放牌子的格子不再放坐标，
  // 坐标牌之间也不互相压；被遮住的直接隐藏（缩放后会自然出现）。
  const CELL_W = 42;
  const CELL_H = 14;
  const occupied = new Set();
  const cellsOf = box => {
    const cells = [];
    const left = Math.floor((box.x - box.w / 2) / CELL_W);
    const right = Math.floor((box.x + box.w / 2) / CELL_W);
    const top = Math.floor((box.y - box.h / 2) / CELL_H);
    const bottom = Math.floor((box.y + box.h / 2) / CELL_H);
    for (let cx = left; cx <= right; cx++)
      for (let cy = top; cy <= bottom; cy++) cells.push(cx + ':' + cy);
    return cells;
  };
  for (const box of placed)
    for (const cell of cellsOf(box)) occupied.add(cell);
  for (const entry of lattice) {
    const label = entry.label;
    const box = boxAt(entry.x, entry.y, label, 0);
    const cells = cellsOf(box);
    if (cells.some(cell => occupied.has(cell))) {
      label.element.style.display = 'none';
      continue;
    }
    for (const cell of cells) occupied.add(cell);
    label.element.style.display = '';
    label.element.style.left = entry.x + 'px';
    label.element.style.top = entry.y + 'px';
    label.element.style.transform = 'translate(-50%, -50%)';
  }
}

function labelPriority(kind) {
  switch (kind) {
    case 'line': return 0;
    case 'direction': case 'pin': return 1;
    case 'cabinet': return 2;
    case 'floor': return 3;
    case 'axis': return 4;
    default: return 2;
  }
}

function candidatePoint(entry, candidate) {
  let x = entry.x, y = entry.y;
  if (entry.label.kind === 'line') {
    const sign = candidate.side === 'below' ? -1 : 1;
    const offset = candidate.offset * sign;
    x += Math.sin(entry.angle) * offset;
    y += -Math.cos(entry.angle) * offset;
  } else if (candidate.dy) {
    y += candidate.dy;
  }
  return { x, y };
}

function boxAt(x, y, label, angle) {
  const cosine = Math.abs(Math.cos(angle));
  const sine = Math.abs(Math.sin(angle));
  const w = label.width * cosine + label.height * sine;
  const h = label.width * sine + label.height * cosine;
  return { x, y, w, h };
}

function overlaps(left, right) {
  return Math.abs(left.x - right.x) < (left.w + right.w) / 2
    && Math.abs(left.y - right.y) < (left.h + right.h) / 2;
}

function animate() {
  requestAnimationFrame(animate);
  state.controls.update();
  state.renderer.render(state.scene, state.camera);
  updateLabels();
}

// ---- 工具 ------------------------------------------------------------------

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
  state.routeGroup.add(tube);
}

function vec(point) { return new THREE.Vector3(point.X, point.Y, point.Z); }

function post(message) {
  if (window.chrome && window.chrome.webview)
    window.chrome.webview.postMessage(message);
}

// 诊断钩子（只读）：真机「画面不对」时从宿主控制台/探针读相机与投影状态。
window.__u1uDebug = function () {
  const projected = new THREE.Vector3(0, 0, 0).project(state.camera);
  return {
    diag: state.diagonal,
    camera: state.camera.position.toArray(),
    target: state.controls.target.toArray(),
    up: state.camera.up.toArray(),
    aspect: state.camera.aspect,
    children: state.scene ? state.scene.children.length : -1,
    labels: state.labels.length,
    origin: [projected.x, projected.y, projected.z],
    render: state.renderer ? {
      calls: state.renderer.info.render.calls,
      triangles: state.renderer.info.render.triangles,
      lines: state.renderer.info.render.lines
    } : null,
    labelStyles: state.labels.slice(0, 4).map(l => ({
      text: l.element.textContent,
      display: l.element.style.display,
      left: l.element.style.left,
      top: l.element.style.top
    }))
  };
};

function fail(error) {
  el.loading.style.display = 'grid';
  el.loading.textContent = '三维渲染失败：'
    + (error && error.message ? error.message : error);
  post({ type: 'renderError', schemaVersion: 1, message: String(error) });
}
