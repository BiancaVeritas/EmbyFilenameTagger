using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace EmbyFilenameTagger
{
    /// <summary>
    /// 一条关键词规则。
    ///
    /// 设计要点：
    ///  - 关键词与目标值分离。关键词是"在文件名里找什么"，目标值是"命中后往媒体库里写什么"。
    ///    这样 "4K" 可以被映射成标签 "4K"，"特效" 可以被映射成类型 "Action"。
    ///  - 支持多个关键词用 | 或 , 分隔，命中任意一个即算命中。
    ///  - Keywords 支持 ${name} 变量，用于"从文件名里提取演员名"这类场景。
    ///    例如 Keywords = "${male}"、Pattern = "^(?&lt;male&gt;[A-Za-z]+)$"。
    /// </summary>
    public class KeywordRule
    {
        /// <summary>规则是否启用。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>规则名称，仅用于日志与排错。</summary>
        public string Name { get; set; }

        /// <summary>
        /// 在文件名里查找的关键词，多个用 | 或 , 分隔。
        /// 不区分大小写，按子串匹配（不是正则，避免用户误输入导致异常）。
        /// </summary>
        public string Keywords { get; set; }

        /// <summary>
        /// 命中后写入的目标值，多个用 | 或 , 分隔。
        /// 留空表示使用 FilmParser 解析出来的值（主要用于演员）。
        /// </summary>
        public string TargetValues { get; set; }

        /// <summary>
        /// 可选的正则表达式。填写后不再做关键词子串匹配，而是用正则去匹配文件名，
        /// 并把捕获组当作"占位符"的值来源（见 Keywords 里的 ${name} 写法）。
        /// </summary>
        public string Pattern { get; set; }

        /// <summary>
        /// 适用范围：留空 = 所有媒体库；
        /// 否则为媒体库名称列表（| 或 , 分隔），或媒体库路径列表，命中任意一个即生效。
        /// </summary>
        public string Libraries { get; set; }

        /// <summary>排除的媒体库，优先级高于 Libraries。</summary>
        public string ExcludedLibraries { get; set; }

        /// <summary>仅对文件路径中包含此文本的条目生效（留空不限制）。</summary>
        public string PathContains { get; set; }
    }

    /// <summary>
    /// 文件名解析结果：从 "片名.演员A.演员B.2023.1080p.BluRay-GRP.mkv" 这类命名里
    /// 拆出演员候选、年份、分辨率等"令牌"。
    /// </summary>
    public class FilmParseResult
    {
        public List<string> Tokens { get; } = new List<string>();
        public List<string> ActorCandidates { get; } = new List<string>();
        public string Year { get; set; }
        public string Resolution { get; set; }
        public override string ToString() => string.Join(".", Tokens);
    }

    /// <summary>单次解析里所有占位符的值。</summary>
    public class PlaceholderContext
    {
        private readonly Dictionary<string, string> _map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string this[string key]
        {
            get => _map.TryGetValue(key, out var v) ? v : null;
            set => _map[key] = value;
        }

        public void Merge(PlaceholderContext other)
        {
            if (other == null) return;
            foreach (var kv in other._map)
            {
                if (!_map.ContainsKey(kv.Key)) _map[kv.Key] = kv.Value;
            }
        }
    }

    /// <summary>
    /// 文件名 → 标识的解析器。纯函数、无 IO、可单测。
    /// </summary>
    public static class FilmParser
    {
        // 常见的"垃圾"令牌：发布组、编码、音轨等，它们不可能是演员名。
        private static readonly HashSet<string> NoiseTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "1080p", "1080i", "720p", "480p", "2160p", "4k", "8k", "uhd", "hdr", "hdr10", "dv", "dolby", "vision",
            "bluray", "blu-ray", "bdrip", "brrip", "webrip", "web-dl", "webdl", "web", "hdtv", "dvdrip", "remux",
            "x264", "x265", "h264", "h265", "hevc", "avc", "av1", "10bit", "8bit",
            "aac", "ac3", "dts", "dtshd", "truehd", "atmos", "flac", "ddp", "dd5", "eac3", "lpcm",
            "中字", "字幕", "简中", "繁中", "国语", "粤语", "日语", "英语", "双语", "内嵌", "外挂",
            "未删减", "导演剪辑版", "加长版", "修复版", "重制版", "完整版", "蓝光", "高清", "超清",
            "mp4", "mkv", "avi", "ts", "m2ts", "mov", "wmv", "flv",
            "proper", "repack", "extended", "unrated", "limited", "internal", "sub", "subs", "multi"
        };

        // 英文虚词：出现在文件名里通常是片名的一部分，不可能是演员。
        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "of", "in", "on", "at", "to", "for", "a", "an", "or", "but",
            "vol", "part", "cd1", "cd2", "disc"
        };

        // 中日韩字符：用于判断"这是不是一个中文名字"，中文姓名不做英文切分。
        private static readonly Regex CjkRegex = new Regex(@"[\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af]", RegexOptions.Compiled);
        private static readonly Regex YearRegex = new Regex(@"(?<!\d)(19\d{2}|20\d{2})(?!\d)", RegexOptions.Compiled);
        private static readonly Regex ResolutionRegex = new Regex(@"(?<!\d)(2160p|1080p|1080i|720p|480p|4k|8k|uhd)(?!\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex MultiSpace = new Regex(@"[\s\._\-]+", RegexOptions.Compiled);

        /// <summary>把文件名拆成令牌（按 . _ - 空格 分组符号切分）。</summary>
        public static List<string> SplitTokens(string fileNameWithoutExtension)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(fileNameWithoutExtension)) return result;

            foreach (var raw in MultiSpace.Split(fileNameWithoutExtension))
            {
                var t = raw.Trim();
                if (t.Length == 0) continue;
                result.Add(t);
            }
            return result;
        }

        /// <summary>
        /// 解析文件名。演员候选 = 去掉片名、年份、分辨率、噪声令牌后剩下的"像人名"的令牌。
        /// </summary>
        public static FilmParseResult Parse(string fileNameWithoutExtension, string knownTitle = null)
        {
            var result = new FilmParseResult();
            var tokens = SplitTokens(fileNameWithoutExtension);
            result.Tokens.AddRange(tokens);

            var yearMatch = YearRegex.Match(fileNameWithoutExtension ?? string.Empty);
            if (yearMatch.Success) result.Year = yearMatch.Groups[1].Value;

            var resMatch = ResolutionRegex.Match(fileNameWithoutExtension ?? string.Empty);
            if (resMatch.Success) result.Resolution = resMatch.Groups[1].Value;

            // 片名可能跨多个令牌（"The Matrix" → The + Matrix），
            // 所以先把片名拆开，得到"标题令牌集合"再逐令牌排除。
            var titleTokens = BuildTitleTokenSet(tokens, knownTitle);

            foreach (var token in tokens)
            {
                if (IsNoise(token)) continue;
                if (YearRegex.IsMatch(token)) continue;
                if (ResolutionRegex.IsMatch(token)) continue;

                // 令牌里带中文的，通常就是中文演员名（如 "周星驰"），直接收。
                if (CjkRegex.IsMatch(token))
                {
                    if (titleTokens.Contains(token)) continue;
                    AddUnique(result.ActorCandidates, token);
                    continue;
                }

                // 纯英文令牌：要求长度 >= 3 且含有元音，过滤掉 "GRP" "H265" 这类东西。
                if (token.Length < 3) continue;
                if (StopWords.Contains(token)) continue;   // The / And / Of 这类虚词不可能是演员
                if (!LooksLikeName(token)) continue;
                if (titleTokens.Contains(token)) continue;

                AddUnique(result.ActorCandidates, token);
            }

            return result;
        }

        /// <summary>
        /// 构造"标题令牌集合"。
        /// 优先用已知片名拆词；没有片名时，把开头的连续英文词当作片名（常见命名是
        /// "片名.片名.演员.年份"），直到遇到年份/分辨率/噪声为止。
        /// </summary>
        private static HashSet<string> BuildTitleTokenSet(List<string> tokens, string knownTitle)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(knownTitle))
            {
                foreach (var t in SplitTokens(knownTitle)) set.Add(t);
            }

            // 没有可用片名时，退化为"开头若干令牌"启发式。
            if (set.Count == 0)
            {
                foreach (var token in tokens)
                {
                    if (YearRegex.IsMatch(token) || ResolutionRegex.IsMatch(token) || IsNoise(token)) break;
                    if (CjkRegex.IsMatch(token)) break;
                    if (!LooksLikeName(token)) break;
                    set.Add(token);
                }
            }

            return set;
        }

        /// <summary>
        /// 构造占位符上下文。
        /// actors 为整条"演员链"，actors[i] 对应 ${actor1}、${actor2} ...
        /// </summary>
        public static PlaceholderContext BuildContext(FilmParseResult parse, string itemName, string libraryName, string libraryPath)
        {
            var ctx = new PlaceholderContext
            {
                ["year"] = parse?.Year ?? string.Empty,
                ["resolution"] = parse?.Resolution ?? string.Empty,
                ["name"] = itemName ?? string.Empty,
                ["library"] = libraryName ?? string.Empty,
                ["librarypath"] = libraryPath ?? string.Empty,
                ["tokens"] = parse == null ? string.Empty : string.Join(".", parse.Tokens)
            };

            var candidates = parse?.ActorCandidates ?? new List<string>();
            for (int i = 0; i < candidates.Count; i++)
            {
                // 1 基下标，写 ${actor1} 更符合直觉。
                ctx["actor" + (i + 1)] = candidates[i];
            }
            ctx["actors"] = string.Join("|", candidates);
            ctx["actor"] = candidates.Count > 0 ? candidates[0] : string.Empty;

            return ctx;
        }

        public static bool IsNoise(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return true;
            if (NoiseTokens.Contains(token)) return true;

            // "Movie.2023.1080p.BluRay.x264-GROUP" 这类令牌整段是噪声。
            // 注意：不能把 "周星驰-导演" 误判成噪声，所以只做整体匹配，不猜左右两段。
            var dash = token.LastIndexOf('-');
            if (dash > 0 && dash < token.Length - 1)
            {
                var left = token.Substring(0, dash);
                var right = token.Substring(dash + 1);
                if (NoiseTokens.Contains(right) && NoiseTokens.Contains(left)) return true;
            }
            return false;
        }

        private static bool LooksLikeName(string token)
        {
            // 至少一个元音（中文已在上层处理）。
            bool hasVowel = false;
            foreach (var ch in token)
            {
                if ("aeiouAEIOU".IndexOf(ch) >= 0) { hasVowel = true; break; }
            }
            if (!hasVowel) return false;

            // 不允许出现明显不像人名的符号。
            foreach (var ch in token)
            {
                if (!char.IsLetter(ch) && ch != '\'' && ch != '-' && ch != '.') return false;
            }
            return true;
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (list.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) return;
            list.Add(value);
        }
    }

    /// <summary>
    /// 规则引擎：把"文件名 + 规则集"变成"要写入的标签/类型/演员"。
    /// 与 Emby 完全解耦，方便单独跑单元测试。
    /// </summary>
    public static class RuleEngine
    {
        public static readonly char[] SplitChars = { '|', '，', ',' };

        /// <summary>范围判定结果，带原因，便于调试日志。</summary>
        public class ScopeResult
        {
            public bool Allowed { get; set; }
            public string Reason { get; set; }
        }

        /// <summary>
        /// 判定一条规则对某个媒体库是否生效。
        ///
        /// 语义（与界面上的"媒体库多选"完全一致）：
        ///  - 选中列表为空          → 全部媒体库生效
        ///  - 选中列表非空          → 只在这些库生效
        ///  - 排除列表命中          → 一律跳过（优先级最高）
        /// </summary>
        public static ScopeResult ResolveScope(
            IEnumerable<string> selectedLibraries,
            IEnumerable<string> excludedLibraries,
            string libraryName,
            string libraryPath)
        {
            var included = (selectedLibraries ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            var excluded = (excludedLibraries ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

            if (excluded.Any(x => LibraryMatches(x, libraryName, libraryPath)))
            {
                return new ScopeResult { Allowed = false, Reason = $"在排除的媒体库 '{libraryName}' 中" };
            }

            if (included.Count == 0)
            {
                return new ScopeResult { Allowed = true, Reason = "未限定媒体库，全局生效" };
            }

            if (included.Any(x => LibraryMatches(x, libraryName, libraryPath)))
            {
                return new ScopeResult { Allowed = true, Reason = $"命中选中的媒体库 '{libraryName}'" };
            }

            return new ScopeResult { Allowed = false, Reason = $"不在选中的媒体库内（当前 '{libraryName}'）" };
        }

        public static string[] SplitValues(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
            return raw.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries)
                      .Select(x => x.Trim())
                      .Where(x => x.Length > 0)
                      .ToArray();
        }

        /// <summary>判断规则是否适用于该条目所在的媒体库。</summary>
        public static bool AppliesToLibrary(KeywordRule rule, string libraryName, string libraryPath)
        {
            if (rule == null) return false;

            var includes = SplitValues(rule.Libraries);
            var excludes = SplitValues(rule.ExcludedLibraries);

            if (excludes.Length > 0 && excludes.Any(x => LibraryMatches(x, libraryName, libraryPath)))
                return false;

            if (includes.Length == 0) return true;
            return includes.Any(x => LibraryMatches(x, libraryName, libraryPath));
        }

        private static bool LibraryMatches(string candidate, string libraryName, string libraryPath)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            if (!string.IsNullOrEmpty(libraryName) &&
                string.Equals(candidate.Trim(), libraryName.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;

            // 也允许填路径片段，例如 "D:\Movies\4K"。
            return !string.IsNullOrEmpty(libraryPath) &&
                   libraryPath.IndexOf(candidate.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 执行一条规则。返回 null 表示未命中；否则返回一组目标值。
        /// </summary>
        public static string[] Evaluate(KeywordRule rule, string haystack, PlaceholderContext ctx, out string reason)
        {
            reason = null;
            if (rule == null || !rule.Enabled) return null;
            if (string.IsNullOrEmpty(haystack)) return null;

            if (rule.PathContains != null && rule.PathContains.Length > 0 &&
                haystack.IndexOf(rule.PathContains, StringComparison.OrdinalIgnoreCase) < 0)
                return null;

            // 1) 正则模式优先。
            if (!string.IsNullOrWhiteSpace(rule.Pattern))
            {
                Regex regex;
                try
                {
                    regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(500));
                }
                catch (ArgumentException ex)
                {
                    reason = "正则表达式无效: " + ex.Message;
                    return null;
                }

                Match m;
                try
                {
                    m = regex.Match(haystack);
                }
                catch (RegexMatchTimeoutException)
                {
                    reason = "正则匹配超时";
                    return null;
                }

                if (!m.Success) return null;

                // 把命名捕获组塞进上下文供 ${group} 使用；若是 actor 相关组名，
                // 同时作为演员候选，这样"演员"规则的目标值可以留空自动填充。
                var local = new PlaceholderContext();
                var groups = new List<string>();
                foreach (var groupName in regex.GetGroupNames())
                {
                    if (int.TryParse(groupName, out _)) continue; // 跳过无名组的数字索引
                    var value = m.Groups[groupName].Value;
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    value = value.Trim();
                    local[groupName] = value;
                    groups.Add(value);
                }
                local["actors"] = string.Join("|", groups);
                if (groups.Count > 0) local["actor"] = groups[0];
                ctx?.Merge(local);

                return ResolveTargets(rule, ctx, local, out reason);
            }

            // 2) 关键词子串匹配。
            var keywords = SplitValues(rule.Keywords);
            if (keywords.Length == 0) return null;

            foreach (var kw in keywords)
            {
                // 支持 ${actor1} 这种占位符关键词 → 要求"取到的值出现在文件名里"。
                var expanded = ExpandPlaceholders(kw, ctx);
                if (expanded == null) continue; // 占位符没有值，跳过

                if (haystack.IndexOf(expanded, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    reason = "命中关键词: " + expanded;
                    return ResolveTargets(rule, ctx, null, out reason);
                }
            }

            return null;
        }

        private static string[] ResolveTargets(KeywordRule rule, PlaceholderContext ctx, PlaceholderContext local, out string reason)
        {
            reason = null;

            // 目标值留空 → 用占位符的值兜底（演员场景：命中的捕获组/令牌就是演员名）。
            var raw = rule.TargetValues;
            if (string.IsNullOrWhiteSpace(raw))
            {
                var fallback = new List<string>();
                if (local != null)
                {
                    foreach (var key in new[] { "actor", "actor1", "name" })
                    {
                        if (!string.IsNullOrEmpty(local[key])) { fallback.Add(local[key]); }
                    }
                }
                if (fallback.Count == 0 && ctx != null)
                {
                    foreach (var a in SplitValues(ctx["actors"])) fallback.Add(a);
                }
                return fallback.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }

            var result = new List<string>();
            foreach (var piece in SplitValues(raw))
            {
                var expanded = ExpandPlaceholders(piece, ctx);
                if (expanded == null) continue;
                if (string.IsNullOrWhiteSpace(expanded)) continue;
                if (!result.Contains(expanded, StringComparer.OrdinalIgnoreCase)) result.Add(expanded);
            }
            return result.ToArray();
        }

        /// <summary>
        /// 把 ${xxx} 展开成上下文里的值；任一占位符取不到值则返回 null，表示这条不成立。
        /// </summary>
        public static string ExpandPlaceholders(string template, PlaceholderContext ctx)
        {
            if (string.IsNullOrEmpty(template)) return template;
            if (template.IndexOf("${", StringComparison.Ordinal) < 0) return template;

            var sb = new StringBuilder();
            int i = 0;
            while (i < template.Length)
            {
                var open = template.IndexOf("${", i, StringComparison.Ordinal);
                if (open < 0) { sb.Append(template, i, template.Length - i); break; }

                sb.Append(template, i, open - i);

                var close = template.IndexOf('}', open + 2);
                if (close < 0) return null; // 语法不完整

                var key = template.Substring(open + 2, close - open - 2).Trim();
                var value = ctx?[key];
                if (string.IsNullOrEmpty(value)) return null;

                sb.Append(value);
                i = close + 1;
            }
            return sb.ToString();
        }
    }
}
