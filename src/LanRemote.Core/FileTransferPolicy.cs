using System.Security.Cryptography;
using System.Text;
using LanRemote.Protocol;

namespace LanRemote.Core;

public static class FileTransferPolicy
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public static void ValidateManifest(
        IReadOnlyList<FileTransferEntry> entries,
        long totalBytes,
        string manifestSha256)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count is 0 or > ProtocolConstants.MaxTransferEntries)
        {
            throw new InvalidDataException(
                $"傳輸項目數必須介於 1 與 {ProtocolConstants.MaxTransferEntries}。 ");
        }

        if (totalBytes < 0 || totalBytes > ProtocolConstants.MaxBatchLength)
        {
            throw new InvalidDataException("傳輸批次超過 10 GB 上限。");
        }

        if (manifestSha256.Length != 64 || !manifestSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Manifest SHA-256 格式無效。");
        }

        HashSet<int> indices = [];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        long computedTotal = 0;
        foreach (FileTransferEntry entry in entries)
        {
            if (!indices.Add(entry.Index) || entry.Index < 0)
            {
                throw new InvalidDataException("傳輸項目索引重複或無效。");
            }

            string relativePath = NormalizeRelativePath(entry.RelativePath);
            if (!paths.Add(relativePath))
            {
                throw new InvalidDataException("傳輸項目路徑重複。");
            }

            if (!Enum.IsDefined(entry.Kind))
            {
                throw new InvalidDataException("傳輸項目類型無效。");
            }

            if (entry.Kind == FileTransferEntryKind.Directory)
            {
                if (entry.Length != 0 || !string.IsNullOrEmpty(entry.Sha256))
                {
                    throw new InvalidDataException("資料夾項目不可攜帶長度或雜湊。");
                }

                continue;
            }

            if (entry.Length < 0 || entry.Length > ProtocolConstants.MaxFileLength)
            {
                throw new InvalidDataException("單一檔案超過 2 GB 上限。");
            }

            if (entry.Sha256.Length != 64 || !entry.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException("檔案 SHA-256 格式無效。");
            }

            checked
            {
                computedTotal += entry.Length;
            }
        }

        if (computedTotal != totalBytes)
        {
            throw new InvalidDataException("傳輸批次大小與 manifest 不一致。");
        }

        string expectedFingerprint = ComputeManifestFingerprint(entries);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expectedFingerprint),
                Convert.FromHexString(manifestSha256)))
        {
            throw new InvalidDataException("傳輸 manifest 雜湊不一致。");
        }
    }

    public static string NormalizeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.Contains(':'))
        {
            throw new InvalidDataException("傳輸項目必須使用安全的相對路徑。");
        }

        string[] segments = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new InvalidDataException("傳輸項目路徑不可為空。");
        }

        if (Encoding.UTF8.GetByteCount(value) > 768)
        {
            throw new InvalidDataException("傳輸項目的相對路徑過長。");
        }

        foreach (string segment in segments)
        {
            if (segment is "." or ".." || segment.IndexOfAny(InvalidFileNameChars) >= 0 ||
                segment.EndsWith(' ') || segment.EndsWith('.') || Encoding.UTF8.GetByteCount(segment) > 255)
            {
                throw new InvalidDataException($"傳輸項目包含不安全的路徑片段：{segment}");
            }
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    public static string ValidateExistingDirectory(string path)
    {
        string fullPath = ValidateBrowsableDirectory(path);
        EnsureNotProtectedDestination(fullPath);
        return fullPath;
    }

    public static string ValidateBrowsableDirectory(string path)
    {
        string fullPath = ValidateLocalPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"找不到目的資料夾：{fullPath}");
        }

        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    public static string ValidateSourcePath(string path)
    {
        string fullPath = ValidateLocalPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("找不到傳輸來源。", fullPath);
        }

        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    public static bool IsTransferablePath(string path)
    {
        try
        {
            _ = ValidateSourcePath(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public static string ComputeManifestFingerprint(IReadOnlyList<FileTransferEntry> entries)
    {
        StringBuilder canonical = new();
        foreach (FileTransferEntry entry in entries.OrderBy(item => item.Index))
        {
            canonical.Append(entry.Index).Append('|')
                .Append((int)entry.Kind).Append('|')
                .Append(entry.RelativePath.Replace('\\', '/')).Append('|')
                .Append(entry.Length).Append('|')
                .Append(entry.Sha256.ToUpperInvariant()).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    public static string CombineUnderRoot(string root, string relativePath)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string result = Path.GetFullPath(Path.Combine(normalizedRoot, NormalizeRelativePath(relativePath)));
        string prefix = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("傳輸路徑逸出目的資料夾。");
        }

        return result;
    }

    private static string ValidateLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("\\\\", StringComparison.Ordinal))
        {
            throw new InvalidDataException("MVP 不接受 UNC 或空白路徑。");
        }

        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidDataException("路徑沒有本機磁碟根目錄。");
        }

        if (fullPath.AsSpan(root.Length).Contains(':'))
        {
            throw new InvalidDataException("MVP 不接受 NTFS alternate data stream 路徑。");
        }

        DriveInfo drive = new(root);
        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable))
        {
            throw new InvalidDataException("MVP 只允許本機固定磁碟或卸除式磁碟。");
        }

        return fullPath;
    }

    private static void EnsureNoReparsePoints(string fullPath)
    {
        string? current = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(current) &&
                File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidDataException("MVP 不傳輸 symlink、junction 或其他 reparse point。");
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        if (File.Exists(fullPath) && File.GetAttributes(fullPath).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException("MVP 不傳輸 symlink、junction 或其他 reparse point。");
        }
    }

    private static void EnsureNotProtectedDestination(string fullPath)
    {
        string[] protectedRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        ];

        foreach (string protectedRoot in protectedRoots.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedRoot));
            if (fullPath.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("基於安全考量，檔案傳輸不可寫入 Windows、程式或啟動資料夾。");
            }
        }
    }
}
