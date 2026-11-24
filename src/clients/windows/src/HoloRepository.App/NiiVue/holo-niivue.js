// Bridge between the HoloRepository Windows client and NiiVue.
// The app calls window.holo.* through WebView2 and receives "ready" / "loaded" / "error:..." messages back.
(async () => {
  const post = (m) => window.chrome && window.chrome.webview && window.chrome.webview.postMessage(m);
  try {
    const nv = new niivue.Niivue({
      backColor: [0.016, 0.039, 0.098, 1],
      show3Dcrosshair: false,
      isColorbar: false,
      loadingText: ''
    });
    await nv.attachTo('gl');
    nv.setSliceType(nv.sliceTypeRender);

    window.holo = {
      // scene: NiiVueSceneDescription serialised by the app; folder: URL prefix of the scene's files.
      async load(scene, folder) {
        try {
          const volumes = [{ url: folder + scene.volumeUrl, colormap: 'gray', cal_min: scene.calMin, cal_max: scene.calMax }];
          if (scene.labelsUrl) volumes.push({ url: folder + scene.labelsUrl, colormap: 'gray', opacity: 0.6 });
          await nv.loadVolumes(volumes);
          if (scene.labelsUrl && scene.labelColours && nv.volumes.length > 1) {
            const c = scene.labelColours;
            nv.volumes[1].setColormapLabel({ R: c.r, G: c.g, B: c.b, A: c.a, I: c.i, labels: c.labels });
            nv.updateGLVolume();
          }
          while (nv.meshes.length > 0) nv.removeMesh(nv.meshes[0]);
          if (scene.meshes.length > 0) {
            await nv.loadMeshes(scene.meshes.map((m) => ({
              // NiiVue infers the file format from the name, so use the file name (e.g. mesh-1.mz3).
              url: folder + m.url, name: m.url, rgba255: m.rgba255, opacity: m.rgba255[3] / 255
            })));
          }
          nv.setSliceType(nv.sliceTypeRender);
          post('loaded');
        } catch (e) {
          post('error:' + (e && e.message ? e.message : e));
        }
      },
      setWindow(min, max) {
        if (nv.volumes.length === 0) return;
        nv.volumes[0].cal_min = min;
        nv.volumes[0].cal_max = max;
        nv.updateGLVolume();
      },
      setView(kind) {
        nv.setSliceType(kind === 'slices' ? nv.sliceTypeMultiplanar : nv.sliceTypeRender);
      }
    };
    post('ready');
  } catch (e) {
    post('error:' + (e && e.message ? e.message : e));
  }
})();
