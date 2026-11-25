// Classic (non-module) script that runs first, so even errors that stop
// viewer.js from loading at all (bad import, syntax error) reach the native app.
(function () {
  function send(message) {
    var m = { type: 'log', message: message }
    if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.holo) window.webkit.messageHandlers.holo.postMessage(m)
    else if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(m)
  }
  window.addEventListener('error', function (e) {
    send('JS error: ' + (e.message || (e.target && (e.target.src || e.target.href)) || 'unknown') + (e.filename ? ' @ ' + e.filename + ':' + e.lineno : ''))
  }, true)
  window.addEventListener('unhandledrejection', function (e) { send('Unhandled: ' + e.reason) })
  document.addEventListener('securitypolicyviolation', function (e) { send('CSP blocked ' + e.blockedURI + ' (' + e.violatedDirective + ')') })
  send('boot.js loaded from ' + location.href)
})()
