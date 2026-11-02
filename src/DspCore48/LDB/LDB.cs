using System.IO;
using System.Xml.Serialization;

namespace DspCore
{
    public static class LDB
    {
        private static readonly object _loadLock = new object();   // 懒加载非线程安全：搜索器多线程首访会竞态（LoadTable 重复/半初始化）
        private static string                                     protoResDir = "Prototypes/";
        public static  ThemeProtoSet _themes;
        public static  VeinProtoSet  _veins;
        public static  ItemProtoSet  _items;

        private static T LoadTable<T>(ref T tmp) where T : ProtoTable
        {
            if ((object) tmp != null)
                return tmp;
            lock (_loadLock)
            {
                if ((object) tmp != null)
                    return tmp;   // 双检锁
                string        str           = LDB.protoResDir + typeof (T).Name;
                XmlSerializer xmlSerializer = new XmlSerializer(typeof (T));
                tmp = xmlSerializer.Deserialize((Stream) File.OpenRead(str + ".xml")) as T;
                if (tmp is VeinProtoSet veinProtoSet)
                    veinProtoSet.OnAfterDeserialize();
                if (tmp is ItemProtoSet itemProtoSet)
                    itemProtoSet.OnAfterDeserialize();
                if (tmp is ThemeProtoSet themeProtoSet)
                    themeProtoSet.OnAfterDeserialize();
                return tmp;
            }
        }

        public static ThemeProtoSet themes => LDB.LoadTable<ThemeProtoSet>(ref LDB._themes);

        public static VeinProtoSet veins => LDB.LoadTable<VeinProtoSet>(ref LDB._veins);

        public static ItemProtoSet items => LDB.LoadTable<ItemProtoSet>(ref LDB._items);

        /// <summary>按 id 取主题（搜索器用，等价 themes.Select）。</summary>
        public static ThemeProto Theme(int id) => themes.Select(id);
    }
    
    public abstract class ProtoTable
    {
        public string TableName;

        public abstract void Init(int length);

        public abstract Proto this[int index] { get; set; }

        public abstract int Length { get; }
    }
}