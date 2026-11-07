using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace EmbyFilenameTagger
{
    /// <summary>
    /// 一个条目"将要被写入什么"的计算结果。
    /// </summary>
    public class TaggingPlan
    {
        public List<string> Tags { get; } = new List<string>();
        public List<string> Genres { get; } = new List<string>();
        public List<string> Actors { get; } = new List<string>();

        /// <summary>制片人（PersonType.Producer）。</summary>
        public List<string> Producers { get; } = new List<string>();

        public bool IsEmpty => Tags.Count == 0 && Genres.Count == 0 && Actors.Count == 0 && Producers.Count == 0;
    }

    /// <summary>
    /// 把 RuleEngine 的结果翻译成 Emby 的写库动作。
    /// 所有 Emby 依赖都集中在这里，方便排查与替换。
    /// </summary>
    public class ItemTagger
    {
        private readonly ILibraryManager _libraryManager;

        public ItemTagger(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager ?? throw new ArgumentNullException(nameof(libraryManager));
        }

        /// <summary>
        /// 计算某条目应得的标签/类型/演员。不做任何写入。
        ///
        /// 规则生效范围完全由每条规则自己的 Libraries / ExcludedLibraries 决定：
        ///  - Libraries 为空      → 所有媒体库生效
        ///  - Libraries 非空      → 只在这些库生效
        ///  - ExcludedLibraries   → 优先级最高，命中的库一律跳过
        /// </summary>
        public TaggingPlan BuildPlan(BaseItem item, PluginConfiguration config, Action<string> trace)
        {
            var plan = new TaggingPlan();
            if (item == null || config == null) return plan;

            var fileName = GetFileNameForMatching(item);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                trace?.Invoke("跳过：拿不到文件名");
                return plan;
            }

            var libraryName = GetLibraryName(item);
            var libraryPath = GetLibraryPath(item);

            var parsed = FilmParser.Parse(fileName, item.Name);
            var ctx = FilmParser.BuildContext(parsed, item.Name, libraryName, libraryPath);

            trace?.Invoke($"文件名='{fileName}' 媒体库='{libraryName}' 演员候选=[{string.Join(", ", parsed.ActorCandidates)}]");

            if (config.Rules == null) return plan;

            foreach (var rule in config.Rules)
            {
                if (rule == null || !rule.Enabled) continue;

                // 媒体库范围过滤
                var scope = RuleEngine.ResolveScope(rule.Libraries, rule.ExcludedLibraries, libraryName, libraryPath);
                if (!scope.Allowed)
                {
                    trace?.Invoke($"[{KindLabel(rule.Kind)}] 规则 '{Describe(rule)}' 跳过：{scope.Reason}");
                    continue;
                }

                var hits = EvaluateRule(rule, fileName, ctx, trace).ToList();
                if (hits.Count == 0) continue;

                // 一条规则可以同时写入多个种类（界面上勾选"同时写入"）。
                var kinds = rule.GetEffectiveKinds();

                foreach (var hit in hits)
                {
                    if (string.IsNullOrWhiteSpace(hit)) continue;

                    // 标签：原样写入
                    if (kinds.Contains(RuleKind.Tag))
                    {
                        AddUnique(plan.Tags, hit);
                    }

                    // 类型：归一化成 Emby 标准名
                    if (kinds.Contains(RuleKind.Genre))
                    {
                        var canonical = GenreMapper.ToCanonical(hit);
                        if (!string.IsNullOrWhiteSpace(canonical)) AddUnique(plan.Genres, canonical);
                    }

                    // 演员：原样写入
                    if (kinds.Contains(RuleKind.Actor))
                    {
                        AddUnique(plan.Actors, hit);
                    }

                    // 制片：原样写入（写入时用 PersonType.Producer）
                    if (kinds.Contains(RuleKind.Producer))
                    {
                        AddUnique(plan.Producers, hit);
                    }
                }
            }

            return plan;
        }

        /// <summary>
        /// 计算一条规则产生的全部命中值。
        ///
        /// 优先级：
        ///  1) 填了正则 → 用正则（支持命名捕获组）
        ///  2) 否则解析「关键词=目标值」映射表，逐行匹配
        ///  3) 都没有 → 用旧版单关键词字段兜底（兼容老配置）
        /// </summary>
        private static IEnumerable<string> EvaluateRule(RuleItem rule, string fileName, PlaceholderContext ctx, Action<string> trace)
        {
            var results = new List<string>();

            // ---- 1) 正则 ----
            if (!string.IsNullOrWhiteSpace(rule.Pattern))
            {
                var engineRule = new KeywordRule
                {
                    Enabled = rule.Enabled,
                    Name = rule.Name,
                    Pattern = rule.Pattern,
                    TargetValues = rule.TargetValues
                };

                var values = RuleEngine.Evaluate(engineRule, fileName, ctx, out var reason);
                if (values == null)
                {
                    if (reason != null) trace?.Invoke($"[{KindLabel(rule.Kind)}] 规则 '{Describe(rule)}' 正则未命中（{reason}）");
                    return results;
                }
                if (reason != null) trace?.Invoke($"[{KindLabel(rule.Kind)}] 规则 '{Describe(rule)}' {reason}");
                results.AddRange(values.Where(v => !string.IsNullOrWhiteSpace(v)));
                return results;
            }

            // ---- 2) 映射表 ----
            var mappingText = BuildMappingText(rule);
            if (!string.IsNullOrWhiteSpace(mappingText))
            {
                var entries = MappingParser.Parse(mappingText, out var parseErrors);
                foreach (var err in parseErrors) trace?.Invoke($"[{KindLabel(rule.Kind)}] 规则 '{Describe(rule)}' {err}");

                foreach (var entry in entries)
                {
                    foreach (var keyword in entry.Keywords)
                    {
                        var expanded = RuleEngine.ExpandPlaceholders(keyword, ctx);
                        if (expanded == null) continue;

                        if (fileName.IndexOf(expanded, StringComparison.OrdinalIgnoreCase) < 0) continue;

                        if (entry.Targets.Count == 0)
                        {
                            // 目标值留空：直接使用关键词本身，不解析文件名。
                            // 例如 "周星驰"= 命中后写入演员「周星驰」。
                            results.Add(expanded);
                            trace?.Invoke($"[{KindLabel(rule.Kind)}] '{expanded}' 命中（目标值留空，直接使用关键词）");
                        }
                        else
                        {
                            foreach (var t in entry.Targets)
                            {
                                var expandedTarget = RuleEngine.ExpandPlaceholders(t, ctx);
                                if (!string.IsNullOrWhiteSpace(expandedTarget)) results.Add(expandedTarget);
                            }
                            trace?.Invoke($"[{KindLabel(rule.Kind)}] '{expanded}' 命中 → {string.Join(", ", entry.Targets)}");
                        }
                        break; // 同一行命中即止，避免重复
                    }
                }

                return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }

            // ---- 3) 旧版字段兜底 ----
            if (!string.IsNullOrWhiteSpace(rule.Keywords))
            {
                var legacy = new KeywordRule
                {
                    Enabled = rule.Enabled,
                    Name = rule.Name,
                    Keywords = rule.Keywords,
                    TargetValues = rule.TargetValues
                };

                var values = RuleEngine.Evaluate(legacy, fileName, ctx, out var reason);
                if (values != null) results.AddRange(values.Where(v => !string.IsNullOrWhiteSpace(v)));
                if (reason != null) trace?.Invoke($"[{KindLabel(rule.Kind)}] 规则 '{Describe(rule)}' {reason}");
            }

            return results;
        }

        /// <summary>
        /// 取出规则要用的映射表文本。
        /// 新配置用 MappingText；老配置字段自动转换，避免升级后规则丢失。
        /// </summary>
        private static string BuildMappingText(RuleItem rule)
        {
            if (!string.IsNullOrWhiteSpace(rule.MappingText)) return rule.MappingText;

            // 旧配置迁移：Keywords + TargetValues → "k1,k2=target"
            return MappingParser.FromLegacy(rule.Keywords, rule.TargetValues);
        }

        /// <summary>
        /// 把计算结果写回条目。返回是否真的发生了变更。
        /// </summary>
        public bool ApplyPlan(BaseItem item, TaggingPlan plan, PluginConfiguration config, Action<string> trace)
        {
            if (item == null || plan == null || plan.IsEmpty) return false;

            bool changed = false;

            if (plan.Tags.Count > 0)
            {
                var existing = item.Tags ?? Array.Empty<string>();
                var merged = Merge(existing, plan.Tags, config.MergeWithExisting);
                if (!SequenceEqual(existing, merged))
                {
                    item.Tags = merged;
                    changed = true;
                    trace?.Invoke($"标签 → {string.Join(", ", merged)}");
                }
            }

            if (plan.Genres.Count > 0)
            {
                var existing = item.Genres ?? Array.Empty<string>();
                var merged = Merge(existing, plan.Genres, config.MergeWithExisting);
                if (!SequenceEqual(existing, merged))
                {
                    item.Genres = merged;
                    changed = true;
                    trace?.Invoke($"类型 → {string.Join(", ", merged)}");
                }
            }

            // 演员与制片统一处理，因为两者的顺序会互相影响。
            //
            // Emby 读取人员用的是 order by ListOrder, PersonType，
            // 而 ListOrder 就是我们传给 UpdatePeople 的 List 下标。
            // 因此要让"制片"排在演职人员列表最前面，只需把它放在列表首位。
            if (plan.Actors.Count > 0 || plan.Producers.Count > 0)
            {
                var people = _libraryManager.GetItemPeople(item) ?? new List<PersonInfo>();

                var existingNames = new HashSet<string>(
                    people.Where(p => !string.IsNullOrEmpty(p.Name)).Select(p => p.Name),
                    StringComparer.OrdinalIgnoreCase);

                // 需要新增的人员：先制片，后演员（顺序决定显示顺序）
                var toAdd = new List<PersonInfo>();

                foreach (var producer in plan.Producers)
                {
                    if (existingNames.Contains(producer)) continue;
                    if (toAdd.Any(x => string.Equals(x.Name, producer, StringComparison.OrdinalIgnoreCase))) continue;
                    toAdd.Add(new PersonInfo { Name = producer, Type = PersonType.Producer });
                }

                foreach (var actor in plan.Actors)
                {
                    if (existingNames.Contains(actor)) continue;
                    if (toAdd.Any(x => string.Equals(x.Name, actor, StringComparison.OrdinalIgnoreCase))) continue;
                    toAdd.Add(new PersonInfo { Name = actor, Type = PersonType.Actor });
                }

                if (toAdd.Count > 0)
                {
                    // 制片置顶：把新制片排到最前，其余原有人员顺序不变。
                    var newProducers = toAdd.Where(x => x.Type == PersonType.Producer).ToList();
                    var newOthers = toAdd.Where(x => x.Type != PersonType.Producer).ToList();

                    var ordered = new List<PersonInfo>();
                    ordered.AddRange(newProducers);   // 新增制片 → 最前
                    ordered.AddRange(people);
                    ordered.AddRange(newOthers);

                    // 统一整理：制片全部前置，其余保持相对顺序
                    ordered = OrderPeopleWithProducersFirst(ordered);

                    _libraryManager.UpdatePeople(item, ordered, true);
                    changed = true;

                    var addedActors = newOthers.Where(x => x.Type == PersonType.Actor).Select(x => x.Name).ToList();
                    if (newProducers.Count > 0)
                        trace?.Invoke($"制片追加（置顶）→ {string.Join(", ", newProducers.Select(x => x.Name))}");
                    if (addedActors.Count > 0)
                        trace?.Invoke($"演员追加 → {string.Join(", ", addedActors)}");
                }
            }

            return changed;
        }

        /// <summary>
        /// 整理人员列表的顺序：让制片排在最前面，其余人员保持原相对顺序。
        ///
        /// 实际规则在 PeopleOrdering 中（纯逻辑、可单测），
        /// 这里只负责在 Emby 的 PersonInfo 与轻量结构之间转换。
        /// </summary>
        public static List<PersonInfo> OrderPeopleWithProducersFirst(IEnumerable<PersonInfo> people)
        {
            var source = people?.ToList() ?? new List<PersonInfo>();

            var entries = source
                .Select(p => new PersonEntry { Name = p.Name, Type = p.Type.ToString() })
                .ToList();

            var ordered = PeopleOrdering.ProducersFirst(entries);

            // 按名字+类型映射回原始对象，保持 Role 等其它字段不变
            var result = new List<PersonInfo>(ordered.Count);
            var used = new HashSet<int>();

            foreach (var e in ordered)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    if (used.Contains(i)) continue;
                    var p = source[i];
                    if (!string.Equals(p.Name, e.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(p.Type.ToString(), e.Type, StringComparison.Ordinal)) continue;

                    used.Add(i);
                    result.Add(p);
                    break;
                }
            }

            return result;
        }

        private static string KindLabel(RuleKind kind)
        {
            switch (kind)
            {
                case RuleKind.Tag: return "标签";
                case RuleKind.Genre: return "类型";
                case RuleKind.Actor: return "演员";
                case RuleKind.Producer: return "制片";
                default: return "未知";
            }
        }

        private static string Describe(RuleItem rule)
        {
            if (!string.IsNullOrWhiteSpace(rule.Name)) return rule.Name;
            if (!string.IsNullOrWhiteSpace(rule.Keywords)) return rule.Keywords;
            if (!string.IsNullOrWhiteSpace(rule.Pattern)) return rule.Pattern;
            return "(未命名)";
        }

        /// <summary>
        /// 参与匹配的文件名：优先真实文件名（去掉扩展名），
        /// 拿不到时退回条目显示名，保证网络库/虚拟库也能工作。
        /// </summary>
        public static string GetFileNameForMatching(BaseItem item)
        {
            if (item == null) return null;

            var path = item.Path;
            if (!string.IsNullOrWhiteSpace(path))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
                catch (ArgumentException)
                {
                    // 路径含非法字符，退回显示名。
                }
            }

            var fileName = item.FileNameWithoutExtension;
            if (!string.IsNullOrWhiteSpace(fileName)) return fileName;

            return item.Name;
        }

        public static string GetLibraryName(BaseItem item)
        {
            try
            {
                var folders = BaseItem.LibraryManager?.GetCollectionFolders(item);
                return folders?.FirstOrDefault()?.Name;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string GetLibraryPath(BaseItem item)
        {
            try
            {
                var folders = BaseItem.LibraryManager?.GetCollectionFolders(item);
                return folders?.FirstOrDefault()?.Path;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string[] Merge(string[] existing, List<string> additions, bool merge)
        {
            var result = merge
                ? new List<string>(existing ?? Array.Empty<string>())
                : new List<string>();

            foreach (var a in additions)
            {
                if (!result.Contains(a, StringComparer.OrdinalIgnoreCase)) result.Add(a);
            }
            return result.ToArray();
        }

        private static bool SequenceEqual(string[] a, string[] b)
        {
            a ??= Array.Empty<string>();
            b ??= Array.Empty<string>();
            return a.Length == b.Length && a.SequenceEqual(b, StringComparer.Ordinal);
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (list.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
            list.Add(value.Trim());
        }
    }
}
