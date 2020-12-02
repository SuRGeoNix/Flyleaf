using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace SuRGeoNix.Flyleaf
{
    public class History
    {
        public class Entry
        {
            public string   Url                 { get; set; } = null;
            public MediaRouter.InputType UrlType{ get; set; } = MediaRouter.InputType.File;
            public string   TorrentFile         { get; set; } = null;
            public long     OpenedAt            { get; set; } = DateTime.Now.Ticks;
            public int      CurSecond           { get; set; } =  0;
            
            public long     AudioExternalDelay  { get; set; } =  0;
            public long     SubsExternalDelay   { get; set; } =  0;
            public int      CurSubId            { get; set; } = -1;

            [XmlIgnore]
            public List<MediaRouter.SubAvailable> AvailableSubs
            {
                get
                {
                    List<MediaRouter.SubAvailable> ret_availableSubs = new List<MediaRouter.SubAvailable>();

                    if (availableSubs != null && availableSubs.Count > 0)
                    {
                        for (int i = 0; i < availableSubs.Count; i++)
                            ret_availableSubs.Add(availableSubs[i]);
                    }

                    return ret_availableSubs;
                }

                set
                {
                    availableSubs = new List<MediaRouter.SubAvailable>();

                    if (value != null && value.Count > 0)
                    {
                        for (int i = 0; i < value.Count; i++)
                            availableSubs.Add(value[i]);
                    }
                }
            }
            public List<MediaRouter.SubAvailable> availableSubs;

            public Entry() { }
            public Entry(string url) : this(url, MediaRouter.InputType.File, null)      { }
            public Entry(string url, MediaRouter.InputType urlType, string torrentFile)    { Url = url; UrlType = urlType; TorrentFile = torrentFile; }
        }

        public event HistoryChangedHandler HistoryChanged;
        public delegate void HistoryChangedHandler(object source, EventArgs e);

        public List<Entry>  Entries     { get; set; }
        public string       Folder      { get; set; }
        public  int         MaxEntries
        {
            get { lock (locker) return maxEntries; }

            set
            {
                lock (locker)
                {
                    maxEntries = value;

                    Entry last = null;
                    if (Entries.Count != 0)
                    {
                        last = Entries[Entries.Count -1];
                        Entries.RemoveAt(Entries.Count-1);
                    }

                    bool changed = false;
                    int removeCount = Entries.Count - maxEntries;
                    for (int i=0; i<=removeCount; i++) { changed = true; Entries.RemoveAt(0); }

                    if (changed) Save();

                    if (last != null) Entries.Add(last);
                }
            }
        }
        private int         maxEntries;

        readonly object     locker      = new object();

        public History(string folder, int maxEntries)
        {
            Folder = folder;
            //Directory.CreateDirectory(Folder);

            this.maxEntries = maxEntries;
            Load();
            Dump();
        }

        public bool Add(string url, MediaRouter.InputType urlType, string subUrl = null)
        {
            lock (locker)
            {
                Entry entry = null;
            
                int existIndex = Get(url, subUrl);

                if (existIndex != -1)
                {
                    entry = Entries[existIndex];
                    entry.OpenedAt = DateTime.Now.Ticks;
                    RemoveAll(url, subUrl);
                }
                else
                    entry = new Entry(url, urlType, subUrl);

                int removeCount = Entries.Count - maxEntries;
                for (int i=0; i<=removeCount; i++) Entries.RemoveAt(0);

                Save();
                Entries.Add(entry);
                SaveLast();

                HistoryChanged?.Invoke(this, EventArgs.Empty);

                return existIndex != -1;
            }
        }
        public void Update(List<MediaRouter.SubAvailable> availableSubs, int curSubId)
        {
            lock (locker)
            {
                if (Entries.Count == 0) return;

                Entries[Entries.Count - 1].AvailableSubs    = availableSubs;
                Entries[Entries.Count - 1].CurSubId         = curSubId;
                SaveLast();
            }

        }
        public void Update(int curSecond, long audioExternalDelay, long subsExternalDelay)
        {
            lock (locker)
            {
                if (Entries.Count == 0) return;

                Entries[Entries.Count - 1].AudioExternalDelay   = audioExternalDelay;
                Entries[Entries.Count - 1].SubsExternalDelay    = subsExternalDelay;
                Entries[Entries.Count - 1].CurSecond            = curSecond;

                SaveLast();
            }   
        }
        public void RemoveAll(string url, string subUrl = null)
        {
            lock (locker)
            {
                for (int i=Entries.Count-1; i>=0; i--)
                    if (Entries[i].Url == url && (subUrl == null || (Entries[i].TorrentFile != null && Entries[i].TorrentFile == subUrl))) Entries.RemoveAt(i);
            }
        }

        public int Get(string url, string subUrl = null)
        {
            lock (locker)
            {
                for (int i=Entries.Count-1; i>=0; i--)
                    if (Entries[i].Url == url && (subUrl == null || (Entries[i].TorrentFile != null && Entries[i].TorrentFile == subUrl))) return i;

                return -1;
            }
        }
        public Entry GetCurrent()
        {
            lock (locker) return Entries.Count != 0 ? Entries[Entries.Count -1] : null;
        }

        public void Load()
        {
            try
            {
                using (FileStream fs = new FileStream(Path.Combine(Folder, "History.xml"), FileMode.Open))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(List<Entry>));
                    Entries = (List<Entry>)xmlSerializer.Deserialize(fs);
                
                }
            } catch (Exception) { }

            if (Entries == null) Entries = new List<Entry>();

            Entry last = null;
            try
            {
                using (FileStream fs = new FileStream(Path.Combine(Folder, "HistoryLast.xml"), FileMode.Open))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(Entry));
                    last = (Entry)xmlSerializer.Deserialize(fs);
                }
            } catch (Exception) { last = null; }
            
            if (last != null) Entries.Add(last);

            int removeCount = Entries.Count - maxEntries;
            for (int i=0; i<removeCount; i++) Entries.RemoveAt(0);
        }
        public void Save()
        {
            using (FileStream fs = new FileStream(Path.Combine(Folder, "History.xml"), FileMode.Create))
            {
                XmlSerializer xmlSerializer = new XmlSerializer(typeof(List<Entry>));
                xmlSerializer.Serialize(fs, Entries);
            }
        }
        public void SaveLast()
        {
            if (Entries.Count == 0) return;

            try
            {
                using (FileStream fs = new FileStream(Path.Combine(Folder, "HistoryLast.xml"), FileMode.Create))
                {
                    XmlSerializer xmlSerializer = new XmlSerializer(typeof(Entry));
                    xmlSerializer.Serialize(fs, Entries[Entries.Count - 1]);
                }
            } catch (Exception) { }
        }
        public void Clear()
        {
            lock (locker)
            {
                Entries.Clear();
                if (File.Exists(Path.Combine(Folder, "History.xml")))     File.Delete(Path.Combine(Folder, "History.xml"));
                if (File.Exists(Path.Combine(Folder, "HistoryLast.xml"))) File.Delete(Path.Combine(Folder, "HistoryLast.xml"));

                HistoryChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dump()
        {
            string str = "";
            foreach (var h in Entries)
                str += $"DT: {(new DateTime(h.OpenedAt)).ToString()}, URL: {h.Url}, INTURL: {(h.TorrentFile != null ? h.TorrentFile : "")}, SUBS: {(h.AvailableSubs == null ? "0" : h.AvailableSubs.Count.ToString())}, SUBID: {h.CurSubId}\n";
            Console.WriteLine(str);
        }
    }
}