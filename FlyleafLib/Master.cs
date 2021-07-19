﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using static FFmpeg.AutoGen.ffmpeg;

using FlyleafLib.MediaPlayer;
using FlyleafLib.Plugins;

namespace FlyleafLib
{
    /// <summary>
    /// Manages library's static configuration
    /// </summary>
    public static class Master
    {
        static Master()
        {
            AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolve;
            
            Plugins     = new List<Type>();
            Players     = new Dictionary<int, Player>();
            AudioMaster = new AudioMaster();
            LoadPlugins();
        }

        /// <summary>
        /// Manages audio devices, volume &amp; mute
        /// </summary>
        public static AudioMaster   AudioMaster         { get; }

        /// <summary>
        /// Holds player instances
        /// </summary>
        public static Dictionary<int, Player>  Players  { get; }

        /// <summary>
        /// Disables aborts (mainly required during seek) (Testing support for .NET 5)
        /// </summary>
        public static bool          PreventAborts       { get;  set; }

        /// <summary>
        /// Prevent auto dispose of the player during window close/unload
        /// </summary>
        public static bool          PreventAutoDispose  { get;  set; }

        /// <summary>
        /// Holds loaded plugin types
        /// </summary>
        public static List<Type>    Plugins             { get; }
        
        internal static void DisposePlayer(Player player)
        {
            if (player == null) return;

            DisposePlayer(player.PlayerId);
            player = null;
        }

        internal static void DisposePlayer(int playerId)
        {
            if (!Players.ContainsKey(playerId)) return;

            Player player = Players[playerId];
            player.DisposeInternal();
            Players.Remove(playerId);
        }
        private static void LoadPlugins()
        {
            // Load .dll Assemblies
            if (Directory.Exists("Plugins"))
            {
                string[] dirs = Directory.GetDirectories("Plugins");
                foreach(string dir in dirs)
                    foreach(string file in Directory.GetFiles(dir, "*.dll"))
                        try { Assembly.LoadFrom(Path.GetFullPath(file));}
                        catch (Exception e) { Log($"[Plugins] [Error] Failed to load assembly ({e.Message} {Utils.GetRecInnerException(e)})"); }
            }
            

            // Find PluginBase Types | Try Catch in for can crash if older version exists
            List<Type> types        = new List<Type>();
            Type pluginBaseType     = typeof(PluginBase);
            Assembly[] assemblies   = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
                try
                {
                    foreach (var type in assembly.GetTypes())
                        if (pluginBaseType.IsAssignableFrom(type) && type.IsClass && type.Name != nameof(PluginBase))
                            types.Add(type);
                } catch (Exception) { }

            // Load Plugins
            foreach (var type in types)
                { Log($"[PluginLoader] {type.FullName}"); Plugins.Add(type); }
        }

        private static Assembly AssemblyResolve(object sender, ResolveEventArgs args)
        {
            // Fix Assemblies redirect bindings and binaryFormater (currently just for BitSwarm plugin)
            try
            {
                foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (assembly.GetName().Name == (new AssemblyName(args.Name)).Name && (assembly.GetName().Name == "BitSwarmLib" || assembly.GetName().Name == "System.Buffers"))
                {
                    Log($"[AssemblyResolver] Found {assembly.FullName}");
                    return assembly;
                }

                Log($"[AssemblyResolver] for {args.Name} not found");
            } catch (Exception) { }

            return null;
        }

        /// <summary>
        /// Registers FFmpeg libraries (ensure you provide x86 or x64 based on your project)
        /// </summary>
        /// <param name="absolutePath">Provide your custom absolute path or :1 for current or :2 for Libs\(x86 or x64 dynamic)\FFmpeg from current to base</param>
        /// <param name="verbosity">FFmpeg's verbosity (24: Warning, 64: Max offset ...) (used only in DEBUG)</param>
        public static void RegisterFFmpeg(string absolutePath = ":1", int verbosity = AV_LOG_WARNING) //AV_LOG_MAX_OFFSET
        {
            if (Utils.IsDesignMode || alreadyRegister) return;
            alreadyRegister = true;
            RootPath        = null;

            if (absolutePath == ":1") 
                RootPath = Environment.CurrentDirectory;
            else if (absolutePath != ":2")
                RootPath = absolutePath;
            else
            {
                var current = Environment.CurrentDirectory;
                var probe   = Path.Combine("Libs", Environment.Is64BitProcess ? "x64" : "x86", "FFmpeg");

                while (current != null)
                {
                    var ffmpegBinaryPath = Path.Combine(current, probe);
                    if (Directory.Exists(ffmpegBinaryPath)) { RootPath = ffmpegBinaryPath; break; }
                    current = Directory.GetParent(current)?.FullName;
                }
            }

            if (RootPath == null) throw new Exception("Failed to register FFmpeg libraries");

            try
            {
                #if DEBUG
                    av_log_set_level(verbosity);
                    av_log_set_callback(Utils.FFmpeg.ffmpegLogCallback);
                #endif

                uint ver = avformat_version();
                Log($"[FFmpegLoader] [Version: {ver >> 16}.{ver >> 8 & 255}.{ver & 255}] [Location: {RootPath}]");
            } catch (Exception e) { throw new Exception("Failed to register FFmpeg libraries", e); }
        }
        static bool alreadyRegister = false;

        public static readonly List<Language> Languages = new List<Language>
        {
            new Language("aar","aa","Afar, afar","0","0"),
            new Language("abk","ab","Abkhazian","1","0"),
            new Language("ace","","Achinese","0","0"),
            new Language("ach","","Acoli","0","0"),
            new Language("ada","","Adangme","0","0"),
            new Language("ady","","adyghé","0","0"),
            new Language("afa","","Afro-Asiatic (Other)","0","0"),
            new Language("afh","","Afrihili","0","0"),
            new Language("afr","af","Afrikaans","1","0"),
            new Language("ain","","Ainu","0","0"),
            new Language("aka","ak","Akan","0","0"),
            new Language("akk","","Akkadian","0","0"),
            new Language("alb","sq","Albanian","1","1"),
            new Language("ale","","Aleut","0","0"),
            new Language("alg","","Algonquian languages","0","0"),
            new Language("alt","","Southern Altai","0","0"),
            new Language("amh","am","Amharic","0","0"),
            new Language("ang","","English, Old (ca.450-1100)","0","0"),
            new Language("apa","","Apache languages","0","0"),
            new Language("ara","ar","Arabic","1","1"),
            new Language("arc","","Aramaic","0","0"),
            new Language("arg","an","Aragonese","1","1"),
            new Language("arm","hy","Armenian","1","1"),
            new Language("arn","","Araucanian","0","0"),
            new Language("arp","","Arapaho","0","0"),
            new Language("art","","Artificial (Other)","0","0"),
            new Language("arw","","Arawak","0","0"),
            new Language("asm","as","Assamese","1","0"),
            new Language("ast","at","Asturian","1","1"),
            new Language("ath","","Athapascan languages","0","0"),
            new Language("aus","","Australian languages","0","0"),
            new Language("ava","av","Avaric","0","0"),
            new Language("ave","ae","Avestan","0","0"),
            new Language("awa","","Awadhi","0","0"),
            new Language("aym","ay","Aymara","0","0"),
            new Language("aze","az","Azerbaijani","1","0"),
            new Language("bad","","Banda","0","0"),
            new Language("bai","","Bamileke languages","0","0"),
            new Language("bak","ba","Bashkir","0","0"),
            new Language("bal","","Baluchi","0","0"),
            new Language("bam","bm","Bambara","0","0"),
            new Language("ban","","Balinese","0","0"),
            new Language("baq","eu","Basque","1","1"),
            new Language("bas","","Basa","0","0"),
            new Language("bat","","Baltic (Other)","0","0"),
            new Language("bej","","Beja","0","0"),
            new Language("bel","be","Belarusian","1","0"),
            new Language("bem","","Bemba","0","0"),
            new Language("ben","bn","Bengali","1","0"),
            new Language("ber","","Berber (Other)","0","0"),
            new Language("bho","","Bhojpuri","0","0"),
            new Language("bih","bh","Bihari","0","0"),
            new Language("bik","","Bikol","0","0"),
            new Language("bin","","Bini","0","0"),
            new Language("bis","bi","Bislama","0","0"),
            new Language("bla","","Siksika","0","0"),
            new Language("bnt","","Bantu (Other)","0","0"),
            new Language("bos","bs","Bosnian","1","0"),
            new Language("bra","","Braj","0","0"),
            new Language("bre","br","Breton","1","1"),
            new Language("btk","","Batak (Indonesia)","0","0"),
            new Language("bua","","Buriat","0","0"),
            new Language("bug","","Buginese","0","0"),
            new Language("bul","bg","Bulgarian","1","1"),
            new Language("bur","my","Burmese","1","0"),
            new Language("byn","","Blin","0","0"),
            new Language("cad","","Caddo","0","0"),
            new Language("cai","","Central American Indian (Other)","0","0"),
            new Language("car","","Carib","0","0"),
            new Language("cat","ca","Catalan","1","1"),
            new Language("cau","","Caucasian (Other)","0","0"),
            new Language("ceb","","Cebuano","0","0"),
            new Language("cel","","Celtic (Other)","0","0"),
            new Language("cha","ch","Chamorro","0","0"),
            new Language("chb","","Chibcha","0","0"),
            new Language("che","ce","Chechen","0","0"),
            new Language("chg","","Chagatai","0","0"),
            new Language("chi","zh","Chinese (simplified)","1","1"),
            new Language("chk","","Chuukese","0","0"),
            new Language("chm","","Mari","0","0"),
            new Language("chn","","Chinook jargon","0","0"),
            new Language("cho","","Choctaw","0","0"),
            new Language("chp","","Chipewyan","0","0"),
            new Language("chr","","Cherokee","0","0"),
            new Language("chu","cu","Church Slavic","0","0"),
            new Language("chv","cv","Chuvash","0","0"),
            new Language("chy","","Cheyenne","0","0"),
            new Language("cmc","","Chamic languages","0","0"),
            new Language("cop","","Coptic","0","0"),
            new Language("cor","kw","Cornish","0","0"),
            new Language("cos","co","Corsican","0","0"),
            new Language("cpe","","Creoles and pidgins, English based (Other)","0","0"),
            new Language("cpf","","Creoles and pidgins, French-based (Other)","0","0"),
            new Language("cpp","","Creoles and pidgins, Portuguese-based (Other)","0","0"),
            new Language("cre","cr","Cree","0","0"),
            new Language("crh","","Crimean Tatar","0","0"),
            new Language("crp","","Creoles and pidgins (Other)","0","0"),
            new Language("csb","","Kashubian","0","0"),
            new Language("cus","","Cushitic (Other)' couchitiques, autres langues","0","0"),
            new Language("cze","cs","Czech","1","1"),
            new Language("dak","","Dakota","0","0"),
            new Language("dan","da","Danish","1","1"),
            new Language("dar","","Dargwa","0","0"),
            new Language("day","","Dayak","0","0"),
            new Language("del","","Delaware","0","0"),
            new Language("den","","Slave (Athapascan)","0","0"),
            new Language("dgr","","Dogrib","0","0"),
            new Language("din","","Dinka","0","0"),
            new Language("div","dv","Divehi","0","0"),
            new Language("doi","","Dogri","0","0"),
            new Language("dra","","Dravidian (Other)","0","0"),
            new Language("dua","","Duala","0","0"),
            new Language("dum","","Dutch, Middle (ca.1050-1350)","0","0"),
            new Language("dut","nl","Dutch","1","1"),
            new Language("dyu","","Dyula","0","0"),
            new Language("dzo","dz","Dzongkha","0","0"),
            new Language("efi","","Efik","0","0"),
            new Language("egy","","Egyptian (Ancient)","0","0"),
            new Language("eka","","Ekajuk","0","0"),
            new Language("elx","","Elamite","0","0"),
            new Language("eng","en","English","1","1"),
            new Language("enm","","English, Middle (1100-1500)","0","0"),
            new Language("epo","eo","Esperanto","1","1"),
            new Language("est","et","Estonian","1","1"),
            new Language("ewe","ee","Ewe","0","0"),
            new Language("ewo","","Ewondo","0","0"),
            new Language("fan","","Fang","0","0"),
            new Language("fao","fo","Faroese","0","0"),
            new Language("fat","","Fanti","0","0"),
            new Language("fij","fj","Fijian","0","0"),
            new Language("fil","","Filipino","0","0"),
            new Language("fin","fi","Finnish","1","1"),
            new Language("fiu","","Finno-Ugrian (Other)","0","0"),
            new Language("fon","","Fon","0","0"),
            new Language("fre","fr","French","1","1"),
            new Language("frm","","French, Middle (ca.1400-1600)","0","0"),
            new Language("fro","","French, Old (842-ca.1400)","0","0"),
            new Language("fry","fy","Frisian","0","0"),
            new Language("ful","ff","Fulah","0","0"),
            new Language("fur","","Friulian","0","0"),
            new Language("gaa","","Ga","0","0"),
            new Language("gay","","Gayo","0","0"),
            new Language("gba","","Gbaya","0","0"),
            new Language("gem","","Germanic (Other)","0","0"),
            new Language("geo","ka","Georgian","1","1"),
            new Language("ger","de","German","1","1"),
            new Language("gez","","Geez","0","0"),
            new Language("gil","","Gilbertese","0","0"),
            new Language("gla","gd","Gaelic","1","0"),
            new Language("gle","ga","Irish","1","0"),
            new Language("glg","gl","Galician","1","1"),
            new Language("glv","gv","Manx","0","0"),
            new Language("gmh","","German, Middle High (ca.1050-1500)","0","0"),
            new Language("goh","","German, Old High (ca.750-1050)","0","0"),
            new Language("gon","","Gondi","0","0"),
            new Language("gor","","Gorontalo","0","0"),
            new Language("got","","Gothic","0","0"),
            new Language("grb","","Grebo","0","0"),
            new Language("grc","","Greek, Ancient (to 1453)","0","0"),
            new Language("ell","el","Greek","1","1"),
            new Language("grn","gn","Guarani","0","0"),
            new Language("guj","gu","Gujarati","0","0"),
            new Language("gwi","","Gwich´in","0","0"),
            new Language("hai","","Haida","0","0"),
            new Language("hat","ht","Haitian","0","0"),
            new Language("hau","ha","Hausa","0","0"),
            new Language("haw","","Hawaiian","0","0"),
            new Language("heb","he","Hebrew","1","1"),
            new Language("her","hz","Herero","0","0"),
            new Language("hil","","Hiligaynon","0","0"),
            new Language("him","","Himachali","0","0"),
            new Language("hin","hi","Hindi","1","1"),
            new Language("hit","","Hittite","0","0"),
            new Language("hmn","","Hmong","0","0"),
            new Language("hmo","ho","Hiri Motu","0","0"),
            new Language("hrv","hr","Croatian","1","1"),
            new Language("hun","hu","Hungarian","1","1"),
            new Language("hup","","Hupa","0","0"),
            new Language("iba","","Iban","0","0"),
            new Language("ibo","ig","Igbo","1","0"),
            new Language("ice","is","Icelandic","1","1"),
            new Language("ido","io","Ido","0","0"),
            new Language("iii","ii","Sichuan Yi","0","0"),
            new Language("ijo","","Ijo","0","0"),
            new Language("iku","iu","Inuktitut","0","0"),
            new Language("ile","ie","Interlingue","0","0"),
            new Language("ilo","","Iloko","0","0"),
            new Language("ina","ia","Interlingua","1","0"),
            new Language("inc","","Indic (Other)","0","0"),
            new Language("ind","id","Indonesian","1","1"),
            new Language("ine","","Indo-European (Other)","0","0"),
            new Language("inh","","Ingush","0","0"),
            new Language("ipk","ik","Inupiaq","0","0"),
            new Language("ira","","Iranian (Other)","0","0"),
            new Language("iro","","Iroquoian languages","0","0"),
            new Language("ita","it","Italian","1","1"),
            new Language("jav","jv","Javanese","0","0"),
            new Language("jpn","ja","Japanese","1","1"),
            new Language("jpr","","Judeo-Persian","0","0"),
            new Language("jrb","","Judeo-Arabic","0","0"),
            new Language("kaa","","Kara-Kalpak","0","0"),
            new Language("kab","","Kabyle","0","0"),
            new Language("kac","","Kachin","0","0"),
            new Language("kal","kl","Kalaallisut","0","0"),
            new Language("kam","","Kamba","0","0"),
            new Language("kan","kn","Kannada","1","0"),
            new Language("kar","","Karen","0","0"),
            new Language("kas","ks","Kashmiri","0","0"),
            new Language("kau","kr","Kanuri","0","0"),
            new Language("kaw","","Kawi","0","0"),
            new Language("kaz","kk","Kazakh","1","0"),
            new Language("kbd","","Kabardian","0","0"),
            new Language("kha","","Khasi","0","0"),
            new Language("khi","","Khoisan (Other)","0","0"),
            new Language("khm","km","Khmer","1","1"),
            new Language("kho","","Khotanese","0","0"),
            new Language("kik","ki","Kikuyu","0","0"),
            new Language("kin","rw","Kinyarwanda","0","0"),
            new Language("kir","ky","Kirghiz","0","0"),
            new Language("kmb","","Kimbundu","0","0"),
            new Language("kok","","Konkani","0","0"),
            new Language("kom","kv","Komi","0","0"),
            new Language("kon","kg","Kongo","0","0"),
            new Language("kor","ko","Korean","1","1"),
            new Language("kos","","Kosraean","0","0"),
            new Language("kpe","","Kpelle","0","0"),
            new Language("krc","","Karachay-Balkar","0","0"),
            new Language("kro","","Kru","0","0"),
            new Language("kru","","Kurukh","0","0"),
            new Language("kua","kj","Kuanyama","0","0"),
            new Language("kum","","Kumyk","0","0"),
            new Language("kur","ku","Kurdish","1","0"),
            new Language("kut","","Kutenai","0","0"),
            new Language("lad","","Ladino","0","0"),
            new Language("lah","","Lahnda","0","0"),
            new Language("lam","","Lamba","0","0"),
            new Language("lao","lo","Lao","0","0"),
            new Language("lat","la","Latin","0","0"),
            new Language("lav","lv","Latvian","1","0"),
            new Language("lez","","Lezghian","0","0"),
            new Language("lim","li","Limburgan","0","0"),
            new Language("lin","ln","Lingala","0","0"),
            new Language("lit","lt","Lithuanian","1","0"),
            new Language("lol","","Mongo","0","0"),
            new Language("loz","","Lozi","0","0"),
            new Language("ltz","lb","Luxembourgish","1","0"),
            new Language("lua","","Luba-Lulua","0","0"),
            new Language("lub","lu","Luba-Katanga","0","0"),
            new Language("lug","lg","Ganda","0","0"),
            new Language("lui","","Luiseno","0","0"),
            new Language("lun","","Lunda","0","0"),
            new Language("luo","","Luo (Kenya and Tanzania)","0","0"),
            new Language("lus","","lushai","0","0"),
            new Language("mac","mk","Macedonian","1","1"),
            new Language("mad","","Madurese","0","0"),
            new Language("mag","","Magahi","0","0"),
            new Language("mah","mh","Marshallese","0","0"),
            new Language("mai","","Maithili","0","0"),
            new Language("mak","","Makasar","0","0"),
            new Language("mal","ml","Malayalam","1","0"),
            new Language("man","","Mandingo","0","0"),
            new Language("mao","mi","Maori","0","0"),
            new Language("map","","Austronesian (Other)","0","0"),
            new Language("mar","mr","Marathi","0","0"),
            new Language("mas","","Masai","0","0"),
            new Language("may","ms","Malay","1","1"),
            new Language("mdf","","Moksha","0","0"),
            new Language("mdr","","Mandar","0","0"),
            new Language("men","","Mende","0","0"),
            new Language("mga","","Irish, Middle (900-1200)","0","0"),
            new Language("mic","","Mi'kmaq","0","0"),
            new Language("min","","Minangkabau","0","0"),
            new Language("mis","","Miscellaneous languages","0","0"),
            new Language("mkh","","Mon-Khmer (Other)","0","0"),
            new Language("mlg","mg","Malagasy","0","0"),
            new Language("mlt","mt","Maltese","0","0"),
            new Language("mnc","","Manchu","0","0"),
            new Language("mni","ma","Manipuri","1","0"),
            new Language("mno","","Manobo languages","0","0"),
            new Language("moh","","Mohawk","0","0"),
            new Language("mol","mo","Moldavian","0","0"),
            new Language("mon","mn","Mongolian","1","0"),
            new Language("mos","","Mossi","0","0"),
            new Language("mwl","","Mirandese","0","0"),
            new Language("mul","","Multiple languages","0","0"),
            new Language("mun","","Munda languages","0","0"),
            new Language("mus","","Creek","0","0"),
            new Language("mwr","","Marwari","0","0"),
            new Language("myn","","Mayan languages","0","0"),
            new Language("myv","","Erzya","0","0"),
            new Language("nah","","Nahuatl","0","0"),
            new Language("nai","","North American Indian","0","0"),
            new Language("nap","","Neapolitan","0","0"),
            new Language("nau","na","Nauru","0","0"),
            new Language("nav","nv","Navajo","1","0"),
            new Language("nbl","nr","Ndebele, South","0","0"),
            new Language("nde","nd","Ndebele, North","0","0"),
            new Language("ndo","ng","Ndonga","0","0"),
            new Language("nds","","Low German","0","0"),
            new Language("nep","ne","Nepali","1","0"),
            new Language("new","","Nepal Bhasa","0","0"),
            new Language("nia","","Nias","0","0"),
            new Language("nic","","Niger-Kordofanian (Other)","0","0"),
            new Language("niu","","Niuean","0","0"),
            new Language("nno","nn","Norwegian Nynorsk","0","0"),
            new Language("nob","nb","Norwegian Bokmal","0","0"),
            new Language("nog","","Nogai","0","0"),
            new Language("non","","Norse, Old","0","0"),
            new Language("nor","no","Norwegian","1","1"),
            new Language("nso","","Northern Sotho","0","0"),
            new Language("nub","","Nubian languages","0","0"),
            new Language("nwc","","Classical Newari","0","0"),
            new Language("nya","ny","Chichewa","0","0"),
            new Language("nym","","Nyamwezi","0","0"),
            new Language("nyn","","Nyankole","0","0"),
            new Language("nyo","","Nyoro","0","0"),
            new Language("nzi","","Nzima","0","0"),
            new Language("oci","oc","Occitan","1","1"),
            new Language("oji","oj","Ojibwa","0","0"),
            new Language("ori","or","Odia","1","0"),
            new Language("orm","om","Oromo","0","0"),
            new Language("osa","","Osage","0","0"),
            new Language("oss","os","Ossetian","0","0"),
            new Language("ota","","Turkish, Ottoman (1500-1928)","0","0"),
            new Language("oto","","Otomian languages","0","0"),
            new Language("paa","","Papuan (Other)","0","0"),
            new Language("pag","","Pangasinan","0","0"),
            new Language("pal","","Pahlavi","0","0"),
            new Language("pam","","Pampanga","0","0"),
            new Language("pan","pa","Panjabi","0","0"),
            new Language("pap","","Papiamento","0","0"),
            new Language("pau","","Palauan","0","0"),
            new Language("peo","","Persian, Old (ca.600-400 B.C.)","0","0"),
            new Language("per","fa","Persian","1","1"),
            new Language("phi","","Philippine (Other)","0","0"),
            new Language("phn","","Phoenician","0","0"),
            new Language("pli","pi","Pali","0","0"),
            new Language("pol","pl","Polish","1","1"),
            new Language("pon","","Pohnpeian","0","0"),
            new Language("por","pt","Portuguese","1","1"),
            new Language("pra","","Prakrit languages","0","0"),
            new Language("pro","","Provençal, Old (to 1500)","0","0"),
            new Language("pus","ps","Pushto","0","0"),
            new Language("que","qu","Quechua","0","0"),
            new Language("raj","","Rajasthani","0","0"),
            new Language("rap","","Rapanui","0","0"),
            new Language("rar","","Rarotongan","0","0"),
            new Language("roa","","Romance (Other)","0","0"),
            new Language("roh","rm","Raeto-Romance","0","0"),
            new Language("rom","","Romany","0","0"),
            new Language("run","rn","Rundi","0","0"),
            new Language("rup","","Aromanian","0","0"),
            new Language("rus","ru","Russian","1","1"),
            new Language("sad","","Sandawe","0","0"),
            new Language("sag","sg","Sango","0","0"),
            new Language("sah","","Yakut","0","0"),
            new Language("sai","","South American Indian (Other)","0","0"),
            new Language("sal","","Salishan languages","0","0"),
            new Language("sam","","Samaritan Aramaic","0","0"),
            new Language("san","sa","Sanskrit","0","0"),
            new Language("sas","","Sasak","0","0"),
            new Language("sat","","Santali","0","0"),
            new Language("scc","sr","Serbian","1","1"),
            new Language("scn","","Sicilian","0","0"),
            new Language("sco","","Scots","0","0"),
            new Language("sel","","Selkup","0","0"),
            new Language("sem","","Semitic (Other)","0","0"),
            new Language("sga","","Irish, Old (to 900)","0","0"),
            new Language("sgn","","Sign Languages","0","0"),
            new Language("shn","","Shan","0","0"),
            new Language("sid","","Sidamo","0","0"),
            new Language("sin","si","Sinhalese","1","1"),
            new Language("sio","","Siouan languages","0","0"),
            new Language("sit","","Sino-Tibetan (Other)","0","0"),
            new Language("sla","","Slavic (Other)","0","0"),
            new Language("slo","sk","Slovak","1","1"),
            new Language("slv","sl","Slovenian","1","1"),
            new Language("sma","","Southern Sami","0","0"),
            new Language("sme","se","Northern Sami","1","0"),
            new Language("smi","","Sami languages (Other)","0","0"),
            new Language("smj","","Lule Sami","0","0"),
            new Language("smn","","Inari Sami","0","0"),
            new Language("smo","sm","Samoan","0","0"),
            new Language("sms","","Skolt Sami","0","0"),
            new Language("sna","sn","Shona","0","0"),
            new Language("snd","sd","Sindhi","1","0"),
            new Language("snk","","Soninke","0","0"),
            new Language("sog","","Sogdian","0","0"),
            new Language("som","so","Somali","1","0"),
            new Language("son","","Songhai","0","0"),
            new Language("sot","st","Sotho, Southern","0","0"),
            new Language("spa","es","Spanish","1","1"),
            new Language("srd","sc","Sardinian","0","0"),
            new Language("srr","","Serer","0","0"),
            new Language("ssa","","Nilo-Saharan (Other)","0","0"),
            new Language("ssw","ss","Swati","0","0"),
            new Language("suk","","Sukuma","0","0"),
            new Language("sun","su","Sundanese","0","0"),
            new Language("sus","","Susu","0","0"),
            new Language("sux","","Sumerian","0","0"),
            new Language("swa","sw","Swahili","1","0"),
            new Language("swe","sv","Swedish","1","1"),
            new Language("syr","sy","Syriac","1","0"),
            new Language("tah","ty","Tahitian","0","0"),
            new Language("tai","","Tai (Other)","0","0"),
            new Language("tam","ta","Tamil","1","0"),
            new Language("tat","tt","Tatar","1","1"),
            new Language("tel","te","Telugu","1","0"),
            new Language("tem","","Timne","0","0"),
            new Language("ter","","Tereno","0","0"),
            new Language("tet","","Tetum","0","0"),
            new Language("tgk","tg","Tajik","0","0"),
            new Language("tgl","tl","Tagalog","1","1"),
            new Language("tha","th","Thai","1","1"),
            new Language("tib","bo","Tibetan","0","0"),
            new Language("tig","","Tigre","0","0"),
            new Language("tir","ti","Tigrinya","0","0"),
            new Language("tiv","","Tiv","0","0"),
            new Language("tkl","","Tokelau","0","0"),
            new Language("tlh","","Klingon","0","0"),
            new Language("tli","","Tlingit","0","0"),
            new Language("tmh","","Tamashek","0","0"),
            new Language("tog","","Tonga (Nyasa)","0","0"),
            new Language("ton","to","Tonga (Tonga Islands)","0","0"),
            new Language("tpi","","Tok Pisin","0","0"),
            new Language("tsi","","Tsimshian","0","0"),
            new Language("tsn","tn","Tswana","0","0"),
            new Language("tso","ts","Tsonga","0","0"),
            new Language("tuk","tk","Turkmen","1","0"),
            new Language("tum","","Tumbuka","0","0"),
            new Language("tup","","Tupi languages","0","0"),
            new Language("tur","tr","Turkish","1","1"),
            new Language("tut","","Altaic (Other)","0","0"),
            new Language("tvl","","Tuvalu","0","0"),
            new Language("twi","tw","Twi","0","0"),
            new Language("tyv","","Tuvinian","0","0"),
            new Language("udm","","Udmurt","0","0"),
            new Language("uga","","Ugaritic","0","0"),
            new Language("uig","ug","Uighur","0","0"),
            new Language("ukr","uk","Ukrainian","1","1"),
            new Language("umb","","Umbundu","0","0"),
            new Language("und","","Undetermined","0","0"),
            new Language("urd","ur","Urdu","1","0"),
            new Language("uzb","uz","Uzbek","0","1"),
            new Language("vai","","Vai","0","0"),
            new Language("ven","ve","Venda","0","0"),
            new Language("vie","vi","Vietnamese","1","1"),
            new Language("vol","vo","Volapük","0","0"),
            new Language("vot","","Votic","0","0"),
            new Language("wak","","Wakashan languages","0","0"),
            new Language("wal","","Walamo","0","0"),
            new Language("war","","Waray","0","0"),
            new Language("was","","Washo","0","0"),
            new Language("wel","cy","Welsh","0","0"),
            new Language("wen","","Sorbian languages","0","0"),
            new Language("wln","wa","Walloon","0","0"),
            new Language("wol","wo","Wolof","0","0"),
            new Language("xal","","Kalmyk","0","0"),
            new Language("xho","xh","Xhosa","0","0"),
            new Language("yao","","Yao","0","0"),
            new Language("yap","","Yapese","0","0"),
            new Language("yid","yi","Yiddish","0","0"),
            new Language("yor","yo","Yoruba","0","0"),
            new Language("ypk","","Yupik languages","0","0"),
            new Language("zap","","Zapotec","0","0"),
            new Language("zen","","Zenaga","0","0"),
            new Language("zha","za","Zhuang","0","0"),
            new Language("znd","","Zande","0","0"),
            new Language("zul","zu","Zulu","0","0"),
            new Language("zun","","Zuni","0","0"),
            new Language("rum","ro","Romanian","1","1"),
            new Language("pob","pb","Portuguese (BR)","1","1"),
            new Language("mne","me","Montenegrin","1","0"),
            new Language("zht","zt","Chinese (traditional)","1","1"),
            new Language("zhe","ze","Chinese bilingual","1","0"),
            new Language("pom","pm","Portuguese (MZ)","1","0"),
            new Language("ext","ex","Extremaduran","1","0"),
            new Language("spl","ea","Spanish (LA)","1","0"),
            new Language("spn","sp","Spanish (EU)","1","0")
        };

        private static void Log(string msg) { Console.WriteLine($"[{DateTime.Now.ToString("hh.mm.ss.fff")}] [Master] {msg}"); }
    }
}
