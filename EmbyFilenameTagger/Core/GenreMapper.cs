using System;
using System.Collections.Generic;

namespace EmbyFilenameTagger
{
    /// <summary>
    /// 把用户随手写的中文/英文类型名，映射到 Emby 的英文标准类型名，
    /// 否则同一个类型会分裂成"动作"和"Action"两个分类。
    /// 此文件不依赖 Emby，可单独测试。
    /// </summary>
    public static class GenreMapper
    {
        private static readonly Dictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["动作"] = "Action",
                ["action"] = "Action",
                ["冒险"] = "Adventure",
                ["adventure"] = "Adventure",
                ["动画"] = "Animation",
                ["animation"] = "Animation",
                ["动漫"] = "Animation",
                ["喜剧"] = "Comedy",
                ["comedy"] = "Comedy",
                ["犯罪"] = "Crime",
                ["crime"] = "Crime",
                ["纪录"] = "Documentary",
                ["纪录片"] = "Documentary",
                ["documentary"] = "Documentary",
                ["剧情"] = "Drama",
                ["drama"] = "Drama",
                ["家庭"] = "Family",
                ["family"] = "Family",
                ["奇幻"] = "Fantasy",
                ["fantasy"] = "Fantasy",
                ["历史"] = "History",
                ["history"] = "History",
                ["恐怖"] = "Horror",
                ["horror"] = "Horror",
                ["音乐"] = "Music",
                ["music"] = "Music",
                ["悬疑"] = "Mystery",
                ["mystery"] = "Mystery",
                ["爱情"] = "Romance",
                ["romance"] = "Romance",
                ["科幻"] = "Science Fiction",
                ["sci-fi"] = "Science Fiction",
                ["scifi"] = "Science Fiction",
                ["science fiction"] = "Science Fiction",
                ["惊悚"] = "Thriller",
                ["thriller"] = "Thriller",
                ["战争"] = "War",
                ["war"] = "War",
                ["西部"] = "Western",
                ["western"] = "Western",
                ["武侠"] = "Martial Arts",
                ["武侠片"] = "Martial Arts",
                ["真人秀"] = "Reality",
                ["reality"] = "Reality",
                ["脱口秀"] = "Talk Show",
                ["talk show"] = "Talk Show"
            };

        /// <summary>找不到映射就原样返回，允许用户自定义类型。</summary>
        public static string ToCanonical(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var key = raw.Trim();
            return Map.TryGetValue(key, out var canonical) ? canonical : key;
        }
    }
}
