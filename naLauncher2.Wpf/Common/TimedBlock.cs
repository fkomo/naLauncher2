using System.Diagnostics;

namespace naLauncher2.Wpf.Common
{
	public class TimedBlock(string message) : IDisposable
	{
        readonly string _message = message;
        readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        void IDisposable.Dispose()
		{
            _stopwatch.Stop();

            if (_stopwatch.Elapsed.TotalSeconds < 1)
                Log.WriteLine($"{_message}: {(int)_stopwatch.Elapsed.TotalMilliseconds}ms");
            else if (_stopwatch.Elapsed.TotalMinutes < 1)
				Log.WriteLine($"{_message}: {_stopwatch.Elapsed.TotalSeconds:F3}s");
			else
                Log.WriteLine($"{_message}: {_stopwatch.Elapsed}");

            GC.SuppressFinalize(this);
        }
	}
}
