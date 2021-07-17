using System;
using System.Collections.Generic;
using System.Linq;

namespace FlyleafLib
{
    // https://www.opensubtitles.org/addons/export_languages.php
    public class Language
    {
        public string IdSubLanguage;
        public string ISO639;
        public string LanguageName;
        public string UploadEnabled;
        public string WebEnabled;

        public Language()
        {
            var lang = Get("eng"); IdSubLanguage = lang.IdSubLanguage; ISO639 = lang.ISO639; LanguageName = lang.LanguageName;
        }

        public Language(string IdSubLanguage, string ISO639, string LanguageName, string UploadEnabled, string WebEnabled)
        {
            this.IdSubLanguage = IdSubLanguage;
            this.ISO639 = ISO639;
            this.LanguageName = LanguageName;
            this.UploadEnabled = UploadEnabled;
            this.WebEnabled = WebEnabled;
        }

        

        private static readonly IDictionary<string, string> AlternativeNames = new Dictionary<string, string>
        {
            { "nld", "dut" },
            { "deu", "ger" },
            { "fra", "fre" },
            { "gre", "ell" },
            { "gr", "el" }
        };

        public static Language Get(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return Get("und");
            }

            var res = Master.Languages.FirstOrDefault(lang => string.Equals(name, lang.IdSubLanguage, StringComparison.OrdinalIgnoreCase) ||
                                                       string.Equals(name, lang.ISO639, StringComparison.OrdinalIgnoreCase) ||
                                                       string.Equals(name, lang.LanguageName, StringComparison.OrdinalIgnoreCase));

            if (res != null)
            {
                return res;
            }

            if (AlternativeNames.TryGetValue(name, out var alternativeName))
            {
                return Get(alternativeName);
            }

            return Get("und");
        }

        public override string ToString()
        {
            return LanguageName;
        }
    }
}