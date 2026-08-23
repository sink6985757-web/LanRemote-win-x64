using LanRemote.Core;

namespace LanRemote.Windows;

public sealed class WindowsClipboardFileProvider : IClipboardFileProvider
{
    public async ValueTask<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<IReadOnlyList<string>> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                System.Collections.Specialized.StringCollection files =
                    System.Windows.Forms.Clipboard.GetFileDropList();
                completion.TrySetResult(files.Cast<string>().Take(200).ToArray());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "LanRemote clipboard reader",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
