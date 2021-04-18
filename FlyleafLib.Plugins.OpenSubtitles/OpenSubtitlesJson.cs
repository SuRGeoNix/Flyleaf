﻿using System;
using System.IO;
using System.Net;

namespace FlyleafLib.Plugins
{
    public class OpenSubtitlesJson
    {
        public int Download(string encoding = "utf8") // Download -> Unzip -> ConvertToUtf8
        {
            try
            {
                using (var client = new WebClient())
                {
                    string subDownloadLinkEnc = SubDownloadLink;

                    string zipPath = Path.GetTempPath() + SubFileName + "." + SubLanguageID + ".srt.gz";

                    File.Delete(zipPath);
                    File.Delete(zipPath.Substring(0, zipPath.Length - 3));

                    OpenSubtitles.Log($"Downloading {zipPath}");
                    client.DownloadFile(new Uri(subDownloadLinkEnc), zipPath);

                    OpenSubtitles.Log($"Unzipping {zipPath}");
                    string unzipPath = Utils.GZipDecompress(zipPath);
                    OpenSubtitles.Log($"Unzipped at {unzipPath}");

                    AvailableAt = unzipPath;

                    return 0;
                }
            }
            catch (Exception e) { AvailableAt = null; OpenSubtitles.Log($"Failed to download subtitle {SubFileName} - {e.Message}"); return -1; }
        }
        public bool Equals(OpenSubtitlesJson other)
        {
            if (SubHash == other.SubHash)
                return true;

            return false;
        }


        public string AvailableAt { get; set; }

        public string MatchedBy { get; set; }
        public string IDSubMovieFile { get; set; }
        [System.Xml.Serialization.XmlIgnore]
        public string MovieHash { get; set; }
        public string MovieByteSize { get; set; }
        public string MovieTimeMS { get; set; }
        public string IDSubtitleFile { get; set; }
        public string SubFileName { get; set; }
        public string SubActualCD { get; set; }
        public string SubSize { get; set; }
        public string SubHash { get; set; }
        public string SubLastTS { get; set; }
        public string SubTSGroup { get; set; }
        public string InfoReleaseGroup { get; set; }
        public string InfoFormat { get; set; }
        public string InfoOther { get; set; }
        public string IDSubtitle { get; set; }
        public string UserID { get; set; }
        public string SubLanguageID { get; set; }
        public string SubFormat { get; set; }
        public string SubSumCD { get; set; }
        public string SubAuthorComment { get; set; }
        public string SubAddDate { get; set; }
        public string SubBad { get; set; }
        public string SubRating { get; set; }
        public string SubSumVotes { get; set; }
        public string SubDownloadsCnt { get; set; }
        public string MovieReleaseName { get; set; }
        public string MovieFPS { get; set; }
        public string IDMovie { get; set; }
        public string IDMovieImdb { get; set; }
        public string MovieName { get; set; }
        public string MovieNameEng { get; set; }
        public string MovieYear { get; set; }
        public string MovieImdbRating { get; set; }
        public string SubFeatured { get; set; }
        public string UserNickName { get; set; }
        public string SubTranslator { get; set; }
        public string ISO639 { get; set; }
        public string LanguageName { get; set; }
        public string SubComments { get; set; }
        public string SubHearingImpaired { get; set; }
        public string UserRank { get; set; }
        public string SeriesSeason { get; set; }
        public string SeriesEpisode { get; set; }
        public string MovieKind { get; set; }
        public string SubHD { get; set; }
        public string SeriesIMDBParent { get; set; }
        public string SubEncoding { get; set; }
        public string SubAutoTranslation { get; set; }
        public string SubForeignPartsOnly { get; set; }
        public string SubFromTrusted { get; set; }
        public string SubTSGroupHash { get; set; }
        public string SubDownloadLink { get; set; }
        public string ZipDownloadLink { get; set; }
        public string SubtitlesLink { get; set; }
        public double Score { get; set; }
    }
}