using AdsUtilities;
using TcEventLoggerAdsProxyLib;
using System.Globalization;

namespace ConsoleApp1
{
    internal class Program
    {
        static async Task Main(string[] args)
        {

            //uint state = await AdsIoClient.GetEcMasterState("5.76.203.150.2.1", default);

            //Console.WriteLine("state: " + state.ToString());


            //(float framesPerSecond,float queuedFramesPerSecond,uint cyclicLostFrames,uint queuedLostFrames) = await AdsIoClient.GetEcFrameStatistics("5.76.203.150.2.1", default);

            //Console.WriteLine($"{framesPerSecond}, {queuedFramesPerSecond}, {cyclicLostFrames}, {queuedLostFrames}");

            //List<uint> slaveCrcs = await AdsIoClient.GetAllSlavesCrc("5.76.203.150.2.1", default);

            //foreach (uint slaveCrc in slaveCrcs)
            //{
            //    Console.WriteLine("CRC: " + slaveCrc);
            //}


            //var logger = new TcEventLogger();

            //logger.MessageSent += (TcMessage message) => Console.WriteLine("Received Message: " + message.GetText(CultureInfo.CurrentCulture.LCID));
            //logger.AlarmRaised += (TcAlarm alarm) => Console.WriteLine("Alarm Raised: " + alarm.GetText(CultureInfo.CurrentCulture.LCID));
            //logger.AlarmCleared += (TcAlarm alarm, bool bRemove) => Console.WriteLine((bRemove ? "Alarm Cleared and was Confirmed: " : "Alarm Cleared: ") + alarm.GetText(CultureInfo.CurrentCulture.LCID));
            //logger.AlarmConfirmed += (TcAlarm alarm, bool bRemove) => Console.WriteLine((bRemove ? "Alarm Confirmed and was Cleared: " : "Alarm Confirmed: ") + alarm.GetText(CultureInfo.CurrentCulture.LCID));

            //logger.Connect(); //connect to localhost

            //Console.Write("Press 'x' or CTRL+C to quit");
            //while (true)
            //{
            //    if (Console.ReadKey(true).Key == ConsoleKey.X) break;
            //}



            AdsSystemClient adsSystemClient = new();

            await adsSystemClient.Connect("172.18.106.7.1.1");

            List<AdsLogEntry> logEntries = [];

            adsSystemClient.RegisterEventListener(logEntries, PrintEventMessage);

            await Task.Delay(100_000);
        }
        static void PrintEventMessage(AdsLogEntry log)
        {
            Console.WriteLine($"{log.TimeRaised}: {log.LogLevel} - {log.Message}");
        }
    }
}
