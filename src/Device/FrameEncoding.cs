using System;
using System.Buffers.Binary;
using GlowSync.Config;

namespace GlowSync.Device;

internal static class FrameEncoding
{
    public static bool IsNetwork(DeviceProtocol protocol) => protocol is DeviceProtocol.WledDdp or DeviceProtocol.Tpm2Net or DeviceProtocol.OpcTcp;
    public static double LimitFps(int requested, DeviceProtocol protocol, int baud, int leds) =>
        IsNetwork(protocol) ? Math.Clamp(requested, 1, 60) :
        Math.Min(Math.Clamp(requested, 1, 60), Math.Max(1, baud / (10.0 * (6 + Math.Max(1, leds) * 3)) * 0.90));

    public static int Build(DeviceProtocol protocol, ChannelOrder order, int leds, ReadOnlySpan<byte> rgb, ref byte[] packet, int opcChannel = 0)
    {
        leds = Math.Clamp(leds, 1, 1000);
        int header = protocol == DeviceProtocol.OpcTcp ? 4 : IsNetwork(protocol) ? 0 : 6;
        int size = header + leds * 3;
        if (packet.Length < size) packet = new byte[size];
        packet.AsSpan(0, size).Clear();
        if (protocol == DeviceProtocol.OpcTcp)
        {
            packet[0] = (byte)Math.Clamp(opcChannel, 0, 255);
            packet[1] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)(leds * 3));
        }
        else if (header != 0)
        {
            "Ada"u8.CopyTo(packet);
            if (protocol == DeviceProtocol.Skydimo)
            {
                packet[3] = 0;
                BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), (ushort)leds);
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(3), (ushort)(leds - 1));
                packet[5] = (byte)(packet[3] ^ packet[4] ^ 0x55);
            }
        }
        string channels = order.ToString();
        for (int i = 0; i < leds && i * 3 + 2 < rgb.Length; i++)
            for (int c = 0; c < 3; c++)
                packet[header + i * 3 + c] = rgb[i * 3 + (channels[c] == 'R' ? 0 : channels[c] == 'G' ? 1 : 2)];
        return size;
    }

    // DDP v1, RGB24, display destination 1, PUSH only on the final packet; byte offset is network-endian.
    public static int DdpChunk(ReadOnlySpan<byte> rgb, int offset, byte sequence, Span<byte> packet)
    {
        int count = Math.Min(1440, rgb.Length - offset);
        packet[0] = (byte)(0x40 | (offset + count == rgb.Length ? 0x01 : 0));
        packet[1] = (byte)(sequence & 0x0f);
        packet[2] = 0x0b;
        packet[3] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(packet[4..], (uint)offset);
        BinaryPrimitives.WriteUInt16BigEndian(packet[8..], (ushort)count);
        rgb.Slice(offset, count).CopyTo(packet[10..]);
        return count + 10;
    }

    public static int TpmChunk(ReadOnlySpan<byte> rgb, int chunk, Span<byte> packet)
    {
        int chunks = Math.Max(1, (rgb.Length + 1439) / 1440);
        // Equal-size packets with zero padding are required by receivers such as WLED.
        int payload = chunks == 1 ? rgb.Length : 1440;
        packet[..(payload + 7)].Clear();
        packet[0] = 0x9c; packet[1] = 0xda;
        BinaryPrimitives.WriteUInt16BigEndian(packet[2..], (ushort)payload);
        packet[4] = (byte)(chunk + 1); packet[5] = (byte)chunks;
        int offset = chunk * 1440;
        rgb.Slice(offset, Math.Min(payload, rgb.Length - offset)).CopyTo(packet[6..]);
        packet[6 + payload] = 0x36;
        return payload + 7;
    }
}
