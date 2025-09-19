using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TwinCAT.Ads;

namespace AdsUtilities
{
    internal class AdsEcMasterClient : AdsClientBase
    {
        public AdsEcMasterClient(ILoggerFactory? loggerFactory = null)
        : base(loggerFactory)
        {

        }

        public async Task<T?> ReadCoeDataAsync<T>(
        int ecSlaveAddress,
        ushort index,
        ushort subIndex,
        CancellationToken cancel = default)
        {
            using var session = CreateSession(ecSlaveAddress);
            using var adsConnection = (AdsConnection)session.Connect();

            ResultAnyValue value = await adsConnection.ReadAnyAsync(
                (uint)AdsIndexGroups.Coe,
                ((uint)index << 16) | subIndex,
                typeof(T),
                cancel);

            adsConnection.Disconnect();

            return value.Value is not null ? (T)value.Value : default;
        }

        public async Task WriteCoeData(
            int ecSlaveAddress,
            ushort index,
            ushort subIndex,
            object value,
            CancellationToken cancel = default)
        {
            using var session = CreateSession(ecSlaveAddress);
            using var adsConnection = (AdsConnection)session.Connect();

            await adsConnection.WriteAnyAsync(
                (uint)AdsIndexGroups.Coe,
                ((uint)index << 16) | subIndex,
                value,
                cancel);

            adsConnection.Disconnect();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct EcPhysicalAccessCommand
        {
            public byte command;
            public byte index;
            public ushort slaveAddress;
            public ushort register;
            public ushort length;
            public ushort irq;
            [MarshalAs(UnmanagedType.ByValArray)]
            public byte[] data;
            public ushort workingCounter;

        }

        public enum EcSlaveAddressType
        {
            fixedAddress,
            autoIncrementAddress
        }

        public async Task<ushort> ReadEscRegister(
            uint ecSlaveAddress,
            EcSlaveAddressType ecSlaveAddressType,
            uint register,
            CancellationToken cancel = default)
        {
            using var session = CreateSession(0xFFFF);
            using var adsConnection = (AdsConnection)session.Connect();

            const byte len = 2;

            EcPhysicalAccessCommand command = new()
            {
                command = (ecSlaveAddressType == EcSlaveAddressType.autoIncrementAddress) ? (byte)1 : (byte)4,  // autoInc: 1; fixed: 4
                index = 0,
                slaveAddress = (ushort)ecSlaveAddress,
                register = (ushort)register,
                length = len,
                data = new byte[len]
            };
            command.data[0] = len + 2;


            byte[] buffer = StructConverter.StructureToByteArray(command);

            await adsConnection.ReadWriteAsync(
                8,
                0,
                buffer,
                buffer,
                cancel);

            ushort value = BitConverter.ToUInt16(buffer, 10);

            adsConnection.Disconnect();

            return value;
        }

        public async Task WriteEscRegister(
            uint ecSlaveAddress,
            EcSlaveAddressType ecSlaveAddressType,
            uint register,
            byte[] value,
            CancellationToken cancel)
        {
            byte len = (byte)value.Length;

            EcPhysicalAccessCommand command = new()
            {
                command = (ecSlaveAddressType == EcSlaveAddressType.autoIncrementAddress) ? (byte)1 : (byte)4,  // autoInc: 1; fixed: 4
                index = 0,
                slaveAddress = (ushort)ecSlaveAddress,
                register = (ushort)register,
                length = len,
                data = new byte[len + 2]
            };
            Array.Copy(value, 0, command.data, 0, value.Length);

            using var session = CreateSession(0xFFFF);
            using var adsConnection = (AdsConnection)session.Connect();

            byte[] buffer = StructConverter.StructureToByteArray(command);

            await adsConnection.ReadWriteAsync(
                8,
                0,
                buffer,
                buffer,
                cancel);


            adsConnection.Disconnect();
        }

        public  async Task<(
        float framesPerSecond,
        float queuedFramesPerSecond,
        uint cyclicLostFrames,
        uint queuedLostFrames)>
        GetEcFrameStatistics(CancellationToken cancel)
        {
            uint deltaTime;
            uint deltaFrames;

            float framesPerSecond = default;
            float queuedFramesPerSecond = default;
            uint cyclicLostFrames = default;
            uint deltaQueuedFrames = default;
            uint queuedLostFrames = default;
            do
            {
                if (cancel.IsCancellationRequested) { break; }

                using var session = CreateSession(0xFFFF);
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


                await Task.Delay(200, cancel);

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

                adsConnection.Disconnect();

            }
            while (deltaFrames >= 0x80000000 || deltaTime >= 0x80000000); // Retry if overflow
            return (framesPerSecond, queuedFramesPerSecond, cyclicLostFrames, queuedLostFrames);
        }

        public async Task<ushort> GetEcMasterDeviceState(CancellationToken cancel)
        {
            using var session = CreateSession(0xFFFF);
            using var adsConnection = (AdsConnection)session.Connect();

            var readResult = await adsConnection.ReadAnyAsync<ushort>(0x45, 0, cancel);
            ushort devState = readResult.Value;

            adsConnection.Disconnect();
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

        public async Task<ushort> GetEcMasterState(CancellationToken cancel)
        {
            using var session = CreateSession(0xFFFF);
            using var adsConnection = (AdsConnection)session.Connect();

            var readResult = await adsConnection.ReadAnyAsync<ushort>(0x3, 0x100, cancel);
            ushort state = readResult.Value;

            adsConnection.Disconnect();
            return state;

            //0x01 = EC_DEVICE_STATE_INIT    
            //0x02 = EC_DEVICE_STATE_PREOP   
            //0x04 = EC_DEVICE_STATE_SAFEOP  
            //0x08 = EC_DEVICE_STATE_OP  

        }


        public async Task<List<uint>> GetAllSlavesCrc(CancellationToken cancel)
        {
            using var session = CreateSession(0xFFFF);
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
    }

}
