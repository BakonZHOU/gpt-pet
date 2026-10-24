using System.IO.Pipes;

namespace CodexPetPlugin;

internal static class ControlPipe
{
    private static string Name => PluginPaths.PipeName;
    public static bool Send(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            pipe.Connect(1500);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.WriteLine(command); return true;
        }
        catch (TimeoutException) { return false; }
        catch (IOException) { return false; }
    }
    public static async Task ListenAsync(PetWindow window, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop).ConfigureAwait(false);
                using var reader = new StreamReader(pipe);
                string? command = await reader.ReadLineAsync(stop).ConfigureAwait(false);
                if (command is "show" or "standalone" or "hide" or "refresh" or "quit")
                    await window.Dispatcher.InvokeAsync(() =>
                    {
                        if (command == "quit") window.Close();
                        else if (command == "hide") window.HidePet();
                        else if (command == "refresh") window.RefreshNow();
                        else window.ShowPet();
                    });
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { }
        }
    }
}
