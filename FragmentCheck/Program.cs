using System;
using System.IO;
using System.Text;

namespace EmbyFilenameTagger.FragmentCheck
{
    /// <summary>
    /// 生成浏览器检查页，用 Emby 官方 viewmanager.js 的真实判定逻辑
    /// 验证配置页片段能否被识别为视图。
    ///
    /// 片段以 base64 注入，彻底避免 </script> 提前闭合导致检查失真。
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var src = args.Length > 0 ? args[0] : "config.html";
            var dst = args.Length > 1 ? args[1] : "check.html";
            var jsPath = args.Length > 2 ? args[2] : "check.js";

            var fragment = File.ReadAllText(src, Encoding.UTF8);
            var js = File.ReadAllText(jsPath, Encoding.UTF8);
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(fragment));

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset=\"utf-8\"><title>Emby 片段检查</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:Consolas,monospace;font-size:14px;padding:24px;background:#1e1e1e;color:#ddd}");
            sb.AppendLine(".ok{color:#4ec9b0}.fail{color:#f48771}h2{color:#fff}");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h2>Emby 配置页片段检查</h2>");
            sb.AppendLine("<div id=\"result\"></div>");
            sb.AppendLine("<script>window.__FRAGMENT_B64__ = \"" + b64 + "\";</script>");
            sb.AppendLine("<script>");
            sb.AppendLine(js);
            sb.AppendLine("</script>");
            sb.AppendLine("</body></html>");

            File.WriteAllText(dst, sb.ToString(), Encoding.UTF8);

            Console.WriteLine("已生成检查页: " + Path.GetFullPath(dst));
            Console.WriteLine("片段大小: " + fragment.Length + " 字符");
            return 0;
        }
    }
}
