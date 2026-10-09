import * as THREE from './vendor/three/three.module.min.js';
import { OrbitControls } from './vendor/three/OrbitControls.js';

const el = {
  viewport: document.getElementById('viewport'), canvasHost: document.getElementById('canvasHost'),
  loading: document.getElementById('loading'), status: document.getElementById('status'),
  machine: document.getElementById('machine'), device: document.getElementById('device'),
  deviceFloor: document.getElementById('deviceFloor'), panelFloor: document.getElementById('panelFloor'),
  projection: document.getElementById('projection'), frameCount: document.getElementById('frameCount'),
  downstream: document.getElementById('downstream'),
  upstream: document.getElementById('upstream'), spacing: document.getElementById('spacing'),
  axisMarks: document.getElementById('axisMarks'), segmentCount: document.getElementById('segmentCount'),
  routeLength: document.getElementById('routeLength'), routeMeters: document.getElementById('routeMeters'),
  routeList: document.getElementById('routeList'), bridgeSection: document.getElementById('bridgeSection'),
  bridgeCount: document.getElementById('bridgeCount'), bridgeLength: document.getElementById('bridgeLength'),
  bridgeList: document.getElementById('bridgeList'), frameSection: document.getElementById('frameSection'),
  frameList: document.getElementById('frameList'),
  diagnosticsSection: document.getElementById('diagnosticsSection'), diagnostics: document.getElementById('diagnostics'),
  routeLabels: document.getElementById('routeLabels'), fit: document.getElementById('fitButton'),
  grid: document.getElementById('gridButton'), labels: document.getElementById('labelsButton'), close: document.getElementById('closeButton')
};
const state = { renderer: null, scene: null, camera: null, controls: null, routeGroup: null, gridGroup: null, directionGroup: null, labelPoints: [], showGrid: true, showRouteLabels: true, viewHeight: 1000 };
const routeColor = 0x9bc2df;

boot();

function boot() {
  try {
    initRenderer(); bindUi(); animate();
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.addEventListener('message', event => receive(event.data));
      post({ type: 'ready', schemaVersion: 1 });
    } else setTimeout(() => receive(demo()), 80);
  } catch (error) { fail(error); }
}

function initRenderer() {
  state.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
  state.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  state.renderer.setClearColor(0x0d131a, 1);
  state.renderer.domElement.setAttribute('aria-label', 'U1L 三维预览画布');
  el.canvasHost.appendChild(state.renderer.domElement);
  state.scene = new THREE.Scene();
  state.camera = new THREE.OrthographicCamera(-500, 500, 500, -500, .1, 100000000);
  state.camera.up.set(0, 0, 1);
  state.camera.position.set(1.35, -1.55, 1.15).normalize().multiplyScalar(12000);
  state.controls = new OrbitControls(state.camera, state.renderer.domElement);
  state.controls.enableDamping = true; state.controls.dampingFactor = .08;
  state.controls.screenSpacePanning = true;
  state.scene.add(new THREE.AxesHelper(1200));
  new ResizeObserver(resize).observe(el.viewport); resize();
}

function bindUi() {
  el.fit.addEventListener('click', fitView);
  el.grid.addEventListener('click', () => { state.showGrid = !state.showGrid; if (state.gridGroup) state.gridGroup.visible = state.showGrid; });
  el.labels.addEventListener('click', () => {
    state.showRouteLabels = !state.showRouteLabels;
    el.labels.textContent = state.showRouteLabels ? '隐藏路线标签' : '显示路线标签';
    updateLabels();
  });
  el.close.addEventListener('click', () => post({ type: 'close', schemaVersion: 1 }));
  window.addEventListener('resize', resize);
}

function receive(raw) {
  try {
    const message = typeof raw === 'string' ? JSON.parse(raw) : raw;
    if (!message || message.type !== 'initialize') return;
    renderEnvelope(message); el.loading.style.display = 'none';
  } catch (error) { fail(error); }
}

function renderEnvelope(envelope) {
  const data = envelope.scene || {}, meta = envelope.metadata || {}, grid = envelope.axisGrid || {};
  const batch = envelope.mode === 'batch';
  state.showRouteLabels = !batch || (data.segments || []).length <= 24;
  el.labels.textContent = state.showRouteLabels ? '隐藏路线标签' : '显示路线标签';
  el.machine.textContent = text(meta.machineId || (batch ? '多图框' : ''));
  el.device.textContent = text(meta.deviceName || (batch ? '统一轴位预览' : ''));
  el.frameCount.textContent = batch ? text(meta.frameCount) : '1';
  el.deviceFloor.textContent = text(meta.deviceFloor); el.panelFloor.textContent = text(meta.panelFloor);
  el.projection.textContent = text(data.projectionMode);
  el.downstream.textContent = batch ? '见场景端点标记' : text(grid.downstreamAxis);
  el.upstream.textContent = batch ? '见场景端点标记' : text(grid.upstreamAxis);
  el.spacing.textContent = `相邻轴位 ${format(grid.spacingMillimetres || 4800)} mm`;
  renderAxisMarks(grid); renderRouteList(data.segments || [], batch);
  renderBridges(envelope.bridges || []);
  renderFrames(envelope.frames || []);
  renderDiagnostics((data.diagnostics || []).concat(grid.diagnostics || [], envelope.diagnostics || []));
  for (const item of document.querySelectorAll('.meta dd, .axis-card strong, .route-summary strong, .route-row > span, .frame-row > *')) {
    item.title = item.textContent;
  }
  buildGeometry(data, grid); fitView(); el.status.textContent = batch
    ? '已载入多图框只读预览' : '已载入只读预览';
}

function buildGeometry(data, grid) {
  if (state.routeGroup) state.scene.remove(state.routeGroup);
  if (state.gridGroup) state.scene.remove(state.gridGroup);
  if (state.directionGroup) state.scene.remove(state.directionGroup);
  state.routeGroup = new THREE.Group(); state.gridGroup = new THREE.Group();
  state.directionGroup = new THREE.Group(); state.labelPoints = [];
  const nodes = new Map((data.nodes || []).map(node => [node.id,
    new THREE.Vector3(node.position.x, node.position.y, node.position.z)]));
  const routeGroups = new Map();
  for (const segment of data.segments || []) {
    const start = nodes.get(segment.startNodeId), end = nodes.get(segment.endNodeId);
    if (!start || !end) continue;
    const geometry = new THREE.BufferGeometry().setFromPoints([start, end]);
    state.routeGroup.add(new THREE.Line(geometry, new THREE.LineBasicMaterial({
      color: routeColor
    })));
    const key = String(segment.routeGroupId || segment.id);
    let group = routeGroups.get(key);
    if (!group) {
      group = {
        routeGroupId: key, frameLabel: segment.frameLabel,
        circuitName: segment.circuitName, distanceMm: 0,
        segmentCount: 0, midpoint: new THREE.Vector3()
      };
      routeGroups.set(key, group);
    }
    group.distanceMm += Number(segment.distanceMm) || 0;
    group.segmentCount++;
    group.midpoint.add(start.clone().add(end).multiplyScalar(.5));
  }
  for (const group of routeGroups.values()) {
    if (group.segmentCount > 0)
      group.midpoint.multiplyScalar(1 / group.segmentCount);
    state.labelPoints.push({ point: group.midpoint,
      text: routeLabel(group), kind: 'route' });
  }
  for (const point of nodes.values()) {
    const mesh = new THREE.Mesh(new THREE.SphereGeometry(38, 12, 8),
      new THREE.MeshBasicMaterial({ color: 0xe7eef7 }));
    mesh.position.copy(point); state.routeGroup.add(mesh);
  }
  buildDirectionMarkers(data.directionMarkers || []);
  buildGrid(grid, nodes, data.directionMarkers || []);
  state.scene.add(state.gridGroup); state.scene.add(state.directionGroup);
  state.scene.add(state.routeGroup);
  el.routeLabels.innerHTML = '';
  for (const item of state.labelPoints) {
    const label = document.createElement('div');
    label.className = item.className || (item.axis ? 'route-label axis-label' : 'route-label');
    label.textContent = item.text;
    label.title = item.title || item.text;
    el.routeLabels.appendChild(label); item.element = label;
  }
}

function buildDirectionMarkers(markers) {
  for (const marker of markers || []) {
    const point = new THREE.Vector3(marker.position?.x || 0,
      marker.position?.y || 0, marker.position?.z || 0);
    const isDownstream = marker.kind === 'downstream';
    const color = isDownstream ? 0x63c9a5 : 0xe0b064;
    const geometry = isDownstream
      ? new THREE.SphereGeometry(125, 16, 10)
      : new THREE.BoxGeometry(220, 220, 220);
    const mesh = new THREE.Mesh(geometry,
      new THREE.MeshBasicMaterial({ color }));
    mesh.position.copy(point); state.directionGroup.add(mesh);
    const owner = marker.frameLabel
      ? ` · ${text(marker.frameLabel)}${marker.circuitName ? ` / ${text(marker.circuitName)}` : ''}`
      : '';
    state.labelPoints.push({
      point,
      text: `${text(marker.title)} ${text(marker.axis)}`,
      title: `${text(marker.title)} ${text(marker.axis)}${owner}`,
      kind: 'direction',
      className: `route-label direction-label ${isDownstream ? 'downstream-label' : 'upstream-label'}`
    });
  }
}

function buildGrid(grid, nodes, markers) {
  const numeric = grid.numericMarks || [], letters = grid.alphabeticMarks || [];
  const spacing = grid.spacingMillimetres || 4800;
  let extent = 9600;
  for (const point of nodes.values()) extent = Math.max(extent, Math.abs(point.x), Math.abs(point.y));
  for (const mark of numeric.concat(letters)) extent = Math.max(extent, Math.abs(mark.offsetMillimetres));
  for (const marker of markers || []) {
    extent = Math.max(extent, Math.abs(marker.position?.x || 0),
      Math.abs(marker.position?.y || 0));
  }
  const material = new THREE.LineBasicMaterial({ color: 0x41505f, transparent: true, opacity: .58 });
  for (const mark of numeric) state.gridGroup.add(gridLine(
    new THREE.Vector3(mark.offsetMillimetres, -extent, 0),
    new THREE.Vector3(mark.offsetMillimetres, extent, 0), material));
  for (const mark of numeric) state.labelPoints.push({
    point: new THREE.Vector3(mark.offsetMillimetres, -extent, 0),
    text: mark.label, axis: true, kind: 'axis', className: 'route-label axis-label'
  });
  for (const mark of letters) state.gridGroup.add(gridLine(
    new THREE.Vector3(-extent, mark.offsetMillimetres, 0),
    new THREE.Vector3(extent, mark.offsetMillimetres, 0), material));
  for (const mark of letters) state.labelPoints.push({
    point: new THREE.Vector3(extent, mark.offsetMillimetres, 0),
    text: mark.label, axis: true, kind: 'axis', className: 'route-label axis-label'
  });
  state.labelPoints.push(
    { point: new THREE.Vector3(extent, 0, 0), text: '东 →', kind: 'axis', className: 'route-label axis-label' },
    { point: new THREE.Vector3(-extent, 0, 0), text: '← 西', kind: 'axis', className: 'route-label axis-label' },
    { point: new THREE.Vector3(0, extent, 0), text: '北 ↑', kind: 'axis', className: 'route-label axis-label' },
    { point: new THREE.Vector3(0, -extent, 0), text: '南 ↓', kind: 'axis', className: 'route-label axis-label' }
  );
  const outer = new THREE.GridHelper(extent * 2, Math.max(2, Math.round(extent * 2 / spacing)), 0x344352, 0x202b36);
  outer.rotation.x = Math.PI / 2; outer.material.transparent = true; outer.material.opacity = .18;
  state.gridGroup.add(outer);
}

function gridLine(a, b, material) { return new THREE.Line(new THREE.BufferGeometry().setFromPoints([a, b]), material); }
function renderAxisMarks(grid) {
  el.axisMarks.innerHTML = '';
  const lines = [
    `东西数字轴（向东 +1）：${(grid.numericMarks || []).map(item => `${item.label} (${format(item.offsetMillimetres)}mm)`).join(' · ')}`,
    `南北字母轴（向北 +1）：${(grid.alphabeticMarks || []).map(item => `${item.label} (${format(item.offsetMillimetres)}mm)`).join(' · ')}`
  ];
  for (const line of lines) { const div = document.createElement('div'); div.textContent = line; el.axisMarks.appendChild(div); }
}
function renderRouteList(segments, batch) {
  let length = 0; el.routeList.innerHTML = '';
  const groups = new Map();
  for (const segment of segments) {
    const lengthMm = Number(segment.distanceMm) || 0;
    length += lengthMm;
    const key = String(segment.routeGroupId || segment.id);
    let group = groups.get(key);
    if (!group) {
      group = {
        routeGroupId: key, frameLabel: segment.frameLabel,
        circuitName: segment.circuitName, distanceMm: 0, segmentCount: 0
      };
      groups.set(key, group);
    }
    group.distanceMm += lengthMm;
    group.segmentCount++;
  }
  for (const group of groups.values()) {
    const row = document.createElement('div'); row.className = 'route-row';
    const id = document.createElement('span'); id.className = 'route-identity';
    const circuit = text(group.circuitName);
    const owner = batch ? `${text(group.frameLabel)} · ` : '';
    id.textContent = `${owner}${circuit === '-' ? '未识别回路' : circuit} · `
      + `${routeName(group.routeGroupId)} · ${group.segmentCount}段`;
    const distance = document.createElement('span'); distance.className = 'distance';
    distance.textContent = `${format(group.distanceMm)} mm`;
    row.append(id, distance); el.routeList.appendChild(row);
  }
  el.segmentCount.textContent = String(segments.length);
  el.routeLength.textContent = format(length);
  el.routeMeters.textContent = format(length / 1000);
}
function renderBridges(bridges) {
  let length = 0; el.bridgeList.innerHTML = '';
  for (const bridge of bridges || []) {
    const value = Number(bridge.lengthM) || Number(bridge.lengthMm || 0) / 1000;
    length += value;
    const row = document.createElement('div'); row.className = 'route-row';
    const spec = document.createElement('span'); spec.className = 'route-identity';
    spec.textContent = `${text(bridge.spec)} · ${bridge.associated ? '已关联' : '未关联'}`;
    const distance = document.createElement('span'); distance.className = 'distance';
    distance.textContent = `${format(value)} m`;
    row.append(spec, distance); el.bridgeList.appendChild(row);
  }
  el.bridgeCount.textContent = String((bridges || []).length);
  el.bridgeLength.textContent = format(length);
  el.bridgeSection.hidden = !(bridges || []).length;
}
function renderFrames(frames) {
  el.frameList.innerHTML = '';
  el.frameSection.hidden = !(frames || []).length;
  for (const frame of frames || []) {
    const row = document.createElement('div'); row.className = 'frame-row';
    const title = document.createElement('strong');
    title.textContent = text(frame.machineId || frame.id);
    const circuit = document.createElement('span');
    circuit.textContent = `回路：${text(frame.deviceName)}`;
    const axis = document.createElement('span');
    axis.textContent = `${text(frame.downstreamAxis)} → ${text(frame.upstreamAxis)}`;
    const offset = document.createElement('span');
    offset.textContent = `(${format(frame.offsetXMillimetres)}, ${format(frame.offsetYMillimetres)}) mm`;
    row.append(title, circuit, axis, offset); el.frameList.appendChild(row);
  }
}
function renderDiagnostics(values) {
  const unique = [...new Set((values || []).filter(Boolean))];
  el.diagnosticsSection.hidden = unique.length === 0; el.diagnostics.innerHTML = '';
  for (const value of unique) { const li = document.createElement('li'); li.textContent = value; el.diagnostics.appendChild(li); }
}

function fitView() {
  if (!state.routeGroup && !state.directionGroup) return;
  const box = new THREE.Box3();
  if (state.routeGroup) box.expandByObject(state.routeGroup);
  if (state.directionGroup) box.expandByObject(state.directionGroup);
  if (box.isEmpty()) return;
  const center = box.getCenter(new THREE.Vector3()), size = box.getSize(new THREE.Vector3());
  const extent = Math.max(size.x, size.y, size.z, 1000); state.viewHeight = extent * 1.55;
  const direction = new THREE.Vector3(1.35, -1.55, 1.15).normalize();
  state.camera.position.copy(center).addScaledVector(direction, extent * 2.5);
  state.camera.near = Math.max(.1, extent * .001); state.camera.far = Math.max(100000, extent * 20);
  state.camera.zoom = 1; state.controls.target.copy(center); state.controls.update(); resize();
}
function resize() {
  if (!state.renderer || !state.camera) return;
  const width = Math.max(1, el.viewport.clientWidth), height = Math.max(1, el.viewport.clientHeight), aspect = width / height;
  state.renderer.setSize(width, height, false); state.camera.left = -state.viewHeight * aspect / 2;
  state.camera.right = state.viewHeight * aspect / 2; state.camera.top = state.viewHeight / 2;
  state.camera.bottom = -state.viewHeight / 2; state.camera.updateProjectionMatrix();
}
function animate() {
  requestAnimationFrame(animate); if (state.controls) state.controls.update();
  if (state.renderer && state.scene && state.camera) state.renderer.render(state.scene, state.camera);
  updateLabels();
}
function updateLabels() {
  if (!state.camera) return;
  const rect = el.viewport.getBoundingClientRect();
  const occupied = [];
  const priority = { direction: 0, axis: 1, route: 2 };
  const items = state.labelPoints.slice().sort((left, right) =>
    (priority[left.kind] ?? 2) - (priority[right.kind] ?? 2));
  for (const item of items) {
    const point = item.point.clone().project(state.camera);
    const x = (point.x * .5 + .5) * rect.width;
    const y = (-point.y * .5 + .5) * rect.height;
    item.element.style.left = `${x}px`;
    item.element.style.top = `${y}px`;
    if (item.kind === 'route' && !state.showRouteLabels) {
      item.element.style.display = 'none';
      continue;
    }
    item.element.style.display = '';
    const width = item.element.offsetWidth || 40;
    const height = item.element.offsetHeight || 16;
    const offsets = item.kind === 'route' ? [0] : [0, -20, 20, -40, 40];
    let placed = false;
    for (const offsetY of offsets) {
      const box = {
        left: x - width / 2 - 3,
        right: x + width / 2 + 3,
        top: y + offsetY - height / 2 - 3,
        bottom: y + offsetY + height / 2 + 3
      };
      const overlaps = occupied.some(other => !(box.right < other.left
        || box.left > other.right || box.bottom < other.top
        || box.top > other.bottom));
      if (overlaps) continue;
      item.element.style.transform =
        `translate(-50%, calc(-50% + ${offsetY}px))`;
      occupied.push(box);
      placed = true;
      break;
    }
    if (!placed && item.kind === 'route') item.element.style.display = 'none';
  }
}
function post(message) { if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(message); }
function fail(error) {
  const message = error && error.message ? error.message : String(error);
  el.loading.textContent = `三维视图不可用：${message}`; el.status.textContent = '预览失败';
  post({ type: 'renderError', schemaVersion: 1, message });
}
function text(value) { return value == null || String(value).trim() === '' ? '-' : String(value); }
function format(value) { const number = Number(value); return Number.isFinite(number) ? Number(number.toFixed(2)).toString() : '0'; }
function routeLabel(segment) {
  const circuit = text(segment.circuitName);
  const frame = compact(segment.frameLabel);
  const id = routeName(segment.routeGroupId || segment.id);
  const owner = frame === '-' ? '' : `${frame} · `;
  return `${owner}${circuit === '-' ? '未识别回路' : compact(circuit)} · `
    + `${id} ${format(segment.distanceMm)}mm · ${segment.segmentCount}段`;
}

function routeName(value) {
  const id = String(value || '').replace(/^F\d+_/, '');
  return id ? `路由${id}` : '路由';
}
function compact(value) {
  const normalized = text(value);
  return normalized.length > 18 ? `${normalized.slice(0, 17)}…` : normalized;
}
function demo() {
  return { type: 'initialize', metadata: { machineId: 'DEMO', deviceName: '预览示例' },
    scene: { projectionMode: 'Isometric', nodes: [
      { id: 'N1', position: { x: 0, y: 0, z: 0 } }, { id: 'N2', position: { x: 4800, y: 0, z: 0 } },
      { id: 'N3', position: { x: 4800, y: 4800, z: 2400 } }], segments: [
      { id: 'demo-1', startNodeId: 'N1', endNodeId: 'N2', axis: 'X', distanceMm: 4800 },
      { id: 'demo-2', circuitName: '预览示例', startNodeId: 'N2', endNodeId: 'N3', axis: 'Y', distanceMm: 4800 }],
      directionMarkers: [
        { kind: 'downstream', title: '下游', axis: '54/W', position: { x: 0, y: 0, z: 0 } },
        { kind: 'upstream', title: '上游', axis: '54/X', position: { x: 0, y: 4800, z: 0 } }
      ], diagnostics: [] },
    axisGrid: { upstreamAxis: '54/X', downstreamAxis: '54/W', spacingMillimetres: 4800,
      numericMarks: [{ label: '54', offsetMillimetres: 0 }],
      alphabeticMarks: [{ label: 'W', offsetMillimetres: 0 }, { label: 'X', offsetMillimetres: 4800 }] } };
}
