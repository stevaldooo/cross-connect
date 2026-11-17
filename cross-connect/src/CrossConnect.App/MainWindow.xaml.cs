using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using CrossConnect.Core.Input;
using CrossConnect.Core.Networking;
using CrossConnect.Core.Protocol;

namespace CrossConnect.App;

/// <summary>
/// Code-behind for MainWindow.
///
/// Responsibilities:
/// - React to button clicks and delegate to CrossConnect.Core objects.
/// - Display log messages and status updates.
/// - Marshal all Core events back to the UI thread via Dispatcher.
///
/// No business logic lives here — this file is UI glue only. All switching
/// behaviour lives in InputSwitchController (host) and InputReceivingController
/// (client). Keystroke and mouse-event content is deliberately never logged.
///
/// Event-marshalling rule: Core events arrive on network/hook threads; handlers
/// here use Dispatcher.BeginInvoke (never the blocking Invoke) because the
/// raising thread may itself be waiting on shared state.
/// </summary>
public partial class MainWindow : Window
{
    private ICrossConnectServer? _server;
    private ICrossConnectClient? _client;

    private InputSwitchController?    _switchController;     // host brain
    private InputReceivingController? _receivingController;  // client brain

    private bool _clientConnected;

    public MainWindow()
    {
        InitializeComponent();
        AppendLog("CrossConnect ready. Choose a mode above.");
    }

    // ── Mode buttons ─────────────────────────────────────────────────────────

    private async void HostModeButton_Click(object sender, RoutedEventArgs e)
    {
        HostModeButton.IsEnabled   = false;
        ClientModeButton.IsEnabled = false;
        ClientPanel.Visibility     = Visibility.Collapsed;
        HostPanel.Visibility       = Visibility.Visible;

        int port = ProtocolConstants.DefaultPort;

        _server = new CrossConnectServer();
        _server.ClientConnected    += OnClientConnected;
        _server.ClientDisconnected += OnClientDisconnected;
        _server.MessageReceived    += OnServerMessageReceived;

        try
        {
            await _server.StartAsync(port);

            string localIp = GetLocalIpAddress();
            HostInfoText.Text = $"Listening on  {localIp}:{port}";
            SetStatus($"● Hosting on {localIp}:{port}", "#22C55E");
            AppendLog($"Server started. Listening on port {port}.");
            AppendLog($"Local IP: {localIp}");
            SetForwardingStatus("Waiting for a client…");
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Failed to start server: {ex.Message}");
            SetStatus("● Error starting server", "#EF4444");
            ResetModeButtons();
        }
    }

    private void ClientModeButton_Click(object sender, RoutedEventArgs e)
    {
        HostModeButton.IsEnabled   = false;
        ClientModeButton.IsEnabled = false;
        HostPanel.Visibility       = Visibility.Collapsed;
        ClientPanel.Visibility     = Visibility.Visible;

        AppendLog("Client mode selected. Enter the host IP and press Connect.");
    }

    // ── Host controls ─────────────────────────────────────────────────────────

    private async void StopHostButton_Click(object sender, RoutedEventArgs e)
    {
        if (_server is null) return;

        _switchController?.Dispose();
        _switchController = null;

        await _server.StopAsync();
        await _server.DisposeAsync();
        _server = null;

        HostPanel.Visibility = Visibility.Collapsed;
        SetStatus("● Not connected", "#6B6B9A");
        AppendLog("Server stopped.");
        ResetModeButtons();
    }

    // ── Input sharing controls (host) ─────────────────────────────────────────

    private void StartForwardingButton_Click(object sender, RoutedEventArgs e)
        => _switchController?.Arm();

    private void StopForwardingButton_Click(object sender, RoutedEventArgs e)
        => _switchController?.Disarm();

    private void ArrangementBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_switchController is null) return;
        _switchController.Arrangement = ArrangementBox.SelectedIndex == 1
            ? ScreenArrangement.ClientLeft
            : ScreenArrangement.ClientRight;
    }

    private InputSwitchController CreateSwitchController()
    {
        var controller = new InputSwitchController(
            new KeyboardHook(),
            new MouseHook(),
            new ScreenService(),
            SendToActiveClientAsync);

        controller.ForwardingChanged += forwarding => BeginInvoke(() =>
        {
            if (forwarding)
            {
                SetForwardingStatus("Forwarding input to the client");
                SetStatus("● Forwarding input to client", "#38BDF8");
                AppendLog("Control transferred to the client.");
            }
            else
            {
                SetForwardingStatus(_clientConnected
                    ? $"Watching the {(ArrangementBox.SelectedIndex == 1 ? "left" : "right")} edge"
                    : "Waiting for a client…");
                SetStatus(_clientConnected ? "● Client connected" : "● Hosting — waiting for connection",
                          _clientConnected ? "#22C55E" : "#F59E0B");
                AppendLog("Control returned to this computer.");
            }
            UpdateSharingButtons();
        });

        controller.ArmedChanged += armed => BeginInvoke(() =>
        {
            AppendLog(armed ? "Edge switching enabled." : "Edge switching disabled.");
            UpdateSharingButtons();
        });

        controller.StateMessage += text => BeginInvoke(() => SetForwardingStatus(text));

        return controller;
    }

    private Task SendToActiveClientAsync(BaseMessage message)
    {
        var endpoint = _switchController?.ClientEndpoint;
        if (endpoint is null || _server is null)
            return Task.CompletedTask;
        return _server.SendAsync(endpoint, message);
    }

    private void UpdateSharingButtons()
    {
        bool controllerActive = _switchController is not null;
        StartForwardingButton.IsEnabled = controllerActive && !_switchController!.IsArmed && !_switchController.IsForwarding;
        StopForwardingButton.IsEnabled  = controllerActive && (_switchController!.IsArmed || _switchController.IsForwarding);
    }

    // ── Client controls ───────────────────────────────────────────────────────

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        string host = HostIpBox.Text.Trim();
        if (!int.TryParse(PortBox.Text.Trim(), out int port))
        {
            AppendLog("[ERROR] Invalid port number.");
            return;
        }

        ConnectButton.IsEnabled    = false;
        DisconnectButton.IsEnabled = false;

        _client = new CrossConnectClient();
        _client.Connected        += OnClientConnected_Client;
        _client.Disconnected     += OnClientDisconnected_Client;
        _client.MessageReceived  += OnClientMessageReceived;

        try
        {
            AppendLog($"Connecting to {host}:{port}…");
            await _client.ConnectAsync(host, port, Environment.MachineName);

            // Tell the host our screen size so it can map cursor coordinates.
            (int w, int h) = ScreenInterop.GetPrimaryScreenSize();
            await _client.SendAsync(new ScreenInfoMessage { Width = w, Height = h });

            _receivingController = new InputReceivingController(
                _client, new KeyboardHook(), new MouseHook(), new ScreenService());
            _receivingController.ControlledChanged += controlled => BeginInvoke(() =>
            {
                AppendLog(controlled
                    ? "Remote control active — local keyboard and mouse are captured."
                    : "Remote control ended — local input restored.");
            });
            _receivingController.StateMessage += text => BeginInvoke(() =>
                AppendLog(text));
            _receivingController.ForceSwitchBackRequested += () =>
            {
                // Fires on the keyboard hook thread; sending is thread-safe.
                _ = _client.SendAsync(new SwitchDeviceMessage { ToClient = false });
            };

            AppendLog("Ready — the host can now transfer control to this machine.");
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Connection failed: {ex.Message}");
            SetStatus("● Connection failed", "#EF4444");
            await _client.DisposeAsync();
            _client = null;
            ConnectButton.IsEnabled = true;
        }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;

        _receivingController?.Dispose();
        _receivingController = null;

        await _client.DisconnectAsync();
        await _client.DisposeAsync();
        _client = null;

        DisconnectButton.IsEnabled = false;
        ConnectButton.IsEnabled    = true;
        SetStatus("● Not connected", "#6B6B9A");
        AppendLog("Disconnected from host.");
    }

    // ── Server event handlers ─────────────────────────────────────────────────

    private void OnClientConnected(object? sender, ConnectionEventArgs e)
        => BeginInvoke(() =>
        {
            _clientConnected = true;
            AppendLog($"[+] Client connected: {e.RemoteEndpoint}");
            SetStatus($"● Client connected: {e.RemoteEndpoint}", "#22C55E");

            if (_switchController is null)
                _switchController = CreateSwitchController();
            _switchController.Arrangement = ArrangementBox.SelectedIndex == 1
                ? ScreenArrangement.ClientLeft
                : ScreenArrangement.ClientRight;
            _switchController.Arm(); // auto-arm: edge watching starts immediately
            UpdateSharingButtons();
        });

    private void OnClientDisconnected(object? sender, ConnectionEventArgs e)
        => BeginInvoke(() =>
        {
            _clientConnected = false;
            AppendLog($"[-] Client disconnected: {e.RemoteEndpoint}");
            SetStatus("● Hosting — waiting for connection", "#F59E0B");

            _switchController?.ClientDisconnected(e.RemoteEndpoint);
            UpdateSharingButtons();
            SetForwardingStatus("Waiting for a client…");
        });

    private void OnServerMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        // Switch to the UI thread only for messages we actually care about;
        // high-rate input messages are handled exclusively by the controller.
        switch (e.Message)
        {
            case ScreenInfoMessage screen:
                BeginInvoke(() =>
                {
                    AppendLog($"[SCREEN] {e.RemoteEndpoint} reported {screen.Width}×{screen.Height}");
                    _switchController?.SetClientScreen(e.RemoteEndpoint, screen.Width, screen.Height);
                });
                break;

            case SwitchDeviceMessage:
                _switchController?.HandleMessage(e.Message);
                break;
        }
    }

    // ── Client event handlers ─────────────────────────────────────────────────

    private void OnClientConnected_Client(object? sender, ConnectionEventArgs e)
        => BeginInvoke(() =>
        {
            AppendLog($"[+] Connected to host: {e.RemoteEndpoint}");
            SetStatus($"● Connected to {e.RemoteEndpoint}", "#22C55E");
            ConnectButton.IsEnabled    = false;
            DisconnectButton.IsEnabled = true;
        });

    private void OnClientDisconnected_Client(object? sender, ConnectionEventArgs e)
        => BeginInvoke(() =>
        {
            AppendLog($"[-] Disconnected from host: {e.RemoteEndpoint}");
            SetStatus("● Not connected", "#6B6B9A");
            ConnectButton.IsEnabled    = true;
            DisconnectButton.IsEnabled = false;

            // Connection lost — release any keys/buttons the host had pressed here.
            _receivingController?.Dispose();
            _receivingController = null;
        });

    private void OnClientMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        // Only log notable protocol traffic — never individual input events.
        switch (e.Message.Type)
        {
            case MessageType.SwitchDevice:
                BeginInvoke(() => AppendLog("[SWITCH] Control transfer message received."));
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void BeginInvoke(Action action) => Dispatcher.BeginInvoke(action);

    private void AppendLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogText.Text += $"[{timestamp}] {message}\n";
        LogScroll.ScrollToBottom();
    }

    private void SetStatus(string text, string hexColor)
    {
        StatusText.Text       = text;
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor));
    }

    private void SetForwardingStatus(string text)
        => ForwardingStatusText.Text = text;

    private void ResetModeButtons()
    {
        HostModeButton.IsEnabled   = true;
        ClientModeButton.IsEnabled = true;
    }

    /// <summary>
    /// Returns the first non-loopback IPv4 address of this machine.
    /// Falls back to "127.0.0.1" if none is found.
    /// </summary>
    private static string GetLocalIpAddress()
    {
        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.OperationalStatus != OperationalStatus.Up) continue;
            if (iface.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (var addr in iface.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    return addr.Address.ToString();
            }
        }
        return "127.0.0.1";
    }

    // ── Window closing ────────────────────────────────────────────────────────

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        _switchController?.Dispose();
        _receivingController?.Dispose();
        _switchController     = null;
        _receivingController  = null;

        if (_server is not null) await _server.DisposeAsync();
        if (_client is not null) await _client.DisposeAsync();
    }
}
