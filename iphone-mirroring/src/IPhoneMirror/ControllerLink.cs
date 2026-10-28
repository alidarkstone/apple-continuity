using System.Collections.Concurrent;
using System.IO;
using System.IO.Ports;
using IPhoneMirror.Core;

namespace IPhoneMirror;

public enum ControllerState { NotFound, Waiting, Paired }

/// <summary>USB serial link to the ESP32 that acts as the iPhone's Bluetooth mouse and keyboard.</summary>
sealed class ControllerLink : IDisposable
{
    readonly BlockingCollection<string> _queue = new();
    readonly CancellationTokenSource _cts = new();
    SerialPort? _port;

    public ControllerState State { get; private set; } = ControllerState.NotFound;
    public string? PortName => _port?.PortName;
    public event Action<ControllerState>? StateChanged;

    public ControllerLink()
    {
        new Thread(WriteLoop) { IsBackground = true, Name = "controller-writer" }.Start();
    }

    /// <summary>Opens the configured port, or tries every COM port until one answers like our firmware.</summary>
    public async Task<bool> ConnectAsync(string preferred)
    {
        var names = SerialPort.GetPortNames().OrderBy(n => n != preferred).ToList();
        foreach (var name in names)
        {
            var port = await Task.Run(() => Probe(name));
            if (port == null) continue;
            _port = port;
            _port.DataReceived += OnData;
            SetState(ControllerState.Waiting);
            Send(Protocol.Query);
            return true;
        }
        SetState(ControllerState.NotFound);
        return false;
    }

    static SerialPort? Probe(string name)
    {
        var port = new SerialPort(name, 115200) { NewLine = "\n", ReadTimeout = 400, DtrEnable = false, RtsEnable = false };
        try
        {
            port.Open();
            var until = DateTime.UtcNow.AddSeconds(2.5);   // some boards reboot when the port opens
            while (DateTime.UtcNow < until)
            {
                port.WriteLine(Protocol.Query);
                try
                {
                    if (Protocol.ParseStatus(port.ReadLine()) != null) return port;
                }
                catch (TimeoutException)
                {
                }
            }
        }
        catch (Exception)
        {
            // Busy or not a serial device we can open.
        }
        port.Dispose();
        return null;
    }

    void OnData(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            while (_port is { IsOpen: true, BytesToRead: > 0 })
            {
                var status = Protocol.ParseStatus(_port.ReadLine());
                if (status != null) SetState(status.Value ? ControllerState.Paired : ControllerState.Waiting);
            }
        }
        catch (Exception)
        {
            // Partial line (TimeoutException) or the port went away; the writer notices the latter.
        }
    }

    public void Send(string line) => _queue.Add(line);

    public void Send(IEnumerable<string> lines)
    {
        foreach (var l in lines) _queue.Add(l);
    }

    void WriteLoop()
    {
        try
        {
            foreach (var line in _queue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    _port?.WriteLine(line);
                }
                catch (Exception) when (_port != null)
                {
                    _port.Dispose();
                    _port = null;
                    SetState(ControllerState.NotFound);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    void SetState(ControllerState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _port?.Dispose();
    }
}
