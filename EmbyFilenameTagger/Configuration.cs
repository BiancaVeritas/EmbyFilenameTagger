using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MediaBrowser.Common;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;

namespace EmbyFilenameTagger
{
    // ============================================================
    // 配置模型：规则可自由增删，每条规则用"媒体库名称列表"决定生效范围
    // ============================================================

    /// <summary>
    /// 插件配置。继承 BasePluginConfiguration 后由 Emby 负责读写 XML，
    /// 不需要自己管文件。
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool Enabled { get; set; } = true;

        public bool EnableRealtimeMonitor { get; set; } = true;

        public bool MergeWithExisting { get; set; } = true;

        public bool VideosOnly { get; set; } = true;

        public bool DebugLogging { get; set; }

        /// <summary>所有规则，按种类混在一起存放，界面上分组显示。</summary>
        public List<RuleItem> Rules { get; set; } = new List<RuleItem>();
    }

    // ============================================================
    // API：页面初始化 + 保存
    // ============================================================

    [Route("/FilenameTagger/Config", "GET", Summary = "获取插件配置与媒体库列表")]
    public class GetTaggerConfig : IReturn<TaggerConfigResult>
    {
    }

    public class TaggerConfigResult
    {
        public bool Enabled { get; set; }
        public bool EnableRealtimeMonitor { get; set; }
        public bool MergeWithExisting { get; set; }
        public bool VideosOnly { get; set; }
        public bool DebugLogging { get; set; }

        public List<RuleItem> Rules { get; set; } = new List<RuleItem>();

        /// <summary>服务端上所有媒体库名称，供前端下拉多选。</summary>
        public List<string> Libraries { get; set; } = new List<string>();
    }

    public class TaggerConfigService : IService
    {
        private readonly ILibraryManager _libraryManager;

        public TaggerConfigService(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        public object Get(GetTaggerConfig request)
        {
            var cfg = Plugin.Instance != null
                ? Plugin.Instance.Options
                : new PluginConfiguration();

            return new TaggerConfigResult
            {
                Enabled = cfg.Enabled,
                EnableRealtimeMonitor = cfg.EnableRealtimeMonitor,
                MergeWithExisting = cfg.MergeWithExisting,
                VideosOnly = cfg.VideosOnly,
                DebugLogging = cfg.DebugLogging,
                Rules = cfg.Rules ?? new List<RuleItem>(),
                Libraries = GetLibraryNames()
            };
        }

        /// <summary>
        /// 直接读虚拟文件夹列表，这是最可靠的媒体库名来源，
        /// 不依赖任何条目是否已入库。
        /// </summary>
        private List<string> GetLibraryNames()
        {
            try
            {
                return _libraryManager.GetVirtualFolders()
                    .Where(v => v != null && !string.IsNullOrWhiteSpace(v.Name))
                    .Select(v => v.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogError("读取媒体库列表失败", ex);
                return new List<string>();
            }
        }
    }

    [Route("/FilenameTagger/Config", "POST", Summary = "保存插件配置")]
    public class SaveTaggerConfig : IReturn<TaggerSaveResult>
    {
        public bool Enabled { get; set; }
        public bool EnableRealtimeMonitor { get; set; }
        public bool MergeWithExisting { get; set; }
        public bool VideosOnly { get; set; }
        public bool DebugLogging { get; set; }
        public List<RuleItem> Rules { get; set; }
    }

    public class TaggerSaveResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public int Count { get; set; }
    }

    public class TaggerSaveService : IService
    {
        public object Post(SaveTaggerConfig request)
        {
            try
            {
                var plugin = Plugin.Instance;
                if (plugin == null)
                {
                    return new TaggerSaveResult { Success = false, Message = "插件尚未初始化" };
                }

                var cfg = new PluginConfiguration
                {
                    Enabled = request.Enabled,
                    EnableRealtimeMonitor = request.EnableRealtimeMonitor,
                    MergeWithExisting = request.MergeWithExisting,
                    VideosOnly = request.VideosOnly,
                    DebugLogging = request.DebugLogging,
                    Rules = Sanitize(request.Rules)
                };

                // 通过插件的强类型入口替换并落盘。
                var saved = plugin.UpdateOptions(cfg);

                plugin.Log($"配置已保存，共 {cfg.Rules.Count} 条规则（落盘={saved}）。");

                return new TaggerSaveResult
                {
                    Success = true,
                    Message = saved ? "已保存" : "已生效，但未能写入磁盘（插件尚未完成初始化）",
                    Count = cfg.Rules.Count
                };
            }
            catch (Exception ex)
            {
                Plugin.Instance?.LogError("保存配置失败", ex);
                return new TaggerSaveResult { Success = false, Message = ex.Message };
            }
        }

        /// <summary>
        /// 去掉空规则和空字符串，避免前端提交脏数据把配置撑大。
        /// </summary>
        private static List<RuleItem> Sanitize(List<RuleItem> rules)
        {
            var result = new List<RuleItem>();
            if (rules == null) return result;

            foreach (var r in rules)
            {
                if (r == null) continue;

                // 映射表与正则都没有内容时视为空规则，直接丢弃。
                var hasMapping = !string.IsNullOrWhiteSpace(r.MappingText);
                var hasPattern = !string.IsNullOrWhiteSpace(r.Pattern);
                var hasLegacy = !string.IsNullOrWhiteSpace(r.Keywords);

                if (!hasMapping && !hasPattern && !hasLegacy) continue;

                result.Add(new RuleItem
                {
                    Kind = r.Kind,
                    Kinds = CleanKinds(r.Kinds, r.Kind),
                    Enabled = r.Enabled,
                    Name = (r.Name ?? string.Empty).Trim(),
                    MappingText = (r.MappingText ?? string.Empty).Trim(),
                    Pattern = (r.Pattern ?? string.Empty).Trim(),
                    Keywords = (r.Keywords ?? string.Empty).Trim(),
                    TargetValues = (r.TargetValues ?? string.Empty).Trim(),
                    Libraries = Clean(r.Libraries),
                    ExcludedLibraries = Clean(r.ExcludedLibraries)
                });
            }
            return result;
        }

        private static List<string> Clean(List<string> values)
        {
            if (values == null) return new List<string>();
            return values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 清洗"同时写入"的种类列表：去掉未定义的值、去重。
        /// 自身 Kind 不必重复存，读取时会自动补上。
        /// </summary>
        private static List<RuleKind> CleanKinds(List<RuleKind> kinds, RuleKind selfKind)
        {
            if (kinds == null) return new List<RuleKind>();

            var valid = new[] { RuleKind.Tag, RuleKind.Genre, RuleKind.Actor, RuleKind.Producer };
            var result = new List<RuleKind>();

            foreach (var k in kinds)
            {
                if (!valid.Contains(k)) continue;
                if (k == selfKind) continue;      // 自身种类无需重复保存
                if (result.Contains(k)) continue;
                result.Add(k);
            }

            return result;
        }
    }
}
