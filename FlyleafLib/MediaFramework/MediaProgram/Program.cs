﻿using System.Collections.Generic;
using System.Linq;

using FlyleafLib.MediaFramework.MediaDemuxer;
using FlyleafLib.MediaFramework.MediaStream;

namespace FlyleafLib.MediaFramework.MediaProgram;

public class Program
{
    public int      ProgramNumber   { get; internal set; }

    public int      ProgramId       { get; internal set; }

    public IReadOnlyDictionary<string, string>
                    Metadata        { get; internal set; }

    public IReadOnlyList<StreamBase>
                    Streams         { get; internal set; }

    public string   Name            => Metadata.ContainsKey("name") ? Metadata["name"] : string.Empty;

    public unsafe Program(AVProgram* program, Demuxer demuxer)
    {
        ProgramNumber = program->program_num;
        ProgramId = program->id;

        // Load stream info
        List<StreamBase> streams = new(3);
        for(int s = 0; s<program->nb_stream_indexes; s++)
        {
            uint streamIndex = program->stream_index[s];
            StreamBase stream = null;
            stream =  demuxer.AudioStreams.FirstOrDefault(it=>it.StreamIndex == streamIndex);

            if (stream == null)
            {
                stream = demuxer.VideoStreams.FirstOrDefault(it => it.StreamIndex == streamIndex);
                stream ??= demuxer.SubtitlesStreams.FirstOrDefault(it => it.StreamIndex == streamIndex);
            }
            if (stream!=null)
            {
                streams.Add(stream);
            }
        }
        Streams = streams;

        // Load metadata
        Dictionary<string, string> metadata = new();
        AVDictionaryEntry* b = null;
        while (true)
        {
            b = av_dict_get(program->metadata, "", b, DictReadFlags.IgnoreSuffix);
            if (b == null) break;
            metadata.Add(Utils.BytePtrToStringUTF8(b->key), Utils.BytePtrToStringUTF8(b->value));
        }
        Metadata = metadata;
    }
}
