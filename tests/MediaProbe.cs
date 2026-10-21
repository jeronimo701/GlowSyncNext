using System;
using System.Threading.Tasks;
using Windows.Media.Control;

internal static class MediaProbe
{
    public static int Run()
    {
        try
        {
            var request = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
            if (!request.Wait(5000)) throw new TimeoutException("SMTC RequestAsync exceeded 5 seconds");
            Console.WriteLine($"PASS live Windows SMTC: {request.Result.GetSessions().Count} sessions; no media titles logged");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("FAIL live Windows SMTC: " + ex.GetType().Name); return 1; }
    }
}
