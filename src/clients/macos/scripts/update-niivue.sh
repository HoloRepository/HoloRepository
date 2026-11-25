#!/bin/zsh
# Refreshes WebViewer/vendor from npm. Only needed when upgrading NiiVue;
# the vendored files are committed so Xcode builds work offline with no Node install.
set -euo pipefail
cd "$(dirname "$0")/../WebViewer"
npm install --no-audit --no-fund
# vendor/niivue/LICENSE and NOTICES are kept by hand: the npm package doesn't ship them.
# When upgrading, check https://github.com/niivue/niivue for licence or bundled-library changes.
rm -f vendor/niivue/niivue.js && rm -rf vendor/dcm2niix && mkdir -p vendor/niivue vendor/dcm2niix
cp node_modules/@niivue/niivue/dist/index.js vendor/niivue/niivue.js
sed -i '' '/^\/\/# sourceMappingURL/d' vendor/niivue/niivue.js
cp node_modules/@niivue/dcm2niix/dist/{index.jpeg.js,worker.jpeg.js,dcm2niix.jpeg.js,dcm2niix.jpeg.wasm} vendor/dcm2niix/
cp node_modules/@niivue/dcm2niix/{LICENSE,NOTICES} vendor/dcm2niix/
echo "Vendored: $(node -p "require('./node_modules/@niivue/niivue/package.json').version") (niivue), $(node -p "require('./node_modules/@niivue/dcm2niix/package.json').version") (dcm2niix)"
