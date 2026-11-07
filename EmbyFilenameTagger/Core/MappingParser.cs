using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EmbyFilenameTagger
{
    /// <summary>
    /// 映射表中的一条：一组关键词 → 一组目标值。
    /// </summary>
    public class MappingEntry
    {
        public List<string> Keywords { get; } = new List<string>();
        public List<string> Targets { get; } = new List<string>();

        /// <summary>原始片段，用于日志。</summary>
        public string Raw { get; set; }

        /// <summary>片段序号（从 1 开始），用于报错定位。</summary>
        public int Index { get; set; }
    }

    /// <summary>
    /// 解析"关键词=目标值"映射表。
    ///
    /// 写法（引号必填）：
    ///
    ///     "2160p"|"4K"="4K", "国语"|"粤语"="中文配音"
    ///
    /// 分隔约定：
    ///  - 逗号（, 或 ，）与换行 → 分隔条目
    ///  - 等号（= 或 ＝ 或 ：）  → 分隔关键词与目标值
    ///  - 竖线（|）或顿号（、）  → 分隔同一侧内的多个关键词/目标值
    ///  - 目标值留空（如 "周星驰"=）→ 直接使用该关键词本身，不解析文件名
    ///
    /// 为什么强制引号：
    ///  名字里可能含 = , | 这类符号（例如 "A=B"），
    ///  不用引号时无法区分"分隔符"和"内容"，
    ///  统一加引号后规则没有歧义，也便于给出准确的错误提示。
    ///
    /// 本文件不依赖 Emby，可脱离服务端测试。
    /// </summary>
    public static class MappingParser
    {
        /// <summary>同一侧内多个值的分隔符（引号外才生效）。</summary>
        private static readonly char[] ValueSeparators = { '|', '、' };

        /// <summary>条目分隔符（引号外才生效）。</summary>
        private static readonly char[] EntrySeparators = { ',', '，', '\n', '\r' };

        /// <summary>关键词与目标值之间的分隔符（引号外才生效）。</summary>
        private static readonly char[] KeyValueSeparators = { '=', '＝', '：' };

        public static List<MappingEntry> Parse(string text, out List<string> errors)
        {
            var entries = new List<MappingEntry>();
            errors = new List<string>();

            if (string.IsNullOrWhiteSpace(text)) return entries;

            var segments = SplitEntries(text);
            int index = 0;

            foreach (var rawSegment in segments)
            {
                var segment = rawSegment.Trim();
                if (segment.Length == 0) continue;

                // 允许注释：整段以 # 或 // 开头则跳过
                if (segment.StartsWith("#") || segment.StartsWith("//")) continue;

                index++;

                var split = FindSeparator(segment);
                if (split < 0)
                {
                    errors.Add($"第 {index} 段缺少等号，应写成 \"关键词\"=\"目标值\"：{segment}");
                    continue;
                }

                var left = segment.Substring(0, split);
                var right = segment.Substring(split + 1).TrimStart('=', '＝', '：', ' ');

                var keys = SplitQuotedValues(left, index, "关键词", errors);
                if (keys.Count == 0) continue;

                var targets = SplitQuotedValues(right, index, "目标值", errors);

                var entry = new MappingEntry { Raw = segment, Index = index };
                entry.Keywords.AddRange(keys);

                foreach (var t in targets)
                {
                    if (!entry.Targets.Contains(t, StringComparer.OrdinalIgnoreCase)) entry.Targets.Add(t);
                }

                entries.Add(entry);
            }

            return entries;
        }

        /// <summary>
        /// 按条目分隔符切分，引号内的分隔符不生效。
        /// </summary>
        private static List<string> SplitEntries(string text)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            bool inQuote = false;

            for (int i = 0; i < text.Length; i++)
            {
                var ch = text[i];

                if (ch == '"')
                {
                    inQuote = !inQuote;
                    sb.Append(ch);
                    continue;
                }

                if (!inQuote && EntrySeparators.Contains(ch))
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(ch);
            }

            result.Add(sb.ToString());
            return result;
        }

        /// <summary>
        /// 找出引号外的第一个关键词/目标值分隔符位置。
        /// </summary>
        private static int FindSeparator(string segment)
        {
            bool inQuote = false;
            for (int i = 0; i < segment.Length; i++)
            {
                var ch = segment[i];
                if (ch == '"') { inQuote = !inQuote; continue; }
                if (inQuote) continue;
                if (KeyValueSeparators.Contains(ch)) return i;
            }
            return -1;
        }

        /// <summary>
        /// 把一侧（关键词侧或目标值侧）拆成多个值。
        /// 每个值都必须用双引号包裹，否则记为错误。
        /// 空侧返回空列表（目标值留空是合法写法）。
        /// </summary>
        private static List<string> SplitQuotedValues(string side, int entryIndex, string sideName, List<string> errors)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(side)) return result;

            var sb = new StringBuilder();
            bool inQuote = false;

            for (int i = 0; i < side.Length; i++)
            {
                var ch = side[i];

                if (ch == '"')
                {
                    inQuote = !inQuote;
                    sb.Append(ch);
                    continue;
                }

                if (!inQuote && ValueSeparators.Contains(ch))
                {
                    Validate(result, sb.ToString(), entryIndex, sideName, errors);
                    sb.Clear();
                    continue;
                }

                sb.Append(ch);
            }

            Validate(result, sb.ToString(), entryIndex, sideName, errors);

            if (inQuote)
            {
                errors.Add($"第 {entryIndex} 段{sideName}的双引号没有闭合");
            }

            return result;
        }

        /// <summary>
        /// 校验单个值：必须整体被双引号包裹。
        /// </summary>
        private static void Validate(List<string> result, string raw, int entryIndex, string sideName, List<string> errors)
        {
            var value = raw.Trim();
            if (value.Length == 0) return;

            if (value.Length < 2 || value[0] != '"' || value[value.Length - 1] != '"')
            {
                errors.Add($"第 {entryIndex} 段{sideName}「{value}」缺少双引号，应写成 \"{value}\"");
                return;
            }

            var inner = value.Substring(1, value.Length - 2).Trim();
            if (inner.Length == 0) return;
            if (result.Contains(inner, StringComparer.OrdinalIgnoreCase)) return;

            result.Add(inner);
        }

        /// <summary>
        /// 把旧配置（Keywords + TargetValues 两个字段）转成映射表文本，
        /// 保证老用户升级后规则不丢。输出统一带引号。
        /// </summary>
        public static string FromLegacy(string keywords, string targetValues)
        {
            if (string.IsNullOrWhiteSpace(keywords)) return null;

            var keys = keywords.Split(new[] { '|', ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(x => x.Trim())
                               .Where(x => x.Length > 0)
                               .Select(x => "\"" + x + "\"");

            var target = (targetValues ?? string.Empty).Trim();
            var targetPart = target.Length > 0 ? "\"" + target + "\"" : string.Empty;

            return string.Join("|", keys) + "=" + targetPart;
        }
    }
}
