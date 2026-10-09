(function (root) {
  "use strict";

  var serial = 0;
  var instance = "gi" + Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
  var names = ["stripe", "halftone", "voronoi", "radialdots", "vape", "app"];
  var palette = {
    stripe: "#087f96",
    halftone: "#4669cf",
    voronoi: "#c08a36",
    radialdots: "#8b69bc",
    vape: "#2c97d3",
    app: "#263b59"
  };

  function symbolFor(kind, id) {
    var grad = "url(#" + id + "-symbol)";
    var shadow = "url(#" + id + "-symbol-shadow)";

    if (kind === "stripe") {
      return '<g fill="none" stroke="' + grad + '" stroke-width="8.5" stroke-linecap="round" filter="' + shadow + '">' +
        '<path d="M43 78 56 51"/><path d="M60 83 73 56"/><path d="M77 78 90 51"/>' +
        '</g><path d="M39 83 51 59" fill="none" stroke="#fff" stroke-opacity=".72" stroke-width="1.6" stroke-linecap="round"/>';
    }

    if (kind === "halftone") {
      var dots = "";
      var xs = [45, 64, 83];
      var ys = [45, 64, 83];
      var radii = [[4.4, 5.3, 4.1], [5.4, 7.2, 5.4], [4.2, 5.3, 4.4]];
      for (var row = 0; row < 3; row += 1) {
        for (var col = 0; col < 3; col += 1) {
          var opacity = (0.72 + ((row + col) % 3) * 0.09).toFixed(2);
          dots += '<circle cx="' + xs[col] + '" cy="' + ys[row] + '" r="' + radii[row][col] + '" fill="' + grad + '" opacity="' + opacity + '"/>';
          dots += '<circle cx="' + (xs[col] - 1.15) + '" cy="' + (ys[row] - 1.35) + '" r="' + (radii[row][col] * 0.25).toFixed(1) + '" fill="#fff" opacity=".5"/>';
        }
      }
      return '<g filter="' + shadow + '">' + dots + '</g>';
    }

    if (kind === "voronoi") {
      return '<g filter="' + shadow + '" stroke="#fff" stroke-opacity=".83" stroke-width="2.2" stroke-linejoin="round">' +
        '<path d="M64 34 78 42 75 56 64 62 52 55 51 42Z" fill="' + grad + '"/>' +
        '<path d="M51 42 52 55 43 66 31 61 35 46Z" fill="#d7a64f" fill-opacity=".9"/>' +
        '<path d="M52 55 64 62 63 78 49 85 40 76 43 66Z" fill="#b77f2d" fill-opacity=".93"/>' +
        '<path d="M64 62 75 56 88 64 84 78 73 86 63 78Z" fill="#d3a452" fill-opacity=".96"/>' +
        '<path d="M78 42 91 47 96 60 88 64 75 56Z" fill="#c08a36" fill-opacity=".9"/>' +
        '<path d="M51 42 52 55 43 66 35 62 35 46Z" fill="#fff" fill-opacity=".16" stroke="none"/>' +
        '</g>' +
        '<path d="M58 39 65 35" fill="none" stroke="#fff" stroke-opacity=".85" stroke-width="2" stroke-linecap="round"/>';
    }

    if (kind === "radialdots") {
      var rings = "";
      var cx = 64;
      var cy = 64;
      var angles = 12;
      var ringSizes = [14, 25, 37];
      for (var ring = 0; ring < ringSizes.length; ring += 1) {
        for (var n = 0; n < angles; n += 1) {
          var a = (Math.PI * 2 * n / angles) + (ring % 2 ? Math.PI / 12 : 0);
          var radius = ringSizes[ring];
          var dotRadius = ring === 0 ? 3.1 : (ring === 1 ? 2.65 : 2.25);
          var dx = (cx + Math.cos(a) * radius).toFixed(2);
          var dy = (cy + Math.sin(a) * radius).toFixed(2);
          var dotOpacity = (0.66 + 0.28 * (0.5 + 0.5 * Math.cos(a))).toFixed(2);
          rings += '<circle cx="' + dx + '" cy="' + dy + '" r="' + dotRadius + '" fill="' + grad + '" opacity="' + dotOpacity + '"/>';
        }
      }
      return '<g filter="' + shadow + '">' + rings + '<circle cx="64" cy="64" r="5.1" fill="' + grad + '"/><circle cx="62.3" cy="62.2" r="1.5" fill="#fff" opacity=".8"/></g>';
    }

    if (kind === "vape") {
      return '<g filter="' + shadow + '">' +
        '<path d="M64 34c-7.8 12.8-22.5 27.2-22.5 40.2a22.5 22.5 0 0 0 45 0C86.5 61.2 71.8 46.8 64 34Z" fill="' + grad + '" stroke="#fff" stroke-opacity=".9" stroke-width="2.4"/>' +
        '<path d="M54.3 66.5c-2.8 5-4.2 8.1-3 12.1 1.1 3.5 3.8 5.8 7.6 6.6" fill="none" stroke="#fff" stroke-opacity=".9" stroke-width="3.2" stroke-linecap="round"/>' +
        '<path d="M70.5 52.5c4 5.2 7 10.2 8 14.7" fill="none" stroke="#fff" stroke-opacity=".42" stroke-width="2.2" stroke-linecap="round"/>' +
        '</g>';
    }

    // The app mark aggregates five distinct tool-nodes around a shared core.
    return '<g filter="' + shadow + '" stroke="#fff" stroke-opacity=".82" stroke-width="1.5">' +
      '<path d="M51 52 64 64 77 52M51 76 64 64 77 76" fill="none" stroke="#fff" stroke-opacity=".58" stroke-width="2.8" stroke-linecap="round" stroke-linejoin="round"/>' +
      '<rect x="41" y="40" width="19" height="19" rx="7" fill="#58b8c6"/><rect x="68" y="40" width="19" height="19" rx="7" fill="#8d77cb"/>' +
      '<rect x="41" y="68" width="19" height="19" rx="7" fill="#d7a452"/><rect x="68" y="68" width="19" height="19" rx="7" fill="#4d9ed2"/>' +
      '<circle cx="64" cy="64" r="9.5" fill="#263b59" stroke-width="2.6"/>' +
      '<path d="M60.8 64h6.4M64 60.8v6.4" stroke="#fff" stroke-width="2.1" stroke-linecap="round"/>' +
      '<path d="M45.5 45.5 51 43.8M72.5 45.5 78 43.8M45.5 73.5 51 71.8M72.5 73.5 78 71.8" stroke="#fff" stroke-width="1.1" stroke-linecap="round" opacity=".7"/>' +
      '</g>';
  }

  function svg(kind, size) {
    var selected = Object.prototype.hasOwnProperty.call(palette, kind) ? kind : "app";
    var px = Number(size === undefined ? 64 : size);
    if (!isFinite(px) || px <= 0) px = 64;
    var id = instance + "-" + (++serial);
    var accent = palette[selected];
    var isApp = selected === "app";
    var cardFill = isApp ? "#263b59" : "#eaf5ff";
    var cardOpacity = isApp ? ".88" : ".37";
    var symbol = symbolFor(selected, id);

    return '<svg xmlns="http://www.w3.org/2000/svg" width="' + px + '" height="' + px + '" viewBox="0 0 128 128" role="img" aria-label="' + selected + ' glass icon" focusable="false">' +
      '<defs>' +
        '<linearGradient id="' + id + '-glass" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fff" stop-opacity="' + (isApp ? '.24' : '.76') + '"/><stop offset=".48" stop-color="' + cardFill + '" stop-opacity="' + cardOpacity + '"/><stop offset="1" stop-color="' + accent + '" stop-opacity="' + (isApp ? '.31' : '.13') + '"/></linearGradient>' +
        '<linearGradient id="' + id + '-edge" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#fff" stop-opacity=".98"/><stop offset=".48" stop-color="#fff" stop-opacity=".54"/><stop offset="1" stop-color="' + accent + '" stop-opacity=".72"/></linearGradient>' +
        '<linearGradient id="' + id + '-symbol" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="' + (isApp ? '#4d769d' : '#fff') + '"/><stop offset=".32" stop-color="' + accent + '"/><stop offset="1" stop-color="' + accent + '" stop-opacity=".77"/></linearGradient>' +
        '<linearGradient id="' + id + '-sheen" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#fff" stop-opacity="0"/><stop offset=".5" stop-color="#fff" stop-opacity=".84"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient>' +
        '<filter id="' + id + '-card-shadow" x="-30%" y="-30%" width="160%" height="170%"><feGaussianBlur in="SourceAlpha" stdDeviation="5" result="blur"/><feOffset dy="3" result="offset"/><feColorMatrix in="offset" type="matrix" values="0 0 0 0 .08 0 0 0 0 .18 0 0 0 0 .28 0 0 0 .2 0" result="shadow"/><feMerge><feMergeNode in="shadow"/><feMergeNode in="SourceGraphic"/></feMerge></filter>' +
        '<filter id="' + id + '-symbol-shadow" x="-30%" y="-30%" width="160%" height="160%"><feGaussianBlur in="SourceAlpha" stdDeviation="2" result="blur"/><feOffset dy="2" result="offset"/><feComponentTransfer><feFuncA type="linear" slope=".22"/></feComponentTransfer><feMerge><feMergeNode/><feMergeNode in="SourceGraphic"/></feMerge></filter>' +
      '</defs>' +
      '<rect x="6" y="6" width="116" height="116" rx="33" fill="url(#' + id + '-glass)" stroke="url(#' + id + '-edge)" stroke-width="1.7" filter="url(#' + id + '-card-shadow)"/>' +
      '<path d="M21 43c2.1-12.8 9.8-20.2 21.8-21.1h42.4c12 .9 19.7 8.3 21.8 21.1" fill="none" stroke="url(#' + id + '-sheen)" stroke-width="2.1" stroke-linecap="round" opacity=".92"/>' +
      '<path d="M18 49c.8-5.3 2.1-9.1 4.2-12" fill="none" stroke="#fff" stroke-opacity=".74" stroke-width="1.4" stroke-linecap="round"/>' +
      '<path d="M18 79c3.6 17.5 14.2 28.8 31.7 31.3" fill="none" stroke="#fff" stroke-opacity=".27" stroke-width="1.2" stroke-linecap="round"/>' +
      '<circle cx="95" cy="34" r="17" fill="' + accent + '" opacity=".045"/>' +
      symbol +
      '</svg>';
  }

  root.GlassIcons = { svg: svg, names: names.slice() };
}(window));
