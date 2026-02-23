using System.Diagnostics;

namespace naLauncher2.Wpf.Common
{
	public class TimedBlock(string message) : IDisposable
	{
        string _message = message;
        Stopwatch _stopwatch = Stopwatch.StartNew();

        void IDisposable.Dispose()
		{
            _stopwatch.Stop();
			Debug.WriteLine($"{ _message } in {_stopwatch.Elapsed }");

			GC.SuppressFinalize(this);
        }
	}
}
