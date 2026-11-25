// HoloRepository 2027 – web viewer glue.
//
// This page runs inside a WKWebView in the macOS app (and can be reused as-is in a
// WebView2 control in the Windows WinUI 3 app). Native code drives it through the
// `window.holo` API below and listens for events posted with `native(...)`.

import { Niivue, SLICE_TYPE, SHOW_RENDER, DRAG_MODE } from './vendor/niivue/niivue.js'
import { Dcm2niix } from './vendor/dcm2niix/index.jpeg.js'

// ---- Native bridge -------------------------------------------------------
// macOS: window.webkit.messageHandlers.holo   Windows (WebView2): window.chrome.webview
function native(message) {
  if (window.webkit?.messageHandlers?.holo) window.webkit.messageHandlers.holo.postMessage(message)
  else if (window.chrome?.webview) window.chrome.webview.postMessage(message)
  else console.log('[native]', message)
}
const log = (...args) => native({ type: 'log', message: args.join(' ') })

// ---- NiiVue setup --------------------------------------------------------
const nv = new Niivue({
  backColor: [0, 0, 0, 1],
  crosshairColor: [0.2, 0.8, 1, 1],
  show3Dcrosshair: true,
  multiplanarShowRender: SHOW_RENDER.ALWAYS, // "All views" = axial + coronal + sagittal + 3D
  isColorbar: false,
  dragAndDropEnabled: false,
  loadingText: '',
  isRadiologicalConvention: true, // patient's left on screen right, like clinical viewers
  multiplanarPadPixels: 8,        // gap between the tiles in "All views"
  tileMargin: -1,                 // room around each view for its orientation letters (S, R, A, P)
})

const state = {
  looksLikeCT: false,
  layout: SLICE_TYPE.MULTIPLANAR,
  win2D: null,        // { min, max } for 2D views
  win3D: null,        // user-adjusted window in the 3D-only view (null = same as 2D)
  preset: 'standard',
  lighting: 0.6,
  cutaway: false,
}

nv.onLocationChange = (data) => {
  native({ type: 'location', text: data?.string ?? '' })
}

// Keep the cut-away facing the camera while the user rotates the 3D view.
nv.onAzimuthElevationChange = () => { if (state.cutaway) applyCutaway() }

nv.onIntensityChange = (volume) => {
  if (volume !== nv.volumes[0]) return
  const win = { min: volume.cal_min, max: volume.cal_max }
  if (state.layout === SLICE_TYPE.RENDER) state.win3D = win
  else state.win2D = win
  reportWindow()
}

function reportWindow() {
  const vol = nv.volumes[0]
  if (!vol) return
  native({ type: 'window', center: (vol.cal_max + vol.cal_min) / 2, width: vol.cal_max - vol.cal_min })
}

// ---- Loading scans -------------------------------------------------------

let converter = null
async function getConverter() {
  if (!converter) {
    converter = new Dcm2niix()
    await converter.init()
  }
  return converter
}

async function fetchBuffer(url) {
  const response = await fetch(url)
  if (!response.ok) throw new Error(`Could not read file (HTTP ${response.status})`)
  return await response.arrayBuffer()
}

async function fetchAll(urls, names, concurrency = 8) {
  const files = new Array(urls.length)
  let next = 0, done = 0
  async function work() {
    while (next < urls.length) {
      const i = next++
      files[i] = new File([await fetchBuffer(urls[i])], names[i] || `${i}.dcm`)
      if (++done % 25 === 0 || done === urls.length)
        native({ type: 'progress', message: `Reading DICOM files… ${done}/${urls.length}` })
    }
  }
  await Promise.all(Array.from({ length: Math.min(concurrency, urls.length) }, work))
  return files
}

/** DICOM files → one NIfTI volume using dcm2niix (WebAssembly). */
async function dicomToNifti(urls, names) {
  const files = await fetchAll(urls, names)
  native({ type: 'progress', message: 'Reconstructing 3D volume…' })
  const dcm2niix = await getConverter()
  const outputs = await dcm2niix.input(files).bids('n').z('n').run()
  const volumes = outputs.filter((f) => /\.nii(\.gz)?$/i.test(f.name))
  if (volumes.length === 0) throw new Error('dcm2niix could not build a volume from these DICOM files.')
  volumes.sort((a, b) => b.size - a.size) // if a series splits, show the largest part
  return { buffer: await volumes[0].arrayBuffer(), fileName: volumes[0].name }
}

function clearAll() {
  while (nv.volumes.length > 0) nv.removeVolumeByIndex(0)
  state.win2D = state.win3D = null
}

async function loadScan(name, format, urls, names) {
  try {
    const started = performance.now()
    clearAll()
    let buffer, fileName
    if (format === 'dicom') {
      ({ buffer, fileName } = await dicomToNifti(urls, names))
    } else {
      native({ type: 'progress', message: 'Reading NIfTI file…' })
      buffer = await fetchBuffer(urls[0])
      fileName = names[0]
    }

    native({ type: 'progress', message: 'Uploading to GPU…' })
    await nv.loadFromArrayBuffer(buffer, fileName)
    const vol = nv.volumes[0]
    if (!vol) throw new Error('NiiVue could not read this file.')
    const [, x, y, z] = vol.hdr.dims
    const [, sx, sy, sz] = vol.hdr.pixDims
    state.looksLikeCT = vol.global_min < -500 && vol.global_max > 300
    state.win2D = { min: vol.cal_min, max: vol.cal_max }
    state.win3D = null

    await nv.setVolumeRenderIllumination(state.lighting)
    nv.setRenderAzimuthElevation(FRONT_VIEW.azimuth, FRONT_VIEW.elevation)
    applyView()

    const seconds = ((performance.now() - started) / 1000).toFixed(1)
    native({
      type: 'loaded',
      looksLikeCT: state.looksLikeCT,
      info: `${x}×${y}×${z} voxels · ${sx.toFixed(2)}×${sy.toFixed(2)}×${sz.toFixed(2)} mm · loaded in ${seconds}s`,
    })
    reportWindow()
    log(`Loaded ${name} (${format}) in ${seconds}s`)
  } catch (error) {
    native({ type: 'error', message: String(error?.message ?? error) })
  }
}

// ---- View state ----------------------------------------------------------

// NiiVue camera: azimuth 180° looks at the patient's front (anterior).
const FRONT_VIEW = { azimuth: 180, elevation: 15 }

const RENDER_PRESETS = {
  bone: { min: 180, max: 900 },
  skin: { min: -450, max: 600 },
}

/** Applies window, 3D preset and visibility for the current layout. */
function applyView() {
  const vol = nv.volumes[0]
  if (!vol) return
  const in3DOnly = state.layout === SLICE_TYPE.RENDER
  let win = state.win2D
  if (in3DOnly) {
    if (state.preset === 'standard') win = state.win3D ?? state.win2D
    else if (RENDER_PRESETS[state.preset] && state.looksLikeCT) win = RENDER_PRESETS[state.preset]
  }
  vol.cal_min = win.min
  vol.cal_max = win.max
  nv.updateGLVolume()
  reportWindow()
}

function applyCutaway() {
  // [depth, azimuth, elevation]; depth 2 = no clipping. The cut faces the camera,
  // so the half nearest the viewer is removed whichever way the volume is turned.
  const az = nv.scene.renderAzimuth ?? 110
  const el = nv.scene.renderElevation ?? 10
  nv.setClipPlane(state.cutaway ? [0, (az + 180) % 360, -el] : [2, 0, 0])
}

// ---- API called from Swift (window.holo.*) -------------------------------
window.holo = {
  loadScan,

  clearAll() {
    clearAll()
    nv.updateGLVolume()
  },

  setLayout(sliceType) {
    state.layout = sliceType
    nv.setSliceType(sliceType)
    applyView()
  },

  setTool(mode) {
    // Left-drag does the chosen tool; right-drag always adjusts window/level.
    nv.opts.dragModePrimary = DRAG_MODE[mode] ?? DRAG_MODE.crosshair
    nv.setDragMode(DRAG_MODE.windowing)
  },

  setWindow(center, width) {
    state.win2D = { min: center - width / 2, max: center + width / 2 }
    if (state.layout === SLICE_TYPE.RENDER) state.win3D = state.win2D
    applyView()
  },

  resetView() {
    const vol = nv.volumes[0]
    if (vol) state.win2D = { min: vol.robust_min, max: vol.robust_max }
    state.win3D = null
    nv.setPan2Dxyzmm([0, 0, 0, 1])
    nv.setRenderAzimuthElevation(FRONT_VIEW.azimuth, FRONT_VIEW.elevation)
    applyView()
  },

  setRenderPreset(name) {
    state.preset = name
    applyView()
  },

  async setLighting(amount) {
    state.lighting = amount
    if (nv.volumes.length) await nv.setVolumeRenderIllumination(amount)
  },

  setCutaway(enabled) {
    state.cutaway = enabled
    applyCutaway()
  },
}

await nv.attachTo('gl')
nv.setSliceType(SLICE_TYPE.MULTIPLANAR)
window.holo.setTool('crosshair')
native({ type: 'ready' })
