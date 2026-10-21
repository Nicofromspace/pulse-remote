using System.IO.Pipes;

namespace Pulse.Remote;

internal static class InstanceSignal
{
    const string Name = "PulseRemote-Show-48261";
    public static void Send()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            pipe.Connect(2000); pipe.WriteByte(1);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        { System.Windows.Forms.MessageBox.Show("Pulse Remote is still starting. Check its tray icon in a moment."); }
    }
    public static async Task Listen(Action show, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation);
                var value = new byte[1];
                if (await pipe.ReadAsync(value, cancellation) == 1 && value[0] == 1) show();
            }
            catch (OperationCanceledException) { return; }
            catch (IOException) { await Task.Delay(250, cancellation); }
        }
    }
}
