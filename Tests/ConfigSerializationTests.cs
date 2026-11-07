using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace EmbyFilenameTagger.Tests
{
    /// <summary>
    /// 配置序列化测试。
    ///
    /// Emby 用 XML 序列化插件配置，如果类型不满足 XML 序列化要求，
    /// 保存会静默失败（配置重启后丢失）。这里直接验证：
    ///  1. 能序列化成 XML
    ///  2. 能反序列化回来且内容一致
    ///  3. 空配置与 null 列表不会让 Emby 崩溃
    /// </summary>
    public static class ConfigSerializationTests
    {
        public static int Run(Action<string, bool> assert)
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "serialize_test");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "config_" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                var config = new TestPluginConfiguration
                {
                    Enabled = true,
                    EnableRealtimeMonitor = false,
                    MergeWithExisting = true,
                    VideosOnly = true,
                    DebugLogging = true,
                    Rules = new List<RuleItem>
                    {
                        new RuleItem(RuleKind.Tag)
                        {
                            Name = "4K 标签",
                            Keywords = "2160p|4K",
                            TargetValues = "4K",
                            Libraries = new List<string> { "电影" },
                            ExcludedLibraries = new List<string>()
                        },
                        new RuleItem(RuleKind.Genre)
                        {
                            Keywords = "国语",
                            TargetValues = "动作|科幻"
                        },
                        new RuleItem(RuleKind.Actor)
                        {
                            Pattern = "^(?<actor>[\\u4e00-\\u9fa5]{2,4})\\.",
                            TargetValues = "",
                            Libraries = new List<string>()
                        }
                    }
                };

                // --- 序列化 ---
                var serializer = new XmlSerializer(typeof(TestPluginConfiguration));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    serializer.Serialize(fs, config);
                }

                assert("配置能写成 XML", File.Exists(path) && new FileInfo(path).Length > 0);

                // --- 反序列化 ---
                TestPluginConfiguration loaded;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    loaded = (TestPluginConfiguration)serializer.Deserialize(fs);
                }

                assert("反序列化成功", loaded != null);
                assert("布尔值保持", loaded.Enabled && !loaded.EnableRealtimeMonitor && loaded.DebugLogging);
                assert("规则数量保持", loaded.Rules != null && loaded.Rules.Count == 3);
                assert("规则种类保持", loaded.Rules[0].Kind == RuleKind.Tag
                                      && loaded.Rules[1].Kind == RuleKind.Genre
                                      && loaded.Rules[2].Kind == RuleKind.Actor);
                assert("关键词保持", loaded.Rules[0].Keywords == "2160p|4K");
                assert("媒体库列表保持", loaded.Rules[0].Libraries.Count == 1
                                        && loaded.Rules[0].Libraries[0] == "电影");
                assert("空列表保持为空", loaded.Rules[2].Libraries != null && loaded.Rules[2].Libraries.Count == 0);
                assert("正则内容保持", loaded.Rules[2].Pattern.Contains("actor"));

                // --- 边界情况 ---
                var emptyConfig = new TestPluginConfiguration();
                var emptyPath = path + ".empty.xml";
                using (var fs = new FileStream(emptyPath, FileMode.Create, FileAccess.Write))
                {
                    serializer.Serialize(fs, emptyConfig);
                }
                TestPluginConfiguration emptyLoaded;
                using (var fs = new FileStream(emptyPath, FileMode.Open, FileAccess.Read))
                {
                    emptyLoaded = (TestPluginConfiguration)serializer.Deserialize(fs);
                }
                assert("空配置可反序列化", emptyLoaded != null);
                assert("空配置规则列表不为 null 或可安全处理",
                    emptyLoaded.Rules == null || emptyLoaded.Rules.Count == 0);

                File.Delete(emptyPath);
            }
            catch (Exception ex)
            {
                assert("配置序列化无异常（" + ex.GetType().Name + ": " + ex.Message + "）", false);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }

            return 0;
        }
    }

    /// <summary>
    /// 测试用的配置类型。
    /// 结构与真实的 PluginConfiguration 一致，但去掉了 Emby 的基类依赖，
    /// 以便在纯 .NET 环境下验证 XML 序列化行为。
    /// </summary>
    public class TestPluginConfiguration
    {
        public bool Enabled { get; set; } = true;
        public bool EnableRealtimeMonitor { get; set; } = true;
        public bool MergeWithExisting { get; set; } = true;
        public bool VideosOnly { get; set; } = true;
        public bool DebugLogging { get; set; }
        public List<RuleItem> Rules { get; set; } = new List<RuleItem>();
    }
}
