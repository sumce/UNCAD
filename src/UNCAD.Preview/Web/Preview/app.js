import * as THREE from './vendor/three/three.module.min.js';
import { OrbitControls } from './vendor/three/OrbitControls.js';

const viewport = document.querySelector('#viewport');
const labelLayer = document.querySelector('#labelLayer');
const groupInputs = [...document.querySelectorAll('[data-group]')];
const hiddenGroups = new Set();
const hiddenKinds = new Set();
const groups = new Map();
let renderer, camera, controls, data, labels = [], queued = false;
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x16181b);
scene.add(new THREE.HemisphereLight(0xffffff, 0x626b65, 2.1));
const sun = new THREE.DirectionalLight(0xffffff, 2);
sun.position.set(-1, -2, 4);
scene.add(sun);

function notify(message) { window.chrome?.webview?.postMessage(message); }
function fail(error) {
  const message = String(error?.message || error);
  const panel = document.querySelector('#error');
  panel.textContent = message;
  panel.hidden = false;
  notify('error:' + message);
}
function invalidate() {
  if (queued || !renderer || !camera) return;
  queued = true;
  requestAnimationFrame(() => {
    queued = false;
    renderer.render(scene, camera);
    updateLabels();
  });
}
function resize() {
  if (!renderer || !camera) return;
  const width = Math.max(1, viewport.clientWidth), height = Math.max(1, viewport.clientHeight);
  renderer.setSize(width, height, false);
  if (camera.isOrthographicCamera) {
    camera.left = -data.camera.height * width / height / 2;
    camera.right = -camera.left;
    camera.top = data.camera.height / 2;
    camera.bottom = -camera.top;
  } else camera.aspect = width / height;
  camera.updateProjectionMatrix();
  invalidate();
}
function reset() {
  if (!controls) return;
  const c = data.camera, center = new THREE.Vector3(...c.center);
  camera.position.copy(new THREE.Vector3(...c.orbit).normalize().multiplyScalar(c.distance).add(center));
  camera.zoom = 1;
  controls.target.copy(center);
  controls.update();
  resize();
}
function updateLabels() {
  const width = viewport.clientWidth, height = viewport.clientHeight;
  const occupied = [], point = new THREE.Vector3();
  for (const label of labels) {
    const el = label.element;
    el.hidden = true;
    if (hiddenGroups.has(label.group) || hiddenKinds.has(label.kind)) continue;
    point.fromArray(label.position).project(camera);
    if (point.z < -1 || point.z > 1) continue;
    const x = (point.x + 1) * width / 2, y = (1 - point.y) * height / 2;
    for (const offset of label.kind === 'axis' ? [0] : [0, -20, 20, -40, 40]) {
      const box = { x: x - label.width / 2, y: y - label.height / 2 + offset,
        w: label.width, h: label.height };
      if (box.x < 0 || box.y < 0 || box.x + box.w > width || box.y + box.h > height) continue;
      if (occupied.some(b => box.x < b.x + b.w + 3 && box.x + box.w + 3 > b.x
        && box.y < b.y + b.h + 2 && box.y + box.h + 2 > b.y)) continue;
      el.style.transform = `translate(${Math.round(box.x)}px,${Math.round(box.y)}px)`;
      el.hidden = false;
      occupied.push(box);
      break;
    }
  }
}
function load(payload) {
  try {
    if (!payload || payload.version !== 1 || !Array.isArray(payload.meshes)) throw Error('场景格式不正确');
    data = payload;
    for (const group of groups.values()) {
      scene.remove(group);
      for (const mesh of group.children) { mesh.geometry.dispose(); mesh.material.dispose(); }
    }
    groups.clear();
    hiddenGroups.clear();
    hiddenKinds.clear();
    for (const group of data.hiddenGroups) hiddenGroups.add(group);
    for (const kind of data.hiddenLabelKinds) hiddenKinds.add(kind);
    for (const raw of data.meshes) {
      if (!groups.has(raw.group)) {
        const group = new THREE.Group();
        group.visible = !hiddenGroups.has(raw.group);
        groups.set(raw.group, group);
        scene.add(group);
      }
      const geometry = new THREE.BufferGeometry();
      geometry.setAttribute('position', new THREE.Float32BufferAttribute(raw.positions, 3));
      geometry.setAttribute('normal', new THREE.Float32BufferAttribute(raw.normals, 3));
      geometry.setIndex(raw.indices);
      geometry.computeBoundingSphere();
      const material = new THREE.MeshStandardMaterial({
        color: raw.color, roughness: 0.6, metalness: 0.08,
        opacity: raw.opacity, transparent: raw.opacity < 1,
        depthWrite: raw.opacity >= 1, side: raw.opacity < 1 ? THREE.DoubleSide : THREE.FrontSide
      });
      groups.get(raw.group).add(new THREE.Mesh(geometry, material));
    }
    document.querySelector('#title').textContent = data.title;
    document.querySelector('#error').hidden = true;
    const c = data.camera;
    controls?.dispose();
    camera = c.orthographic
      ? new THREE.OrthographicCamera(-1, 1, 1, -1, c.near, c.far)
      : new THREE.PerspectiveCamera(60, 1, c.near, c.far);
    camera.up.set(0, 0, 1);
    controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = false;
    controls.zoomToCursor = true;
    controls.mouseButtons.MIDDLE = THREE.MOUSE.PAN;
    controls.minDistance = Math.max(1, c.diagonal * 0.02);
    controls.maxDistance = Math.max(c.diagonal * 50, c.distance * 10);
    controls.addEventListener('change', invalidate);
    labelLayer.replaceChildren();
    labels = data.labels.map(raw => {
      const element = document.createElement('span');
      element.className = 'label';
      element.dataset.kind = raw.kind;
      element.textContent = raw.text;
      labelLayer.append(element);
      return { ...raw, element, width: element.offsetWidth, height: element.offsetHeight };
    }).sort((a, b) => priority(a.kind) - priority(b.kind));
    for (const input of groupInputs) {
      input.checked = !hiddenGroups.has(input.dataset.group);
      input.parentElement.hidden = !groups.has(input.dataset.group);
    }
    reset();
    requestAnimationFrame(() => notify('rendered'));
  } catch (error) { fail(error); }
}
function priority(kind) { return ['mm', 'pin', 'route', 'ref', 'axis'].indexOf(kind) + 1 || 6; }

try {
  renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.5));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  document.querySelector('#canvasHost').append(renderer.domElement);
  renderer.domElement.addEventListener('dblclick', reset);
  renderer.domElement.addEventListener('webglcontextlost', event => {
    event.preventDefault();
    fail('图形上下文已丢失，请重新打开 WebV 或返回 WPF。');
  });
  new ResizeObserver(resize).observe(viewport);
  for (const input of groupInputs) input.addEventListener('change', () => {
    const group = input.dataset.group;
    if (input.checked) hiddenGroups.delete(group); else hiddenGroups.add(group);
    if (groups.has(group)) groups.get(group).visible = input.checked;
    invalidate();
  });
  document.querySelector('#labels').addEventListener('change', event => {
    hiddenKinds.clear();
    if (!event.target.checked)
      for (const label of labels) hiddenKinds.add(label.kind);
    invalidate();
  });
  document.querySelector('#fit').addEventListener('click', reset);
  document.querySelector('#back').addEventListener('click', () => notify('close'));
  window.chrome?.webview?.addEventListener('message', async event => {
    if (event.data !== 'load') return;
    try {
      const response = await fetch('./scene.json', { cache: 'no-store' });
      if (!response.ok) throw Error(`场景数据读取失败 (${response.status})`);
      load(await response.json());
      document.querySelector('#labels').checked = hiddenKinds.size === 0;
      notify('scene-loaded');
    } catch (error) { fail(error); }
  });
  notify('ready');
} catch (error) { fail(error); }
