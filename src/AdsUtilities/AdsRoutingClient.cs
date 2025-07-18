﻿using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using TwinCAT.Ads;
using System.Security;
using System.Runtime.InteropServices;
using System.Threading.Channels;



namespace AdsUtilities;

public class AdsRoutingClient : AdsClientBase
{
    public AdsRoutingClient(ILoggerFactory? loggerFactory = null)
        : base(loggerFactory)
    {

    }

    public async Task AddRouteByIpAsync(
        string netIdTarget,
        string ipAddressTarget, 
        string routeName,
        string usernameTarget, 
        string passwordTarget,
        string remoteRouteName, 
        CancellationToken cancel = default)
    {
        await AddLocalRouteEntryByIpAsync(
            netIdTarget,
            ipAddressTarget,
            routeName,
            cancel);

        await AddRemoteRouteEntryByIpAsync(
            ipAddressTarget,
            usernameTarget,
            passwordTarget,
            remoteRouteName,
            false,
            cancel);
    }

    public async Task AddRouteByIpAsync(
        string netIdTarget, 
        string ipAddressTarget,
        string routeName,
        string usernameTarget, 
        SecureString passwordTarget,
        string remoteRouteName,
        CancellationToken cancel = default)
    {
        await AddLocalRouteEntryByIpAsync(
            netIdTarget,
            ipAddressTarget,
            routeName,
            cancel);

        await AddRemoteRouteEntryByIpAsync(
            ipAddressTarget,
            usernameTarget,
            passwordTarget,
            remoteRouteName,
            false,
            cancel);
    }

    public async Task AddRouteByNameAsync(
        string netIdTarget,
        string hostnameTarget,
        string routeName, 
        string usernameTarget,
        string passwordTarget, 
        string remoteRouteName, 
        CancellationToken cancel = default)
    {
        await AddLocalRouteEntryByNameAsync(
            netIdTarget,
            hostnameTarget,
            routeName,
            cancel);

        await AddRemoteRouteEntryByNameAsync(
            hostnameTarget,
            usernameTarget,
            passwordTarget,
            remoteRouteName,
            false,
            cancel);
    }

    public async Task AddRouteByNameAsync(
        string netIdTarget, 
        string hostnameTarget,
        string routeName,
        string usernameTarget,
        SecureString passwordTarget,
        string remoteRouteName, 
        CancellationToken cancel = default)
    {
        await AddLocalRouteEntryByNameAsync(
            netIdTarget,
            hostnameTarget,
            routeName,
            cancel);

        await AddRemoteRouteEntryByNameAsync(
            hostnameTarget,
            usernameTarget,
            passwordTarget,
            remoteRouteName,
            false,
            cancel);
    }

    public async Task RemoveLocalRouteEntryAsync(string routeName, CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        WriteRequestHelper deleteRouteReq = new WriteRequestHelper()
            .AddStringUTF8(routeName);

        var wRes = await adsConnection.WriteAsync(
            (uint)AdsIndexGroups.SysServDelRemote, 
            0, 
            deleteRouteReq.GetBytes(), 
            cancel);

        adsConnection.Disconnect();

        //wRes.ThrowOnError();  // There seems to be a timeout even if the command succeeds
    }

    public async Task AddLocalRouteEntryByIpAsync(
        string netIdEntry,
        string ipAddressEntry,
        string routeNameEntry,
        CancellationToken cancel = default, 
        bool temporary = false)
    {
        WriteRequestHelper addRouteRequest = new WriteRequestHelper()
            .Add(netIdEntry.Split('.').Select(byte.Parse).ToArray())
            .Add(temporary ? Segments.ROUTETYPE_TEMP_LOCAL : Segments.ROUTETYPE_STATIC_LOCAL)
            .Add(new byte[23])
            .Add((byte)(ipAddressEntry.Length + 1))
            .Add(new byte[3])
            .Add((byte)(routeNameEntry.Length + 1))
            .Add(new byte[7])
            .AddStringUTF8(ipAddressEntry)
            .AddStringUTF8(routeNameEntry);

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var wRes = await adsConnection.WriteAsync(
            (uint)AdsIndexGroups.SysServAddRemote,
            0,
            addRouteRequest.GetBytes(),
            cancel);

        adsConnection.Disconnect();

        wRes.ThrowOnError();
    }

    public async Task AddLocalRouteEntryByNameAsync(
        string netIdEntry, 
        string hostnameEntry, 
        string routeNameEntry, 
        CancellationToken cancel = default,
        bool temporary = false)
    {
        WriteRequestHelper addRouteRequest = new WriteRequestHelper()
            .Add(netIdEntry.Split('.').Select(byte.Parse).ToArray())
            .Add(temporary ? Segments.ROUTETYPE_TEMP_LOCAL : Segments.ROUTETYPE_STATIC_LOCAL)
            .Add(new byte[23])
            .Add((byte)(hostnameEntry.Length + 1))
            .Add(new byte[3])
            .Add((byte)(routeNameEntry.Length + 1))
            .Add(new byte[7])
            .AddStringUTF8(hostnameEntry)
            .AddStringUTF8(routeNameEntry);

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var wRes = await adsConnection.WriteAsync(
            (uint)AdsIndexGroups.SysServAddRemote,
            0,
            addRouteRequest.GetBytes(),
            cancel);

        adsConnection.Disconnect();

        wRes.ThrowOnError();
    }

    public async Task AddRemoteRouteEntryByIpAsync(
        string ipAddressRemote,
        string usernameRemote,
        string passwordRemote,
        string remoteRouteName,
        bool temporary = false, 
        CancellationToken cancel = default)
    {
        await AddRemoteRouteEntryInternalAsync(
            ipAddressRemote,
            usernameRemote,
            passwordRemote,
            remoteRouteName,
            cancel,
            null,
            temporary);
    }

    public async Task AddRemoteRouteEntryByNameAsync(
        string hostnameRemote, 
        string usernameRemote,
        string passwordRemote, 
        string remoteRouteName, 
        bool temporary = false, 
        CancellationToken cancel = default)
    {
        string? ipAddressRemote = await GetIpFromHostname(hostnameRemote, cancel);

        if (ipAddressRemote is null)
            return;

        await AddRemoteRouteEntryInternalAsync(
            ipAddressRemote,
            usernameRemote, 
            passwordRemote,
            remoteRouteName, 
            cancel, 
            hostnameRemote, 
            temporary);
    }


    public async Task AddRemoteRouteEntryByIpAsync(
        string ipAddressRemote, 
        string usernameRemote, 
        SecureString passwordRemote,
        string remoteRouteName,
        bool temporary = false, 
        CancellationToken cancel = default)
    {
        IntPtr passwordBinStrPtr = IntPtr.Zero;
        try
        {
            passwordBinStrPtr = Marshal.SecureStringToBSTR(passwordRemote);
            string plainPassword = Marshal.PtrToStringBSTR(passwordBinStrPtr);

            await AddRemoteRouteEntryInternalAsync(
                ipAddressRemote,
                usernameRemote,
                plainPassword,
                remoteRouteName,
                cancel,
                null,
                temporary);
        }
        finally
        {
            if (passwordBinStrPtr != IntPtr.Zero)
            {
                Marshal.ZeroFreeBSTR(passwordBinStrPtr);
            }
        }
    }

    public async Task AddRemoteRouteEntryByNameAsync(
        string hostnameRemote,
        string usernameRemote, 
        SecureString passwordRemote, 
        string remoteRouteName,
        bool temporary = false, 
        CancellationToken cancel = default)
    {
        string? ipAddressRemote = await GetIpFromHostname(hostnameRemote, cancel);

        if (ipAddressRemote is null)
            return;

        IntPtr passwordBinStrPtr = IntPtr.Zero;
        try
        {
            passwordBinStrPtr = Marshal.SecureStringToBSTR(passwordRemote);
            string plainPassword = Marshal.PtrToStringBSTR(passwordBinStrPtr);

			await AddRemoteRouteEntryInternalAsync(
                ipAddressRemote, 
                usernameRemote, 
                plainPassword, 
                remoteRouteName, 
                cancel, 
                null, 
                temporary);
        }
        finally
        {
            if (passwordBinStrPtr != IntPtr.Zero)
            {
                Marshal.ZeroFreeBSTR(passwordBinStrPtr);
            }
        }
    }

    private async Task AddRemoteRouteEntryInternalAsync(
        string ipAddressRemote, 
        string usernameRemote, 
        string passwordRemote, 
        string remoteRouteName, 
        CancellationToken cancel, 
        string? hostNameRemote = null, 
        bool temporary = false)
    {
        if (!IPAddress.TryParse(ipAddressRemote, out IPAddress? ipBytes))
        {
            _logger?.LogError("Could not add a route entry on remote system because the provided IP address is invalid");
            return;
        }

        byte[] Segment_ROUTENAME_LENGTH = Segments.ROUTENAME_L;
        Segment_ROUTENAME_LENGTH[2] = (byte)(remoteRouteName.Length + 1);
        byte[] Segment_USERNAME_LENGTH = Segments.USERNAME_L;
        Segment_USERNAME_LENGTH[2] = (byte)(usernameRemote.Length + 1);
        byte[] Segment_PASSWORD_LENGTH = Segments.PASSWORD_L;
        Segment_PASSWORD_LENGTH[2] = (byte)(passwordRemote.Length + 1);

        WriteRequestHelper addRouteRequest = new WriteRequestHelper()
            .Add(Segments.IPADDRESS_L)
            .Add(ipBytes.GetAddressBytes())
            .Add(new byte[8])
            .Add(Segments.HEADER)
            .Add(new byte[4])
            .Add(Segments.REQUEST_ADDROUTE)
            .Add(_netId.ToBytes())
            .Add(Segments.PORT)
            .Add(temporary ? Segments.ROUTETYPE_TEMP : Segments.ROUTETYPE_STATIC)
            .Add(Segment_ROUTENAME_LENGTH)
            .AddStringUTF8(remoteRouteName)
            .Add(Segments.AMSNETID_L)
            .Add(_netId.ToBytes())
            .Add(Segment_USERNAME_LENGTH)
            .AddStringUTF8(usernameRemote)
            .Add(Segment_PASSWORD_LENGTH)
            .AddStringUTF8(passwordRemote);

        List<NetworkInterfaceInfo> nicsInfo = await GetNetworkInterfacesAsync(cancel);
        bool foundNwAdapterInRange = false;
        bool rwSuccessAny = false;

        if (hostNameRemote is not null) // Add route by hostname or ip address
        {
            string localHostName = Environment.MachineName;
            byte[] Segment_HOSTNAME_LENGTH = Segments.LOCALHOST_L;
            Segment_HOSTNAME_LENGTH[2] = (byte)(localHostName.Length + 1);
            addRouteRequest.Add(Segment_HOSTNAME_LENGTH);
            addRouteRequest.AddStringUTF8(localHostName);

            byte[] rdBfr = new byte[2048];

            using var session = CreateSession((int)AdsPorts.SystemService);
            using var adsConnection = (AdsConnection)session.Connect();

            var rwResult = await adsConnection.ReadWriteAsync(
                (uint)AdsIndexGroups.SysServBroadcast, 
                0, 
                rdBfr, 
                addRouteRequest.GetBytes(), 
                cancel);

            adsConnection.Disconnect();

            if (rwResult.ErrorCode == AdsErrorCode.NoError)
                rwSuccessAny = true;
        }
        else
        {
            foreach (var nic in nicsInfo)
            {
                if (!IsIpAddressInRange(nic.IpAddress, nic.SubnetMask))             // look for a NIC with an IP that's in the same address range as the remote system and use this IP for the remote route entry
                    continue;

                byte[] Segment_IPADDRESS_LENGTH = Segments.LOCALHOST_L;
                Segment_IPADDRESS_LENGTH[2] = (byte)(nic.IpAddress.Length + 1);
                addRouteRequest.Add(Segment_IPADDRESS_LENGTH);
                addRouteRequest.AddStringUTF8(nic.IpAddress);

                byte[] rdBfr = new byte[2048];

                using var session = CreateSession((int)AdsPorts.SystemService);
                using var adsConnection = (AdsConnection)session.Connect();

                var rwResult = await adsConnection.ReadWriteAsync(
                    (uint)AdsIndexGroups.SysServBroadcast, 
                    0, 
                    rdBfr, 
                    addRouteRequest.GetBytes(), 
                    cancel);

                adsConnection.Disconnect();

                if (rwResult.ErrorCode == AdsErrorCode.NoError)
                    rwSuccessAny = true;

                foundNwAdapterInRange = true;
                break;
            }
        }

        if (!foundNwAdapterInRange && hostNameRemote is not null)
            _logger?.LogError("No network adapter on the local system matched address range of the provided remote IP address");
        if (!rwSuccessAny)
            _logger?.LogError("ADS call to add remote route entry failed");
    }

    public async Task<string?> GetIpFromHostname(string hostname, CancellationToken cancel = default)
    {
        WriteRequestHelper getIpRequest = new WriteRequestHelper()
            .AddStringUTF8(hostname);

        byte[] ipAddressBuffer = new byte[4];

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var rwResult = await adsConnection.ReadWriteAsync(
            (uint)AdsIndexGroups.SysServIpHelperApi, 
            (uint)AdsIndexOffsets.SysServIpHelperIpFromHostname, 
            ipAddressBuffer, 
            getIpRequest.GetBytes(), 
            cancel);

        adsConnection.Disconnect();

        return ipAddressBuffer.All(b => b == 0) ? null : new IPAddress(ipAddressBuffer).ToString();
    }

    private static bool IsIpAddressInRange(string ipAddressStr, string subnetMaskStr)
    {
        IPAddress ipAddress = IPAddress.Parse(ipAddressStr);
        IPAddress subnetMask = IPAddress.Parse(subnetMaskStr);

        IPAddress networkAddress = GetNetworkAddress(ipAddress, subnetMask);
        IPAddress broadcastAddress = GetBroadcastAddress(networkAddress, subnetMask);

        long ipAddrNumeric = IPAddressToLong(ipAddress);
        long networkAddrNumeric = IPAddressToLong(networkAddress);
        long broadcastAddrNumeric = IPAddressToLong(broadcastAddress);

        return ipAddrNumeric >= networkAddrNumeric && ipAddrNumeric <= broadcastAddrNumeric;
    }

    private static IPAddress GetNetworkAddress(IPAddress address, IPAddress subnetMask)
    {
        byte[] ipAdressBytes = address.GetAddressBytes();
        byte[] subnetMaskBytes = subnetMask.GetAddressBytes();
        byte[] networkAddressBytes = new byte[ipAdressBytes.Length];

        for (int i = 0; i < networkAddressBytes.Length; i++)
        {
            networkAddressBytes[i] = (byte)(ipAdressBytes[i] & subnetMaskBytes[i]);
        }

        return new IPAddress(networkAddressBytes);
    }

    private static IPAddress GetBroadcastAddress(IPAddress networkAddress, IPAddress subnetMask)
    {
        byte[] networkAddressBytes = networkAddress.GetAddressBytes();
        byte[] subnetMaskBytes = subnetMask.GetAddressBytes();
        byte[] broadcastAddressBytes = new byte[networkAddressBytes.Length];

        for (int i = 0; i < broadcastAddressBytes.Length; i++)
        {
            broadcastAddressBytes[i] = (byte)(networkAddressBytes[i] | ~subnetMaskBytes[i]);
        }

        return new IPAddress(broadcastAddressBytes);
    }

    private static long IPAddressToLong(IPAddress ip)
    {
        byte[] bytes = ip.GetAddressBytes();
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
        return BitConverter.ToUInt32(bytes, 0);
    }

    //public async Task AddSubRouteAsync(
    //    string netIdGateway,
    //    string netIdSubRoute,
    //    string nameSubRoute, 
    //    CancellationToken cancel = default)
    //{
    //    using AdsRoutingClient routesReader = new();

    //    await routesReader.Connect(netIdGateway);

    //    var routesGateway = await routesReader.GetRoutesListAsync(cancel);          // Check if there is a route between gateway and sub-route-system - mandatory for sub-route to work

    //    if (!routesGateway.Where(r => r.NetId == netIdSubRoute).Any())
    //    {
    //        _logger?.LogCritical("Sub-Route to {netIdSub} with gateway {netIdGate} could not be added because there is no existing route entry to the sub-route on the gateway",
    //            netIdSubRoute, netIdGateway);

    //        return;
    //    }

    //    AdsDirectory staticRoutesDir = AdsDirectory.TargetDir;
    //    string staticRoutesPath = "StaticRoutes.xml";

    //    using AdsFileClient routesEditor = new();


    //    await routesEditor.Connect(_netId.ToString());

    //    byte[] staticRoutesBuffer = new byte[20_000];

    //    await routesEditor.FileReadFullAsync(
    //        staticRoutesBuffer,
    //        staticRoutesPath,
    //        staticRoutesDir,
    //        false,
    //        cancel);

    //    string staticRoutesString = Encoding.UTF8.GetString(staticRoutesBuffer.Where(c => c is not 0).ToArray());
    //    XDocument routesXml = XDocument.Parse(staticRoutesString);

    //    var gatewayEntry = routesXml.Descendants("Route")
    //                         .FirstOrDefault(route => route.Element("NetId")?.Value == netIdGateway);

    //    if (gatewayEntry != null)
    //    {
    //        XElement subRoute = new("SubRoute",
    //                            new XElement("Name", nameSubRoute),
    //                            new XElement("NetId", netIdSubRoute));

    //        gatewayEntry.Add(subRoute);

    //        await routesEditor.FileWriteFullAsync(
    //            Encoding.UTF8.GetBytes(routesXml.ToString()),
    //            staticRoutesPath,
    //            staticRoutesDir,               
    //            false, 
    //            cancel);

    //        _logger?.LogInformation("Sub-Route to {netIdSub} with gateway {netIdGate} was successfully added.", 
    //            netIdSubRoute, netIdGateway);
    //    }
    //    else
    //    {
    //        _logger?.LogError("Parsing error with the StaticRoutesXml of {netIdLocal}", _netId);
    //    }
    //}

    //public async Task AddAdsMqttRouteAsync(
    //    string brokerAddress,
    //    uint brokerPort, 
    //    string topic, 
    //    bool unidirectional = false,
    //    uint qualityOfService = default, 
    //    string user = default, 
    //    string password = default, 
    //    CancellationToken cancel = default)
    //{
    //    AdsDirectory staticRoutesDir = AdsDirectory.TargetDir;
    //    string staticRoutesPath = "StaticRoutes.xml";

    //    using AdsFileClient routesEditor = new();
    //    await routesEditor.Connect(_netId.ToString());

    //    byte[] staticRoutesBuffer = new byte[20_000];

    //    await routesEditor.FileReadFullAsync(
    //        staticRoutesBuffer,
    //        staticRoutesPath,
    //        staticRoutesDir,
    //        false,
    //        cancel);

    //    string staticRoutesString = Encoding.UTF8.GetString(staticRoutesBuffer.Where(c => c is not 0).ToArray());
    //    XDocument routesXml = XDocument.Parse(staticRoutesString);

    //    var connectionsEntry = routesXml.Element("RemoteConnections");

    //    if (connectionsEntry is null)
    //        return;

    //    XElement mqttRoute = new("Mqtt",
    //    new XElement("Address", brokerAddress, new XAttribute("Port", $"{brokerPort}")));

    //    if (unidirectional)
    //        mqttRoute.Add(new XAttribute("Unidirectional", "true"));
    //    if (!string.IsNullOrEmpty(topic))
    //        mqttRoute.Add(new XElement("Topic", topic));
    //    if (qualityOfService > 0)
    //        mqttRoute.Add(new XElement("QoS", qualityOfService));
    //    if (!string.IsNullOrEmpty(user))
    //        mqttRoute.Add(new XElement("User", user));
    //    if (!string.IsNullOrEmpty(password))
    //        mqttRoute.Add(new XElement("Pwd", password));

    //    connectionsEntry.Add(mqttRoute);

    //    await routesEditor.FileWriteFullAsync(
    //        Encoding.UTF8.GetBytes(routesXml.ToString()), 
    //        staticRoutesPath,
    //        staticRoutesDir,
    //        false, 
    //        cancel);
    //}

    /*public void AddAdsMqttRoute(string brokerAddress, uint brokerPort, AdsMqttTlsParameters tlsParameters, bool unidirectional = false, string topic = default, uint qualityOfService = default, string user = default, string password = default)
    {

    }

    public struct AdsMqttTlsParameters
    {
        public string CertificateAuthority;
        public string ClientCert;
        public string Key;
        public string TlsVersion;
        public string? KeyPassword = default;           
        public List<string>? Cipher = default;
        public string? RevocationList = default;
    }*/

    public async Task<List<StaticRoutesInfo>> GetRoutesListAsync(CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        List<StaticRoutesInfo> routesList = new();

        for (uint i = 0; i < 100; i++)
        {
            ReadRequestHelper routeInfo = new(235);
            var readResult = await adsConnection.ReadAsync((uint)AdsIndexGroups.SysServEnumRemote, i, routeInfo.Bytes, cancel);

            if (readResult.ErrorCode != AdsErrorCode.NoError)
                break;

            string netIdRd = routeInfo.ExtractNetId();
            byte[] unknown = routeInfo.ExtractBytes(38);    // ToDo: Test what these parameters do
            string ip = routeInfo.ExtractString();
            string name = routeInfo.ExtractString();

            StaticRoutesInfo entry = new()
            {
                NetId = netIdRd,
                Name = name,
                IpAddress = ip
            };
            routesList.Add(entry);
        }
        adsConnection.Disconnect();

        return routesList;
    }

    public async Task<string> GetFingerprint(CancellationToken cancel = default)
    {
        byte[] rdBfr = new byte[129];

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        var rRes = await adsConnection.ReadAsync((uint)AdsIndexGroups.SysServTcSystemInfo, 9, rdBfr, cancel);

        adsConnection.Disconnect();

        rRes.ThrowOnError();

        return Encoding.UTF8.GetString(rdBfr);
    }

    public async Task<List<NetworkInterfaceInfo>> GetNetworkInterfacesAsync(CancellationToken cancel = default)
    {
        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        byte[] readBfr = new byte[4];

        var rRes = await adsConnection.ReadAsync(
            (uint)AdsIndexGroups.SysServIpHelperApi,
            (uint)AdsIndexOffsets.SysServIpHelperAdapterInfo, 
            readBfr, 
            cancel);

        rRes.ThrowOnError();
        
        uint nicBfrSize = BitConverter.ToUInt32(readBfr, 0);
        byte[] nicBfr = new byte[nicBfrSize];

        rRes = await adsConnection.ReadAsync(
            (uint)AdsIndexGroups.SysServIpHelperApi,
            (uint)AdsIndexOffsets.SysServIpHelperAdapterInfo,
            nicBfr, 
            cancel);

        adsConnection.Disconnect();

        rRes.ThrowOnError();

        uint bytesPerNic = BitConverter.ToUInt32(nicBfr, 0); //640;                        // Info on every NIC takes 640 bytes. There might be a data field in the byte array that contains that size. For now it's statically defined
        uint numOfNics = nicBfrSize / bytesPerNic;

        ConcurrentBag<NetworkInterfaceInfo> nicBag = new();

        string nicStr = nicBfr.ToString();

        await Task.Run(() =>
        {
            Parallel.ForEach(Partitioner.Create(0, (int)numOfNics), range =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    NetworkInterfaceInfo newNic = new()
                    {
                        Guid = Encoding.UTF8
                            .GetString(nicBfr[(Index)(i * bytesPerNic + 8)..(Index)(i * bytesPerNic + 267)])
                            .Replace("\0", string.Empty),

                        Name = Encoding.UTF8
                            .GetString(nicBfr[(Index)(i * bytesPerNic + 268)..(Index)(i * bytesPerNic + 380)])
                            .Replace("\0", string.Empty),

                        IpAddress = Encoding.UTF8
                            .GetString(nicBfr[(Index)(i * bytesPerNic + 432)..(Index)(i * bytesPerNic + 447)])
                            .Replace("\0", string.Empty),

                        SubnetMask = Encoding.UTF8
                            .GetString(nicBfr[(Index)(i * bytesPerNic + 448)..(Index)(i * bytesPerNic + 463)])
                            .Replace("\0", string.Empty),

                        DefaultGateway = Encoding.UTF8
                            .GetString(nicBfr[(Index)(i * bytesPerNic + 472)..(Index)(i * bytesPerNic + 487)])
                            .Replace("\0", string.Empty),

                        DhcpServer = Encoding.UTF8
                            .GetString(nicBfr[(Index)(i * bytesPerNic + 512)..(Index)(i * bytesPerNic + 527)])
                            .Replace("\0", string.Empty)
                    };

                    nicBag.Add(newNic);
                }
            });
        }, cancel);
        return nicBag.ToList();
    }

    public async IAsyncEnumerable<TargetInfo> AdsSearchByIpAsync(
        string ipAddress,
        ushort secondsTimeout = 5,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(ipAddress, out var ip))
            yield break;

        BlockingCollection<TargetInfo> broadcastResults = new();
        TaskCompletionSource completionSource = new();

        void RecievedBroadcastResponse(object sender, AdsNotificationEventArgs e)
        {
            var targetInfo = ParseBroadcastReturn(e.Data.ToArray());
            broadcastResults.Add(targetInfo, cancellationToken);   // Add responses to the thread-safe collection
            completionSource.TrySetResult();    // Signals that there is a new response to the broadcast search
        }

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        // Register a notification and set callback method - the system service generates notification responses for every remote system found on the broadcast
        adsConnection.AdsNotification += RecievedBroadcastResponse;

        NotificationSettings sttngs = new(AdsTransMode.OnChange, 100, 0);       

        var deviceNotiResult = await adsConnection.AddDeviceNotificationAsync(
            (uint)AdsIndexGroups.SysServBroadcast, 
            0, 
            2048, 
            sttngs, 
            null, 
            cancellationToken);

        TriggerBroadcastPacket broadcastPacket = new(ip.GetAddressBytes(), _netId.ToBytes());

        // This tells the system service to send a broadcast telegram on the selected NIC
        var wRes = await adsConnection.WriteAsync(
            (uint)AdsIndexGroups.SysServBroadcast, 
            1, 
            StructConverter.StructureToByteArray(broadcastPacket), 
            cancellationToken);  

        wRes.ThrowOnError();

        var timeout = TimeSpan.FromSeconds(secondsTimeout);
        var startTime = DateTime.UtcNow;

        try
        {
            while (DateTime.UtcNow - startTime < timeout)   // Wait for broadcast responses to arrive
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogInformation("ADS broadcast search was canceled by caller. The list of available TwinCAT systems may be incomplete.");
                    break;
                }

                // Process all received broadcast results safely
                while (broadcastResults.TryTake(out var result, 100, cancellationToken))
                {
                    yield return result;
                }

                // Check for new responses every 100ms and when a new response is signaled
                await Task.WhenAny(completionSource.Task, Task.Delay(100, cancellationToken));
                completionSource = new TaskCompletionSource();  // reset for next response
            }
        }
        finally
        {
            // Mark the collection as complete to exit the foreach loop safely
            broadcastResults.CompleteAdding();

            // Unregister the Event / Handle after timeout has elapsed or the action was canceled 
            await adsConnection.DeleteDeviceNotificationAsync(
                deviceNotiResult.Handle, 
                cancellationToken);

            adsConnection.AdsNotification -= RecievedBroadcastResponse;

            adsConnection.Disconnect();
        }

        // Drain remaining items after marking collection complete
        while (broadcastResults.TryTake(out var result))
        {
            yield return result;
        }
    }

    public async Task<List<TargetInfo>> AdsBroadcastSearchAsync(ushort secondsTimeout = 5, CancellationToken cancellationToken = default)
    {
        List<NetworkInterfaceInfo> nicsInfo = await GetNetworkInterfacesAsync(cancellationToken);

        Task<List<TargetInfo>> taskPerformBroadcast = AdsBroadcastSearchAsync(
            nicsInfo, 
            secondsTimeout, 
            cancellationToken);

        return await taskPerformBroadcast;
    }

    public async Task<List<TargetInfo>> AdsBroadcastSearchAsync(
        List<NetworkInterfaceInfo> interfacesToBroadcastOn, 
        ushort secondsTimeout = 5,
        CancellationToken cancellationToken = default)
    {
        List<TargetInfo> broadcastResults = new();

        void RecievedBroadcastResponse(object sender, AdsNotificationEventArgs e)
        {
            broadcastResults.Add(ParseBroadcastReturn(e.Data.ToArray()));
        }

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        // Register a notification. The results of the broadcast search will be sent as a response on this notification (one notification per found device)
        adsConnection.AdsNotification += RecievedBroadcastResponse;

        NotificationSettings sttngs = new(AdsTransMode.OnChange, 100, 0);

        uint notiHdl = adsConnection.AddDeviceNotification(
            (uint)AdsIndexGroups.SysServBroadcast, 
            0, 
            2048, 
            sttngs, 
            null);

        foreach (NetworkInterfaceInfo nic in interfacesToBroadcastOn)
        {
            if (nic.IpAddress is "0.0.0.0" or null || !IPAddress.TryParse(nic.IpAddress, out _))
            {
                _logger?.LogWarning("The NIC '{nicName}' has no valid IP address.", nic.Name);
                continue;
            }

            IPAddress broadcastAddress = CalculateBroadcastAddress(nic);

            TriggerBroadcastPacket broadcastPacket = new(broadcastAddress.GetAddressBytes(), _netId.ToBytes());
            try
            {
                await adsConnection.WriteAsync(
                    (uint)AdsIndexGroups.SysServBroadcast,
                    1, 
                    StructConverter.StructureToByteArray(broadcastPacket), 
                    cancellationToken);
            }
            catch (AdsErrorException ex)
            {
                _logger?.LogInformation("Could not perform an ADS broadcast search on adapter '{nicName}': '{error}'.", nic.Name, ex.Message);
            }
        }
        try
        {
            await Task.Delay(secondsTimeout * 1000, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            _logger?.LogInformation("ADS broadcast search was canceled by caller.");
        }
        finally
        {
            // Unregister the Event / Handle
            adsConnection.DeleteDeviceNotification(notiHdl);
            adsConnection.AdsNotification -= RecievedBroadcastResponse;
            adsConnection.Disconnect();
        }
        return broadcastResults;
    }

    public async IAsyncEnumerable<TargetInfo> AdsBroadcastSearchStreamAsync(
        List<NetworkInterfaceInfo> interfacesToBroadcastOn,
        ushort secondsTimeout = 5,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        BlockingCollection<TargetInfo> broadcastResults = new();
        TaskCompletionSource completionSource = new();

        void RecievedBroadcastResponse(object sender, AdsNotificationEventArgs e)
        {
            var targetInfo = ParseBroadcastReturn(e.Data.ToArray());
            broadcastResults.Add(targetInfo, cancellationToken);            // Add responses to the thread-safe collection
            completionSource.TrySetResult();                                // Signals that there is a new response to the broadcast search
        }

        using var session = CreateSession((int)AdsPorts.SystemService);
        using var adsConnection = (AdsConnection)session.Connect();

        // Register a notification and set callback method - the system service generates notification responses for every remote system found on the broadcast
        adsConnection.AdsNotification += RecievedBroadcastResponse;

        NotificationSettings sttngs = new(AdsTransMode.OnChange, 100, 0);

        var deviceNotiResult = await adsConnection.AddDeviceNotificationAsync(
            (uint)AdsIndexGroups.SysServBroadcast, 
            0, 
            2048, 
            sttngs, 
            null, 
            cancellationToken);

        foreach (var nic in interfacesToBroadcastOn)
        {
            if (nic.IpAddress is "0.0.0.0" or null || !IPAddress.TryParse(nic.IpAddress, out _))
            {
                _logger?.LogInformation("The NIC '{nicName}' has no valid IP address.", nic.Name);
                continue;
            }

            IPAddress broadcastAddress = CalculateBroadcastAddress(nic);

            TriggerBroadcastPacket broadcastPacket = new(broadcastAddress.GetAddressBytes(), _netId.ToBytes());

            // This tells the system service to send a broadcast telegram on the selected NIC
            var wRes = await adsConnection.WriteAsync(
                (uint)AdsIndexGroups.SysServBroadcast,
                1,
                StructConverter.StructureToByteArray(broadcastPacket),
                cancellationToken);

            if (wRes.Failed)
                _logger?.LogInformation("Could not perform an ADS broadcast search on adapter '{nicName}': '{error}'.", nic.Name, wRes.ErrorCode);
        }

        var timeout = TimeSpan.FromSeconds(secondsTimeout);
        var startTime = DateTime.UtcNow;

        try
        {
            while (DateTime.UtcNow - startTime < timeout)   // Wait for broadcast responses to arrive
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogInformation("ADS broadcast search was canceled by caller.");
                    break;
                }

                // Process all received broadcast results safely
                while (broadcastResults.TryTake(out var result, 100, cancellationToken))
                {
                    yield return result;
                }

                // Check for new responses every 100ms and when a new response is signaled
                await Task.WhenAny(completionSource.Task, Task.Delay(100, cancellationToken));
                completionSource = new TaskCompletionSource();  // reset for next response
            }
        }
        finally
        {
            // Mark the collection as complete to exit the foreach loop safely
            broadcastResults.CompleteAdding();

            // Unregister the Event / Handle after timeout has elapsed or the action was canceled 
            await adsConnection.DeleteDeviceNotificationAsync(
                deviceNotiResult.Handle, 
                cancellationToken);

            adsConnection.AdsNotification -= RecievedBroadcastResponse;

            adsConnection.Disconnect();
        }

        // Drain remaining items after marking collection complete
        while (broadcastResults.TryTake(out var result))
        {
            yield return result;
        }
    }

    public async IAsyncEnumerable<TargetInfo> AdsBroadcastSearchStreamAsync(
        ushort secondsTimeout = 5,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var nicsInfo = await GetNetworkInterfacesAsync(cancellationToken);

        await foreach (var targetInfo in AdsBroadcastSearchStreamAsync(
            nicsInfo, 
            secondsTimeout, 
            cancellationToken))
        {
            yield return targetInfo;
        }
    }

    private static IPAddress CalculateBroadcastAddress(NetworkInterfaceInfo nic)
    {
        IPAddress subnetAddress = IPAddress.Parse(nic.SubnetMask);
        byte[] subnetBytes = subnetAddress.GetAddressBytes();
        byte[] broadcastBytes = new byte[4];

        IPAddress ipAddress = IPAddress.Parse(nic.IpAddress);
        byte[] ipBytes = ipAddress.GetAddressBytes();

        for (int i = 0; i < 4; i++)
        {
            broadcastBytes[i] = (byte)(ipBytes[i] | ~subnetBytes[i]);
        }

        return new IPAddress(broadcastBytes);
    }

    public static TargetInfo ParseBroadcastReturn(byte[] data)
    {
        TargetInfo targetInfo = new()
        {
            IpAddress = string.Empty,
            NetId = string.Empty,
            Name = string.Empty,
            TcVersion = string.Empty,
            Fingerprint = string.Empty,
            //TcType = string.Empty,
            OsVersion = string.Empty
        };

        Dictionary<string, string> TcTypeDictionary = new()// {4,0,148,0,148,0,0,0} for engineering and {4,0,20,1,20,1,00} for runtime
        {
            { "04-00-94-00-94-00-00-00", "Engineering" },
            { "04-00-14-01-14-01-00-00", "Runtime" }
            // Add more mappings as needed
        };

        Dictionary<ushort, string> OsIdDictionary = new()
        {
            {0x0700, "Windows CE (7.0)"},
            {0x0602, "Windows 8/8.1/10"}, //TC2 images and TC3 images up to 4020
            {0x0A00, "Windows"},
            {0x0601, "Windows 7"},
            {0x0600, "Windows CE (6.0)"},
            {0x0500, "Windows CE (5.0)"},
            {0x0501, "Windows XP"},
            {0x0009, "RTOS"}
            //{0x0C02, "TwinCAT/BSD (12.2)"},
            //{0x0D01, "TwinCAT/BSD (13.1)"},
            //{0x0D02, "TwinCAT/BSD (13.2)"}
            // Add more mappings as needed
        };

        // Windows build versions
        Dictionary<ushort, string> OsBuildDictionary = new()
        {
            // all tested with 4022 !
            {0x5D58, "11 (22621) 22H2"},
            {0x654A, "10 (19045) 22H2"},
            {0x644A, "10 (19044) 21H2"},
            {0x634A, "10 (19043) 21H1"},
            {0x624A, "10 (19042) 20H2"},// 4.8.1
            {0x614A, "10 (19041) 2004"},// only up to .NET Framework 4.8
            {0x4447, "10 (18363) 1909"},// only up to .NET Framework 4.8
            {0xBA47, "10 (18362) 1903"},// only up to .NET Framework 4.8
            {0x6345, "10 (17763) 1809"},// only up to .NET Framework 4.8
            {0xEE42, "10 (17134) 1803"},// only up to .NET Framework 4.8
            {0xAB3F, "10 (16299) 1709"},// only up to .NET Framework 4.8
            {0xD73A, "10 (15063) 1703"},// only up to .NET Framework 4.8
            {0x3938, "10 (14393) 1607"},
            {0x5A29, "10 (10586) 1511"},// only up to .NET Framework 4.6.2
            {0x0028, "10 (10240) 1507"} // only up to .NET Framework 4.6.2
            // Add more mappings as needed
        };


        for (int index = 0; index < data.Length;)
        {
            // Check for IP Address Header: 2-0-191-3
            if (MatchHeader(data, index, new byte[] { 2, 0, 191, 3 }))
            {
                index += 4; // Move past header
                if (index + 4 <= data.Length)
                {
                    targetInfo.IpAddress = $"{data[index]}.{data[index + 1]}.{data[index + 2]}.{data[index + 3]}";
                    index += 4; // Move past IP address
                }
            }
            // Check for NetId Header: 1-0-0-128
            else if (MatchHeader(data, index, new byte[] { 1, 0, 0, 128 }))
            {
                index += 4; // Move past header
                if (index + 6 <= data.Length)
                {
                    targetInfo.NetId = string.Join(".", data.Skip(index).Take(6).Select(b => b.ToString()));
                    index += 6; // Move past NetId
                }
            }
            // Check for Name Header: 5-0-x-0, where x is the name's length
            else if (data[index] == 5 && data[index + 1] == 0 && data[index + 2] != 0 && data[index + 3] == 0)
            {
                int nameLength = data[index + 2];
                index += 4; // Move past header
                if (index + nameLength <= data.Length)
                {
                    targetInfo.Name = Encoding.UTF8.GetString(data, index, nameLength - 1); // -1 to skip string termination
                    index += nameLength; // Move past Name
                }

                // Parse TcType (8-byte field after Name)
                if (index + 8 <= data.Length)
                {
                    string tcTypeBytes = BitConverter.ToString(data, index, 8);
                    //targetInfo.TcType = TcTypeDictionary.ContainsKey(tcTypeBytes) ? TcTypeDictionary[tcTypeBytes] : string.Empty;
                    var tcType = TcTypeDictionary.ContainsKey(tcTypeBytes) ? TcTypeDictionary[tcTypeBytes] : string.Empty;
                    index += 8; // Move past TcType
                }

                // Parse OsVersion (12-byte field after TcType)
                if (index + 12 <= data.Length)
                {
                    byte[] osVer = new byte[12];
                    Array.Copy(data, index, osVer, 0, 12);

                    ushort osKey = (ushort)(osVer[0] * 256 + osVer[4]);
                    ushort osBuildKey = (ushort)(osVer[8] * 256 + osVer[9]);
                    string os = OsIdDictionary.ContainsKey(osKey) ? OsIdDictionary[osKey] : osKey.ToString("X2");

                    if (osKey > 0x0C00) //TCBSD has no BuildKey
                        targetInfo.OsVersion = $"TwinCAT/BSD ({osVer[0]}.{osVer[4]})";
                    else if (os.Contains("Windows")) //Windows 10 has BulidKey
                        targetInfo.OsVersion = os + " " + 
                            (OsBuildDictionary.ContainsKey(osBuildKey) ? OsBuildDictionary[osBuildKey] : osBuildKey.ToString("X2"));
                    else if (osKey > 0x0601 && osKey < 0x0700) //Linux
                        targetInfo.OsVersion = $"Linux {osVer[0]}.{osVer[4]}";
                    else if (osKey < 0x0500) //TCRTOS
                        targetInfo.OsVersion = $"TC/RTOS ({osVer[0]}.{osVer[4]})";
                    else
                        targetInfo.OsVersion = os;

                    index += 12; // Move past OsVersion
                }
            }
            // Check for TcVersion Header: 3-0-4-0
            else if (MatchHeader(data, index, new byte[] { 3, 0, 4, 0 }))
            {
                index += 4; // Move past header
                if (index + 4 <= data.Length)
                {
                    targetInfo.TcVersion = $"{data[index]}.{data[index + 1]}.{data[index + 2] + 256 * data[index + 3]}";
                    index += 4; // Move past TcVersion
                }
            }
            // Check for Fingerprint Header: 18-0-65
            else if (MatchHeader(data, index, new byte[] { 18, 0, 65 }))
            {
                index += 3; // Move past header
                if (index + 64 <= data.Length)
                {
                    targetInfo.Fingerprint = BitConverter.ToString(data, index, 64).Replace("-", "").ToLower();
                    index += 64; // Move past Fingerprint
                }
            }
            else
            {
                index++; // Move to the next byte if no matching header is found
            }
        }

        return targetInfo;

        static bool MatchHeader(byte[] data, int index, byte[] header)
        {
            if (index + header.Length > data.Length) return false;
            for (int i = 0; i < header.Length; i++)
            {
                if (data[index + i] != header[i]) return false;
            }
            return true;
        }
    }



    public async Task ChangeNetIdOnWindowsAsync(string netIdNew, bool rebootNow = false, CancellationToken cancel = default)
    {
        string[] partsNetId = netIdNew.Split('.');
        byte[] bytesNetId = new byte[partsNetId.Length];

        for (int i = 0; i < partsNetId.Length; i++)
        {
            if (!byte.TryParse(partsNetId[i], out bytesNetId[i]))
            {
                _logger?.LogError("NetId contains invalid value.");
                return;
            }
        }
        AdsSystemClient systemClient = new();

        await systemClient.Connect(_netId.ToString());

        await systemClient.SetRegEntryAsync(
            @"Software\Beckhoff\TwinCAT3\System", 
            "RequestedAmsNetId", 
            RegEditTypeCode.REG_BINARY, 
            bytesNetId, 
            cancel);

        if (rebootNow)
        {
            await systemClient.RebootAsync(0, cancel);
        }
    }
}
