// HoloRepository 2027 – web viewer glue.
//
// This page runs inside a WKWebView in the macOS app (and can be reused as-is in a
// WebView2 control in the Windows WinUI 3 app). Native code drives it through the
// `window.holo` API below and listens for events posted with `native(...)`.

import { Niivue, SLICE_TYPE, SHOW_RENDER } from './vendor/niivue/niivue.js'
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
  multiplanarShowRender: SHOW_RENDER.ALWAYS, // axial, coronal, sagittal and 3D side by side
  isColorbar: false,
  dragAndDropEnabled: false,
  loadingText: '',
  isRadiologicalConvention: true, // patient's left on screen right, like clinical viewers
  multiplanarPadPixels: 8,        // gap between the views
  tileMargin: -1,                 // room around each view for its orientation letters (S, R, A, P)
})

nv.onLocationChange = (data) => {
  native({ type: 'location', text: data?.string ?? '' })
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
    nv.setRenderAzimuthElevation(FRONT_VIEW.azimuth, FRONT_VIEW.elevation)

    const seconds = ((performance.now() - started) / 1000).toFixed(1)
    native({
      type: 'loaded',
      info: `${x}×${y}×${z} voxels · ${sx.toFixed(2)}×${sy.toFixed(2)}×${sz.toFixed(2)} mm · loaded in ${seconds}s`,
    })
    log(`Loaded ${name} (${format}) in ${seconds}s`)
  } catch (error) {
    native({ type: 'error', message: String(error?.message ?? error) })
  }
}

// NiiVue camera: azimuth 180° looks at the patient's front (anterior).
const FRONT_VIEW = { azimuth: 180, elevation: 15 }

// ---- API called from Swift (window.holo.*) -------------------------------
window.holo = {
  loadScan,

  clearAll() {
    clearAll()
    nv.updateGLVolume()
  },
}

await nv.attachTo('gl')
nv.setSliceType(SLICE_TYPE.MULTIPLANAR)
native({ type: 'ready' })
