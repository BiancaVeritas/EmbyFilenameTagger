using System;
using System.Collections.Generic;
using System.Linq;

namespace EmbyFilenameTagger
{
    /// <summary>
    /// 人员排序规则。
    ///
    /// Emby 读取条目人员时执行的是：
    ///     select ... from ItemPeople2 ... order by ListOrder, PersonType
    /// 而 ListOrder 就是 UpdatePeople 传入列表的下标。
    ///
    /// 也就是说：想让谁排在最前面，就把它放在传给 UpdatePeople 的 List 里最前。
    ///
    /// 本类不依赖 Emby 类型，用 (名字, 类型名) 元组表达人员，
    /// 方便脱离服务端单测。
    /// </summary>
    public static class PeopleOrdering
    {
        /// <summary>Emby 的 PersonType 中与本插件相关的取值。</summary>
        public const string ProducerType = "Producer";
        public const string ActorType = "Actor";

        /// <summary>
        /// 让制片排在最前，其余人员保持原有相对顺序。
        ///
        /// 采用稳定分区：不改变任何人的相对次序，只把制片整体前移，
        /// 避免"为了置顶把导演、编剧的顺序也打乱"。
        /// </summary>
        public static List<PersonEntry> ProducersFirst(IEnumerable<PersonEntry> people)
        {
            var list = people?.ToList() ?? new List<PersonEntry>();

            var result = new List<PersonEntry>(list.Count);
            result.AddRange(list.Where(p => p != null && p.Type == ProducerType));
            result.AddRange(list.Where(p => p == null || p.Type != ProducerType));
            return result;
        }

        /// <summary>
        /// 合并"已有人员"与"要新增的人员"，返回最终顺序。
        ///
        /// 规则：
        ///  - 已存在同名同类型的人员不重复添加
        ///  - 新增的制片排在最前
        ///  - 其余保持：已有人员原顺序 + 新增演员
        /// </summary>
        public static List<PersonEntry> Merge(List<PersonEntry> existing, IEnumerable<PersonEntry> addProducers, IEnumerable<PersonEntry> addActors)
        {
            var result = new List<PersonEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in existing ?? new List<PersonEntry>())
            {
                if (p == null) continue;
                var key = p.Name + "|" + p.Type;
                if (!seen.Add(key)) continue;
                result.Add(p);
            }

            var producers = new List<PersonEntry>();
            foreach (var p in addProducers ?? Enumerable.Empty<PersonEntry>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                var key = p.Name + "|" + ProducerType;
                if (!seen.Add(key)) continue;
                producers.Add(new PersonEntry { Name = p.Name, Type = ProducerType });
            }

            var actors = new List<PersonEntry>();
            foreach (var p in addActors ?? Enumerable.Empty<PersonEntry>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                var key = p.Name + "|" + ActorType;
                if (!seen.Add(key)) continue;
                actors.Add(new PersonEntry { Name = p.Name, Type = ActorType });
            }

            // 新增制片 → 最前；随后是原有人员；最后是新增演员
            result.InsertRange(0, producers);
            result.AddRange(actors);

            return ProducersFirst(result);
        }
    }

    /// <summary>与 Emby 的 PersonInfo 对应的轻量结构，仅保留排序需要的字段。</summary>
    public class PersonEntry
    {
        public string Name { get; set; }
        public string Type { get; set; }

        public override string ToString() => Name + "(" + Type + ")";
    }
}
