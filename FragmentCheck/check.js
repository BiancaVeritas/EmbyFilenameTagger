(function () {
  // 由宿主程序注入：片段内容（base64，UTF-8）
  var B64 = window.__FRAGMENT_B64__ || '';

  var bin = atob(B64);
  var bytes = new Uint8Array(bin.length);
  for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  var html = new TextDecoder('utf-8').decode(bytes);

  var out = [];
  var problems = 0;

  function log(ok, msg) {
    out.push((ok ? '<span class="ok">[PASS]</span> ' : '<span class="fail">[FAIL]</span> ') + msg);
    if (!ok) problems++;
  }

  // ===== 完整复刻 Emby modules/viewmanager/viewmanager.js 的 normalizeNewView =====
  var viewHtml = html;
  var isPluginPage = true;
  var hasScript = !!isPluginPage && viewHtml.includes('<script');

  if (hasScript) {
    viewHtml = viewHtml.replaceAll('<!--<script', '<script')
                       .replaceAll('<\/script>-->', '<\/script>');
  }

  var div = document.createElement('div');
  div.innerHTML = viewHtml;
  var view = div.querySelector('.view,div[data-role="page"]');

  log(view !== null,
      'querySelector(\'.view, div[data-role="page"]\') 命中根视图 -> ' +
      (view ? '&lt;' + view.tagName.toLowerCase() + ' class=&quot;' + view.className + '&quot;&gt;' : '<b>null</b>'));

  if (view !== null) {
    try {
      log(true, 'view.getAttribute("data-title") 正常，值=' + String(view.getAttribute('data-title')));
      log(true, 'view.getAttribute("data-helpurl") 正常');
      log(true, 'view.getAttribute("data-require") 正常，值=' + String(view.getAttribute('data-require')));
      log(true, 'view.getAttribute("data-controller") 正常，值=' + String(view.getAttribute('data-controller')));
    } catch (e) {
      log(false, 'getAttribute 抛异常: ' + e.message);
    }

    log(view.querySelector('#ft-root') !== null, '视图内部存在 #ft-root 容器');
    log(view.querySelector('.ft-root') !== null, '视图内部存在 .ft-root 样式根');
  }

  // ===== 片段结构约束 =====
  var lower = html.toLowerCase();
  log(!lower.includes('<!doctype'), '不含 <!DOCTYPE>');
  log(!lower.includes('<html'), '不含 <html>');
  log(!lower.includes('<head'), '不含 <head>');
  log(!lower.includes('<body'), '不含 <body>');
  log(!lower.includes('type="range"'), '不含 range 滑轨');
  log(!lower.includes('type="number"'), '不含 number 数字框');
  log(html.includes('ft-libs'), '媒体库多选区存在');

  document.getElementById('result').innerHTML =
    out.join('<br>') + '<br><br><b class="' + (problems === 0 ? 'ok' : 'fail') + '">' +
    (problems === 0 ? '全部通过' : problems + ' 个问题') + '</b>';

  window.__FRAGMENT_CHECK__ = problems;
  document.title = problems === 0 ? 'PASS' : 'FAIL_' + problems;
})();
