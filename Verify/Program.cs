using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Services;
using MediaBrowser.Model.Tasks;

namespace EmbyFilenameTagger.Verify
{
    /// <summary>
    /// 加载性校验：确认 Emby 能正确识别并渲染插件。
    ///
    /// 这里的每条检查都对应一个曾经踩过的坑，用来防止回归。
    /// </summary>
    public static class Program
    {
        private static int _problems;

        private const string HtmlRes = "EmbyFilenameTagger.Web.config.html";
        private const string JsRes = "EmbyFilenameTagger.Web.config.js";

        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("用法: Verify <EmbyFilenameTagger.dll>");
                return 2;
            }

            var asm = Assembly.LoadFrom(args[0]);
            Console.WriteLine("程序集 : " + asm.GetName().Name + " v" + asm.GetName().Version);
            Console.WriteLine();

            CheckEmbeddedResources(asm);
            CheckTypes(asm);
            CheckWebPages(asm);
            CheckServices(asm);
            CheckHtml(asm);
            CheckJs(asm);

            Console.WriteLine();
            Console.WriteLine(_problems == 0
                ? "[结果] 全部通过，可被 Emby 加载"
                : "[结果] 发现 " + _problems + " 个问题");

            return _problems;
        }

        private static void CheckEmbeddedResources(Assembly asm)
        {
            Console.WriteLine("== 嵌入资源 ==");
            var names = asm.GetManifestResourceNames();
            foreach (var n in names) Console.WriteLine("   " + n);

            foreach (var required in new[] { HtmlRes, JsRes })
            {
                if (!names.Contains(required))
                {
                    Console.WriteLine("   !! 缺少 " + required);
                    _problems++;
                }
            }
            Console.WriteLine();
        }

        private static void CheckTypes(Assembly asm)
        {
            Console.WriteLine("== 自动类型发现 ==");

            var plugins = asm.GetTypes().Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsAbstract).ToList();
            var entries = asm.GetTypes().Where(t => typeof(IServerEntryPoint).IsAssignableFrom(t) && !t.IsAbstract).ToList();
            var tasks = asm.GetTypes().Where(t => typeof(IScheduledTask).IsAssignableFrom(t) && !t.IsAbstract).ToList();
            var webPages = asm.GetTypes().Where(t => typeof(IHasWebPages).IsAssignableFrom(t) && !t.IsAbstract).ToList();

            Console.WriteLine("   插件类   : " + plugins.Count + "  " + string.Join(", ", plugins.Select(t => t.Name)));
            Console.WriteLine("   入口点   : " + entries.Count + "  " + string.Join(", ", entries.Select(t => t.Name)));
            Console.WriteLine("   计划任务 : " + tasks.Count + "  " + string.Join(", ", tasks.Select(t => t.Name)));
            Console.WriteLine("   WebPages : " + webPages.Count + "  " + string.Join(", ", webPages.Select(t => t.Name)));

            if (plugins.Count != 1) { Console.WriteLine("   !! 插件类应为 1 个"); _problems++; }
            if (tasks.Count != 1) { Console.WriteLine("   !! 计划任务应为 1 个"); _problems++; }
            if (webPages.Count != 1) { Console.WriteLine("   !! 应实现 IHasWebPages 以注册页面"); _problems++; }

            Console.WriteLine("   构造函数:");
            foreach (var t in plugins.Concat<Type>(entries).Concat(tasks))
            {
                foreach (var c in t.GetConstructors())
                {
                    Console.WriteLine("     " + t.Name + "(" +
                        string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name)) + ")");
                }
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 校验页面注册：必须恰好两个页面，Name 全小写，
        /// 且 HTML 页面的 data-controller 与脚本页对应。
        /// </summary>
        private static void CheckWebPages(Assembly asm)
        {
            Console.WriteLine("== 页面注册 (IHasWebPages) ==");

            var pluginType = asm.GetTypes().FirstOrDefault(t =>
                typeof(IHasWebPages).IsAssignableFrom(t) && !t.IsAbstract && typeof(IPlugin).IsAssignableFrom(t));

            if (pluginType == null)
            {
                Console.WriteLine("   !! 未找到同时实现 IPlugin 与 IHasWebPages 的类型");
                _problems++;
                return;
            }

            // 不实例化（构造函数需要 DI），直接从方法体无法取常量，
            // 因此改为读取类型上的公开常量字段。
            var pageNameField = pluginType.GetField("PageName", BindingFlags.Public | BindingFlags.Static);
            var scriptField = pluginType.GetField("ScriptPageName", BindingFlags.Public | BindingFlags.Static);

            var pageName = pageNameField?.GetValue(null) as string;
            var scriptName = scriptField?.GetValue(null) as string;

            Console.WriteLine("   HTML 页名 : '" + pageName + "'");
            Console.WriteLine("   脚本页名  : '" + scriptName + "'");

            foreach (var kv in new[] { new { n = pageName, l = "HTML" }, new { n = scriptName, l = "脚本" } })
            {
                if (string.IsNullOrEmpty(kv.n))
                {
                    Console.WriteLine("   !! " + kv.l + " 页名为空");
                    _problems++;
                }
                else if (kv.n != kv.n.ToLowerInvariant())
                {
                    // Emby 前端 getConfigurationPageUrl 会 toLowerCase 后请求，
                    // 服务端按原名匹配，大小写不一致会导致找不到页面。
                    Console.WriteLine("   !! " + kv.l + " 页名含大写字母");
                    _problems++;
                }
            }

            // HTML 必须引用脚本页
            var html = ReadResource(asm, HtmlRes);
            if (html != null && !string.IsNullOrEmpty(scriptName))
            {
                var marker = "__plugin/" + scriptName;
                if (!html.Contains(marker))
                {
                    Console.WriteLine("   !! HTML 未通过 data-controller 引用脚本页 " + marker);
                    _problems++;
                }
                else
                {
                    Console.WriteLine("   HTML 引用脚本 : " + marker + " (ok)");
                }
            }

            Console.WriteLine();
        }

        private static void CheckServices(Assembly asm)
        {
            Console.WriteLine("== API 端点 ==");

            var services = asm.GetTypes().Where(t => typeof(IService).IsAssignableFrom(t) && !t.IsAbstract).ToList();
            Console.WriteLine("   服务实现 : " + services.Count + "  " + string.Join(", ", services.Select(t => t.Name)));

            var routes = new List<string>();
            foreach (var t in asm.GetTypes())
            {
                var r = t.GetCustomAttribute<RouteAttribute>();
                if (r == null) continue;
                var key = r.Verbs + " " + r.Path;
                if (routes.Contains(key)) { Console.WriteLine("   !! 重复路由: " + key); _problems++; }
                routes.Add(key);
            }

            foreach (var r in routes) Console.WriteLine("     " + r);
            if (!routes.Any(r => r.Contains("/FilenameTagger/Config")))
            {
                Console.WriteLine("   !! 缺少配置读写 API");
                _problems++;
            }
            Console.WriteLine();
        }

        /// <summary>
        /// HTML 片段约束。
        /// 最重要的一条：绝不能含 &lt;script&gt;，
        /// 否则 Emby 4.10.2 的 PackageCreator.ModifyHtml 会抛空引用导致 500。
        /// </summary>
        private static void CheckHtml(Assembly asm)
        {
            Console.WriteLine("== 配置页 HTML ==");
            var html = ReadResource(asm, HtmlRes);
            if (html == null) { Console.WriteLine("   !! 无法读取 HTML"); _problems++; return; }

            Console.WriteLine("   大小 : " + html.Length + " 字符");

            var lower = html.ToLowerInvariant();

            // 1) 严禁 script 标签
            if (lower.Contains("<script"))
            {
                Console.WriteLine("   !! 含 <script>，会导致服务端 500（必须拆到独立 JS 页）");
                _problems++;
            }
            else
            {
                Console.WriteLine("   无 <script> : 通过");
            }

            // 2) 严禁完整文档标签
            foreach (var bad in new[] { "<!doctype", "<html", "<head", "<body" })
            {
                if (lower.Contains(bad))
                {
                    Console.WriteLine("   !! 含有文档级标签 " + bad + "，必须是纯片段");
                    _problems++;
                }
            }

            // 3) 根视图元素：viewmanager 用 querySelector('.view, div[data-role="page"]')
            var hasViewRoot = Regex.IsMatch(html, @"class\s*=\s*[""'][^""']*\bview\b", RegexOptions.IgnoreCase)
                           || Regex.IsMatch(html, @"data-role\s*=\s*[""']page[""']", RegexOptions.IgnoreCase);
            if (!hasViewRoot)
            {
                Console.WriteLine("   !! 缺少 class 含 view 的根元素，会报 getAttribute 错误");
                _problems++;
            }
            else
            {
                Console.WriteLine("   根视图元素 : 存在");
            }

            // 4) 渲染容器
            if (!html.Contains("id=\"ft-root\""))
            {
                Console.WriteLine("   !! 缺少 #ft-root 容器");
                _problems++;
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 脚本页约束：必须是 AMD define 模块，并包含全部交互逻辑。
        /// </summary>
        private static void CheckJs(Assembly asm)
        {
            Console.WriteLine("== 配置页脚本 ==");
            var js = ReadResource(asm, JsRes);
            if (js == null) { Console.WriteLine("   !! 无法读取脚本"); _problems++; return; }

            Console.WriteLine("   大小 : " + js.Length + " 字符");

            if (!js.TrimStart().StartsWith("define("))
            {
                Console.WriteLine("   !! 不是 AMD define 模块，Emby 无法加载");
                _problems++;
            }
            else
            {
                Console.WriteLine("   AMD 模块 : 通过");
            }

            var must = new[]
            {
                "FilenameTagger/Config",
                "Libraries", "ExcludedLibraries",
                "KIND_META", "ft-libs", "保存配置"
            };
            foreach (var m in must)
            {
                if (!js.Contains(m)) { Console.WriteLine("   !! 缺少关键内容: " + m); _problems++; }
            }

            // 用户明确不要的控件
            if (Regex.IsMatch(js, @"type\s*=\s*[""']range[""']", RegexOptions.IgnoreCase))
            {
                Console.WriteLine("   !! 含 range 滑轨");
                _problems++;
            }
            if (Regex.IsMatch(js, @"type\s*=\s*[""']number[""']", RegexOptions.IgnoreCase))
            {
                Console.WriteLine("   !! 含 number 数字框");
                _problems++;
            }

            // 媒体库多选必须是 checkbox
            if (!js.Contains("'checkbox'") && !js.Contains("\"checkbox\""))
            {
                Console.WriteLine("   !! 媒体库未使用 checkbox 多选");
                _problems++;
            }
            Console.WriteLine();
        }

        private static string ReadResource(Assembly asm, string name)
        {
            using (var s = asm.GetManifestResourceStream(name))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8))
                {
                    return r.ReadToEnd();
                }
            }
        }
    }
}
