using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static FFmpeg.AutoGen.AVMediaType;

namespace FlyleafLib.MediaFramework
{
    public unsafe class DemuxerInfo
    {
        public string   Name        { get; private set; }
        public string   LongName    { get; private set; }
        public string   Extensions  { get; private set; }

        public long     StartTime   { get; private set; }
        public long     Duration    { get; private set; }

        public static void Fill(Demuxer demuxer)
        {
            DemuxerInfo di = new DemuxerInfo();

            di.Name         = Utils.BytePtrToStringUTF8(demuxer.fmtCtx->iformat->name);
            di.LongName     = Utils.BytePtrToStringUTF8(demuxer.fmtCtx->iformat->long_name);
            di.Extensions   = Utils.BytePtrToStringUTF8(demuxer.fmtCtx->iformat->extensions);
            di.StartTime    = demuxer.fmtCtx->start_time* 10;
            di.Duration     = demuxer.fmtCtx->duration  * 10;

            demuxer.info = di;
            StreamInfo.Fill(demuxer);

            //for (int i=0; i<demuxer.fmtCtx->nb_streams; i++)
            //{
            //    switch (demuxer.fmtCtx->streams[i]->codecpar->codec_type)
            //    {
            //        case AVMEDIA_TYPE_AUDIO:
            //            demuxer.AudioStreams.Add(new EmbeddedAudioStream(demuxer.fmtCtx->streams[i]));
            //            if (demuxer.fmtCtx->streams[i]->duration <= 0) demuxer.AudioStreams[demuxer.AudioStreams.Count-1].Duration = demuxer.decCtx.demuxer.info.Duration;
            //            break;

            //        case AVMEDIA_TYPE_VIDEO:
            //            demuxer.VideoStreams.Add(new EmbeddedVideoStream(demuxer.fmtCtx->streams[i]));
            //            if (demuxer.fmtCtx->streams[i]->duration <= 0) demuxer.VideoStreams[demuxer.VideoStreams.Count-1].Duration = demuxer.decCtx.demuxer.info.Duration;
            //            break;

            //        case AVMEDIA_TYPE_SUBTITLE:
            //            demuxer.SubtitleStreams.Add(new EmbeddedSubtitleStream(demuxer.fmtCtx->streams[i]));
            //            if (demuxer.fmtCtx->streams[i]->duration <= 0) demuxer.SubtitleStreams[demuxer.SubtitleStreams.Count-1].Duration = demuxer.decCtx.demuxer.info.Duration;
            //            break;
            //    }
            //}
        }

        public override string ToString() { return $"{LongName}/{Name} | {Extensions} {new TimeSpan(StartTime)}/{new TimeSpan(Duration)}"; }

        public static string GetDump(Demuxer demuxer) { return demuxer.info.ToString(); }
        public static string GetDumpAll(Demuxer demuxer)
        {
            var dump = demuxer.info.ToString() + "\r\n";

            foreach (var stream in demuxer.streams)
                dump += stream.ToString() + "\r\n";

            return dump.Trim();

            //var dump = demuxer.info.ToString() + "\r\n";

            //foreach(var stream in demuxer.VideoStreams)
            //    dump += stream.GetDump() + "\r\n";

            //foreach(var stream in demuxer.AudioStreams)
            //    dump += stream.GetDump() + "\r\n";

            //foreach(var stream in demuxer.SubtitleStreams)
            //    dump += stream.GetDump() + "\r\n";

            //return dump.Trim();
        }
    }
}
