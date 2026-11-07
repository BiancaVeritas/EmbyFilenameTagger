using System;
using System.Collections.Generic;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;

namespace EmbyFilenameTagger
{
    /// <summary>
    /// 实时监听：新条目入库后立刻套用规则，不用等定时任务。
    ///
    /// 注意：ItemAdded 在媒体库扫描过程中触发，此时条目可能还没完全落库，
    /// 所以这里只做轻量处理并吞掉所有异常，绝不能让插件的错误影响扫描本身。
    /// </summary>
    public class ItemChangeMonitor : IServerEntryPoint
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IItemRepository _itemRepository;
        private readonly ILogger _logger;
        private readonly ItemTagger _tagger;

        private bool _subscribed;

        public ItemChangeMonitor(ILibraryManager libraryManager, IItemRepository itemRepository, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _logger = logManager?.GetLogger("FilenameTagger");
            _tagger = new ItemTagger(libraryManager);
        }

        public void Run()
        {
            if (_subscribed) return;

            // 新条目入库时处理
            _libraryManager.ItemAdded += OnItemAdded;

            // 已有条目"刷新元数据"后也处理一次。
            // 这条很重要：用户手动刷新某个条目时，
            // Emby 触发的是 ItemUpdated 而不是 ItemAdded。
            _libraryManager.ItemUpdated += OnItemUpdated;

            _subscribed = true;
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            Process(e?.Item);
        }

        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
        {
            Process(e?.Item);
        }

        private void Process(BaseItem item)
        {
            try
            {
                var config = Plugin.Instance?.Options;
                if (config == null || !config.Enabled || !config.EnableRealtimeMonitor) return;

                if (item == null) return;
                if (string.IsNullOrWhiteSpace(item.Path)) return;
                if (!IsSupported(item, config)) return;

                var trace = config.DebugLogging
                    ? new Action<string>(m => _logger?.Info("[FilenameTagger][调试] " + m))
                    : null;

                var plan = _tagger.BuildPlan(item, config, trace);
                if (plan.IsEmpty) return;

                if (_tagger.ApplyPlan(item, plan, config, trace))
                {
                    _itemRepository.SaveItem(item, System.Threading.CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                // 实时处理失败不应阻断媒体库扫描，只记日志。
                _logger?.ErrorException("[FilenameTagger] 实时处理条目失败", ex);
            }
        }

        private static bool IsSupported(BaseItem item, PluginConfiguration config)
        {
            if (item is Movie || item is Episode || item is Series || item is Video) return true;
            return !config.VideosOnly;
        }

        public void Dispose()
        {
            if (!_subscribed) return;
            try
            {
                _libraryManager.ItemAdded -= OnItemAdded;
                _libraryManager.ItemUpdated -= OnItemUpdated;
            }
            catch (Exception)
            {
                // 卸载阶段忽略异常。
            }
            _subscribed = false;
        }
    }
}
