using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinCAT.Ads;

namespace AdsUtilities
{
    public abstract class AdsClientBase
    {
        public string NetId
        {
            get
            {
                if (_netId is null)
                {
                    return string.Empty;
                }
                return _netId.ToString();
            }
        }

        protected AmsNetId? _netId;
        protected readonly ILoggerFactory? _loggerFactory;
        protected readonly ILogger? _logger;

        protected AdsClientBase(ILoggerFactory? loggerFactory = null)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory?.CreateLogger(GetType());
        }

        protected AdsSession CreateSession(int adsPort)
        {
            if (_netId is null)
            {
                throw new InvalidOperationException("NetId must be set before creating a session.");
            }
            var sessionSettings = SessionSettings.Default;
            AmsAddress amsAddress = new AmsAddress(_netId, adsPort);
            return new AdsSession(amsAddress, sessionSettings, _loggerFactory);
        }

        public async Task<bool> Connect(string netId, CancellationToken cancel = default)
        {
            _netId = new AmsNetId(netId);
            using var session = CreateSession((int)AmsPort.SystemService);
            using var adsConnection = (AdsConnection)session.Connect();
            var readState = await adsConnection.ReadStateAsync(cancel);
            return readState.Succeeded;
        }
    }
}
