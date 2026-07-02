using System;
using System.Configuration;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using OpenCvSharp;

// 解决Window类命名冲突
using WpfWindow = System.Windows.Window;

// 解决命名冲突
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;

namespace ThreeKingdomsHeroes;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : WpfWindow
{
    private List<Hero> heroes;
    private bool isF12Active = false;
    private bool isAutoHammerEnabled = false;
    private bool isAutoUltEnabled = false;
    private bool isAutoBaodaboEnabled = false;
    private System.Threading.Timer hammerTimer;
    private System.Threading.Timer ultTimer;
    private System.Threading.Timer processCheckTimer;
    private System.Threading.Timer baodaboTimer;
    private System.Threading.Timer debugTimer;
    private Process gameProcess = null;
    private int hammerIntervalMs = 2000;
    private int ultIntervalMs = 500;
    private bool isDebugModeEnabled = false;
    private int skillBrightnessThreshold = 100;
    private int hammerBrightnessThreshold = 80;
    private int baodaboWBrightnessThreshold = 60;
    private int baodaboEBrightnessThreshold = 60;

    private const int DebugUpdateIntervalMs = 300;
    private const int HOTKEY_ID = 9000;
    private const uint MOD_NONE = 0x0000;
    private const uint VK_F12 = 0x7B;
    private const uint VK_F1 = 0x70;
    private const uint VK_W = 0x57;
    private const uint VK_E = 0x45;

    // 全局键盘钩子相关
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private static LowLevelKeyboardProc _proc;
    private static IntPtr _hookID = IntPtr.Zero;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, uint dwExtraInfo);

    [DllImport("user32.dll", CharSet = CharSet.Auto, CallingConvention = CallingConvention.StdCall)]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, uint dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private const uint MOUSEEVENTF_RIGHTDOWN = 0x08;
    private const uint MOUSEEVENTF_RIGHTUP = 0x10;
    private const uint KEYEVENTF_KEYDOWN = 0x00;
    private const uint KEYEVENTF_KEYUP = 0x02;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    public MainWindow()
    {
        InitializeComponent();
        InitializeHeroes();
        LoadAutoReleaseSettings();
        LoadIntervalSettings();
        LoadThresholdSettings();
        LoadSkillArea();
        LoadHammerArea();
        LoadBaodaboArea();
        LoadDebugModeSettings();
        _proc = HookCallback;
        _hookID = SetHook(_proc);
    }

    private void LoadAutoReleaseSettings()
    {
        // 从app.config读取自动释放锤子的状态
        string autoHammerEnabled = ConfigurationManager.AppSettings["AutoHammerEnabled"];
        if (!string.IsNullOrEmpty(autoHammerEnabled))
        {
            bool enabled = bool.TryParse(autoHammerEnabled, out bool result) && result;
            cbAutoHammer.IsChecked = enabled;
            isAutoHammerEnabled = enabled;
        }

        // 从app.config读取自动释放大招的状态
        string autoUltEnabled = ConfigurationManager.AppSettings["AutoUltEnabled"];
        if (!string.IsNullOrEmpty(autoUltEnabled))
        {
            bool enabled = bool.TryParse(autoUltEnabled, out bool result) && result;
            cbAutoUlt.IsChecked = enabled;
            isAutoUltEnabled = enabled;
        }

        // 从app.config读取自动升级包大伯的状态，默认启用
        string autoBaodaboEnabled = ConfigurationManager.AppSettings["AutoBaodaboEnabled"];
        if (!string.IsNullOrEmpty(autoBaodaboEnabled))
        {
            bool enabled = bool.TryParse(autoBaodaboEnabled, out bool result) && result;
            cbAutoBaodabo.IsChecked = enabled;
            isAutoBaodaboEnabled = enabled;
        }
        else
        {
            // 默认启用
            cbAutoBaodabo.IsChecked = true;
            isAutoBaodaboEnabled = true;
        }
    }

    private void LoadIntervalSettings()
    {
        hammerIntervalMs = GetIntervalFromConfig("HammerInterval", 2000);
        ultIntervalMs = GetIntervalFromConfig("UltInterval", 500);
        txtHammerInterval.Text = hammerIntervalMs.ToString();
        txtUltInterval.Text = ultIntervalMs.ToString();
    }

    private void LoadThresholdSettings()
    {
        skillBrightnessThreshold = GetThresholdFromConfig("SkillBrightnessThreshold", 100);
        hammerBrightnessThreshold = GetThresholdFromConfig("HammerBrightnessThreshold", 80);

        int baodaboDefault = GetThresholdFromConfig("BaodaboBrightnessThreshold", 60);
        baodaboWBrightnessThreshold = GetThresholdFromConfig("BaodaboWBrightnessThreshold", baodaboDefault);
        baodaboEBrightnessThreshold = GetThresholdFromConfig("BaodaboEBrightnessThreshold", baodaboDefault);

        txtSkillThreshold.Text = skillBrightnessThreshold.ToString();
        txtHammerThreshold.Text = hammerBrightnessThreshold.ToString();
        txtBaodaboWThreshold.Text = baodaboWBrightnessThreshold.ToString();
        txtBaodaboEThreshold.Text = baodaboEBrightnessThreshold.ToString();
    }

    private int GetThresholdFromConfig(string key, int defaultValue)
    {
        string thresholdStr = ConfigurationManager.AppSettings[key];
        if (int.TryParse(thresholdStr, out int threshold) && threshold >= 0)
        {
            return threshold;
        }
        return defaultValue;
    }

    private void LoadDebugModeSettings()
    {
        string debugModeEnabled = ConfigurationManager.AppSettings["DebugModeEnabled"];
        if (!string.IsNullOrEmpty(debugModeEnabled) && bool.TryParse(debugModeEnabled, out bool enabled) && enabled)
        {
            cbDebugMode.IsChecked = true;
            isDebugModeEnabled = true;
            panelDebug.Visibility = Visibility.Visible;
            StartDebugTimer();
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
        if (source != null)
        {
            // 设置窗口样式，确保即使在后台也能接收消息
            source.AddHook(HwndHook);
            // 使用当前窗口句柄注册热键
            bool success = RegisterHotKey(source.Handle, HOTKEY_ID, MOD_NONE, VK_F12);
            // 调试信息
            System.Diagnostics.Debug.WriteLine($"热键注册成功: {success}");
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            // 在UI线程上执行操作
            this.Dispatcher.Invoke(() =>
            {
                ToggleF12Status();
            });
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        // 移除全局键盘钩子
        UnhookWindowsHookEx(_hookID);
        
        var source = PresentationSource.FromVisual(this) as System.Windows.Interop.HwndSource;
        if (source != null)
        {
            bool success = UnregisterHotKey(source.Handle, HOTKEY_ID);
            System.Diagnostics.Debug.WriteLine($"热键注销成功: {success}");
        }
        // 停止所有定时器
        StopAllTimers();
        StopDebugTimer();
        base.OnClosed(e);
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule)
        {
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == VK_F12)
            {
                // 在UI线程上执行操作
                this.Dispatcher.Invoke(() =>
                {
                    ToggleF12Status();
                });
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private void InitializeHeroes()
    {
        heroes = new List<Hero>
        {
            // 魏国
            new Hero { Name = "夏侯惇", Country = "魏" },
            new Hero { Name = "荀彧", Country = "魏" },
            new Hero { Name = "曹丕", Country = "魏" },
            new Hero { Name = "郭嘉", Country = "魏" },
            new Hero { Name = "于禁", Country = "魏" },
            new Hero { Name = "张辽", Country = "魏" },
            new Hero { Name = "徐晃", Country = "魏" },
            new Hero { Name = "典韦", Country = "魏" },
            new Hero { Name = "贾诩", Country = "魏" },
            new Hero { Name = "司马懿", Country = "魏" },
            new Hero { Name = "张郃", Country = "魏" },
            new Hero { Name = "曹仁", Country = "魏" },
            new Hero { Name = "甄宓", Country = "魏" },
            new Hero { Name = "蔡文姬", Country = "魏" },

            // 蜀国
            new Hero { Name = "诸葛亮", Country = "蜀" },
            new Hero { Name = "关羽 ", Country = "蜀" },
            new Hero { Name = "张飞 ", Country = "蜀" },
            new Hero { Name = "赵云 ", Country = "蜀" },
            new Hero { Name = "梦*赵云 ", Country = "蜀" },
            new Hero { Name = "马超 ", Country = "蜀" },
            new Hero { Name = "黄忠 ", Country = "蜀" },
            new Hero { Name = "法正 ", Country = "蜀" },
            new Hero { Name = "姜维 ", Country = "蜀" },
            new Hero { Name = "徐庶 ", Country = "蜀" },
            new Hero { Name = "庞统 ", Country = "蜀" },
            new Hero { Name = "黄月英", Country = "蜀" },
            new Hero { Name = "蒲元 ", Country = "蜀" },

            // 吴国
			new Hero { Name = "孙坚", Country = "吴" },
            new Hero { Name = "孙策", Country = "吴" },
            new Hero { Name = "甘宁", Country = "吴" },
            new Hero { Name = "周瑜", Country = "吴" },
            new Hero { Name = "鲁肃", Country = "吴" },
            new Hero { Name = "吕蒙", Country = "吴" },
            new Hero { Name = "陆逊", Country = "吴" },
            new Hero { Name = "黄盖", Country = "吴" },
            new Hero { Name = "凌统", Country = "吴" },
            new Hero { Name = "太史慈", Country = "吴" },
            new Hero { Name = "周泰", Country = "吴" },
            new Hero { Name = "孙尚香", Country = "吴" },
            new Hero { Name = "大乔", Country = "吴" },
            new Hero { Name = "小乔", Country = "吴" },
            new Hero { Name = "孙玲珑", Country = "吴" },

            // 群雄
            new Hero { Name = "董卓", Country = "群雄" },
            new Hero { Name = "袁绍", Country = "群雄" },
            new Hero { Name = "张角", Country = "群雄" },
            new Hero { Name = "公孙瓒", Country = "群雄" },
            new Hero { Name = "高顺", Country = "群雄" },
            new Hero { Name = "吕布", Country = "群雄" },
            new Hero { Name = "祝融", Country = "群雄" },
            new Hero { Name = "孟获", Country = "群雄" },
            new Hero { Name = "貂蝉", Country = "群雄" }
        };
    }

    private void btnWei_Click(object sender, RoutedEventArgs e)
    {
        var weiHeroes = heroes.Where(h => h.Country == "魏").ToList();
        DisplayHeroes(weiHeroes);
    }

    private void btnShu_Click(object sender, RoutedEventArgs e)
    {
        var shuHeroes = heroes.Where(h => h.Country == "蜀").ToList();
        DisplayHeroes(shuHeroes);
    }

    private void btnWu_Click(object sender, RoutedEventArgs e)
    {
        var wuHeroes = heroes.Where(h => h.Country == "吴").ToList();
        DisplayHeroes(wuHeroes);
    }

    private void btnNeutral_Click(object sender, RoutedEventArgs e)
    {
        var neutralHeroes = heroes.Where(h => h.Country == "群雄").ToList();
        DisplayHeroes(neutralHeroes);
    }

    private void DisplayHeroes(List<Hero> heroList)
    {
        wpHeroes.Children.Clear();
        
        if (heroList.Count > 0)
        {
            tbNoData.Visibility = Visibility.Collapsed;
            
            foreach (var hero in heroList)
            {
                Border heroBorder = new Border
                {
                    Background = WpfBrushes.White,
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#E0E0E0")),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 4, 6, 4),
                    Margin = new Thickness(5, 5, 5, 5),
                    Width = 82,
                    Height = 46,
                    Effect = new DropShadowEffect { BlurRadius = 2, ShadowDepth = 1, Opacity = 0.15 }
                };
                
                TextBlock heroText = new TextBlock
                {
                    Text = hero.Name,
                    FontSize = 12,
                    Foreground = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#333333")),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                
                heroBorder.Child = heroText;
                wpHeroes.Children.Add(heroBorder);
            }
        }
        else
        {
            tbNoData.Visibility = Visibility.Visible;
        }
    }

    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        this.DragMove();
    }

    private void ToggleF12Status()
    {
        isF12Active = !isF12Active;
        UpdateF12StatusText();
        
        if (isF12Active)
        {
            // 启动进程监控
            StartProcessMonitoring();
        }
        else
        {
            // 停止所有定时器
            StopAllTimers();
        }
    }

    private void UpdateF12StatusText()
    {
        if (isF12Active)
        {
            tbF12Status.Text = "F12状态: 已启动";
            tbF12Status.Foreground = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#4CAF50"));
        }
        else
        {
            tbF12Status.Text = "F12状态: 未启动";
            tbF12Status.Foreground = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#666666"));
        }
    }

    private void StartHammerTimer()
    {
        if (hammerTimer == null)
        {
            hammerTimer = new System.Threading.Timer(CheckProcessAndClick, null, 0, hammerIntervalMs);
        }
    }

    private void StopHammerTimer()
    {
        if (hammerTimer != null)
        {
            hammerTimer.Dispose();
            hammerTimer = null;
        }
    }

    private void StartUltTimer()
    {
        if (ultTimer == null)
        {
            ultTimer = new System.Threading.Timer(CheckProcessAndPressF1, null, 0, ultIntervalMs);
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 自动释放大招定时器已启动，间隔: {ultIntervalMs}ms");
        }
    }

    private void StopUltTimer()
    {
        if (ultTimer != null)
        {
            ultTimer.Dispose();
            ultTimer = null;
        }
    }

    private void StartBaodaboTimer()
    {
        if (baodaboTimer == null)
        {
            int interval = GetIntervalFromConfig("BaodaboCheckInterval", 80); // 将默认间隔从5000ms改为80ms，实现低延迟响应
            baodaboTimer = new System.Threading.Timer(CheckBaodaboIcons, null, 0, interval);
        }
    }

    private void StopBaodaboTimer()
    {
        if (baodaboTimer != null)
        {
            baodaboTimer.Dispose();
            baodaboTimer = null;
        }
    }

    private int GetIntervalFromConfig(string key, int defaultValue)
    {
        string intervalStr = ConfigurationManager.AppSettings[key];
        if (int.TryParse(intervalStr, out int interval) && interval > 0)
        {
            return interval;
        }
        return defaultValue;
    }

    private void StartProcessMonitoring()
    {
        // 启动时检查进程
        if (CheckAndSubscribeProcess())
        {
            // 进程存在，开始执行操作
            StartActiveTimers();
        }
        else
        {
            // 进程不存在，启动较低频率的轮询检查
            StartProcessCheckTimer();
        }
    }

    private bool CheckAndSubscribeProcess()
    {
        // 检查TDClient或TDClient.bin进程
        Process[] processes = Process.GetProcessesByName("TDClient");
        if (processes.Length == 0)
        {
            processes = Process.GetProcessesByName("TDClient.bin");
        }

        if (processes.Length > 0)
        {
            // 找到进程，订阅Exited事件
            gameProcess = processes[0];
            gameProcess.EnableRaisingEvents = true;
            gameProcess.Exited += GameProcess_Exited;
            return true;
        }
        return false;
    }

    private void GameProcess_Exited(object sender, EventArgs e)
    {
        // 进程退出，停止所有定时器
        StopAllTimers();
        // 开始轮询检查进程
        StartProcessCheckTimer();
    }

    private void StartProcessCheckTimer()
    {
        // 启动较低频率的轮询检查（每5秒）
        if (processCheckTimer == null)
        {
            processCheckTimer = new System.Threading.Timer(CheckProcessPeriodically, null, 0, 5000);
        }
    }

    private void CheckProcessPeriodically(object state)
    {
        if (CheckAndSubscribeProcess())
        {
            // 进程存在，停止轮询检查
            StopProcessCheckTimer();
            // 开始执行操作
            StartActiveTimers();
        }
    }

    private void StopProcessCheckTimer()
    {
        if (processCheckTimer != null)
        {
            processCheckTimer.Dispose();
            processCheckTimer = null;
        }
    }

    private void StartActiveTimers()
    {
        // 开始所有激活的定时器
        if (isAutoHammerEnabled && isF12Active)
        {
            StartHammerTimer();
        }
        if (isAutoUltEnabled && isF12Active)
        {
            StartUltTimer();
        }
        if (isAutoBaodaboEnabled && isF12Active)
        {
            StartBaodaboTimer();
        }
    }

    private void StopAllTimers()
    {
        // 停止所有定时器
        StopHammerTimer();
        StopUltTimer();
        StopBaodaboTimer();
        StopProcessCheckTimer();
    }

    private void CheckProcessAndClick(object state)
    {
        try
        {
            if (hammerAreaRect.Width <= 0 || hammerAreaRect.Height <= 0)
            {
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 锤子区域无效");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 锤子区域: {hammerAreaRect}");
            
            // 截取锤子区域
            using (var hammerBmp = CaptureArea(hammerAreaRect))
            {
                // 将Bitmap转换为Mat
                using (var hammerMat = OpenCvSharp.Extensions.BitmapConverter.ToMat(hammerBmp))
                {
                    // 计算图像亮度
                    double brightness = CalculateBrightness(hammerMat);
                    System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 锤子图标亮度: {brightness}");
                    
                    // 亮度阈值，需要根据实际游戏调整
                    if (brightness > hammerBrightnessThreshold)
                    {
                        // 模拟右键点击
                        System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 锤子就绪，执行右键点击，亮度: {brightness}");
                        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                        Thread.Sleep(50);
                        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                        // 防止重复触发
                        Thread.Sleep(120);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 检测锤子就绪失败: {ex.Message}");
        }
    }

    private void CheckProcessAndPressF1(object state)
    {
        try
        {
            // 检查技能是否就绪
            if (IsSkillReady())
            {
                // 模拟F1键按下
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 技能就绪，按下F1键");
                keybd_event((byte)VK_F1, 0, KEYEVENTF_KEYDOWN, 0);
                Thread.Sleep(30);
                keybd_event((byte)VK_F1, 0, KEYEVENTF_KEYUP, 0);
                // 防止重复触发
                Thread.Sleep(120);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 检测技能就绪失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查技能是否就绪（CD好了）
    /// </summary>
    private bool IsSkillReady()
    {
        try
        {
            if (skillAreaRect.Width <= 0 || skillAreaRect.Height <= 0)
            {
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 技能区域无效");
                return false;
            }
            
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 技能区域: {skillAreaRect}");
            
            // 截取技能区域
            using (var skillBmp = CaptureArea(skillAreaRect))
            {
                // 将Bitmap转换为Mat
                using (var skillMat = OpenCvSharp.Extensions.BitmapConverter.ToMat(skillBmp))
                {
                    // 这里需要加载技能就绪的模板图像
                    // 暂时使用简单的亮度检测，实际应用中应该使用模板匹配
                    // 计算图像亮度
                    double brightness = CalculateBrightness(skillMat);
                    System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 技能图标亮度: {brightness}");
                    
                    // 亮度阈值，需要根据实际游戏调整
                    return brightness > skillBrightnessThreshold;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 检查技能就绪失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 截取指定区域的屏幕
    /// </summary>
    private System.Drawing.Bitmap CaptureArea(System.Drawing.Rectangle rect)
    {
        // 尝试使用Windows API截取整个屏幕
        var bmp = new System.Drawing.Bitmap(rect.Width, rect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        
        // 方法1: 使用Graphics.CopyFromScreen
        try
        {
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size);
            }
            return bmp;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"使用Graphics.CopyFromScreen截图失败: {ex.Message}");
            // 方法2: 使用Windows API BitBlt
            try
            {
                IntPtr hDC = System.Drawing.Graphics.FromHwnd(IntPtr.Zero).GetHdc();
                IntPtr memDC = CreateCompatibleDC(hDC);
                IntPtr hBitmap = CreateCompatibleBitmap(hDC, rect.Width, rect.Height);
                IntPtr oldBitmap = SelectObject(memDC, hBitmap);
                
                BitBlt(memDC, 0, 0, rect.Width, rect.Height, hDC, rect.Left, rect.Top, SRCCOPY);
                
                SelectObject(memDC, oldBitmap);
                DeleteDC(memDC);
                System.Drawing.Graphics.FromHwnd(IntPtr.Zero).ReleaseHdc(hDC);
                
                System.Drawing.Bitmap bitmap = System.Drawing.Bitmap.FromHbitmap(hBitmap);
                DeleteObject(hBitmap);
                
                return bitmap;
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"使用BitBlt截图失败: {ex2.Message}");
                return bmp; // 返回空位图
            }
        }
    }
    
    // Windows API 函数
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);
    
    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hgdiobj);
    
    private const int SRCCOPY = 0x00CC0020;
    
    /// <summary>
    /// 计算图像亮度
    /// </summary>
    private double CalculateBrightness(Mat mat)
    {
        using (var gray = new Mat())
        {
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
            return Cv2.Mean(gray).Val0;
        }
    }

    // 区域调整相关字段
    private System.Windows.Window skillAreaWindow;
    private System.Windows.Window hammerAreaWindow;
    private System.Windows.Window baodaboAreaWindow; // 包大伯建造区域窗口（W键）
    private System.Windows.Window baodaboEAreaWindow; // 包大伯升级区域窗口（E键）
    private bool isDragging = false;
    private System.Windows.Point dragStartPoint;
    private System.Drawing.Rectangle skillAreaRect;
    private System.Drawing.Rectangle hammerAreaRect;
    private System.Drawing.Rectangle baodaboWAreaRect; // 包大伯建造区域（W键）
    private System.Drawing.Rectangle baodaboEAreaRect; // 包大伯升级区域（E键）

    // 窗口相关API
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// 显示区域调整窗口
    /// </summary>
    private System.Windows.Window ShowAreaWindow(string title, string tooltip, System.Drawing.Rectangle rect, System.Windows.Media.Color color)
    {
        // 创建区域窗口
        System.Windows.Window window = new System.Windows.Window
        {
            Title = title,
            ToolTip = tooltip,
            Width = rect.Width,
            Height = rect.Height,
            Left = rect.Left,
            Top = rect.Top,
            WindowStyle = System.Windows.WindowStyle.None,
            AllowsTransparency = true,
            Background = new System.Windows.Media.SolidColorBrush(color),
            BorderThickness = new System.Windows.Thickness(2),
            BorderBrush = System.Windows.Media.Brushes.Red,
            ResizeMode = System.Windows.ResizeMode.CanResizeWithGrip,
            ShowInTaskbar = false,
            Topmost = true
        };
        
        // 添加鼠标事件处理（每个窗口独立拖拽状态，避免 W/E 等多窗口互相干扰）
        bool isWindowDragging = false;
        System.Windows.Point windowDragStart = default;

        window.MouseLeftButtonDown += (sender, e) => {
            if (e.OriginalSource is System.Windows.Controls.Primitives.ResizeGrip)
                return;

            isWindowDragging = true;
            windowDragStart = e.GetPosition(window);
            window.CaptureMouse();
        };
        
        window.MouseMove += (sender, e) => {
            if (isWindowDragging)
            {
                System.Windows.Point currentPoint = e.GetPosition(window);
                double deltaX = currentPoint.X - windowDragStart.X;
                double deltaY = currentPoint.Y - windowDragStart.Y;
                
                window.Left += deltaX;
                window.Top += deltaY;
            }
        };
        
        window.MouseLeftButtonUp += (sender, e) => {
            isWindowDragging = false;
            window.ReleaseMouseCapture();
        };
        
        // 显示窗口
        window.Show();
        return window;
    }

    /// <summary>
    /// 隐藏区域调整窗口
    /// </summary>
    private System.Drawing.Rectangle HideAreaWindow(System.Windows.Window window)
    {
        System.Drawing.Rectangle rect = new System.Drawing.Rectangle(0, 0, 0, 0);
        if (window != null)
        {
            // 保存当前窗口位置和大小（使用 ActualWidth/Height 以正确反映缩放后的尺寸）
            rect = new System.Drawing.Rectangle(
                (int)Math.Round(window.Left),
                (int)Math.Round(window.Top),
                Math.Max(1, (int)Math.Round(window.ActualWidth)),
                Math.Max(1, (int)Math.Round(window.ActualHeight))
            );
            
            window.Close();
        }
        return rect;
    }

    /// <summary>
    /// 显示技能区域调整窗口
    /// </summary>
    private void ShowSkillAreaWindow()
    {
        skillAreaWindow = ShowAreaWindow(
            "技能区域 (F1)", 
            "用于检测大招技能是否就绪，触发F1键", 
            skillAreaRect, 
            System.Windows.Media.Color.FromArgb(50, 255, 0, 0)
        );
    }

    /// <summary>
    /// 隐藏技能区域调整窗口
    /// </summary>
    private void HideSkillAreaWindow()
    {
        if (skillAreaWindow != null)
        {
            // 保存当前窗口位置和大小
            skillAreaRect = HideAreaWindow(skillAreaWindow);
            skillAreaWindow = null;
        }
    }

    /// <summary>
    /// 显示锤子区域调整窗口
    /// </summary>
    private void ShowHammerAreaWindow()
    {
        hammerAreaWindow = ShowAreaWindow(
            "锤子区域 (右键)", 
            "用于检测锤子技能是否就绪，触发右键点击", 
            hammerAreaRect, 
            System.Windows.Media.Color.FromArgb(50, 0, 255, 0)
        );
    }

    /// <summary>
    /// 隐藏锤子区域调整窗口
    /// </summary>
    private void HideHammerAreaWindow()
    {
        if (hammerAreaWindow != null)
        {
            // 保存当前窗口位置和大小
            hammerAreaRect = HideAreaWindow(hammerAreaWindow);
            hammerAreaWindow = null;
        }
    }

    /// <summary>
    /// 显示包大伯区域调整窗口
    /// </summary>
    private void ShowBaodaboAreaWindow()
    {
        baodaboAreaWindow = ShowAreaWindow(
            "包大伯建造区域 (W)", 
            "用于检测包大伯建造图标，触发W键", 
            baodaboWAreaRect, 
            System.Windows.Media.Color.FromArgb(50, 255, 0, 0)
        );
        
        // 创建包大伯升级区域窗口
        baodaboEAreaWindow = ShowAreaWindow(
            "包大伯升级区域 (E)", 
            "用于检测包大伯升级图标，触发E键", 
            baodaboEAreaRect, 
            System.Windows.Media.Color.FromArgb(50, 0, 255, 0)
        );
    }

    /// <summary>
    /// 隐藏包大伯区域调整窗口
    /// </summary>
    private void HideBaodaboAreaWindow()
    {
        if (baodaboAreaWindow != null)
        {
            // 保存当前窗口位置和大小
            baodaboWAreaRect = HideAreaWindow(baodaboAreaWindow);
            baodaboAreaWindow = null;
        }
        
        if (baodaboEAreaWindow != null)
        {
            // 保存当前窗口位置和大小
            baodaboEAreaRect = HideAreaWindow(baodaboEAreaWindow);
            baodaboEAreaWindow = null;
        }
    }

    /// <summary>
    /// 鼠标按下事件
    /// </summary>
    private void SkillAreaWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        isDragging = true;
        dragStartPoint = e.GetPosition(skillAreaWindow);
        skillAreaWindow.CaptureMouse();
    }

    /// <summary>
    /// 鼠标移动事件
    /// </summary>
    private void SkillAreaWindow_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (isDragging)
        {
            System.Windows.Point currentPoint = e.GetPosition(skillAreaWindow);
            double deltaX = currentPoint.X - dragStartPoint.X;
            double deltaY = currentPoint.Y - dragStartPoint.Y;
            
            skillAreaWindow.Left += deltaX;
            skillAreaWindow.Top += deltaY;
        }
    }

    /// <summary>
    /// 鼠标释放事件
    /// </summary>
    private void SkillAreaWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        isDragging = false;
        skillAreaWindow.ReleaseMouseCapture();
    }

    /// <summary>
    /// 加载技能区域配置
    /// </summary>
    private void LoadSkillArea()
    {
        try
        {
            string left = ConfigurationManager.AppSettings["SkillAreaLeft"];
            string top = ConfigurationManager.AppSettings["SkillAreaTop"];
            string width = ConfigurationManager.AppSettings["SkillAreaWidth"];
            string height = ConfigurationManager.AppSettings["SkillAreaHeight"];
            
            if (!string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(top) && !string.IsNullOrEmpty(width) && !string.IsNullOrEmpty(height))
            {
                skillAreaRect = new System.Drawing.Rectangle(
                    int.Parse(left),
                    int.Parse(top),
                    int.Parse(width),
                    int.Parse(height)
                );
            }
            else
            {
                // 默认值
                skillAreaRect = new System.Drawing.Rectangle(1200, 800, 40, 40);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载技能区域配置失败: {ex.Message}");
            skillAreaRect = new System.Drawing.Rectangle(1200, 800, 40, 40);
        }
    }

    /// <summary>
    /// 保存技能区域配置
    /// </summary>
    private void SaveSkillArea()
    {
        try
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            
            // 确保配置项存在
            if (config.AppSettings.Settings["SkillAreaLeft"] == null)
                config.AppSettings.Settings.Add("SkillAreaLeft", skillAreaRect.Left.ToString());
            else
                config.AppSettings.Settings["SkillAreaLeft"].Value = skillAreaRect.Left.ToString();
            
            if (config.AppSettings.Settings["SkillAreaTop"] == null)
                config.AppSettings.Settings.Add("SkillAreaTop", skillAreaRect.Top.ToString());
            else
                config.AppSettings.Settings["SkillAreaTop"].Value = skillAreaRect.Top.ToString();
            
            if (config.AppSettings.Settings["SkillAreaWidth"] == null)
                config.AppSettings.Settings.Add("SkillAreaWidth", skillAreaRect.Width.ToString());
            else
                config.AppSettings.Settings["SkillAreaWidth"].Value = skillAreaRect.Width.ToString();
            
            if (config.AppSettings.Settings["SkillAreaHeight"] == null)
                config.AppSettings.Settings.Add("SkillAreaHeight", skillAreaRect.Height.ToString());
            else
                config.AppSettings.Settings["SkillAreaHeight"].Value = skillAreaRect.Height.ToString();
            
            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
            
            System.Diagnostics.Debug.WriteLine($"技能区域已保存: {skillAreaRect}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存技能区域配置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载锤子区域配置
    /// </summary>
    private void LoadHammerArea()
    {
        try
        {
            string left = ConfigurationManager.AppSettings["HammerAreaLeft"];
            string top = ConfigurationManager.AppSettings["HammerAreaTop"];
            string width = ConfigurationManager.AppSettings["HammerAreaWidth"];
            string height = ConfigurationManager.AppSettings["HammerAreaHeight"];
            
            if (!string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(top) && !string.IsNullOrEmpty(width) && !string.IsNullOrEmpty(height))
            {
                hammerAreaRect = new System.Drawing.Rectangle(
                    int.Parse(left),
                    int.Parse(top),
                    int.Parse(width),
                    int.Parse(height)
                );
            }
            else
            {
                // 默认值
                hammerAreaRect = new System.Drawing.Rectangle(1150, 800, 40, 40);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载锤子区域配置失败: {ex.Message}");
            hammerAreaRect = new System.Drawing.Rectangle(1150, 800, 40, 40);
        }
    }

    /// <summary>
    /// 保存锤子区域配置
    /// </summary>
    private void SaveHammerArea()
    {
        try
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            
            // 确保配置项存在
            if (config.AppSettings.Settings["HammerAreaLeft"] == null)
                config.AppSettings.Settings.Add("HammerAreaLeft", hammerAreaRect.Left.ToString());
            else
                config.AppSettings.Settings["HammerAreaLeft"].Value = hammerAreaRect.Left.ToString();
            
            if (config.AppSettings.Settings["HammerAreaTop"] == null)
                config.AppSettings.Settings.Add("HammerAreaTop", hammerAreaRect.Top.ToString());
            else
                config.AppSettings.Settings["HammerAreaTop"].Value = hammerAreaRect.Top.ToString();
            
            if (config.AppSettings.Settings["HammerAreaWidth"] == null)
                config.AppSettings.Settings.Add("HammerAreaWidth", hammerAreaRect.Width.ToString());
            else
                config.AppSettings.Settings["HammerAreaWidth"].Value = hammerAreaRect.Width.ToString();
            
            if (config.AppSettings.Settings["HammerAreaHeight"] == null)
                config.AppSettings.Settings.Add("HammerAreaHeight", hammerAreaRect.Height.ToString());
            else
                config.AppSettings.Settings["HammerAreaHeight"].Value = hammerAreaRect.Height.ToString();
            
            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
            
            System.Diagnostics.Debug.WriteLine($"锤子区域已保存: {hammerAreaRect}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存锤子区域配置失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载包大伯区域配置
    /// </summary>
    private void LoadBaodaboArea()
    {
        try
        {
            // 加载包大伯建造区域（W键）
            string wLeft = ConfigurationManager.AppSettings["BaodaboWAreaLeft"];
            string wTop = ConfigurationManager.AppSettings["BaodaboWAreaTop"];
            string wWidth = ConfigurationManager.AppSettings["BaodaboWAreaWidth"];
            string wHeight = ConfigurationManager.AppSettings["BaodaboWAreaHeight"];
            
            if (!string.IsNullOrEmpty(wLeft) && !string.IsNullOrEmpty(wTop) && !string.IsNullOrEmpty(wWidth) && !string.IsNullOrEmpty(wHeight))
            {
                baodaboWAreaRect = new System.Drawing.Rectangle(
                    int.Parse(wLeft),
                    int.Parse(wTop),
                    int.Parse(wWidth),
                    int.Parse(wHeight)
                );
            }
            else
            {
                // 默认值
                baodaboWAreaRect = new System.Drawing.Rectangle(1250, 800, 40, 40);
            }
            
            // 加载包大伯升级区域（E键）
            string eLeft = ConfigurationManager.AppSettings["BaodaboEAreaLeft"];
            string eTop = ConfigurationManager.AppSettings["BaodaboETop"];
            string eWidth = ConfigurationManager.AppSettings["BaodaboEWidth"];
            string eHeight = ConfigurationManager.AppSettings["BaodaboEHeight"];
            
            if (!string.IsNullOrEmpty(eLeft) && !string.IsNullOrEmpty(eTop) && !string.IsNullOrEmpty(eWidth) && !string.IsNullOrEmpty(eHeight))
            {
                baodaboEAreaRect = new System.Drawing.Rectangle(
                    int.Parse(eLeft),
                    int.Parse(eTop),
                    int.Parse(eWidth),
                    int.Parse(eHeight)
                );
            }
            else
            {
                // 默认值
                baodaboEAreaRect = new System.Drawing.Rectangle(1300, 800, 40, 40);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载包大伯区域配置失败: {ex.Message}");
            baodaboWAreaRect = new System.Drawing.Rectangle(1250, 800, 40, 40);
            baodaboEAreaRect = new System.Drawing.Rectangle(1300, 800, 40, 40);
        }
    }

    /// <summary>
    /// 保存包大伯区域配置
    /// </summary>
    private void SaveBaodaboArea()
    {
        try
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            
            // 保存包大伯建造区域（W键）
            if (config.AppSettings.Settings["BaodaboWAreaLeft"] == null)
                config.AppSettings.Settings.Add("BaodaboWAreaLeft", baodaboWAreaRect.Left.ToString());
            else
                config.AppSettings.Settings["BaodaboWAreaLeft"].Value = baodaboWAreaRect.Left.ToString();
            
            if (config.AppSettings.Settings["BaodaboWAreaTop"] == null)
                config.AppSettings.Settings.Add("BaodaboWAreaTop", baodaboWAreaRect.Top.ToString());
            else
                config.AppSettings.Settings["BaodaboWAreaTop"].Value = baodaboWAreaRect.Top.ToString();
            
            if (config.AppSettings.Settings["BaodaboWAreaWidth"] == null)
                config.AppSettings.Settings.Add("BaodaboWAreaWidth", baodaboWAreaRect.Width.ToString());
            else
                config.AppSettings.Settings["BaodaboWAreaWidth"].Value = baodaboWAreaRect.Width.ToString();
            
            if (config.AppSettings.Settings["BaodaboWAreaHeight"] == null)
                config.AppSettings.Settings.Add("BaodaboWAreaHeight", baodaboWAreaRect.Height.ToString());
            else
                config.AppSettings.Settings["BaodaboWAreaHeight"].Value = baodaboWAreaRect.Height.ToString();
            
            // 保存包大伯升级区域（E键）
            if (config.AppSettings.Settings["BaodaboEAreaLeft"] == null)
                config.AppSettings.Settings.Add("BaodaboEAreaLeft", baodaboEAreaRect.Left.ToString());
            else
                config.AppSettings.Settings["BaodaboEAreaLeft"].Value = baodaboEAreaRect.Left.ToString();
            
            if (config.AppSettings.Settings["BaodaboETop"] == null)
                config.AppSettings.Settings.Add("BaodaboETop", baodaboEAreaRect.Top.ToString());
            else
                config.AppSettings.Settings["BaodaboETop"].Value = baodaboEAreaRect.Top.ToString();
            
            if (config.AppSettings.Settings["BaodaboEWidth"] == null)
                config.AppSettings.Settings.Add("BaodaboEWidth", baodaboEAreaRect.Width.ToString());
            else
                config.AppSettings.Settings["BaodaboEWidth"].Value = baodaboEAreaRect.Width.ToString();
            
            if (config.AppSettings.Settings["BaodaboEHeight"] == null)
                config.AppSettings.Settings.Add("BaodaboEHeight", baodaboEAreaRect.Height.ToString());
            else
                config.AppSettings.Settings["BaodaboEHeight"].Value = baodaboEAreaRect.Height.ToString();
            
            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
            
            System.Diagnostics.Debug.WriteLine($"包大伯建造区域已保存: {baodaboWAreaRect}");
            System.Diagnostics.Debug.WriteLine($"包大伯升级区域已保存: {baodaboEAreaRect}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存包大伯区域配置失败: {ex.Message}");
        }
    }



    /// <summary>
    /// 显示/隐藏所有区域按钮点击事件
    /// </summary>
    private void btnShowAllAreas_Click(object sender, RoutedEventArgs e)
    {
        if (skillAreaWindow == null && hammerAreaWindow == null && baodaboAreaWindow == null && baodaboEAreaWindow == null)
        {
            // 显示所有区域
            ShowSkillAreaWindow();
            ShowHammerAreaWindow();
            ShowBaodaboAreaWindow();
            btnShowAllAreas.Content = "隐藏所有区域";
        }
        else
        {
            // 隐藏所有区域
            HideSkillAreaWindow();
            HideHammerAreaWindow();
            HideBaodaboAreaWindow();
            btnShowAllAreas.Content = "显示所有区域";
        }
    }

    /// <summary>
    /// 保存所有区域按钮点击事件
    /// </summary>
    private void btnSaveAllAreas_Click(object sender, RoutedEventArgs e)
    {
        // 隐藏所有区域并保存配置
        HideSkillAreaWindow();
        HideHammerAreaWindow();
        HideBaodaboAreaWindow();
        
        SaveSkillArea();
        SaveHammerArea();
        SaveBaodaboArea();

        LoadSkillArea();
        LoadHammerArea();
        LoadBaodaboArea();
        
        btnShowAllAreas.Content = "显示所有区域";
        System.Windows.MessageBox.Show("所有区域已保存！", "提示");
    }

    private void cbAutoHammer_Checked(object sender, RoutedEventArgs e)
    {
        isAutoHammerEnabled = true;
        // 保存到配置文件
        SaveAutoSetting("AutoHammerEnabled", true);
        if (isF12Active)
        {
            // 重新启动进程监控，确保使用新的设置
            StopAllTimers();
            StartProcessMonitoring();
        }
    }

    private void cbAutoHammer_Unchecked(object sender, RoutedEventArgs e)
    {
        isAutoHammerEnabled = false;
        // 保存到配置文件
        SaveAutoSetting("AutoHammerEnabled", false);
        StopHammerTimer();
    }

    private void cbAutoUlt_Checked(object sender, RoutedEventArgs e)
    {
        isAutoUltEnabled = true;
        // 保存到配置文件
        SaveAutoSetting("AutoUltEnabled", true);
        if (isF12Active)
        {
            // 重新启动进程监控，确保使用新的设置
            StopAllTimers();
            StartProcessMonitoring();
        }
    }

    private void cbAutoUlt_Unchecked(object sender, RoutedEventArgs e)
    {
        isAutoUltEnabled = false;
        // 保存到配置文件
        SaveAutoSetting("AutoUltEnabled", false);
        StopUltTimer();
    }

    private void SaveAutoSetting(string key, bool enabled)
    {
        SaveAppSetting(key, enabled.ToString());
    }

    private void SaveAppSetting(string key, string value)
    {
        try
        {
            Configuration config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            if (config.AppSettings.Settings[key] == null)
                config.AppSettings.Settings.Add(key, value);
            else
                config.AppSettings.Settings[key].Value = value;

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("appSettings");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存配置失败 [{key}]: {ex.Message}");
        }
    }

    private void Interval_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox || textBox.Tag is not string configKey)
            return;

        if (!int.TryParse(textBox.Text, out int interval) || interval <= 0)
        {
            if (configKey == "HammerInterval")
                textBox.Text = hammerIntervalMs.ToString();
            else if (configKey == "UltInterval")
                textBox.Text = ultIntervalMs.ToString();
            return;
        }

        if (configKey == "HammerInterval")
        {
            if (interval == hammerIntervalMs)
                return;

            hammerIntervalMs = interval;
            SaveAppSetting("HammerInterval", interval.ToString());
            RestartHammerTimerIfActive();
        }
        else if (configKey == "UltInterval")
        {
            if (interval == ultIntervalMs)
                return;

            ultIntervalMs = interval;
            SaveAppSetting("UltInterval", interval.ToString());
            RestartUltTimerIfActive();
        }
    }

    private void Threshold_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox textBox || textBox.Tag is not string configKey)
            return;

        if (!int.TryParse(textBox.Text, out int threshold) || threshold < 0)
        {
            switch (configKey)
            {
                case "SkillBrightnessThreshold":
                    textBox.Text = skillBrightnessThreshold.ToString();
                    break;
                case "HammerBrightnessThreshold":
                    textBox.Text = hammerBrightnessThreshold.ToString();
                    break;
                case "BaodaboWBrightnessThreshold":
                    textBox.Text = baodaboWBrightnessThreshold.ToString();
                    break;
                case "BaodaboEBrightnessThreshold":
                    textBox.Text = baodaboEBrightnessThreshold.ToString();
                    break;
            }
            return;
        }

        switch (configKey)
        {
            case "SkillBrightnessThreshold":
                if (threshold == skillBrightnessThreshold)
                    return;
                skillBrightnessThreshold = threshold;
                break;
            case "HammerBrightnessThreshold":
                if (threshold == hammerBrightnessThreshold)
                    return;
                hammerBrightnessThreshold = threshold;
                break;
            case "BaodaboWBrightnessThreshold":
                if (threshold == baodaboWBrightnessThreshold)
                    return;
                baodaboWBrightnessThreshold = threshold;
                break;
            case "BaodaboEBrightnessThreshold":
                if (threshold == baodaboEBrightnessThreshold)
                    return;
                baodaboEBrightnessThreshold = threshold;
                break;
            default:
                return;
        }

        SaveAppSetting(configKey, threshold.ToString());
    }

    private void RestartHammerTimerIfActive()
    {
        if (isAutoHammerEnabled && isF12Active && hammerTimer != null)
        {
            StopHammerTimer();
            StartHammerTimer();
        }
    }

    private void RestartUltTimerIfActive()
    {
        if (isAutoUltEnabled && isF12Active && ultTimer != null)
        {
            StopUltTimer();
            StartUltTimer();
        }
    }

    private void cbDebugMode_Checked(object sender, RoutedEventArgs e)
    {
        isDebugModeEnabled = true;
        panelDebug.Visibility = Visibility.Visible;
        SaveAppSetting("DebugModeEnabled", "true");
        StartDebugTimer();
    }

    private void cbDebugMode_Unchecked(object sender, RoutedEventArgs e)
    {
        isDebugModeEnabled = false;
        panelDebug.Visibility = Visibility.Collapsed;
        SaveAppSetting("DebugModeEnabled", "false");
        StopDebugTimer();
    }

    private void StartDebugTimer()
    {
        if (debugTimer == null)
        {
            debugTimer = new System.Threading.Timer(UpdateDebugBrightness, null, 0, DebugUpdateIntervalMs);
        }
    }

    private void StopDebugTimer()
    {
        if (debugTimer != null)
        {
            debugTimer.Dispose();
            debugTimer = null;
        }
    }

    private void UpdateDebugBrightness(object state)
    {
        if (!isDebugModeEnabled)
            return;

        try
        {
            double skillBrightness = GetAreaBrightness(skillAreaRect);
            double hammerBrightness = GetAreaBrightness(hammerAreaRect);
            double baodaboWBrightness = GetAreaBrightness(baodaboWAreaRect);
            double baodaboEBrightness = GetAreaBrightness(baodaboEAreaRect);

            Dispatcher.Invoke(() =>
            {
                UpdateBrightnessLabel(tbSkillBrightness, tbSkillStatus, skillBrightness, skillBrightnessThreshold, "就绪");
                UpdateBrightnessLabel(tbHammerBrightness, tbHammerStatus, hammerBrightness, hammerBrightnessThreshold, "就绪");
                UpdateBrightnessLabel(tbBaodaboWBrightness, tbBaodaboWStatus, baodaboWBrightness, baodaboWBrightnessThreshold, "W就绪");
                UpdateBrightnessLabel(tbBaodaboEBrightness, tbBaodaboEStatus, baodaboEBrightness, baodaboEBrightnessThreshold, "E就绪");
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 调试亮度更新失败: {ex.Message}");
        }
    }

    private double GetAreaBrightness(System.Drawing.Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return -1;

        using (var bmp = CaptureArea(rect))
        using (var mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(bmp))
        {
            return CalculateBrightness(mat);
        }
    }

    private void UpdateBrightnessLabel(TextBlock brightnessBlock, TextBlock statusBlock, double brightness, int threshold, string readyText)
    {
        if (brightness < 0)
        {
            brightnessBlock.Text = "无效";
            statusBlock.Text = "区域无效";
            brightnessBlock.Foreground = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#999999"));
            statusBlock.Foreground = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#999999"));
            return;
        }

        brightnessBlock.Text = brightness.ToString("F1");
        bool isReady = brightness > threshold;
        statusBlock.Text = isReady ? readyText : "未就绪";
        var readyColor = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#4CAF50"));
        var notReadyColor = new SolidColorBrush((WpfColor)WpfColorConverter.ConvertFromString("#F44336"));
        brightnessBlock.Foreground = isReady ? readyColor : notReadyColor;
        statusBlock.Foreground = isReady ? readyColor : notReadyColor;
    }

    private void cbAutoBaodabo_Checked(object sender, RoutedEventArgs e)
    {
        isAutoBaodaboEnabled = true;
        // 保存到配置文件
        SaveAutoSetting("AutoBaodaboEnabled", true);
        if (isF12Active)
        {
            // 重新启动进程监控，确保使用新的设置
            StopAllTimers();
            StartProcessMonitoring();
        }
    }

    private void cbAutoBaodabo_Unchecked(object sender, RoutedEventArgs e)
    {
        isAutoBaodaboEnabled = false;
        // 保存到配置文件
        SaveAutoSetting("AutoBaodaboEnabled", false);
        StopBaodaboTimer();
    }



    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F12)
        {
            ToggleF12Status();
            e.Handled = true;
        }
    }

    // 上次操作时间，用于防止重复触发
    private DateTime lastBaodaboActionTime = DateTime.MinValue;
    private const int BaodaboActionCooldown = 1000; // 操作冷却时间，单位：毫秒
    
    private void CheckBaodaboIcons(object state)
    {
        try
        {
            // 检查冷却时间，防止重复触发
            if ((DateTime.Now - lastBaodaboActionTime).TotalMilliseconds < BaodaboActionCooldown)
            {
                return;
            }
            
            if (baodaboWAreaRect.Width <= 0 || baodaboWAreaRect.Height <= 0)
            {
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 包大伯建造区域无效");
                return;
            }
            
            // 确保升级区域有效
            if (baodaboEAreaRect.Width <= 0 || baodaboEAreaRect.Height <= 0)
            {
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 包大伯升级区域无效");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 包大伯建造区域: {baodaboWAreaRect}");
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 包大伯升级区域: {baodaboEAreaRect}");
            
            // 优先检测建造图标（W键）
            using (var wBmp = CaptureArea(baodaboWAreaRect))
            {
                using (var wMat = OpenCvSharp.Extensions.BitmapConverter.ToMat(wBmp))
                {
                    double wBrightness = CalculateBrightness(wMat);
                    System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 包大伯建造图标亮度: {wBrightness}");
                    
                    if (wBrightness > baodaboWBrightnessThreshold)
                    {
                        // 模拟按下W键（建造）
                        System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 包大伯建造图标就绪，按下W键建造");
                        keybd_event((byte)VK_W, 0, KEYEVENTF_KEYDOWN, 0);
                        Thread.Sleep(50);
                        keybd_event((byte)VK_W, 0, KEYEVENTF_KEYUP, 0);
                        
                        // 更新上次操作时间
                        lastBaodaboActionTime = DateTime.Now;
                        return; // 建造成功或尝试过建造，直接返回
                    }
                }
            }
            
            // 如果建造图标未就绪，检测升级图标（E键）
            using (var eBmp = CaptureArea(baodaboEAreaRect))
            {
                using (var eMat = OpenCvSharp.Extensions.BitmapConverter.ToMat(eBmp))
                {
                    double eBrightness = CalculateBrightness(eMat);
                    System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 包大伯升级图标亮度: {eBrightness}");
                    
                    if (eBrightness > baodaboEBrightnessThreshold)
                    {
                        // 模拟按下E键（升级）
                        System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 包大伯升级图标就绪，按下E键升级");
                        keybd_event((byte)VK_E, 0, KEYEVENTF_KEYDOWN, 0);
                        Thread.Sleep(50);
                        keybd_event((byte)VK_E, 0, KEYEVENTF_KEYUP, 0);
                        
                        // 更新上次操作时间
                        lastBaodaboActionTime = DateTime.Now;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 检测包大伯图标失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 模拟鼠标点击
    /// </summary>
    private void SimulateMouseClick(int x, int y)
    {
        try
        {
            // 设置鼠标位置
            SetCursorPos(x, y);
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 设置鼠标位置: ({x}, {y})");
            
            // 模拟鼠标左键按下
            mouse_event(MOUSEEVENTF_LEFTDOWN, x, y, 0, 0);
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 模拟鼠标左键按下");
            
            // 短暂延迟
            Thread.Sleep(50);
            
            // 模拟鼠标左键释放
            mouse_event(MOUSEEVENTF_LEFTUP, x, y, 0, 0);
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 模拟鼠标左键释放");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss.fff")}] 模拟鼠标点击失败: {ex.Message}");
        }
    }

    // 鼠标事件常量
    private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
    private const uint MOUSEEVENTF_LEFTUP = 0x04;

    // 导入Windows API
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, int dwExtraInfo);


}

public class Hero
{
    public string Name { get; set; }
    public string Country { get; set; }
}