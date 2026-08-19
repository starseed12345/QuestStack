using System.Diagnostics;
using System.Text;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace QuestStack;

internal enum FastbootResponseStatus
{
    Success,
    Failure,
    Timeout
}

internal readonly record struct FastbootResponse(FastbootResponseStatus Status, string Text)
{
    public bool IsSuccess => Status == FastbootResponseStatus.Success;
}

internal sealed class FastbootUsbDevice : IDisposable
{
    private readonly UsbDeviceFinder _deviceFinder;
    private readonly object _readBufferLock = new();
    private readonly List<byte> _readBuffer = new();

    private UsbDevice? _device;
    private IUsbDevice? _claimedDevice;
    private UsbEndpointReader? _reader;
    private UsbEndpointWriter? _writer;

    public FastbootUsbDevice(int vendorId, int productId)
    {
        _deviceFinder = new UsbDeviceFinder(vendorId, productId);
    }

    public bool TryConnect()
    {
        Close();

        try
        {
            _device = UsbDevice.OpenUsbDevice(_deviceFinder);
            if (_device == null)
                return false;

            ClaimUnixInterfaceIfRequired();

            _reader = _device.OpenEndpointReader(ReadEndpointID.Ep01);
            _writer = _device.OpenEndpointWriter(WriteEndpointID.Ep01);
            _reader.DataReceived += OnDataReceived;
            _reader.DataReceivedEnabled = true;
            return true;
        }
        catch
        {
            Close();
            return false;
        }
    }

    public bool Write(byte[] data, out string error)
    {
        error = string.Empty;
        if (_writer == null)
        {
            error = "Fastboot USB endpoint is not open.";
            return false;
        }

        try
        {
            ErrorCode result = _writer.Write(data, 10_000, out int transferred);
            if (result != ErrorCode.None)
            {
                error = $"USB write failed: {result}.";
                return false;
            }

            if (transferred != data.Length)
            {
                error = $"Short USB write: sent {transferred} of {data.Length} bytes.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"USB write failed: {ex.Message}";
            return false;
        }
    }

    public bool WriteCommand(string command, out string error)
    {
        return Write(Encoding.ASCII.GetBytes(command), out error);
    }

    public FastbootResponse SendCommand(string command, int timeoutMs = 3_000)
    {
        ClearReadBuffer();
        if (!WriteCommand(command, out string error))
            return new FastbootResponse(FastbootResponseStatus.Failure, error);

        return ReadResponse(timeoutMs);
    }

    public FastbootResponse ReadResponse(int timeoutMs)
    {
        var response = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            byte[] chunk = TakeReadBuffer();
            if (chunk.Length > 0)
            {
                response.Append(Encoding.ASCII.GetString(chunk));
                string text = response.ToString();

                if (text.Contains("FAIL", StringComparison.Ordinal))
                    return new FastbootResponse(FastbootResponseStatus.Failure, text.Trim());

                if (text.Contains("OKAY", StringComparison.Ordinal))
                    return new FastbootResponse(FastbootResponseStatus.Success, text.Trim());
            }

            Thread.Sleep(20);
        }

        byte[] remaining = TakeReadBuffer();
        if (remaining.Length > 0)
            response.Append(Encoding.ASCII.GetString(remaining));

        return new FastbootResponse(FastbootResponseStatus.Timeout, response.ToString().Trim());
    }

    public Dictionary<string, string>? GetDeviceInfo()
    {
        FastbootResponse response = SendCommand("oem device-info", 3_000);
        if (!response.IsSuccess)
            return null;

        return ParseDeviceInfoResponse(response.Text);
    }

    internal static Dictionary<string, string>? ParseDeviceInfoResponse(string responseText)
    {
        var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawLine in responseText.Replace("\r", string.Empty).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith("INFO", StringComparison.Ordinal))
                line = line[4..].Trim();

            if (line.StartsWith("(bootloader)", StringComparison.OrdinalIgnoreCase))
                line = line[12..].Trim();

            int separator = line.IndexOf(':');
            if (separator <= 0)
                continue;

            string key = line[..separator].Trim();
            string value = line[(separator + 1)..].Replace("OKAY", string.Empty).Trim();
            if (key.Length > 0 && value.Length > 0)
                info[key] = value;
        }

        return info.Count == 0 ? null : info;
    }

    public void ClearReadBuffer()
    {
        lock (_readBufferLock)
            _readBuffer.Clear();
    }

    public void Close()
    {
        if (_reader != null)
        {
            try
            {
                _reader.DataReceivedEnabled = false;
                _reader.DataReceived -= OnDataReceived;
            }
            catch
            {
            }
        }

        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _claimedDevice?.ReleaseInterface(0); } catch { }
        try { _device?.Close(); } catch { }

        _reader = null;
        _writer = null;
        _claimedDevice = null;
        _device = null;
        ClearReadBuffer();
    }

    public void Dispose()
    {
        Close();
    }

    private void OnDataReceived(object? sender, EndpointDataEventArgs args)
    {
        lock (_readBufferLock)
        {
            for (int index = 0; index < args.Count; index++)
                _readBuffer.Add(args.Buffer[index]);

            _readBuffer.Add((byte)'\n');
        }
    }

    private void ClaimUnixInterfaceIfRequired()
    {
        if (OperatingSystem.IsWindows() || _device is not IUsbDevice unixDevice)
            return;

        if (!unixDevice.GetConfiguration(out byte configuration))
            throw new InvalidOperationException($"Could not read the USB configuration: {UsbDevice.LastErrorString}");

        if (configuration != 1 && !unixDevice.SetConfiguration(1))
            throw new InvalidOperationException($"Could not select USB configuration 1: {UsbDevice.LastErrorString}");

        if (!unixDevice.ClaimInterface(0))
            throw new InvalidOperationException($"Could not claim fastboot USB interface 0: {UsbDevice.LastErrorString}");

        _claimedDevice = unixDevice;
    }

    private byte[] TakeReadBuffer()
    {
        lock (_readBufferLock)
        {
            if (_readBuffer.Count == 0)
                return Array.Empty<byte>();

            byte[] data = _readBuffer.ToArray();
            _readBuffer.Clear();
            return data;
        }
    }
}
