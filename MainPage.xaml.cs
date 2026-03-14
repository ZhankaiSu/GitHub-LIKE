using System.Net;
using System.Net.Sockets;
using System.Text;
using ZXing.Net.Maui;

namespace MauiApp1;

public partial class MainPage : ContentPage
{
    private Socket? _socket;
    private bool _isConnected = false;
    private readonly object _lock = new();

    public MainPage()
    {
        InitializeComponent();

        // 初始化时加载上次的补光灯偏好
        bool isTorchOn = Preferences.Get("UserTorchPreference", false);
        TorchSwitch.IsToggled = isTorchOn;
        TorchStatusLabel.Text = isTorchOn ? "补光灯: 开" : "补光灯: 关";

        if (BarcodeReader != null)
        {
            BarcodeReader.Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormat.QrCode | BarcodeFormat.Code128 | BarcodeFormat.Ean13,
                AutoRotate = true,   // 保持开启，允许任意角度扫描
                Multiple = false,    // 保持关闭，一次只扫一个
                TryHarder = true     // 保持开启，会进行深度分析，虽然慢微毫秒但识别率高
            };
        }
        StartAutoConnectLoop();
    }

    private async void OnStartScanClicked(object sender, EventArgs e)
    {
        ScannerOverlay.IsVisible = true;
        await Task.Delay(300);
        BarcodeReader.IsDetecting = true;

        // 根据记录的偏好设置补光灯状态
        try
        {
            BarcodeReader.IsTorchOn = TorchSwitch.IsToggled;
        }
        catch { /* 设备不支持 */ }
    }

    // 补光灯切换逻辑
    private void OnTorchToggled(object sender, ToggledEventArgs e)
    {
        bool isOn = e.Value;

        // 1. 立即保存用户偏好
        Preferences.Set("UserTorchPreference", isOn);

        // 2. 更新UI文字
        TorchStatusLabel.Text = isOn ? "补光灯: 开" : "补光灯: 关";

        // 3. 如果当前正在扫描，立即切换物理灯光
        if (BarcodeReader != null && ScannerOverlay.IsVisible)
        {
            try
            {
                BarcodeReader.IsTorchOn = isOn;
            }
            catch { }
        }
    }

    //private async void OnStartScanClicked(object sender, EventArgs e)
    //{
    //    ScannerOverlay.IsVisible = true;

    //    // 给 UI 一点渲染时间
    //    await Task.Delay(300);

    //    BarcodeReader.IsDetecting = true;

    //    // 💡 这是一个“黑科技”补丁：
    //    // 如果画面模糊，在启动瞬间闪一下灯，可以强迫摄像头重新寻找焦距
    //    try
    //    {
    //        BarcodeReader.IsTorchOn = true;
    //        await Task.Delay(150);
    //        BarcodeReader.IsTorchOn = false;
    //    }
    //    catch { /* 部分设备不支持闪光灯则跳过 */ }
    //}

    // 页面销毁或隐藏时，务必关闭摄像头，防止资源占用导致下次黑屏
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BarcodeReader != null)
        {
            BarcodeReader.IsDetecting = false;
            ScannerOverlay.IsVisible = false;
            try { BarcodeReader.IsTorchOn = false; } catch { }
        }
    }

    // --- 网络状态监听 ---
    private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
    {
        // 如果当前没有网络访问权限，强制断开
        if (e.NetworkAccess != NetworkAccess.Internet && e.NetworkAccess != NetworkAccess.ConstrainedInternet && e.NetworkAccess != NetworkAccess.Local)
        {
            DisconnectSocket("网络连接已断开");
        }
    }

    // --- 自动重连逻辑 ---
    private void StartAutoConnectLoop()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                if (_isConnected)
                {
                    // 轮询检查 Socket 物理状态 (解决拔网线/关WiFi后状态不更新的问题)
                    if (_socket == null || !IsSocketConnected(_socket))
                    {
                        DisconnectSocket("服务器连接中断");
                    }
                }

                if (!_isConnected)
                {
                    // 检查手机是否有网络，没网就不尝试连服务器了
                    if (Connectivity.Current.NetworkAccess != NetworkAccess.None &&
                        Connectivity.Current.NetworkAccess != NetworkAccess.Unknown)
                    {
                        await TryConnect();
                    }
                }

                await Task.Delay(3000);
            }
        });
    }

    // 深度检查 Socket 连接状态
    private bool IsSocketConnected(Socket s)
    {
        try
        {
            return !((s.Poll(1000, SelectMode.SelectRead) && (s.Available == 0)) || !s.Connected);
        }
        catch
        {
            return false;
        }
    }

    private async Task TryConnect()
    {
        string ip = Preferences.Get("IP", "192.168.6.6");
        if (!int.TryParse(Preferences.Get("Port", "5050"), out int port)) port = 8080;

        try
        {
            Socket tempSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // 异步连接
            var connectTask = tempSocket.ConnectAsync(IPAddress.Parse(ip), port);

            // 设置 2 秒超时
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(2000));

            if (completedTask == connectTask && tempSocket.Connected)
            {
                lock (_lock) { _socket = tempSocket; _isConnected = true; }
                UpdateUIStatus(true);
            }
            else
            {
                // 超时或失败
                tempSocket.Close();
                UpdateUIStatus(false);
            }
        }
        catch
        {
            UpdateUIStatus(false);
        }
    }

    private void DisconnectSocket(string reason)
    {
        lock (_lock)
        {
            if (_socket != null)
            {
                try { _socket.Close(); } catch { }
                _socket = null;
            }
            _isConnected = false;
        }
        UpdateUIStatus(false);
        // 可选：记录日志 AddLog($"[系统] {reason}", Colors.Red);
    }

    private void UpdateUIStatus(bool connected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = connected ? "✅ 已连接服务器" : "❌ 未连接 (自动重试中...)";
            StatusFrame.BackgroundColor = connected ? Colors.Green : Colors.Red;
        });
    }

    // --- 扫码逻辑 ---
    //private async void OnStartScanClicked(object sender, EventArgs e)
    //{
    //    // 1. 先显示遮罩层
    //    ScannerOverlay.IsVisible = true;

    //    // 2. 【关键修复】增加微小延时，等待UI渲染完毕后再开启摄像头检测
    //    // 这通常能解决部分设备上点击后黑屏的问题
    //    await Task.Delay(100);

    //    BarcodeReader.IsDetecting = true;
    //}

    private void OnStopScanClicked(object sender, EventArgs e)
    {
        BarcodeReader.IsDetecting = false;
        try { BarcodeReader.IsTorchOn = false; } catch { }
        ScannerOverlay.IsVisible = false;
    }

    private void OnBarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        var result = e.Results?.FirstOrDefault();
        if (result == null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            BarcodeReader.IsDetecting = false;
            ScannerOverlay.IsVisible = false;
            try { BarcodeReader.IsTorchOn = false; } catch { } // 添加这行来关闭闪光灯
            try { Vibration.Default.Vibrate(); } catch { }
            ProcessAndSend(result.Value);
        });
    }

    // --- 扫码数据直接发送 ---
    private void ProcessAndSend(string scanCode)
    {
        SendData(scanCode);
    }

    private void SendData(string data)
    {
        // 再次检查连接状态
        if (!_isConnected || _socket == null)
        {
            AddLog($"❌ 未连接，丢弃: {data}", Colors.Red);
            // 尝试触发一次断线检测
            DisconnectSocket("发送失败，判定为断开");
            return;
        }

        try
        {
            _socket.Send(Encoding.UTF8.GetBytes(data));
            AddLog($"📤 已发送: {data}", Colors.Black);
        }
        catch
        {
            // 发送异常，说明连接已断
            DisconnectSocket("发送异常");
            AddLog("❌ 连接已断开", Colors.Red);
        }
    }

    private void AddLog(string msg, Color color)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            LogEditor.Text = $"[{time}] {msg}\n" + LogEditor.Text;
            LogEditor.CursorPosition = 0;
        });
    }

    private void OnClearLogClicked(object sender, EventArgs e) => LogEditor.Text = "";
}