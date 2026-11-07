using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace EmbyFilenameTagger
{
    /// <summary>
    /// 扫描任务：在"计划任务"里手动运行，或配置触发器定期运行。
    /// 遍历媒体库中的所有视频条目并套用规则。
    /// </summary>
    public class TaggingTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly ILogger _logger;

        public TaggingTask(ILibraryManager libraryManager, IItemRepository itemRepository, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _logger = logManager?.GetLogger("FilenameTagger");
        }

        /// <summary>
        /// 任务显示名。统一用英文插件名，便于按名称检索。
        /// 中文说明放在 Description 里。
        /// </summary>
        public string Name => "EmbyFilenameTagger";

        /// <summary>任务唯一键，不要修改。</summary>
        public string Key => "FilenameTaggerScan";

        /// <summary>
        /// 任务说明。以中文动作说明开头，后面补一句内容说明。
        /// </summary>
        public string Description =>
            "按文件名写入标签/类型/演员。扫描所有媒体条目，按文件名关键词补齐标签、类型与演员。";

        /// <summary>
        /// 归入 Emby 内置的 Library 分类。
        ///
        /// 必须用英文 "Library"：内置任务（Scan media library、
        /// Download subtitles 等）都用这个值，用中文分类名会被归到另一个分组。
        /// </summary>
        public string Category => "Library";

        /// <summary>默认不自动执行，由用户在计划任务里配置。 </summary>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            yield break;
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            return Task.Run(() => ExecuteCore(cancellationToken, progress), cancellationToken);
        }

        private void ExecuteCore(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var config = Plugin.Instance?.Options;
            if (config == null || !config.Enabled)
            {
                _logger?.Info("[FilenameTagger] 插件已关闭，任务跳过。");
                progress?.Report(100);
                return;
            }

            var tagger = new ItemTagger(_libraryManager);

            // 只取有文件路径的媒体条目。Recursive=true 会连剧集的每集一起取到。
            var query = new InternalItemsQuery
            {
                Recursive = true,
                IsVirtualItem = false
            };

            List<BaseItem> items;
            try
            {
                items = _libraryManager.GetItemList(query)
                    .Where(x => x != null)
                    .Where(x => !string.IsNullOrWhiteSpace(x.Path))
                    .Where(x => IsSupported(x, config))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[FilenameTagger] 枚举媒体条目失败", ex);
                progress?.Report(100);
                return;
            }

            _logger?.Info($"[FilenameTagger] 开始扫描，共 {items.Count} 个条目。");

            int processed = 0, changed = 0, failed = 0;

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var plan = tagger.BuildPlan(item, config, config.DebugLogging ? NewTrace() : null);
                    if (!plan.IsEmpty && tagger.ApplyPlan(item, plan, config, config.DebugLogging ? NewTrace() : null))
                    {
                        // 标签/类型是条目的持久化字段，必须显式保存；
                        // 演员由 UpdatePeople 就地写库，无需再保存一次。
                        _itemRepository.SaveItem(item, cancellationToken);
                        changed++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger?.ErrorException($"[FilenameTagger] 处理 '{item.Name}' 失败", ex);
                }

                processed++;
                if (processed % 50 == 0 || processed == items.Count)
                {
                    progress?.Report(processed * 100.0 / Math.Max(1, items.Count));
                }
            }

            _logger?.Info($"[FilenameTagger] 扫描完成：处理 {processed}，变更 {changed}，失败 {failed}。");
            progress?.Report(100);
        }

        private Action<string> NewTrace()
        {
            return msg => _logger?.Info("[FilenameTagger][调试] " + msg);
        }

        private static bool IsSupported(BaseItem item, PluginConfiguration config)
        {
            if (item is Movie || item is Episode || item is Series || item is Video) return true;
            return !config.VideosOnly;
        }
    }
}
