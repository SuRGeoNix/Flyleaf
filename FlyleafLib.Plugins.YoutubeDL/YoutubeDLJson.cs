using System;
using System.Collections.Generic;

namespace FlyleafLib.Plugins
{
    internal class YoutubeDLJson
    {
        public string url { get; set; }
        public string manifest_url { get; set; }
        //public string fragment_base_url { get; set; }
        public object series { get; set; }
        public string format { get; set; }
        public List<string> tags { get; set; }
        public string channel_id { get; set; }
        public string webpage_url_basename { get; set; }
        public object artist { get; set; }
        public List<Format> formats { get; set; }
        public object chapters { get; set; }
        public object release_year { get; set; }
        public object album { get; set; }
        public object annotations { get; set; }
        public string upload_date { get; set; }
        public string extractor_key { get; set; }
        public object is_live { get; set; }
        public double height { get; set; }
        public string uploader { get; set; }
        public List<Thumbnail> thumbnails { get; set; }
        public object resolution { get; set; }
        public string extractor { get; set; }
        public object vbr { get; set; }
        public double view_count { get; set; }
        public double duration { get; set; }
        public string description { get; set; }
        public object requested_subtitles { get; set; }
        public object episode_number { get; set; }
        public double abr { get; set; }
        public object release_date { get; set; }
        public object start_time { get; set; }
        public object alt_title { get; set; }
        public object creator { get; set; }
        public string acodec { get; set; }
        public float fps { get; set; }
        public string thumbnail { get; set; }
        public double average_rating { get; set; }
        public string ext { get; set; }
        public string _filename { get; set; }
        public Subtitles subtitles { get; set; }
        public object dislike_count { get; set; }
        public string uploader_id { get; set; }
        public object end_time { get; set; }
        public object playlist { get; set; }
        public object license { get; set; }
        public string format_id { get; set; }
        public string channel_url { get; set; }
        public double width { get; set; }
        public string id { get; set; }
        public string display_id { get; set; }
        public object track { get; set; }
        public string uploader_url { get; set; }
        public string title { get; set; }
        public AutomaticCaptions automatic_captions { get; set; }
        public object season_number { get; set; }
        public string webpage_url { get; set; }
        public object like_count { get; set; }
        public List<string> categories { get; set; }
        public string fulltitle { get; set; }
        public double age_limit { get; set; }
        public string vcodec { get; set; }
        public object playlist_index { get; set; }
        public object stretched_ratio { get; set; }

        public class Format
        {
            public string url { get; set; }
            public string manifest_url { get; set; } 
            public object player_url { get; set; }


            public double asr { get; set; }
            public double abr { get; set; }
            public double vbr { get; set; }
            public double tbr { get; set; }

            public string acodec { get; set; }
            public string vcodec { get; set; }

            public double width { get; set; }
            public double height { get; set; }
            public double fps { get; set; }


            public string format { get; set; }
            public string format_id { get; set; }
            public string format_note { get; set; }
            public string ext { get; set; }
            public long filesize { get; set; }
            public string protocol { get; set; }
            public string container { get; set; }
            public string language { get; set; }
            public Dictionary<string, string> downloader_options { get; set; }
            public Dictionary<string, string> http_headers { get; set; }

            public string fragment_base_url { get; set; }
            public List<Fragment> fragments { get; set; }
        }

        public class Fragment
        {
            public string path { get; set; }
            public double duration { get; set; }
        }

        public class Thumbnail
        {
            public string url { get; set; }
            public string resolution { get; set; }
            public double height { get; set; }
            public string id { get; set; }
            public double width { get; set; }
        }

        public class Subtitles
        {
        }

        public class AutomaticCaptions
        {
        }
    }
}