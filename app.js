/* IVAN CENTER interaction preview. It never calls Rhino or changes the installation. */
(function (window, document) {
  "use strict";

  var data = window.PanelData || {};
  var kinds = Object.keys(data);
  var iconApi = window.GlassIcons;
  var pluginAccents = { stripe: "#087f96", halftone: "#4669cf", voronoi: "#c08a36", radialdots: "#8b69bc", vape: "#2c97d3" };
  var $ = function (id) { return document.getElementById(id); };
  var state = {
    activePanel: null,
    targetFaces: 2,
    values: {},
    picks: {},
    installed: {},
    dark: false,
    minimized: false,
    compact: false,
    backendConnected: false
  };
  var panelTrigger = null;
  var iconTrigger = null;
  var closeTimer = 0;
  var toastTimer = 0;
  var installTimer = 0;
  var pendingRemoval = null;
  var logEntries = ["[UI PREVIEW] 五个插件已载入设计预览。", "[UI PREVIEW] 不执行安装、卸载或 Rhino 命令。"];

  var glyphPaths = {
    grid: '<rect x="3.5" y="3.5" width="7" height="7" rx="1.3"/><rect x="13.5" y="3.5" width="7" height="7" rx="1.3"/><rect x="3.5" y="13.5" width="7" height="7" rx="1.3"/><rect x="13.5" y="13.5" width="7" height="7" rx="1.3"/>',
    minus: '<path d="M5 12h14"/>',
    square: '<rect x="5" y="5" width="14" height="14" rx="2"/>',
    close: '<path d="m6 6 12 12M18 6 6 18"/>',
    download: '<path d="M12 3v11m0 0 4-4m-4 4-4-4M4 17v3h16v-3"/>',
    search: '<circle cx="10.8" cy="10.8" r="6.3"/><path d="m16 16 4 4"/>',
    folder: '<path d="M3 7.5a2 2 0 0 1 2-2h5l2 2h7a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><path d="M3.5 10h17"/>',
    terminal: '<path d="m5 7 4 4-4 4m7 1h7"/>',
    chevron: '<path d="m7 9 5 5 5-5"/>',
    arrow: '<path d="M4 12h15m-6-6 6 6-6 6"/>',
    check: '<path d="m5 12 4.2 4.2L19 6.5"/>',
    target: '<circle cx="12" cy="12" r="8.5"/><circle cx="12" cy="12" r="4.2"/><circle cx="12" cy="12" r=".9" fill="currentColor" stroke="none"/>',
    trash: '<path d="M4 7h16M9 7V4h6v3m3 0-.8 13H6.8L6 7m4 4v5m4-5v5"/>'
  };

  function escapeHtml(value) {
    return String(value == null ? "" : value).replace(/[&<>"']/g, function (c) {
      return ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c];
    });
  }

  function glyph(name) {
    var paths = glyphPaths[name] || glyphPaths.check;
    return '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">' + paths + "</svg>";
  }

  function icon(kind, size) {
    try { return iconApi && iconApi.svg ? iconApi.svg(kind, size || 48) : ""; }
    catch (_) { return ""; }
  }

  function installGlyphs(root) {
    (root || document).querySelectorAll("[data-glyph]").forEach(function (node) {
      node.innerHTML = glyph(node.getAttribute("data-glyph"));
    });
  }

  function initializeState() {
    kinds.forEach(function (kind) {
      var plugin = data[kind];
      var values = {};
      (plugin.groups || []).forEach(function (group) {
        (group.controls || []).forEach(function (control) {
          if (Object.prototype.hasOwnProperty.call(control, "value")) values[control.key] = control.value;
        });
      });
      state.values[kind] = values;
      state.picks[kind] = {};
      (plugin.picks || []).forEach(function (pick) { state.picks[kind][pick] = false; });
      state.installed[kind] = true;
    });
  }

  function button(label, className, attrs) {
    return '<button type="button" class="' + className + '" ' + (attrs || "") + ">" + label + "</button>";
  }

  function renderPanelNav() {
    var nav = $("panelNav");
    nav.innerHTML = kinds.map(function (kind) {
      var plugin = data[kind];
      return '<button type="button" class="side-panel-button" data-open="' + escapeHtml(kind) + '" aria-label="预览 ' + escapeHtml(plugin.name) + '"><span class="mini-icon">' + icon(kind, 40) + '</span><span>' + escapeHtml(plugin.name) + "</span></button>";
    }).join("");
  }

  function renderPluginList(query) {
    var list = $("pluginList");
    var q = (query || "").trim().toLocaleLowerCase();
    var visible = 0;
    list.innerHTML = kinds.map(function (kind) {
      var plugin = data[kind];
      var haystack = (plugin.name + " " + plugin.english + " " + plugin.description + " " + kind).toLocaleLowerCase();
      var match = !q || haystack.indexOf(q) >= 0;
      if (match) visible++;
      var installed = state.installed[kind];
      return '<article class="plugin-row' + (match ? "" : " is-filtered") + '" data-kind="' + escapeHtml(kind) + '"' + (match ? "" : ' hidden') + '>' +
        '<span class="plugin-icon">' + icon(kind, 64) + '</span>' +
        '<div class="plugin-copy"><div class="title-line"><h2>' + escapeHtml(plugin.name) + '</h2><span class="installed-tag' + (installed ? "" : " is-missing") + '">' + (installed ? "已安装" : "未安装") + '</span></div>' +
        '<div class="plugin-meta">' + escapeHtml(plugin.description) + '</div></div>' +
        '<div class="plugin-actions"><button type="button" class="row-preview" data-action="preview" data-kind="' + kind + '"><span class="ui-icon" data-glyph="target"></span>面板预览</button>' +
        '<button type="button" class="row-update" data-action="update" data-kind="' + kind + '"><span class="ui-icon" data-glyph="download"></span>' + (installed ? "更新" : "安装") + '</button>' +
        '<button type="button" class="row-remove" data-action="remove" data-kind="' + kind + '" aria-label="卸载 ' + escapeHtml(plugin.name) + '"><span class="ui-icon" data-glyph="trash"></span></button></div></article>';
    }).join("");
    $("emptySearch").hidden = visible !== 0;
    installGlyphs(list);
    updateOverallStatus();
  }

  function renderIconFamily() {
    var family = $("iconFamily");
    var names = kinds;
    family.innerHTML = names.map(function (kind) {
      var label = data[kind] ? data[kind].name : (kind === "app" ? "IVAN CENTER" : kind);
      return '<div class="family-item">' + icon(kind, 94) + '<strong>' + escapeHtml(label) + '</strong><span>' + escapeHtml(kind) + '</span></div>';
    }).join("");
  }

  function renderTileSymbol(index, family) {
    var shapes = family === "array" ? [
      '<path d="M5 5h6v6H5zM15 5h6v6h-6zM5 15h6v6H5zM15 15h6v6h-6z"/>',
      '<path d="M4 5h6v6H4zM14 5h6v6h-6zM9 14h6v6H9z"/>',
      '<path d="m12 4 7 4v8l-7 4-7-4V8zM5 8l7 4 7-4M12 12v8"/>',
      '<circle cx="12" cy="12" r="3"/><circle cx="12" cy="12" r="7"/><circle cx="12" cy="12" r="10"/>',
      '<path d="M12 12 19 8M12 12l-2-8M12 12l-8 1M12 12l2 8M12 12l8-1"/>',
      '<circle cx="6" cy="7" r="1.5"/><circle cx="15" cy="6" r="1.5"/><circle cx="11" cy="13" r="1.5"/><circle cx="19" cy="16" r="1.5"/><circle cx="5" cy="19" r="1.5"/>'
    ] : [
      '<circle cx="12" cy="12" r="8"/>',
      '<path d="m12 4 8 14H4z"/>',
      '<path d="M6 6h12v12H6z"/>',
      '<path d="m12 3 8 5v8l-8 5-8-5V8z"/>',
      '<circle cx="12" cy="12" r="8"/><path d="M8 5h8v14H8z"/>'
    ];
    var shape = shapes[index % shapes.length];
    return '<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round" stroke-linecap="round">' + shape + "</svg>";
  }

  function renderControl(kind, control) {
    var key = escapeHtml(control.key);
    var current = state.values[kind][control.key];
    var currentValue = current == null ? control.value : current;
    var condition = control.showIf ? ' data-show-key="' + escapeHtml(control.showIf.key) + '"' : "";
    var holderStart = '<div class="control-item" data-control="' + key + '"' + condition + '>';
    var label = '<label class="control-label" for="range-' + kind + '-' + key + '">' + escapeHtml(control.label || "") + (control.unit ? '<span class="unit">' + escapeHtml(control.unit) + '</span>' : "") + '</label>';

    if (control.type === "slider") {
      var unit = control.unit ? '<span class="unit">' + escapeHtml(control.unit) + '</span>' : "";
      var rangeVal = Number(currentValue);
      var visualVal = Math.min(Number(control.max), Math.max(Number(control.min), rangeVal));
      var fill = sliderFill(visualVal, Number(control.min), Number(control.max));
      return holderStart + '<div class="param-row"><label class="param-label" for="range-' + kind + '-' + key + '">' + escapeHtml(control.label) + '</label>' +
        '<input class="param-range range" id="range-' + kind + '-' + key + '" type="range" data-key="' + key + '" min="' + control.min + '" max="' + control.max + '" step="' + control.step + '" value="' + visualVal + '" style="--fill:' + fill + '%" aria-label="' + escapeHtml(control.label) + '">' +
        '<input class="param-number" type="number" data-number="' + key + '" min="' + control.min + '" max="' + (control.hardMax == null ? control.max : control.hardMax) + '" step="' + control.step + '" value="' + escapeHtml(formatControlValue(kind, control.key, currentValue)) + '" aria-label="' + escapeHtml(control.label) + ' 数值">' + unit + '</div>' +
        '<div class="slider-condition" hidden></div></div>';
    }
    if (control.type === "toggle") {
      return holderStart + '<label class="toggle-row"><span>' + escapeHtml(control.label) + '</span><input class="switch" type="checkbox" data-key="' + key + '"' + (currentValue ? " checked" : "") + ' aria-label="' + escapeHtml(control.label) + '"></label></div>';
    }
    if (control.type === "segments" || control.type === "tiles") {
      var options = control.options || [];
      var optionsHtml = options.map(function (option, index) {
        var chosen = Number(currentValue) === index;
        var cls = control.type === "tiles" ? "tile" : "segmented-option";
        var content = control.type === "tiles" ? renderTileSymbol(index, kind === "halftone" && control.key === "ArrayMode" ? "array" : "shape") + '<span>' + escapeHtml(option) + '</span>' : escapeHtml(option);
        return '<button type="button" class="' + cls + (chosen ? " selected" : "") + '" data-choice="' + key + '" data-index="' + index + '" aria-pressed="' + (chosen ? "true" : "false") + '">' + content + '</button>';
      }).join("");
      var wrapperClass = control.type === "tiles" ? "tiles-row" : "segmented-row";
      var choiceClass = control.type === "tiles" ? "tiles" : "segmented";
      return holderStart + '<div class="' + wrapperClass + '"><span class="control-label">' + escapeHtml(control.label || "") + '</span><div class="' + choiceClass + '" data-choice-group="' + key + '">' + optionsHtml + '</div></div></div>';
    }
    if (control.type === "action") {
      return holderStart + '<button type="button" class="inline-action" data-action="' + key + '">' + (key === "clearGradient" ? glyph("close") : key === "chooseCenter" ? glyph("target") : glyph("check")) + '<span>' + escapeHtml(control.label) + '</span></button></div>';
    }
    if (control.type === "note") {
      var noteText = noteValue(kind, control.key, control.text || "");
      return holderStart + '<div class="param-note" data-note="' + key + '">' + escapeHtml(noteText) + '</div></div>';
    }
    if (control.type === "result") {
      var valueText = kind === "vape" && control.key === "capacity" ? "— mL" : (control.text || "—");
      return holderStart + '<div class="result-control" data-result="' + key + '"><span class="result-label">' + escapeHtml(control.label) + '</span><strong>' + escapeHtml(valueText) + '</strong><span class="result-placeholder">' + (kind === "vape" ? "未选择物件 · 未连接 Rhino" : escapeHtml(control.text || "")) + '</span></div></div>';
    }
    return holderStart + label + '</div>';
  }

  function noteValue(kind, key, fallback) {
    if (key === "faceHint") {
      var faceCount = state.targetFaces;
      var onlyFace = state.values[kind] && state.values[kind].OnlyFace;
      if (faceCount <= 1) return "当前对象只有 1 个面，两种设置效果相同";
      if (Number(onlyFace) === 0) return kind === "voronoi" ? "只处理点选的那一个面" : "只生成点选的那一个面";
      return kind === "voronoi" ? "处理整个多重曲面的所有面（" + faceCount + " 个面，接缝处胞元连续）" : "生成整个多重曲面的所有面（" + faceCount + " 个面）";
    }
    if (key === "centerInfo") {
      var pts = state.values.halftone.CenterPoints;
      return Array.isArray(pts) && pts.length ? "圆心：已指定（点阵从圆心向外扩散）" : fallback;
    }
    if (key === "gradientHint" && state.picks.voronoi && state.picks.voronoi.gradient) {
      var amount = Number(state.values.voronoi.GradientAmount || 0);
      return amount >= 0 ? "已设置渐变参考物件；越往外胞元越小" : "已设置渐变参考物件；越往外胞元越大";
    }
    return fallback;
  }

  function renderPanelBody(kind) {
    var plugin = data[kind];
    var pickControls = (plugin.picks || []).map(function (pick) {
      var picked = !!state.picks[kind][pick];
      var label = pick === "target" ? "选择物件" : "选择渐变物件";
      return '<button type="button" class="pick-button' + (picked ? " picked" : "") + '" data-pick="' + escapeHtml(pick) + '" aria-pressed="' + (picked ? "true" : "false") + '"><span class="ui-icon" data-glyph="' + (picked ? "check" : "target") + '"></span><span>' + label + '</span><small>' + (picked ? "已选择" : "点击模拟选择") + '</small></button>';
    }).join("");
    var pickSection = plugin.picks && plugin.picks.length ? '<div class="pick-section"><div class="pick-buttons">' + pickControls + '</div></div>' : "";
    var groups = (plugin.groups || []).map(function (group) {
      var controls = (group.controls || []).map(function (control) { return renderControl(kind, control); }).join("");
      return '<section class="control-group param-group" data-group="' + escapeHtml(group.id) + '"' + (group.showIf ? ' data-show-key="' + escapeHtml(group.showIf.key) + '"' : "") + '><h3>' + escapeHtml(group.title) + '</h3><div class="control-group-body">' + controls + '</div></section>';
    }).join("");
    $("panelBody").innerHTML = pickSection + groups;
    installGlyphs($("panelBody"));
    applyConditions(kind);
  }

  function conditionPass(condition, kind) {
    if (!condition) return true;
    var value = conditionValue(kind, condition.key);
    if (Object.prototype.hasOwnProperty.call(condition, "equals")) return value === condition.equals;
    if (Object.prototype.hasOwnProperty.call(condition, "gt")) {
      var comparable = Array.isArray(value) ? value.length : Number(value);
      return comparable > Number(condition.gt);
    }
    if (Object.prototype.hasOwnProperty.call(condition, "in")) return condition.in.indexOf(value) >= 0;
    return true;
  }

  function conditionValue(kind, key) {
    if (key === "targetFaces") return state.targetFaces;
    var value = state.values[kind] ? state.values[kind][key] : undefined;
    // CupMode is a two-segment index in the preview but a bool in the actual setting.
    if (key === "CupMode") return Number(value) === 0;
    if (key === "CenterPoints") return state.values[kind].CenterPoints || null;
    return value;
  }

  function applyConditions(kind) {
    var plugin = data[kind];
    if (!plugin) return;
    Array.prototype.forEach.call($("panelBody").querySelectorAll(".control-group"), function (groupNode, groupIndex) {
      var groupData = plugin.groups[groupIndex];
      groupNode.hidden = !conditionPass(groupData.showIf, kind);
    });
    (plugin.groups || []).forEach(function (group) {
      (group.controls || []).forEach(function (control) {
        var node = $("panelBody").querySelector('[data-control="' + cssEscape(control.key) + '"]');
        if (!node) return;
        node.hidden = !conditionPass(control.showIf, kind);
        var disabled = !!(control.disabledIf && conditionPass(control.disabledIf, kind));
        node.querySelectorAll("input,button").forEach(function (input) { input.disabled = disabled; });
        if (control.type === "segments" || control.type === "tiles") {
          var selected = Number(state.values[kind][control.key]);
          node.querySelectorAll("[data-choice]").forEach(function (choice) {
            var active = Number(choice.getAttribute("data-index")) === selected;
            choice.classList.toggle("selected", active);
            choice.setAttribute("aria-pressed", active ? "true" : "false");
          });
        }
        if (control.type === "note") {
          var note = node.querySelector("[data-note]");
          if (note) note.textContent = noteValue(kind, control.key, control.text || "");
        }
        if (control.type === "result" && kind === "vape" && control.key === "capacity") {
          var result = node.querySelector(".result-placeholder");
          if (result) result.textContent = state.picks.vape.target ? "未连接 Rhino · 未执行容量计算" : "未选择物件 · 未连接 Rhino";
        }
      });
    });
    updatePickButtons(kind);
    updatePanelFooter(kind);
  }

  function cssEscape(value) {
    return String(value).replace(/\\/g, "\\\\").replace(/"/g, '\\"');
  }

  function updatePickButtons(kind) {
    $("panelBody").querySelectorAll("[data-pick]").forEach(function (button) {
      var pick = button.getAttribute("data-pick");
      var picked = !!state.picks[kind][pick];
      button.classList.toggle("picked", picked);
      button.setAttribute("aria-pressed", picked ? "true" : "false");
      var hint = button.querySelector("small");
      if (hint) hint.textContent = picked ? "已选择" : "点击模拟选择";
      var iconNode = button.querySelector(".ui-icon");
      if (iconNode) {
        iconNode.setAttribute("data-glyph", picked ? "check" : "target");
        iconNode.innerHTML = glyph(picked ? "check" : "target");
      }
    });
  }

  function needsTarget(kind) {
    return (data[kind].picks || []).indexOf("target") >= 0;
  }

  function updatePanelFooter(kind) {
    var selected = !!(state.picks[kind] && state.picks[kind].target);
    var status = $("panelStatus");
    if (kind === "radialdots") status.textContent = "预览样例 · 无需选择物件 · 不连接 Rhino";
    else if (selected) status.textContent = "示例物件已选择 · " + state.targetFaces + " 面 · UI 预览";
    else status.textContent = "未选择物件 · UI 预览";
    var primary = $("generateButton");
    var plugin = data[kind];
    primary.disabled = needsTarget(kind) && !selected;
    primary.innerHTML = escapeHtml(plugin.primaryLabel || "生成") + '<span class="ui-icon" data-glyph="arrow"></span>';
    installGlyphs(primary);
    $("cancelPanel").textContent = plugin.secondaryLabel || "取消";
  }

  function updateViewportArt(kind, force) {
    if (!kind) return;
    if (!force && state.values[kind] && state.values[kind].LivePreview === false) return;
    var art = $("viewportArt");
    var rendered = "";
    try {
      if (window.PreviewArt && typeof window.PreviewArt.render === "function") {
        rendered = window.PreviewArt.render(kind, Object.assign({}, state.values[kind], { targetFaces: state.targetFaces, targetSelected: !!state.picks[kind].target }));
      }
    } catch (_) { rendered = ""; }
    if (rendered && typeof rendered === "string") art.innerHTML = rendered;
    else if (rendered && rendered.nodeType) { art.innerHTML = ""; art.appendChild(rendered); }
    else art.innerHTML = icon(kind, 200) || "";
  }

  function setTheme(theme) {
    state.dark = theme === "dark";
    document.body.classList.toggle("dark", state.dark);
    document.querySelectorAll("[data-theme]").forEach(function (button) {
      var selected = button.getAttribute("data-theme") === (state.dark ? "dark" : "light");
      button.setAttribute("aria-pressed", selected ? "true" : "false");
    });
    return state.dark;
  }

  function openPanel(kind, trigger) {
    if (!data[kind]) return false;
    window.clearTimeout(closeTimer);
    panelTrigger = trigger || document.activeElement;
    state.activePanel = kind;
    var plugin = data[kind];
    $("panelTitle").textContent = plugin.name;
    $("panelOverlay").querySelector(".preview-shell").dataset.kind = kind;
    $("panelOverlay").querySelector(".parameter-panel").style.setProperty("--plugin-accent", pluginAccents[kind] || "#4776d0");
    $("panelIndex").textContent = String(kinds.indexOf(kind) + 1).padStart(2, "0") + " / " + String(kinds.length).padStart(2, "0");
    $("panelHeaderIcon").innerHTML = icon(kind, 50);
    $("visualEnglish").textContent = plugin.english;
    $("visualTitle").textContent = plugin.name;
    $("visualDescription").textContent = plugin.description;
    renderPanelBody(kind);
    updatePanelFooter(kind);
    $("fixtureButton").textContent = "示例：多重曲面 · " + state.targetFaces + " 面";
    updateViewportArt(kind, true);
    $("panelOverlay").hidden = false;
    window.requestAnimationFrame(function () { $("panelOverlay").classList.add("is-open"); });
    window.setTimeout(function () { $("closePanel").focus(); }, 40);
    return true;
  }

  function closePanel() {
    var overlay = $("panelOverlay");
    if (overlay.hidden) return;
    overlay.classList.remove("is-open");
    window.clearTimeout(closeTimer);
    closeTimer = window.setTimeout(function () {
      overlay.hidden = true;
      if (panelTrigger && typeof panelTrigger.focus === "function") panelTrigger.focus();
      panelTrigger = null;
      state.activePanel = null;
    }, 150);
  }

  function openOverlay(id, trigger) {
    var overlay = $(id);
    if (!overlay) return;
    if (id === "iconsOverlay") iconTrigger = trigger || document.activeElement;
    overlay.hidden = false;
    window.requestAnimationFrame(function () { overlay.classList.add("is-open"); });
    var focusable = overlay.querySelector("button, input, [tabindex]:not([tabindex='-1'])");
    if (focusable) window.setTimeout(function () { focusable.focus(); }, 30);
  }

  function closeOverlay(id) {
    var overlay = $(id);
    if (!overlay || overlay.hidden) return;
    overlay.classList.remove("is-open");
    window.setTimeout(function () {
      overlay.hidden = true;
      if (id === "iconsOverlay" && iconTrigger && typeof iconTrigger.focus === "function") iconTrigger.focus();
    }, 150);
  }

  function showToast(message) {
    var toast = $("toast");
    window.clearTimeout(toastTimer);
    toast.textContent = message;
    toast.hidden = false;
    toast.classList.add("is-visible");
    toastTimer = window.setTimeout(function () {
      toast.classList.remove("is-visible");
      window.setTimeout(function () { toast.hidden = true; }, 180);
    }, 2300);
  }

  function log(message) {
    logEntries.push(message);
    if (logEntries.length > 14) logEntries = logEntries.slice(-14);
    $("logOutput").textContent = logEntries.join("\n");
  }

  function updateOverallStatus() {
    var count = kinds.filter(function (kind) { return state.installed[kind]; }).length;
    $("overallStatus").textContent = "演示状态 · " + count + " 个插件已安装";
    var uninstallAll = $("uninstallAll");
    if (uninstallAll) uninstallAll.disabled = count === 0;
  }

  function updatePluginInstallView(kind) {
    var row = document.querySelector('.plugin-row[data-kind="' + cssEscape(kind) + '"]');
    if (!row) return;
    var installed = state.installed[kind];
    var tag = row.querySelector(".installed-tag");
    if (tag) {
      tag.textContent = installed ? "已安装" : "未安装";
      tag.classList.toggle("is-missing", !installed);
    }
    var updateButton = row.querySelector(".row-update");
    if (updateButton) updateButton.innerHTML = glyph("download") + (installed ? "更新" : "安装");
    updateOverallStatus();
  }

  function demoUpdate(kind) {
    state.installed[kind] = true;
    updatePluginInstallView(kind);
    var label = data[kind].name;
    log("[UI PREVIEW] 演示更新：" + label + "；未执行真实安装。");
    showToast(label + " · 仅更新预览状态，未执行真实安装");
  }

  function showConfirm(kind) {
    pendingRemoval = kind;
    var all = kind === "*";
    $("confirmTitle").textContent = all ? "卸载全部插件？" : "卸载插件？";
    $("confirmDescription").textContent = all ? "仅演示本地状态变化，不会卸载任何真实插件。" : "将“" + data[kind].name + "”标记为未安装。仅演示本地状态，不会卸载真实插件。";
    $("confirmAccept").textContent = all ? "演示全部卸载" : "演示卸载";
    openOverlay("confirmOverlay", document.activeElement);
  }

  function confirmRemove() {
    if (!pendingRemoval) return;
    var target = pendingRemoval;
    pendingRemoval = null;
    var removed = target === "*" ? kinds.slice() : [target];
    removed.forEach(function (kind) {
      state.installed[kind] = false;
      updatePluginInstallView(kind);
    });
    closeOverlay("confirmOverlay");
    if (target === "*") {
      log("[UI PREVIEW] 已演示全部卸载；未执行真实卸载。");
      showToast("已演示全部卸载 · 没有真实卸载插件");
    } else {
      log("[UI PREVIEW] 演示卸载：" + data[target].name + "；未执行真实卸载。");
      showToast(data[target].name + " · 仅更新预览状态，未执行真实卸载");
    }
  }

  function demoInstallAll() {
    var progress = $("installProgress");
    var bar = progress.firstElementChild;
    if (installTimer) window.clearTimeout(installTimer);
    progress.hidden = false;
    if (bar) { bar.style.transition = "none"; bar.style.transform = "scaleX(0)"; }
    window.requestAnimationFrame(function () {
      if (bar) { bar.style.transition = "transform 400ms ease"; bar.style.transform = "scaleX(1)"; }
    });
    $("installAll").disabled = true;
    installTimer = window.setTimeout(function () {
      kinds.forEach(function (kind) { state.installed[kind] = true; updatePluginInstallView(kind); });
      progress.hidden = true;
      $("installAll").disabled = false;
      log("[UI PREVIEW] 演示全部安装 / 更新完成；未执行真实安装。");
      showToast("演示已完成 · 未执行真实安装");
      installTimer = 0;
    }, 400);
  }

  function setValue(kind, key, value, refreshArt) {
    if (!state.values[kind]) return;
    state.values[kind][key] = value;
    applyConditions(kind);
    if (refreshArt !== false) updateViewportArt(kind, false);
  }

  function handleControlInput(event) {
    var target = event.target;
    var kind = state.activePanel;
    if (!kind || !target) return;
    if (target.matches("input[data-key][type='range']")) {
      var key = target.getAttribute("data-key");
      var value = Number(target.value);
      target.style.setProperty("--fill", sliderFill(value, Number(target.min), Number(target.max)) + "%");
      setValue(kind, key, value);
      var num = $("panelBody").querySelector('[data-number="' + cssEscape(key) + '"]');
      if (num) num.value = formatControlValue(kind, key, value);
      return;
    }
    if (target.matches("input[data-number]")) {
      if (target.value === "") return;
      var numberKey = target.getAttribute("data-number");
      var numberValue = Number(target.value);
      setValue(kind, numberKey, numberValue);
      var range = $("panelBody").querySelector('input[type="range"][data-key="' + cssEscape(numberKey) + '"]');
      if (range) {
        range.value = String(Math.min(Number(range.max), Math.max(Number(range.min), numberValue)));
        range.style.setProperty("--fill", sliderFill(Number(range.value), Number(range.min), Number(range.max)) + "%");
      }
      return;
    }
    if (target.matches("input[type='checkbox'][data-key]")) {
      setValue(kind, target.getAttribute("data-key"), target.checked);
    }
  }

  function handlePanelClick(event) {
    var target = event.target.closest("button");
    if (!target) return;
    var kind = state.activePanel;
    if (target.hasAttribute("data-pick")) {
      var pick = target.getAttribute("data-pick");
      state.picks[kind][pick] = !state.picks[kind][pick];
      updatePickButtons(kind);
      updatePanelFooter(kind);
      if (pick === "gradient" && kind === "voronoi") {
        state.values.voronoi.GradientOn = state.picks[kind].gradient;
        state.values.voronoi.GradientPoint = state.picks[kind].gradient ? "preview-fixture" : null;
        applyConditions(kind);
      }
      updateViewportArt(kind, false);
      showToast(pick === "target" ? (state.picks[kind][pick] ? "已模拟选择示例物件" : "已清除示例物件") : (state.picks[kind][pick] ? "已模拟选择渐变参考物件" : "已清除渐变参考物件"));
      return;
    }
    if (target.hasAttribute("data-choice")) {
      var choiceKey = target.getAttribute("data-choice");
      var choiceIndex = Number(target.getAttribute("data-index"));
      setValue(kind, choiceKey, choiceIndex);
      return;
    }
    if (target.hasAttribute("data-action")) {
      var action = target.getAttribute("data-action");
      if (action === "clearGradient") {
        state.picks[kind].gradient = false;
        state.values[kind].GradientOn = false;
        state.values[kind].GradientPoint = null;
        updatePickButtons(kind);
        showToast("已清除渐变参考 · UI 预览");
      } else if (action === "chooseCenter") {
        state.values[kind].CenterPoints = ["preview-fixture"];
        state.values[kind].UsePickedCenter = true;
        showToast("已模拟指定圆心 · UI 预览");
      } else if (action === "useSurfaceCenter") {
        state.values[kind].CenterPoints = null;
        state.values[kind].UsePickedCenter = false;
        showToast("已切换为曲面中心 · UI 预览");
      }
      applyConditions(kind);
      updateViewportArt(kind, false);
      return;
    }
  }

  function focusables(root) {
    return Array.prototype.slice.call(root.querySelectorAll('button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])')).filter(function (element) {
      return !element.hidden && element.getAttribute("aria-hidden") !== "true";
    });
  }

  function onDocumentKeydown(event) {
    if (event.key === "Escape") {
      if (!$ ("confirmOverlay").hidden) closeOverlay("confirmOverlay");
      else if (!$ ("iconsOverlay").hidden) closeOverlay("iconsOverlay");
      else if (!$ ("panelOverlay").hidden) closePanel();
      return;
    }
    var overlay = !$ ("confirmOverlay").hidden ? $("confirmOverlay") : (!$ ("iconsOverlay").hidden ? $("iconsOverlay") : (!$ ("panelOverlay").hidden ? $("panelOverlay") : null));
    if (!overlay || event.key !== "Tab") return;
    var items = focusables(overlay);
    if (!items.length) { event.preventDefault(); return; }
    var first = items[0], last = items[items.length - 1];
    if (event.shiftKey && (document.activeElement === first || !overlay.contains(document.activeElement))) {
      event.preventDefault(); last.focus();
    } else if (!event.shiftKey && (document.activeElement === last || !overlay.contains(document.activeElement))) {
      event.preventDefault(); first.focus();
    }
  }

  function bindEvents() {
    document.addEventListener("click", function (event) {
      var open = event.target.closest("[data-open]");
      if (open) { openPanel(open.getAttribute("data-open"), open); return; }
      var rowAction = event.target.closest("[data-action][data-kind]");
      if (rowAction) {
        var rowKind = rowAction.getAttribute("data-kind");
        var action = rowAction.getAttribute("data-action");
        if (action === "preview") openPanel(rowKind, rowAction);
        else if (action === "update") demoUpdate(rowKind);
        else if (action === "remove") showConfirm(rowKind);
        return;
      }
      if (event.target.closest("#closePanel, #cancelPanel")) { closePanel(); return; }
      if (event.target === $("panelOverlay")) { closePanel(); return; }
      if (event.target.closest("#fixtureButton")) {
        state.targetFaces = state.targetFaces === 2 ? 1 : 2;
        $("fixtureButton").textContent = "示例：多重曲面 · " + state.targetFaces + " 面";
        if (state.activePanel) {
          applyConditions(state.activePanel);
          updatePanelFooter(state.activePanel);
        }
        showToast("示例对象已切换为 " + state.targetFaces + " 面");
        return;
      }
      if (event.target.closest("#iconBoardButton")) { openOverlay("iconsOverlay", $("iconBoardButton")); return; }
      if (event.target.closest("#closeIcons") || event.target === $("iconsOverlay")) { closeOverlay("iconsOverlay"); return; }
      if (event.target.closest("#installAll")) { demoInstallAll(); return; }
      if (event.target.closest("#openDirectory")) { showToast("此处仅为 UI 预览，没有打开真实目录"); log("[UI PREVIEW] 打开目录为演示动作；未访问文件系统。"); return; }
      if (event.target.closest("#uninstallAll")) { showConfirm("*"); return; }
      if (event.target.closest("#confirmCancel")) { pendingRemoval = null; closeOverlay("confirmOverlay"); return; }
      if (event.target.closest("#confirmAccept")) { confirmRemove(); return; }
      if (event.target === $("confirmOverlay")) { pendingRemoval = null; closeOverlay("confirmOverlay"); return; }
      if (event.target.closest("#generateButton")) {
        if (state.activePanel && !$("generateButton").disabled) showToast(state.activePanel === "vape" ? "UI 预览未连接 Rhino；未运行重新生成标注命令" : "UI 预览 · 未运行生成命令");
        return;
      }
      if (event.target.closest("#minimizeButton")) {
        state.minimized = !state.minimized;
        $("appWindow").classList.toggle("is-minimized", state.minimized);
        event.target.closest("#minimizeButton").setAttribute("aria-pressed", String(state.minimized));
        showToast(state.minimized ? "预览窗口已最小化（演示状态）" : "预览窗口已恢复");
        return;
      }
      if (event.target.closest("#resizeButton")) {
        state.compact = !state.compact;
        $("appWindow").classList.toggle("is-compact", state.compact);
        event.target.closest("#resizeButton").setAttribute("aria-pressed", String(state.compact));
        showToast(state.compact ? "已切换为紧凑预览尺寸" : "已恢复预览尺寸");
        return;
      }
      if (event.target.closest("#exitButton")) {
        closePanel();
        $("appWindow").hidden = true;
        $("closedState").hidden = false;
        $("reopenButton").focus();
        return;
      }
      if (event.target.closest("#reopenButton")) {
        $("closedState").hidden = true;
        $("appWindow").hidden = false;
        $("homeButton").focus();
        return;
      }
      if (event.target.closest("#homeButton")) {
        $("homeButton").classList.add("selected");
        closePanel();
        return;
      }
      var theme = event.target.closest("[data-theme]");
      if (theme) { setTheme(theme.getAttribute("data-theme")); return; }
    });

    $("panelBody").addEventListener("input", handleControlInput);
    $("panelBody").addEventListener("change", handleNumberCommit);
    $("panelBody").addEventListener("click", handlePanelClick);
    $("pluginSearch").addEventListener("input", function () { renderPluginList($("pluginSearch").value); });
    document.addEventListener("keydown", onDocumentKeydown);
  }

  function getControl(kind, key) {
    var groups = data[kind] && data[kind].groups || [];
    for (var i = 0; i < groups.length; i++) {
      var controls = groups[i].controls || [];
      for (var j = 0; j < controls.length; j++) if (controls[j].key === key) return controls[j];
    }
    return null;
  }

  function formatControlValue(kind, key, value) {
    var control = getControl(kind, key);
    var decimals = control && Number(control.step) >= 1 ? 0 : 2;
    var number = Number(value);
    return Number.isFinite(number) ? number.toFixed(decimals) : String(value);
  }

  function sliderFill(value, min, max) {
    if (!Number.isFinite(value) || !Number.isFinite(min) || !Number.isFinite(max) || max <= min) return 0;
    return Math.max(0, Math.min(100, (value - min) / (max - min) * 100));
  }

  function handleNumberCommit(event) {
    var target = event.target;
    if (!target || !target.matches("input[data-number]") || target.value === "" || !state.activePanel) return;
    target.value = formatControlValue(state.activePanel, target.getAttribute("data-number"), target.value);
  }

  function refreshPanel(kind) {
    if (!kind) return;
    var body = $("panelBody");
    var scrollTop = body.scrollTop;
    var active = document.activeElement;
    var focusKey = active && (active.getAttribute("data-key") || active.getAttribute("data-choice") || active.getAttribute("data-number"));
    renderPanelBody(kind);
    body.scrollTop = scrollTop;
    if (focusKey) {
      var focusTarget = body.querySelector('[data-key="' + cssEscape(focusKey) + '"], [data-choice="' + cssEscape(focusKey) + '"], [data-number="' + cssEscape(focusKey) + '"]');
      if (focusTarget && typeof focusTarget.focus === "function") focusTarget.focus();
    }
  }

  function initialize() {
    initializeState();
    if ($("brandIcon")) $("brandIcon").innerHTML = icon("app", 48);
    if ($("closedIcon")) $("closedIcon").innerHTML = icon("app", 64);
    renderPanelNav();
    renderPluginList("");
    renderIconFamily();
    installGlyphs(document);
    bindEvents();
    var params = new URLSearchParams(window.location.search);
    if (params.get("theme") === "dark") setTheme("dark");
    var requestedPanel = params.get("panel");
    if (requestedPanel && data[requestedPanel]) openPanel(requestedPanel, null);
    window.preview = {
      openPanel: openPanel,
      closePanel: closePanel,
      renderPanel: refreshPanel,
      setTheme: setTheme,
      getState: function () { return JSON.parse(JSON.stringify(state)); },
      ready: true
    };
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", initialize, { once: true });
  else initialize();
}(window, document));
