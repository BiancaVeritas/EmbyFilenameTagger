using System;
using System.IO;
using System.Text;

namespace EmbyFilenameTagger.Preview
{
    /// <summary>
    /// 生成可独立打开的预览页。
    ///
    /// 生产用的 config.html 是「Emby 配置页片段」（无 html/head/body），
    /// 这里给它套一个最小外壳，并注入假 ApiClient + 假 fetch，
    /// 以便在浏览器里直接查看真实渲染效果。
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var src = args.Length > 0 ? args[0] : "config.html";
            var dst = args.Length > 1 ? args[1] : "preview.html";

            var fragment = File.ReadAllText(src, Encoding.UTF8);

            var mock = @"
<script>
(function () {
  // ---- 模拟 Emby 的 ApiClient ----
  window.ApiClient = {
    getUrl: function (p) { return '/emby/' + String(p).replace(/^\//, ''); }
  };

  var fakeConfig = {
    Enabled: true,
    EnableRealtimeMonitor: true,
    MergeWithExisting: true,
    VideosOnly: true,
    DebugLogging: false,
    Libraries: ['电影', '电视剧', '儿童动画', '纪录片', '演唱会'],
    Rules: [
      {
        Kind: 0, Enabled: true, Name: '4K 片源',
        Keywords: '2160p|4K|UHD', TargetValues: '4K', Pattern: '',
        Libraries: ['电影'], ExcludedLibraries: []
      },
      {
        Kind: 1, Enabled: true, Name: '国语配音 → 剧情',
        Keywords: '国语|中字', TargetValues: '剧情', Pattern: '',
        Libraries: [], ExcludedLibraries: ['儿童动画']
      },
      {
        Kind: 2, Enabled: true, Name: '文件名开头的中文演员',
        Keywords: '', TargetValues: '',
        Pattern: '^(?<actor>[\u4e00-\u9fa5]{2,4})\\.',
        Libraries: ['电影'], ExcludedLibraries: []
      }
    ]
  };

  var origFetch = window.fetch;
  window.fetch = function (url, opts) {
    if (String(url).indexOf('FilenameTagger/Config') >= 0) {
      if (opts && opts.method === 'POST') {
        return Promise.resolve({
          ok: true,
          json: function () {
            var body = JSON.parse(opts.body || '{}');
            return Promise.resolve({ Success: true, Message: 'ok', Count: (body.Rules || []).length });
          }
        });
      }
      return Promise.resolve({
        ok: true,
        json: function () { return Promise.resolve(fakeConfig); }
      });
    }
    return origFetch ? origFetch.apply(this, arguments) : Promise.reject(new Error('no fetch'));
  };
})();
</script>
";

            var html = new StringBuilder();
            html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
            html.Append("<style>body{margin:0;padding:16px;background:#f2f2f2;font-family:sans-serif}</style>");
            html.Append("</head><body>");
            html.Append(mock);
            html.Append(fragment);
            html.Append("</body></html>");

            File.WriteAllText(dst, html.ToString(), Encoding.UTF8);
            Console.WriteLine("已生成预览: " + Path.GetFullPath(dst) + "  (" + html.Length + " 字符)");
            return 0;
        }
    }
}
