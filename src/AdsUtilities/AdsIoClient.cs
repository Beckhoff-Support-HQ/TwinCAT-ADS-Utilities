using Microsoft.Extensions.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TwinCAT;
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
            DeviceId = deviceId,
            NetId = masterNetId,
            DeviceName = masterName,
            Boxes = boxes,
            BoxCount = slaveCnt };

        return ecMaster;
    }

    public static async Task<(
        float framesPerSecond,
        float queuedFramesPerSecond,
        uint cyclicLostFrames,
        uint queuedLostFrames)>
        GetEcFrameStatistics(string ecMasterNetId, CancellationToken cancel)
    {
        uint deltaTime;
        uint deltaFrames;

        float framesPerSecond;
        float queuedFramesPerSecond;
        uint cyclicLostFrames;
        uint deltaQueuedFrames;
        uint queuedLostFrames;
        do
        {
            var sessionSettings = SessionSettings.Default;
            AmsAddress amsAddress = new(ecMasterNetId, 0xFFFF);
            using var session = new AdsSession(amsAddress, sessionSettings);
            using var adsConnection = (AdsConnection)session.Connect();

            var readBuffer = new byte[20];
            await adsConnection.ReadAsync(
                0xC,
                0,
                readBuffer,
                cancel);

            uint systemTimeOld = BitConverter.ToUInt32(readBuffer, 0);
            uint cyclicFramesOld = BitConverter.ToUInt32(readBuffer, 4);

            uint queuedFramesOld = BitConverter.ToUInt32(readBuffer, 12);


            await Task.Delay(200);

            await adsConnection.ReadAsync(
                0xC,
                0,
                readBuffer,
                cancel);

            deltaTime = BitConverter.ToUInt32(readBuffer, 0) - systemTimeOld; // in us

            deltaFrames = BitConverter.ToUInt32(readBuffer, 4) - cyclicFramesOld;

            cyclicLostFrames = BitConverter.ToUInt32(readBuffer, 8);

            deltaQueuedFrames = BitConverter.ToUInt32(readBuffer, 12) - queuedFramesOld;
            queuedLostFrames = BitConverter.ToUInt32(readBuffer, 16);

            framesPerSecond = (float)(10000000 * deltaFrames) / deltaTime;
            queuedFramesPerSecond = (float)(10000000 * deltaQueuedFrames) / deltaTime;

        }
        while (deltaFrames >= 0x80000000 || deltaTime >= 0x80000000); // Retry if overflow
        return (framesPerSecond, queuedFramesPerSecond, cyclicLostFrames, queuedLostFrames);
    }

    public static async Task<ushort> GetEcMasterDeviceState(string ecMasterNetId, CancellationToken cancel)
    {
        var sessionSettings = SessionSettings.Default;
        AmsAddress amsAddress = new(ecMasterNetId, 0xFFFF);
        using var session = new AdsSession(amsAddress, sessionSettings);
        using var adsConnection = (AdsConnection)session.Connect();

        var readResult = await adsConnection.ReadAnyAsync<ushort>(0x45, 0, cancel);
        ushort devState = readResult.Value;
        return devState;

        //0x0001 = Link error
        //0x0002 = I / O locked after link error(I/ O reset required)
        //0x0004 = Link error(redundancy adapter)
        //0x0008 = Missing one frame(redundancy mode)
        //0x0010 = Out of send resources(I / O reset required)
        //0x0020 = Watchdog triggered
        //0x0040 = Ethernet driver(miniport) not found
        //0x0080 = I / O reset active
        //0x0100 = At least one device in 'INIT' state
        //0x0200 = At least one device in 'PRE-OP' state
        //0x0400 = At least one device in 'SAFE-OP' state
        //0x0800 = At least one device indicates an error state
        //0x1000 = DC not in sync
    }

    public static async Task<ushort> GetEcMasterState(string ecMasterNetId, CancellationToken cancel)
    {
        var sessionSettings = SessionSettings.Default;
        AmsAddress amsAddress = new(ecMasterNetId, 0xFFFF);
        using var session = new AdsSession(amsAddress, sessionSettings);
        using var adsConnection = (AdsConnection)session.Connect();

        var readResult = await adsConnection.ReadAnyAsync<ushort>(0x3, 0x100, cancel);
        ushort state = readResult.Value;
        return state;

        //0x01 = EC_DEVICE_STATE_INIT    
        //0x02 = EC_DEVICE_STATE_PREOP   
        //0x04 = EC_DEVICE_STATE_SAFEOP  
        //0x08 = EC_DEVICE_STATE_OP  

    }


    public static async Task<List<uint>> GetAllSlavesCrc(string ecMasterNetId, CancellationToken cancel)
    {
        var sessionSettings = SessionSettings.Default;
        AmsAddress amsAddress = new(ecMasterNetId, 0xFFFF);
        using var session = new AdsSession(amsAddress, sessionSettings);
        using var adsConnection = (AdsConnection)session.Connect();

        var readResult = await adsConnection.ReadAnyAsync<ushort>(0x6, 0, cancel);
        ushort slaveCount = readResult.Value;

        var byteBuffer = new byte[slaveCount * sizeof(uint)];

        await adsConnection.ReadAsync(
            0x12,
            0,
            byteBuffer,
            cancel);

        var slaveCrcs = MemoryMarshal.Cast<byte, uint>(byteBuffer.AsSpan()).ToArray();

        return slaveCrcs.ToList();
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
