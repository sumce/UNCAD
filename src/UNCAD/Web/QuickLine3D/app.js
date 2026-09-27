import * as THREE from './vendor/three/three.module.min.js';
import { OrbitControls } from './vendor/three/OrbitControls.js';

const el = {
  viewport: document.getElementById('viewport'), canvasHost: document.getElementById('canvasHost'),
  loading: document.getElementById('loading'), status: document.getElementById('status'),
  machine: document.getElementById('machine'), device: document.getElementById('device'),
  deviceFloor: document.getElementById('deviceFloor'), panelFloor: document.getElementById('panelFloor'),
  projection: document.getElementById('projection'), downstream: document.getElementById('downstream'),
  upstream: document.getElementById('upstream'), spacing: document.getElementById('spacing'),
  axisMarks: document.getElementById('axisMarks'), segmentCount: document.getElementById('segmentCount'),
  routeLength: document.getElementById('routeLength'), routeList: document.getElementById('routeList'),
  diagnosticsSection: document.getElementById('diagnosticsSection'), diagnostics: document.getElementById('diagnostics'),
  routeLabels: document.getElementById('routeLabels'), fit: document.getElementById('fitButton'),
  grid: document.getElementById('gridButton'), labels: document.getElementById('labelsButton'), close: document.getElementById('closeButton')
};
const state = { renderer: null, scene: null, camera: null, controls: null, routeGroup: null, gridGroup: null, labelPoints: [], showGrid: true, showLabels: true, viewHeight: 1000 };
const colors = { X: 0xf1847f, Y: 0x7ed49c, Z: 0x79b8ff };

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
  el.labels.addEventListener('click', () => { state.showLabels = !state.showLabels; el.routeLabels.style.display = state.showLabels ? '' : 'none'; });
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
  el.machine.textContent = text(meta.machineId); el.device.textContent = text(meta.deviceName);
  el.deviceFloor.textContent = text(meta.deviceFloor); el.panelFloor.textContent = text(meta.panelFloor);
  el.projection.textContent = text(data.projectionMode); el.downstream.textContent = text(grid.downstreamAxis);
  el.upstream.textContent = text(grid.upstreamAxis); el.spacing.textContent = `相邻轴位 ${format(grid.spacingMillimetres || 4800)} mm`;
  renderAxisMarks(grid); renderRouteList(data.segments || []);
  renderDiagnostics((data.diagnostics || []).concat(grid.diagnostics || [], envelope.diagnostics || []));
  buildGeometry(data, grid); fitView(); el.status.textContent = '已载入只读预览';
}

function buildGeometry(data, grid) {
  if (state.routeGroup) state.scene.remove(state.routeGroup);
  if (state.gridGroup) state.scene.remove(state.gridGroup);
  state.routeGroup = new THREE.Group(); state.gridGroup = new THREE.Group(); state.labelPoints = [];
  const nodes = new Map((data.nodes || []).map(node => [node.id,
    new THREE.Vector3(node.position.x, node.position.y, node.position.z)]));
  for (const segment of data.segments || []) {
    const start = nodes.get(segment.startNodeId), end = nodes.get(segment.endNodeId);
    if (!start || !end) continue;
    const geometry = new THREE.BufferGeometry().setFromPoints([start, end]);
    state.routeGroup.add(new THREE.Line(geometry, new THREE.LineBasicMaterial({
      color: colors[segment.axis] || 0xdbe5ef
    })));
    state.labelPoints.push({ point: start.clone().add(end).multiplyScalar(.5),
      text: `${segment.axis}  ${format(segment.distanceMm)}mm` });
  }
  for (const point of nodes.values()) {
    const mesh = new THREE.Mesh(new THREE.SphereGeometry(38, 12, 8),
      new THREE.MeshBasicMaterial({ color: 0xe7eef7 }));
    mesh.position.copy(point); state.routeGroup.add(mesh);
  }
  buildGrid(grid, nodes); state.scene.add(state.gridGroup); state.scene.add(state.routeGroup);
  el.routeLabels.innerHTML = '';
  for (const item of state.labelPoints) {
    const label = document.createElement('div');
    label.className = item.axis ? 'route-label axis-label' : 'route-label';
    label.textContent = item.text; el.routeLabels.appendChild(label); item.element = label;
  }
}

function buildGrid(grid, nodes) {
  const numeric = grid.numericMarks || [], letters = grid.alphabeticMarks || [];
  const spacing = grid.spacingMillimetres || 4800;
  let extent = 9600;
  for (const point of nodes.values()) extent = Math.max(extent, Math.abs(point.x), Math.abs(point.y));
  for (const mark of numeric.concat(letters)) extent = Math.max(extent, Math.abs(mark.offsetMillimetres));
  const material = new THREE.LineBasicMaterial({ color: 0x41505f, transparent: true, opacity: .58 });
  for (const mark of numeric) state.gridGroup.add(gridLine(
    new THREE.Vector3(mark.offsetMillimetres, -extent, 0),
    new THREE.Vector3(mark.offsetMillimetres, extent, 0), material));
  for (const mark of numeric) state.labelPoints.push({
    point: new THREE.Vector3(mark.offsetMillimetres, -extent, 0),
    text: mark.label, axis: true
  });
  for (const mark of letters) state.gridGroup.add(gridLine(
    new THREE.Vector3(-extent, mark.offsetMillimetres, 0),
    new THREE.Vector3(extent, mark.offsetMillimetres, 0), material));
  for (const mark of letters) state.labelPoints.push({
    point: new THREE.Vector3(extent, mark.offsetMillimetres, 0),
    text: mark.label, axis: true
  });
  const outer = new THREE.GridHelper(extent * 2, Math.max(2, Math.round(extent * 2 / spacing)), 0x344352, 0x202b36);
  outer.rotation.x = Math.PI / 2; outer.material.transparent = true; outer.material.opacity = .18;
  state.gridGroup.add(outer);
}

function gridLine(a, b, material) { return new THREE.Line(new THREE.BufferGeometry().setFromPoints([a, b]), material); }
function renderAxisMarks(grid) {
  el.axisMarks.innerHTML = '';
  const lines = [`数字轴: ${(grid.numericMarks || []).map(item => `${item.label} (${format(item.offsetMillimetres)}mm)`).join(' · ')}`,
    `字母轴: ${(grid.alphabeticMarks || []).map(item => `${item.label} (${format(item.offsetMillimetres)}mm)`).join(' · ')}`];
  for (const line of lines) { const div = document.createElement('div'); div.textContent = line; el.axisMarks.appendChild(div); }
}
function renderRouteList(segments) {
  let length = 0; el.routeList.innerHTML = '';
  for (const segment of segments) {
    length += Number(segment.distanceMm) || 0;
    const row = document.createElement('div'); row.className = 'route-row';
    const id = document.createElement('span'); id.textContent = segment.id;
    const axis = document.createElement('span'); axis.className = 'axis'; axis.textContent = segment.axis;
    const distance = document.createElement('span'); distance.className = 'distance'; distance.textContent = `${format(segment.distanceMm)} mm`;
    row.append(id, axis, distance); el.routeList.appendChild(row);
  }
  el.segmentCount.textContent = String(segments.length); el.routeLength.textContent = format(length);
}
function renderDiagnostics(values) {
  const unique = [...new Set((values || []).filter(Boolean))];
  el.diagnosticsSection.hidden = unique.length === 0; el.diagnostics.innerHTML = '';
  for (const value of unique) { const li = document.createElement('li'); li.textContent = value; el.diagnostics.appendChild(li); }
}

function fitView() {
  if (!state.routeGroup) return;
  const box = new THREE.Box3().setFromObject(state.routeGroup); if (box.isEmpty()) return;
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
  if (!state.showLabels || !state.camera) return;
  const rect = el.viewport.getBoundingClientRect();
  for (const item of state.labelPoints) {
    const point = item.point.clone().project(state.camera);
    item.element.style.left = `${(point.x * .5 + .5) * rect.width}px`;
    item.element.style.top = `${(-point.y * .5 + .5) * rect.height}px`;
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
function demo() {
  return { type: 'initialize', metadata: { machineId: 'DEMO', deviceName: '预览示例' },
    scene: { projectionMode: 'Isometric', nodes: [
      { id: 'N1', position: { x: 0, y: 0, z: 0 } }, { id: 'N2', position: { x: 4800, y: 0, z: 0 } },
      { id: 'N3', position: { x: 4800, y: 4800, z: 2400 } }], segments: [
      { id: 'demo-1', startNodeId: 'N1', endNodeId: 'N2', axis: 'X', distanceMm: 4800 },
      { id: 'demo-2', startNodeId: 'N2', endNodeId: 'N3', axis: 'Y', distanceMm: 4800 }], diagnostics: [] },
    axisGrid: { upstreamAxis: '54/X', downstreamAxis: '54/W', spacingMillimetres: 4800,
      numericMarks: [{ label: '54', offsetMillimetres: 0 }],
      alphabeticMarks: [{ label: 'W', offsetMillimetres: 0 }, { label: 'X', offsetMillimetres: 4800 }] } };
}
