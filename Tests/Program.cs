using System;
using System.Collections.Generic;
using System.Linq;

namespace EmbyFilenameTagger.Tests
{
    /// <summary>
    /// 规则引擎与解析器的单元测试。
    /// 不依赖 Emby 运行时，直接 dotnet run 即可。
    /// </summary>
    public static class Program
    {
        private static int _passed;
        private static int _failed;
        private static readonly List<string> Failures = new List<string>();

        public static int Main()
        {
            // 文件名解析
            TestParse_Basic();
            TestParse_Chinese();
            TestParse_MultiWordTitle();
            TestParse_Noise();

            // 关键词与占位符
            TestKeyword();
            TestMultipleKeywords();
            TestPlaceholder();
            TestRegexActor();

            // 媒体库范围（本次重点）
            TestScope_NoSelectionMeansAll();
            TestScope_SelectedLibraries();
            TestScope_ExcludedLibraries();
            TestScope_ExcludeWins();
            TestScope_PathMatch();

            // 类型归一
            TestGenreMapper();

            // 端到端：规则列表 → 计划
            TestRuleList_PerKind();
            TestRuleList_LibraryIsolation();
            TestRuleList_MultiKind();

            // 配置持久化（XML 序列化）
            ConfigSerializationTests.Run(Assert);

            // 人员排序（制片置顶）
            TestPeopleOrdering();

            // 映射表解析（强制引号）
            TestMapping_Basic();
            TestMapping_RequireQuotes();
            TestMapping_SingleLine();
            TestMapping_Quoted();
            TestMapping_MultiKeywords();
            TestMapping_EmptyTarget();
            TestMapping_CommentsAndBlank();
            TestMapping_FullWidth();
            TestMapping_LegacyMigration();

            Console.WriteLine();
            Console.WriteLine($"===== 通过 {_passed} / 失败 {_failed} =====");
            foreach (var f in Failures) Console.WriteLine("  FAIL: " + f);
            return _failed == 0 ? 0 : 1;
        }

        // ---------------- 解析 ----------------

        private static void TestParse_Basic()
        {
            var p = FilmParser.Parse("The.Matrix.1999.1080p.BluRay.x264-GROUP", "The Matrix");
            Assert("解析年份", p.Year == "1999");
            Assert("解析分辨率", p.Resolution == "1080p");
            Assert("编码不入演员", !p.ActorCandidates.Any(a => a.Equals("x264", StringComparison.OrdinalIgnoreCase)));
        }

        private static void TestParse_Chinese()
        {
            var p = FilmParser.Parse("功夫.周星驰.2004.1080p", "功夫");
            Assert("中文演员被识别", p.ActorCandidates.Contains("周星驰"));
            Assert("中文片名不入演员", !p.ActorCandidates.Contains("功夫"));
        }

        private static void TestParse_MultiWordTitle()
        {
            // 多词片名必须整体排除，不能只排除第一个词
            var p = FilmParser.Parse("The.Matrix.1999.1080p.BluRay", "The Matrix");
            Assert("多词片名 The 不入演员", !p.ActorCandidates.Contains("The"));
            Assert("多词片名 Matrix 不入演员", !p.ActorCandidates.Contains("Matrix"));
        }

        private static void TestParse_Noise()
        {
            Assert("1080p 是噪声", FilmParser.IsNoise("1080p"));
            Assert("x265 是噪声", FilmParser.IsNoise("x265"));
            Assert("中字 是噪声", FilmParser.IsNoise("中字"));
            Assert("人名不是噪声", !FilmParser.IsNoise("周星驰"));
        }

        // ---------------- 关键词 ----------------

        private static void TestKeyword()
        {
            var ctx = new PlaceholderContext();
            var rule = new KeywordRule { Enabled = true, Keywords = "4K", TargetValues = "4K" };

            Assert("关键词命中", RuleEngine.Evaluate(rule, "Movie.2021.4K.HDR", ctx, out _)?.SequenceEqual(new[] { "4K" }) == true);
            Assert("关键词未命中", RuleEngine.Evaluate(rule, "Movie.2021.1080p", ctx, out _) == null);
            Assert("忽略大小写", RuleEngine.Evaluate(rule, "movie.4k", ctx, out _) != null);
        }

        private static void TestMultipleKeywords()
        {
            var ctx = new PlaceholderContext();
            var rule = new KeywordRule { Enabled = true, Keywords = "国语|粤语", TargetValues = "中文配音" };
            Assert("多关键词命中其一", RuleEngine.Evaluate(rule, "片名.粤语.1080p", ctx, out _) != null);

            var disabled = new KeywordRule { Enabled = false, Keywords = "国语", TargetValues = "x" };
            Assert("禁用规则不生效", RuleEngine.Evaluate(disabled, "片名.国语", ctx, out _) == null);
        }

        private static void TestPlaceholder()
        {
            var parse = FilmParser.Parse("Movie.2020.2160p", "Movie");
            var ctx = FilmParser.BuildContext(parse, "Movie", "电影库", @"D:\Movies");

            Assert("占位符展开", RuleEngine.ExpandPlaceholders("${year}-${resolution}", ctx) == "2020-2160p");
            Assert("媒体库占位符", RuleEngine.ExpandPlaceholders("${library}", ctx) == "电影库");
            Assert("缺失占位符返回 null", RuleEngine.ExpandPlaceholders("${不存在}", ctx) == null);
        }

        private static void TestRegexActor()
        {
            var rule = new KeywordRule
            {
                Enabled = true,
                Pattern = @"^(?<actor>[\u4e00-\u9fa5]{2,4})\.",
                TargetValues = "${actor}"
            };
            var result = RuleEngine.Evaluate(rule, "周星驰.功夫.2004", new PlaceholderContext(), out _);
            Assert("正则捕获演员", result != null && result[0] == "周星驰");

            var auto = new KeywordRule { Enabled = true, Pattern = @"^(?<actor>[\u4e00-\u9fa5]{2,4})\." };
            var autoResult = RuleEngine.Evaluate(auto, "成龙.醉拳.1978", new PlaceholderContext(), out _);
            Assert("目标值留空自动取捕获值", autoResult != null && autoResult[0] == "成龙");
        }

        // ---------------- 媒体库范围 ----------------

        private static void TestScope_NoSelectionMeansAll()
        {
            var r = RuleEngine.ResolveScope(new List<string>(), new List<string>(), "任何库", "/data");
            Assert("不选媒体库 = 全局生效", r.Allowed);
        }

        private static void TestScope_SelectedLibraries()
        {
            var selected = new List<string> { "电影", "纪录片" };

            Assert("选中库生效", RuleEngine.ResolveScope(selected, new List<string>(), "电影", @"D:\A").Allowed);
            Assert("选中的第二个库也生效", RuleEngine.ResolveScope(selected, new List<string>(), "纪录片", @"D:\B").Allowed);
            Assert("未选中的库不生效", !RuleEngine.ResolveScope(selected, new List<string>(), "儿童动画", @"D:\C").Allowed);
        }

        private static void TestScope_ExcludedLibraries()
        {
            var excluded = new List<string> { "儿童动画" };

            Assert("排除库不生效", !RuleEngine.ResolveScope(new List<string>(), excluded, "儿童动画", @"D:\Kids").Allowed);
            Assert("非排除库仍生效", RuleEngine.ResolveScope(new List<string>(), excluded, "电影", @"D:\M").Allowed);
        }

        private static void TestScope_ExcludeWins()
        {
            // 同时选中又排除时，排除优先
            var selected = new List<string> { "电影" };
            var excluded = new List<string> { "电影" };
            Assert("排除优先于选中", !RuleEngine.ResolveScope(selected, excluded, "电影", @"D:\M").Allowed);
        }

        private static void TestScope_PathMatch()
        {
            // 库名对不上但路径片段命中时也应生效（应对用户改了库名的情况）
            var selected = new List<string> { "Movies" };
            Assert("路径片段匹配", RuleEngine.ResolveScope(selected, new List<string>(), "影库", @"D:\Movies").Allowed);
        }

        private static void TestGenreMapper()
        {
            Assert("中文类型映射", GenreMapper.ToCanonical("动作") == "Action");
            Assert("英文原样", GenreMapper.ToCanonical("Action") == "Action");
            Assert("未知原样返回", GenreMapper.ToCanonical("自定义") == "自定义");
        }

        // ---------------- 端到端 ----------------

        private static void TestRuleList_PerKind()
        {
            // 模拟界面提交的规则列表，验证三类规则各司其职
            var rules = new List<RuleItem>
            {
                new RuleItem(RuleKind.Tag)   { Keywords = "2160p", TargetValues = "4K" },
                new RuleItem(RuleKind.Genre) { Keywords = "国语", TargetValues = "剧情" },
                new RuleItem(RuleKind.Actor) { Keywords = "周星驰", TargetValues = "周星驰" }
            };

            var hits = EvaluateAll(rules, "周星驰.功夫.2004.2160p.国语", "电影");

            Assert("标签规则产出 4K", hits.Tags.Contains("4K"));
            Assert("类型规则归一为 Drama", hits.Genres.Contains("Drama"));
            Assert("演员规则产出周星驰", hits.Actors.Contains("周星驰"));
            Assert("标签没有串到类型", !hits.Genres.Contains("4K"));
            Assert("类型没有串到标签", !hits.Tags.Contains("Drama"));
        }

        private static void TestRuleList_LibraryIsolation()
        {
            // 用户的核心诉求：某演员的关键词只在指定媒体库生效
            var rules = new List<RuleItem>
            {
                new RuleItem(RuleKind.Actor)
                {
                    Keywords = "周星驰",
                    TargetValues = "周星驰",
                    Libraries = new List<string> { "电影" }
                }
            };

            var inMovie = EvaluateAll(rules, "周星驰.功夫.2004", "电影");
            Assert("指定库内演员生效", inMovie.Actors.Contains("周星驰"));

            var inKids = EvaluateAll(rules, "周星驰.功夫.2004", "儿童动画");
            Assert("其它库演员不生效", inKids.Actors.Count == 0);
        }

        private class Hits
        {
            public List<string> Tags = new List<string>();
            public List<string> Genres = new List<string>();
            public List<string> Actors = new List<string>();
        }

        /// <summary>
        /// 复刻 ItemTagger.BuildPlan 的核心流程（不含 Emby 依赖部分），
        /// 用于验证"规则列表 → 三类产出"。
        /// </summary>
        private static Hits EvaluateAll(List<RuleItem> rules, string fileName, string libraryName)
        {
            var hits = new Hits();

            var parsed = FilmParser.Parse(fileName, null);
            var ctx = FilmParser.BuildContext(parsed, null, libraryName, null);

            foreach (var rule in rules)
            {
                var scope = RuleEngine.ResolveScope(rule.Libraries, rule.ExcludedLibraries, libraryName, null);
                if (!scope.Allowed) continue;

                var engineRule = new KeywordRule
                {
                    Enabled = rule.Enabled,
                    Keywords = rule.Keywords,
                    TargetValues = rule.TargetValues,
                    Pattern = rule.Pattern
                };

                var values = RuleEngine.Evaluate(engineRule, fileName, ctx, out _);
                if (values == null) continue;

                foreach (var v in values)
                {
                    if (string.IsNullOrWhiteSpace(v)) continue;
                    switch (rule.Kind)
                    {
                        case RuleKind.Tag: if (!hits.Tags.Contains(v)) hits.Tags.Add(v); break;
                        case RuleKind.Genre: { var c = GenreMapper.ToCanonical(v); if (!hits.Genres.Contains(c)) hits.Genres.Add(c); break; }
                        case RuleKind.Actor: if (!hits.Actors.Contains(v)) hits.Actors.Add(v); break;
                    }
                }
            }

            return hits;
        }

        // ---------------- 映射表 ----------------

        private static void TestMapping_Basic()
        {
            var e = MappingParser.Parse("\"2160p\"=\"4K\"", out var errs);
            Assert("映射表：单条解析", e.Count == 1 && e[0].Keywords[0] == "2160p" && e[0].Targets[0] == "4K");
            Assert("映射表：无语法错误", errs.Count == 0);

            var e2 = MappingParser.Parse("\"国语\"=\"中文配音\"", out _);
            Assert("映射表：中文目标值", e2[0].Targets[0] == "中文配音");

            var e3 = MappingParser.Parse("\"科幻\"=\"Science Fiction\"", out _);
            Assert("映射表：目标值含空格", e3[0].Targets[0] == "Science Fiction");
        }

        /// <summary>强制引号：不带引号必须报错。</summary>
        private static void TestMapping_RequireQuotes()
        {
            var e = MappingParser.Parse("2160p=4K", out var errs);
            Assert("无引号：不产生条目", e.Count == 0);
            Assert("无引号：给出错误", errs.Count > 0);

            var e2 = MappingParser.Parse("\"2160p\"=4K", out var errs2);
            Assert("目标值缺引号：报错", errs2.Count > 0);

            var e3 = MappingParser.Parse("\"2160p\"=\"4K\"", out var errs3);
            Assert("都带引号：通过", e3.Count == 1 && errs3.Count == 0);
        }

        /// <summary>连续写多条，用逗号分隔。</summary>
        private static void TestMapping_SingleLine()
        {
            var e = MappingParser.Parse("\"2160p\"|\"4K\"|\"UHD\"=\"4K\", \"1080p\"=\"1080P\", \"国语\"|\"粤语\"=\"中文配音\"", out var errs);

            Assert("同行：解析出 3 条", e.Count == 3);
            Assert("同行：无错误", errs.Count == 0);
            Assert("同行：第1条关键词", e[0].Keywords.Count == 3 && e[0].Targets[0] == "4K");
            Assert("同行：第2条", e[1].Keywords[0] == "1080p" && e[1].Targets[0] == "1080P");
            Assert("同行：第3条中文", e[2].Keywords.Count == 2 && e[2].Targets[0] == "中文配音");

            var e2 = MappingParser.Parse("\"a\"=\"X\"\n\"b\"=\"Y\"", out _);
            Assert("换行分隔同样有效", e2.Count == 2);
        }

        /// <summary>引号保护内容里的 = 号与逗号。</summary>
        private static void TestMapping_Quoted()
        {
            var e = MappingParser.Parse("\"A=B\"=\"X=Y\"", out var errs);
            Assert("引号：能解析", e.Count == 1 && errs.Count == 0);
            Assert("引号：关键词保留等号", e[0].Keywords[0] == "A=B");
            Assert("引号：目标值保留等号", e[0].Targets[0] == "X=Y");

            var e2 = MappingParser.Parse("\"带,逗号\"=\"正常\", \"另一条\"=\"值\"", out _);
            Assert("引号：逗号不分段", e2.Count == 2);
            Assert("引号：逗号保留", e2[0].Keywords[0] == "带,逗号");

            var e3 = MappingParser.Parse("\"a|b\"=\"X\"", out _);
            Assert("引号：竖线不分割", e3[0].Keywords.Count == 1 && e3[0].Keywords[0] == "a|b");

            // 括号不需要引号保护，但加了也必须正常
            var e4 = MappingParser.Parse("\"长离（鸣潮）\"=", out _);
            Assert("引号：全角括号正常", e4.Count == 1 && e4[0].Keywords[0] == "长离（鸣潮）");
        }

        private static void TestMapping_MultiKeywords()
        {
            var e = MappingParser.Parse("\"2160p\"|\"4K\"|\"UHD\"=\"4K\"", out _);
            Assert("竖线分隔多关键词", e[0].Keywords.Count == 3);
            Assert("目标值只有 1 个", e[0].Targets.Count == 1);

            var e2 = MappingParser.Parse("\"国语\"、\"粤语\"、\"闽南语\"=\"中文配音\"", out _);
            Assert("顿号分隔多关键词", e2[0].Keywords.Count == 3);

            var e3 = MappingParser.Parse("\"星爷\"=\"周星驰\"|\"Stephen Chow\"", out _);
            Assert("多目标值", e3[0].Targets.Count == 2);
        }

        private static void TestMapping_EmptyTarget()
        {
            var e = MappingParser.Parse("\"周星驰\"=", out var errs);
            Assert("空目标值可解析", e.Count == 1 && e[0].Keywords[0] == "周星驰");
            Assert("空目标值为 0 个", e[0].Targets.Count == 0);
            Assert("空目标值无错误", errs.Count == 0);
        }

        private static void TestMapping_CommentsAndBlank()
        {
            var e = MappingParser.Parse("\"a\"=\"X\", # 注释, \"b\"=\"Y\"", out var errs);
            Assert("忽略注释段", e.Count == 2);

            var e2 = MappingParser.Parse("\"a\"=\"X\"\n\n\n\"b\"=\"Y\"", out _);
            Assert("忽略空行", e2.Count == 2);
        }

        private static void TestMapping_FullWidth()
        {
            var e = MappingParser.Parse("\"动作\"＝\"Action\"", out _);
            Assert("全角等号", e.Count == 1 && e[0].Targets[0] == "Action");

            var e2 = MappingParser.Parse("\"恐怖\"：\"Horror\"", out _);
            Assert("中文冒号", e2.Count == 1 && e2[0].Targets[0] == "Horror");

            var e3 = MappingParser.Parse("\"a\"=\"X\"，\"b\"=\"Y\"", out _);
            Assert("全角逗号分隔", e3.Count == 2);
        }

        private static void TestMapping_LegacyMigration()
        {
            var migrated = MappingParser.FromLegacy("a|b", "X");
            Assert("迁移：旧配置转换带引号", migrated == "\"a\"|\"b\"=\"X\"");

            var e = MappingParser.Parse(migrated, out _);
            Assert("迁移：转换后可解析", e.Count == 1 && e[0].Keywords.Count == 2 && e[0].Targets[0] == "X");

            Assert("迁移：空关键词返回 null", MappingParser.FromLegacy("", "X") == null);
        }

        /// <summary>
        /// 一条规则同时写入多个种类（界面上"命中后同时写入"）。
        /// </summary>
        private static void TestRuleList_MultiKind()
        {
            // 标签规则，同时勾选类型和演员
            var rule = new RuleItem(RuleKind.Tag)
            {
                MappingText = "导演剪辑版=Special",
                Kinds = new List<RuleKind> { RuleKind.Genre, RuleKind.Actor }
            };

            Assert("多类型：有效种类含自身", rule.WritesTo(RuleKind.Tag));
            Assert("多类型：有效种类含类型", rule.WritesTo(RuleKind.Genre));
            Assert("多类型：有效种类含演员", rule.WritesTo(RuleKind.Actor));
            Assert("多类型：种类总数 3", rule.GetEffectiveKinds().Count == 3);

            // 默认（不勾选）只写自身种类
            var single = new RuleItem(RuleKind.Tag) { MappingText = "x=Y" };
            Assert("单类型：只写自身", single.GetEffectiveKinds().Count == 1 && single.WritesTo(RuleKind.Tag));
            Assert("单类型：不写演员", !single.WritesTo(RuleKind.Actor));

            // 全部勾选
            var all = new RuleItem(RuleKind.Actor)
            {
                Kinds = new List<RuleKind> { RuleKind.Tag, RuleKind.Genre }
            };
            Assert("全勾选：3 个种类", all.GetEffectiveKinds().Count == 3);
            Assert("全勾选：无重复", all.GetEffectiveKinds().Distinct().Count() == 3);

            // 重复勾选自身种类不应导致重复
            var dup = new RuleItem(RuleKind.Tag) { Kinds = new List<RuleKind> { RuleKind.Tag, RuleKind.Tag } };
            Assert("重复勾选自身去重", dup.GetEffectiveKinds().Count == 1);
        }

        // ---------------- 人员排序（制片置顶） ----------------

        private static void TestPeopleOrdering()
        {
            // 场景：原本是导演、演员，现在新增制片 → 制片应排第一
            var existing = new List<PersonEntry>
            {
                new PersonEntry { Name = "诺兰", Type = "Director" },
                new PersonEntry { Name = "基利安·墨菲", Type = "Actor" },
                new PersonEntry { Name = "艾米莉·布朗特", Type = "Actor" }
            };

            var addProducers = new List<PersonEntry> { new PersonEntry { Name = "某制片", Type = "Producer" } };
            var addActors = new List<PersonEntry> { new PersonEntry { Name = "马特·达蒙", Type = "Actor" } };

            var merged = PeopleOrdering.Merge(existing, addProducers, addActors);

            Assert("制片置顶：制片排第一", merged[0].Name == "某制片" && merged[0].Type == "Producer");
            Assert("制片置顶：总人数 5", merged.Count == 5);
            Assert("制片置顶：导演仍在", merged.Any(p => p.Name == "诺兰"));
            Assert("制片置顶：新演员在末尾", merged[merged.Count - 1].Name == "马特·达蒙");
            Assert("制片置顶：原有顺序未乱",
                merged[1].Name == "诺兰" && merged[2].Name == "基利安·墨菲" && merged[3].Name == "艾米莉·布朗特");

            // 已在第一位的制片不应被重复添加
            var already = new List<PersonEntry>
            {
                new PersonEntry { Name = "某制片", Type = "Producer" },
                new PersonEntry { Name = "诺兰", Type = "Director" }
            };
            var merged2 = PeopleOrdering.Merge(already, addProducers, null);
            Assert("去重：制片不重复", merged2.Count(p => p.Name == "某制片") == 1);
            Assert("去重：制片仍在第一", merged2[0].Name == "某制片");

            // 没有制片时，顺序完全不变
            var noProducer = PeopleOrdering.Merge(existing, null, null);
            Assert("无制片：顺序不变", noProducer[0].Name == "诺兰" && noProducer.Count == 3);

            // 多个制片
            var many = PeopleOrdering.Merge(
                new List<PersonEntry> { new PersonEntry { Name = "演员A", Type = "Actor" } },
                new List<PersonEntry>
                {
                    new PersonEntry { Name = "制片1", Type = "Producer" },
                    new PersonEntry { Name = "制片2", Type = "Producer" }
                },
                null);
            Assert("多制片：都在前面", many[0].Type == "Producer" && many[1].Type == "Producer");
            Assert("多制片：演员在后", many[2].Name == "演员A");
        }

        // ---------------- 断言 ----------------

        private static void Assert(string name, bool condition)
        {
            if (condition)
            {
                _passed++;
            }
            else
            {
                _failed++;
                Failures.Add(name);
                Console.WriteLine("FAIL: " + name);
            }
        }
    }
}
