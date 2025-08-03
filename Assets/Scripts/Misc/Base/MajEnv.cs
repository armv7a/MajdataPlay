using HidSharp.Platform.Windows;
using LibVLCSharp;
using MajdataPlay.Extensions;
using MajdataPlay.Numerics;
using MychIO;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using MajdataPlay.Settings;
using MajdataPlay.Utils;
using UnityEngine;
using Cysharp.Threading.Tasks;
#nullable enable
namespace MajdataPlay
{
    public static class MajEnv
    {
        public const int DEFAULT_LAYER = 0;
        public const int HIDDEN_LAYER = 3;
        public const int HTTP_BUFFER_SIZE = 8192;
        public const int HTTP_REQUEST_MAX_RETRY = 4;
        public const int HTTP_TIMEOUT_MS = 8000;
        public const float FRAME_LENGTH_SEC = 1f / 60;
        public const float FRAME_LENGTH_MSEC = FRAME_LENGTH_SEC * 1000;

        public static readonly System.Threading.ThreadPriority THREAD_PRIORITY_IO = System.Threading.ThreadPriority.AboveNormal;
        public static readonly System.Threading.ThreadPriority THREAD_PRIORITY_MAIN = System.Threading.ThreadPriority.Normal;

        public static event Action? OnApplicationQuit;
        public static LibVLC? VLCLibrary { get; private set; }
        public static ConcurrentQueue<Action> ExecutionQueue { get; } = IOManager.ExecutionQueue;
        internal static HardwareEncoder HWEncoder { get; private set; } = HardwareEncoder.None;
        internal static RunningMode Mode { get; set; } = RunningMode.Play;
#if UNITY_EDITOR
        public static bool IsEditor { get; } = true;
#else
        public static bool IsEditor { get; } = false;
#endif
#if UNITY_STANDALONE_WIN
        public static string RootPath { get; } = Path.Combine(Application.dataPath, "../");
        public static string AssetsPath { get; } = Application.streamingAssetsPath;
        public static string CachePath { get; } = Path.Combine(RootPath, "Cache");
#else
        private static string? _rootPath;
        private static string? _assetsPath;
        private static string? _cachePath;
        private static string? _chartPath;
        private static string? _settingPath;
        private static string? _skinPath;
        private static string? _logsPath;
        private static string? _langPath;
        private static string? _scoreDBPath;
        private static string? _logPath;
        private static string? _recordOutputsPath;

        public static string RootPath
        {
            get
            {
                if (_rootPath == null)
                {
                    _rootPath = Application.persistentDataPath;
                }
                return _rootPath;
            }
        }

        public static string AssetsPath
        {
            get
            {
                if (_assetsPath == null)
                {
                    _assetsPath = Path.Combine(Application.persistentDataPath, "ExtStreamingAssets/");
                }
                return _assetsPath;
            }
        }

        public static string CachePath
        {
            get
            {
                if (_cachePath == null)
                {
                    _cachePath = Application.temporaryCachePath;
                }
                return _cachePath;
            }
        }

        public static string ChartPath
        {
            get
            {
                if (_chartPath == null)
                {
                    _chartPath = Path.Combine(RootPath, "MaiCharts");
                }
                return _chartPath;
            }
        }

        public static string SettingPath
        {
            get
            {
                if (_settingPath == null)
                {
                    _settingPath = Path.Combine(RootPath, "settings.json");
                }
                return _settingPath;
            }
        }

        public static string SkinPath
        {
            get
            {
                if (_skinPath == null)
                {
                    _skinPath = Path.Combine(RootPath, "Skins");
                }
                return _skinPath;
            }
        }

        public static string LogsPath
        {
            get
            {
                if (_logsPath == null)
                {
                    _logsPath = Path.Combine(RootPath, "Logs");
                }
                return _logsPath;
            }
        }

        public static string LangPath
        {
            get
            {
                if (_langPath == null)
                {
                    _langPath = Path.Combine(AssetsPath, "Langs");
                }
                return _langPath;
            }
        }

        public static string ScoreDBPath
        {
            get
            {
                if (_scoreDBPath == null)
                {
                    _scoreDBPath = Path.Combine(RootPath, "MajDatabase.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db.db");
                }
                return _scoreDBPath;
            }
        }

        public static string LogPath
        {
            get
            {
                if (_logPath == null)
                {
                    _logPath = Path.Combine(LogsPath, "MajPlayRuntime.log");
                }
                return _logPath;
            }
        }

        public static string RecordOutputsPath
        {
            get
            {
                if (_recordOutputsPath == null)
                {
                    _recordOutputsPath = Path.Combine(RootPath, "RecordOutputs");
                }
                return _recordOutputsPath;
            }
        }
#endif

        public static Sprite EmptySongCover { get; private set; }
        public static Material BreakMaterial { get; private set; }
        public static Material DefaultMaterial { get; private set; }
        public static Material HoldShineMaterial { get; private set; }
        public static Thread MainThread { get; } = Thread.CurrentThread;
        public static Process GameProcess { get; } = Process.GetCurrentProcess();
        public static HttpClient SharedHttpClient { get; } = new HttpClient(new HttpClientHandler()
        {
            Proxy = WebRequest.GetSystemWebProxy(),
            UseProxy = true,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        })
        {
            Timeout = TimeSpan.FromMilliseconds(HTTP_TIMEOUT_MS),
            DefaultRequestHeaders = 
            {
                UserAgent = { new ProductInfoHeaderValue("MajPlay", MajInstances.GameVersion.ToString()) },
            }
        };
        public static GameSetting UserSettings { get; private set; }
        public static CancellationToken GlobalCT
        {
            get
            {
                return _globalCTS.Token;
            }
        }
        public static JsonSerializerOptions UserJsonReaderOption { get; } = new()
        {
            Converters =
            {
                new JsonStringEnumConverter()
            },
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };

        readonly static CancellationTokenSource _globalCTS = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ChangedSynchronizationContext()
        {
#if !UNITY_EDITOR
            SynchronizationContext.SetSynchronizationContext(new UniTaskSynchronizationContext());
#endif
        }
        static MajEnv()
        {
            ChangedSynchronizationContext();
            UserSettings = new GameSetting();
        }
        internal static void Init()
        {
#if UNITY_STANDALONE_WIN
            MajDebug.Log("[VLC] init");
            if (VLCLibrary != null)
            {
                VLCLibrary.Dispose();
            }
            Core.Initialize(Path.Combine(Application.dataPath, "Plugins")); // Load VLC dlls
            VLCLibrary = new LibVLC(enableDebugLogs: true, "--no-audio");
#else
            VLCLibrary = null;
#endif
            CheckNoteSkinFolder();
            var netCachePath = Path.Combine(CachePath, "Net");
            var runtimeCachePath = Path.Combine(CachePath, "Runtime");
            CreateDirectoryIfNotExists(CachePath);
            CreateDirectoryIfNotExists(runtimeCachePath);
            CreateDirectoryIfNotExists(netCachePath);
            CreateDirectoryIfNotExists(ChartPath);
            CreateDirectoryIfNotExists(RecordOutputsPath);

            if (File.Exists(SettingPath))
            {
                var js = File.ReadAllText(SettingPath);
                GameSetting? setting;

                if (!Serializer.Json.TryDeserialize(js, out setting, UserJsonReaderOption) || setting is null)
                {
                    UserSettings = new();
                    MajDebug.LogError("Failed to read setting from file");
                    var bakFileName = $"{SettingPath}.bak";
                    while (File.Exists(bakFileName))
                    {
                        bakFileName = $"{bakFileName}.bak";
                    }
                    try
                    {
                        File.Copy(SettingPath, bakFileName, true);
                    }
                    catch { }
                }
                else
                {
                    UserSettings = setting;
                    UserSettings.Mod = new ModOptions(); // Reset Mod option after reboot
                }
            }
            else
            {
                var json = Serializer.Json.Serialize(UserSettings, UserJsonReaderOption);
                File.WriteAllText(SettingPath, json);
            }

            UserSettings.IO.InputDevice.ButtonRing.PollingRateMs = Math.Max(0, UserSettings.IO.InputDevice.ButtonRing.PollingRateMs);
            UserSettings.IO.InputDevice.TouchPanel.PollingRateMs = Math.Max(0, UserSettings.IO.InputDevice.TouchPanel.PollingRateMs);
            UserSettings.IO.InputDevice.ButtonRing.DebounceThresholdMs = Math.Max(0, UserSettings.IO.InputDevice.ButtonRing.DebounceThresholdMs);
            UserSettings.IO.InputDevice.TouchPanel.DebounceThresholdMs = Math.Max(0, UserSettings.IO.InputDevice.TouchPanel.DebounceThresholdMs);
            UserSettings.Display.InnerJudgeDistance = UserSettings.Display.InnerJudgeDistance.Clamp(0, 1);
            UserSettings.Display.OuterJudgeDistance = UserSettings.Display.OuterJudgeDistance.Clamp(0, 1);

            SharedHttpClient.Timeout = TimeSpan.FromMilliseconds(HTTP_TIMEOUT_MS);
            MainThread.Priority = THREAD_PRIORITY_MAIN;
#if !UNITY_EDITOR
            if (MainThread.Name is not null)
            {
                MainThread.Name = "MajdataPlay MainThread";
            }
#endif
        }
        internal static void OnApplicationQuitRequested()
        {
            SharedHttpClient.CancelPendingRequests();
            SharedHttpClient.Dispose();
            if (VLCLibrary != null)
            {
                VLCLibrary.Dispose();
            }
            _globalCTS.Cancel();
            if (OnApplicationQuit is not null)
            {
                OnApplicationQuit();
            }
            WinHidManager.QuitThisBs();
        }
        static void CheckNoteSkinFolder()
        {
            if (!Directory.Exists(SkinPath))
            {
                Directory.CreateDirectory(SkinPath);
            }
        }
        static void CreateDirectoryIfNotExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
    }
}