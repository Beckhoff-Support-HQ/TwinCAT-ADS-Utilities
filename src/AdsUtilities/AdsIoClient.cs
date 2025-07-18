using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinCAT.Ads;

namespace AdsUtilities;

public class AdsIoClient : AdsClientBase
{
    public AdsIoClient(ILoggerFactory? loggerFactory = null)
        : base(loggerFactory)
    {

    }

    public async Task<IoDevice> GetIoDeviceInfoAsync(uint deviceId, CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.R0Io);
        using var adsConnection = (AdsConnection)session.Connect();

        uint readLen = (
            await adsConnection.ReadAnyAsync<uint>(
                (uint)AdsIndexGroups.IoDeviceStateBase + deviceId,
                (uint)AdsIndexOffsets.DeviceDataDeviceFullInfo,
                cancel
            )
        ).Value;

        ReadRequestHelper readRequest = new((int)readLen);

        await adsConnection.ReadAsync(
            (uint)AdsIndexGroups.IoDeviceStateBase + deviceId,
            (uint)AdsIndexOffsets.DeviceDataDeviceFullInfo,
            readRequest.Bytes,
            cancel);

        adsConnection.Disconnect();

        // Get Master info
        uint dataLen = readRequest.ExtractUint32();
        byte unknown1 = readRequest.ExtractByte();
        readRequest.Skip();
        byte[] unknown2 = readRequest.ExtractBytes(4);
        uint slaveCnt = readRequest.ExtractByte();
        readRequest.Skip();
        string masterNetId = readRequest.ExtractNetId();
        byte[] unknown3 = readRequest.ExtractBytes(2);
        string masterName = readRequest.ExtractStringWithLength();

        // Get slaves info
        List<IoBox> boxes = new();
        while (!readRequest.IsFullyProcessed())
        {
            byte[] unknown4 = readRequest.ExtractBytes(2);
            uint id = readRequest.ExtractByte();
            readRequest.Skip();
            byte[] unknown5 = readRequest.ExtractBytes(2);
            uint port = readRequest.ExtractUint16();
            string netIdMaster = readRequest.ExtractNetId();
            uint unknown6 = readRequest.ExtractUint16();   // In some cases this is the same as port, in some it is null
            string slaveName = readRequest.ExtractStringWithLength();
            IoBox box = new() { name = slaveName, port = port, boxId = id };
            boxes.Add(box);
        }

        IoDevice ecMaster = new() { 
            deviceId = deviceId, 
            netId = masterNetId, 
            deviceName = masterName,
            boxes = boxes, 
            boxCount = slaveCnt };

        return ecMaster;
    }

    public async Task<List<IoDevice>> GetIoDevicesAsync(CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.R0Io);
        using var adsConnection = (AdsConnection)session.Connect();

        ReadRequestHelper readRequest = new(402);

        await adsConnection.ReadAsync(
            (uint)AdsIndexGroups.IoDeviceStateBase,
            (uint)AdsIndexOffsets.DeviceDataDeviceId,
            readRequest.Bytes, 
            cancel);

        adsConnection.Disconnect();

        uint numberOfIoDevices = readRequest.ExtractUint16();
        List<IoDevice> ioDevices = new();

        for (int i = 0; i < numberOfIoDevices; i++)
        {
            uint id = readRequest.ExtractUint16();
            ioDevices.Add(await GetIoDeviceInfoAsync(id, cancel));
        }               

        return ioDevices;
    }

    public T ReadCoeData<T>(
        int ecSlaveAddress, 
        ushort index, 
        ushort subIndex)
    {
        using var session = CreateSession(ecSlaveAddress);
        using var adsConnection = (AdsConnection)session.Connect();

        T value = (T)adsConnection.ReadAny(
            (uint)AdsIndexGroups.Coe, 
            ((uint)index << 16) | subIndex, 
            typeof(T));

        adsConnection.Disconnect();

        return value;
    }

    public void WriteCoeData(
        int ecSlaveAddress, 
        ushort index, 
        ushort subIndex, 
        object value)
    {
        using var session = CreateSession(ecSlaveAddress);
        using var adsConnection = (AdsConnection)session.Connect();

        adsConnection.WriteAny(
            (uint)AdsIndexGroups.Coe, 
            ((uint)index << 16) | subIndex, 
            value);

        adsConnection.Disconnect();
    }
}
