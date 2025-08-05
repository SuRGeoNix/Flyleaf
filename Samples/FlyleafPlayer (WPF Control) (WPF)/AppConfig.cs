using System;
using System.IO;
using System.Text.Json;

using FlyleafLib;

namespace FlyleafPlayer;
public class AppConfig
{
    static readonly string PATH = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Flyleaf.AppConfig.json");

    public GeneralConfig    General     { get; set; } = new();
    public SlideShowConfig  SlideShow   { get; set; } = new();

    public class GeneralConfig : NotifyPropertyChanged
    {
        public bool     SingleInstance          { get => _SingleInstance;       set => Set(ref _SingleInstance, value); }
        bool _SingleInstance = true;
    }
    public class SlideShowConfig : NotifyPropertyChanged
    {
        public int      MaxFiles                { get; set; } = 5000;

        public bool     DeleteConfirmation      { get => _DeleteConfirmation;   set => Set(ref _DeleteConfirmation, value); }
        bool _DeleteConfirmation = true;

        public int      PageStep                { get => _PageStep;             set => Set(ref _PageStep, value); }
        int _PageStep = 10;

        public int      SlideShowTimer          { get => _SlideShowTimer;       set => Set(ref _SlideShowTimer, value); }
        int _SlideShowTimer = 3000;
    }

    public static AppConfig Load()
    {
        #if DEBUG
            return new();
        #endif

        AppConfig config;

        if (!File.Exists(PATH))
        {
            config = new();
            config.Save();
        }
        else
            config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(PATH));
        
        return config;
    }

    public void Save()
        => File.WriteAllText(PATH, JsonSerializer.Serialize(this, Config.jsonOpts));

}
