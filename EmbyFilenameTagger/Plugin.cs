using System;
using System.Collections.Generic;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace EmbyFilenameTagger
{
    /// <summary>
    /// 插件入口。
    ///
    /// 关键设计（均为在 Emby 4.10.2.0 上实测后确定）：
    ///
    /// 1) 用 BasePlugin&lt;PluginConfiguration&gt; 而不是 BasePlugin。
    ///    它的 SaveConfiguration()/Configuration 会自动把配置写到
    ///    plugins/configurations/&lt;插件名&gt;.xml，不需要自己碰 IConfigurationManager。
    ///    （手动调用 SaveConfiguration(key, obj) 需要 key 已在 _configurationStores
    ///      中注册，否则 First() 抛异常，配置只留在内存里、重启即丢。）
    ///
    /// 2) 用 IHasWebPages 注册两个页面：HTML 与脚本分离。
    ///    HTML 里绝不能出现 &lt;script&gt;，否则 WebAppService -> PackageCreator.ModifyHtml
    ///    会走"注释脚本"的特殊分支，该版本直接抛 NullReferenceException，
    ///    请求返回 500；前端把错误页当视图解析、找不到 .view 元素，
    ///    就会报 "Cannot read properties of null (reading 'getAttribute')"。
    ///
    /// 3) HTML 根节点必须是 class 含 view 的元素，
    ///    因为前端 viewmanager.normalizeNewView 用
    ///    querySelector('.view, div[data-role="page"]') 定位根视图。
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// 固定 Id。发布后不要修改，否则 Emby 会当成另一个插件，用户配置会"丢失"。
        /// </summary>
        public static readonly Guid PluginId = new Guid("7f3c1a52-9b4e-4d61-8c2a-5e6d9f0a1b23");

        /// <summary>配置页（HTML）名称，必须全小写：前端拼 URL 时会 toLowerCase。</summary>
        public const string PageName = "filenametaggerconfig";

        /// <summary>脚本页名称，须与 HTML 里 data-controller="__plugin/filenametaggerjs" 对应。</summary>
        public const string ScriptPageName = "filenametaggerjs";

        private const string HtmlResource = "EmbyFilenameTagger.Web.config.html";
        private const string JsResource = "EmbyFilenameTagger.Web.config.js";

        public static Plugin Instance { get; private set; }

        private readonly ILogger _logger;

        public Plugin(IApplicationHost applicationHost, ILogManager logManager, IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
            _logger = logManager?.GetLogger("FilenameTagger");

            // 注意：这里绝对不能访问 Configuration / ConfigurationFilePath。
            // BasePlugin<T> 的 ConfigurationFilePath 依赖 AssemblyFileName ->
            // base.AssemblyFilePath，而 AssemblyFilePath 要等 Emby 在构造完成之后
            // 调用 SetAttributes() 才赋值。构造函数里访问会抛
            // ArgumentNullException("path2")，导致整个插件创建失败。
            Log("插件已加载。设置页：仪表板 → 插件 → EmbyFilenameTagger → 设置。");
        }

        public override string Name => "EmbyFilenameTagger";

        public override Guid Id => PluginId;

        public override string Description =>
            "按文件名中的关键词自动写入标签、类型与演员，每条规则可指定生效的媒体库。";

        // ---------------- IHasWebPages ----------------

        /// <summary>
        /// 注册两个页面：HTML 主页面 + 脚本模块。
        ///
        /// HTML 页面是插件设置入口；脚本页只负责被 data-controller 引用。
        /// </summary>
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = PageName,
                    EmbeddedResourcePath = HtmlResource,
                    DisplayName = "EmbyFilenameTagger",
                    EnableInMainMenu = false,
                    EnableInUserMenu = false,
                    IsMainConfigPage = true
                },
                new PluginPageInfo
                {
                    Name = ScriptPageName,
                    EmbeddedResourcePath = JsResource,
                    EnableInMainMenu = false,
                    EnableInUserMenu = false,
                    IsMainConfigPage = false
                }
            };
        }

        // ---------------- 便捷访问 ----------------

        /// <summary>
        /// 当前配置，空安全。
        ///
        /// 注意时序：BasePlugin&lt;T&gt;.Configuration 首次访问会触发 LoadConfiguration()，
        /// 而它依赖 ConfigurationFilePath -> AssemblyFileName -> base.AssemblyFilePath。
        /// AssemblyFilePath 由 Emby 在构造完成**之后**通过 SetAttributes() 注入，
        /// 因此在那之前访问会抛 ArgumentNullException("path2")。
        ///
        /// 这里做降级：尚未就绪时返回一个默认实例，保证 API 不会 500，
        /// 等 Emby 注入完成后再读就是真实配置。
        /// </summary>
        public PluginConfiguration Options
        {
            get
            {
                try
                {
                    return Configuration ?? new PluginConfiguration();
                }
                catch (Exception ex)
                {
                    LogError("读取配置失败（可能过早访问），返回默认配置", ex);
                    return new PluginConfiguration();
                }
            }
        }

        /// <summary>
        /// 保存配置。BasePlugin&lt;T&gt;.SaveConfiguration() 会写到
        /// plugins/configurations/&lt;程序集名&gt;.xml。
        ///
        /// 若 Emby 还没调用 SetAttributes()（AssemblyFilePath 为 null），
        /// 此时既没有可靠的路径也没有正在使用的配置，保存没有意义，直接返回。
        /// </summary>
        public bool SaveOptions()
        {
            try
            {
                if (string.IsNullOrEmpty(AssemblyFilePath))
                {
                    Log("插件尚未完成初始化（AssemblyFilePath 为空），本次不落盘。");
                    return false;
                }

                SaveConfiguration();
                Log("配置已保存到：" + ConfigurationFilePath);
                return true;
            }
            catch (Exception ex)
            {
                LogError("保存配置失败", ex);
                return false;
            }
        }

        /// <summary>
        /// 替换内存中的配置并立即落盘。
        ///
        /// BasePlugin&lt;T&gt;.Configuration 的 setter 是 protected，
        /// 外部不能直接赋值，所以通过这个强类型入口完成。
        /// </summary>
        public bool UpdateOptions(PluginConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            Configuration = configuration;
            return SaveOptions();
        }

        public void Log(string message) => _logger?.Info("[FilenameTagger] " + message);

        public void LogDebug(string message)
        {
            if (Options.DebugLogging) _logger?.Info("[FilenameTagger][调试] " + message);
        }

        public void LogError(string message, Exception ex = null)
        {
            if (ex == null) _logger?.Error("[FilenameTagger] " + message);
            else _logger?.ErrorException("[FilenameTagger] " + message, ex);
        }
    }
}
