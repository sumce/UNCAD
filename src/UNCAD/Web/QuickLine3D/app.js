import * as THREE from './vendor/three/three.module.min.js';
import { OrbitControls } from './vendor/three/OrbitControls.js';

const elements = {
  viewport: document.getElementById('viewport'),
  canvasHost: document.getElementById('canvasHost'),
  labelsLayer: document.getElementById('labelsLayer'),
  selectionBox: document.getElementById('selectionBox'),
  loading: document.getElementById('loadingState'),
  orbitMode: document.getElementById('orbitMode'),
  selectMode: document.getElementById('selectMode'),
  fitView: document.getElementById('fitView'),
  orthographicPlane: document.getElementById('orthographicPlane'),
  labelsToggle: document.getElementById('labelsToggle'),
  swapAxes: document.getElementById('swapAxes'),
  flipZ: document.getElementById('flipZ'),
  segmentCount: document.getElementById('segmentCount'),
  selectionCount: document.getElementById('selectionCount'),
  selectedMetric: document.getElementById('selectedMetric'),
  axisMetric: document.getElementById('axisMetric'),
  angleMetric: document.getElementById('angleMetric'),
  distanceMetric: document.getElementById('distanceMetric'),
  quickInputDisplay: document.getElementById('quickInputDisplay'),
  quickInputHint: document.getElementById('quickInputHint'),
  validation: document.getElementById('validationMessage'),
  clearSelection: document.getElementById('clearSelection'),
  rows: document.getElementById('segmentRows'),
  changedCount: document.getElementById('changedCount'),
  diagnosticSection: document.getElementById('diagnosticSection'),
  diagnosticList: document.getElementById('diagnosticList'),
  status: document.getElementById('statusText'),
  cancel: document.getElementById('cancelButton'),
  commit: document.getElementById('commitButton'),
  toast: document.getElementById('toast')
};

const state = {
  mode: 'orbit',
  projectionMode: 'Isometric',
  sessionId: '',
  revision: 0,
  rootNodeId: '',
  segments: [],
  diagnostics: [],
  selected: new Set(),
  changed: new Set(),
  quickVisited: new Set(),
  quickPlan: [],
  quickCursor: -1,
  quickIndex: -1,
  inputBuffer: '',
  adjacency: new Map(),
  labels: [],
  nodePositions: new Map(),
  pointerStart: null,
  pointerCurrent: null,
  viewHeight: 1000,
  initialized: false
};

let renderer;
let scene;
let camera;
let controls;
let routeLines;
let jointPoints;
let resizeObserver;
let toastTimer;

const axisColors = {
  X: new THREE.Color('#b24e47'),
  Y: new THREE.Color('#32805a'),
  Z: new THREE.Color('#3478a6')
};
const selectedColor = new THREE.Color('#00a7c7');

boot();

function boot() {
  try {
    initializeThree();
    bindUi();
    animate();
    if (hasWebViewHost()) {
      window.chrome.webview.addEventListener('message', event => receiveMessage(event.data));
      postHost({ type: 'ready', schemaVersion: 1 });
    } else {
      window.setTimeout(() => initializeScene(demoEnvelope()), 80);
    }
  } catch (error) {
    fail(String(error && error.message ? error.message : error));
  }
}

function initializeThree() {
  renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
  renderer.setClearColor(0xedf1f4, 1);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.domElement.tabIndex = 0;
  renderer.domElement.setAttribute('aria-label', '3D 线段画布');
  elements.canvasHost.appendChild(renderer.domElement);

  scene = new THREE.Scene();
  scene.background = new THREE.Color(0xedf1f4);
  camera = new THREE.OrthographicCamera(-500, 500, 500, -500, 0.1, 10000000);
  camera.up.set(0, 0, 1);
  camera.position.set(1600, -1800, 1400);

  controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.dampingFactor = 0.08;
  controls.screenSpacePanning = true;
  controls.zoomToCursor = true;

  const grid = new THREE.GridHelper(100000, 40, 0xaeb8c1, 0xd5dce2);
  grid.rotation.x = Math.PI / 2;
  grid.material.opacity = 0.36;
  grid.material.transparent = true;
  scene.add(grid);
  scene.add(new THREE.AxesHelper(1200));

  resizeObserver = new ResizeObserver(resizeRenderer);
  resizeObserver.observe(elements.viewport);
  resizeRenderer();
}

function bindUi() {
  elements.orbitMode.addEventListener('click', () => setMode('orbit'));
  elements.selectMode.addEventListener('click', () => setMode('select'));
  elements.fitView.addEventListener('click', fitView);
  elements.orthographicPlane.addEventListener('change', remapAxes);
  elements.labelsToggle.addEventListener('change', updateLabels);
  elements.swapAxes.addEventListener('change', remapAxes);
  elements.flipZ.addEventListener('change', remapAxes);
  elements.clearSelection.addEventListener('click', () => {
    cancelQuickEdit();
    setSelection([]);
  });
  elements.cancel.addEventListener('click', cancelEditor);
  elements.commit.addEventListener('click', commitEditor);

  const canvas = renderer.domElement;
  canvas.addEventListener('pointerdown', beginSelection);
  canvas.addEventListener('pointermove', moveSelection);
  canvas.addEventListener('pointerup', finishSelection);
  canvas.addEventListener('pointercancel', cancelSelectionBox);
  canvas.addEventListener('contextmenu', event => event.preventDefault());

  window.addEventListener('keydown', event => {
    if (handleQuickInputKey(event)) return;
    if (event.key === 'Escape') {
      cancelQuickEdit();
      setSelection([]);
    }
    if (event.ctrlKey && event.key.toLowerCase() === 'a') {
      event.preventDefault();
      setSelection(state.segments.map((_, index) => index));
    }
  });
}

function receiveMessage(message) {
  if (typeof message === 'string') {
    try { message = JSON.parse(message); }
    catch { return; }
  }
  if (!message || message.type !== 'initialize') return;
  initializeScene(message);
}

function initializeScene(envelope) {
  const payload = envelope.scene || envelope.payload || envelope;
  const rawSegments = payload.segments || payload.Segments || [];
  state.sessionId = envelope.sessionId || payload.sessionId || '';
  state.revision = Number(envelope.revision || payload.revision || 0);
  state.rootNodeId = payload.rootNodeId || payload.RootNodeId || 'N1';
  state.projectionMode = normalizeProjectionMode(
    payload.projectionMode || payload.ProjectionMode || 'Isometric');
  state.diagnostics = payload.diagnostics || payload.Diagnostics || [];
  state.segments = rawSegments.map(normalizeSegment);
  state.selected.clear();
  state.changed.clear();
  state.quickVisited.clear();
  state.quickPlan = [];
  state.quickCursor = -1;
  state.quickIndex = -1;
  state.inputBuffer = '';
  state.adjacency = buildAdjacency();
  state.initialized = true;
  elements.loading.style.display = 'none';
  elements.segmentCount.textContent = `${state.segments.length} 段`;
  elements.status.textContent = `已载入 ${state.segments.length} 根相连线段`;
  elements.commit.disabled = false;
  syncProjectionControls();
  rebuildRoute();
  rebuildTable();
  rebuildDiagnostics();
  updateSelectionUi();
  fitView();
}

function normalizeProjectionMode(value) {
  const normalized = String(value || '').toLowerCase();
  return normalized === 'orthographic' ? 'Orthographic' : 'Isometric';
}

function syncProjectionControls() {
  const orthographic = state.projectionMode === 'Orthographic';
  elements.orthographicPlane.disabled = !orthographic;
  elements.swapAxes.disabled = orthographic;
  elements.orthographicPlane.title = orthographic
    ? '选择正交草图的两个实际空间轴'
    : '当前为等轴测模式，正交平面设置不适用';
}

function normalizeSegment(source, index) {
  const axisValue = source.axis ?? source.Axis ?? 'X';
  return {
    id: String(source.id ?? source.Id ?? `S${index + 1}`),
    startNodeId: String(source.startNodeId ?? source.StartNodeId ?? `N${index + 1}`),
    endNodeId: String(source.endNodeId ?? source.EndNodeId ?? `N${index + 2}`),
    axis: typeof axisValue === 'number' ? ['?', 'X', 'Y', 'Z'][axisValue] : String(axisValue).toUpperCase(),
    directionSign: Number(source.directionSign ?? source.DirectionSign ?? 1) < 0 ? -1 : 1,
    planAngleDegrees: Number(source.planAngleDegrees ?? source.PlanAngleDegrees ?? 0),
    distanceMm: Number(source.distanceMm ?? source.DistanceMillimetres ?? source.DistanceMm ?? 0),
    completed: Boolean(source.completed ?? source.Completed ?? false),
    start: new THREE.Vector3(),
    end: new THREE.Vector3()
  };
}

function recomputeTopology() {
  const positions = new Map();
  positions.set(state.rootNodeId, new THREE.Vector3(0, 0, 0));
  let changed = true;
  let passes = 0;
  while (changed && passes++ <= state.segments.length + 1) {
    changed = false;
    for (const segment of state.segments) {
      const start = positions.get(segment.startNodeId);
      const end = positions.get(segment.endNodeId);
      const delta = segmentVector(segment);
      if (start && !end) {
        positions.set(segment.endNodeId, start.clone().add(delta));
        changed = true;
      } else if (!start && end) {
        positions.set(segment.startNodeId, end.clone().sub(delta));
        changed = true;
      }
    }
  }

  let disconnectedOffset = 0;
  for (const segment of state.segments) {
    if (!positions.has(segment.startNodeId) && !positions.has(segment.endNodeId)) {
      const anchor = new THREE.Vector3(disconnectedOffset, 0, 0);
      disconnectedOffset += Math.max(1000, segment.distanceMm * 1.2);
      positions.set(segment.startNodeId, anchor);
      positions.set(segment.endNodeId, anchor.clone().add(segmentVector(segment)));
    }
    segment.start.copy(positions.get(segment.startNodeId) || new THREE.Vector3());
    segment.end.copy(positions.get(segment.endNodeId) || segment.start.clone().add(segmentVector(segment)));
  }
  state.nodePositions = positions;
}

function segmentVector(segment) {
  let axis = segment.axis;
  let verticalAxis = axis === 'Z';
  if (state.projectionMode === 'Orthographic') {
    // The classifier keeps horizontal as abstract X and vertical as abstract
    // Z. Choose which physical plane receives that pair in the editor.
    if (elements.orthographicPlane.value === 'XY' && axis === 'Z') axis = 'Y';
    else if (elements.orthographicPlane.value === 'YZ' && axis === 'X') axis = 'Y';
  } else if (elements.swapAxes.checked) {
    if (axis === 'X') axis = 'Y';
    else if (axis === 'Y') axis = 'X';
  }
  let sign = segment.directionSign;
  if (verticalAxis && elements.flipZ.checked) sign *= -1;
  const distance = Math.max(0, Number(segment.distanceMm) || 0) * sign;
  if (axis === 'Y') return new THREE.Vector3(0, distance, 0);
  if (axis === 'Z') return new THREE.Vector3(0, 0, distance);
  return new THREE.Vector3(distance, 0, 0);
}

function rebuildRoute() {
  recomputeTopology();
  if (routeLines) {
    scene.remove(routeLines);
    routeLines.geometry.dispose();
    routeLines.material.dispose();
  }
  if (jointPoints) {
    scene.remove(jointPoints);
    jointPoints.geometry.dispose();
    jointPoints.material.dispose();
  }

  const positions = new Float32Array(state.segments.length * 6);
  const colors = new Float32Array(state.segments.length * 6);
  state.segments.forEach((segment, index) => {
    segment.start.toArray(positions, index * 6);
    segment.end.toArray(positions, index * 6 + 3);
    const color = state.selected.has(index) ? selectedColor : axisColors[segment.axis] || axisColors.X;
    color.toArray(colors, index * 6);
    color.toArray(colors, index * 6 + 3);
  });
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  geometry.computeBoundingSphere();
  routeLines = new THREE.LineSegments(geometry,
    new THREE.LineBasicMaterial({ vertexColors: true, linewidth: 2 }));
  routeLines.frustumCulled = false;
  scene.add(routeLines);

  const unique = [];
  for (const point of state.nodePositions.values()) unique.push(point.x, point.y, point.z);
  const jointGeometry = new THREE.BufferGeometry();
  jointGeometry.setAttribute('position', new THREE.Float32BufferAttribute(unique, 3));
  jointPoints = new THREE.Points(jointGeometry,
    new THREE.PointsMaterial({ color: 0x2f3942, size: 5, sizeAttenuation: false }));
  scene.add(jointPoints);
  rebuildLabels();
}

function rebuildLabels() {
  elements.labelsLayer.replaceChildren();
  state.labels = state.segments.map((segment, index) => {
    const label = document.createElement('div');
    label.className = 'distance-label';
    label.textContent = formatDistance(segment.distanceMm);
    label.dataset.index = String(index);
    elements.labelsLayer.appendChild(label);
    return label;
  });
  updateLabels();
}

function updateLabels() {
  const visible = elements.labelsToggle.checked;
  const rect = elements.viewport.getBoundingClientRect();
  state.labels.forEach((label, index) => {
    if (!visible) {
      label.style.display = 'none';
      return;
    }
    const segment = state.segments[index];
    const midpoint = segment.start.clone().add(segment.end).multiplyScalar(0.5).project(camera);
    const x = (midpoint.x * 0.5 + 0.5) * rect.width;
    const y = (-midpoint.y * 0.5 + 0.5) * rect.height;
    const onScreen = midpoint.z >= -1 && midpoint.z <= 1
      && x >= -40 && x <= rect.width + 40 && y >= -20 && y <= rect.height + 20;
    label.style.display = onScreen ? 'block' : 'none';
    label.style.left = `${x}px`;
    label.style.top = `${y}px`;
    label.textContent = formatDistance(segment.distanceMm);
    label.classList.toggle('selected', state.selected.has(index));
  });
}

function rebuildTable() {
  const fragment = document.createDocumentFragment();
  state.segments.forEach((segment, index) => {
    const row = document.createElement('tr');
    row.dataset.index = String(index);
    row.innerHTML = `<td>${escapeHtml(segment.id)}</td><td>${escapeHtml(displayAxis(segment))}</td><td>${escapeHtml(formatDistance(segment.distanceMm))}</td>`;
    row.addEventListener('click', event => {
      const additive = event.ctrlKey || event.shiftKey;
      if (!additive) {
        startQuickEdit(index, projectPoint(segment.start.clone().add(segment.end).multiplyScalar(0.5)));
        return;
      }
      const next = additive ? new Set(state.selected) : new Set();
      if (additive && next.has(index)) next.delete(index);
      else next.add(index);
      setSelection(next);
    });
    fragment.appendChild(row);
  });
  elements.rows.replaceChildren(fragment);
  updateTableSelection();
}

function rebuildDiagnostics() {
  elements.diagnosticList.replaceChildren();
  for (const diagnostic of state.diagnostics) {
    const item = document.createElement('li');
    item.textContent = String(diagnostic);
    elements.diagnosticList.appendChild(item);
  }
  elements.diagnosticSection.hidden = state.diagnostics.length === 0;
}

function setMode(mode) {
  state.mode = mode;
  controls.enabled = mode === 'orbit';
  elements.orbitMode.classList.toggle('active', mode === 'orbit');
  elements.selectMode.classList.toggle('active', mode === 'select');
  renderer.domElement.style.cursor = mode === 'select' ? 'crosshair' : 'grab';
  cancelSelectionBox();
}

function beginSelection(event) {
  if (event.button !== 0 || !state.initialized) return;
  if (state.mode === 'select') renderer.domElement.setPointerCapture(event.pointerId);
  state.pointerStart = localPoint(event);
  state.pointerCurrent = state.pointerStart;
}

function moveSelection(event) {
  if (!state.pointerStart) return;
  state.pointerCurrent = localPoint(event);
  const dx = state.pointerCurrent.x - state.pointerStart.x;
  const dy = state.pointerCurrent.y - state.pointerStart.y;
  if (state.mode === 'select' && Math.hypot(dx, dy) >= 3)
    showSelectionBox(state.pointerStart, state.pointerCurrent);
}

function finishSelection(event) {
  if (!state.pointerStart) return;
  const start = state.pointerStart;
  const end = localPoint(event);
  const distance = Math.hypot(end.x - start.x, end.y - start.y);
  if (distance < 4) {
    const hit = raycastHit(end);
    if (hit) {
      startQuickEdit(hit.index, end);
    } else if (state.mode === 'select') {
      cancelQuickEdit();
      setSelection([]);
    }
  } else if (state.mode === 'select') {
    const additive = event.ctrlKey || event.shiftKey;
    const indices = boxSelect(start, end);
    const next = additive ? new Set(state.selected) : new Set();
    for (const index of indices) {
      if (additive && next.has(index)) next.delete(index);
      else next.add(index);
    }
    setSelection(next);
  }
  cancelSelectionBox();
}

function cancelSelectionBox() {
  state.pointerStart = null;
  state.pointerCurrent = null;
  elements.selectionBox.style.display = 'none';
}

function raycastHit(point) {
  const rect = elements.viewport.getBoundingClientRect();
  const pointer = new THREE.Vector2(
    point.x / rect.width * 2 - 1,
    -(point.y / rect.height) * 2 + 1);
  const raycaster = new THREE.Raycaster();
  raycaster.params.Line.threshold = state.viewHeight / Math.max(rect.height, 1) * 8 / camera.zoom;
  raycaster.setFromCamera(pointer, camera);
  const hits = raycaster.intersectObject(routeLines, false);
  if (!hits.length) return null;
  const hit = hits[0];
  return {
    index: Math.floor((hit.index || 0) / 2)
  };
}

function boxSelect(start, end) {
  const rect = {
    left: Math.min(start.x, end.x),
    right: Math.max(start.x, end.x),
    top: Math.min(start.y, end.y),
    bottom: Math.max(start.y, end.y)
  };
  const crossing = end.x < start.x;
  const result = [];
  state.segments.forEach((segment, index) => {
    const a = projectPoint(segment.start);
    const b = projectPoint(segment.end);
    const selected = crossing
      ? lineIntersectsRect(a, b, rect)
      : pointInside(a, rect) && pointInside(b, rect);
    if (selected) result.push(index);
  });
  return result;
}

function showSelectionBox(start, end) {
  const left = Math.min(start.x, end.x);
  const top = Math.min(start.y, end.y);
  elements.selectionBox.style.display = 'block';
  elements.selectionBox.style.left = `${left}px`;
  elements.selectionBox.style.top = `${top}px`;
  elements.selectionBox.style.width = `${Math.abs(end.x - start.x)}px`;
  elements.selectionBox.style.height = `${Math.abs(end.y - start.y)}px`;
  elements.selectionBox.classList.toggle('crossing', end.x < start.x);
}

function setSelection(indices) {
  state.selected = indices instanceof Set ? indices : new Set(indices);
  updateLineColors();
  updateSelectionUi();
  updateTableSelection();
  updateLabels();
}

function handleQuickInputKey(event) {
  const target = event.target;
  if (target && /^(INPUT|TEXTAREA|SELECT|BUTTON)$/.test(target.tagName)) return false;
  if (!state.initialized || state.quickIndex < 0) return false;

  if (event.key === 'Enter') {
    event.preventDefault();
    if (!state.inputBuffer) {
      elements.validation.textContent = '请输入距离数字后按 Enter';
      updateQuickInputUi();
      return true;
    }
    const value = Number(state.inputBuffer);
    if (!Number.isFinite(value) || value < 0) {
      elements.validation.textContent = '请输入有效的非负毫米数';
      updateQuickInputUi();
      return true;
    }
    applyQuickDistance(value);
    return true;
  }

  if (event.key === 'Backspace') {
    event.preventDefault();
    state.inputBuffer = state.inputBuffer.slice(0, -1);
    elements.validation.textContent = '';
    updateQuickInputUi();
    return true;
  }

  if (/^[0-9]$/.test(event.key)) {
    event.preventDefault();
    if (state.inputBuffer.length < 16)
      state.inputBuffer += event.key;
    elements.validation.textContent = '';
    updateQuickInputUi();
    return true;
  }

  if ((event.key === '.' || event.key === ',') && !state.inputBuffer.includes('.')) {
    event.preventDefault();
    state.inputBuffer += state.inputBuffer ? '.' : '0.';
    elements.validation.textContent = '';
    updateQuickInputUi();
    return true;
  }
  return false;
}

function startQuickEdit(index, screenPoint) {
  const segment = state.segments[index];
  if (!segment) return;
  renderer.domElement.focus();
  if (state.inputBuffer) {
    elements.validation.textContent = '当前距离尚未确认，请先按 Enter';
    updateQuickInputUi();
    return;
  }

  state.quickPlan = createTraversalPlan(index, screenPoint, state.quickVisited);
  state.quickCursor = 0;
  state.quickIndex = state.quickPlan.length ? state.quickPlan[0].index : index;
  state.inputBuffer = '';
  elements.validation.textContent = '';
  setSelection(new Set([state.quickIndex]));
  focusSegment(state.quickIndex);
  updateQuickInputUi();
}

function cancelQuickEdit() {
  state.quickIndex = -1;
  state.quickPlan = [];
  state.quickCursor = -1;
  state.inputBuffer = '';
  if (elements.validation) elements.validation.textContent = '';
  updateQuickInputUi();
}

function buildAdjacency() {
  const map = new Map();
  const add = (nodeId, index, endpoint) => {
    if (!map.has(nodeId)) map.set(nodeId, []);
    map.get(nodeId).push({ index, endpoint });
  };
  state.segments.forEach((segment, index) => {
    add(segment.startNodeId, index, 'start');
    add(segment.endNodeId, index, 'end');
  });
  return map;
}

function endpointNode(segment, endpoint) {
  return endpoint === 'start' ? segment.startNodeId : segment.endNodeId;
}

function otherEndpoint(endpoint) {
  return endpoint === 'start' ? 'end' : 'start';
}

function neighbors(index, endpoint, blocked) {
  const segment = state.segments[index];
  if (!segment) return [];
  const nodeId = endpointNode(segment, endpoint);
  const entries = state.adjacency.get(nodeId) || [];
  return entries.filter(entry => entry.index !== index
    && !(blocked && blocked.has(entry.index)));
}

function reachableCount(index, entryEndpoint, blocked) {
  const visited = new Set(blocked || []);
  visited.add(index);
  const queue = [];
  for (const entry of neighbors(index, entryEndpoint, visited)) {
    visited.add(entry.index);
    queue.push(entry.index);
  }
  let count = 0;
  while (queue.length) {
    const currentIndex = queue.shift();
    count++;
    const current = state.segments[currentIndex];
    for (const endpoint of ['start', 'end']) {
      for (const entry of neighbors(currentIndex, endpoint, visited)) {
        visited.add(entry.index);
        queue.push(entry.index);
      }
    }
  }
  return count;
}

function classifyScreenClick(segment, point) {
  if (!point) return 'interior';
  const start = projectPoint(segment.start);
  const end = projectPoint(segment.end);
  const startDistance = Math.hypot(point.x - start.x, point.y - start.y);
  const endDistance = Math.hypot(point.x - end.x, point.y - end.y);
  const threshold = 18;
  if (startDistance <= threshold || endDistance <= threshold)
    return startDistance <= endDistance ? 'start' : 'end';
  return 'interior';
}

function createTraversalPlan(startIndex, point, blocked) {
  const start = state.segments[startIndex];
  if (!start) return [];
  const visited = new Set(blocked || []);
  visited.delete(startIndex);
  const region = classifyScreenClick(start, point);
  let exitEndpoint;
  if (region === 'start') exitEndpoint = 'end';
  else if (region === 'end') exitEndpoint = 'start';
  else {
    const startCount = reachableCount(startIndex, 'start', visited);
    const endCount = reachableCount(startIndex, 'end', visited);
    exitEndpoint = startCount >= endCount ? 'start' : 'end';
  }

  const plan = [];
  let index = startIndex;
  let entryEndpoint = region === 'interior' ? null : region;
  while (index >= 0 && !visited.has(index)) {
    const segment = state.segments[index];
    if (!segment) break;
    visited.add(index);
    plan.push({ index, entryEndpoint, exitEndpoint });
    const candidates = neighbors(index, exitEndpoint, visited);
    let next = null;
    let nextScore = -1;
    for (const candidate of candidates) {
      const candidateExit = otherEndpoint(candidate.endpoint);
      const score = reachableCount(candidate.index, candidateExit, visited);
      const candidateId = state.segments[candidate.index].id;
      const nextId = next ? state.segments[next.index].id : '';
      if (!next || score > nextScore
        || (score === nextScore && candidateId.localeCompare(nextId) < 0)) {
        next = candidate;
        nextScore = score;
      }
    }
    if (!next) break;
    index = next.index;
    entryEndpoint = next.endpoint;
    exitEndpoint = otherEndpoint(entryEndpoint);
  }
  return plan;
}

function applyQuickDistance(value) {
  const index = state.quickIndex;
  const segment = state.segments[index];
  if (!segment) return;
  if (Math.abs(segment.distanceMm - value) > 1e-8) {
    segment.distanceMm = value;
    state.changed.add(segment.id);
  }
  state.quickVisited.add(index);
  state.inputBuffer = '';
  state.revision += 1;
  rebuildRoute();

  let nextCursor = state.quickCursor + 1;
  while (nextCursor < state.quickPlan.length
    && state.quickVisited.has(state.quickPlan[nextCursor].index)) nextCursor++;
  if (nextCursor < state.quickPlan.length) {
    state.quickCursor = nextCursor;
    state.quickIndex = state.quickPlan[nextCursor].index;
    setSelection(new Set([state.quickIndex]));
    focusSegment(state.quickIndex);
    showToast(`已设置 ${formatDistance(value)}，下一段已选中`);
  } else {
    state.quickCursor = -1;
    state.quickIndex = -1;
    setSelection(new Set([index]));
    showToast(`已设置 ${formatDistance(value)}，该方向没有更多相连线段`);
  }
  elements.validation.textContent = '';
  updateQuickInputUi();
}

function focusSegment(index) {
  const segment = state.segments[index];
  if (!segment || !controls || !camera) return;
  const center = segment.start.clone().add(segment.end).multiplyScalar(0.5);
  const offset = camera.position.clone().sub(controls.target);
  controls.target.copy(center);
  camera.position.copy(center).add(offset);
  controls.update();
}

function updateLineColors() {
  if (!routeLines) return;
  const colors = routeLines.geometry.getAttribute('color');
  state.segments.forEach((segment, index) => {
    const color = state.selected.has(index) ? selectedColor : axisColors[segment.axis] || axisColors.X;
    colors.setXYZ(index * 2, color.r, color.g, color.b);
    colors.setXYZ(index * 2 + 1, color.r, color.g, color.b);
  });
  colors.needsUpdate = true;
}

function updateSelectionUi() {
  const selected = Array.from(state.selected).map(index => state.segments[index]).filter(Boolean);
  const count = selected.length;
  elements.selectionCount.textContent = count ? `已选 ${count} 段` : '未选择';
  elements.selectedMetric.textContent = String(count);
  elements.axisMetric.textContent = commonValue(selected.map(displayAxis)) || '-';
  const angles = selected.map(item => `${formatNumber(item.planAngleDegrees)}°`);
  elements.angleMetric.textContent = commonValue(angles) || '-';
  const distances = selected.map(item => item.distanceMm);
  const commonDistance = commonNumber(distances);
  elements.distanceMetric.textContent = count
    ? (commonDistance === null ? '混合' : formatDistance(commonDistance)) : '-';
  elements.changedCount.textContent = `${state.changed.size} 项修改`;
  const active = state.quickIndex >= 0 && state.segments[state.quickIndex];
  elements.status.textContent = active
    ? `当前 ${active.id}：输入距离并按 Enter`
    : (count ? `已选择 ${count} 根线段` : `已载入 ${state.segments.length} 根相连线段`);
  updateQuickInputUi();
}

function updateQuickInputUi() {
  if (!elements.quickInputDisplay) return;
  const active = state.quickIndex >= 0 && state.segments[state.quickIndex];
  if (!active) {
    elements.quickInputDisplay.textContent = '点击 3D 线段开始';
    elements.quickInputDisplay.classList.remove('active');
    if (elements.quickInputHint)
      elements.quickInputHint.textContent = '直接输入数字，按 Enter 写入当前线段';
    return;
  }
  elements.quickInputDisplay.textContent = state.inputBuffer || '输入距离...';
  elements.quickInputDisplay.classList.add('active');
  if (elements.quickInputHint)
    elements.quickInputHint.textContent = `当前线段 ${active.id}，输入数字后按 Enter`;
}

function updateTableSelection() {
  for (const row of elements.rows.querySelectorAll('tr')) {
    const index = Number(row.dataset.index);
    row.classList.toggle('selected', state.selected.has(index));
    row.classList.toggle('changed', state.changed.has(state.segments[index]?.id));
    const distanceCell = row.cells[2];
    if (distanceCell && state.segments[index])
      distanceCell.textContent = formatDistance(state.segments[index].distanceMm);
  }
}

function commitEditor() {
  if (!state.initialized) return;
  if (state.inputBuffer) {
    elements.validation.textContent = '当前距离尚未确认，请先按 Enter';
    updateQuickInputUi();
    return;
  }
  const updates = state.segments
    .filter(segment => state.changed.has(segment.id))
    .map(segment => ({ segmentId: segment.id, distanceMm: segment.distanceMm }));
  if (!hasWebViewHost()) {
    showToast(updates.length ? `演示模式：${updates.length} 项修改` : '演示模式：没有修改');
    return;
  }
  elements.commit.disabled = true;
  postHost({
    type: 'commit',
    schemaVersion: 1,
    sessionId: state.sessionId,
    revision: state.revision,
    updates
  });
}

function cancelEditor() {
  if (hasWebViewHost()) {
    postHost({ type: 'cancel', schemaVersion: 1, sessionId: state.sessionId });
  } else {
    initializeScene(demoEnvelope());
    showToast('已还原演示数据');
  }
}

function remapAxes() {
  rebuildRoute();
  fitView();
}

function displayAxis(segment) {
  if (state.projectionMode !== 'Orthographic') return segment.axis;
  if (elements.orthographicPlane.value === 'XY')
    return segment.axis === 'Z' ? 'Y' : segment.axis;
  if (elements.orthographicPlane.value === 'YZ')
    return segment.axis === 'X' ? 'Y' : segment.axis;
  return segment.axis;
}

function fitView() {
  if (!state.segments.length) return;
  const box = new THREE.Box3();
  for (const segment of state.segments) {
    box.expandByPoint(segment.start);
    box.expandByPoint(segment.end);
  }
  const center = box.getCenter(new THREE.Vector3());
  const size = box.getSize(new THREE.Vector3());
  const extent = Math.max(size.x, size.y, size.z, 100);
  state.viewHeight = extent * 1.45;
  const direction = new THREE.Vector3(1.35, -1.55, 1.15).normalize();
  camera.position.copy(center).addScaledVector(direction, extent * 2.4);
  camera.near = Math.max(0.01, extent * 0.001);
  camera.far = Math.max(10000, extent * 12);
  camera.zoom = 1;
  controls.target.copy(center);
  controls.update();
  updateCameraFrustum();
}

function resizeRenderer() {
  const width = Math.max(1, elements.viewport.clientWidth);
  const height = Math.max(1, elements.viewport.clientHeight);
  renderer.setSize(width, height, false);
  updateCameraFrustum();
}

function updateCameraFrustum() {
  if (!camera || !renderer) return;
  const width = Math.max(1, elements.viewport.clientWidth);
  const height = Math.max(1, elements.viewport.clientHeight);
  const aspect = width / height;
  camera.left = -state.viewHeight * aspect / 2;
  camera.right = state.viewHeight * aspect / 2;
  camera.top = state.viewHeight / 2;
  camera.bottom = -state.viewHeight / 2;
  camera.updateProjectionMatrix();
}

function animate() {
  window.requestAnimationFrame(animate);
  if (controls) controls.update();
  if (renderer && scene && camera) renderer.render(scene, camera);
  if (state.initialized) updateLabels();
}

function localPoint(event) {
  const rect = elements.viewport.getBoundingClientRect();
  return { x: event.clientX - rect.left, y: event.clientY - rect.top };
}

function projectPoint(point) {
  const rect = elements.viewport.getBoundingClientRect();
  const projected = point.clone().project(camera);
  return {
    x: (projected.x * 0.5 + 0.5) * rect.width,
    y: (-projected.y * 0.5 + 0.5) * rect.height
  };
}

function pointInside(point, rect) {
  return point.x >= rect.left && point.x <= rect.right
    && point.y >= rect.top && point.y <= rect.bottom;
}

function lineIntersectsRect(a, b, rect) {
  if (pointInside(a, rect) || pointInside(b, rect)) return true;
  const topLeft = { x: rect.left, y: rect.top };
  const topRight = { x: rect.right, y: rect.top };
  const bottomLeft = { x: rect.left, y: rect.bottom };
  const bottomRight = { x: rect.right, y: rect.bottom };
  return linesIntersect(a, b, topLeft, topRight)
    || linesIntersect(a, b, topRight, bottomRight)
    || linesIntersect(a, b, bottomRight, bottomLeft)
    || linesIntersect(a, b, bottomLeft, topLeft);
}

function linesIntersect(a, b, c, d) {
  const epsilon = 1e-7;
  const cross = (p, q, r) => (q.x - p.x) * (r.y - p.y) - (q.y - p.y) * (r.x - p.x);
  const onSegment = (point, start, end) =>
    point.x >= Math.min(start.x, end.x) - epsilon
    && point.x <= Math.max(start.x, end.x) + epsilon
    && point.y >= Math.min(start.y, end.y) - epsilon
    && point.y <= Math.max(start.y, end.y) + epsilon;
  const abC = cross(a, b, c);
  const abD = cross(a, b, d);
  const cdA = cross(c, d, a);
  const cdB = cross(c, d, b);
  const abStraddles = (abC > epsilon && abD < -epsilon)
    || (abC < -epsilon && abD > epsilon);
  const cdStraddles = (cdA > epsilon && cdB < -epsilon)
    || (cdA < -epsilon && cdB > epsilon);
  if (abStraddles && cdStraddles) return true;
  if (Math.abs(abC) <= epsilon && onSegment(c, a, b)) return true;
  if (Math.abs(abD) <= epsilon && onSegment(d, a, b)) return true;
  if (Math.abs(cdA) <= epsilon && onSegment(a, c, d)) return true;
  return Math.abs(cdB) <= epsilon && onSegment(b, c, d);
}

function commonValue(values) {
  if (!values.length) return null;
  const first = values[0];
  return values.every(value => value === first) ? first : '混合';
}

function commonNumber(values) {
  if (!values.length) return null;
  const first = values[0];
  return values.every(value => Math.abs(value - first) <= 1e-8) ? first : null;
}

function formatDistance(value) { return `${formatNumber(value)}mm`; }

function formatNumber(value) {
  if (!Number.isFinite(value)) return '0';
  return Number(value.toFixed(3)).toString();
}

function escapeHtml(value) {
  return String(value).replace(/[&<>'"]/g, character => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;'
  })[character]);
}

function showToast(message) {
  window.clearTimeout(toastTimer);
  elements.toast.textContent = message;
  elements.toast.classList.add('visible');
  toastTimer = window.setTimeout(() => elements.toast.classList.remove('visible'), 1800);
}

function hasWebViewHost() {
  return Boolean(window.chrome && window.chrome.webview);
}

function postHost(message) {
  if (hasWebViewHost()) window.chrome.webview.postMessage(message);
}

function fail(message) {
  elements.loading.style.display = 'grid';
  elements.loading.textContent = `三维视图不可用：${message}`;
  elements.commit.disabled = true;
  postHost({ type: 'renderError', schemaVersion: 1, message });
}

function demoEnvelope() {
  return {
    type: 'initialize',
    schemaVersion: 1,
    sessionId: 'browser-preview',
    revision: 0,
    scene: {
      rootNodeId: 'N1',
      diagnostics: [],
      segments: [
        segment('16CB', 'N1', 'N2', 'X', 1, 30, 3400),
        segment('16CD', 'N2', 'N3', 'Z', 1, 90, 1500),
        segment('16CF', 'N3', 'N4', 'Y', 1, 150, 3200),
        segment('16D1', 'N4', 'N5', 'X', -1, 30, 2100),
        segment('1751', 'N5', 'N6', 'Z', -1, 90, 1200),
        segment('1753', 'N6', 'N7', 'Y', -1, 150, 4100),
        segment('1755', 'N7', 'N8', 'X', 1, 30, 2000)
      ]
    }
  };
}

function segment(id, startNodeId, endNodeId, axis, directionSign,
  planAngleDegrees, distanceMm) {
  return { id, startNodeId, endNodeId, axis, directionSign, planAngleDegrees, distanceMm };
}
