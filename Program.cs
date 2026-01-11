using System.Runtime.InteropServices;
using System.Text.Json;

enum Keys
{
    None = 0,
    LButton = 1,
    RButton = 2,
    Cancel = 3,
    MButton = 4,
    XButton1 = 5,
    XButton2 = 6,
    Back = 8,
    Tab = 9,
    Clear = 12,
    Return = 13,
    ShiftKey = 16,
    ControlKey = 17,
    Menu = 18,
    Pause = 19,
    Capital = 20,
    Escape = 27,
    Space = 32,
    PageUp = 33,
    PageDown = 34,
    End = 35,
    Home = 36,
    Left = 37,
    Up = 38,
    Right = 39,
    Down = 40,
    Select = 41,
    Print = 42,
    Execute = 43,
    Snapshot = 44,
    Insert = 45,
    Delete = 46,
    Help = 47,
    D0 = 48,
    D1 = 49,
    D2 = 50,
    D3 = 51,
    D4 = 52,
    D5 = 53,
    D6 = 54,
    D7 = 55,
    D8 = 56,
    D9 = 57,
    A = 65,
    B = 66,
    C = 67,
    D = 68,
    E = 69,
    F = 70,
    G = 71,
    H = 72,
    I = 73,
    J = 74,
    K = 75,
    L = 76,
    M = 77,
    N = 78,
    O = 79,
    P = 80,
    Q = 81,
    R = 82,
    S = 83,
    T = 84,
    U = 85,
    V = 86,
    W = 87,
    X = 88,
    Y = 89,
    Z = 90,
    NumPad0 = 96,
    NumPad1 = 97,
    NumPad2 = 98,
    NumPad3 = 99,
    NumPad4 = 100,
    NumPad5 = 101,
    NumPad6 = 102,
    NumPad7 = 103,
    NumPad8 = 104,
    NumPad9 = 105,
    F1 = 112,
    F2 = 113,
    F3 = 114,
    F4 = 115,
    F5 = 116,
    F6 = 117,
    F7 = 118,
    F8 = 119,
    F9 = 120,
    F10 = 121,
    F11 = 122,
    F12 = 123
}

class Program
{
    static GammaConfig config;
    static bool boosted = false;
    static double currentGamma;
    static IntPtr primaryDC;

    static void Main()
    {
        Console.Title = "Gamma Manager";

        LoadConfig();
        primaryDC = GetPrimaryMonitorDC();

        // If DefaultGamma is not set, read the current system gamma
        if (config.DefaultGamma <= 0)
        {
            double sys = ReadGamma(primaryDC);
            config.DefaultGamma = sys;
            config.PreviousGamma = sys;
            SaveConfig();
        }

        ApplyGamma(primaryDC, config.DefaultGamma);
        currentGamma = config.DefaultGamma;

        // Resolve configured keys to virtual-key codes
        int vkToggle = KeyToVK(config.ToggleKey ?? string.Empty);
        int vkReset = KeyToVK(config.ResetKey ?? string.Empty);
        int vkIncrease = KeyToVK(config.IncreaseKey ?? string.Empty);
        int vkDecrease = KeyToVK(config.DecreaseKey ?? string.Empty);

        // Start poller to detect mouse buttons and any other keys that couldn't be registered
        StartKeyPoller(vkToggle, vkReset, vkIncrease, vkDecrease);

        InitTray();

        // Use the improved console UI to render a friendly production-ready summary
        string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gamma_config.json");
        ConsoleUI.RenderStartupSummary(
            title: "Gamma Manager",
            toggleKey: config.ToggleKey ?? "Unset",
            resetKey: config.ResetKey ?? "Unset",
            increaseKey: config.IncreaseKey ?? "Unset",
            decreaseKey: config.DecreaseKey ?? "Unset",
            boostedGamma: config.BoostedGamma,
            defaultGamma: config.DefaultGamma,
            configPath: jsonPath
        );

        // Start a background periodic status logger
        new Thread(() =>
        {
            try
            {
                ConsoleUI.PeriodicStatus(() => $"Current Gamma={currentGamma:0.00}", TimeSpan.FromMinutes(1));
            }
            catch { /* keep alive */ }
        })
        { IsBackground = true }.Start();

        // Block main thread indefinitely - poller and tray remain active
        Thread.Sleep(Timeout.Infinite);
    }

    // Background poller for keys that aren't registered as WM_HOTKEY (including mouse buttons).
    static void StartKeyPoller(int vkToggle, int vkReset, int vkIncrease, int vkDecrease)
    {
        var keys = new Dictionary<int, Action>
        {
            [vkToggle] = () => { try { Toggle(); } catch { } },
            [vkReset] = () => { try { Reset(); } catch { } },
            [vkIncrease] = () => { try { IncreaseGamma(); } catch { } },
            [vkDecrease] = () => { try { DecreaseGamma(); } catch { } }
        };

        // Remove invalid (0) entries to reduce polling
        var monitored = new Dictionary<int, Action>();
        foreach (var kv in keys)
            if (kv.Key != 0)
                monitored[kv.Key] = kv.Value;

        new Thread(() =>
        {
            var prevState = new Dictionary<int, bool>();
            foreach (var kv in monitored)
                prevState[kv.Key] = false;

            while (true)
            {
                foreach (var kv in monitored)
                {
                    int vk = kv.Key;
                    bool pressed = (GetAsyncKeyState(vk) & 0x8000) != 0;

                    // detect rising edge
                    if (pressed && !prevState[vk])
                    {
                        try { kv.Value(); }
                        catch { /* swallow exceptions to keep poller alive */ }
                    }

                    prevState[vk] = pressed;
                }

                Thread.Sleep(30); // ~33Hz poll, light-weight
            }
        })
        { IsBackground = true }.Start();
    }

    // ======= TOGGLE GAMMA =======
    static void Toggle()
    {
        if (!boosted)
        {
            currentGamma = ReadGamma(primaryDC);
            config.PreviousGamma = currentGamma;
            SaveConfig();

            ApplyGamma(primaryDC, config.BoostedGamma);
            currentGamma = config.BoostedGamma;

            ConsoleUI.LogSuccess($"Toggle: BOOST ON. PreviousGamma saved={config.PreviousGamma:0.00}, applied BoostedGamma={config.BoostedGamma:0.00}");
        }
        else
        {
            ApplyGamma(primaryDC, config.PreviousGamma);
            currentGamma = config.PreviousGamma;

            ConsoleUI.LogSuccess($"Toggle: BOOST OFF. Restored PreviousGamma={config.PreviousGamma:0.00}");
        }
        boosted = !boosted;
    }

    // ======= GAMMA FUNCTIONS =======
    static double ReadGamma(IntPtr dc)
    {
        RAMP ramp = new RAMP
        {
            Red = new ushort[256],
            Green = new ushort[256],
            Blue = new ushort[256]
        };

        if (!GetDeviceGammaRamp(dc, ref ramp))
            return 1.0; // fallback if reading fails

        double sum = 0;
        for (int i = 1; i < 256; i++)
        {
            double actual = ramp.Red[i] / 65535.0;
            double expected = i / 255.0;

            if (actual <= 0 || expected <= 0) continue;

            double val = Math.Log(actual) / Math.Log(expected);
            if (double.IsInfinity(val) || double.IsNaN(val)) continue;

            sum += val;
        }

        double gamma = sum / 255.0;
        if (double.IsInfinity(gamma) || double.IsNaN(gamma) || gamma <= 0)
            gamma = 1.0;

        return gamma;
    }

    static void ApplyGamma(IntPtr dc, double gamma)
    {
        RAMP ramp = new RAMP
        {
            Red = new ushort[256],
            Green = new ushort[256],
            Blue = new ushort[256]
        };

        for (int i = 1; i < 256; i++)
        {
            int v = (int)(Math.Pow(i / 255.0, 1.0 / gamma) * 65535);
            v = Math.Clamp(v, 0, 65535);
            ramp.Red[i] = ramp.Green[i] = ramp.Blue[i] = (ushort)v;
        }

        SetDeviceGammaRamp(dc, ref ramp);
    }

    static void IncreaseGamma()
    {
        const double maxGamma = 5.0; // maximum allowed gamma
        bool isBoostedActive = currentGamma == config.BoostedGamma;

        if (config.BoostedGamma < maxGamma)
        {
            config.BoostedGamma += 0.1;
            if (config.BoostedGamma > maxGamma) config.BoostedGamma = maxGamma;
            config.BoostedGamma = Math.Round(config.BoostedGamma, 2);
            SaveConfig();

            if (isBoostedActive)
            {
                ApplyGamma(primaryDC, config.BoostedGamma);
                currentGamma = config.BoostedGamma;
            }

            ConsoleUI.LogSuccess($"BoostedGamma increased: {config.BoostedGamma:0.00}");
        }
        else
        {
            ConsoleUI.LogWarning($"BoostedGamma is already at maximum: {maxGamma:0.00}");
        }
    }

    static void DecreaseGamma()
    {
        bool isBoostedActive = currentGamma == config.BoostedGamma;

        if (config.BoostedGamma > config.DefaultGamma)
        {
            config.BoostedGamma -= 0.1;
            if (config.BoostedGamma < config.DefaultGamma) config.BoostedGamma = config.DefaultGamma;
            config.BoostedGamma = Math.Round(config.BoostedGamma, 2);
            SaveConfig();

            if (isBoostedActive)
            {
                ApplyGamma(primaryDC, config.BoostedGamma);
                currentGamma = config.BoostedGamma;
            }

            ConsoleUI.LogSuccess($"BoostedGamma decreased: {config.BoostedGamma:0.00}");
        }
        else
        {
            ConsoleUI.LogWarning($"BoostedGamma is already at minimum: {config.DefaultGamma:0.00}");
        }
    }


    // ======= MONITOR =======
    static IntPtr GetPrimaryMonitorDC()
    {
        return GetDC(IntPtr.Zero); // primary monitor DC
    }

    // ======= CONFIG =======
    // In LoadConfig() DefaultGamma is set to a hardcoded value which will be overwritten on first run
    static void LoadConfig()
    {
        if (!File.Exists("gamma_config.json"))
            File.WriteAllText("gamma_config.json", "{}");

        config = JsonSerializer.Deserialize<GammaConfig>(
            File.ReadAllText("gamma_config.json")) ?? new GammaConfig();

        // ToggleKey
        if (string.IsNullOrWhiteSpace(config.ToggleKey))
        {
            ConsoleUI.LogInfo("ToggleKey not set. Press a key (F1–F12), numpad or mouse button for Toggle...");
            config.ToggleKey = WaitForKeyPress().ToString();
        }

        // ResetKey
        if (string.IsNullOrWhiteSpace(config.ResetKey))
        {
            // Hardcoded F7
            config.ResetKey = "F7";
        }

        // IncreaseKey
        if (string.IsNullOrWhiteSpace(config.IncreaseKey))
        {
            // Hardcoded F8
            config.IncreaseKey = "F8";
        }

        // DecreaseKey
        if (string.IsNullOrWhiteSpace(config.DecreaseKey))
        {
            // Hardcoded F9
            config.DecreaseKey = "F9";
        }

        // BoostedGamma
        if (config.BoostedGamma <= 0)
        {
            ConsoleUI.LogInfo("Boost gamma (2.3): ");
            string? input = Console.ReadLine();
            config.BoostedGamma = !string.IsNullOrWhiteSpace(input) ? double.Parse(input) : 2.3;
        }

        // DefaultGamma hardcoded to 1.0
        config.DefaultGamma = 1.0;

        // On first run set PreviousGamma = DefaultGamma
        if (config.PreviousGamma <= 0)
            config.PreviousGamma = config.DefaultGamma;

        SaveConfig();
    }

    // Reset now always restores DefaultGamma
    static void Reset()
    {
        ApplyGamma(primaryDC, config.DefaultGamma);
        currentGamma = config.DefaultGamma;
        boosted = false;

        // Save PreviousGamma
        config.PreviousGamma = currentGamma;
        SaveConfig();

        ConsoleUI.LogSuccess($"Reset: applied DefaultGamma={config.DefaultGamma:0.00} and cleared boost state.");
    }


    static Keys WaitForKeyPress()
    {
        Keys[] possibleKeys = (Keys[])Enum.GetValues(typeof(Keys));

        while (true)
        {
            foreach (Keys key in possibleKeys)
            {
                if ((GetAsyncKeyState((int)key) & 0x8000) != 0)
                {
                    ConsoleUI.LogSuccess($"Wybrano: {key}");
                    Thread.Sleep(200); // debounce
                    return key;
                }
            }
            Thread.Sleep(10);
        }
    }



    static void SaveConfig()
    {
        // Sanitize values against NaN/Infinity
        if (double.IsNaN(config.DefaultGamma) || double.IsInfinity(config.DefaultGamma))
            config.DefaultGamma = 1.0;

        if (double.IsNaN(config.PreviousGamma) || double.IsInfinity(config.PreviousGamma))
            config.PreviousGamma = 1.0;

        if (double.IsNaN(config.BoostedGamma) || double.IsInfinity(config.BoostedGamma))
            config.BoostedGamma = 2.3;

        File.WriteAllText("gamma_config.json",
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }


    // ======= TRAY ICON =======
    static void InitTray()
    {
        NOTIFYICONDATA data = new NOTIFYICONDATA();
        data.cbSize = Marshal.SizeOf(data);
        data.uFlags = 2 | 4;
        data.szTip = "Gamma Manager";
        Shell_NotifyIcon(0, ref data);
    }

    // ======= HOTKEYS =======
    static int KeyToVK(string k)
    {
        if (string.IsNullOrWhiteSpace(k))
            return 0;

        // Try parse our Keys enum (covers mouse buttons and function keys defined in enum)
        if (Enum.TryParse<Keys>(k, true, out Keys key))
            return (int)key;

        // Try ConsoleKey (covers many standard keyboard keys)
        if (Enum.TryParse<ConsoleKey>(k, true, out ConsoleKey ckey))
            return (int)ckey;

        // Single-character mappings (A-Z, 0-9)
        k = k.Trim();
        if (k.Length == 1)
        {
            char ch = char.ToUpperInvariant(k[0]);
            if (ch >= 'A' && ch <= 'Z')
                return (int)ch; // VK_A..VK_Z map to ASCII
            if (ch >= '0' && ch <= '9')
                return (int)ch;
        }

        // Unknown mapping
        return 0;
    }

    // ======= WINAPI =======
    const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fs, uint vk);
    [DllImport("user32.dll")] static extern bool GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);

    [DllImport("gdi32.dll")] static extern bool SetDeviceGammaRamp(IntPtr hDC, ref RAMP ramp);
    [DllImport("gdi32.dll")] static extern bool GetDeviceGammaRamp(IntPtr hDC, ref RAMP ramp);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("shell32.dll")] static extern bool Shell_NotifyIcon(int msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    struct RAMP
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Blue;
    }

    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; }

    struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }
}

class GammaConfig
{
    public string? ToggleKey { get; set; }
    public string? ResetKey { get; set; }
    public string? IncreaseKey { get; set; }
    public string? DecreaseKey { get; set; }
    public double DefaultGamma { get; set; }
    public double BoostedGamma { get; set; }
    public double PreviousGamma { get; set; }
}

static class ConsoleUI
{
    private static readonly object _lock = new();

    // Render a boxed header + key bindings + config path
    public static void RenderStartupSummary(string title, string toggleKey, string resetKey, string increaseKey, string decreaseKey, double boostedGamma, double defaultGamma, string configPath)
    {
        lock (_lock)
        {
            Console.Clear();
            PrintHeader(title);
            Console.WriteLine();

            PrintKeyBindings(toggleKey, resetKey, increaseKey, decreaseKey);
            Console.WriteLine();

            PrintConfigInfo(configPath);
            Console.WriteLine();

            PrintGammaInfo(defaultGamma, boostedGamma);

            PrintSeparator();
            WriteColored("ℹ️  Running with poller-only input handling. Press ", ConsoleColor.DarkGray);
            WriteColored("Ctrl+C", ConsoleColor.Yellow);
            WriteColored(" to exit.", ConsoleColor.DarkGray);
            Console.WriteLine();
            PrintSeparator();
        }
    }

    // PeriodicStatus will repeatedly write a single-line status with timestamp at the bottom.
    // It avoids flooding by using the interval provided.
    public static void PeriodicStatus(Func<string> statusProvider, TimeSpan interval)
    {
        while (true)
        {
            string status = statusProvider();
            LogInfo(status);
            Thread.Sleep(interval);
        }
    }

    private static void PrintHeader(string title)
    {
        var c = ConsoleColor.Cyan;
        string top = "┌" + new string('─', 52) + "┐";
        string bottom = "└" + new string('─', 52) + "┘";
        WriteLineColored(top, c);
        WriteLineColored($"│{CenterText(title + "  v1.0", 52)}│", c);
        WriteLineColored(bottom, c);
    }

    private static void PrintKeyBindings(string toggleKey, string resetKey, string increaseKey, string decreaseKey)
    {
        WriteColored("Keys: ", ConsoleColor.Green);
        Console.Write(" ");
        WriteKey("Toggle", toggleKey);
        Console.Write("   ");
        WriteKey("Reset", resetKey);
        Console.Write("   ");
        WriteKey("Increase", increaseKey);
        Console.Write("   ");
        WriteKey("Decrease", decreaseKey);
        Console.WriteLine();
    }

    private static void PrintConfigInfo(string path)
    {
        WriteColored("Config: ", ConsoleColor.Green);
        WriteColored(path, ConsoleColor.Yellow);
        Console.WriteLine();
    }

    private static void PrintGammaInfo(double defaultGamma, double boostedGamma)
    {
        WriteColored("Default Gamma: ", ConsoleColor.Green);
        WriteColored($"{defaultGamma:0.00}", ConsoleColor.Magenta);
        Console.Write("   ");
        WriteColored("Boosted Gamma: ", ConsoleColor.Green);
        WriteColored($"{boostedGamma:0.00}", ConsoleColor.Magenta);
        Console.WriteLine();
    }

    private static void PrintSeparator()
    {
        WriteLineColored(new string('─', 72), ConsoleColor.DarkGray);
    }

    // Simple key formatting helper
    private static void WriteKey(string label, string keyName)
    {
        WriteColored($"{label}=", ConsoleColor.DarkGray);
        WriteColored($"[{keyName}]", ConsoleColor.Magenta);
    }

    // Generic log helpers
    public static void LogInfo(string message) => Log(message, ConsoleColor.White);
    public static void LogSuccess(string message) => Log(message, ConsoleColor.Green);
    public static void LogWarning(string message) => Log(message, ConsoleColor.Yellow);
    public static void LogError(string message) => Log(message, ConsoleColor.Red);

    private static void Log(string message, ConsoleColor color)
    {
        lock (_lock)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            WriteColored($"[{ts}] ", ConsoleColor.DarkGray);
            WriteColored(message, color);
            Console.WriteLine();
        }
    }

    // Low-level helpers
    private static void WriteColored(string text, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = prev;
    }

    private static void WriteLineColored(string text, ConsoleColor color)
    {
        WriteColored(text + Environment.NewLine, color);
    }

    private static string CenterText(string text, int width)
    {
        if (text.Length >= width) return text.Substring(0, width);
        int pad = (width - text.Length) / 2;
        return new string(' ', pad) + text + new string(' ', width - text.Length - pad);
    }
}
