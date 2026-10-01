using System.Text;
using DockiUp.Application.Dtos;

namespace DockiUp.Infrastructure.Services
{
    /// <summary>File operations inside one project folder. Every path comes from the browser, so each one is
    /// resolved and must stay inside the folder: no "..", no absolute paths, no symlinks pointing out, and
    /// the .git folder is hidden and untouchable (corrupting it would break syncing).</summary>
    public static class ProjectFileSystem
    {
        public const int MaxTextBytes = 1024 * 1024;
        public const int MaxUploadBytes = 25 * 1024 * 1024;

        private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <summary>Absolute path for <paramref name="relative"/> inside <paramref name="root"/>; throws
        /// <see cref="ArgumentException"/> when it would leave the folder or touch .git.</summary>
        public static string Resolve(string root, string? relative)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rel = (relative ?? "").Replace('\\', '/');
            // Path.IsPathRooted differs per OS ("C:foo", "\x"); refuse leading slashes and drive prefixes
            // outright so a path means the same on the server and on any node.
            if (rel.StartsWith('/') || Path.IsPathRooted(rel) || rel.Contains(':'))
                throw new ArgumentException($"'{relative}' must be a path relative to the project folder.");
            rel = rel.TrimEnd('/');
            if (rel.Length == 0) return root;

            var segments = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(s => s is "." or ".."))
                throw new ArgumentException($"'{relative}' must not contain '.' or '..' segments.");
            if (segments.Any(s => s.Equals(".git", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("The .git folder is managed by DockiUp and can't be accessed here.");

            var full = Path.GetFullPath(Path.Combine([root, .. segments]));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException($"'{relative}' is outside the project folder.");

            // A symlink anywhere along the way could point outside; follow each existing one and check.
            var current = root;
            foreach (var segment in segments)
            {
                current = Path.Combine(current, segment);
                FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
                if (!info.Exists || info.LinkTarget is null) continue;
                var target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
                if (target is null || !(target + Path.DirectorySeparatorChar).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new ArgumentException($"'{relative}' is a link to outside the project folder.");
            }
            return full;
        }

        /// <summary>Relative, '/'-separated form of a path under root.</summary>
        private static string Relative(string root, string full) => Path.GetRelativePath(root, full).Replace('\\', '/');

        public static ProjectFileEntryDto[] List(string root, string? relative, IReadOnlySet<string> tracked)
        {
            var dir = Resolve(root, relative);
            if (!Directory.Exists(dir)) throw new KeyNotFoundException($"Folder '{relative}' not found.");
            var fullRoot = Resolve(root, null);
            return new DirectoryInfo(dir).EnumerateFileSystemInfos()
                .Where(i => !i.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                .Select(i =>
                {
                    var path = Relative(fullRoot, i.FullName);
                    return i is DirectoryInfo
                        ? new ProjectFileEntryDto(i.Name, path, true, 0, i.LastWriteTimeUtc, tracked.Any(t => t.StartsWith(path + "/", StringComparison.Ordinal)))
                        : new ProjectFileEntryDto(i.Name, path, false, ((FileInfo)i).Length, i.LastWriteTimeUtc, tracked.Contains(path));
                })
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static async Task<ProjectFileContentDto> ReadTextAsync(string root, string relative, IReadOnlySet<string> tracked, CancellationToken cancellationToken)
        {
            var file = ExistingFile(root, relative);
            if (file.Length > MaxTextBytes)
                throw new ArgumentException($"'{relative}' is too large to edit here ({file.Length / 1024} KB, limit {MaxTextBytes / 1024} KB). Download it instead.");
            var bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
            string content;
            try
            {
                // NUL bytes are valid UTF-8 but mean binary in practice.
                if (Array.IndexOf(bytes, (byte)0) >= 0) throw new DecoderFallbackException();
                content = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                throw new ArgumentException($"'{relative}' is a binary file and can't be edited here. Download it instead.");
            }
            if (content.Length > 0 && content[0] == '\uFEFF') content = content[1..]; // BOM
            var path = Relative(Resolve(root, null), file.FullName);
            return new ProjectFileContentDto(path, content, file.Length, file.LastWriteTimeUtc, tracked.Contains(path));
        }

        public static Task<byte[]> ReadBytesAsync(string root, string relative, CancellationToken cancellationToken)
        {
            var file = ExistingFile(root, relative);
            // Downloads from a node travel as one SignalR message, so they share the upload limit.
            if (file.Length > MaxUploadBytes)
                throw new ArgumentException($"'{relative}' is too large to download here (limit {MaxUploadBytes / 1024 / 1024} MB).");
            return File.ReadAllBytesAsync(file.FullName, cancellationToken);
        }

        /// <summary>Writes the file (creating parent folders); returns its absolute path and the previous
        /// content (null when it is new), so a failed follow-up (git push) can put it back.</summary>
        public static async Task<(string FullPath, byte[]? Previous)> WriteAsync(string root, string relative, byte[] content, CancellationToken cancellationToken)
        {
            if (content.Length > MaxUploadBytes)
                throw new ArgumentException($"The file is too large (limit {MaxUploadBytes / 1024 / 1024} MB).");
            var full = Resolve(root, relative);
            if (full == Resolve(root, null) || Directory.Exists(full))
                throw new ArgumentException($"'{relative}' is a folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var previous = File.Exists(full) ? await File.ReadAllBytesAsync(full, cancellationToken) : null;
            await File.WriteAllBytesAsync(full, content, cancellationToken);
            return (full, previous);
        }

        public static async Task RestoreAsync(string fullPath, byte[]? previous)
        {
            if (previous is null) File.Delete(fullPath);
            else await File.WriteAllBytesAsync(fullPath, previous);
        }

        public static void Delete(string root, string relative)
        {
            var full = Resolve(root, relative);
            if (full == Resolve(root, null)) throw new ArgumentException("The project folder itself can't be deleted here.");
            // A symlink is removed as a link; its target (inside the folder, see Resolve) is left alone.
            var info = new FileInfo(full);
            if (info.LinkTarget is not null || File.Exists(full)) { File.Delete(full); return; }
            if (!Directory.Exists(full)) throw new KeyNotFoundException($"'{relative}' not found.");
            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal); // read-only files block recursive delete on Windows
            Directory.Delete(full, recursive: true);
        }

        public static void CreateFolder(string root, string relative)
        {
            var full = Resolve(root, relative);
            if (File.Exists(full)) throw new ArgumentException($"A file named '{relative}' already exists.");
            Directory.CreateDirectory(full);
        }

        private static FileInfo ExistingFile(string root, string relative)
        {
            var file = new FileInfo(Resolve(root, relative));
            if (!file.Exists) throw new KeyNotFoundException($"File '{relative}' not found.");
            return file;
        }
    }
}
