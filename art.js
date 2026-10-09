(function (root) {
  "use strict";

  var serial = 0;
  var instance = "pa" + Date.now().toString(36) + Math.random().toString(36).slice(2, 7);
  var kinds = ["stripe", "halftone", "voronoi", "radialdots", "vape"];

  function numeric(value, fallback, min, max) {
    var n = Number(value);
    return isFinite(n) ? Math.max(min, Math.min(max, n)) : fallback;
  }

  function silverDefs(id) {
    return '<defs>' +
      '<linearGradient id="' + id + '-metal" x1="0" y1="0" x2=".82" y2="1"><stop offset="0" stop-color="#f8fbff"/><stop offset=".16" stop-color="#cdd6df"/><stop offset=".36" stop-color="#778594"/><stop offset=".53" stop-color="#edf2f7"/><stop offset=".73" stop-color="#9ba7b5"/><stop offset="1" stop-color="#596777"/></linearGradient>' +
      '<linearGradient id="' + id + '-edge" x1="0" y1="0" x2=".1" y2="1"><stop offset="0" stop-color="#f8fbff"/><stop offset=".25" stop-color="#98a6b5"/><stop offset=".7" stop-color="#425062"/><stop offset="1" stop-color="#202b39"/></linearGradient>' +
      '<linearGradient id="' + id + '-bevel" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".95"/><stop offset=".34" stop-color="#fff" stop-opacity=".28"/><stop offset=".68" stop-color="#364456" stop-opacity=".16"/><stop offset="1" stop-color="#fff" stop-opacity=".62"/></linearGradient>' +
      '<linearGradient id="' + id + '-oil" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff1b9" stop-opacity=".9"/><stop offset=".35" stop-color="#e2c27b" stop-opacity=".82"/><stop offset="1" stop-color="#b58a43" stop-opacity=".9"/></linearGradient>' +
      '<linearGradient id="' + id + '-glass" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#edf5ff" stop-opacity=".12"/><stop offset=".18" stop-color="#fff" stop-opacity=".42"/><stop offset=".42" stop-color="#9db4cb" stop-opacity=".06"/><stop offset=".77" stop-color="#fff" stop-opacity=".23"/><stop offset="1" stop-color="#dcecff" stop-opacity=".09"/></linearGradient>' +
      '<radialGradient id="' + id + '-dot" cx="30%" cy="22%"><stop offset="0" stop-color="#fff"/><stop offset=".24" stop-color="#e7edf3"/><stop offset=".66" stop-color="#8996a5"/><stop offset="1" stop-color="#414e5e"/></radialGradient>' +
      '<filter id="' + id + '-shadow" x="-35%" y="-35%" width="170%" height="190%"><feGaussianBlur in="SourceAlpha" stdDeviation="9" result="blur"/><feOffset dy="13" result="offset"/><feComponentTransfer><feFuncA type="linear" slope=".55"/></feComponentTransfer><feMerge><feMergeNode/><feMergeNode in="SourceGraphic"/></feMerge></filter>' +
      '<filter id="' + id + '-soft-shadow" x="-30%" y="-30%" width="160%" height="170%"><feGaussianBlur in="SourceAlpha" stdDeviation="3" result="blur"/><feOffset dy="4" result="offset"/><feComponentTransfer><feFuncA type="linear" slope=".42"/></feComponentTransfer><feMerge><feMergeNode/><feMergeNode in="SourceGraphic"/></feMerge></filter>' +
      '</defs>';
  }

  function stripe(id, state) {
    var width = numeric(state.Width, 25, 14, 38);
    var d = [
      "M158 286 218 142",
      "M220 306 280 162",
      "M282 286 342 142"
    ];
    var bands = "";
    for (var i = 0; i < d.length; i += 1) {
      bands += '<path d="' + d[i] + '" fill="none" stroke="url(#' + id + '-edge)" stroke-width="' + (width + 5) + '" stroke-linecap="round"/>' +
        '<path d="' + d[i] + '" fill="none" stroke="url(#' + id + '-metal)" stroke-width="' + width + '" stroke-linecap="round"/>' +
        '<path d="' + d[i] + '" fill="none" stroke="#fff" stroke-opacity=".55" stroke-width="2.2" stroke-linecap="round" transform="translate(-1 -2)"/>';
    }
    return '<g filter="url(#' + id + '-shadow)">' + bands + '</g>' +
      '<path d="M148 305c50 35 148 39 211 4" fill="none" stroke="#fff" stroke-opacity=".1" stroke-width="2" stroke-linecap="round"/>';
  }

  function halftone(id, state) {
    var pitch = numeric(state.Pitch, 35, 24, 58);
    var count = Math.max(5, Math.min(9, Math.round(252 / pitch)));
    var span = (count - 1) * pitch;
    var start = 250 - span / 2;
    var dots = "";
    for (var row = 0; row < count; row += 1) {
      for (var col = 0; col < count; col += 1) {
        var dx = (col - (count - 1) / 2) / ((count - 1) / 2);
        var dy = (row - (count - 1) / 2) / ((count - 1) / 2);
        var falloff = Math.max(0.24, 1 - Math.sqrt(dx * dx + dy * dy) * 0.42);
        var radius = (6.5 + falloff * 6.5).toFixed(2);
        var x = start + col * pitch;
        var y = start + row * pitch;
        dots += '<circle cx="' + x.toFixed(2) + '" cy="' + y.toFixed(2) + '" r="' + radius + '" fill="url(#' + id + '-dot)" stroke="url(#' + id + '-edge)" stroke-width="1.2" opacity="' + (0.78 + falloff * 0.22).toFixed(2) + '"/>' +
          '<ellipse cx="' + (x - Number(radius) * 0.22).toFixed(2) + '" cy="' + (y - Number(radius) * 0.3).toFixed(2) + '" rx="' + (Number(radius) * 0.31).toFixed(2) + '" ry="' + (Number(radius) * 0.17).toFixed(2) + '" fill="#fff" opacity=".58"/>';
      }
    }
    return '<g transform="rotate(-18 250 220)" filter="url(#' + id + '-soft-shadow)">' + dots + '</g>';
  }

  function voronoi(id, state) {
    var cell = numeric(state.CellSize, 1, 0.78, 1.24);
    return '<g transform="translate(250 220) scale(' + cell + ') translate(-250 -220)" filter="url(#' + id + '-shadow)" stroke="url(#' + id + '-edge)" stroke-width="4" stroke-linejoin="round">' +
      '<path d="M170 114 221 93 254 118 246 171 198 190 164 160Z" fill="url(#' + id + '-metal)"/>' +
      '<path d="M221 93 279 91 309 124 291 169 246 171 254 118Z" fill="url(#' + id + '-metal)"/>' +
      '<path d="M279 91 330 112 347 155 309 184 291 169 309 124Z" fill="url(#' + id + '-metal)"/>' +
      '<path d="M164 160 198 190 207 239 167 261 137 228 140 184Z" fill="url(#' + id + '-metal)"/>' +
      '<path d="M198 190 246 171 291 169 278 222 238 252 207 239Z" fill="url(#' + id + '-metal)"/>' +
      '<path d="M291 169 309 184 347 155 363 204 331 241 278 222Z" fill="url(#' + id + '-metal)"/>' +
      '<path d="M167 261 207 239 238 252 244 299 199 323 160 302Z" fill="url(#' + id + '-metal)"/>' +
      '<path d="M238 252 278 222 331 241 327 289 286 316 244 299Z" fill="url(#' + id + '-metal)"/>' +
      '<path d="M331 241 363 204 376 253 352 291 327 289Z" fill="url(#' + id + '-metal)"/>' +
      '<path d="M170 118 218 99 242 117" fill="none" stroke="#fff" stroke-opacity=".72" stroke-width="3" stroke-linecap="round"/>' +
      '<path d="M246 177 284 175 298 187" fill="none" stroke="#fff" stroke-opacity=".6" stroke-width="3" stroke-linecap="round"/>' +
      '<path d="M205 246 235 259 239 289" fill="none" stroke="#fff" stroke-opacity=".55" stroke-width="3" stroke-linecap="round"/>' +
      '</g>';
  }

  function radialdots(id, state) {
    var outer = numeric(state.OuterR, 145, 112, 166);
    var radii = [outer * 0.43, outer * 0.72, outer];
    var dots = "";
    for (var ring = 0; ring < radii.length; ring += 1) {
      var total = [12, 18, 24][ring];
      var dotR = [8, 6.6, 5.4][ring];
      for (var n = 0; n < total; n += 1) {
        var angle = Math.PI * 2 * n / total + (ring % 2 ? Math.PI / total : 0);
        var x = 250 + Math.cos(angle) * radii[ring];
        var y = 220 + Math.sin(angle) * radii[ring];
        dots += '<circle cx="' + x.toFixed(2) + '" cy="' + y.toFixed(2) + '" r="' + dotR + '" fill="url(#' + id + '-dot)" stroke="url(#' + id + '-edge)" stroke-width="1"/>' +
          '<circle cx="' + (x - dotR * 0.28).toFixed(2) + '" cy="' + (y - dotR * 0.32).toFixed(2) + '" r="' + (dotR * 0.24).toFixed(2) + '" fill="#fff" opacity=".72"/>';
      }
    }
    return '<g filter="url(#' + id + '-soft-shadow)">' + dots +
      '<circle cx="250" cy="220" r="20" fill="url(#' + id + '-edge)"/><circle cx="250" cy="217" r="16" fill="url(#' + id + '-metal)" stroke="#fff" stroke-opacity=".55" stroke-width="2"/>' +
      '<circle cx="245" cy="212" r="4" fill="#fff" opacity=".62"/></g>';
  }

  function vape(id) {
    return '<g filter="url(#' + id + '-shadow)">' +
      // metal neck and top rim
      '<path d="M202 96h96v28h-96z" fill="url(#' + id + '-metal)" stroke="url(#' + id + '-edge)" stroke-width="4"/>' +
      '<path d="M194 119h112v18H194z" fill="url(#' + id + '-edge)" stroke="#eaf1f8" stroke-opacity=".55" stroke-width="2"/>' +
      // transparent reservoir wall
      '<path d="M198 137h104v192q-52 32-104 0z" fill="url(#' + id + '-glass)" stroke="url(#' + id + '-edge)" stroke-width="5"/>' +
      // warm oil inside, with a gently curved meniscus
      '<path d="M205 221q45-11 90 0v101q-45 26-90 0z" fill="url(#' + id + '-oil)" stroke="#f7e5b5" stroke-opacity=".7" stroke-width="2"/>' +
      '<path d="M207 222q43-10 86 0" fill="none" stroke="#fff5d2" stroke-width="3" stroke-linecap="round" opacity=".9"/>' +
      // central atomizer core and perforated coil windows
      '<rect x="231" y="158" width="38" height="160" rx="13" fill="url(#' + id + '-edge)" stroke="#f1f5f9" stroke-width="2.4"/>' +
      '<rect x="238" y="174" width="24" height="128" rx="9" fill="url(#' + id + '-metal)" stroke="#fff" stroke-opacity=".55" stroke-width="1.4"/>' +
      '<path d="M242 188c17 7-17 13 0 20s-17 13 0 20-17 13 0 20-17 13 0 20-17 13 0 20" fill="none" stroke="#384656" stroke-width="4" stroke-linecap="round"/>' +
      '<path d="M258 188c-17 7 17 13 0 20s17 13 0 20 17 13 0 20-17 13 0 20 17 13 0 20" fill="none" stroke="#f7fafc" stroke-opacity=".94" stroke-width="3" stroke-linecap="round"/>' +
      // crisp refracted highlights and bottom cap
      '<path d="M207 151v151" fill="none" stroke="#fff" stroke-opacity=".84" stroke-width="4" stroke-linecap="round"/>' +
      '<path d="M293 154v144" fill="none" stroke="#fff" stroke-opacity=".32" stroke-width="3" stroke-linecap="round"/>' +
      '<path d="M194 331h112v20H194z" fill="url(#' + id + '-metal)" stroke="url(#' + id + '-edge)" stroke-width="4"/>' +
      '<path d="M209 356h82" fill="none" stroke="#e6edf4" stroke-opacity=".65" stroke-width="4" stroke-linecap="round"/>' +
      '</g>';
  }

  function render(kind, state) {
    var selected = kinds.indexOf(kind) >= 0 ? kind : "stripe";
    var opts = state || {};
    var id = instance + "-" + (++serial);
    var shape;
    if (selected === "stripe") shape = stripe(id, opts);
    else if (selected === "halftone") shape = halftone(id, opts);
    else if (selected === "voronoi") shape = voronoi(id, opts);
    else if (selected === "radialdots") shape = radialdots(id, opts);
    else shape = vape(id);

    return '<svg xmlns="http://www.w3.org/2000/svg" width="500" height="440" viewBox="0 0 500 440" role="img" aria-label="' + selected + ' material preview" focusable="false">' +
      silverDefs(id) +
      '<ellipse cx="250" cy="365" rx="142" ry="18" fill="#000" opacity=".2" filter="url(#' + id + '-soft-shadow)"/>' +
      shape +
      '</svg>';
  }

  root.PreviewArt = { render: render };
}(window));
