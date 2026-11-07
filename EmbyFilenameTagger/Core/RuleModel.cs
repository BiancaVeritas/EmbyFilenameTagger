using System;
using System.Collections.Generic;

namespace EmbyFilenameTagger
{
    /// <summary>规则种类。</summary>
    public enum RuleKind
    {
        Tag = 0,
        Genre = 1,
        Actor = 2,
        Producer = 3
    }

    /// <summary>
    /// 一条规则。
    ///
    /// 设计要点：
    ///  - 生效范围用"媒体库名称列表"表达，与界面上的多选一一对应，
    ///    不用位标志、不用数字，避免前后端理解错位。
    ///  - Libraries 为空 = 全部媒体库；非空 = 只在这些库生效。
    ///  - ExcludedLibraries 优先级高于 Libraries。
    ///
    /// 本文件不依赖 Emby，可脱离服务端测试。
    /// </summary>
    /// <summary>
    /// 一条规则。
    ///
    /// 设计要点：
    ///  - 生效范围用"媒体库名称列表"表达，与界面上的多选一一对应。
    ///  - Libraries 为空 = 全部媒体库；非空 = 只在这些库生效。
    ///  - ExcludedLibraries 优先级高于 Libraries。
    ///
    /// 本文件不依赖 Emby，可脱离服务端测试。
    /// </summary>
    public class RuleItem
    {
        public RuleItem()
        {
        }

        public RuleItem(RuleKind kind)
        {
            Kind = kind;
        }

        /// <summary>
        /// 规则种类。创建后不再允许修改（避免误操作），
        /// 需要别的种类请新建一条规则。
        /// </summary>
        public RuleKind Kind { get; set; }

        /// <summary>
        /// 这条规则同时写入哪些种类。
        ///
        /// 例如一条"标签规则"可以额外勾上"类型"和"演员"，
        /// 那么同一个关键词命中时会同时写三处，省得重复配置。
        ///
        /// 为空时退回 <see cref="Kind"/>（兼容旧配置）。
        /// </summary>
        public List<RuleKind> Kinds { get; set; } = new List<RuleKind>();

        /// <summary>
        /// 取得本条规则实际要写入的种类集合（去重、且始终包含自身 Kind）。
        /// </summary>
        public List<RuleKind> GetEffectiveKinds()
        {
            var result = new List<RuleKind> { Kind };

            if (Kinds != null)
            {
                foreach (var k in Kinds)
                {
                    if (!result.Contains(k)) result.Add(k);
                }
            }

            return result;
        }

        /// <summary>该规则是否要写入指定种类。</summary>
        public bool WritesTo(RuleKind kind) => GetEffectiveKinds().Contains(kind);

        public bool Enabled { get; set; } = true;

        /// <summary>规则名称，仅用于界面显示和日志。</summary>
        public string Name { get; set; }

        /// <summary>
        /// 关键词与目标值的映射表，一行一条，写法：
        ///
        ///     关键词=目标值
        ///     4K,2160p,UHD=4K
        ///     国语,粤语=中文配音
        ///     周星驰=周星驰
        ///
        /// 规则：
        ///  - 每行一条，用等号分隔；等号左边是关键词，右边是目标值
        ///  - 多个关键词用逗号分隔，命中任意一个即算命中
        ///  - 目标值留空（如 "星爷="）表示自动采用解析出的结果
        ///  - 空行和以 # 开头的行会被忽略
        ///
        /// 这是界面上唯一需要填的规则内容，取代了旧版的两个独立输入框。
        /// </summary>
        public string MappingText { get; set; }

        /// <summary>
        /// 旧版的单个关键词。仅用于兼容已保存的配置，
        /// 新配置统一使用 <see cref="MappingText"/>。
        /// </summary>
        public string Keywords { get; set; }

        /// <summary>旧版的单个目标值，仅用于兼容。</summary>
        public string TargetValues { get; set; }

        /// <summary>可选正则，填写后优先用正则匹配（支持命名捕获组）。</summary>
        public string Pattern { get; set; }

        /// <summary>生效的媒体库名称列表。空 = 全部媒体库。</summary>
        public List<string> Libraries { get; set; } = new List<string>();

        /// <summary>排除的媒体库名称列表，优先级最高。</summary>
        public List<string> ExcludedLibraries { get; set; } = new List<string>();
    }
}
