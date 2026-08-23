using LanRemote.Protocol;

namespace LanRemote.Core;

public static class RemoteFileBrowser
{
    public const int MaximumEntries = 2_000;

    public static DirectoryBrowseResponse Browse(DirectoryBrowseRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Path))
            {
                RemoteDirectoryEntry[] drives = DriveInfo.GetDrives()
                    .Where(drive => drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable)
                    .Select(drive => new RemoteDirectoryEntry(
                        drive.Name,
                        drive.RootDirectory.FullName,
                        true,
                        0,
                        drive.RootDirectory.LastWriteTimeUtc,
                        true))
                    .ToArray();
                return new DirectoryBrowseResponse(request.RequestId, string.Empty, drives, false, null);
            }

            string directory = FileTransferPolicy.ValidateBrowsableDirectory(request.Path);
            List<RemoteDirectoryEntry> entries = [];
            bool truncated = false;
            foreach (string path in Directory.EnumerateFileSystemEntries(directory)
                         .OrderBy(path => !Directory.Exists(path))
                         .ThenBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            {
                if (entries.Count >= MaximumEntries)
                {
                    truncated = true;
                    break;
                }

                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(path);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                bool isDirectory = attributes.HasFlag(FileAttributes.Directory);
                bool transferable = !attributes.HasFlag(FileAttributes.ReparsePoint) &&
                                    FileTransferPolicy.IsTransferablePath(path);
                FileSystemInfo info = isDirectory ? new DirectoryInfo(path) : new FileInfo(path);
                entries.Add(new RemoteDirectoryEntry(
                    info.Name,
                    info.FullName,
                    isDirectory,
                    isDirectory ? 0 : ((FileInfo)info).Length,
                    info.LastWriteTimeUtc,
                    transferable));
            }

            return new DirectoryBrowseResponse(request.RequestId, directory, entries, truncated, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new DirectoryBrowseResponse(request.RequestId, request.Path ?? string.Empty, [], false, exception.Message);
        }
    }
}
