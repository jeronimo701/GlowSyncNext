using System;
using System.Net;
using System.Net.Sockets;
using GlowSync.Config;

namespace GlowSync.Device;

internal interface IFrameTransport : IDisposable
{
    string Name { get; }
    void Write(ReadOnlySpan<byte> data);
}

internal sealed class SerialFrameTransport : IFrameTransport
{
    private readonly NativeSerialPort _port;
    public string Name => _port.Name;
    public SerialFrameTransport(NativeSerialPort port) => _port = port;
    public void Write(ReadOnlySpan<byte> data) => _port.Write(data);
    public void Dispose() => _port.Dispose();
}

internal sealed class DdpFrameTransport : IFrameTransport
{
    private readonly Socket _socket;
    private readonly byte[] _packet = new byte[1450];
    private byte _sequence;
    private readonly bool _tpm;
    public string Name { get; }
    public DdpFrameTransport(string host, int port, bool tpm = false)
    {
        _tpm = tpm;
        // Explicit IP avoids DNS stalls in the device thread. No discovery or broadcasts.
        var address = IPAddress.Parse(host.Trim());
        _socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp) { SendTimeout = 250 };
        try { _socket.Connect(new IPEndPoint(address, port)); }
        catch { _socket.Dispose(); throw; }
        Name = $"{host}:{port} (UDP; без подтверждения)";
    }
    public void Write(ReadOnlySpan<byte> data)
    {
        _sequence = (byte)(_sequence % 15 + 1);
        for (int offset = 0; offset < data.Length; offset += 1440)
        {
            int length = _tpm ? FrameEncoding.TpmChunk(data, offset / 1440, _packet) : FrameEncoding.DdpChunk(data, offset, _sequence, _packet);
            _socket.Send(_packet.AsSpan(0, length), SocketFlags.None);
        }
    }
    public void Dispose() => _socket.Dispose();
}

internal sealed class OpcFrameTransport : IFrameTransport
{
    private readonly Socket _socket;
    public string Name { get; }
    public OpcFrameTransport(string host, int port)
    {
        var address = IPAddress.Parse(host.Trim());
        _socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
        { NoDelay = true, SendTimeout = 250, SendBufferSize = 4096 };
        try
        {
            if (!_socket.ConnectAsync(new IPEndPoint(address, port)).Wait(700)) throw new TimeoutException("OPC connect timeout");
        }
        catch { _socket.Dispose(); throw; }
        Name = $"{host}:{port} (OPC/TCP)";
    }
    public void Write(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            int sent = _socket.Send(data, SocketFlags.None);
            if (sent == 0) throw new SocketException((int)SocketError.ConnectionReset);
            data = data[sent..];
        }
    }
    public void Dispose() => _socket.Dispose();
}
